//! Stage parity, A5 (docs/design/native-app.md, "Stage parity"): structure's features (chroma,
//! MFCCs, loudness) and keys against Python's golden data (`native/models/golden/`), then its
//! changes, jumps, ends and keys against `structure.structure` on a synthetic piece built to have
//! all of them, and on the Galop's 30 s (`tests/fixtures/a5-structure.json`).
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use analysis::analyzer::{Analyzer, Entry, Params};
use analysis::structure::{KEYS, N_FFT, Structure, StructureParams, StructureTick, TickFeatures};
use analysis::tensors::Tensors;

const SR: f64 = 44100.0;
const HOP: usize = 2205;
/// Ticks a second (`stream.py`: `SR / hop`).
const TICK_FPS: f64 = SR / HOP as f64;

fn models_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../models")
}

fn models() -> Tensors {
    Tensors::load(&models_dir().join("madmom.bin")).expect("madmom.bin")
}

fn golden(excerpt: &str) -> Tensors {
    Tensors::load(&models_dir().join(format!("golden/{excerpt}.bin"))).expect("golden data")
}

fn fixture() -> serde_json::Value {
    let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/a5-structure.json");
    serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap()
}

/// About 1e-5 relative, with an absolute floor of 1e-7 (the plan's stage tolerance).
fn tolerance(python: f64) -> f64 {
    (1e-5 * python.abs()).max(1e-7)
}

/// The features' own bound: they differ from librosa's only by summation order (its BLAS
/// filterbank products over 2049 bins and its FFT DCT over 128 mel bands, against sequential dot
/// products with the same matrices), a few hundred ulps of the terms' scale at most: chroma is
/// at most 1, and an MFCC sums dB values. Measured: 5e-12 (1 + |value|).
fn summation_bound(python: f64) -> f64 {
    1e-10 * (1.0 + python.abs())
}

/// Tick k's frame (k from 1): the N_FFT samples before `k * HOP`, zeros before the start and past
/// the end (`structure.frames`).
fn frame(pcm: &[f32], k: usize) -> Vec<f64> {
    let end = k * HOP;
    (0..N_FFT)
        .map(|j| {
            (end + j)
                .checked_sub(N_FFT)
                .and_then(|n| pcm.get(n))
                .map_or(0.0, |&x| f64::from(x))
        })
        .collect()
}

/// The structure of `n` ticks of `pcm`.
fn run(pcm: &[f32], n: usize) -> Vec<StructureTick> {
    let mut s = Structure::new(&models(), &StructureParams::default(), SR, TICK_FPS).unwrap();
    (1..=n)
        .map(|k| s.tick(k as f64 * HOP as f64 / SR, &frame(pcm, k)))
        .collect()
}

#[test]
fn params_and_keys_are_the_streams() {
    let manifest: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(models_dir().join("manifest.json")).unwrap())
            .unwrap();
    let keys: Vec<&str> = manifest["codes"]["key"]
        .as_array()
        .unwrap()
        .iter()
        .map(|k| k.as_str().unwrap())
        .collect();
    assert_eq!(keys, KEYS);
    let s = &manifest["streamParams"]["structure"];
    let f = |group: &str, key: &str| {
        s[group][key]
            .as_f64()
            .unwrap_or_else(|| panic!("{group}.{key}"))
    };
    let p = StructureParams::default();
    let ours = [
        (p.change_past_s, f("change", "pastS")),
        (p.change_next_s, f("change", "nextS")),
        (p.change_stats_s, f("change", "statsS")),
        (p.change_peak_s, f("change", "peakS")),
        (p.change_threshold_s, f("change", "thresholdS")),
        (p.change_k, f("change", "k")),
        (p.change_min_prior as f64, f("change", "minPrior")),
        (p.jump_db, f("jump", "db")),
        (p.jump_recent_s, f("jump", "recentS")),
        (p.jump_before_s, f("jump", "beforeS")),
        (p.jump_gap_s, f("jump", "gapS")),
        (p.key_window_s, f("key", "windowS")),
        (p.key_after_s, f("key", "afterS")),
        (p.end_drop_db, f("end", "dropDb")),
        (p.end_hold_s, f("end", "holdS")),
        (p.end_after_s, f("end", "afterS")),
        (p.end_window_s, f("end", "windowS")),
        (p.end_smooth_s, f("end", "smoothS")),
    ];
    for (i, (a, b)) in ours.iter().enumerate() {
        assert_eq!(a, b, "parameter {i}");
    }
}

