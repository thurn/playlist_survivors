//! The Core Audio process taps, each through a private aggregate device into a lock-free ring:
//! all system output except our own process (Direct listening), or one app's processes, muted
//! while tapped, its stereo also into Delayed listening's delay line (docs/design/native-app.md,
//! "Capture and mute").

use std::cell::UnsafeCell;
use std::ffi::c_void;
use std::ptr::NonNull;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};

use audio::delay::{DelayLine, Writer};

use objc2::AllocAnyThread;
use objc2::rc::Retained;
use objc2::runtime::AnyObject;
use objc2_core_audio::*;
use objc2_core_audio_types::*;
use objc2_core_foundation::CFDictionary;
use objc2_foundation::{NSArray, NSDictionary, NSMutableDictionary, NSNumber, NSString};

use crate::Meta;
use crate::coreaudio::{self, Id, check};
use crate::watchdog::Watchdog;

struct Ctx {
    samples: rtrb::Producer<f32>,
    meta: rtrb::Producer<Meta>,
    channels: usize,
    interleaved: bool,
    received: u64,
    dropped: u64,
    /// The delay line's writer (Delayed listening), the host clock's seconds a tick, and the
    /// host time of the latest buffer (an f64's bits, for the watchdog).
    delay: Option<Writer>,
    tick_s: f64,
    alive: Arc<AtomicU64>,
}

/// An input IOProc on one device, downmixing to mono into a ring.
pub struct Capture {
    pub device: Id,
    proc_id: AudioDeviceIOProcID,
    ctx: Box<UnsafeCell<Ctx>>,
    pub rate: f64,
    pub channels: usize,
    pub samples: rtrb::Consumer<f32>,
    pub meta: rtrb::Consumer<Meta>,
    /// The host time of the latest buffer captured (an f64's bits; 0 before the first).
    pub alive: Arc<AtomicU64>,
}

pub struct Tap {
    pub tap: Id,
    pub aggregate: Id,
    pub capture: Capture,
    /// Set once the tap and its aggregate are destroyed (here, or by the watchdog).
    pub destroyed: Arc<AtomicBool>,
}

impl std::ops::Deref for Tap {
    type Target = Capture;
    fn deref(&self) -> &Capture {
        &self.capture
    }
}

impl std::ops::DerefMut for Tap {
    fn deref_mut(&mut self) -> &mut Capture {
        &mut self.capture
    }
}

impl Capture {
    pub fn start(device: Id, fmt: AudioStreamBasicDescription) -> Result<Capture, String> {
        Capture::start_with(device, fmt, 4.0, None)
    }

    /// As `start`, with a ring of `ring_s` seconds (long enough to ride out a stalled reader), and
    /// the stereo frames also into `delay` when given.
    pub fn start_with(
        device: Id,
        fmt: AudioStreamBasicDescription,
        ring_s: f64,
        delay: Option<Writer>,
    ) -> Result<Capture, String> {
        Capture::start_at(device, fmt, ring_s, delay, 0).map_err(|(e, _)| e)
    }

    /// As `start_with`, numbering its frames from `first` (a capture that continues another's
    /// timeline); on failure the delay line's writer comes back with the error.
    fn start_at(
        device: Id,
        fmt: AudioStreamBasicDescription,
        ring_s: f64,
        delay: Option<Writer>,
        first: u64,
    ) -> Result<Capture, (String, Option<Writer>)> {
        let channels = fmt.mChannelsPerFrame as usize;
        let interleaved = fmt.mFormatFlags & kAudioFormatFlagIsNonInterleaved == 0;
        if fmt.mFormatID != kAudioFormatLinearPCM
            || fmt.mFormatFlags & kAudioFormatFlagIsFloat == 0
            || fmt.mBitsPerChannel != 32
        {
            return Err((format!("unexpected input format {fmt:?}"), delay));
        }
        let (sp, sc) = rtrb::RingBuffer::new((fmt.mSampleRate * ring_s) as usize);
        let (mp, mc) = rtrb::RingBuffer::new(4096);
        let mut ctx = Box::new(UnsafeCell::new(Ctx {
            samples: sp,
            meta: mp,
            channels,
            interleaved,
            received: first,
            dropped: 0,
            delay,
            tick_s: crate::clock::tick_s(),
            alive: Arc::new(AtomicU64::new(0)),
        }));
        let alive = Arc::clone(&ctx.get_mut().alive);
        let mut proc_id: AudioDeviceIOProcID = None;
        let created = check(
            unsafe {
                AudioDeviceCreateIOProcID(
                    device,
                    Some(io_proc),
                    &*ctx as *const UnsafeCell<Ctx> as *mut c_void,
                    NonNull::from(&mut proc_id),
                )
            },
            "AudioDeviceCreateIOProcID",
        );
        if let Err(e) = created {
            return Err((e, ctx.get_mut().delay.take()));
        }
        if let Err(e) = check(
            unsafe { AudioDeviceStart(device, proc_id) },
            "AudioDeviceStart",
        ) {
            unsafe { AudioDeviceDestroyIOProcID(device, proc_id) };
            return Err((e, ctx.get_mut().delay.take()));
        }
        Ok(Capture {
            device,
            proc_id,
            ctx,
            rate: fmt.mSampleRate,
            channels,
            samples: sc,
            meta: mc,
            alive,
        })
    }

