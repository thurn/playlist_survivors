//! The Core Audio process taps, each through a private aggregate device into a [`Sink`]: all
//! system output except our own process (Direct listening), or one app's processes, muted while
//! tapped, its stereo also into Delayed listening's delay line (docs/archive/design/native-app.md,
//! "Capture and mute"). A live tap's IOProc, aggregate and tap are torn down once, by the app or
//! its watchdog ([`Teardown`]).

use std::cell::UnsafeCell;
use std::ffi::c_void;
use std::ptr::NonNull;
use std::sync::Arc;

use audio::delay::{DelayLine, Writer};

use objc2::AllocAnyThread;
use objc2::rc::Retained;
use objc2::runtime::AnyObject;
use objc2_core_audio::*;
use objc2_core_audio_types::*;
use objc2_core_foundation::CFDictionary;
use objc2_foundation::{NSArray, NSDictionary, NSMutableDictionary, NSNumber, NSString};

use crate::Meta;
use crate::clock::HostTime;
use crate::coreaudio::{self, Id, check};
use crate::sink::{Sink, Stream, sink};
use crate::teardown::Teardown;
use crate::watchdog::Watchdog;

/// The IOProc's context: its sink, and how the device's buffers hold their channels.
struct Ctx {
    sink: Sink,
    channels: usize,
    interleaved: bool,
}

/// An IOProc registered on a device and started; stopped and destroyed when dropped.
struct IoProc {
    device: Id,
    id: AudioDeviceIOProcID,
}

impl Drop for IoProc {
    fn drop(&mut self) {
        unsafe {
            AudioDeviceStop(self.device, self.id);
            AudioDeviceDestroyIOProcID(self.device, self.id);
        }
    }
}

/// A process tap, destroyed when dropped: the tapped app is heard again.
struct ProcessTap(Id);

impl Drop for ProcessTap {
    fn drop(&mut self) {
        unsafe { AudioHardwareDestroyProcessTap(self.0) };
    }
}

/// An aggregate device of ours, destroyed when dropped.
struct Aggregate(Id);

impl Drop for Aggregate {
    fn drop(&mut self) {
        unsafe { AudioHardwareDestroyAggregateDevice(self.0) };
    }
}

/// A tap and the private aggregate that reads it, dropped in field order.
struct Tapped {
    aggregate: Aggregate,
    tap: ProcessTap,
}

/// What a live tap holds, dropped in field order under its [`Teardown`]. The IOProc goes first:
/// the aggregate's destruction is asynchronous, and its IO thread went on calling the IOProc
/// after its context was freed (a SIGSEGV in `io_proc` once the watchdog had released a tap,
/// ps-0e3).
#[allow(dead_code, reason = "held for their drops")]
struct Live {
    io: IoProc,
    tapped: Tapped,
}

/// An input IOProc on one device, into a sink; read through its [`Stream`].
pub struct Capture {
    // (Dropped first: the IOProc stops before its context is freed.)
    _io: IoProc,
    _ctx: Box<UnsafeCell<Ctx>>,
    stream: Stream,
}

/// A process tap, captured through its aggregate; read through its [`Stream`].
pub struct Tap {
    teardown: Arc<Teardown<Live>>,
    /// Freed only after the teardown (`Drop`), which the watchdog may be in the middle of.
    ctx: Box<UnsafeCell<Ctx>>,
    stream: Stream,
}

impl std::ops::Deref for Capture {
    type Target = Stream;
    fn deref(&self) -> &Stream {
        &self.stream
    }
}

impl std::ops::DerefMut for Capture {
    fn deref_mut(&mut self) -> &mut Stream {
        &mut self.stream
    }
}

impl std::ops::Deref for Tap {
    type Target = Stream;
    fn deref(&self) -> &Stream {
        &self.stream
    }
}

impl std::ops::DerefMut for Tap {
    fn deref_mut(&mut self) -> &mut Stream {
        &mut self.stream
    }
}

/// A started IOProc, its context and the stream it captures into.
type Opened = (IoProc, Box<UnsafeCell<Ctx>>, Stream);

/// Starts an IOProc on `device`, whose buffers are in `fmt`, into a sink (`crate::sink::sink`'s
/// `ring_s`, `delay` and `first`). On failure the delay line's writer comes back with the error.
fn open(
    device: Id,
    fmt: AudioStreamBasicDescription,
    ring_s: f64,
    delay: Option<Writer>,
    first: u64,
) -> Result<Opened, (String, Option<Writer>)> {
    let channels = fmt.mChannelsPerFrame as usize;
    let interleaved = fmt.mFormatFlags & kAudioFormatFlagIsNonInterleaved == 0;
    if fmt.mFormatID != kAudioFormatLinearPCM
        || fmt.mFormatFlags & kAudioFormatFlagIsFloat == 0
        || fmt.mBitsPerChannel != 32
        || channels == 0
    {
        return Err((format!("unexpected input format {fmt:?}"), delay));
    }
    let (sink, stream) = sink(fmt.mSampleRate, ring_s, delay, first);
    let mut ctx = Box::new(UnsafeCell::new(Ctx {
        sink,
        channels,
        interleaved,
    }));
    let mut id: AudioDeviceIOProcID = None;
    let created = check(
        unsafe {
            AudioDeviceCreateIOProcID(
                device,
                Some(io_proc),
                &*ctx as *const UnsafeCell<Ctx> as *mut c_void,
                NonNull::from(&mut id),
            )
        },
        "AudioDeviceCreateIOProcID",
    );
    if let Err(e) = created {
        return Err((e, ctx.get_mut().sink.take_writer()));
    }
    let io = IoProc { device, id };
    if let Err(e) = check(unsafe { AudioDeviceStart(device, id) }, "AudioDeviceStart") {
        drop(io);
        return Err((e, ctx.get_mut().sink.take_writer()));
    }
    Ok((io, ctx, stream))
}

