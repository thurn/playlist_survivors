//! Delayed listening's platform risks (ES, docs/design/native-app.md "Testing it now"):
//! a per-process tap that mutes what it taps, what `SIGKILL` and a stalled main thread
//! leave muted, and the tapped app's stereo played `M` late through AUHAL.
//!
//! Every test listens through the built-in microphone (loopback): a 1 kHz tone or click
//! from the tapped process is heard only while it is not muted.

use std::cell::UnsafeCell;
use std::ffi::c_void;
use std::io::{BufRead, BufReader, Write};
use std::path::Path;
use std::ptr::NonNull;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering};
use std::time::Duration;

use objc2::AllocAnyThread;
use objc2::rc::Retained;
use objc2_audio_toolbox::*;
use objc2_core_audio::*;
use objc2_core_audio_types::*;
use objc2_foundation::{NSArray, NSNumber};

use crate::coreaudio::{self, Id, check, host_s, now_s};
use crate::loopback::{envelope, host_of, onsets};
use crate::stats::summarize;
use crate::tap::{self, Capture, Meta, Tap};

const TONE: f32 = 1000.0;
/// The delay test's click period.
const CLICK_S: f64 = 1.3;
/// Level windows for the mute tests.
const WIN_S: f64 = 0.05;

// --- processes ----------------------------------------------------------------

pub struct Proc {
    pub object: Id,
    pub pid: i32,
    pub bundle: String,
    pub output: bool,
}

pub fn processes() -> Vec<Proc> {
    let g = kAudioObjectPropertyScopeGlobal;
    coreaudio::get_vec::<Id>(
        coreaudio::SYSTEM,
        kAudioHardwarePropertyProcessObjectList,
        g,
    )
    .unwrap_or_default()
    .into_iter()
    .map(|o| Proc {
        object: o,
        pid: coreaudio::get(o, kAudioProcessPropertyPID, g).unwrap_or(-1),
        bundle: coreaudio::get_string(o, kAudioProcessPropertyBundleID, g)
            .map(|s| s.to_string())
            .unwrap_or_default(),
        output: coreaudio::get::<u32>(o, kAudioProcessPropertyIsRunningOutput, g).unwrap_or(0) != 0,
    })
    .collect()
}

fn command_of(pid: i32) -> String {
    std::process::Command::new("/bin/ps")
        .args(["-o", "comm=", "-p", &pid.to_string()])
        .output()
        .map(|o| String::from_utf8_lossy(&o.stdout).trim().to_string())
        .unwrap_or_default()
}

/// Core Audio's process objects: the candidates Delayed listening's picker would show.
pub fn procs() {
    for p in processes() {
        println!(
            "object {:>4} pid {:>6} output {} bundle '{}' command '{}'",
            p.object,
            p.pid,
            if p.output { "yes" } else { "no " },
            p.bundle,
            command_of(p.pid)
        );
    }
}

fn object_of_pid(pid: i32) -> Option<Id> {
    // A process gets its object once it opens audio: wait up to 3 s.
    for _ in 0..60 {
        let o: Id = coreaudio::get_q(
            coreaudio::SYSTEM,
            kAudioHardwarePropertyTranslatePIDToProcessObject,
            kAudioObjectPropertyScopeGlobal,
            Some(&pid),
        )
        .unwrap_or(0);
        if o != 0 {
            return Some(o);
        }
        std::thread::sleep(Duration::from_millis(50));
    }
    None
}

/// A muting tap over some process objects, read by an IOProc on a private aggregate device.
pub fn process_tap(objects: &[Id]) -> Result<Tap, String> {
    let (tap, aggregate, fmt) = create_process_tap(objects)?;
    let capture = Capture::start(aggregate, fmt)?;
    Ok(Tap {
        tap,
        aggregate,
        capture,
    })
}

fn create_process_tap(objects: &[Id]) -> Result<(Id, Id, AudioStreamBasicDescription), String> {
    let nums: Vec<Retained<NSNumber>> = objects.iter().map(|&o| NSNumber::new_u32(o)).collect();
    let arr = NSArray::from_retained_slice(&nums);
    let desc =
        unsafe { CATapDescription::initStereoMixdownOfProcesses(CATapDescription::alloc(), &arr) };
    unsafe { desc.setMuteBehavior(CATapMuteBehavior::MutedWhenTapped) };
    tap::aggregate_for(&desc)
}

fn wav(path: &Path, rate: u32, pcm: &[i16]) {
    let mut f = std::io::BufWriter::new(std::fs::File::create(path).unwrap());
    let data = (2 * pcm.len()) as u32;
    let mut h = Vec::with_capacity(44);
    h.extend_from_slice(b"RIFF");
    h.extend_from_slice(&(36 + data).to_le_bytes());
    h.extend_from_slice(b"WAVEfmt ");
    h.extend_from_slice(&16u32.to_le_bytes());
    h.extend_from_slice(&1u16.to_le_bytes());
    h.extend_from_slice(&1u16.to_le_bytes());
    h.extend_from_slice(&rate.to_le_bytes());
    h.extend_from_slice(&(rate * 2).to_le_bytes());
    h.extend_from_slice(&2u16.to_le_bytes());
    h.extend_from_slice(&16u16.to_le_bytes());
    h.extend_from_slice(b"data");
    h.extend_from_slice(&data.to_le_bytes());
    f.write_all(&h).unwrap();
    for s in pcm {
        f.write_all(&s.to_le_bytes()).unwrap();
    }
}

