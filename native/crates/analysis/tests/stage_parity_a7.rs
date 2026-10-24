//! Stage parity, A7 (docs/design/native-app.md, "Stage parity"): bands, brightness, onset bands
//! and timbre against Python's (`stream.build_stream` on the golden excerpts' PCM,
//! `tests/fixtures/a7-bands.json`), fed Python's tick and onset frames.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use analysis::analyzer::{Analyzer, Entry, Params};
use analysis::bands::{BANDS, Bands, HOP, Timbre};
use analysis::tensors::Tensors;

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn json(path: &Path) -> serde_json::Value {
    serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap()
}

fn golden(name: &str) -> Tensors {
    Tensors::load(&root().join(format!("native/models/golden/{name}.bin"))).unwrap()
}

fn fbits(v: &serde_json::Value) -> f64 {
    f64::from_bits(v.as_u64().unwrap())
}

/// numpy computes the spectra in float32 (relative rounding about 6e-8 per bin, a few times that
/// through its FFT), so a band's level differs by about 1e-6 dB and a 0-1 value read over 30 dB or
/// against a running peak by well under 1e-6; brightness and timbre average many frames. 1e-5
/// absolute on 0-1 values leaves a margin of 10 over the worst measured (9e-7).
const TOLERANCE: f64 = 1e-5;

struct Worst {
    ticks: f64,
    brightness: f64,
    timbre: f64,
    bands_differ: usize,
}

fn run(name: &str, want: &serde_json::Value) -> Worst {
    let pcm = golden(name).f32("pcm").unwrap();
    let ticks = want["ticks"].as_array().unwrap();
    let onsets = want["onsets"].as_array().unwrap();
    let timbre = want["timbre"].as_array().unwrap();
    let mut bands = Bands::new();
    bands.push_samples(&pcm);
    let last = pcm.len() / HOP;
    let mut worst = Worst {
        ticks: 0.0,
        brightness: 0.0,
        timbre: 0.0,
        bands_differ: 0,
    };
    // Onsets by frame, interleaved with ticks in frame order, as the analyzer meets them.
    let onset_frames: Vec<(usize, f64, &str)> = onsets
        .iter()
        .map(|o| {
            let k = fbits(&o[0]);
            ((k * 100.0).round() as usize, k, o[1].as_str().unwrap())
        })
        .collect();
    let mut next_onset = 0;
    let mut out = Vec::new();
    for (i, t) in ticks.iter().enumerate() {
        let f = 5 * (i + 1);
        while next_onset < onset_frames.len() && onset_frames[next_onset].0 <= f {
            let (n, k, band) = onset_frames[next_onset];
            bands.advance_to(n.min(last));
            let ours = bands.onset(n, k);
            if ours != band {
                worst.bands_differ += 1;
                println!("{name}: onset at {k} s: {ours} against {band}");
            }
            next_onset += 1;
        }
        bands.advance_to(f.min(last));
        bands.timbre(f.min(last) as f64 / 100.0, &mut out);
        let (b, bright) = bands.tick(f, 5);
        let w: Vec<f64> = t.as_array().unwrap().iter().map(fbits).collect();
        for (x, y) in [b.kick, b.snare, b.hat, b.bass].iter().zip(&w[..4]) {
            worst.ticks = worst.ticks.max((x - y).abs());
        }
        worst.brightness = worst.brightness.max((bright - w[4]).abs());
    }
    bands.advance_to(last);
    bands.timbre(pcm.len() as f64 / 44100.0, &mut out);
    assert_eq!(out.len(), timbre.len(), "{name}: timbre entries");
    for (e, w) in out.iter().zip(timbre) {
        let w: Vec<f64> = w.as_array().unwrap().iter().map(fbits).collect();
        assert_eq!(e.known_at.to_bits(), w[0].to_bits(), "{name}: timbre time");
        let Timbre {
            brightness,
            flatness,
            percussive,
            density,
            ..
        } = *e;
        for (x, y) in [brightness, flatness, percussive].iter().zip(&w[1..4]) {
            worst.timbre = worst.timbre.max((x - y).abs());
        }
        assert_eq!(
            density.to_bits(),
            w[4].to_bits(),
            "{name}: density at {}",
            e.known_at
        );
    }
    worst
}

