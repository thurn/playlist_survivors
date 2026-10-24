//! Stage parity, A4 (docs/design/native-app.md, "Stage parity" and "Hindsight's live latency"):
//! the offline front end, the BiLSTM ensemble, the offline DBN's decode, the hindsight entries
//! and the phase check against Python's golden data (`native/models/golden/`), each stage fed
//! Python's own upstream; then the whole analyzer from PCM with D = 0, inline and on the worker.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};
use std::time::Instant;

use analysis::analyzer::{Analyzer, Entry, Params};
use analysis::beats::{BeatParams, Beliefs, Commit, Committer, Evidence};
use analysis::dbn::OnlineDbn;
use analysis::hindsight::{
    HindsightDecoder, HindsightEntry, HindsightParams, OnlineBeats, phase_shift,
};
use analysis::tensors::Tensors;

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

/// A committed beat's field, by name (as the golden data names them).
type Field = (&'static str, fn(&Commit) -> f64);
const SR: usize = 44_100;

fn bits(v: impl IntoIterator<Item = f64>) -> Vec<u64> {
    v.into_iter().map(f64::to_bits).collect()
}

/// About 1e-5 relative, with an absolute floor of 1e-7 (the plan's stage tolerance).
fn tolerance(python: f32) -> f32 {
    (1e-5 * python.abs()).max(1e-7)
}

fn worst(rust: &[f32], python: &[f32], tol: impl Fn(usize) -> f32) -> (f32, usize) {
    assert_eq!(rust.len(), python.len());
    rust.iter()
        .zip(python)
        .enumerate()
        .map(|(i, (r, p))| ((r - p).abs() / tol(i), i))
        .fold((0.0, 0), |a, b| if b.0 > a.0 { b } else { a })
}

/// A golden ragged list (values and CSR pointers) as lists.
fn ragged(g: &Tensors, name: &str) -> Vec<Vec<f64>> {
    let v = g.f64(name).unwrap();
    let ptr = g.i32(&format!("{name}_ptr")).unwrap();
    ptr.windows(2)
        .map(|w| v[w[0] as usize..w[1] as usize].to_vec())
        .collect()
}

/// Python's hindsight entries from the 30 s golden excerpt.
fn python_entries(g: &Tensors) -> Vec<HindsightEntry> {
    let known = g.f64("hindsight.knownAt").unwrap();
    let off = ragged(g, "hindsight.offline");
    let on = ragged(g, "hindsight.online");
    known
        .iter()
        .zip(off)
        .zip(on)
        .map(|((&k, offline), online)| HindsightEntry {
            known_at: k,
            window_end: k,
            offline,
            online,
        })
        .collect()
}

/// The resolutions' feature blocks: (first column, bands, diff_frames) for 1024, 2048, 4096.
fn blocks(models: &Tensors) -> Vec<(usize, usize, usize)> {
    let mut col = 0;
    [1024, 2048, 4096]
        .iter()
        .map(|s| {
            let bands = models.shape(&format!("offline.{s}.filterbank")).unwrap()[1];
            let d = models.i32(&format!("offline.{s}.diff_frames")).unwrap()[0] as usize;
            let b = (col, bands, d);
            col += 2 * bands;
            b
        })
        .collect()
}

#[test]
fn params_are_the_streams() {
    let manifest: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(models_dir().join("manifest.json")).unwrap())
            .unwrap();
    let s = &manifest["streamParams"];
    let p = HindsightParams::default();
    assert_eq!(s["hindsight"]["everyS"].as_f64(), Some(p.every_s));
    assert_eq!(s["hindsight"]["windowS"].as_f64(), Some(p.window_s));
    assert_eq!(s["hindsight"]["keepS"].as_f64(), Some(p.keep_s));
    assert_eq!(s["hindsight"]["biasS"].as_f64(), Some(p.bias_s));
    let ph = &s["phase"];
    assert_eq!(ph["settleS"].as_f64(), Some(p.phase_settle_s));
    assert_eq!(ph["minBeats"].as_u64(), Some(p.phase_min_beats as u64));
    assert_eq!(ph["minR"].as_f64(), Some(p.phase_min_r));
    assert_eq!(ph["minShift"].as_f64(), Some(p.phase_min_shift));
    assert_eq!(ph["levelTol"].as_f64(), Some(p.phase_level_tol));
    assert_eq!(ph["levelShare"].as_f64(), Some(p.phase_level_share));
    assert_eq!(ph["persist"].as_u64(), Some(p.phase_persist as u64));
    assert_eq!(ph["agree"].as_f64(), Some(p.phase_agree));
    assert_eq!(p.delay_s, 0.0, "stage parity runs with D = 0");
}

