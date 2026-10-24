//! Phase 0 spike: capture, live beat tracking and the latency chain.
//! Throwaway quality; see docs/design/native-app.md, "Phase 0: The Spike".
#![allow(
    dead_code,
    deprecated,
    clippy::chunks_exact_to_as_chunks,
    clippy::explicit_counter_loop
)]

mod analyzer;
mod app;
mod coreaudio;
mod dbn;
mod frontend;
mod loopback;
mod lstm;
mod models;
mod output;
mod stats;
mod tap;

use std::time::Instant;

use models::{Tensors, models_dir};

fn main() {
    let args: Vec<String> = std::env::args().collect();
    match args.get(1).map(String::as_str) {
        Some("check") => {
            if !check() {
                std::process::exit(1);
            }
        }
        Some("devices") => coreaudio::print_devices(),
        Some("loopback") => loopback::run(
            args.get(2).and_then(|s| s.parse().ok()).unwrap_or(8.0),
            std::path::Path::new(args.get(3).map(String::as_str).unwrap_or("/tmp")),
            args.iter().any(|a| a == "--include-self"),
        ),
        Some("app") => app::run(
            args.get(2).and_then(|s| s.parse().ok()).unwrap_or(30.0),
            std::path::Path::new(args.get(3).map(String::as_str).unwrap_or("/tmp")),
            args.iter().any(|a| a == "--mute"),
        ),
        Some("yaml") => yaml(&args[2..]),
        Some("tap") => tap_test(&args[2..]),
        Some("bench") => bench(args.get(2).and_then(|s| s.parse().ok()).unwrap_or(300)),
        _ => {
            eprintln!("usage: spike check | bench [decodes]");
            std::process::exit(2);
        }
    }
}

fn max_diff(a: &[f32], b: &[f32]) -> (f32, f32) {
    assert_eq!(a.len(), b.len(), "lengths");
    let mut abs = 0.0f32;
    let mut rel = 0.0f32;
    for (x, y) in a.iter().zip(b) {
        let d = (x - y).abs();
        abs = abs.max(d);
        rel = rel.max(d / y.abs().max(1e-3));
    }
    (abs, rel)
}

