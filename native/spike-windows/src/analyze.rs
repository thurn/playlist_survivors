//! Offline analysis of the WAVs: click fits, tone levels, and delays between two recordings by
//! their QPC timestamps.

use std::path::Path;

use serde_json::{Value, json};

use crate::stream::{CLICK, ideal_click};
use crate::wav::{RATE, read_mono, read_packets};
use crate::R;

pub fn db(x: f64) -> f64 {
    if x > 0.0 { 20.0 * x.log10() } else { -999.0 }
}

pub fn stats(v: &[f64]) -> Value {
    if v.is_empty() {
        return json!({"n": 0});
    }
    let mut s = v.to_vec();
    s.sort_by(f64::total_cmp);
    let q = |p: f64| s[((s.len() - 1) as f64 * p).round() as usize];
    json!({"n": s.len(), "min": s[0], "p05": q(0.05), "median": q(0.5), "p95": q(0.95),
           "max": s[s.len() - 1]})
}

/// Click onsets: first sample over 30% of the file's typical click peak, at least 50 ms apart.
/// (The typical peak is the 99.8th percentile of |x|: clicks fill 2% of the track, so a few
/// louder or quieter clicks, as around a route change, don't move it.)
pub fn onsets(x: &[f32]) -> Vec<usize> {
    if x.is_empty() {
        return Vec::new();
    }
    let mut a: Vec<f32> = x.iter().map(|s| s.abs()).collect();
    let k = ((a.len() - 1) as f64 * 0.998) as usize;
    let (_, peak, _) = a.select_nth_unstable_by(k, f32::total_cmp);
    let peak = *peak;
    if peak == 0.0 {
        return Vec::new();
    }
    let thr = 0.3 * peak;
    let mut out = Vec::new();
    let mut i = 0;
    while i < x.len() {
        if x[i].abs() >= thr {
            out.push(i);
            i += 2400;
        } else {
            i += 1;
        }
    }
    out
}

pub fn level(x: &[f32]) -> Value {
    let peak = x.iter().fold(0f32, |m, s| m.max(s.abs())) as f64;
    let rms = (x.iter().map(|s| (*s as f64).powi(2)).sum::<f64>() / x.len().max(1) as f64).sqrt();
    json!({"seconds": x.len() as f64 / RATE as f64, "peak_dbfs": db(peak), "rms_dbfs": db(rms)})
}

/// A sine's amplitude at `f` Hz over the whole signal (Goertzel), in dBFS.
pub fn tone(x: &[f32], f: f64) -> f64 {
    let w = 2.0 * std::f64::consts::PI * f / RATE as f64;
    let c = 2.0 * w.cos();
    let (mut s1, mut s2) = (0f64, 0f64);
    for &v in x {
        let s0 = v as f64 + c * s1 - s2;
        s2 = s1;
        s1 = s0;
    }
    let p = s1 * s1 + s2 * s2 - c * s1 * s2;
    db(2.0 * p.max(0.0).sqrt() / x.len().max(1) as f64)
}