#[test]
fn offline_features_match_python() {
    let models = models();
    let mut dec = HindsightDecoder::new(&models, 100.0, SR).unwrap();
    let (fe, _, _) = dec.stages();
    for excerpt in EXCERPTS {
        let g = golden(excerpt);
        let (frames, ours) = fe.process(&g.f32("pcm").unwrap());
        let py = g.f32("offline.features").unwrap();
        assert_eq!(
            frames,
            g.shape("offline.features").unwrap()[0],
            "{excerpt}: frames"
        );
        let width = fe.width();
        let mut spectra = (Vec::new(), Vec::new());
        for (col, bands, d) in blocks(&models) {
            for f in 0..frames {
                for b in 0..bands {
                    let (i, j) = (f * width + col + b, f * width + col + bands + b);
                    spectra.0.push(ours[i]);
                    spectra.1.push(py[i]);
                    // The positive differences exactly, on both sides (they inherit the spectra's rounding).
                    for (who, v) in [("rust", &ours), ("python", &py)] {
                        let want = if f >= d {
                            (v[i] - v[(f - d) * width + col + b]).max(0.0)
                        } else {
                            0.0
                        };
                        assert_eq!(
                            v[j].to_bits(),
                            want.to_bits(),
                            "{excerpt} {who}: frame {f}, column {j}"
                        );
                    }
                }
            }
        }
        let (w, at) = worst(&spectra.0, &spectra.1, |i| tolerance(spectra.1[i]));
        eprintln!("{excerpt}: offline spectra worst {w:.3} of tolerance (value {at})");
        assert!(w <= 1.0, "{excerpt}: offline spectra beyond tolerance");
    }
}

#[test]
fn offline_activations_and_beats_match_python() {
    let models = models();
    let mut dec = HindsightDecoder::new(&models, 100.0, SR).unwrap();
    let (fe, nn, dbn) = dec.stages();
    for excerpt in EXCERPTS {
        let g = golden(excerpt);
        let py_feat = g.f32("offline.features").unwrap();
        let frames = g.shape("offline.features").unwrap()[0];
        let py_acts = g.f32("offline.activations").unwrap();
        let acts = nn.activations(&py_feat, frames);
        let (w, at) = worst(&acts, &py_acts, |i| tolerance(py_acts[i]));
        let (rf, rfeat) = fe.process(&g.f32("pcm").unwrap());
        let (w2, _) = worst(&nn.activations(&rfeat, rf), &py_acts, |i| {
            tolerance(py_acts[i])
        });
        eprintln!(
            "{excerpt}: offline activations worst {w:.3} of tolerance (frame {at}) from Python's features, {w2:.3} from Rust's"
        );
        assert!(
            w <= 1.0 && w2 <= 1.0,
            "{excerpt}: offline activations beyond tolerance"
        );
        let beats = dbn.beats(&py_acts);
        assert_eq!(
            bits(beats.iter().copied()),
            bits(g.f64("offline.beats").unwrap()),
            "{excerpt}: offline beats from Python's activations"
        );
        assert_eq!(
            bits(dbn.beats(&acts)),
            bits(g.f64("offline.beats").unwrap()),
            "{excerpt}: offline beats from Rust's activations"
        );
        eprintln!("{excerpt}: {} offline beats bit-equal", beats.len());
    }
}

