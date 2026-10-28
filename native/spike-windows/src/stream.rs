//! WASAPI streams: process loopback, endpoint loopback and microphone capture, and shared-mode
//! rendering, all as 48 kHz float stereo (the engine converts).

use std::ptr::null_mut;

use windows::Win32::Foundation::{HANDLE, WAIT_OBJECT_0};
use windows::Win32::Media::Audio::{
    AUDCLNT_BUFFERFLAGS_SILENT, AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
    AUDCLNT_STREAMFLAGS_EVENTCALLBACK, AUDCLNT_STREAMFLAGS_LOOPBACK,
    AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, AUDIOCLIENT_ACTIVATION_PARAMS,
    AUDIOCLIENT_ACTIVATION_PARAMS_0, AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
    AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS, ActivateAudioInterfaceAsync, IActivateAudioInterfaceAsyncOperation,
    IActivateAudioInterfaceCompletionHandler, IActivateAudioInterfaceCompletionHandler_Impl,
    IAudioCaptureClient, IAudioClient, IAudioClock, IAudioRenderClient,
    PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE, VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
    WAVEFORMATEX, eCapture, eRender,
};
use windows::Win32::Media::Multimedia::WAVE_FORMAT_IEEE_FLOAT;
use windows::Win32::System::Com::StructuredStorage::PROPVARIANT;
use windows::Win32::System::Com::{BLOB, CLSCTX_ALL};
use windows::Win32::System::Threading::{CreateEventW, SetEvent, WaitForSingleObject};
use windows::Win32::System::Variant::VT_BLOB;
use windows::core::{HRESULT, IUnknown, Interface, Ref, implement};

use crate::wav::RATE;
use crate::{R, clock, dev};

fn format() -> WAVEFORMATEX {
    WAVEFORMATEX {
        wFormatTag: WAVE_FORMAT_IEEE_FLOAT as u16,
        nChannels: 2,
        nSamplesPerSec: RATE,
        nAvgBytesPerSec: RATE * 8,
        nBlockAlign: 8,
        wBitsPerSample: 32,
        cbSize: 0,
    }
}

#[implement(IActivateAudioInterfaceCompletionHandler)]
struct Done(HANDLE);

impl IActivateAudioInterfaceCompletionHandler_Impl for Done_Impl {
    fn ActivateCompleted(
        &self,
        _op: Ref<IActivateAudioInterfaceAsyncOperation>,
    ) -> windows::core::Result<()> {
        unsafe { SetEvent(self.0) }
    }
}

/// An audio client capturing `pid`'s process tree by process loopback.
fn process_loopback(pid: u32) -> R<IAudioClient> {
    let params = AUDIOCLIENT_ACTIVATION_PARAMS {
        ActivationType: AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
        Anonymous: AUDIOCLIENT_ACTIVATION_PARAMS_0 {
            ProcessLoopbackParams: AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS {
                TargetProcessId: pid,
                ProcessLoopbackMode: PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE,
            },
        },
    };
    let mut pv = PROPVARIANT::default();
    unsafe {
        let inner = &mut *pv.Anonymous.Anonymous;
        inner.vt = VT_BLOB;
        inner.Anonymous.blob = BLOB {
            cbSize: size_of::<AUDIOCLIENT_ACTIVATION_PARAMS>() as u32,
            pBlobData: (&raw const params).cast_mut().cast(),
        };
        let event = CreateEventW(None, true, false, None)?;
        let handler: IActivateAudioInterfaceCompletionHandler = Done(event).into();
        let op = ActivateAudioInterfaceAsync(
            VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
            &IAudioClient::IID,
            Some(&raw const pv),
            &handler,
        )?;
        if WaitForSingleObject(event, 10_000) != WAIT_OBJECT_0 {
            return Err("process loopback activation timed out".into());
        }
        let mut hr = HRESULT(0);
        let mut unk: Option<IUnknown> = None;
        let r = op.GetActivateResult(&mut hr, &mut unk);
        // (Its drop would PropVariantClear the blob, which points at `params` on our stack.)
        std::mem::forget(pv);
        r?;
        hr.ok()?;
        Ok(unk.ok_or("activation returned no interface")?.cast()?)
    }
}