/// Fits each click to the ideal one (best integer lag, least-squares gain): its level against
/// the source's (amplitude `amp`) and the residual after the fit.
pub fn clicks(x: &[f32], amp: f64) -> Value {
    let c = ideal_click();
    let cc: f64 = c.iter().map(|v| (*v as f64).powi(2)).sum();
    let n = CLICK as usize;
    let mut gains = Vec::new();
    let mut resid = Vec::new();
    let mut starts = Vec::new();
    for o in onsets(x) {
        let mut best: Option<(f64, f64, usize)> = None;
        for l in o.saturating_sub(16)..=o + 4 {
            if l + n > x.len() {
                break;
            }
            let w = &x[l..l + n];
            let xc: f64 = w.iter().zip(&c).map(|(a, b)| *a as f64 * *b as f64).sum();
            let g = xc / cc;
            let e: f64 = w.iter().zip(&c).map(|(a, b)| (*a as f64 - g * *b as f64).powi(2)).sum();
            let r = e / (g * g * cc);
            if best.is_none_or(|b| r < b.1) {
                best = Some((g, r, l));
            }
        }
        if let Some((g, r, l)) = best {
            gains.push(db(g / amp));
            resid.push(10.0 * r.max(1e-30).log10());
            starts.push(l);
        }
    }
    // The noise between clicks: everything outside [start - 10 ms, start + 30 ms].
    let mut quiet = vec![true; x.len()];
    for &s in &starts {
        for q in &mut quiet[s.saturating_sub(480)..(s + 1440).min(x.len())] {
            *q = false;
        }
    }
    let (sum, cnt) = x
        .iter()
        .zip(&quiet)
        .filter(|(_, q)| **q)
        .fold((0f64, 0usize), |(s, c), (v, _)| (s + (*v as f64).powi(2), c + 1));
    let spacing: Vec<f64> =
        starts.windows(2).map(|w| (w[1] - w[0]) as f64 / RATE as f64 * 1000.0).collect();
    json!({"clicks": starts.len(), "level_db_vs_source": stats(&gains),
           "residual_db": stats(&resid), "between_clicks_rms_dbfs": db((sum / cnt.max(1) as f64).sqrt()),
           "spacing_ms": stats(&spacing)})
}

/// Onset times (QPC s) of a WAV, by its sidecar's packet timestamps.
fn onset_times(path: &Path) -> R<(Vec<f64>, usize)> {
    let x = read_mono(path)?;
    let packets = read_packets(path)?;
    let zero = packets.iter().filter(|p| p.1 == 0).count();
    let times = onsets(&x)
        .into_iter()
        .filter_map(|o| {
            let i = packets.partition_point(|p| p.0 <= o as u64).checked_sub(1)?;
            let (f, q) = packets[i];
            (q != 0).then(|| q as f64 * 1e-7 + (o as u64 - f) as f64 / RATE as f64)
        })
        .collect();
    Ok((times, zero))
}

/// The delay from each click in `a` to its copy in `b`, `m` s later: its spread, and its drift
/// (least-squares slope against time). Clicks in `b` that match `a` at lag 0 instead are counted
/// as live (undelayed) clicks.
pub fn lag(a: &Path, b: &Path, m: f64, series: bool) -> R<Value> {
    let (ta, za) = onset_times(a)?;
    let (tb, zb) = onset_times(b)?;
    let mut delays = Vec::new();
    let mut at = Vec::new();
    let mut live = 0;
    let mut unmatched = 0;
    for &t in &tb {
        let near = |target: f64| {
            ta.iter().map(|x| t - x - target).min_by(|p, q| p.abs().total_cmp(&q.abs()))
        };
        let (Some(dm), Some(d0)) = (near(m), near(0.0)) else {
            continue;
        };
        if d0.abs() < 0.02 && d0.abs() < dm.abs() {
            live += 1;
        } else if dm.abs() < 0.25 {
            delays.push((dm + m) * 1000.0);
            at.push(t);
        } else {
            unmatched += 1;
        }
    }
    let slope = if at.len() > 2 {
        let n = at.len() as f64;
        let mx = at.iter().sum::<f64>() / n;
        let my = delays.iter().sum::<f64>() / n;
        let sxy: f64 = at.iter().zip(&delays).map(|(x, y)| (x - mx) * (y - my)).sum();
        let sxx: f64 = at.iter().map(|x| (x - mx).powi(2)).sum();
        sxy / sxx * 60.0
    } else {
        f64::NAN
    };
    let mut v = json!({"a_clicks": ta.len(), "b_clicks": tb.len(), "a_packets_without_qpc": za,
              "b_packets_without_qpc": zb, "delayed": delays.len(), "live": live,
              "unmatched": unmatched, "delay_ms": stats(&delays),
              "drift_ms_per_minute": slope});
    if series {
        let t0 = at.first().copied().unwrap_or(0.0);
        v["series"] = at.iter().zip(&delays).map(|(t, d)| json!([((t - t0) * 1000.0).round() / 1000.0, (d * 1000.0).round() / 1000.0])).collect();
    }
    Ok(v)
}