/// A steady 1 kHz tone, for level steps.
pub fn tone_wav(path: &Path, secs: f64) {
    let rate = 16000u32;
    let pcm: Vec<i16> = (0..(secs * rate as f64) as usize)
        .map(|i| {
            let x = i as f32 / rate as f32;
            let fade = (x / 0.02).min(1.0).min(((secs as f32) - x) / 0.02).max(0.0);
            (0.5 * fade * (std::f32::consts::TAU * TONE * x).sin() * 32767.0) as i16
        })
        .collect();
    wav(path, rate, &pcm);
}

/// 30 ms 1 kHz clicks every 1.3 s from 0.3 s, at 8 kHz (an hour is 58 MB).
fn click_wav(path: &Path, secs: f64) {
    let rate = 8000u32;
    let mut pcm = vec![0i16; (secs * rate as f64) as usize];
    let mut t = 0.3;
    while t + 0.05 < secs {
        let start = (t * rate as f64) as usize;
        for i in 0..(0.03 * rate as f64) as usize {
            let x = i as f32 / rate as f32;
            let s = 0.6 * (-x / 0.006).exp() * (std::f32::consts::TAU * TONE * x).sin();
            pcm[start + i] = (s * 32767.0) as i16;
        }
        t += CLICK_S;
    }
    wav(path, rate, &pcm);
}

fn afplay(path: &Path, volume: f32) -> std::process::Child {
    std::process::Command::new("/usr/bin/afplay")
        .arg("-v")
        .arg(volume.to_string())
        .arg(path)
        .spawn()
        .expect("afplay")
}

/// Process objects for target specs: `pid:N` or `bundle:PREFIX` (every process whose bundle ID
/// starts with it).
fn objects_for(specs: &[String]) -> Vec<Id> {
    let mut out = Vec::new();
    for s in specs {
        if let Some(pid) = s.strip_prefix("pid:") {
            if let Some(o) = object_of_pid(pid.parse().expect("pid")) {
                out.push(o);
            }
        } else if let Some(prefix) = s.strip_prefix("bundle:") {
            out.extend(
                processes()
                    .into_iter()
                    .filter(|p| p.bundle.starts_with(prefix))
                    .map(|p| p.object),
            );
        }
    }
    out
}

// --- recording and levels -----------------------------------------------------

/// The microphone (and optionally a tap's capture) drained until a deadline.
struct Rec {
    /// The listener: the microphone, or with `PS_LISTEN=tap` a global tap (including us), which
    /// hears what reaches the output mix when the speakers can't be used.
    mic: Capture,
    _observer: Option<Tap>,
    ms: Vec<f32>,
    mm: Vec<Meta>,
    tap_peak: f32,
    events: Vec<(String, f64)>,
    heartbeat: Option<Arc<AtomicU64>>,
}

/// The microphone, or (`PS_LISTEN=tap`) a global tap's capture and the tap that owns it.
fn listener() -> (Capture, Option<Tap>) {
    if std::env::var("PS_LISTEN").as_deref() == Ok("tap") {
        let none: Retained<NSArray<NSNumber>> = NSArray::new();
        let desc = unsafe {
            CATapDescription::initStereoGlobalTapButExcludeProcesses(
                CATapDescription::alloc(),
                &none,
            )
        };
        // (The devices stay until the process exits.)
        let (_, aggregate, fmt) = tap::aggregate_for(&desc).expect("observer tap");
        println!("listening through a global tap");
        (
            Capture::start_with(aggregate, fmt, 30.0).expect("observer capture"),
            None,
        )
    } else {
        (Capture::microphone_with(30.0).expect("microphone"), None)
    }
}

impl Rec {
    fn new() -> Rec {
        let (mic, observer) = listener();
        Rec {
            mic,
            _observer: observer,
            ms: Vec::new(),
            mm: Vec::new(),
            tap_peak: 0.0,
            events: Vec::new(),
            heartbeat: None,
        }
    }

    fn mark(&mut self, what: &str) {
        let t = now_s();
        println!("{:>10.3}  {what}", t);
        self.events.push((what.to_string(), t));
    }

    fn pump(&mut self, secs: f64, mut tap: Option<&mut Capture>) {
        let end = now_s() + secs;
        let (mut ts, mut tm) = (Vec::new(), Vec::new());
        while now_s() < end {
            if let Some(hb) = &self.heartbeat {
                hb.store(coreaudio::now_ticks(), Ordering::Relaxed);
            }
            self.mic.drain(&mut self.ms, &mut self.mm);
            if let Some(t) = tap.as_deref_mut() {
                t.drain(&mut ts, &mut tm);
                for m in tm.drain(..) {
                    self.tap_peak = self.tap_peak.max(m.peak);
                }
                ts.clear();
            }
            std::thread::sleep(Duration::from_millis(5));
        }
    }