#[test]
fn features_match_python() {
    let models = models();
    for excerpt in ["house-128", "orpheus-galop"] {
        let g = golden(excerpt);
        let pcm = g.f32("pcm").unwrap();
        let times = g.f64("ticks.t").unwrap();
        let (chroma, feats, ldb) = (
            g.f64("structure.chroma").unwrap(),
            g.f64("structure.features").unwrap(),
            g.f64("structure.loudness_db").unwrap(),
        );
        let n = times.len();
        assert_eq!((chroma.len(), feats.len(), ldb.len()), (12 * n, 25 * n, n));
        let mut s = Structure::new(&models, &StructureParams::default(), SR, TICK_FPS).unwrap();
        let (mut worst, mut equal, mut count) = (0.0f64, 0, 0);
        // The plain relative error of the chroma, and the MFCCs' error over 1 + |value|.
        let (mut chroma_rel, mut mfcc_scaled) = (0.0f64, 0.0f64);
        for (i, &t) in times.iter().enumerate() {
            assert_eq!(
                t.to_bits(),
                ((i + 1) as f64 * HOP as f64 / SR).to_bits(),
                "{excerpt} tick {i}'s time"
            );
            let f = s.features().tick(&frame(&pcm, i + 1));
            assert_eq!(
                f.loudness_db.to_bits(),
                ldb[i].to_bits(),
                "{excerpt} tick {i}'s loudness"
            );
            for (c, &py) in f.chroma.iter().zip(&chroma[i * 12..]) {
                assert!(
                    (c - py).abs() <= tolerance(py).min(summation_bound(py)),
                    "{excerpt} tick {i}: chroma {c} against {py}"
                );
                worst = worst.max((c - py).abs() / tolerance(py));
                if py != 0.0 {
                    chroma_rel = chroma_rel.max((c - py).abs() / py.abs());
                }
                equal += usize::from(c.to_bits() == py.to_bits());
                count += 1;
            }
            for (d, (x, &py)) in f.features.iter().zip(&feats[i * 25..]).enumerate() {
                assert!(
                    (x - py).abs() <= tolerance(py).min(summation_bound(py)),
                    "{excerpt} tick {i}: feature {d} {x} against {py}"
                );
                worst = worst.max((x - py).abs() / tolerance(py));
                if (12..24).contains(&d) {
                    mfcc_scaled = mfcc_scaled.max((x - py).abs() / (1.0 + py.abs()));
                }
                equal += usize::from(x.to_bits() == py.to_bits());
                count += 1;
            }
        }
        println!(
            "{excerpt}: {n} ticks, features within {worst:.2e} of their tolerance, {equal} of {count} values bit-equal; chroma within {chroma_rel:.1e} relative, MFCCs within {mfcc_scaled:.1e} (1 + |value|)"
        );
    }
}

#[test]
fn keys_from_pythons_features_match_to_the_bit() {
    // Fed Python's own chroma, the window means and numpy's corrcoef give Python's keys.
    let models = models();
    for excerpt in ["house-128", "orpheus-galop"] {
        let g = golden(excerpt);
        let times = g.f64("ticks.t").unwrap();
        let keys = g.i32("ticks.key").unwrap();
        let (chroma, feats, ldb) = (
            g.f64("structure.chroma").unwrap(),
            g.f64("structure.features").unwrap(),
            g.f64("structure.loudness_db").unwrap(),
        );
        let n = times.len();
        assert_eq!(
            (chroma.len(), feats.len(), ldb.len(), keys.len()),
            (12 * n, 25 * n, n, n)
        );
        let mut stage = Structure::new(&models, &StructureParams::default(), SR, TICK_FPS).unwrap();
        let ours: Vec<i32> = (0..n)
            .map(|i| {
                let features = TickFeatures {
                    chroma: chroma[12 * i..12 * i + 12].try_into().unwrap(),
                    features: feats[25 * i..25 * i + 25].try_into().unwrap(),
                    loudness_db: ldb[i],
                };
                let k = stage.tick_features(times[i], &features).key;
                k.map_or(-1, |k| KEYS.iter().position(|&n| n == k).unwrap() as i32)
            })
            .collect();
        assert_eq!(ours, keys, "{excerpt}");
    }
}

