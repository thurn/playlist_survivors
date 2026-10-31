//! Stage parity, A3 (docs/design/native-app.md, "Stage parity"): ticks (evidence, energy,
//! level) and onsets (the superflux function, its online peak picking, strengths and strong
//! flags) against Python's golden data (`native/models/golden/`), each stage fed Python's own
//! upstream; then the whole analyzer's independence of block sizes.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use analysis::analyzer::{Analyzer, Params};
use analysis::beats::{BeatParams, Beliefs, Evidence};
use analysis::dbn::OnlineDbn;
use analysis::frontend::{HOP, OnlineFrontEnd};
use analysis::onsets::{Onset, OnsetParams, PeakPicker, Strengths, Superflux};
use analysis::tensors::Tensors;
use analysis::ticks::{Energy, Levels, Tick, TickParams};

fn models_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../models")
}

fn models() -> Tensors {
    Tensors::load(&models_dir().join("madmom.bin")).expect("madmom.bin")
}

fn golden(excerpt: &str) -> Tensors {
    Tensors::load(&models_dir().join(format!("golden/{excerpt}.bin"))).expect("golden data")
}

const EXCERPTS: [&str; 2] = ["house-128", "orpheus-galop"];

fn bits(v: impl IntoIterator<Item = f64>) -> Vec<u64> {
    v.into_iter().map(f64::to_bits).collect()
}

/// About 1e-5 relative, with an absolute floor of 1e-7 (the plan's stage tolerance).
fn tolerance(python: f32) -> f32 {
    (1e-5 * python.abs()).max(1e-7)
}

/// The ticks from Python's activations and PCM: the evidence from the DBN's beliefs, the energy
/// and level from the samples heard by each tick, and Python's last tick past the last frame.
fn ticks(models: &Tensors, acts: &[f32], pcm: &[f32]) -> Vec<Tick> {
    let (bp, tp) = (BeatParams::default(), TickParams::default());
    let mut dbn = OnlineDbn::new(models, "dbn_online", bp.fps).unwrap();
    let mut beliefs = Beliefs::new(&bp);
    let mut evidence = Evidence::new(&bp, dbn.max_period());
    let mut energy = Energy::new(&tp, HOP);
    let mut levels = Levels::new(&tp);
    let mut out = Vec::new();
    let mut fed = 0;
    let mut take = |f: usize,
                    last: f64,
                    period: f64,
                    evidence: &Evidence,
                    energy: &mut Energy,
                    fed: &mut usize| {
        // (The samples heard by frame f's end, as the analyzer feeds them.)
        let upto = (f * HOP + 1).min(pcm.len());
        energy.push(&pcm[*fed..upto]);
        *fed = upto;
        let t = f as f64 / bp.fps;
        let e = energy.at(t);
        out.push(Tick {
            t,
            known_at: t,
            evidence: evidence.at(last, period, t),
            energy: e,
            level: levels.step(e),
            key: None,
            bands: analysis::bands::TickBands::default(),
            brightness: 0.0,
        });
    };
    let mut last = None;
    for (n, &a) in acts.iter().enumerate() {
        let b = beliefs.step(dbn.step(a));
        evidence.push(a);
        if n > 0 && n % bp.tick_frames == 0 {
            take(n, b.last, b.period, &evidence, &mut energy, &mut fed);
        }
        last = Some(b);
    }
    let b = last.unwrap();
    let total = (pcm.len() as f64 / tp.sample_rate * bp.fps / bp.tick_frames as f64 + 1e-9).floor()
        as usize;
    let mut f = (b.frame / bp.tick_frames + 1) * bp.tick_frames;
    while f <= total * bp.tick_frames {
        take(f, b.last, b.period, &evidence, &mut energy, &mut fed);
        f += bp.tick_frames;
    }
    out
}

/// Onsets picked from a detection function.
fn pick(odf: &[f32]) -> Vec<Onset> {
    let p = OnsetParams::default();
    let (mut picker, mut strengths) = (PeakPicker::new(&p), Strengths::new(&p));
    odf.iter()
        .filter_map(|&v| picker.step(v).map(|t| strengths.onset(t, v)))
        .collect()
}