    /// The 1 kHz level in each window: (host time, mean envelope).
    fn levels(&self) -> Vec<(f64, f32)> {
        let env = envelope(&self.ms, self.mic.rate, TONE);
        let w = (WIN_S * self.mic.rate) as usize;
        env.chunks(w)
            .enumerate()
            .filter_map(|(k, c)| {
                let t = host_of(&self.mm, self.mic.rate, k * w)?;
                Some((t, c.iter().sum::<f32>() / c.len() as f32))
            })
            .collect()
    }

    fn event(&self, what: &str) -> f64 {
        self.events
            .iter()
            .find(|e| e.0 == what)
            .map(|e| e.1)
            .unwrap_or_else(|| panic!("no event {what}"))
    }

    /// Writes the levels and events, and returns the baseline (median level before `before`).
    fn report(&self, log: &Path, name: &str, before: &str) -> (Vec<(f64, f32)>, f32) {
        let lv = self.levels();
        let mut f = std::fs::File::create(log.join(format!("{name}-levels.csv"))).unwrap();
        writeln!(f, "host_s,level").unwrap();
        for (t, l) in &lv {
            writeln!(f, "{t:.4},{l:.6}").unwrap();
        }
        let t0 = self.events.first().map(|e| e.1).unwrap_or(0.0);
        let cut = self.event(before);
        let mut base: Vec<f32> = lv
            .iter()
            .filter(|(t, _)| *t > t0 + 0.5 && *t < cut)
            .map(|x| x.1)
            .collect();
        base.sort_by(|a, b| a.partial_cmp(b).unwrap());
        let baseline = base.get(base.len() / 2).copied().unwrap_or(f32::NAN);
        println!(
            "baseline 1 kHz level {baseline:.5} ({} windows)",
            base.len()
        );
        (lv, baseline)
    }
}

/// The median level between two times, as a share of the baseline.
fn share(lv: &[(f64, f32)], a: f64, b: f64, baseline: f32) -> f32 {
    let mut v: Vec<f32> = lv
        .iter()
        .filter(|(t, _)| *t >= a && *t < b)
        .map(|x| x.1)
        .collect();
    v.sort_by(|a, b| a.partial_cmp(b).unwrap());
    v.get(v.len() / 2).copied().unwrap_or(f32::NAN) / baseline
}

/// Seconds after `at` until the level first goes below (falling) or above (rising) a share of
/// the baseline.
fn crossing(lv: &[(f64, f32)], at: f64, baseline: f32, share: f32, falling: bool) -> Option<f64> {
    lv.iter()
        .find(|(t, l)| {
            *t >= at
                && if falling {
                    *l < share * baseline
                } else {
                    *l > share * baseline
                }
        })
        .map(|(t, _)| t - at)
}

fn ms(x: Option<f64>) -> String {
    x.map(|v| format!("{:.0} ms", v * 1e3))
        .unwrap_or_else(|| "never".into())
}

// --- the mute test --------------------------------------------------------------

/// Taps a process (afplay's tone by default, else `pid:N` / `bundle:PREFIX` specs) and checks it
/// goes silent while tapped, with the IOProc stopped, restarted, and after the tap is destroyed.
pub fn mute_test(log: &Path, specs: &[String]) {
    let mut rec = Rec::new();
    let mut child = None;
    let objects = if specs.is_empty() {
        let path = log.join("tone.wav");
        tone_wav(&path, 30.0);
        let c = afplay(&path, 0.3);
        let pid = c.id() as i32;
        child = Some(c);
        vec![object_of_pid(pid).expect("afplay's process object")]
    } else {
        objects_for(specs)
    };
    println!("tapping process objects {objects:?}");
    rec.mark("start");
    rec.pump(3.0, None);
    rec.mark("create");
    let tap = process_tap(&objects);
    let mut tap = match tap {
        Ok(t) => t,
        Err(e) => {
            println!("tap failed: {e}");
            return;
        }
    };
    rec.mark("tapping");
    rec.pump(5.0, Some(&mut tap.capture));
    rec.mark("pause");
    tap.capture.pause().unwrap();
    rec.pump(3.0, None);
    rec.mark("resume");
    tap.capture.resume().unwrap();
    rec.pump(3.0, Some(&mut tap.capture));
    rec.mark("destroy");
    drop(tap);
    rec.mark("destroyed");
    rec.pump(3.0, None);
    if let Some(mut c) = child {
        let _ = c.kill();
        let _ = c.wait();
    }
    let (lv, b) = rec.report(log, "mute", "create");
    let e = |w: &str| rec.event(w);
    println!("tap capture peak while tapped: {:.3}", rec.tap_peak);
    println!(
        "level while tapped (create + 0.5 s .. pause): {:.3} of baseline",
        share(&lv, e("create") + 0.5, e("pause"), b)
    );
    println!(
        "mute: below 10% {} after the create call (it returned after {:.0} ms)",
        ms(crossing(&lv, e("create"), b, 0.1, true)),
        (e("tapping") - e("create")) * 1e3
    );
    println!(
        "IOProc stopped: level {:.3} of baseline; above 50% {} after the stop",
        share(&lv, e("pause") + 0.5, e("resume"), b),
        ms(crossing(&lv, e("pause"), b, 0.5, false))
    );
    println!(
        "IOProc restarted: level {:.3}; below 10% {} after the start",
        share(&lv, e("resume") + 0.5, e("destroy"), b),
        ms(crossing(&lv, e("resume"), b, 0.1, true))
    );
    println!(
        "destroyed: level {:.3}; above 50% {} after the destroy call (it took {:.0} ms)",
        share(&lv, e("destroyed") + 0.5, e("destroyed") + 3.0, b),
        ms(crossing(&lv, e("destroy"), b, 0.5, false)),
        (e("destroyed") - e("destroy")) * 1e3
    );
}

