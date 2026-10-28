//! The Windows Delayed listening spike (docs/design/windows-delayed-spike.md): one binary with
//! subcommands, each printing JSON lines.

mod analyze;
mod dev;
mod route;
mod stream;
mod wav;

use std::collections::HashMap;
use std::io::Write as _;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, mpsc};
use std::time::Duration;

use audio::delay::{Read, delay_line};
use serde_json::json;

use crate::route::Mode;
use crate::stream::{Capture, Clicks, Render, Source};
use crate::wav::RATE;

pub type R<T> = Result<T, Box<dyn std::error::Error>>;

pub mod clock {
    use windows::Win32::System::Performance::{QueryPerformanceCounter, QueryPerformanceFrequency};

    /// QPC time in seconds (the timeline WASAPI's QPC positions are on).
    pub fn now() -> f64 {
        let (mut c, mut f) = (0i64, 0i64);
        unsafe {
            let _ = QueryPerformanceCounter(&mut c);
            let _ = QueryPerformanceFrequency(&mut f);
        }
        c as f64 / f as f64
    }
}

const USAGE: &str = "spike <command>
  devices                              endpoints and their mix formats
  apps                                 audio sessions on every render endpoint
  source [--seconds S] [--device D] [--jitter] [--amp A (0.1)]
                                       play the click track
  capture <pid> <seconds> [--wav F] [--tone]
                                       process loopback of pid's tree (--tone: we play 3 kHz meanwhile)
  loopback <seconds> [--device D] [--wav F]
                                       endpoint loopback of a render device
  measure <seconds> [--device D] [--wav F]
                                       record a microphone
  mute <pid> <mute|vol:L>              silence pid's sessions, saving their state
  restore                              undo mute
  delay <pid> <M> <seconds> [--route R] [--guard] [--out D] [--wav F] [--gain G] [--no-compensate]
                                       capture, silence, and play M late
  guard <parent-pid>                   restore when the parent exits
  analyze clicks <wav> [--amp A (0.1)] | tone <wav> | level <wav> | lag <a.wav> <b.wav> <M> [--series]";

struct Args {
    pos: Vec<String>,
    opts: HashMap<String, String>,
}

impl Args {
    fn parse() -> Args {
        const FLAGS: &[&str] = &["tone", "guard", "jitter", "no-compensate", "series"];
        let mut pos = Vec::new();
        let mut opts = HashMap::new();
        let mut it = std::env::args().skip(1);
        while let Some(a) = it.next() {
            if let Some(k) = a.strip_prefix("--") {
                let v = if FLAGS.contains(&k) { String::new() } else { it.next().unwrap_or_default() };
                opts.insert(k.to_string(), v);
            } else {
                pos.push(a);
            }
        }
        Args { pos, opts }
    }
    fn pos(&self, i: usize) -> R<&str> {
        self.pos.get(i).map(String::as_str).ok_or_else(|| USAGE.into())
    }
    fn opt(&self, k: &str) -> Option<&str> {
        self.opts.get(k).map(String::as_str)
    }
    fn flag(&self, k: &str) -> bool {
        self.opts.contains_key(k)
    }
}

fn out(v: &serde_json::Value) {
    let mut o = std::io::stdout().lock();
    let _ = writeln!(o, "{v}");
    let _ = o.flush();
}

fn main() {
    let a = Args::parse();
    dev::com();
    if let Err(e) = run(&a) {
        out(&json!({"error": e.to_string()}));
        std::process::exit(1);
    }
}