    /// The default input device (the built-in microphone), for the loopback test.
    pub fn microphone() -> Result<Capture, String> {
        Capture::microphone_with(4.0)
    }

    pub fn microphone_with(ring_s: f64) -> Result<Capture, String> {
        let dev = coreaudio::default_input();
        let streams: Vec<Id> = coreaudio::get_vec(
            dev,
            kAudioDevicePropertyStreams,
            kAudioObjectPropertyScopeInput,
        )?;
        let fmt: AudioStreamBasicDescription = coreaudio::get(
            streams[0],
            kAudioStreamPropertyVirtualFormat,
            kAudioObjectPropertyScopeGlobal,
        )?;
        Capture::start_with(dev, fmt, ring_s, None)
    }

    /// Stops the IOProc, leaving the device (and any tap it reads) in place.
    pub fn pause(&self) -> Result<(), String> {
        check(
            unsafe { AudioDeviceStop(self.device, self.proc_id) },
            "AudioDeviceStop",
        )
    }

    pub fn resume(&self) -> Result<(), String> {
        check(
            unsafe { AudioDeviceStart(self.device, self.proc_id) },
            "AudioDeviceStart",
        )
    }

    /// Drain everything captured so far.
    pub fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>) {
        while let Ok(m) = self.meta.pop() {
            metas.push(m);
        }
        let n = self.samples.slots();
        if n > 0 {
            let c = self.samples.read_chunk(n).unwrap();
            let (a, b) = c.as_slices();
            samples.extend_from_slice(a);
            samples.extend_from_slice(b);
            c.commit_all();
        }
    }
}

impl Drop for Capture {
    fn drop(&mut self) {
        if self.proc_id.is_none() {
            return;
        }
        unsafe {
            AudioDeviceStop(self.device, self.proc_id);
            AudioDeviceDestroyIOProcID(self.device, self.proc_id);
        }
    }
}

unsafe extern "C-unwind" fn io_proc(
    _device: AudioObjectID,
    now: NonNull<AudioTimeStamp>,
    input: NonNull<AudioBufferList>,
    input_time: NonNull<AudioTimeStamp>,
    _output: NonNull<AudioBufferList>,
    _output_time: NonNull<AudioTimeStamp>,
    client: *mut c_void,
) -> i32 {
    // Real time: no allocation, locks or logging.
    let ctx = unsafe { &mut *(client as *mut UnsafeCell<Ctx>) }.get_mut();
    let list = unsafe { input.as_ref() };
    let nbuf = list.mNumberBuffers as usize;
    if nbuf == 0 {
        return 0;
    }
    let bufs = unsafe { std::slice::from_raw_parts(list.mBuffers.as_ptr(), nbuf) };
    let ch = ctx.channels;
    let frames = if ctx.interleaved {
        bufs[0].mDataByteSize as usize / 4 / ch.max(1)
    } else {
        bufs[0].mDataByteSize as usize / 4
    };
    let mut peak = 0.0f32;
    let first = ctx.received;
    let gain = 1.0 / ch as f32;
    if let Ok(mut chunk) = ctx.samples.write_chunk_uninit(frames) {
        let (a, b) = chunk.as_mut_slices();
        let mut i = 0;
        for slot in a.iter_mut().chain(b.iter_mut()) {
            let mut s = 0.0f32;
            if ctx.interleaved {
                let data = bufs[0].mData as *const f32;
                for c in 0..ch {
                    s += unsafe { *data.add(i * ch + c) };
                }
            } else {
                for buf in bufs.iter().take(ch) {
                    s += unsafe { *(buf.mData as *const f32).add(i) };
                }
            }
            let s = s * gain;
            peak = peak.max(s.abs());
            slot.write(s);
            i += 1;
        }
        unsafe { chunk.commit_all() };
        // (Delayed listening: the stereo frames too, the first two channels, by capture time.)
        let host = unsafe { input_time.as_ref() }.mHostTime as f64 * ctx.tick_s;
        if let Some(w) = ctx.delay.as_mut() {
            let interleaved = ctx.interleaved;
            w.write(first, frames, host, |a, b| {
                let channel = |i: usize, c: usize| {
                    let c = c.min(ch - 1);
                    if interleaved {
                        unsafe { *(bufs[0].mData as *const f32).add(i * ch + c) }
                    } else {
                        unsafe { *(bufs[c].mData as *const f32).add(i) }
                    }
                };
                for (k, slot) in a.iter_mut().chain(b.iter_mut()).enumerate() {
                    *slot = channel(k / 2, k % 2);
                }
            });
        }
        ctx.alive.store(host.to_bits(), Ordering::Relaxed);
        ctx.received += frames as u64;
        let _ = ctx.meta.push(Meta {
            first,
            frames: frames as u32,
            input_host: unsafe { input_time.as_ref() }.mHostTime,
            dropped: ctx.dropped,
            now_host: unsafe { now.as_ref() }.mHostTime,
            peak,
        });
    } else {
        // Keep the sample index on the device timeline across a drop.
        ctx.dropped += frames as u64;
        ctx.received += frames as u64;
    }
    0
}