impl Capture {
    /// The default input device (the built-in microphone), for the loopback test.
    pub fn microphone() -> Result<Capture, String> {
        Capture::microphone_with(4.0)
    }

    /// As `microphone`, with a ring of `ring_s` seconds.
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
        let (io, ctx, stream) = open(dev, fmt, ring_s, None, 0).map_err(|(e, _)| e)?;
        Ok(Capture {
            _io: io,
            _ctx: ctx,
            stream,
        })
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
    let input = HostTime(unsafe { input_time.as_ref() }.mHostTime);
    let now = HostTime(unsafe { now.as_ref() }.mHostTime);
    if ctx.interleaved {
        let ch = ctx.channels;
        let data = bufs[0].mData as *const f32;
        let frames = bufs[0].mDataByteSize as usize / 4 / ch;
        ctx.sink.push(
            frames,
            ch,
            |i, c| unsafe { *data.add(i * ch + c) },
            input,
            now,
        );
    } else {
        // (A buffer a channel.)
        let ch = ctx.channels.min(nbuf);
        let frames = bufs[0].mDataByteSize as usize / 4;
        let at = |i: usize, c: usize| unsafe { *(bufs[c].mData as *const f32).add(i) };
        ctx.sink.push(frames, ch, at, input, now);
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
        let (tapped, fmt) = aggregate_for(&desc)?;
        Tap::capture(tapped, fmt, None, 0).map_err(|(e, _)| e)
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
        let (tapped, fmt) = muted_aggregate(processes)?;
        let (writer, line) = audio::delay::delay_line(fmt.mSampleRate, delay, keep);
        let tap = Tap::capture(tapped, fmt, Some(writer), 0).map_err(|(e, _)| e)?;
        Ok((tap, line))
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
        let (tapped, fmt) = match muted_aggregate(processes) {
            Ok(t) => t,
            Err(e) => return Err((e, writer)),
        };
        if fmt.mSampleRate != rate {
            return Err((
                format!(
                    "the tap is at {} Hz, its line at {rate} Hz",
                    fmt.mSampleRate
                ),
                writer,
            ));
        }
        Tap::capture(tapped, fmt, Some(writer), first)
            .map_err(|(e, writer)| (e, writer.expect("the writer comes back")))
    }

    /// Starts capturing a tap's aggregate, or destroys them both.
    fn capture(
        tapped: Tapped,
        fmt: AudioStreamBasicDescription,
        delay: Option<Writer>,
        first: u64,
    ) -> Result<Tap, (String, Option<Writer>)> {
        let (io, ctx, stream) = open(tapped.aggregate.0, fmt, 4.0, delay, first)?;
        Ok(Tap {
            teardown: Arc::new(Teardown::new(Live { io, tapped })),
            ctx,
            stream,
        })
    }

    /// Whether the tap is live (its app muted): not yet destroyed, here or by its watchdog.
    pub fn live(&self) -> bool {
        self.teardown.live()
    }

    /// Destroys the tap (its app is heard again) and hands back the delay line's writer it
    /// captured into, if any; if the watchdog is destroying it, waits until it has.
    pub fn into_writer(mut self) -> Option<Writer> {
        self.teardown.release();
        // (The IOProc is gone, so its context is ours again.)
        self.ctx.get_mut().sink.take_writer()
    }

    /// A watchdog over this tap, destroying it when its capture or the main thread stalls.
    pub fn watchdog(&self) -> Watchdog {
        Watchdog::start(
            Arc::downgrade(&self.teardown),
            Arc::clone(&self.stream.alive),
        )
    }
}

/// A tap of `processes`, muted while tapped, and its aggregate, with the tap's format.
fn muted_aggregate(processes: &[Id]) -> Result<(Tapped, AudioStreamBasicDescription), String> {
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
/// output that reads it, with the tap's format.
fn aggregate_for(desc: &CATapDescription) -> Result<(Tapped, AudioStreamBasicDescription), String> {
    unsafe {
        desc.setName(&NSString::from_str(TAP_NAME));
        desc.setPrivate(!public());
    }
    let mut tap: Id = 0;
    check(
        unsafe { AudioHardwareCreateProcessTap(Some(desc), &mut tap) },
        "AudioHardwareCreateProcessTap",
    )?;
    let tap = ProcessTap(tap);
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
    let tapped = Tapped {
        aggregate: Aggregate(aggregate),
        tap,
    };

    let fmt: AudioStreamBasicDescription = coreaudio::get(
        tapped.tap.0,
        kAudioTapPropertyFormat,
        kAudioObjectPropertyScopeGlobal,
    )?;
    Ok((tapped, fmt))
}

impl crate::analysis::Source for Tap {
    fn rate(&self) -> f64 {
        self.stream.rate
    }

    fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>) {
        self.stream.drain(samples, metas);
    }
}

impl Drop for Tap {
    fn drop(&mut self) {
        // (Before its context is freed, waiting for the watchdog if it is tearing the tap down.)
        self.teardown.release();
    }
}