#[test]
fn hindsight_entries_match_python() {
    // The 30 s Galop excerpt: a window every second, 20 s long once the audio allows, decoded from
    // PCM; the believed beats from Python's online beliefs.
    let models = models();
    let g = golden("orpheus-galop-hindsight");
    let pcm = g.f32("pcm").unwrap();
    let p = HindsightParams::default();
    let bp = BeatParams::default();
    let mut dec = HindsightDecoder::new(&models, bp.fps, SR).unwrap();
    let since = g.f64("online.since").unwrap();
    let mut online = OnlineBeats::new(p.keep_s, bp.fps, bp.bias_s);
    let python = python_entries(&g);
    let mut times = Vec::new();
    let mut n = 0;
    for (k, want) in (1u32..).zip(&python) {
        let t_end = f64::from(k) * p.every_s;
        let frame = (t_end * bp.fps).round() as usize;
        while n <= frame.min(since.len() - 1) {
            online.push(n, since[n]);
            n += 1;
        }
        let start = (t_end - p.window_s).max(0.0);
        let window = &pcm[(start * SR as f64).round() as usize
            ..((t_end * SR as f64).round() as usize).min(pcm.len())];
        let t0 = Instant::now();
        let beats = dec.decode(window);
        times.push(t0.elapsed().as_secs_f64() * 1000.0);
        let lo = t_end - p.keep_s;
        let offline: Vec<f64> = beats
            .into_iter()
            .map(|b| b + start + p.bias_s)
            .filter(|&b| lo <= b && b <= t_end)
            .collect();
        assert_eq!(
            t_end.to_bits(),
            want.window_end.to_bits(),
            "window {k}: its end"
        );
        assert_eq!(
            bits(offline),
            bits(want.offline.iter().copied()),
            "window {k}: offline beats"
        );
        assert_eq!(
            bits(online.kept(frame, lo, t_end)),
            bits(want.online.iter().copied()),
            "window {k}: believed beats"
        );
    }
    times.sort_by(f64::total_cmp);
    eprintln!(
        "hindsight: {} entries bit-equal; decode ms (window up to 20 s): min {:.0}, median {:.0}, p90 {:.0}, max {:.0}",
        python.len(),
        times[0],
        times[times.len() / 2],
        times[times.len() * 9 / 10],
        times[times.len() - 1]
    );
}

#[test]
fn the_phase_check_commits_pythons_beats() {
    // Python's activations and hindsight entries into the commit rule with D = 0.
    let models = models();
    let g = golden("orpheus-galop-hindsight");
    let (commits, shifted) = phased_commits(&models, &g, 0.0);
    let fields: [Field; 4] = [
        ("t", |c| c.t),
        ("knownAt", |c| c.known_at),
        ("period", |c| c.period),
        ("evidence", |c| c.evidence),
    ];
    for (name, f) in fields {
        assert_eq!(
            bits(commits.iter().map(f)),
            bits(g.f64(&format!("commits.{name}")).unwrap()),
            "phased commits: {name}"
        );
    }
    eprintln!(
        "phase check: {} committed beats bit-equal, {shifted} frames shifted",
        commits.len()
    );
    assert!(shifted > 0, "the excerpt exercises the phase check");
}

fn analyze(models: &Tensors, pcm: &[f32], worker: bool, delay_s: f64) -> Vec<Entry> {
    let params = Params {
        hindsight: HindsightParams {
            delay_s,
            ..HindsightParams::default()
        },
        ..Params::default()
    };
    let mut an = if worker {
        Analyzer::with_hindsight_worker(models, params)
    } else {
        Analyzer::new(models, params)
    }
    .unwrap();
    let mut out = Vec::new();
    for block in pcm.chunks(4096) {
        an.push(block, &mut out);
    }
    an.finish(&mut out);
    out
}