fn run(a: &Args) -> R<()> {
    match a.pos(0)? {
        "apps" => apps(),
        "devices" => devices(),
        "source" => source(a),
        "capture" => {
            let src = Source::Process(a.pos(1)?.parse()?);
            capture(&src, a.pos(2)?.parse()?, a.opt("wav"), a.flag("tone"))
        }
        "loopback" => capture(&Source::Loopback(a.opt("device")), a.pos(1)?.parse()?, a.opt("wav"), false),
        "measure" => capture(&Source::Mic(a.opt("device")), a.pos(1)?.parse()?, a.opt("wav"), false),
        "mute" => {
            out(&route::apply(a.pos(1)?.parse()?, Mode::parse(a.pos(2)?)?)?);
            Ok(())
        }
        "restore" => {
            out(&route::restore()?);
            Ok(())
        }
        "delay" => delay(a),
        "guard" => guard(a.pos(1)?.parse()?),
        "analyze" => {
            let p = |i| a.pos(i).map(Path::new);
            let amp = a.opt("amp").map_or(Ok(0.1), str::parse)?;
            let v = match a.pos(1)? {
                "clicks" => analyze::clicks(&wav::read_mono(p(2)?)?, amp),
                "tone" => {
                    let x = wav::read_mono(p(2)?)?;
                    json!({"tone_1k_dbfs": analyze::tone(&x, 1000.0), "tone_3k_dbfs": analyze::tone(&x, 3000.0)})
                }
                "level" => analyze::level(&wav::read_mono(p(2)?)?),
                "lag" => analyze::lag(p(2)?, p(3)?, a.pos(4)?.parse()?, a.flag("series"))?,
                _ => return Err(USAGE.into()),
            };
            out(&v);
            Ok(())
        }
        _ => Err(USAGE.into()),
    }
}

fn devices() -> R<()> {
    use windows::Win32::Media::Audio::{IAudioClient, eCapture, eRender};
    use windows::Win32::System::Com::{CLSCTX_ALL, CoTaskMemFree};
    for (flow, kind) in [(eRender, "render"), (eCapture, "capture")] {
        let default = dev::find(flow, None).map(|d| d.name).unwrap_or_default();
        for d in dev::devices(flow)? {
            let (rate, channels, bits) = unsafe {
                let c: IAudioClient = d.dev.Activate(CLSCTX_ALL, None)?;
                let f = c.GetMixFormat()?;
                let v = ((*f).nSamplesPerSec, (*f).nChannels, (*f).wBitsPerSample);
                CoTaskMemFree(Some(f as _));
                v
            };
            out(&json!({"kind": kind, "name": d.name, "default": d.name == default,
                        "mix_rate": rate, "mix_channels": channels, "mix_bits": bits}));
        }
    }
    Ok(())
}

fn apps() -> R<()> {
    for s in dev::sessions()? {
        out(&json!({"device": s.device, "pid": s.pid, "exe": s.exe, "state": s.state,
                    "volume": s.volume(), "mute": s.muted(), "ident": s.ident}));
    }
    Ok(())
}

fn source(a: &Args) -> R<()> {
    let seconds: f64 = a.opt("seconds").map_or(Ok(3600.0), str::parse)?;
    let clicks = Clicks { jitter: a.flag("jitter"), amp: a.opt("amp").map_or(Ok(0.1), str::parse)? };
    let mut r = Render::open(a.opt("device"))?;
    out(&json!({"source": std::process::id(), "device": r.name, "jitter": clicks.jitter}));
    let end = clock::now() + seconds;
    let (mut n, mut k) = (0u64, 0u64);
    r.run(
        || clock::now() >= end,
        |t0, buf| {
            let len = buf.len() as u64 / 2;
            for i in 0..len {
                let s = clicks.sample(n + i);
                buf[2 * i as usize] = s;
                buf[2 * i as usize + 1] = s;
            }
            while clicks.start(k) < n + len {
                let at = t0 + (clicks.start(k) as f64 - n as f64) / RATE as f64;
                out(&json!({"click": k, "plays_at": at}));
                k += 1;
            }
            n += len;
        },
    )
}

/// Plays a 3 kHz tone on the default endpoint until `stop`: our own output, which a process
/// loopback of another process must not pick up.
fn tone(stop: Arc<AtomicBool>) -> std::thread::JoinHandle<Result<(), String>> {
    std::thread::spawn(move || {
        dev::com();
        let mut r = Render::open(None).map_err(|e| e.to_string())?;
        let mut n = 0u64;
        r.run(
            || stop.load(Ordering::Relaxed),
            |_, buf| {
                for f in buf.chunks_exact_mut(2) {
                    let s = 0.05 * (2.0 * std::f64::consts::PI * 3000.0 * n as f64 / RATE as f64).sin() as f32;
                    f[0] = s;
                    f[1] = s;
                    n += 1;
                }
            },
        )
        .map_err(|e| e.to_string())
    })
}