/// Parity against madmom's own outputs on the golden excerpts; true when every stage matches.
fn check() -> bool {
    let tol = 1e-5;
    let mut ok = true;
    let t = Tensors::load(&models_dir().join("models.bin"));
    let g = Tensors::load(&models_dir().join("golden.bin"));

    // Online front end, end to end from PCM.
    let pcm = g.f32("online.pcm");
    let gold_feat = g.f32("online.features");
    let width = g.shape("online.features")[1];
    let mut fe = frontend::OnlineFrontEnd::new(&t);
    let mut feats = Vec::new();
    for block in pcm.chunks(512) {
        fe.push(block, |_, f| feats.extend_from_slice(f));
    }
    let n = gold_feat.len() / width;
    let got = feats.len() / width;
    let d = max_diff(
        &feats[..n.min(got) * width],
        &gold_feat[..n.min(got) * width],
    );
    ok &= got == n && d.0 < tol;
    println!("online features: {got} frames (madmom {n}); diff {d:?}");

    // Online nets on madmom's features.
    let gold_act = g.f32("online.activations");
    let mut nn = lstm::OnlineEnsemble::load(&t);
    let act: Vec<f32> = gold_feat.chunks(width).map(|f| nn.step(f)).collect();
    let d = max_diff(&act, &gold_act);
    ok &= d.0 < tol;
    println!("online activations (madmom features in): diff {d:?}");

    // End to end activations.
    let mut an = analyzer::Analyzer::new(&t);
    let mut beats = Vec::new();
    an.push(&pcm, &mut beats);
    let k = an.activations.len().min(gold_act.len());
    let d = max_diff(&an.activations[..k], &gold_act[..k]);
    ok &= d.0 < tol;
    println!("online activations (end to end): diff {d:?}");

    // Forward states and commits on madmom's activations.
    let gold_states = g.i32("online.states");
    let mut tr = dbn::Tracker::new(dbn::Hmm::load(&t, "dbn_online"));
    let mut committed = Vec::new();
    let states: Vec<usize> = gold_act
        .iter()
        .map(|&a| tr.step(a, &mut committed))
        .collect();
    let mismatched = states
        .iter()
        .zip(&gold_states)
        .filter(|(a, b)| **a != **b as usize)
        .count();
    ok &= mismatched == 0;
    println!("forward states: {mismatched} of {} differ", states.len());
    let gt = g.f32("online.commit_t");
    let gk = g.f32("online.commit_known");
    println!("committed beats: {} (python {})", committed.len(), gt.len());
    let mut worst = 0.0f64;
    let mut known_diff = 0;
    for (b, (t, k)) in committed.iter().zip(gt.iter().zip(&gk)) {
        worst = worst.max((b.t - *t as f64).abs());
        if (b.known_at - *k as f64).abs() > 1e-4 {
            known_diff += 1;
        }
    }
    ok &= committed.len() == gt.len() && worst < 1e-5 && known_diff == 0;
    println!("  max |t diff| {worst:.2e} s, knownAt differing: {known_diff}");

    // Offline.
    let opcm = g.f32("offline.pcm");
    let ofeat = g.f32("offline.features");
    let owidth = g.shape("offline.features")[1];
    let mut h = analyzer::Hindsight::new(&t);
    let (on, f) = h.features(&opcm);
    let d = max_diff(&f, &ofeat);
    ok &= d.0 < tol;
    println!("offline features: {on} frames; diff {d:?}");
    let oact = h.activations(&ofeat, ofeat.len() / owidth);
    let gold_oact = g.f32("offline.activations");
    let d = max_diff(&oact, &gold_oact);
    ok &= d.0 < tol;
    println!("offline activations (madmom features in): diff {d:?}");
    let hmm = dbn::Hmm::load(&t, "dbn_offline");
    let ob = hmm.offline_beats(&gold_oact);
    let gob = g.f32("offline.beats");
    let same = ob.len() == gob.len()
        && ob
            .iter()
            .zip(&gob)
            .all(|(a, b)| (a - *b as f64).abs() < 1e-4);
    ok &= same;
    println!(
        "offline beats: {} (madmom {}), identical: {same}",
        ob.len(),
        gob.len()
    );
    let d = h.decode(&opcm);
    let same = d.beats.len() == gob.len()
        && d.beats
            .iter()
            .zip(&gob)
            .all(|(a, b)| (a - *b as f64).abs() < 1e-4);
    ok &= same;
    println!(
        "offline beats end to end: {} identical: {same}",
        d.beats.len()
    );
    ok
}

/// Real-time factors: the live analyzer over a long signal in 512-sample blocks,
/// and repeated 20 s hindsight decodes.
fn bench(decodes: usize) {
    let t = Tensors::load(&models_dir().join("models.bin"));
    let g = Tensors::load(&models_dir().join("golden.bin"));
    let pcm = g.f32("online.pcm");
    let reps = 10;
    let mut an = analyzer::Analyzer::new(&t);
    let mut beats = Vec::new();
    let mut block_ms = Vec::new();
    let t0 = Instant::now();
    for _ in 0..reps {
        for block in pcm.chunks(512) {
            let s = Instant::now();
            an.push(block, &mut beats);
            block_ms.push(s.elapsed().as_secs_f64() * 1e3);
        }
    }
    let audio_s = reps as f64 * pcm.len() as f64 / 44100.0;
    let el = t0.elapsed().as_secs_f64();
    println!(
        "live analyzer: {audio_s:.0} s of audio in {el:.3} s, real-time factor {:.4}",
        el / audio_s
    );
    println!(
        "  per 512-sample block (ms): {}",
        stats::summarize(&block_ms)
    );

    let opcm = g.f32("offline.pcm");
    let mut h = analyzer::Hindsight::new(&t);
    let mut total = Vec::new();
    let (mut fe, mut nets, mut vit) = (Vec::new(), Vec::new(), Vec::new());
    for _ in 0..decodes {
        let s = Instant::now();
        let d = h.decode(&opcm);
        total.push(s.elapsed().as_secs_f64() * 1e3);
        fe.push(d.front_end_s * 1e3);
        nets.push(d.nets_s * 1e3);
        vit.push(d.viterbi_s * 1e3);
    }
    println!(
        "hindsight decode of 20 s (ms): {}",
        stats::summarize(&total)
    );
    println!("  front end: {}", stats::summarize(&fe));
    println!("  nets:      {}", stats::summarize(&nets));
    println!("  viterbi:   {}", stats::summarize(&vit));
    let s = stats::summarize(&total);
    println!(
        "  p99.9 = {:.1} ms; hindsight real-time factor (median / 1 s period) {:.4}",
        s.p999,
        s.median / 1000.0
    );
}

