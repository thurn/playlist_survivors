//! The loopback test: built-in speakers into the built-in microphone.
//!
//! Another process (`afplay`) plays 1 kHz clicks, which the tap captures; we
//! play 3 kHz clicks through AUHAL. The microphone hears both. Against the
//! microphone's own timeline:
//!
//! - `mic - tap` (afplay's clicks) = `L_tap + M`
//! - `mic - render` (our clicks)    = `L_out + M`
//!
//! where `M` is the microphone's offset. The difference `L_tap - L_out` is
//! exact; the absolute values take `M` from the input latency Core Audio reports.

use std::time::Duration;

use crate::coreaudio::{self, host_s};
use crate::output::{self, Output};
use crate::stats::summarize;
use crate::tap::{Capture, Meta, Tap};

const PERIOD: f64 = 0.6;
const TAP_FREQ: f32 = 1000.0;
const OUR_FREQ: f32 = 3000.0;

fn click_wav(path: &std::path::Path, rate: u32, secs: f64, freq: f32) {
    let n = (secs * rate as f64) as usize;
    let mut pcm = vec![0i16; n];
    let mut t = 0.3;
    while t + 0.05 < secs {
        let start = (t * rate as f64) as usize;
        for i in 0..(0.03 * rate as f64) as usize {
            let x = i as f32 / rate as f32;
            let s = 0.5 * (-x / 0.006).exp() * (std::f32::consts::TAU * freq * x).sin();
            pcm[start + i] = (s * 32767.0) as i16;
        }
        t += PERIOD;
    }
    let mut b = Vec::with_capacity(44 + 2 * n);
    let data = (2 * n) as u32;
    b.extend_from_slice(b"RIFF");
    b.extend_from_slice(&(36 + data).to_le_bytes());
    b.extend_from_slice(b"WAVEfmt ");
    b.extend_from_slice(&16u32.to_le_bytes());
    b.extend_from_slice(&1u16.to_le_bytes());
    b.extend_from_slice(&1u16.to_le_bytes());
    b.extend_from_slice(&rate.to_le_bytes());
    b.extend_from_slice(&(rate * 2).to_le_bytes());
    b.extend_from_slice(&2u16.to_le_bytes());
    b.extend_from_slice(&16u16.to_le_bytes());
    b.extend_from_slice(b"data");
    b.extend_from_slice(&data.to_le_bytes());
    for s in pcm {
        b.extend_from_slice(&s.to_le_bytes());
    }
    std::fs::write(path, b).unwrap();
}

/// RBJ band-pass (0 dB peak), then a 1 ms envelope.
pub(crate) fn envelope(x: &[f32], rate: f64, freq: f32) -> Vec<f32> {
    let w0 = std::f64::consts::TAU * freq as f64 / rate;
    let q = 4.0;
    let alpha = w0.sin() / (2.0 * q);
    let a0 = 1.0 + alpha;
    let (b0, b2) = (alpha / a0, -alpha / a0);
    let (a1, a2) = (-2.0 * w0.cos() / a0, (1.0 - alpha) / a0);
    let (mut x1, mut x2, mut y1, mut y2) = (0.0, 0.0, 0.0, 0.0);
    let k = (-1.0 / (0.001 * rate)).exp();
    let mut env = 0.0f64;
    x.iter()
        .map(|&v| {
            let v = v as f64;
            let y = b0 * v + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = v;
            y2 = y1;
            y1 = y;
            env = y.abs().max(env * k);
            env as f32
        })
        .collect()
}

/// Onsets (sample indices): each event's first sample above 30% of its own
/// peak, events separated by at least 0.3 s, ignoring events under `floor`.
pub(crate) fn onsets(env: &[f32], rate: f64, floor: f32) -> Vec<usize> {
    let gap = (0.3 * rate) as usize;
    let mut out = Vec::new();
    let mut i = 0;
    while i < env.len() {
        if env[i] > floor {
            let end = (i + (0.05 * rate) as usize).min(env.len());
            let (pk, peak) = env[i..end]
                .iter()
                .enumerate()
                .fold((0, 0.0f32), |a, (j, &v)| if v > a.1 { (j, v) } else { a });
            let pk = i + pk;
            let th = 0.3 * peak;
            let mut s = pk;
            while s > 0 && env[s - 1] > th && pk - s < (0.05 * rate) as usize {
                s -= 1;
            }
            out.push(s);
            i = pk + gap;
        } else {
            i += 1;
        }
    }
    out
}