/// The committed beats of Python's activations and hindsight entries (usable `delay_s` after
/// their windows end), through the commit rule and the phase check.
fn phased_commits(models: &Tensors, g: &Tensors, delay_s: f64) -> (Vec<Commit>, usize) {
    let entries: Vec<HindsightEntry> = python_entries(g)
        .into_iter()
        .map(|e| HindsightEntry {
            known_at: e.window_end + delay_s,
            ..e
        })
        .collect();
    let (bp, hp) = (
        BeatParams::default(),
        HindsightParams {
            delay_s,
            ..HindsightParams::default()
        },
    );
    let mut dbn = OnlineDbn::new(models, "dbn_online", bp.fps).unwrap();
    let mut beliefs = Beliefs::new(&bp);
    let mut evidence = Evidence::new(&bp, dbn.max_period());
    let mut committer = Committer::new(&bp);
    let mut commits: Vec<Commit> = Vec::new();
    let mut current = 0.0;
    let mut usable: Vec<HindsightEntry> = Vec::new();
    let mut shifted = 0;
    for (n, &a) in g.f32("online.activations").unwrap().iter().enumerate() {
        let b = beliefs.step(dbn.step(a));
        evidence.push(a);
        let t = n as f64 / bp.fps;
        if n > 0 && n.is_multiple_of(bp.tick_frames) {
            current = evidence.at(b.last, b.period, t);
        }
        while usable.len() < entries.len() && entries[usable.len()].known_at <= t + 1e-9 {
            usable.push(entries[usable.len()].clone());
        }
        let shift = phase_shift(&usable, committer.biased_last(&b), b.period, &hp);
        shifted += usize::from(shift != 0.0);
        committer.step(&b, shift, current, &mut commits);
    }
    (commits, shifted)
}

#[test]
fn the_analyzer_from_pcm_commits_pythons_phased_beats() {
    let models = models();
    let g = golden("orpheus-galop-hindsight");
    let out = analyze(&models, &g.f32("pcm").unwrap(), false, 0.0);
    let beats: Vec<f64> = out
        .iter()
        .filter_map(|e| {
            if let Entry::Beat(b) = e {
                Some(b.t)
            } else {
                None
            }
        })
        .collect();
    let entries: Vec<&HindsightEntry> = out
        .iter()
        .filter_map(|e| {
            if let Entry::Hindsight(h) = e {
                Some(h)
            } else {
                None
            }
        })
        .collect();
    let python = python_entries(&g);
    assert_eq!(entries.len(), python.len(), "hindsight entries");
    for (ours, want) in entries.iter().zip(&python) {
        assert_eq!(
            bits(ours.offline.iter().copied()),
            bits(want.offline.iter().copied()),
            "window ending {}: offline",
            want.window_end
        );
        assert_eq!(
            bits(ours.online.iter().copied()),
            bits(want.online.iter().copied()),
            "window ending {}: online",
            want.window_end
        );
    }
    assert_eq!(
        bits(beats.iter().copied()),
        bits(g.f64("commits.t").unwrap()),
        "phased committed beats from PCM"
    );
    eprintln!(
        "analyzer from PCM: {} hindsight entries and {} phased beats bit-equal",
        entries.len(),
        beats.len()
    );
}

#[test]
fn the_worker_gives_the_inline_results() {
    let models = models();
    let pcm = golden("orpheus-galop-hindsight").f32("pcm").unwrap();
    for delay in [0.0, 0.67] {
        assert_eq!(
            analyze(&models, &pcm, true, delay),
            analyze(&models, &pcm, false, delay),
            "D = {delay}"
        );
    }
}

#[test]
fn with_a_delay_entries_count_from_their_window_ends_plus_d() {
    // D = 0.67 s (the live delay): every entry is usable 0.67 s after its window ends, and the
    // analyzer's commits from PCM equal the commit rule fed Python's entries shifted by D (whose
    // beats equal the analyzer's, as the D = 0 test shows).
    let models = models();
    let g = golden("orpheus-galop-hindsight");
    let d = HindsightParams::live().delay_s;
    assert_eq!(d, 0.67);
    let out = analyze(&models, &g.f32("pcm").unwrap(), false, d);
    for e in out.iter().filter_map(|e| {
        if let Entry::Hindsight(h) = e {
            Some(h)
        } else {
            None
        }
    }) {
        assert_eq!(
            e.known_at.to_bits(),
            (e.window_end + d).to_bits(),
            "window ending {}",
            e.window_end
        );
    }
    let beats: Vec<f64> = out
        .iter()
        .filter_map(|e| {
            if let Entry::Beat(b) = e {
                Some(b.t)
            } else {
                None
            }
        })
        .collect();
    let (want, shifted) = phased_commits(&models, &g, d);
    assert_eq!(
        bits(beats),
        bits(want.iter().map(|c| c.t)),
        "commits with D = {d}"
    );
    let (at_zero, _) = phased_commits(&models, &g, 0.0);
    eprintln!(
        "D = {d}: {} committed beats as the oracle gives them ({shifted} frames shifted); {} differ from D = 0",
        want.len(),
        want.iter()
            .zip(&at_zero)
            .filter(|(a, b)| a.t != b.t)
            .count()
            + want.len().abs_diff(at_zero.len())
    );
}

