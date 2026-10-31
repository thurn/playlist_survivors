//! Delayed listening's heard delay, by loopback (docs/design/native-app.md, "Targets": the measured
//! music delay against `M`, gated at ±2 ms over an hour; `hv-t8o8.87`).
//!
//! A child of ours clicks once a second. First, a global tap (which mutes nothing) captures the
//! clicks while the built-in microphone hears them live: the live path's capture-to-microphone
//! time. Then the child is tapped as Delayed listening taps an app (muted), and the delay line
//! plays it `M` late through the speakers: the delayed path's time. Their difference is the
//! music's heard delay, which should be `M`; it is reported each minute.
//!
//! It needs the capture grant, the microphone and the speakers on (an operator's check:
//! `native/evidence/operator-pending.md`).

use std::time::{Duration, Instant};

use capture::Meta;
use capture::clock::host_s;
use capture::tap::{Capture, Tap};

use super::output::Output;

/// A click needs this long of quiet before it (s), and stands this far above the floor.
const REFRACTORY_S: f64 = 0.3;
const OVER_FLOOR: f32 = 8.0;
/// How long the live reference listens (s).
const REFERENCE_S: f64 = 20.0;

/// Finds clicks in a captured stream: where the level first passes a threshold (`floor` or
/// `OVER_FLOOR` times the quiet level, whichever is higher) after `REFRACTORY_S` below it.
pub struct Detector {
    rate: f64,
    floor: f32,
    noise: f32,
    last: f64,
}

impl Detector {
    pub fn new(rate: f64, floor: f32) -> Detector {
        Detector {
            rate,
            floor,
            noise: 0.0,
            last: f64::NEG_INFINITY,
        }
    }

    /// A buffer whose first sample was captured at host time `host`: its clicks' host times.
    pub fn push(&mut self, host: f64, samples: &[f32], out: &mut Vec<f64>) {
        // (The quiet level follows over about a second.)
        let k = (1.0 / self.rate) as f32;
        for (i, &x) in samples.iter().enumerate() {
            let t = host + i as f64 / self.rate;
            let a = x.abs();
            let threshold = self.floor.max(OVER_FLOOR * self.noise);
            if a > threshold {
                if t - self.last > REFRACTORY_S {
                    out.push(t);
                }
                self.last = t;
            } else {
                self.noise += (a - self.noise) * k;
            }
        }
    }
}

/// For each source click, the first heard click from `lo` to `hi` s after it: the delays.
pub fn delays(sources: &[f64], heard: &[f64], lo: f64, hi: f64) -> Vec<f64> {
    sources
        .iter()
        .filter_map(|&s| {
            let i = heard.partition_point(|&h| h < s + lo);
            heard.get(i).filter(|&&h| h <= s + hi).map(|&h| h - s)
        })
        .collect()
}

pub fn median(mut v: Vec<f64>) -> Option<f64> {
    if v.is_empty() {
        return None;
    }
    v.sort_by(f64::total_cmp);
    Some(v[v.len() / 2])
}

/// Drains a capture into a detector.
fn listen(c: &mut Capture, d: &mut Detector, out: &mut Vec<f64>) {
    let (mut samples, mut metas) = (Vec::new(), Vec::<Meta>::new());
    c.drain(&mut samples, &mut metas);
    let mut at = 0usize;
    for m in metas {
        let n = (m.frames as usize).min(samples.len() - at);
        d.push(host_s(m.input_host), &samples[at..at + n], out);
        at += n;
    }
}

