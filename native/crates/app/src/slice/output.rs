//! Native output through AUHAL (the default output unit): `audio::mixer`'s voices, scheduled at
//! host times on the render timestamps' timeline, and the played file.

use std::cell::UnsafeCell;
use std::ffi::c_void;
use std::ptr::NonNull;
use std::sync::Arc;

use objc2_audio_toolbox::*;
use objc2_core_audio_types::*;

use audio::mixer::{Command, Mixer, Report};

use super::coreaudio::{self, check};
use super::tap::Meta;

/// What the render callback did, for the log.
#[derive(Clone, Copy, Debug)]
pub enum Event {
    /// A render buffer: its host time (seconds) and frames.
    Buffer { host: f64, frames: u32, now: f64 },
    /// What the mixer did with a voice.
    Voice(Report),
}

struct Ctx {
    mixer: Mixer,
    commands: rtrb::Consumer<Command>,
    events: rtrb::Producer<Event>,
    /// The timing of each buffer of the played file (`--file`), shaped as the tap's (`Meta`), so
    /// the analyzer hears what is played.
    music_meta: Option<rtrb::Producer<Meta>>,
    /// Everything runs, but the device gets silence.
    muted: bool,
    tick_s: f64,
}

pub struct Output {
    unit: AudioUnit,
    _ctx: Box<UnsafeCell<Ctx>>,
    pub rate: f64,
    /// Voices to schedule, move or cancel.
    pub commands: rtrb::Producer<Command>,
    pub events: rtrb::Consumer<Event>,
    /// The played file's buffer timing (`--file`).
    pub music_meta: Option<rtrb::Consumer<Meta>>,
}

unsafe impl Send for Output {}

unsafe extern "C-unwind" fn render(
    client: NonNull<c_void>,
    _flags: NonNull<AudioUnitRenderActionFlags>,
    ts: NonNull<AudioTimeStamp>,
    _bus: u32,
    frames: u32,
    data: *mut AudioBufferList,
) -> i32 {
    let ctx = unsafe { &mut *(client.as_ptr() as *mut UnsafeCell<Ctx>) }.get_mut();
    let host_ticks = unsafe { ts.as_ref() }.mHostTime;
    let host = host_ticks as f64 * ctx.tick_s;
    let now = unsafe { libc::mach_absolute_time() } as f64 * ctx.tick_s;
    let _ = ctx.events.push(Event::Buffer { host, frames, now });
    let list = unsafe { &mut *data };
    let bufs = unsafe {
        std::slice::from_raw_parts_mut(list.mBuffers.as_mut_ptr(), list.mNumberBuffers as usize)
    };
    let out =
        unsafe { std::slice::from_raw_parts_mut(bufs[0].mData as *mut f32, frames as usize * 2) };
    let events = &mut ctx.events;
    let mut report = |r: Report| {
        let _ = events.push(Event::Voice(r));
    };
    while let Ok(c) = ctx.commands.pop() {
        ctx.mixer.apply(c, &mut report);
    }
    let first = ctx.mixer.music_pos();
    let peak = ctx.mixer.render(host, out, &mut report);
    if ctx.muted {
        out.fill(0.0);
    }
    if let Some(meta) = &mut ctx.music_meta {
        let _ = meta.push(Meta {
            first,
            frames,
            input_host: host_ticks,
            now_host: unsafe { libc::mach_absolute_time() },
            peak,
            dropped: 0,
        });
    }
    0
}

impl Output {
    /// The default device's sample rate (a `--file` is decoded at it).
    pub fn device_rate() -> f64 {
        coreaudio::device(coreaudio::default_output()).rate
    }