fn ns(s: &std::ffi::CStr) -> Retained<NSString> {
    NSString::from_str(s.to_str().unwrap())
}

/// Our own process's Core Audio object.
pub fn own_process_object() -> Result<Id, String> {
    let pid: libc::pid_t = std::process::id() as libc::pid_t;
    coreaudio::get_q(
        coreaudio::SYSTEM,
        kAudioHardwarePropertyTranslatePIDToProcessObject,
        kAudioObjectPropertyScopeGlobal,
        Some(&pid),
    )
}

impl Tap {
    /// Tap everything but our own process; `exclude_self = false` taps us too (for the negative check).
    pub fn start(exclude_self: bool) -> Result<Tap, String> {
        let me = own_process_object()?;
        let excluded: Retained<NSArray<NSNumber>> = if exclude_self && me != 0 {
            NSArray::from_retained_slice(&[NSNumber::new_u32(me)])
        } else {
            NSArray::new()
        };
        let desc = unsafe {
            CATapDescription::initStereoGlobalTapButExcludeProcesses(
                CATapDescription::alloc(),
                &excluded,
            )
        };
        let (tap, aggregate, fmt) = aggregate_for(&desc)?;
        let capture = Capture::start(aggregate, fmt)?;
        Ok(Tap {
            tap,
            aggregate,
            capture,
            destroyed: Arc::new(AtomicBool::new(false)),
        })
    }

    /// Tap the process objects `processes` (one app's), muted while tapped (macOS unmutes them
    /// when the tap is destroyed, or when this process exits): their mono mixdown to the
    /// analyzer, their stereo into `delay`.
    /// The delay line plays each frame `delay` s after its capture, holding `keep` s.
    pub fn start_processes(
        processes: &[Id],
        delay: f64,
        keep: f64,
    ) -> Result<(Tap, DelayLine), String> {
        let (tap, aggregate, fmt) = muted_aggregate(processes)?;
        let (writer, line) = audio::delay::delay_line(fmt.mSampleRate, delay, keep);
        Tap::start_aggregate(tap, aggregate, fmt, writer, 0)
            .map(|t| (t, line))
            .map_err(|(e, _)| e)
    }

    /// Tap `processes` muted, as [`Tap::start_processes`], into an existing line's `writer` at
    /// `rate`, numbering the frames from `first`: a tap that continues an earlier one's timeline.
    /// On failure (or a tap at another rate) the writer comes back with the error;
    /// [`Tap::into_writer`] hands it back once the tap is done.
    pub fn start_processes_into(
        processes: &[Id],
        writer: Writer,
        rate: f64,
        first: u64,
    ) -> Result<Tap, (String, Writer)> {
        let (tap, aggregate, fmt) = match muted_aggregate(processes) {
            Ok(t) => t,
            Err(e) => return Err((e, writer)),
        };
        if fmt.mSampleRate != rate {
            destroy(tap, aggregate, &AtomicBool::new(false));
            return Err((
                format!(
                    "the tap is at {} Hz, its line at {rate} Hz",
                    fmt.mSampleRate
                ),
                writer,
            ));
        }
        Tap::start_aggregate(tap, aggregate, fmt, writer, first)
    }