// --- SIGKILL --------------------------------------------------------------------

/// `hold PID [SECS]`: taps a process and holds the tap, saying `tapped` once it is read.
pub fn hold(args: &[String]) {
    let pid: i32 = args[0].parse().expect("pid");
    let secs: f64 = args.get(1).and_then(|s| s.parse().ok()).unwrap_or(60.0);
    let object = object_of_pid(pid).expect("process object");
    let mut tap = process_tap(&[object]).expect("tap");
    println!("tapped {} {}", tap.tap, tap.aggregate);
    std::io::stdout().flush().unwrap();
    let (mut s, mut m) = (Vec::new(), Vec::new());
    let end = now_s() + secs;
    while now_s() < end {
        tap.drain(&mut s, &mut m);
        s.clear();
        m.clear();
        std::thread::sleep(Duration::from_millis(10));
    }
}

fn spike_objects() -> (usize, usize) {
    let g = kAudioObjectPropertyScopeGlobal;
    let aggregates = coreaudio::devices()
        .iter()
        .filter(|d| d.name == "PS spike tap")
        .count();
    let taps = coreaudio::get_vec::<Id>(coreaudio::SYSTEM, kAudioHardwarePropertyTapList, g)
        .unwrap_or_default()
        .len();
    (aggregates, taps)
}

/// Every tap and spike aggregate other processes can see, and whether an IOProc runs on it.
pub fn taps() {
    let g = kAudioObjectPropertyScopeGlobal;
    for t in coreaudio::get_vec::<Id>(coreaudio::SYSTEM, kAudioHardwarePropertyTapList, g)
        .unwrap_or_default()
    {
        let name = coreaudio::get_string(t, kAudioObjectPropertyName, g)
            .map(|s| s.to_string())
            .unwrap_or_default();
        let uid = coreaudio::get_string(t, kAudioTapPropertyUID, g)
            .map(|s| s.to_string())
            .unwrap_or_default();
        println!("tap {t} '{name}' {uid}");
    }
    for d in coreaudio::devices()
        .iter()
        .filter(|d| d.name == "PS spike tap")
    {
        let running: u32 =
            coreaudio::get(d.id, kAudioDevicePropertyDeviceIsRunningSomewhere, g).unwrap_or(9);
        println!(
            "aggregate {} '{}' {} running somewhere {running}",
            d.id, d.name, d.uid
        );
    }
}

/// Destroys every spike aggregate and every tap named as the spike names them.
pub fn cleanup() {
    let g = kAudioObjectPropertyScopeGlobal;
    for d in coreaudio::devices()
        .iter()
        .filter(|d| d.name == "PS spike tap")
    {
        let st = unsafe { AudioHardwareDestroyAggregateDevice(d.id) };
        println!("destroy aggregate {}: {st}", d.id);
    }
    for t in coreaudio::get_vec::<Id>(coreaudio::SYSTEM, kAudioHardwarePropertyTapList, g)
        .unwrap_or_default()
    {
        let name = coreaudio::get_string(t, kAudioObjectPropertyName, g)
            .map(|s| s.to_string())
            .unwrap_or_default();
        if name.contains("spike") {
            let st = unsafe { AudioHardwareDestroyProcessTap(t) };
            println!("destroy tap {t} '{name}': {st}");
        }
    }
}

/// A child process taps afplay's tone and is killed with SIGKILL while tapping.
pub fn sigkill_test(log: &Path) {
    let mut rec = Rec::new();
    let path = log.join("tone.wav");
    tone_wav(&path, 30.0);
    let mut player = afplay(&path, 0.3);
    let pid = player.id() as i32;
    object_of_pid(pid).expect("afplay's process object");
    println!(
        "before: spike aggregates and taps visible {:?}",
        spike_objects()
    );
    rec.mark("start");
    rec.pump(3.0, None);
    rec.mark("spawn");
    let mut child = std::process::Command::new(std::env::current_exe().unwrap())
        .args(["hold", &pid.to_string(), "60"])
        .env(
            "PS_PUBLIC",
            if std::env::var("PS_CHILD_PRIVATE").as_deref() == Ok("1") {
                "0"
            } else {
                "1"
            },
        )
        .stdout(std::process::Stdio::piped())
        .spawn()
        .expect("child");
    let mut line = String::new();
    BufReader::new(child.stdout.take().unwrap())
        .read_line(&mut line)
        .unwrap();
    rec.mark("tapped");
    println!("child {} says: {}", child.id(), line.trim());
    println!(
        "while tapped: spike aggregates and taps visible {:?}",
        spike_objects()
    );
    taps();
    // The child's own object IDs, queried directly (a private tap is not listed to us).
    let ids: Vec<Id> = line
        .split_whitespace()
        .skip(1)
        .filter_map(|x| x.parse().ok())
        .collect();
    let g = kAudioObjectPropertyScopeGlobal;
    let probe = |when: &str| {
        for &id in &ids {
            let class = coreaudio::get::<u32>(id, kAudioObjectPropertyClass, g);
            let running =
                coreaudio::get::<u32>(id, kAudioDevicePropertyDeviceIsRunningSomewhere, g);
            println!(
                "{when}: object {id} class {:?} running somewhere {:?}",
                class.map(coreaudio::fourcc),
                running
            );
        }
    };
    probe("while tapped");
    rec.pump(4.0, None);
    rec.mark("kill");
    unsafe { libc::kill(child.id() as i32, libc::SIGKILL) };
    let status = child.wait().unwrap();
    rec.mark("reaped");
    println!("child exit: {status:?}");
    rec.pump(4.0, None);
    probe("4 s after SIGKILL");
    println!(
        "after: spike aggregates and taps visible {:?}",
        spike_objects()
    );
    taps();
    let _ = player.kill();
    let _ = player.wait();
    let (lv, b) = rec.report(log, "sigkill", "spawn");
    let e = |w: &str| rec.event(w);
    println!(
        "level while the child taps: {:.3} of baseline",
        share(&lv, e("tapped") + 0.5, e("kill"), b)
    );
    println!(
        "after SIGKILL: level {:.3}; above 50% {} after the kill",
        share(&lv, e("reaped") + 1.0, e("reaped") + 4.0, b),
        ms(crossing(&lv, e("kill"), b, 0.5, false))
    );
}

