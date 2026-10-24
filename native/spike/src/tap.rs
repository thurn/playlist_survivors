//! The Core Audio process tap: all system output except our own process,
//! through a private aggregate device, into a lock-free ring.

use std::cell::UnsafeCell;
use std::ffi::c_void;
use std::ptr::NonNull;

use objc2::AllocAnyThread;
use objc2::rc::Retained;
use objc2::runtime::AnyObject;
use objc2_core_audio::*;
use objc2_core_audio_types::*;
use objc2_core_foundation::CFDictionary;
use objc2_foundation::{NSArray, NSDictionary, NSMutableDictionary, NSNumber, NSString};

use crate::coreaudio::{self, Id, check};

/// One captured buffer's timing, pushed beside its samples.
#[derive(Clone, Copy, Debug, Default)]
pub struct Meta {
    /// Index (at the tap's rate) of the buffer's first frame.
    pub first: u64,
    pub frames: u32,
    /// `inInputTime.mHostTime`: when the first frame was captured.
    pub input_host: u64,
    /// `inNow.mHostTime`: when the IOProc ran.
    pub now_host: u64,
    /// Peak absolute sample, to tell silence (or a missing grant) from audio.
    pub peak: f32,
    /// Frames dropped so far because the ring was full.
    pub dropped: u64,
}

struct Ctx {
    samples: rtrb::Producer<f32>,
    meta: rtrb::Producer<Meta>,
    channels: usize,
    interleaved: bool,
    received: u64,
    dropped: u64,
}

/// An input IOProc on one device, downmixing to mono into a ring.
pub struct Capture {
    pub device: Id,
    proc_id: AudioDeviceIOProcID,
    _ctx: Box<UnsafeCell<Ctx>>,
    pub rate: f64,
    pub channels: usize,
    pub samples: rtrb::Consumer<f32>,
    pub meta: rtrb::Consumer<Meta>,
}

pub struct Tap {
    pub tap: Id,
    pub aggregate: Id,
    pub capture: Capture,
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
        Capture::start_with(device, fmt, 4.0)
    }

    /// As `start`, with a ring of `ring_s` seconds (long enough to ride out a stalled reader).
    pub fn start_with(
        device: Id,
        fmt: AudioStreamBasicDescription,
        ring_s: f64,
    ) -> Result<Capture, String> {
        let channels = fmt.mChannelsPerFrame as usize;
        let interleaved = fmt.mFormatFlags & kAudioFormatFlagIsNonInterleaved == 0;
        if fmt.mFormatID != kAudioFormatLinearPCM
            || fmt.mFormatFlags & kAudioFormatFlagIsFloat == 0
            || fmt.mBitsPerChannel != 32
        {
            return Err(format!("unexpected input format {fmt:?}"));
        }
        let (sp, sc) = rtrb::RingBuffer::new((fmt.mSampleRate * ring_s) as usize);
        let (mp, mc) = rtrb::RingBuffer::new(4096);
        let ctx = Box::new(UnsafeCell::new(Ctx {
            samples: sp,
            meta: mp,
            channels,
            interleaved,
            received: 0,
            dropped: 0,
        }));
        let mut proc_id: AudioDeviceIOProcID = None;
        check(
            unsafe {
                AudioDeviceCreateIOProcID(
                    device,
                    Some(io_proc),
                    &*ctx as *const UnsafeCell<Ctx> as *mut c_void,
                    NonNull::from(&mut proc_id),
                )
            },
            "AudioDeviceCreateIOProcID",
        )?;
        check(
            unsafe { AudioDeviceStart(device, proc_id) },
            "AudioDeviceStart",
        )?;
        Ok(Capture {
            device,
            proc_id,
            _ctx: ctx,
            rate: fmt.mSampleRate,
            channels,
            samples: sc,
            meta: mc,
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
        Capture::start_with(dev, fmt, ring_s)
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
        })
    }
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
        desc.setName(&NSString::from_str("Playlist Survivors spike tap"));
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
    let agg_uid = NSString::from_str(&format!("ps-spike-aggregate-{}-{n}", std::process::id()));
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
        Retained::into_super(Retained::into_super(NSString::from_str("PS spike tap"))),
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

impl Drop for Tap {
    fn drop(&mut self) {
        unsafe {
            AudioDeviceStop(self.aggregate, self.capture.proc_id);
            AudioDeviceDestroyIOProcID(self.aggregate, self.capture.proc_id);
            self.capture.proc_id = None;
            AudioHardwareDestroyAggregateDevice(self.aggregate);
            AudioHardwareDestroyProcessTap(self.tap);
        }
    }
}