fn capture(src: &Source, seconds: f64, wav_path: Option<&str>, with_tone: bool) -> R<()> {
    let stop = Arc::new(AtomicBool::new(false));
    let toner = with_tone.then(|| tone(stop.clone()));
    let cap = Capture::open(src)?;
    out(&json!({"capture": cap.desc, "seconds": seconds, "tone_3k": with_tone}));
    let mut w = wav_path.map(|p| wav::Writer::create(Path::new(p))).transpose()?;
    let start = clock::now();
    let end = start + seconds;
    let mut all: Vec<f32> = Vec::new();
    let (mut frames, mut packets, mut silent, mut disc, mut tserr, mut qzero) = (0u64, 0u64, 0u64, 0u64, 0u64, 0u64);
    let (mut sumsq, mut peak, mut sec_frames) = (0f64, 0f32, 0u64);
    let mut next = start + 1.0;
    let mut first_devpos = None;
    let mut err = None;
    cap.run(
        || clock::now() >= end,
        |p| {
            packets += 1;
            frames += p.frames() as u64;
            sec_frames += p.frames() as u64;
            silent += u64::from(p.flags & 2 != 0);
            disc += u64::from(p.flags & 1 != 0);
            tserr += u64::from(p.flags & 4 != 0);
            qzero += u64::from(p.qpc == 0);
            first_devpos.get_or_insert(p.devpos);
            for f in p.data.chunks_exact(2) {
                let m = 0.5 * (f[0] + f[1]);
                sumsq += (m as f64).powi(2);
                peak = peak.max(m.abs());
                all.push(m);
            }
            if let Some(w) = &mut w {
                if let Err(e) = w.packet(p.data, p.devpos, p.qpc, p.flags) {
                    err = Some(e.to_string());
                }
            }
            let now = clock::now();
            if now >= next {
                // How long ago the newest frame was captured, by its QPC position.
                let age = if p.qpc != 0 { (now - p.host()) * 1000.0 - p.frames() as f64 / RATE as f64 * 1000.0 } else { f64::NAN };
                out(&json!({"t": now - start, "frames": sec_frames, "rms_dbfs": analyze::db((sumsq / sec_frames.max(1) as f64).sqrt()),
                            "peak_dbfs": analyze::db(peak as f64), "devpos": p.devpos, "qpc_100ns": p.qpc,
                            "newest_age_ms": age, "packets": packets, "silent_packets": silent}));
                sumsq = 0.0;
                peak = 0.0;
                sec_frames = 0;
                next += 1.0;
            }
        },
    )?;
    stop.store(true, Ordering::Relaxed);
    if let Some(t) = toner {
        t.join().map_err(|_| "tone thread panicked")??;
    }
    if let Some(w) = w {
        w.finish()?;
    }
    if let Some(e) = err {
        return Err(e.into());
    }
    out(&json!({"summary": {"frames": frames, "expected_frames": (seconds * RATE as f64) as u64,
        "packets": packets, "silent_packets": silent, "discontinuities": disc, "timestamp_errors": tserr,
        "packets_without_qpc": qzero, "level": analyze::level(&all),
        "tone_1k_dbfs": analyze::tone(&all, 1000.0), "tone_3k_dbfs": analyze::tone(&all, 3000.0),
        "clicks": analyze::clicks(&all, 0.1)}}));
    Ok(())
}

fn spawn_guard() -> R<u32> {
    use std::os::windows::process::CommandExt;
    use std::process::{Command, Stdio};
    const DETACHED_PROCESS: u32 = 0x8;
    const CREATE_NEW_PROCESS_GROUP: u32 = 0x200;
    const CREATE_BREAKAWAY_FROM_JOB: u32 = 0x0100_0000;
    let exe = std::env::current_exe()?;
    let me = std::process::id().to_string();
    let spawn = |flags| {
        Command::new(&exe)
            .args(["guard", &me])
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .creation_flags(flags)
            .spawn()
    };
    let child = spawn(DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_BREAKAWAY_FROM_JOB)
        .or_else(|_| spawn(DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP))?;
    Ok(child.id())
}