    /// Starts capturing a muted tap's aggregate into `writer`, or destroys them.
    fn start_aggregate(
        tap: Id,
        aggregate: Id,
        fmt: AudioStreamBasicDescription,
        writer: Writer,
        first: u64,
    ) -> Result<Tap, (String, Writer)> {
        let destroyed = Arc::new(AtomicBool::new(false));
        match Capture::start_at(aggregate, fmt, 4.0, Some(writer), first) {
            Ok(capture) => Ok(Tap {
                tap,
                aggregate,
                capture,
                destroyed,
            }),
            Err((e, writer)) => {
                destroy(tap, aggregate, &destroyed);
                Err((e, writer.expect("the writer comes back")))
            }
        }
    }

    /// Destroys the tap (its app is heard again) and hands back the delay line's writer it
    /// captured into, if any.
    pub fn into_writer(mut self) -> Option<Writer> {
        self.stop();
        // (The IOProc is gone, so its context is ours again.)
        self.capture.ctx.get_mut().delay.take()
    }

    /// Stops the capture and destroys the tap and its aggregate, once.
    fn stop(&mut self) {
        // (Once the watchdog destroyed the aggregate, its IOProc went with it.)
        if !self.destroyed.load(Ordering::SeqCst) && self.capture.proc_id.is_some() {
            unsafe {
                AudioDeviceStop(self.aggregate, self.capture.proc_id);
                AudioDeviceDestroyIOProcID(self.aggregate, self.capture.proc_id);
            }
        }
        self.capture.proc_id = None;
        destroy(self.tap, self.aggregate, &self.destroyed);
    }

    /// A watchdog over this tap, destroying it when its capture or the main thread stalls.
    pub fn watchdog(&self) -> Watchdog {
        let (tap, aggregate, destroyed) = (self.tap, self.aggregate, Arc::clone(&self.destroyed));
        Watchdog::start(
            Arc::clone(&self.destroyed),
            Arc::clone(&self.alive),
            move || destroy(tap, aggregate, &destroyed),
        )
    }
}

/// A tap of `processes`, muted while tapped, and its aggregate: (tap, aggregate, format).
fn muted_aggregate(processes: &[Id]) -> Result<(Id, Id, AudioStreamBasicDescription), String> {
    if processes.is_empty() {
        return Err("no process to tap".into());
    }
    let ids: Vec<Retained<NSNumber>> = processes.iter().map(|&p| NSNumber::new_u32(p)).collect();
    let list = NSArray::from_retained_slice(&ids);
    let desc =
        unsafe { CATapDescription::initStereoMixdownOfProcesses(CATapDescription::alloc(), &list) };
    unsafe { desc.setMuteBehavior(CATapMuteBehavior::MutedWhenTapped) };
    aggregate_for(&desc)
}

/// Destroys a tap and its aggregate, once (the app's own end or the watchdog's): the tapped app
/// is heard again. Safe from any thread.
pub fn destroy(tap: Id, aggregate: Id, destroyed: &AtomicBool) {
    if destroyed.swap(true, Ordering::SeqCst) {
        return;
    }
    unsafe {
        AudioHardwareDestroyAggregateDevice(aggregate);
        AudioHardwareDestroyProcessTap(tap);
    }
}

/// The name our taps carry, and the prefix of our aggregates' UIDs.
const TAP_NAME: &str = "Playlist Survivors tap";
const AGGREGATE_UID: &str = "ps-aggregate-";

/// Destroys any public tap or aggregate of ours left registered by a process that was killed
/// (private ones go with their process: ES, hv-t8o8.36). Returns how many it destroyed.
pub fn destroy_leftovers() -> usize {
    let mut n = 0;
    let taps: Vec<Id> = coreaudio::get_vec(
        coreaudio::SYSTEM,
        kAudioHardwarePropertyTapList,
        kAudioObjectPropertyScopeGlobal,
    )
    .unwrap_or_default();
    for t in taps {
        let name =
            coreaudio::get_string(t, kAudioObjectPropertyName, kAudioObjectPropertyScopeGlobal);
        if name.is_ok_and(|s| s.to_string() == TAP_NAME) {
            unsafe { AudioHardwareDestroyProcessTap(t) };
            n += 1;
        }
    }
    for d in coreaudio::devices() {
        let uid = coreaudio::get_string(
            d.id,
            kAudioDevicePropertyDeviceUID,
            kAudioObjectPropertyScopeGlobal,
        );
        if uid.is_ok_and(|s| s.to_string().starts_with(AGGREGATE_UID)) {
            unsafe { AudioHardwareDestroyAggregateDevice(d.id) };
            n += 1;
        }
    }
    n
}