fn assert_onsets(ours: &[Onset], g: &Tensors, who: &str) {
    assert_eq!(
        bits(ours.iter().map(|o| o.t)),
        bits(g.f64("onsets.t").unwrap()),
        "{who}: onset times"
    );
    assert_eq!(
        bits(ours.iter().map(|o| o.known_at)),
        bits(g.f64("onsets.knownAt").unwrap()),
        "{who}: knownAt"
    );
    assert_eq!(
        bits(ours.iter().map(|o| o.strength)),
        bits(g.f64("onsets.strength").unwrap()),
        "{who}: strengths"
    );
    let strong: Vec<i32> = ours.iter().map(|o| i32::from(o.strong)).collect();
    assert_eq!(
        strong,
        g.i32("onsets.strong").unwrap(),
        "{who}: strong flags"
    );
}

#[test]
fn params_are_the_streams() {
    let manifest: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(models_dir().join("manifest.json")).unwrap())
            .unwrap();
    let s = &manifest["streamParams"];
    let (t, o) = (TickParams::default(), OnsetParams::default());
    assert_eq!(s["energy"]["windowS"].as_f64(), Some(t.energy_window_s));
    assert_eq!(
        s["energy"]["decayDbPerS"].as_f64(),
        Some(t.energy_decay_db_per_s)
    );
    assert_eq!(s["energy"]["rangeDb"].as_f64(), Some(t.energy_range_db));
    assert_eq!(s["energy"]["floorDb"].as_f64(), Some(t.energy_floor_db));
    let level = &s["structure"]["level"];
    assert_eq!(
        [
            level["loudIn"].as_f64(),
            level["loudOut"].as_f64(),
            level["quietIn"].as_f64(),
            level["quietOut"].as_f64()
        ],
        [
            Some(t.loud_in),
            Some(t.loud_out),
            Some(t.quiet_in),
            Some(t.quiet_out)
        ]
    );
    assert_eq!(s["onsets"]["threshold"].as_f64(), Some(o.threshold));
    assert_eq!(s["onsets"]["biasS"].as_f64(), Some(o.bias_s));
    assert_eq!(
        s["onsets"]["strongPercentile"].as_f64(),
        Some(o.strong_percentile)
    );
    assert_eq!(
        s["onsets"]["strongWindowS"].as_f64(),
        Some(o.strong_window_s)
    );
    assert_eq!(
        models().i32("onsets.diff_max_bins").unwrap(),
        [o.diff_max_bins as i32]
    );
    let codes: Vec<&str> = manifest["codes"]["level"]
        .as_array()
        .unwrap()
        .iter()
        .map(|v| v.as_str().unwrap())
        .collect();
    assert_eq!(codes, ["quiet", "medium", "loud"], "Level::code's order");
}

#[test]
fn ticks_match_python_to_the_bit() {
    let models = models();
    for excerpt in EXCERPTS {
        let g = golden(excerpt);
        let ticks = ticks(
            &models,
            &g.f32("online.activations").unwrap(),
            &g.f32("pcm").unwrap(),
        );
        assert_eq!(
            bits(ticks.iter().map(|k| k.t)),
            bits(g.f64("ticks.t").unwrap()),
            "{excerpt}: tick times"
        );
        assert_eq!(
            bits(ticks.iter().map(|k| k.evidence)),
            bits(g.f64("ticks.evidence").unwrap()),
            "{excerpt}: evidence"
        );
        assert_eq!(
            bits(ticks.iter().map(|k| k.energy)),
            bits(g.f64("ticks.energy").unwrap()),
            "{excerpt}: energy"
        );
        let levels: Vec<i32> = ticks.iter().map(|k| k.level.code()).collect();
        assert_eq!(levels, g.i32("ticks.level").unwrap(), "{excerpt}: levels");
        eprintln!(
            "{excerpt}: {} ticks bit-equal, levels {:?}",
            ticks.len(),
            levels.iter().fold([0; 3], |mut c, &l| {
                c[l as usize] += 1;
                c
            })
        );
    }
}

