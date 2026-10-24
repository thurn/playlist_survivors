//! Stage parity, A6 (docs/design/native-app.md, "Stage parity"): accent forecasts against
//! Python's `accents.forecasts` given Python's upstream, the committed beats and onsets of every
//! stream under `public/reactive` (`tests/fixtures/a6-forecasts.json`), fed in `knownAt` order as
//! the analyzer delivers them.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use analysis::accents::{AccentParams, Accents, Forecast};
use analysis::analyzer::{Analyzer, Entry, Params};
use analysis::beats::Commit;
use analysis::onsets::Onset;
use analysis::tensors::Tensors;

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn json(path: &Path) -> serde_json::Value {
    serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap()
}

fn f(v: &serde_json::Value, key: &str) -> f64 {
    v[key].as_f64().unwrap_or_else(|| panic!("{key} in {v}"))
}

/// The forecasts of a stream's beats and onsets, delivered a frame (`knownAt`) at a time.
fn forecasts(stream: &serde_json::Value) -> Vec<Forecast> {
    enum Input {
        Beat(Commit),
        Onset(Onset),
    }
    let mut inputs: Vec<(f64, Input)> = Vec::new();
    for b in stream["beats"].as_array().unwrap() {
        let c = Commit {
            t: f(b, "t"),
            known_at: f(b, "knownAt"),
            period: f(b, "period"),
            evidence: f(b, "evidence"),
        };
        inputs.push((c.known_at, Input::Beat(c)));
    }
    for o in stream["onsets"].as_array().unwrap() {
        let onset = Onset {
            t: f(o, "t"),
            known_at: f(o, "knownAt"),
            strength: f(o, "strength"),
            strong: o["strong"].as_bool().unwrap(),
        };
        inputs.push((onset.known_at, Input::Onset(onset)));
    }
    // (Stable: each group keeps its stream order; a frame's onsets come before its commits.)
    inputs.sort_by(|a, b| {
        a.0.total_cmp(&b.0)
            .then((matches!(a.1, Input::Beat(_))).cmp(&matches!(b.1, Input::Beat(_))))
    });
    let mut stage = Accents::new(&AccentParams::default());
    let mut out = Vec::new();
    let mut k = 0;
    while k < inputs.len() {
        let now = inputs[k].0;
        while k < inputs.len() && inputs[k].0 == now {
            match &inputs[k].1 {
                Input::Beat(c) => stage.push_beat(c),
                Input::Onset(o) => stage.push_onset(o),
            }
            k += 1;
        }
        let before = out.len();
        stage.take(&mut out);
        for fc in &out[before..] {
            assert!(
                fc.known_at <= now,
                "a forecast known at {} taken at {now}",
                fc.known_at
            );
        }
    }
    out.sort_by(|a, b| a.t.total_cmp(&b.t));
    out
}

fn expected(list: &serde_json::Value) -> Vec<Forecast> {
    list.as_array()
        .unwrap()
        .iter()
        .map(|w| Forecast {
            t: f64::from_bits(w[0].as_u64().unwrap()),
            known_at: f64::from_bits(w[1].as_u64().unwrap()),
            cycle: w[2].as_u64().unwrap() as usize,
            confidence: f64::from_bits(w[3].as_u64().unwrap()),
        })
        .collect()
}

fn bits(v: &[Forecast]) -> Vec<(u64, u64, usize, u64)> {
    v.iter()
        .map(|x| {
            (
                x.t.to_bits(),
                x.known_at.to_bits(),
                x.cycle,
                x.confidence.to_bits(),
            )
        })
        .collect()
}

#[test]
fn params_are_the_streams() {
    let manifest = json(&root().join("native/models/manifest.json"));
    let a = &manifest["streamParams"]["accents"];
    let p = AccentParams::default();
    let cycles: Vec<usize> = a["cycles"]
        .as_array()
        .unwrap()
        .iter()
        .map(|c| c.as_u64().unwrap() as usize)
        .collect();
    assert_eq!(cycles, p.cycles);
    assert_eq!(a["confirm"].as_u64().unwrap() as usize, p.confirm);
    assert_eq!(a["history"].as_u64().unwrap() as usize, p.history);
    assert_eq!(f(a, "windowS"), p.window_s);
    assert_eq!(f(a, "minLeadS"), p.min_lead_s);
}

#[test]
fn forecasts_match_python_on_every_stream() {
    let fixture = json(&root().join("native/crates/analysis/tests/fixtures/a6-forecasts.json"));
    let tracks = fixture["tracks"].as_object().unwrap();
    assert!(tracks.len() >= 60, "{} tracks", tracks.len());
    let (mut total, mut tracks_with) = (0, 0);
    for (track, want) in tracks {
        let stream = json(&root().join(format!("public/reactive/{track}/stream.json")));
        let ours = forecasts(&stream);
        let want = expected(want);
        assert_eq!(
            bits(&ours),
            bits(&want),
            "{track}: {ours:?} against {want:?}"
        );
        total += want.len();
        tracks_with += usize::from(!want.is_empty());
    }
    println!(
        "{} streams: {total} forecasts on {tracks_with} of them, bit-equal",
        tracks.len()
    );
}

#[test]
fn forecasts_match_python_at_the_edges() {
    // Synthetic beats and onsets built for the rule's edges: three cycles completing together
    // (the tie-break), a missing hit breaking runs, onsets exactly at and just past the window,
    // and onsets known late, so the 2-beat cycle misses the least lead and the 4-beat one forecasts.
    let fixture = json(&root().join("native/crates/analysis/tests/fixtures/a6-forecasts.json"));
    for (name, case) in fixture["synthetic"].as_object().unwrap() {
        let want = expected(&case["forecasts"]);
        assert!(!want.is_empty(), "{name}: no forecasts to compare");
        let ours = forecasts(case);
        assert_eq!(
            bits(&ours),
            bits(&want),
            "{name}: {ours:?} against {want:?}"
        );
        println!("{name}: {} forecasts bit-equal", want.len());
    }
}

/// The fixture's accented pulse (`fixture.py`'s `accented`), sample for sample.
fn accented(seconds: usize) -> Vec<f32> {
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
#[ignore = "release: the whole analyzer, decoding hindsight windows (scripts/ci-full native runs it)"]
fn the_analyzer_from_pcm_forecasts_as_python() {
    // Python's whole stream of a 40 s pulse with every 4th beat accented: its phase-checked beats
    // and onsets are the analyzer's to the bit (A3, A4), so its forecasts must be too.
    let models = Tensors::load(&root().join("native/models/madmom.bin")).unwrap();
    let fixture = json(&root().join("native/crates/analysis/tests/fixtures/a6-forecasts.json"));
    let want = expected(&fixture["accented40"]);
    assert!(want.len() > 10, "{} forecasts", want.len());
    let mut an = Analyzer::new(&models, Params::default()).unwrap();
    let mut out = Vec::new();
    for block in accented(40).chunks(1000) {
        an.push(block, &mut out);
    }
    an.finish(&mut out);
    let mut ours: Vec<Forecast> = out
        .iter()
        .filter_map(|e| {
            if let Entry::Forecast(f) = e {
                Some(*f)
            } else {
                None
            }
        })
        .collect();
    ours.sort_by(|a, b| a.t.total_cmp(&b.t));
    assert_eq!(bits(&ours), bits(&want), "{ours:?} against {want:?}");
    println!("accented pulse: {} forecasts bit-equal", want.len());
}