fn guard_log() -> PathBuf {
    route::state_path().with_file_name("guard.jsonl")
}

fn guard(parent: u32) -> R<()> {
    use windows::Win32::System::Threading::{INFINITE, OpenProcess, PROCESS_SYNCHRONIZE, WaitForSingleObject};
    let h = unsafe { OpenProcess(PROCESS_SYNCHRONIZE, false, parent)? };
    unsafe { WaitForSingleObject(h, INFINITE) };
    let exited = clock::now();
    let result = route::restore().map_err(|e| e.to_string());
    let v = json!({"guard": std::process::id(), "parent": parent, "parent_exited_qpc": exited,
                   "restore_ms": (clock::now() - exited) * 1000.0,
                   "result": match result { Ok(v) => v, Err(e) => json!({"error": e}) }});
    let mut f = std::fs::OpenOptions::new().create(true).append(true).open(guard_log())?;
    writeln!(f, "{v}")?;
    out(&v);
    Ok(())
}

#[derive(Default)]
struct CapStats {
    packets: AtomicU64,
    frames: AtomicU64,
    no_qpc: AtomicU64,
    dropped: AtomicU64,
    sumsq_bits: AtomicU64,
}

fn delay(a: &Args) -> R<()> {
    let pid: u32 = a.pos(1)?.parse()?;
    let m: f64 = a.pos(2)?.parse()?;
    let seconds: f64 = a.pos(3)?.parse()?;
    let mode = a.opt("route").map(Mode::parse).transpose()?;
    let gain = match (a.opt("gain"), mode) {
        (Some(g), _) => g.parse()?,
        (None, Some(Mode::Volume(v))) if !a.flag("no-compensate") => 1.0 / v,
        _ => 1.0,
    };
    let guard_pid = if a.flag("guard") { Some(spawn_guard()?) } else { None };
    let (mut writer, mut line) = delay_line(RATE as f64, m, m + 2.0);
    let stop = Arc::new(AtomicBool::new(false));
    let stats = Arc::new(CapStats::default());
    let (ready_tx, ready_rx) = mpsc::channel();
    let wav_path = a.opt("wav").map(PathBuf::from);
    // The route goes on before the capture starts, so no unsilenced packet is ever multiplied by
    // the compensating gain.
    let applied = mode.map(|md| route::apply(pid, md)).transpose()?;
    let capture = {
        let (stop, stats) = (stop.clone(), stats.clone());
        std::thread::spawn(move || -> Result<(), String> {
            dev::com();
            let e = |e: Box<dyn std::error::Error>| e.to_string();
            let cap = match Capture::open(&Source::Process(pid)) {
                Ok(c) => c,
                Err(err) => {
                    let _ = ready_tx.send(Err(err.to_string()));
                    return Err(err.to_string());
                }
            };
            let _ = ready_tx.send(Ok(cap.desc.clone()));
            let mut w = wav_path.as_deref().map(wav::Writer::create).transpose().map_err(e)?;
            let mut frame = 0u64;
            let mut sumsq = 0f64;
            cap.run(
                || stop.load(Ordering::Relaxed),
                |p| {
                    let n = p.frames();
                    let ok = writer.write(frame, n, p.host(), |x, y| {
                        let k = x.len();
                        for (i, s) in x.iter_mut().enumerate() {
                            *s = p.data[i] * gain;
                        }
                        for (i, s) in y.iter_mut().enumerate() {
                            *s = p.data[k + i] * gain;
                        }
                    });
                    for s in p.data {
                        sumsq += (*s as f64 * gain as f64).powi(2);
                    }
                    if let Some(w) = &mut w {
                        let _ = w.packet(p.data, p.devpos, p.qpc, p.flags);
                    }
                    frame += n as u64;
                    stats.packets.fetch_add(1, Ordering::Relaxed);
                    stats.frames.fetch_add(n as u64, Ordering::Relaxed);
                    stats.no_qpc.fetch_add(u64::from(p.qpc == 0), Ordering::Relaxed);
                    stats.dropped.fetch_add(u64::from(!ok), Ordering::Relaxed);
                    stats.sumsq_bits.store(sumsq.to_bits(), Ordering::Relaxed);
                },
            )
            .map_err(e)?;
            if let Some(w) = w {
                w.finish().map_err(e)?;
            }
            Ok(())
        })
    };
    let desc = match ready_rx.recv_timeout(Duration::from_secs(15)) {
        Ok(Ok(d)) => d,
        failed => {
            stop.store(true, Ordering::Relaxed);
            if mode.is_some() {
                route::restore()?;
            }
            return Err(match failed {
                Ok(Err(e)) => e.into(),
                _ => "capture never started".into(),
            });
        }
    };
    let result = (|| -> R<serde_json::Value> {
        let mut r = Render::open(a.opt("out"))?;
        out(&json!({"delay": m, "capture": desc, "render": r.name, "route": applied,
                    "gain": gain, "guard": guard_pid, "pid": std::process::id()}));
        let start = clock::now();
        let end = start + seconds;
        let mut next = start + 1.0;
        let (mut played, mut before, mut after, mut underrun) = (0u64, 0u64, 0u64, 0u64);
        let (mut out_sumsq, mut out_n, mut last_cap_sumsq, mut last_cap_frames) = (0f64, 0u64, 0f64, 0u64);
        let mut fills = Vec::new();
        r.run(
            || clock::now() >= end,
            |t0, buf| {
                line.pull();
                for (i, f) in buf.chunks_exact_mut(2).enumerate() {
                    match line.at(t0 + i as f64 / RATE as f64 - m) {
                        Read::Frame(l, rr) => {
                            f[0] = l;
                            f[1] = rr;
                            played += 1;
                            out_sumsq += (0.5 * (l + rr) as f64).powi(2);
                        }
                        Read::Before => {
                            f[0] = 0.0;
                            f[1] = 0.0;
                            before += 1;
                        }
                        Read::After => {
                            f[0] = 0.0;
                            f[1] = 0.0;
                            after += 1;
                            underrun += u64::from(played > 0);
                        }
                    }
                    out_n += 1;
                }
                let now = clock::now();
                if now >= next {
                    let fill = line.fill(t0 - m).map(|f| f * 1000.0);
                    let cap_frames = stats.frames.load(Ordering::Relaxed);
                    let cap_sumsq = f64::from_bits(stats.sumsq_bits.load(Ordering::Relaxed));
                    let cap_rms = ((cap_sumsq - last_cap_sumsq) / (2 * (cap_frames - last_cap_frames).max(1)) as f64).sqrt();
                    let t = now - start;
                    if t > m + 1.0 {
                        if let Some(f) = fill {
                            fills.push(f);
                        }
                    }
                    out(&json!({"t": t, "fill_ms": fill, "lead_ms": (t0 - now) * 1000.0,
                                "played": played, "before": before, "after": after, "underrun_frames": underrun,
                                "cap_frames": cap_frames, "cap_packets": stats.packets.load(Ordering::Relaxed),
                                "cap_no_qpc": stats.no_qpc.load(Ordering::Relaxed),
                                "cap_dropped": stats.dropped.load(Ordering::Relaxed),
                                "cap_rms_dbfs": analyze::db(cap_rms),
                                "out_rms_dbfs": analyze::db((out_sumsq / out_n.max(1) as f64).sqrt())}));
                    out_sumsq = 0.0;
                    out_n = 0;
                    last_cap_sumsq = cap_sumsq;
                    last_cap_frames = cap_frames;
                    next += 1.0;
                }
            },
        )?;
        Ok(json!({"played": played, "before": before, "after": after, "underrun_frames": underrun,
                  "fill_ms_after_m": analyze::stats(&fills)}))
    })();
    stop.store(true, Ordering::Relaxed);
    let cap_result = capture.join().map_err(|_| "capture thread panicked")?;
    let restored = if mode.is_some() { Some(route::restore()?) } else { None };
    let summary = result?;
    cap_result?;
    out(&json!({"summary": summary, "restored": restored}));
    Ok(())
}