// --- the watchdog ---------------------------------------------------------------

/// The main thread stalls for 6 s while tapping; a watchdog thread destroys the tap once the
/// main thread has not checked in for 2 s.
pub fn watchdog_test(log: &Path) {
    let mut rec = Rec::new();
    let path = log.join("tone.wav");
    tone_wav(&path, 30.0);
    let mut player = afplay(&path, 0.3);
    let object = object_of_pid(player.id() as i32).expect("afplay's process object");
    let hb = Arc::new(AtomicU64::new(coreaudio::now_ticks()));
    rec.heartbeat = Some(hb.clone());
    rec.mark("start");
    rec.pump(3.0, None);
    rec.mark("create");
    let mut tap = process_tap(&[object]).expect("tap");
    let (tap_id, agg) = (tap.tap, tap.aggregate);
    let fired = Arc::new(AtomicU64::new(0));
    let stop = Arc::new(AtomicBool::new(false));
    let statuses = Arc::new(AtomicU64::new(0));
    let dog = {
        let (hb, fired, stop, statuses) =
            (hb.clone(), fired.clone(), stop.clone(), statuses.clone());
        std::thread::spawn(move || {
            while !stop.load(Ordering::Relaxed) {
                std::thread::sleep(Duration::from_millis(50));
                let age = now_s() - host_s(hb.load(Ordering::Relaxed));
                if age > 2.0 {
                    let (a, t) = unsafe {
                        (
                            AudioHardwareDestroyAggregateDevice(agg),
                            AudioHardwareDestroyProcessTap(tap_id),
                        )
                    };
                    fired.store(coreaudio::now_ticks(), Ordering::Relaxed);
                    statuses.store(
                        ((a as u32 as u64) << 32) | t as u32 as u64,
                        Ordering::Relaxed,
                    );
                    return;
                }
            }
        })
    };
    rec.mark("tapping");
    rec.pump(3.0, Some(&mut tap.capture));
    rec.mark("stall");
    std::thread::sleep(Duration::from_secs(6));
    rec.mark("unstall");
    rec.pump(3.0, None);
    stop.store(true, Ordering::Relaxed);
    dog.join().unwrap();
    let f = fired.load(Ordering::Relaxed);
    if f != 0 {
        let st = statuses.load(Ordering::Relaxed);
        println!(
            "watchdog: destroy aggregate status {}, destroy tap status {}",
            (st >> 32) as u32 as i32,
            st as u32 as i32
        );
        let g = kAudioObjectPropertyScopeGlobal;
        for id in [agg, tap_id] {
            println!(
                "after the watchdog: object {id} class {:?}",
                coreaudio::get::<u32>(id, kAudioObjectPropertyClass, g).map(coreaudio::fourcc)
            );
        }
        rec.events.push(("fired".into(), host_s(f)));
        // The watchdog destroyed the devices; leak the rest rather than destroy them twice.
        std::mem::forget(tap);
    } else {
        drop(tap);
    }
    let _ = player.kill();
    let _ = player.wait();
    let (lv, b) = rec.report(log, "watchdog", "create");
    let e = |w: &str| rec.event(w);
    println!(
        "level while tapped: {:.3} of baseline",
        share(&lv, e("tapping") + 0.5, e("stall"), b)
    );
    if f == 0 {
        println!("the watchdog never fired");
        return;
    }
    println!(
        "watchdog fired {:.0} ms after the stall began",
        (e("fired") - e("stall")) * 1e3
    );
    println!(
        "audible (above 50%) {} after the stall began, {} after the watchdog fired",
        ms(crossing(&lv, e("stall"), b, 0.5, false)),
        ms(crossing(&lv, e("fired"), b, 0.5, false))
    );
}

// --- the delay line -------------------------------------------------------------