pub enum Source<'a> {
    /// Process loopback of a pid's tree.
    Process(u32),
    /// Endpoint loopback of a render device (the default if None).
    Loopback(Option<&'a str>),
    /// A capture device (the default microphone if None).
    Mic(Option<&'a str>),
}

pub struct Capture {
    client: IAudioClient,
    cap: IAudioCaptureClient,
    event: HANDLE,
    pub desc: String,
}

pub struct Packet<'a> {
    /// Interleaved stereo (zeros for a silent packet).
    pub data: &'a [f32],
    pub devpos: u64,
    /// QPC time of the first frame, 100 ns units (0 if the stream gave none).
    pub qpc: u64,
    pub flags: u32,
}

impl Packet<'_> {
    pub fn frames(&self) -> usize {
        self.data.len() / 2
    }
    /// The first frame's capture time (QPC s), falling back to now less the packet's length.
    pub fn host(&self) -> f64 {
        if self.qpc != 0 {
            self.qpc as f64 * 1e-7
        } else {
            clock::now() - self.frames() as f64 / RATE as f64
        }
    }
}

impl Capture {
    pub fn open(src: &Source) -> R<Capture> {
        let base = AUDCLNT_STREAMFLAGS_EVENTCALLBACK
            | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
            | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
        let (client, flags, desc): (IAudioClient, u32, String) = match src {
            Source::Process(pid) => {
                let c = process_loopback(*pid)?;
                let d = format!("process loopback of {pid} ({})", dev::exe(*pid));
                (
                    c,
                    AUDCLNT_STREAMFLAGS_LOOPBACK
                        | AUDCLNT_STREAMFLAGS_EVENTCALLBACK
                        | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
                    d,
                )
            }
            Source::Loopback(name) => {
                let d = dev::find(eRender, *name)?;
                (
                    unsafe { d.dev.Activate(CLSCTX_ALL, None)? },
                    base | AUDCLNT_STREAMFLAGS_LOOPBACK,
                    format!("endpoint loopback of {}", d.name),
                )
            }
            Source::Mic(name) => {
                let d = dev::find(eCapture, *name)?;
                (
                    unsafe { d.dev.Activate(CLSCTX_ALL, None)? },
                    base,
                    format!("capture from {}", d.name),
                )
            }
        };
        let fmt = format();
        unsafe {
            client.Initialize(AUDCLNT_SHAREMODE_SHARED, flags, 1_000_000, 0, &fmt, None)?;
            let event = CreateEventW(None, false, false, None)?;
            client.SetEventHandle(event)?;
            let cap: IAudioCaptureClient = client.GetService()?;
            client.Start()?;
            Ok(Capture { client, cap, event, desc })
        }
    }

    /// Hands every packet to `f` until `stop` says so (checked at least every 100 ms).
    pub fn run(&self, mut stop: impl FnMut() -> bool, mut f: impl FnMut(&Packet)) -> R<()> {
        let mut zeros = Vec::new();
        while !stop() {
            unsafe {
                WaitForSingleObject(self.event, 100);
            }
            loop {
                if unsafe { self.cap.GetNextPacketSize()? } == 0 {
                    break;
                }
                let mut p = null_mut();
                let (mut frames, mut flags, mut devpos, mut qpc) = (0u32, 0u32, 0u64, 0u64);
                unsafe {
                    self.cap.GetBuffer(
                        &mut p,
                        &mut frames,
                        &mut flags,
                        Some(&mut devpos),
                        Some(&mut qpc),
                    )?;
                }
                let n = 2 * frames as usize;
                let data: &[f32] = if flags & AUDCLNT_BUFFERFLAGS_SILENT.0 as u32 != 0 || p.is_null()
                {
                    zeros.resize(n, 0.0);
                    &zeros[..n]
                } else {
                    unsafe { std::slice::from_raw_parts(p.cast::<f32>(), n) }
                };
                f(&Packet { data, devpos, qpc, flags });
                unsafe { self.cap.ReleaseBuffer(frames)? };
            }
        }
        unsafe { self.client.Stop()? };
        Ok(())
    }
}