/// Host time (s) of sample `i` of a capture.
pub(crate) fn host_of(metas: &[Meta], rate: f64, i: usize) -> Option<f64> {
    let i = i as u64;
    let m = metas.iter().rev().find(|m| m.first <= i)?;
    Some(host_s(m.input_host) + (i - m.first) as f64 / rate)
}

/// The detector's own delay on a clean click (filter and threshold).
fn detector_bias(rate: f64, freq: f32) -> f64 {
    let mut x = vec![0.0f32; (0.5 * rate) as usize];
    let at = (0.1 * rate) as usize;
    for i in 0..(0.03 * rate) as usize {
        let t = i as f32 / rate as f32;
        x[at + i] = 0.5 * (-t / 0.006).exp() * (std::f32::consts::TAU * freq * t).sin();
    }
    let env = envelope(&x, rate, freq);
    let o = onsets(&env, rate, 0.01);
    (o[0] as f64 - at as f64) / rate
}

pub fn run(secs: f64, log_dir: &std::path::Path, include_self: bool) {
    let mut out = Output::start().expect("output");
    let mut tap = Tap::start(!include_self).expect("tap");
    let mut mic = match Capture::microphone() {
        Ok(m) => m,
        Err(e) => {
            println!("microphone failed: {e}");
            return;
        }
    };
    let in_dev = coreaudio::device(mic.device);
    let lin = in_dev.input.unwrap();
    let lambda = (lin.device + lin.safety + lin.stream) as f64 / in_dev.rate;
    println!(
        "mic '{}' at {} Hz, reported input latency {:.2} ms (device {} safety {} stream {})",
        in_dev.name,
        mic.rate,
        lambda * 1e3,
        lin.device,
        lin.safety,
        lin.stream
    );
    let reported_out = Output::reported_latency_s();
    println!(
        "reported output latency (no buffer) {:.2} ms",
        reported_out * 1e3
    );

    let wav = log_dir.join("loopback-clicks.wav");
    click_wav(&wav, 48000, secs, TAP_FREQ);
    std::thread::sleep(Duration::from_millis(300));
    let mut child = std::process::Command::new("/usr/bin/afplay")
        .arg("-v")
        .arg("0.6")
        .arg(&wav)
        .spawn()
        .expect("afplay");
    let start = coreaudio::now_s();
    let (mut ts, mut tm, mut ms, mut mm) = (Vec::new(), Vec::new(), Vec::new(), Vec::new());
    let mut played = Vec::new();
    let mut next = start + 0.6 + PERIOD / 2.0;
    while coreaudio::now_s() - start < secs + 0.5 {
        while next < coreaudio::now_s() + 0.1 && next < start + secs {
            let _ = out.clicks.push(output::Click {
                at: next,
                freq: OUR_FREQ,
            });
            next += PERIOD;
        }
        while let Ok(e) = out.events.pop() {
            if let output::Event::Played { actual, .. } = e {
                played.push(actual);
            }
        }
        tap.drain(&mut ts, &mut tm);
        mic.drain(&mut ms, &mut mm);
        std::thread::sleep(Duration::from_millis(5));
    }
    let _ = child.wait();

    let tap_env = envelope(&ts, tap.rate, TAP_FREQ);
    let tap_on = onsets(&tap_env, tap.rate, 0.02);
    let mic_tap_env = envelope(&ms, mic.rate, TAP_FREQ);
    let mic_our_env = envelope(&ms, mic.rate, OUR_FREQ);
    let peak = |e: &[f32]| e.iter().cloned().fold(0.0f32, f32::max);
    println!(
        "levels: tap 1k peak {:.3}, mic 1k peak {:.4}, mic 3k peak {:.4}",
        peak(&tap_env),
        peak(&mic_tap_env),
        peak(&mic_our_env)
    );
    let mic_tap_on = onsets(&mic_tap_env, mic.rate, 0.25 * peak(&mic_tap_env));
    let mic_our_on = onsets(&mic_our_env, mic.rate, 0.25 * peak(&mic_our_env));
    let bias_1k = detector_bias(mic.rate, TAP_FREQ);
    let bias_3k = detector_bias(mic.rate, OUR_FREQ);
    let mic_t: Vec<f64> = mic_tap_on
        .iter()
        .filter_map(|&i| host_of(&mm, mic.rate, i))
        .collect();
    let mic_o: Vec<f64> = mic_our_on
        .iter()
        .filter_map(|&i| host_of(&mm, mic.rate, i))
        .map(|t| t - bias_3k)
        .collect();
    let tap_t: Vec<f64> = tap_on
        .iter()
        .filter_map(|&i| host_of(&tm, tap.rate, i))
        .collect();
    println!(
        "onsets: tap {} (afplay clicks), mic 1k {}, mic 3k {}, our clicks played {}",
        tap_t.len(),
        mic_t.len(),
        mic_o.len(),
        played.len()
    );
    let rel = |v: &[f64]| {
        v.iter()
            .map(|t| format!("{:.3}", t - start))
            .collect::<Vec<_>>()
            .join(" ")
    };
    println!("tap 1k:  {}", rel(&tap_t));
    println!("mic 1k:  {}", rel(&mic_t));
    println!("played:  {}", rel(&played));
    println!("mic 3k:  {}", rel(&mic_o));
    // Pair each source event with the first microphone onset after it (within 300 ms).
    let pair = |src: &[f64], mic: &[f64]| -> Vec<f64> {
        src.iter()
            .filter_map(|&s| {
                mic.iter()
                    .find(|&&m| m > s - 0.05 && m < s + 0.3)
                    .map(|&m| m - s)
            })
            .collect()
    };
    // Both tap and mic 1k onsets went through the same detector: its bias cancels.
    let _ = bias_1k;
    let d_tap: Vec<f64> = pair(&tap_t, &mic_t).iter().map(|d| d * 1e3).collect();
    let d_out: Vec<f64> = pair(&played, &mic_o).iter().map(|d| d * 1e3).collect();
    let st = summarize(&d_tap);
    let so = summarize(&d_out);
    println!("mic - tap    (L_tap + M), ms: {st}");
    println!("mic - render (L_out + M), ms: {so}");
    println!("L_tap - L_out = {:.2} ms (median)", st.median - so.median);
    println!(
        "with M = +reported input latency ({:.2} ms): L_tap = {:.2} ms, L_out = {:.2} ms (reported {:.2})",
        lambda * 1e3,
        st.median - lambda * 1e3,
        so.median - lambda * 1e3,
        reported_out * 1e3
    );
    if include_self {
        // Our own clicks in the tap: the tap's timeline against the render timeline, no microphone.
        let env = envelope(&ts, tap.rate, OUR_FREQ);
        let on: Vec<f64> = onsets(&env, tap.rate, 0.25 * peak(&env))
            .iter()
            .filter_map(|&i| host_of(&tm, tap.rate, i))
            .map(|t| t - bias_3k)
            .collect();
        let d: Vec<f64> = pair(&played, &on).iter().map(|d| d * 1e3).collect();
        println!(
            "tap - render (our clicks in a tap including us), ms: {}",
            summarize(&d)
        );
    }
    // Tap timeline against the IOProc: how long after a sample's tap time it reaches us.
    let lag: Vec<f64> = tm
        .iter()
        .map(|m| (host_s(m.now_host) - host_s(m.input_host)) * 1e3)
        .collect();
    println!("tap IOProc now - input time, ms: {}", summarize(&lag));
}