/// Stereo frames captured from the tap, read back `M` later by the output's render callback.
struct DelayLine {
    buf: Vec<AtomicU32>,
    cap: u64,
    written: AtomicU64,
    // The latest tap buffer: its first frame and that frame's host time (a seqlock).
    seq: AtomicU64,
    anchor_first: AtomicU64,
    anchor_host: AtomicU64,
    rate: f64,
    m: f64,
    tick_s: f64,
    underruns: AtomicU64,
    slips: AtomicU64,
    /// Largest |wanted - read position| this minute, in micro-frames.
    max_err: AtomicU64,
}

impl DelayLine {
    fn anchor(&self) -> Option<(u64, f64)> {
        loop {
            let s1 = self.seq.load(Ordering::Acquire);
            if s1 == 0 {
                return None;
            }
            if s1 % 2 == 1 {
                continue;
            }
            let f = self.anchor_first.load(Ordering::Acquire);
            let h = self.anchor_host.load(Ordering::Acquire);
            if self.seq.load(Ordering::Acquire) == s1 {
                return Some((f, h as f64 * self.tick_s));
            }
        }
    }
}

struct TapCtx {
    line: Arc<DelayLine>,
    channels: usize,
    interleaved: bool,
    mono: rtrb::Producer<f32>,
    meta: rtrb::Producer<Meta>,
}

unsafe extern "C-unwind" fn delay_io(
    _device: AudioObjectID,
    now: NonNull<AudioTimeStamp>,
    input: NonNull<AudioBufferList>,
    input_time: NonNull<AudioTimeStamp>,
    _output: NonNull<AudioBufferList>,
    _output_time: NonNull<AudioTimeStamp>,
    client: *mut c_void,
) -> i32 {
    let ctx = unsafe { &mut *(client as *mut UnsafeCell<TapCtx>) }.get_mut();
    let list = unsafe { input.as_ref() };
    let nbuf = list.mNumberBuffers as usize;
    if nbuf == 0 {
        return 0;
    }
    let bufs = unsafe { std::slice::from_raw_parts(list.mBuffers.as_ptr(), nbuf) };
    let ch = ctx.channels.max(1);
    let frames = if ctx.interleaved {
        bufs[0].mDataByteSize as usize / 4 / ch
    } else {
        bufs[0].mDataByteSize as usize / 4
    };
    let sample = |i: usize, c: usize| -> f32 {
        let c = c.min(ch - 1);
        if ctx.interleaved {
            unsafe { *(bufs[0].mData as *const f32).add(i * ch + c) }
        } else {
            unsafe { *(bufs[c.min(nbuf - 1)].mData as *const f32).add(i) }
        }
    };
    let line = &ctx.line;
    let w = line.written.load(Ordering::Relaxed);
    let mut peak = 0.0f32;
    for i in 0..frames {
        let (l, r) = (sample(i, 0), sample(i, 1));
        let slot = ((w + i as u64) % line.cap) as usize * 2;
        line.buf[slot].store(l.to_bits(), Ordering::Relaxed);
        line.buf[slot + 1].store(r.to_bits(), Ordering::Relaxed);
        let m = 0.5 * (l + r);
        peak = peak.max(m.abs());
        let _ = ctx.mono.push(m);
    }
    line.written.store(w + frames as u64, Ordering::Release);
    let host = unsafe { input_time.as_ref() }.mHostTime;
    line.seq.fetch_add(1, Ordering::AcqRel);
    line.anchor_first.store(w, Ordering::Release);
    line.anchor_host.store(host, Ordering::Release);
    line.seq.fetch_add(1, Ordering::AcqRel);
    let _ = ctx.meta.push(Meta {
        first: w,
        frames: frames as u32,
        input_host: host,
        now_host: unsafe { now.as_ref() }.mHostTime,
        peak,
        dropped: 0,
    });
    0
}

struct RenderCtx {
    line: Arc<DelayLine>,
    read: Option<u64>,
}

unsafe extern "C-unwind" fn delay_render(
    client: NonNull<c_void>,
    _flags: NonNull<AudioUnitRenderActionFlags>,
    ts: NonNull<AudioTimeStamp>,
    _bus: u32,
    frames: u32,
    data: *mut AudioBufferList,
) -> i32 {
    let ctx = unsafe { &mut *(client.as_ptr() as *mut UnsafeCell<RenderCtx>) }.get_mut();
    let list = unsafe { &mut *data };
    let bufs = unsafe {
        std::slice::from_raw_parts_mut(list.mBuffers.as_mut_ptr(), list.mNumberBuffers as usize)
    };
    let out =
        unsafe { std::slice::from_raw_parts_mut(bufs[0].mData as *mut f32, frames as usize * 2) };
    out.fill(0.0);
    let line = &ctx.line;
    let Some((first, host)) = line.anchor() else {
        return 0;
    };
    // The frame captured at (this buffer's host time - M).
    let h = unsafe { ts.as_ref() }.mHostTime as f64 * line.tick_s;
    let want = first as f64 + (h - line.m - host) * line.rate;
    if want < 0.0 {
        return 0;
    }
    let r = match ctx.read {
        None => want.round() as u64,
        Some(r) => {
            let err = want - r as f64;
            line.max_err
                .fetch_max((err.abs() * 1e6) as u64, Ordering::Relaxed);
            // Same device clock: correct only a drift past 2 frames, by skipping or repeating.
            if err.abs() > 2.0 {
                line.slips.fetch_add(1, Ordering::Relaxed);
                want.round() as u64
            } else {
                r
            }
        }
    };
    let written = line.written.load(Ordering::Acquire);
    if r + frames as u64 > written || written - r > line.cap - frames as u64 {
        line.underruns.fetch_add(1, Ordering::Relaxed);
        ctx.read = Some(r + frames as u64);
        return 0;
    }
    for i in 0..frames as usize {
        let slot = ((r + i as u64) % line.cap) as usize * 2;
        out[2 * i] = f32::from_bits(line.buf[slot].load(Ordering::Relaxed));
        out[2 * i + 1] = f32::from_bits(line.buf[slot + 1].load(Ordering::Relaxed));
    }
    ctx.read = Some(r + frames as u64);
    0
}