pub struct Render {
    client: IAudioClient,
    rc: IAudioRenderClient,
    clock: IAudioClock,
    event: HANDLE,
    buffer: u32,
    freq: u64,
    written: u64,
    started: bool,
    pub name: String,
}

impl Render {
    /// A shared-mode stream on the default render endpoint, or the first matching `name`.
    pub fn open(name: Option<&str>) -> R<Render> {
        let d = dev::find(eRender, name)?;
        let fmt = format();
        unsafe {
            let client: IAudioClient = d.dev.Activate(CLSCTX_ALL, None)?;
            client.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_EVENTCALLBACK
                    | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                    | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                300_000,
                0,
                &fmt,
                None,
            )?;
            let event = CreateEventW(None, false, false, None)?;
            client.SetEventHandle(event)?;
            let buffer = client.GetBufferSize()?;
            let rc: IAudioRenderClient = client.GetService()?;
            let clock: IAudioClock = client.GetService()?;
            let freq = clock.GetFrequency()?;
            Ok(Render {
                client,
                rc,
                clock,
                event,
                buffer,
                freq,
                written: 0,
                started: false,
                name: d.name,
            })
        }
    }

    /// When the next frame written will play (QPC s), by the clock's position.
    fn next_play_time(&self, padding: u32) -> f64 {
        let (mut pos, mut qpc) = (0u64, 0u64);
        if self.started
            && unsafe { self.clock.GetPosition(&mut pos, Some(&mut qpc)) }.is_ok()
            && qpc != 0
        {
            qpc as f64 * 1e-7 + (self.written as f64 / RATE as f64 - pos as f64 / self.freq as f64)
        } else {
            clock::now() + padding as f64 / RATE as f64
        }
    }

    /// Asks `fill` for each period's frames (interleaved stereo), with the QPC time its first
    /// frame plays, until `stop` says so.
    pub fn run(&mut self, mut stop: impl FnMut() -> bool, mut fill: impl FnMut(f64, &mut [f32])) -> R<()> {
        while !stop() {
            if self.started {
                unsafe {
                    WaitForSingleObject(self.event, 200);
                }
            }
            let padding = unsafe { self.client.GetCurrentPadding()? };
            let n = self.buffer - padding;
            if n == 0 {
                continue;
            }
            let t0 = self.next_play_time(padding);
            unsafe {
                let p = self.rc.GetBuffer(n)?;
                let buf = std::slice::from_raw_parts_mut(p.cast::<f32>(), 2 * n as usize);
                fill(t0, buf);
                self.rc.ReleaseBuffer(n, 0)?;
            }
            self.written += u64::from(n);
            if !self.started {
                unsafe { self.client.Start()? };
                self.started = true;
            }
        }
        unsafe { self.client.Stop()? };
        Ok(())
    }
}

/// The click track: a 1 kHz, 10 ms click every 500 ms (optionally jittered by up to 100 ms, so
/// a click `M` late can't coincide with a live one).
pub struct Clicks {
    pub jitter: bool,
    pub amp: f32,
}

pub const PERIOD: u64 = 24_000;
pub const CLICK: u64 = 480;

impl Clicks {
    /// The frame click `k` starts on.
    pub fn start(&self, k: u64) -> u64 {
        let off = if self.jitter {
            // (A fixed hash of k, 0..4800 frames.)
            (k.wrapping_mul(0x9E37_79B9_7F4A_7C15) >> 40) % 4800
        } else {
            0
        };
        k * PERIOD + off
    }

    pub fn sample(&self, n: u64) -> f32 {
        let s = self.start(n / PERIOD);
        if n >= s && n < s + CLICK {
            let x = (n - s) as f64 / RATE as f64;
            self.amp * (2.0 * std::f64::consts::PI * 1000.0 * x).sin() as f32
        } else {
            0.0
        }
    }
}

/// The ideal click waveform, for fitting.
pub fn ideal_click() -> Vec<f32> {
    (0..CLICK)
        .map(|n| (2.0 * std::f64::consts::PI * 1000.0 * n as f64 / RATE as f64).sin() as f32)
        .collect()
}