/// `PS_PUBLIC=1` makes taps and aggregates visible to other processes (so a parent can see a
/// killed child's go away).
fn public() -> bool {
    std::env::var("PS_PUBLIC").as_deref() == Ok("1")
}

/// Creates the tap a description describes and a private aggregate device on the default
/// output that reads it: (tap, aggregate, the tap's format).
pub fn aggregate_for(
    desc: &CATapDescription,
) -> Result<(Id, Id, AudioStreamBasicDescription), String> {
    unsafe {
        desc.setName(&NSString::from_str(TAP_NAME));
        desc.setPrivate(!public());
    }
    let mut tap: Id = 0;
    check(
        unsafe { AudioHardwareCreateProcessTap(Some(desc), &mut tap) },
        "AudioHardwareCreateProcessTap",
    )?;
    let tap_uid = unsafe { desc.UUID().UUIDString() };
    let out_uid = coreaudio::get_string(
        coreaudio::default_output(),
        kAudioDevicePropertyDeviceUID,
        kAudioObjectPropertyScopeGlobal,
    )?;

    // (Unique per aggregate: one process may hold several.)
    static NEXT: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);
    let n = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    let agg_uid = NSString::from_str(&format!("{AGGREGATE_UID}{}-{n}", std::process::id()));
    let sub: Retained<NSDictionary<NSString, AnyObject>> = NSDictionary::from_retained_objects(
        &[&*ns(kAudioSubDeviceUIDKey)],
        &[Retained::into_super(Retained::into_super(out_uid.clone()))],
    );
    let tap_entry: Retained<NSDictionary<NSString, AnyObject>> =
        NSDictionary::from_retained_objects(
            &[
                &*ns(kAudioSubTapUIDKey),
                &*ns(kAudioSubTapDriftCompensationKey),
            ],
            &[
                Retained::into_super(Retained::into_super(tap_uid)),
                Retained::into_super(Retained::into_super(Retained::into_super(
                    NSNumber::new_bool(true),
                ))),
            ],
        );
    let d: Retained<NSMutableDictionary<NSString, AnyObject>> = NSMutableDictionary::new();
    let put = |k: &std::ffi::CStr, v: Retained<AnyObject>| unsafe {
        d.setObject_forKey(&v, objc2::runtime::ProtocolObject::from_ref(&*ns(k)));
    };
    put(
        kAudioAggregateDeviceNameKey,
        Retained::into_super(Retained::into_super(NSString::from_str(TAP_NAME))),
    );
    put(
        kAudioAggregateDeviceUIDKey,
        Retained::into_super(Retained::into_super(agg_uid)),
    );
    put(
        kAudioAggregateDeviceMainSubDeviceKey,
        Retained::into_super(Retained::into_super(out_uid)),
    );
    put(
        kAudioAggregateDeviceIsPrivateKey,
        Retained::into_super(Retained::into_super(Retained::into_super(
            NSNumber::new_bool(!public()),
        ))),
    );
    put(
        kAudioAggregateDeviceIsStackedKey,
        Retained::into_super(Retained::into_super(Retained::into_super(
            NSNumber::new_bool(false),
        ))),
    );
    put(
        kAudioAggregateDeviceTapAutoStartKey,
        Retained::into_super(Retained::into_super(Retained::into_super(
            NSNumber::new_bool(true),
        ))),
    );
    put(
        kAudioAggregateDeviceSubDeviceListKey,
        Retained::into_super(Retained::into_super(NSArray::from_retained_slice(&[sub]))),
    );
    put(
        kAudioAggregateDeviceTapListKey,
        Retained::into_super(Retained::into_super(NSArray::from_retained_slice(&[
            tap_entry,
        ]))),
    );

    let mut aggregate: Id = 0;
    let cf: &CFDictionary = unsafe { &*(Retained::as_ptr(&d) as *const CFDictionary) };
    check(
        unsafe { AudioHardwareCreateAggregateDevice(cf, NonNull::from(&mut aggregate)) },
        "AudioHardwareCreateAggregateDevice",
    )?;

    let fmt: AudioStreamBasicDescription = coreaudio::get(
        tap,
        kAudioTapPropertyFormat,
        kAudioObjectPropertyScopeGlobal,
    )?;
    Ok((tap, aggregate, fmt))
}

impl crate::analysis::Source for Tap {
    fn rate(&self) -> f64 {
        self.capture.rate
    }

    fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>) {
        self.capture.drain(samples, metas);
    }
}

impl Drop for Tap {
    fn drop(&mut self) {
        self.stop();
    }
}