/// The measure: `minutes` of Delayed listening at `m` s (the tap's lead over the render
/// timeline `offset`), on the clicking child `processes` (whose `go` starts it), each minute's
/// heard delay printed as a JSON line. Fails when the live clicks aren't heard.
pub fn run(
    minutes: f64,
    m: f64,
    offset: f64,
    processes: &[capture::coreaudio::Id],
    go: impl FnOnce() -> Result<(), String>,
) -> Result<bool, String> {
    let mut mic = Capture::microphone()?;
    let mut mic_clicks = Vec::new();
    let mut mic_d = Detector::new(mic.rate, 0.01);
    // The live path: a global tap, which mutes nothing, and the microphone.
    let (mut live_src, mut live_d) = (Vec::new(), None);
    {
        let mut tap = Tap::start(true)?;
        live_d.get_or_insert_with(|| Detector::new(tap.rate, 0.05));
        go()?;
        let t0 = Instant::now();
        while t0.elapsed() < Duration::from_secs_f64(REFERENCE_S) {
            std::thread::sleep(Duration::from_millis(20));
            listen(&mut tap, live_d.as_mut().expect("made"), &mut live_src);
            listen(&mut mic, &mut mic_d, &mut mic_clicks);
        }
    }
    let base = median(delays(&live_src, &mic_clicks, 0.0, 0.5))
        .filter(|_| live_src.len() >= 5)
        .ok_or_else(|| {
            format!(
                "the live clicks weren't heard ({} captured, {} heard): check the speakers, the \
                 microphone and the capture grant",
                live_src.len(),
                mic_clicks.len()
            )
        })?;
    println!(
        "{}",
        serde_json::json!({"phase": "live", "clicks": live_src.len(), "captureToMicMs": base * 1e3})
    );
    // The delayed path: the child tapped (muted), its capture played `m` late.
    let (mut tap, line) = Tap::start_processes(processes, m - offset, m + 2.0)?;
    let _out = Output::start(None, 1.0, 0.0, Some(line), Vec::new(), false)?;
    let mut src_d = Detector::new(tap.rate, 0.05);
    let (mut src, mut heard) = (Vec::new(), Vec::new());
    let mut medians = Vec::new();
    let mut ok = true;
    let start = Instant::now();
    for minute in 1..=(minutes.ceil() as u32) {
        while start.elapsed() < Duration::from_secs(60 * u64::from(minute)) {
            std::thread::sleep(Duration::from_millis(20));
            listen(&mut tap, &mut src_d, &mut src);
            listen(&mut mic, &mut mic_d, &mut heard);
        }
        // (The last clicks' echoes may still be on their way: they count next minute.)
        let cut = src.partition_point(|&s| s < host_s(capture::clock::now_ticks()) - m - 1.0);
        let ds = delays(&src[..cut], &heard, m - 0.5, m + 0.5);
        src.drain(..cut);
        let n = ds.len();
        let Some(path) = median(ds) else {
            println!("{}", serde_json::json!({"minute": minute, "clicks": 0}));
            ok = false;
            continue;
        };
        let delay = path - base;
        let error_ms = (delay - m) * 1e3;
        ok &= error_ms.abs() <= 2.0;
        medians.push(delay);
        println!(
            "{}",
            serde_json::json!({"minute": minute, "clicks": n, "delayS": delay, "errorMs": error_ms})
        );
    }
    let spread = medians.iter().copied().fold(f64::NEG_INFINITY, f64::max)
        - medians.iter().copied().fold(f64::INFINITY, f64::min);
    println!(
        "{}",
        serde_json::json!({"minutes": medians.len(), "spreadMs": spread * 1e3, "withinTwoMs": ok})
    );
    Ok(ok)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn clicks_are_found_over_the_noise_once_each() {
        let rate = 1000.0;
        // Noise at 0.01, clicks of 20 ms at 0.5 every 0.5 s from 0.2 s.
        let mut x: Vec<f32> = (0..2000)
            .map(|i| if i % 2 == 0 { 0.01 } else { -0.01 })
            .collect();
        for c in [200usize, 700, 1200, 1700] {
            for s in x.iter_mut().skip(c).take(20) {
                *s = 0.5;
            }
        }
        let mut d = Detector::new(rate, 0.02);
        let mut out = Vec::new();
        // (In two buffers, the second starting at its host time.)
        d.push(10.0, &x[..1000], &mut out);
        d.push(11.0, &x[1000..], &mut out);
        let want = [10.2, 10.7, 11.2, 11.7];
        assert_eq!(out.len(), 4, "{out:?}");
        assert!(out.iter().zip(want).all(|(a, b)| (a - b).abs() < 1e-9));
    }

    #[test]
    fn each_source_click_takes_the_first_heard_in_its_window() {
        let src = [1.0, 2.0, 3.0];
        let heard = [1.02, 4.03, 5.04, 6.6];
        // (Delayed by 3 s: the second source's echo is missing.)
        let d = delays(&src, &heard, 2.5, 3.5);
        assert_eq!(d.len(), 2);
        assert!((d[0] - 3.03).abs() < 1e-9 && (d[1] - 3.04).abs() < 1e-9);
        assert_eq!(median(vec![3.0, 1.0, 2.0]), Some(2.0));
        assert_eq!(median(vec![]), None);
    }
}