/// Captures for a while and reports buffer timing and levels.
/// `spike tap [seconds] [--include-self] [--click]`
fn tap_test(args: &[String]) {
    let secs: f64 = args.first().and_then(|s| s.parse().ok()).unwrap_or(10.0);
    let include_self = args.iter().any(|a| a == "--include-self");
    // Output first: our process object exists only once we are an audio client.
    let mut out = if args.iter().any(|a| a == "--click") {
        let o = output::Output::start().expect("output");
        println!("clicking every 250 ms through AUHAL at {} Hz", o.rate);
        Some(o)
    } else {
        None
    };
    println!("own process object: {:?}", tap::own_process_object());
    let mut t = match tap::Tap::start(!include_self) {
        Ok(t) => t,
        Err(e) => {
            println!("tap failed: {e}");
            std::process::exit(1);
        }
    };
    println!(
        "tap {} aggregate {} rate {} channels {} (excluding self: {})",
        t.tap, t.aggregate, t.rate, t.channels, !include_self
    );
    let start = coreaudio::now_s();
    let mut next_click = start + 0.5;
    let mut played = 0;
    let mut metas = Vec::new();
    let mut second_peak = 0.0f32;
    let mut next_report = start + 1.0;
    while coreaudio::now_s() - start < secs {
        if let Some(o) = out.as_mut() {
            while next_click < coreaudio::now_s() + 0.1 {
                let _ = o.clicks.push(output::Click {
                    at: next_click,
                    freq: 2000.0,
                });
                next_click += 0.25;
            }
            while let Ok(e) = o.events.pop() {
                if let output::Event::Played { .. } = e {
                    played += 1;
                }
            }
        }
        while let Ok(m) = t.meta.pop() {
            second_peak = second_peak.max(m.peak);
            metas.push(m);
        }
        let n = t.samples.slots();
        if n > 0 {
            let c = t.samples.read_chunk(n).unwrap();
            c.commit_all();
        }
        if coreaudio::now_s() >= next_report {
            println!("  t={:.0}s peak {:.4}", next_report - start, second_peak);
            second_peak = 0.0;
            next_report += 1.0;
        }
        std::thread::sleep(std::time::Duration::from_millis(2));
    }
    let frames: Vec<f64> = metas.iter().map(|m| m.frames as f64).collect();
    let intervals: Vec<f64> = metas
        .windows(2)
        .map(|w| coreaudio::host_s(w[1].now_host - w[0].now_host) * 1e3)
        .collect();
    let lag: Vec<f64> = metas
        .iter()
        .map(|m| (coreaudio::host_s(m.now_host) - coreaudio::host_s(m.input_host)) * 1e3)
        .collect();
    println!("buffers: {}", metas.len());
    println!("buffer frames: {}", stats::summarize(&frames));
    println!("buffer interval (ms): {}", stats::summarize(&intervals));
    println!("IOProc now - input time (ms): {}", stats::summarize(&lag));
    if out.is_some() {
        println!("clicks played: {played}");
    }
    let silent = metas.iter().all(|m| m.peak == 0.0);
    println!("all silent: {silent}");
}

/// Parses YAML files with serde-saphyr into JSON values and prints them as one
/// JSON object keyed by path, to compare with the TS loader's `yaml` package.
fn yaml(paths: &[String]) {
    let mut out = serde_json::Map::new();
    for p in paths {
        let text = std::fs::read_to_string(p).unwrap();
        let v: serde_json::Value =
            serde_saphyr::from_str(&text).unwrap_or_else(|e| panic!("{p}: {e}"));
        out.insert(p.clone(), v);
    }
    println!("{}", serde_json::Value::Object(out));
}

#[cfg(test)]
mod tests {
    /// Stage parity on the golden excerpts, when `export_models.py` has run.
    #[test]
    fn parity_with_madmom() {
        if !crate::models::models_dir().join("golden.bin").exists() {
            eprintln!("skipped: run native/spike/export_models.py first");
            return;
        }
        assert!(super::check());
    }
}