#[test]
fn bands_brightness_and_timbre_match_python() {
    let fixture = json(&root().join("native/crates/analysis/tests/fixtures/a7-bands.json"));
    for (name, want) in fixture["excerpts"].as_object().unwrap() {
        let w = run(name, want);
        println!(
            "{name}: bands within {:.2e}, brightness {:.2e}, timbre {:.2e}; {} onset bands differ",
            w.ticks, w.brightness, w.timbre, w.bands_differ
        );
        assert!(
            w.ticks <= TOLERANCE && w.brightness <= TOLERANCE && w.timbre <= TOLERANCE,
            "{name}"
        );
        assert_eq!(w.bands_differ, 0, "{name}: onset bands");
    }
    assert_eq!(BANDS, ["kick", "snare", "hat", "bass"]);
}

#[test]
#[ignore = "release: the whole analyzer, decoding hindsight windows (scripts/ci-full native runs it)"]
fn the_analyzer_from_pcm_carries_pythons_bands() {
    // The analyzer's ticks, onsets and timbre entries from PCM in uneven blocks: Python's
    // tick and onset frames are the analyzer's (A3), so the same values within the same tolerance.
    let models = Tensors::load(&root().join("native/models/madmom.bin")).unwrap();
    let fixture = json(&root().join("native/crates/analysis/tests/fixtures/a7-bands.json"));
    for (name, want) in fixture["excerpts"].as_object().unwrap() {
        let mut an = Analyzer::new(&models, Params::default()).unwrap();
        let mut out = Vec::new();
        for block in golden(name).f32("pcm").unwrap().chunks(777) {
            an.push(block, &mut out);
        }
        an.finish(&mut out);
        let ticks: Vec<_> = out
            .iter()
            .filter_map(|e| {
                if let Entry::Tick(t) = e {
                    Some(*t)
                } else {
                    None
                }
            })
            .collect();
        let onsets: Vec<_> = out
            .iter()
            .filter_map(|e| {
                if let Entry::Onset(o) = e {
                    Some(*o)
                } else {
                    None
                }
            })
            .collect();
        let timbre: Vec<_> = out
            .iter()
            .filter_map(|e| {
                if let Entry::Timbre(t) = e {
                    Some(*t)
                } else {
                    None
                }
            })
            .collect();
        let (wt, wo, we) = (
            want["ticks"].as_array().unwrap(),
            want["onsets"].as_array().unwrap(),
            want["timbre"].as_array().unwrap(),
        );
        assert_eq!(
            (ticks.len(), onsets.len(), timbre.len()),
            (wt.len(), wo.len(), we.len()),
            "{name}"
        );
        let mut worst = 0.0f64;
        for (t, w) in ticks.iter().zip(wt) {
            let w: Vec<f64> = w.as_array().unwrap().iter().map(fbits).collect();
            let b = t.bands;
            for (x, y) in [b.kick, b.snare, b.hat, b.bass, t.brightness]
                .iter()
                .zip(&w)
            {
                worst = worst.max((x - y).abs());
            }
        }
        for (o, w) in onsets.iter().zip(wo) {
            assert_eq!(o.known_at.to_bits(), fbits(&w[0]).to_bits(), "{name}");
            assert_eq!(
                o.band,
                w[1].as_str().unwrap(),
                "{name}: onset at {}",
                o.known_at
            );
        }
        for (e, w) in timbre.iter().zip(we) {
            let w: Vec<f64> = w.as_array().unwrap().iter().map(fbits).collect();
            assert_eq!(e.known_at.to_bits(), w[0].to_bits());
            for (x, y) in [e.brightness, e.flatness, e.percussive]
                .iter()
                .zip(&w[1..4])
            {
                worst = worst.max((x - y).abs());
            }
            assert_eq!(e.density.to_bits(), w[4].to_bits(), "{name}: density");
        }
        assert!(worst <= TOLERANCE, "{name}: {worst}");
        println!(
            "{name}: analyzer's bands, brightness and timbre within {worst:.2e}; onset bands equal"
        );
    }
}