/// The onset function from Rust's spectra, with each frame's tolerance: a sum of positive
/// differences of near-equal magnitudes inherits their float32 rounding, so it takes the summed
/// relative tolerance of the two operands of every positive term (Python's spectra aren't in the
/// golden data, so the operands are Rust's, equal to Python's within A1's tolerance).
fn onset_function(models: &Tensors, pcm: &[f32]) -> (Vec<f32>, Vec<f32>) {
    let mut fe = OnlineFrontEnd::new(models).unwrap();
    let d = fe.diff_frames();
    let mut sf = Superflux::new(&OnsetParams::default(), d);
    let (mut odf, mut tol) = (Vec::new(), Vec::new());
    let mut past: std::collections::VecDeque<Vec<f32>> = std::collections::VecDeque::new();
    fe.push(pcm, |fr| {
        odf.push(sf.step(fr.spectrum));
        let n = fr.spectrum.len();
        let maxed: Vec<f32> = (0..n)
            .map(|b| {
                fr.spectrum[b.saturating_sub(1)..=(b + 1).min(n - 1)]
                    .iter()
                    .copied()
                    .fold(f32::MIN, f32::max)
            })
            .collect();
        let scale: f32 = if past.len() == d {
            fr.spectrum
                .iter()
                .zip(&past[0])
                .filter(|(now, back)| now > back)
                .map(|(now, back)| tolerance(*now) + tolerance(*back))
                .sum()
        } else {
            0.0
        };
        tol.push(scale.max(1e-7));
        past.push_back(maxed);
        if past.len() > d {
            past.pop_front();
        }
    });
    (odf, tol)
}

#[test]
fn the_onset_function_matches_python() {
    let models = models();
    for excerpt in EXCERPTS {
        let g = golden(excerpt);
        let (odf, tol) = onset_function(&models, &g.f32("pcm").unwrap());
        let py = g.f32("onsets.odf").unwrap();
        assert_eq!(odf.len(), py.len(), "{excerpt}: frames");
        let relative = odf
            .iter()
            .zip(&py)
            .map(|(r, p)| (r - p).abs() / tolerance(*p))
            .fold(0.0f32, f32::max);
        let (worst, at) = odf
            .iter()
            .zip(&py)
            .zip(&tol)
            .enumerate()
            .map(|(i, ((r, p), t))| ((r - p).abs() / t, i))
            .fold((0.0f32, 0), |a, b| if b.0 > a.0 { b } else { a });
        eprintln!(
            "{excerpt}: onset function worst {worst:.3} of the operands' tolerance (frame {at}; {relative:.3} of its own relative tolerance)"
        );
        assert!(
            worst <= 1.0,
            "{excerpt}: the onset function differs from Python's beyond tolerance (frame {at})"
        );
        // (And, against the operands' wide budget, a bound on its own relative error: 1.6 today.)
        assert!(
            relative <= 3.0,
            "{excerpt}: the onset function's relative error grew to {relative}"
        );
    }
}

#[test]
fn onsets_from_pythons_function_match_to_the_bit() {
    for excerpt in EXCERPTS {
        let g = golden(excerpt);
        let ours = pick(&g.f32("onsets.odf").unwrap());
        assert_onsets(&ours, &g, excerpt);
        eprintln!(
            "{excerpt}: {} onsets ({} strong) bit-equal",
            ours.len(),
            ours.iter().filter(|o| o.strong).count()
        );
    }
}

#[test]
#[ignore = "release: the whole analyzer on the Galop excerpt, three times (scripts/ci-full native-tests runs it)"]
fn the_analyzer_is_independent_of_block_sizes() {
    let models = models();
    let pcm = golden("orpheus-galop").f32("pcm").unwrap();
    let run = |size: usize| {
        let mut an = Analyzer::new(&models, Params::default()).unwrap();
        let mut out = Vec::new();
        for block in pcm.chunks(size) {
            an.push(block, &mut out);
        }
        an.finish(&mut out);
        out
    };
    let whole = run(pcm.len());
    for size in [333, 4096] {
        assert_eq!(run(size), whole, "blocks of {size}");
    }
}

#[test]
fn thirty_seconds_of_tick_evidence_match_python() {
    // The 30 s Galop excerpt: 600 ticks, the last at frame 3000, one past the last frame.
    let models = models();
    let g = golden("orpheus-galop-hindsight");
    let ticks = ticks(
        &models,
        &g.f32("online.activations").unwrap(),
        &g.f32("pcm").unwrap(),
    );
    assert_eq!(
        bits(ticks.iter().map(|k| k.evidence)),
        bits(g.f64("ticks.evidence").unwrap()),
        "tick evidence"
    );
}
