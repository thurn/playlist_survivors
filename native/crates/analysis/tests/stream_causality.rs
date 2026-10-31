//! The stream's own causality (docs/design/native-app.md, "The stream's own causality"): the
//! analyzer's stream of audio cut at X holds exactly the full stream's entries known by X less a
//! margin, in every group (Python's `test_stream_is_causal`, extended to the analyzer's every
//! entry). Cuts between frames take no margin, so an entry stamped even a little before the audio
//! it needs would differ. Live it holds by construction; the test catches any normalisation over
//! the whole buffer.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use analysis::analyzer::{Analyzer, Entry, Params};
use analysis::tensors::Tensors;

const MARGIN_S: f64 = 0.05;

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn models() -> Tensors {
    Tensors::load(&root().join("native/models/madmom.bin")).unwrap()
}

fn pcm(name: &str) -> Vec<f32> {
    Tensors::load(&root().join(format!("native/models/golden/{name}.bin")))
        .unwrap()
        .f32("pcm")
        .unwrap()
}

/// The analyzer's whole stream of `samples`, fed in uneven blocks.
fn stream(models: &Tensors, samples: &[f32]) -> Vec<Entry> {
    let mut an = Analyzer::new(models, Params::default()).unwrap();
    let mut out = Vec::new();
    for (k, block) in samples.chunks(1013).enumerate() {
        // (Blocks of 1013 and a few samples, so frames end at every offset within them.)
        let (a, b) = block.split_at(block.len().min(7 + k % 5));
        an.push(a, &mut out);
        an.push(b, &mut out);
    }
    an.finish(&mut out);
    out
}

fn group(e: &Entry) -> &'static str {
    match e {
        Entry::Beat(_) => "beats",
        Entry::Tick(_) => "ticks",
        Entry::Onset(_) => "onsets",
        Entry::Hindsight(_) => "hindsight",
        Entry::Change(_) => "changes",
        Entry::Jump(_) => "jumps",
        Entry::End(_) => "ends",
        Entry::Forecast(_) => "forecasts",
        Entry::Timbre(_) => "timbre",
    }
}

const GROUPS: [&str; 9] = [
    "beats",
    "ticks",
    "onsets",
    "hindsight",
    "changes",
    "jumps",
    "ends",
    "forecasts",
    "timbre",
];

/// Each group's entries known by `x - margin`, in stream order.
fn known_by(entries: &[Entry], x: f64, margin: f64) -> Vec<Vec<&Entry>> {
    GROUPS
        .iter()
        .map(|g| {
            entries
                .iter()
                .filter(|e| group(e) == *g && e.known_at() <= x - margin + 1e-9)
                .collect()
        })
        .collect()
}

/// Asserts the cut streams are the full one's prefix by knownAt; returns each group's count in
/// the full stream known by the last cut (so the test can show it isn't vacuous).
fn check(name: &str, samples: &[f32], cuts: &[(f64, f64)]) -> Vec<usize> {
    let models = models();
    let full = stream(&models, samples);
    let mut seen = vec![0; GROUPS.len()];
    for &(cut, margin) in cuts {
        let part = stream(&models, &samples[..(cut * 44100.0).round() as usize]);
        let (want, got) = (known_by(&full, cut, margin), known_by(&part, cut, margin));
        for (k, (w, g)) in want.iter().zip(&got).enumerate() {
            assert_eq!(
                g.len(),
                w.len(),
                "{name}: {} at {cut}: {} against {}",
                GROUPS[k],
                g.len(),
                w.len()
            );
            for (i, (a, b)) in g.iter().zip(w).enumerate() {
                assert_eq!(a, b, "{name}: {}[{i}] at {cut}", GROUPS[k]);
            }
            seen[k] = seen[k].max(w.len());
        }
    }
    seen
}

#[test]
fn a_short_stream_is_causal() {
    // The gate's smoke: 6 s of the house excerpt, cut on a frame and between frames.
    let samples = pcm("house-128");
    let seen = check(
        "house-128",
        &samples[..6 * 44100],
        &[(2.5, MARGIN_S), (4.123_457, 0.0)],
    );
    assert!(seen[0] > 0 && seen[1] > 0 && seen[2] > 0, "{seen:?}");
}

#[test]
#[ignore = "release: re-analyses 30 s at three cuts, decoding hindsight windows (scripts/ci-full native-tests runs it)"]
fn the_stream_is_causal_in_every_group() {
    let samples = pcm("orpheus-galop-hindsight");
    let cuts = [(6.37, MARGIN_S), (9.005_013, 0.0), (18.4, MARGIN_S)];
    let seen = check("orpheus-galop-hindsight", &samples, &cuts);
    let shown: Vec<String> = GROUPS
        .iter()
        .zip(&seen)
        .map(|(g, n)| format!("{g} {n}"))
        .collect();
    println!("entries known by the last cut: {}", shown.join(", "));
    // Not vacuous: beats, ticks, onsets, hindsight and timbre all occur before the cuts.
    for k in [0, 1, 2, 3, 8] {
        assert!(seen[k] > 0, "{}: none", GROUPS[k]);
    }
}