#[test]
fn a_stream_of_awkward_length_has_pythons_entries() {
    // The 30 s Galop excerpt cut to 29.997 s (neither whole seconds nor a multiple of 441):
    // Python's entries for it (tests/fixtures/a4-galop-29997.json, from stream.hindsight_entries).
    let models = models();
    let fixture: serde_json::Value = serde_json::from_str(
        &std::fs::read_to_string(
            Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/a4-galop-29997.json"),
        )
        .unwrap(),
    )
    .unwrap();
    let len = fixture["samples"].as_u64().unwrap() as usize;
    let pcm = golden("orpheus-galop-hindsight").f32("pcm").unwrap();
    let out = analyze(&models, &pcm[..len], false, 0.0);
    let ours: Vec<&HindsightEntry> = out
        .iter()
        .filter_map(|e| {
            if let Entry::Hindsight(h) = e {
                Some(h)
            } else {
                None
            }
        })
        .collect();
    let want = fixture["entries"].as_array().unwrap();
    assert_eq!(ours.len(), want.len(), "entries");
    let words = |v: &serde_json::Value| {
        v.as_array()
            .unwrap()
            .iter()
            .map(|x| x.as_u64().unwrap())
            .collect::<Vec<u64>>()
    };
    for (o, w) in ours.iter().zip(want) {
        assert_eq!(
            o.window_end.to_bits(),
            w["knownAt"].as_u64().unwrap(),
            "window end"
        );
        assert_eq!(
            bits(o.offline.iter().copied()),
            words(&w["offline"]),
            "window ending {}: offline",
            o.window_end
        );
        assert_eq!(
            bits(o.online.iter().copied()),
            words(&w["online"]),
            "window ending {}: online",
            o.window_end
        );
    }
}

#[test]
fn viterbi_ties_and_edges_are_madmoms() {
    // A 5-state model with tied transitions (tests/fixtures/a4-tiny-dbn.bin), decoded by madmom's
    // HiddenMarkovModel and process_offline's correction on activations with exact 1.0 and 0.0
    // values, beat runs at both edges, and all zeros (tests/fixtures/a4-tiny-dbn.json).
    let dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures");
    let t = Tensors::load(&dir.join("a4-tiny-dbn.bin")).unwrap();
    let mut dbn = analysis::hindsight::OfflineDbn::new(&t, 100.0).unwrap();
    let fixture: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(dir.join("a4-tiny-dbn.json")).unwrap())
            .unwrap();
    for (case, want) in fixture["cases"]
        .as_array()
        .unwrap()
        .iter()
        .zip(fixture["beats"].as_array().unwrap())
    {
        let acts: Vec<f32> = case
            .as_array()
            .unwrap()
            .iter()
            .map(|v| v.as_f64().unwrap() as f32)
            .collect();
        let want: Vec<u64> = want
            .as_array()
            .unwrap()
            .iter()
            .map(|v| v.as_u64().unwrap())
            .collect();
        assert_eq!(bits(dbn.beats(&acts)), want, "activations {acts:?}");
    }
}

#[test]
fn the_worker_reports_its_stalls() {
    // With D = 0 every entry is due as its window ends, so the analyzer waits for each decode
    // (bar the windows too short to decode): the stalls count them.
    let models = models();
    let pcm = golden("orpheus-galop-hindsight").f32("pcm").unwrap();
    let params = Params::default();
    let mut an = Analyzer::with_hindsight_worker(&models, params).unwrap();
    let mut out = Vec::new();
    an.push(&pcm, &mut out);
    let (stalls, waited) = an.hindsight_stalls();
    eprintln!("worker at D = 0: {stalls} stalls, {waited:?} in all");
    assert!(stalls > 0 && waited > std::time::Duration::ZERO);
}