fn start_output(ctx: *mut c_void, rate: f64) -> Result<AudioUnit, String> {
    let desc = AudioComponentDescription {
        componentType: kAudioUnitType_Output,
        componentSubType: kAudioUnitSubType_DefaultOutput,
        componentManufacturer: kAudioUnitManufacturer_Apple,
        componentFlags: 0,
        componentFlagsMask: 0,
    };
    let comp = unsafe { AudioComponentFindNext(std::ptr::null_mut(), NonNull::from(&desc)) };
    if comp.is_null() {
        return Err("no default output".into());
    }
    let mut unit: AudioUnit = std::ptr::null_mut();
    check(
        unsafe { AudioComponentInstanceNew(comp, NonNull::from(&mut unit)) },
        "AudioComponentInstanceNew",
    )?;
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
    let cb = AURenderCallbackStruct {
        inputProc: Some(delay_render),
        inputProcRefCon: ctx,
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
    Ok(unit)
}

/// Metas with absolute frame indices, placed on a chunk starting at absolute frame `start`
/// (a meta that began before it is moved to the chunk's first frame).
fn rebase(metas: &[Meta], start: u64, rate: f64) -> Vec<Meta> {
    let tick = coreaudio::tick_s();
    metas
        .iter()
        .filter(|x| x.first + x.frames as u64 > start)
        .map(|x| {
            let skip = start.saturating_sub(x.first);
            Meta {
                first: x.first + skip - start,
                input_host: x.input_host + (skip as f64 / rate / tick) as u64,
                ..*x
            }
        })
        .collect()
}

/// Onset host times of 1 kHz clicks in a chunk, at `floor` of the chunk's peak.
fn click_times(samples: &[f32], metas: &[Meta], rate: f64, floor: f32) -> (Vec<f64>, f32) {
    let env = envelope(samples, rate, TONE);
    let peak = env.iter().cloned().fold(0.0f32, f32::max);
    let on = onsets(&env, rate, floor * peak);
    (
        on.iter().filter_map(|&i| host_of(metas, rate, i)).collect(),
        peak,
    )
}

/// afplay's clicks, tapped and muted, played `m` s late through AUHAL for `minutes`; the delay
/// (microphone onset - tap onset) is measured every minute.
pub fn delay_test(log: &Path, m: f64, minutes: f64) {
    // A global tap hears the live (pre-mute) clicks too, at multiples of the period after each
    // click: one within 0.35 s of M would be paired, or would hide the late copy from the onset
    // detector's 0.3 s gap.
    if std::env::var("PS_LISTEN").as_deref() == Ok("tap") {
        let k = (m / CLICK_S).round();
        if (m - k * CLICK_S).abs() < 0.35 {
            println!("M = {m} s puts a live click within 0.35 s of the late copy; pick another M");
            return;
        }
    }
    let secs = minutes * 60.0 + 10.0;
    let path = log.join("delay-clicks.wav");
    click_wav(&path, secs);
    let (mut mic, _observer) = listener();
    let mut player = afplay(&path, 0.5);
    let object = object_of_pid(player.id() as i32).expect("afplay's process object");
    let (tap_id, agg, fmt) = create_process_tap(&[object]).expect("tap");
    let out_rate = coreaudio::device(coreaudio::default_output()).rate;
    println!(
        "tap {} Hz {} ch, output {} Hz, M = {m} s",
        fmt.mSampleRate, fmt.mChannelsPerFrame, out_rate
    );
    if (fmt.mSampleRate - out_rate).abs() > 0.5 {
        println!("rates differ: this spike plays without resampling, so it stops here");
        let _ = player.kill();
        let _ = player.wait();
        return;
    }
    let rate = fmt.mSampleRate;
    let cap = ((m + 2.0) * rate) as u64;
    let line = Arc::new(DelayLine {
        buf: (0..cap * 2).map(|_| AtomicU32::new(0)).collect(),
        cap,
        written: AtomicU64::new(0),
        seq: AtomicU64::new(0),
        anchor_first: AtomicU64::new(0),
        anchor_host: AtomicU64::new(0),
        rate,
        m,
        tick_s: coreaudio::tick_s(),
        underruns: AtomicU64::new(0),
        slips: AtomicU64::new(0),
        max_err: AtomicU64::new(0),
    });
    let (mp, mut mc) = rtrb::RingBuffer::new((rate * 30.0) as usize);
    let (metp, mut metc) = rtrb::RingBuffer::new(1 << 16);
    let tctx = Box::new(UnsafeCell::new(TapCtx {
        line: line.clone(),
        channels: fmt.mChannelsPerFrame as usize,
        interleaved: fmt.mFormatFlags & kAudioFormatFlagIsNonInterleaved == 0,
        mono: mp,
        meta: metp,
    }));
    let mut proc_id: AudioDeviceIOProcID = None;
    check(
        unsafe {
            AudioDeviceCreateIOProcID(
                agg,
                Some(delay_io),
                &*tctx as *const UnsafeCell<TapCtx> as *mut c_void,
                NonNull::from(&mut proc_id),
            )
        },
        "AudioDeviceCreateIOProcID",
    )
    .unwrap();
    check(
        unsafe { AudioDeviceStart(agg, proc_id) },
        "AudioDeviceStart",
    )
    .unwrap();
    let rctx = Box::new(UnsafeCell::new(RenderCtx {
        line: line.clone(),
        read: None,
    }));
    let unit =
        start_output(&*rctx as *const UnsafeCell<RenderCtx> as *mut c_void, rate).expect("output");

    let mut jl = std::fs::File::create(log.join("delay.jsonl")).unwrap();
    let start = now_s();
    let (mut ts, mut tm, mut ms, mut mm) = (Vec::new(), Vec::new(), Vec::new(), Vec::new());
    let (mut tap_on, mut mic_on): (Vec<f64>, Vec<f64>) = (Vec::new(), Vec::new());
    let mut paired = 0usize;
    let mut minute_medians = Vec::new();
    let mut all = Vec::new();
    let mut next = start + 60.0;
    // The absolute index of each chunk's first sample, in the tap's and the listener's frames.
    let (mut t_start, mut m_start) = (0u64, 0u64);
    loop {
        let done = now_s() - start > minutes * 60.0 + m + 1.0;
        while let Ok(x) = mc.pop() {
            ts.push(x);
        }
        while let Ok(x) = metc.pop() {
            tm.push(x);
        }
        mic.drain(&mut ms, &mut mm);
        if now_s() >= next || done {
            // Metas index each capture's frames absolutely; place them on this chunk.
            tap_on.extend(click_times(&ts, &rebase(&tm, t_start, rate), rate, 0.25).0);
            let (on, peak) = click_times(&ms, &rebase(&mm, m_start, mic.rate), mic.rate, 0.25);
            mic_on.extend(on);
            t_start += ts.len() as u64;
            m_start += ms.len() as u64;
            ts.clear();
            ms.clear();
            // (Keep the latest metas: the next chunk's first samples may belong to them.)
            tm.drain(..tm.len().saturating_sub(16));
            mm.drain(..mm.len().saturating_sub(16));
            // Pair every tap click old enough for its delayed copy to have been heard.
            let horizon = now_s() - m - 0.5;
            let mut d = Vec::new();
            while paired < tap_on.len() && tap_on[paired] < horizon {
                let s = tap_on[paired];
                if let Some(&h) = mic_on
                    .iter()
                    .find(|&&h| h > s + m - 0.05 && h < s + m + 0.3)
                {
                    d.push((h - s) * 1e3);
                }
                paired += 1;
            }
            let st = summarize(&d);
            let minute = ((now_s() - start) / 60.0).round();
            let err = line.max_err.swap(0, Ordering::Relaxed) as f64 / 1e6;
            let row = serde_json::json!({
                "minute": minute,
                "pairs": st.n,
                "medianMs": st.median,
                "minMs": d.iter().cloned().fold(f64::INFINITY, f64::min),
                "maxMs": st.max,
                "micPeak": peak,
                "underruns": line.underruns.load(Ordering::Relaxed),
                "slips": line.slips.load(Ordering::Relaxed),
                "maxReadErrFrames": err,
            });
            println!("{row}");
            writeln!(jl, "{row}").unwrap();
            if st.n > 0 {
                minute_medians.push(st.median);
            }
            all.extend(d);
            next += 60.0;
        }
        if done {
            break;
        }
        std::thread::sleep(Duration::from_millis(20));
    }
    unsafe {
        AudioOutputUnitStop(unit);
        AudioComponentInstanceDispose(unit);
        AudioDeviceStop(agg, proc_id);
        AudioDeviceDestroyIOProcID(agg, proc_id);
        AudioHardwareDestroyAggregateDevice(agg);
        AudioHardwareDestroyProcessTap(tap_id);
    }
    let _ = player.kill();
    let _ = player.wait();
    let lo = minute_medians.iter().cloned().fold(f64::INFINITY, f64::min);
    let hi = minute_medians
        .iter()
        .cloned()
        .fold(f64::NEG_INFINITY, f64::max);
    println!("all clicks, delay ms: {}", summarize(&all));
    println!(
        "per-minute medians: {} minutes, {lo:.2} .. {hi:.2} ms (spread {:.2} ms); first {:.2}, last {:.2}",
        minute_medians.len(),
        hi - lo,
        minute_medians.first().copied().unwrap_or(f64::NAN),
        minute_medians.last().copied().unwrap_or(f64::NAN)
    );
    println!(
        "underruns {}, slips {}",
        line.underruns.load(Ordering::Relaxed),
        line.slips.load(Ordering::Relaxed)
    );
}