#[test]
fn keys_match_python() {
    for excerpt in ["house-128", "orpheus-galop"] {
        let g = golden(excerpt);
        let pcm = g.f32("pcm").unwrap();
        let keys = g.i32("ticks.key").unwrap();
        let ours: Vec<i32> = run(&pcm, keys.len())
            .iter()
            .map(|t| {
                t.key
                    .map_or(-1, |k| KEYS.iter().position(|&n| n == k).unwrap() as i32)
            })
            .collect();
        assert_eq!(ours, keys, "{excerpt}");
    }
}

/// The fixture's synthetic piece: saw voices per section, summed in order, as float32.
fn synthetic(sections: &serde_json::Value) -> Vec<f32> {
    let total = sections.as_array().unwrap().last().unwrap()[1]
        .as_f64()
        .unwrap() as usize
        * SR as usize;
    let mut y = vec![0.0f32; total];
    for s in sections.as_array().unwrap() {
        let (from, to) = (
            s[0].as_f64().unwrap() as usize * SR as usize,
            s[1].as_f64().unwrap() as usize * SR as usize,
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

fn floats(v: &serde_json::Value) -> Vec<Vec<f64>> {
    v.as_array()
        .unwrap()
        .iter()
        .map(|row| {
            row.as_array()
                .unwrap()
                .iter()
                .map(|b| f64::from_bits(b.as_u64().unwrap()))
                .collect()
        })
        .collect()
}

fn assert_structure(ours: &[StructureTick], want: &serde_json::Value, who: &str) {
    let changes: Vec<Vec<f64>> = ours
        .iter()
        .filter_map(|t| t.change)
        .map(|c| vec![c.t, c.known_at, c.strength])
        .collect();
    let jumps: Vec<Vec<f64>> = ours
        .iter()
        .filter_map(|t| t.jump)
        .map(|j| vec![j.t, j.known_at, j.db])
        .collect();
    let ends: Vec<Vec<f64>> = ours
        .iter()
        .filter_map(|t| t.end)
        .map(|e| vec![e.t, e.known_at])
        .collect();
    for (name, ours, theirs) in [
        ("changes", changes, floats(&want["changes"])),
        ("jumps", jumps, floats(&want["jumps"])),
        ("ends", ends, floats(&want["ends"])),
    ] {
        assert_eq!(
            ours.len(),
            theirs.len(),
            "{who}: {name} {ours:?} against {theirs:?}"
        );
        let equal = ours
            .iter()
            .zip(&theirs)
            .filter(|(a, b)| {
                a.iter()
                    .zip(b.iter())
                    .all(|(x, y)| x.to_bits() == y.to_bits())
            })
            .count();
        println!("{who}: {} {name}, {equal} bit-equal", ours.len());
        for (a, b) in ours.iter().zip(&theirs) {
            // (Times exactly; a change's strength and a jump's dB within the stage tolerance.)
            assert_eq!(
                a[0].to_bits(),
                b[0].to_bits(),
                "{who}: {name} {a:?} against {b:?}"
            );
            assert_eq!(
                a[1].to_bits(),
                b[1].to_bits(),
                "{who}: {name} {a:?} against {b:?}"
            );
            if let (Some(x), Some(y)) = (a.get(2), b.get(2)) {
                assert!(
                    (x - y).abs() <= tolerance(*y),
                    "{who}: {name} {a:?} against {b:?}"
                );
            }
        }
    }
    let keys: Vec<&str> = ours.iter().map(|t| t.key.unwrap_or("")).collect();
    let theirs: Vec<&str> = want["keys"]
        .as_array()
        .unwrap()
        .iter()
        .map(|k| k.as_str().unwrap())
        .collect();
    assert_eq!(keys, theirs, "{who}: keys");
}

#[test]
fn structure_matches_python_on_a_synthetic_piece() {
    let f = fixture();
    let pcm = synthetic(&f["sections"]);
    let want = &f["synthetic"];
    let ours = run(&pcm, want["ticks"].as_u64().unwrap() as usize);
    assert_structure(&ours, want, "synthetic");
}

#[test]
fn structure_matches_python_on_a_cut_piece() {
    // (29.997 s: the last tick's frame ends 0.047 s before the samples do.)
    let f = fixture();
    let pcm = synthetic(&f["sections"]);
    let pcm = &pcm[..(29.997 * SR) as usize];
    let want = &f["synthetic29997"];
    let ours = run(pcm, want["ticks"].as_u64().unwrap() as usize);
    assert_structure(&ours, want, "synthetic29997");
}

#[test]
fn structure_matches_python_past_every_window_it_keeps() {
    // 150 s: the novelty ring (68 s), the change threshold's prior (60 s), the feature
    // statistics (30 s) and the keys' window (20 s) all evict.
    let f = fixture();
    let want = &f["long"];
    let pcm = synthetic(&want["sections"]);
    let ours = run(&pcm, want["ticks"].as_u64().unwrap() as usize);
    assert_structure(&ours, want, "long");
}

#[test]
fn structure_matches_python_on_the_galop() {
    let pcm = golden("orpheus-galop-hindsight").f32("pcm").unwrap();
    let want = &fixture()["galop30"];
    let ours = run(&pcm, want["ticks"].as_u64().unwrap() as usize);
    assert_structure(&ours, want, "galop30");
}

#[test]
#[ignore = "release: the whole analyzer, decoding hindsight windows (scripts/ci-full native runs it)"]
fn the_analyzer_from_pcm_matches_python() {
    // Structure reads only samples, so the analyzer's ticks carry Python's keys and its changes,
    // jumps and ends are Python's, fed in uneven blocks.
    let models = models();
    let f = fixture();
    for (who, pcm) in [
        ("synthetic", synthetic(&f["sections"])),
        (
            "synthetic29997",
            synthetic(&f["sections"])[..(29.997 * SR) as usize].to_vec(),
        ),
        ("long", synthetic(&f["long"]["sections"])),
        (
            "galop30",
            golden("orpheus-galop-hindsight").f32("pcm").unwrap(),
        ),
    ] {
        let want = &f[who];
        let mut an = Analyzer::new(&models, Params::default()).unwrap();
        let mut out = Vec::new();
        for block in pcm.chunks(1000) {
            an.push(block, &mut out);
        }
        an.finish(&mut out);
        let mut ticks: Vec<StructureTick> = Vec::new();
        for e in &out {
            match e {
                Entry::Tick(k) => ticks.push(StructureTick {
                    key: k.key,
                    ..StructureTick::default()
                }),
                Entry::Change(c) => ticks.last_mut().unwrap().change = Some(*c),
                Entry::Jump(j) => ticks.last_mut().unwrap().jump = Some(*j),
                Entry::End(e) => ticks.last_mut().unwrap().end = Some(*e),
                _ => {}
            }
        }
        assert_eq!(
            ticks.len(),
            want["ticks"].as_u64().unwrap() as usize,
            "{who}: ticks"
        );
        assert_structure(&ticks, want, who);
    }
}

#[test]
fn ticks_from_heard_samples_match_ticks_from_frames() {
    // The analyzer's path: samples pushed in uneven blocks, each tick's frame cut from the ring
    // by `tick_at`, gives what whole frames give (on the synthetic piece's events and keys).
    let f = fixture();
    let pcm = synthetic(&f["sections"]);
    let want = &f["synthetic"];
    let n = want["ticks"].as_u64().unwrap() as usize;
    let mut stage = Structure::new(&models(), &StructureParams::default(), SR, TICK_FPS).unwrap();
    let mut ours = Vec::new();
    let mut fed = 0;
    let mut k = 1;
    // (Uneven blocks, so ticks fall at every offset within them; the analyzer's are a hop.)
    let sizes = [997, 1994, 13, 441];
    let mut blocks = 0;
    while fed < pcm.len() {
        let block = &pcm[fed..(fed + sizes[blocks % sizes.len()]).min(pcm.len())];
        blocks += 1;
        stage.push_samples(block);
        fed += block.len();
        while k <= n && k * HOP <= fed {
            ours.push(stage.tick_at(k as f64 * HOP as f64 / SR, k * HOP));
            k += 1;
        }
    }
    assert_eq!(ours.len(), n);
    assert_structure(&ours, want, "synthetic from heard samples");
}
