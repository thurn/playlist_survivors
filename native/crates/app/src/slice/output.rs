//! Native output through AUHAL (the default output unit): sample-accurate clicks
//! scheduled at host times on the render timestamps' timeline.

use std::cell::UnsafeCell;
use std::ffi::c_void;
use std::ptr::NonNull;
use std::sync::Arc;

use objc2_audio_toolbox::*;
use objc2_core_audio_types::*;

use super::coreaudio::{self, check};
use super::tap::Meta;

/// A click whose first sample should be rendered at `at` (host seconds, the
/// render timestamp's timeline; heard about `L_out` later).
#[derive(Clone, Copy, Debug)]
pub struct Click {
    pub at: f64,
    pub freq: f32,
}

/// What the render callback did, for the log.
#[derive(Clone, Copy, Debug)]
pub enum Event {
    /// A render buffer: its host time (seconds) and frames.
    Buffer { host: f64, frames: u32, now: f64 },
    /// A click started: requested time, actual first-sample host time.
    Played { at: f64, actual: f64 },
    /// A click that arrived too late to play at its time.
    Late { at: f64, buffer: f64 },
}

/// A sound effect to start now: an index into the bank, from a sample offset, at a gain.
#[derive(Clone, Copy, Debug)]
pub struct Play {
    pub sound: usize,
    pub from: usize,
    pub gain: f32,
}

struct Voice {
    pos: u32,
    freq: f32,
}

struct Ctx {
    /// A file played through the output (`--file`), mono at the output's rate, and the timing of
    /// each buffer of it, shaped as the tap's (`Meta`), so the analyzer hears what is played.
    music: Option<(Arc<[f32]>, rtrb::Producer<Meta>)>,
    music_gain: f32,
    music_pos: u64,
    /// Sound effects (mono at the output's rate), the requests to play them, and those playing.
    bank: Vec<Arc<[f32]>>,
    plays: rtrb::Consumer<Play>,
    playing: [Option<Play>; 16],
    clicks: rtrb::Consumer<Click>,
    events: rtrb::Producer<Event>,
    pending: [Option<Click>; 32],
    voices: [Option<Voice>; 8],
    rate: f64,
    tick_s: f64,
}

pub struct Output {
    unit: AudioUnit,
    _ctx: Box<UnsafeCell<Ctx>>,
    pub rate: f64,
    pub clicks: rtrb::Producer<Click>,
    pub events: rtrb::Consumer<Event>,
    /// The played file's buffer timing (`--file`).
    pub music_meta: Option<rtrb::Consumer<Meta>>,
    /// Sound effects to start.
    pub plays: rtrb::Producer<Play>,
}

unsafe impl Send for Output {}

const CLICK_FRAMES: u32 = 1440; // 30 ms at 48 kHz

unsafe extern "C-unwind" fn render(
    client: NonNull<c_void>,
    _flags: NonNull<AudioUnitRenderActionFlags>,
    ts: NonNull<AudioTimeStamp>,
    _bus: u32,
    frames: u32,
    data: *mut AudioBufferList,
) -> i32 {
    let ctx = unsafe { &mut *(client.as_ptr() as *mut UnsafeCell<Ctx>) }.get_mut();
    let host = unsafe { ts.as_ref() }.mHostTime as f64 * ctx.tick_s;
    let now = unsafe { libc::mach_absolute_time() } as f64 * ctx.tick_s;
    let _ = ctx.events.push(Event::Buffer { host, frames, now });
    while let Ok(c) = ctx.clicks.pop() {
        if let Some(slot) = ctx.pending.iter_mut().find(|p| p.is_none()) {
            *slot = Some(c);
        }
    }
    let list = unsafe { &mut *data };
    let bufs = unsafe {
        std::slice::from_raw_parts_mut(list.mBuffers.as_mut_ptr(), list.mNumberBuffers as usize)
    };
    let out =
        unsafe { std::slice::from_raw_parts_mut(bufs[0].mData as *mut f32, frames as usize * 2) };
    out.fill(0.0);
    let rate = ctx.rate;
    while let Ok(p) = ctx.plays.pop() {
        if p.sound < ctx.bank.len()
            && let Some(slot) = ctx.playing.iter_mut().find(|v| v.is_none())
        {
            *slot = Some(p);
        }
    }
    if let Some((music, meta)) = &mut ctx.music {
        let first = ctx.music_pos;
        let mut peak = 0.0f32;
        for i in 0..frames as usize {
            let s = music.get(first as usize + i).copied().unwrap_or(0.0);
            peak = peak.max(s.abs());
            out[2 * i] = s * ctx.music_gain;
            out[2 * i + 1] = s * ctx.music_gain;
        }
        ctx.music_pos += u64::from(frames);
        let _ = meta.push(Meta {
            first,
            frames,
            input_host: unsafe { ts.as_ref() }.mHostTime,
            now_host: unsafe { libc::mach_absolute_time() },
            peak,
            dropped: 0,
        });
    }
    for i in 0..frames {
        let t = host + i as f64 / rate;
        for p in ctx.pending.iter_mut() {
            if let Some(c) = *p
                && c.at <= t + 0.5 / rate
            {
                if c.at < host - 0.5 / rate && i == 0 {
                    let _ = ctx.events.push(Event::Late {
                        at: c.at,
                        buffer: host,
                    });
                }
                if let Some(v) = ctx.voices.iter_mut().find(|v| v.is_none()) {
                    *v = Some(Voice {
                        pos: 0,
                        freq: c.freq,
                    });
                }
                let _ = ctx.events.push(Event::Played {
                    at: c.at,
                    actual: t,
                });
                *p = None;
            }
        }
        let mut s = 0.0f32;
        for v in ctx.voices.iter_mut() {
            if let Some(voice) = v {
                let x = voice.pos as f32 / rate as f32;
                let env = (-x / 0.006).exp();
                s += 0.5 * env * (std::f32::consts::TAU * voice.freq * x).sin();
                voice.pos += 1;
                if voice.pos >= CLICK_FRAMES {
                    *v = None;
                }
            }
        }
        out[2 * i as usize] += s;
        out[2 * i as usize + 1] += s;
    }
    for slot in ctx.playing.iter_mut() {
        if let Some(p) = slot {
            let sound = &ctx.bank[p.sound];
            let n = (frames as usize).min(sound.len().saturating_sub(p.from));
            for i in 0..n {
                let x = sound[p.from + i] * p.gain;
                out[2 * i] += x;
                out[2 * i + 1] += x;
            }
            p.from += n;
            if p.from >= sound.len() {
                *slot = None;
            }
        }
    }
    0
}

impl Output {
    /// The default device's sample rate (a `--file` is decoded at it).
    pub fn device_rate() -> f64 {
        coreaudio::device(coreaudio::default_output()).rate
    }

    /// Starts the output, playing `music` (mono at `device_rate`) from the start if given.
    pub fn start(
        music: Option<Arc<[f32]>>,
        music_gain: f32,
        bank: Vec<Arc<[f32]>>,
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
        let (music, music_meta) = match music {
            Some(m) => {
                let (mp, mc) = rtrb::RingBuffer::new(4096);
                (Some((m, mp)), Some(mc))
            }
            None => (None, None),
        };
        let (pp, pc) = rtrb::RingBuffer::new(64);
        let ctx = Box::new(UnsafeCell::new(Ctx {
            bank,
            plays: pc,
            playing: [None; 16],
            music,
            music_gain,
            music_pos: 0,
            clicks: cc,
            events: ep,
            pending: [None; 32],
            voices: Default::default(),
            rate,
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
            clicks: cp,
            events: ec,
            music_meta,
            plays: pp,
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