    /// Starts the output, playing `music` (mono at `device_rate`) from the start if given, heard
    /// `music_delay` s after the analyzer reads it (Delayed listening; 0 in Direct), and the
    /// tapped app's capture from `delay_line` (Delayed listening on the system's audio); `muted`,
    /// it renders everything and sends silence.
    pub fn start(
        music: Option<Arc<[f32]>>,
        music_gain: f32,
        music_delay: f64,
        delay_line: Option<audio::delay::DelayLine>,
        bank: Vec<Arc<[f32]>>,
        muted: bool,
    ) -> Result<Output, String> {
        let desc = AudioComponentDescription {
            componentType: kAudioUnitType_Output,
            componentSubType: kAudioUnitSubType_DefaultOutput,
            componentManufacturer: kAudioUnitManufacturer_Apple,
            componentFlags: 0,
            componentFlagsMask: 0,
        };
        let comp = unsafe { AudioComponentFindNext(std::ptr::null_mut(), NonNull::from(&desc)) };
        if comp.is_null() {
            return Err("no default output component".into());
        }
        let mut unit: AudioUnit = std::ptr::null_mut();
        check(
            unsafe { AudioComponentInstanceNew(comp, NonNull::from(&mut unit)) },
            "AudioComponentInstanceNew",
        )?;
        let rate = coreaudio::device(coreaudio::default_output()).rate;
        let fmt = AudioStreamBasicDescription {
            mSampleRate: rate,
            mFormatID: kAudioFormatLinearPCM,
            mFormatFlags: kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked,
            mBytesPerPacket: 8,
            mFramesPerPacket: 1,
            mBytesPerFrame: 8,
            mChannelsPerFrame: 2,
            mBitsPerChannel: 32,
            mReserved: 0,
        };
        check(
            unsafe {
                AudioUnitSetProperty(
                    unit,
                    kAudioUnitProperty_StreamFormat,
                    kAudioUnitScope_Input,
                    0,
                    &fmt as *const _ as *const c_void,
                    std::mem::size_of_val(&fmt) as u32,
                )
            },
            "set stream format",
        )?;
        let (cp, cc) = rtrb::RingBuffer::new(256);
        let (ep, ec) = rtrb::RingBuffer::new(1 << 16);
        let (meta_tx, music_meta) = match &music {
            Some(_) => {
                let (mp, mc) = rtrb::RingBuffer::new(4096);
                (Some(mp), Some(mc))
            }
            None => (None, None),
        };
        let ctx = Box::new(UnsafeCell::new(Ctx {
            mixer: {
                let m = Mixer::new(rate, bank, music, music_gain)
                    .with_music_delay((music_delay * rate).round() as u64);
                match delay_line {
                    Some(line) => m.with_delay_line(line),
                    None => m,
                }
            },
            commands: cc,
            events: ep,
            music_meta: meta_tx,
            muted,
            tick_s: coreaudio::tick_s(),
        }));
        let cb = AURenderCallbackStruct {
            inputProc: Some(render),
            inputProcRefCon: &*ctx as *const UnsafeCell<Ctx> as *mut c_void,
        };
        check(
            unsafe {
                AudioUnitSetProperty(
                    unit,
                    kAudioUnitProperty_SetRenderCallback,
                    kAudioUnitScope_Input,
                    0,
                    &cb as *const _ as *const c_void,
                    std::mem::size_of_val(&cb) as u32,
                )
            },
            "set render callback",
        )?;
        check(unsafe { AudioUnitInitialize(unit) }, "AudioUnitInitialize")?;
        check(
            unsafe { AudioOutputUnitStart(unit) },
            "AudioOutputUnitStart",
        )?;
        Ok(Output {
            unit,
            _ctx: ctx,
            rate,
            commands: cp,
            events: ec,
            music_meta,
        })
    }

    /// The reported output latency of the default device, without the buffer
    /// (the render timestamp already marks the buffer's start), in seconds.
    pub fn reported_latency_s() -> f64 {
        let d = coreaudio::device(coreaudio::default_output());
        let l = d.out.unwrap();
        (l.device + l.safety + l.stream) as f64 / d.rate
    }
}

impl Drop for Output {
    fn drop(&mut self) {
        unsafe {
            AudioOutputUnitStop(self.unit);
            AudioComponentInstanceDispose(self.unit);
        }
    }
}
