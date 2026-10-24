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

/// When an entry is known: its `knownAt`, or a tick's time.
fn known(e: &Entry) -> f64 {
    match e {
        Entry::Beat(c) => c.known_at,
        Entry::Tick(t) => t.t,
        Entry::Onset(o) => o.known_at,
        Entry::Hindsight(h) => h.known_at,
        Entry::Change(c) => c.known_at,
        Entry::Jump(j) => j.known_at,
        Entry::End(e) => e.known_at,
        Entry::Forecast(f) => f.known_at,
        Entry::Timbre(t) => t.known_at,
    }
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
                .filter(|e| group(e) == *g && known(e) <= x - margin + 1e-9)
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
#[ignore = "release: re-analyses 30 s at eight cuts, decoding hindsight windows (scripts/ci-full native runs it)"]
fn the_stream_is_causal_in_every_group() {
    let samples = pcm("orpheus-galop-hindsight");
    let cuts = [
        (3.0, MARGIN_S),
        (6.37, MARGIN_S),
        (10.91, MARGIN_S),
        (18.4, MARGIN_S),
        (26.2, MARGIN_S),
        (4.123_457, 0.0),
        (9.005_013, 0.0),
        (22.5, 0.0),
    ];
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

/// A5's synthetic piece (`tests/fixtures/a5-structure.json`'s sections): saw chords that change,
/// a loud stretch and near silence, so changes, jumps and an end occur.
fn sections_piece() -> Vec<f32> {
    let fixture: serde_json::Value = serde_json::from_str(
        &std::fs::read_to_string(
            root().join("native/crates/analysis/tests/fixtures/a5-structure.json"),
        )
        .unwrap(),
    )
    .unwrap();
    let sections = fixture["sections"].as_array().unwrap();
    let total = sections.last().unwrap()[1].as_u64().unwrap() as usize * 44100;
    let mut y = vec![0.0f32; total];
    for s in sections {
        let (from, to) = (
            s[0].as_u64().unwrap() as usize * 44100,
            s[1].as_u64().unwrap() as usize * 44100,
        );
        let amp = s[2].as_f64().unwrap();
        let periods: Vec<usize> = s[3]
            .as_array()
            .unwrap()
            .iter()
            .map(|p| p.as_u64().unwrap() as usize)
            .collect();
        for (n, v) in y.iter_mut().enumerate().take(to).skip(from) {
            let mut x = 0.0f64;
            for &p in &periods {
                x += amp * ((n % p) as f64 / p as f64 * 2.0 - 1.0);
            }
            *v = x as f32;
        }
    }
    y
}

/// A6's accented pulse (every 4th beat at 120 bpm loud), so forecasts occur.
fn accented_pulse(seconds: usize) -> Vec<f32> {
    (0..seconds * 44100)
        .map(|n| {
            let k = n / 22050;
            let amp = if k % 4 == 0 { 0.8 } else { 0.2 };
            let env = (1.0 - (n - k * 22050) as f64 / 2205.0).max(0.0);
            let saw = (n % 100) as f64 / 100.0 * 2.0 - 1.0;
            (amp * env * saw) as f32
        })
        .collect()
}

#[test]
#[ignore = "release: re-analyses synthetic pieces at several cuts (scripts/ci-full native runs it)"]
fn structure_and_forecasts_are_causal() {
    // Cuts after each structure event is known (changes about 18 s and 26 s, jumps 20.8 s and
    // 26.2 s, the end 27.55 s) and between them, and through the pulse's forecasts.
    let seen = check(
        "sections",
        &sections_piece(),
        &[
            (19.0, MARGIN_S),
            (26.1, MARGIN_S),
            (27.7, 0.0),
            (31.37, MARGIN_S),
        ],
    );
    for k in [4, 5, 6] {
        assert!(seen[k] > 0, "{}: none", GROUPS[k]);
    }
    let pulse = check(
        "pulse",
        &accented_pulse(40),
        &[(24.0, MARGIN_S), (33.333_331, 0.0), (39.0, MARGIN_S)],
    );
    assert!(pulse[7] > 0, "forecasts: none");
    println!(
        "sections: {} changes, {} jumps, {} ends; pulse: {} forecasts",
        seen[4], seen[5], seen[6], pulse[7]
    );
}
