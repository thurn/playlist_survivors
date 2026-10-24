//! Stage parity, A1 (docs/design/native-app.md, "Stage parity"): the online front end and the
//! online LSTM ensemble against Python's golden data (`native/models/golden/`), fed Python's own
//! decoded PCM in uneven blocks, as a stream delivers it.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use analysis::frontend::{FRAME_SIZE, HOP, OnlineFrontEnd, hanning};
use analysis::rnn::OnlineEnsemble;
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

/// About 1e-5 relative, with an absolute floor of 1e-7 (the plan's stage tolerance).
fn tolerance(python: f32) -> f32 {
    (1e-5 * python.abs()).max(1e-7)
}

/// Uneven block sizes, as capture delivers them.
fn blocks(pcm: &[f32]) -> impl Iterator<Item = &[f32]> {
    const SIZES: [usize; 6] = [1, 7, 441, 512, 4096, 333];
    let mut at = 0;
    let mut k = 0;
    std::iter::from_fn(move || {
        (at < pcm.len()).then(|| {
            let n = SIZES[k % SIZES.len()].min(pcm.len() - at);
            k += 1;
            at += n;
            &pcm[at - n..at]
        })
    })
}

/// The front end's features and the ensemble's activations over a whole excerpt.
fn run(models: &Tensors, pcm: &[f32]) -> (Vec<Vec<f32>>, Vec<f32>) {
    let mut fe = OnlineFrontEnd::new(models).expect("front end");
    let mut nn = OnlineEnsemble::new(models).expect("ensemble");
    let (mut frames, mut acts) = (Vec::new(), Vec::new());
    for block in blocks(pcm) {
        fe.push(block, |n, f| {
            assert_eq!(n, frames.len(), "frames in order");
            frames.push(f.to_vec());
            acts.push(nn.step(f));
        });
    }
    (frames, acts)
}

/// The largest error against Python, as a share of each value's tolerance, and where.
fn worst(rust: &[f32], python: &[f32], tol: impl Fn(usize) -> f32) -> (f32, usize) {
    assert_eq!(rust.len(), python.len());
    rust.iter()
        .zip(python)
        .enumerate()
        .map(|(i, (r, p))| ((r - p).abs() / tol(i), i))
        .fold((0.0, 0), |a, b| if b.0 > a.0 { b } else { a })
}

/// The positive differences in each frame, checked exactly: `max(now - back, 0)` of the frame's
/// own spectrum and the one `diff_frames` back (zero before then), in float32. With the spectra
/// within tolerance of Python's, and Python's differences built the same way, the differences
/// follow; their own relative tolerance would be too tight, since a difference of two near-equal
/// spectrum values inherits their float32 rounding (house-128, frame 761: 1 and 2 ulps).
fn assert_differences(features: &[f32], diff_frames: usize, who: &str) {
    for (n, f) in features.as_chunks::<162>().0.iter().enumerate() {
        for b in 0..81 {
            let want = if n >= diff_frames {
                (f[b] - features[(n - diff_frames) * 162 + b]).max(0.0)
            } else {
                0.0
            };
            assert_eq!(
                f[81 + b].to_bits(),
                want.to_bits(),
                "{who}: frame {n}, band {b}'s difference"
            );
        }
    }
}

/// The log spectra alone (each frame's first 81 features).
fn spectra(features: &[f32]) -> Vec<f32> {
    features
        .as_chunks::<162>()
        .0
        .iter()
        .flat_map(|f| f[..81].to_vec())
        .collect()
}

#[test]
fn hanning_matches_the_exported_window() {
    let exported = models().f32("online.2048.window").unwrap();
    for (i, (ours, theirs)) in hanning(FRAME_SIZE).iter().zip(&exported).enumerate() {
        assert_eq!(*ours as f32, *theirs, "window[{i}]");
    }
}

#[test]
fn frames_are_cut_as_madmom_cuts_them() {
    let models = models();
    for excerpt in EXCERPTS {
        let g = golden(excerpt);
        let pcm = g.f32("pcm").unwrap();
        let mut fe = OnlineFrontEnd::new(&models).unwrap();
        let mut seen = Vec::new();
        // (Sample by sample, so each frame's raw samples can be read as it completes.)
        for s in &pcm[..=60 * HOP] {
            fe.push(std::slice::from_ref(s), |_, _| {});
            if let Some(raw) = fe.last_frame_samples() {
                seen.push((fe.frames() - 1, raw));
            }
        }
        for n in [0, 60] {
            let raw = &seen
                .iter()
                .find(|(k, _)| *k == n)
                .expect("frame completed")
                .1;
            assert_eq!(
                raw,
                &g.f32(&format!("online.frame{n}")).unwrap(),
                "{excerpt}: frame {n}"
            );
        }
    }
}

#[test]
fn features_and_activations_match_python() {
    let models = models();
    let diff_frames = models.i32("online.2048.diff_frames").unwrap()[0] as usize;
    for excerpt in EXCERPTS {
        let g = golden(excerpt);
        let pcm = g.f32("pcm").unwrap();
        let (frames, acts) = run(&models, &pcm);
        assert_eq!(
            frames.len(),
            g.shape("online.features").unwrap()[0],
            "{excerpt}: Python's frame count"
        );
        let features: Vec<f32> = frames.concat();
        let py = g.f32("online.features").unwrap();
        assert_differences(&features, diff_frames, "rust");
        assert_differences(&py, diff_frames, "python");
        let py_spectra = spectra(&py);
        let (fw, fi) = worst(&spectra(&features), &py_spectra, |i| {
            tolerance(py_spectra[i])
        });
        let py_acts = g.f32("online.activations").unwrap();
        let (aw, ai) = worst(&acts, &py_acts, |i| tolerance(py_acts[i]));
        eprintln!(
            "{excerpt}: spectra worst {fw:.3} of tolerance (frame {}, band {}), activations worst {aw:.3} (frame {ai})",
            fi / 81,
            fi % 81
        );
        assert!(
            fw <= 1.0,
            "{excerpt}: a band differs from Python's beyond tolerance (frame {}, band {})",
            fi / 81,
            fi % 81
        );
        assert!(
            aw <= 1.0,
            "{excerpt}: an activation differs from Python's beyond tolerance (frame {ai})"
        );
    }
}

#[test]
fn the_stream_is_independent_of_block_sizes() {
    let models = models();
    let pcm = golden("house-128").f32("pcm").unwrap();
    let (_, streamed) = run(&models, &pcm);
    let mut fe = OnlineFrontEnd::new(&models).unwrap();
    let mut nn = OnlineEnsemble::new(&models).unwrap();
    let mut whole = Vec::new();
    fe.push(&pcm, |_, f| whole.push(nn.step(f)));
    assert_eq!(streamed, whole);
}

#[test]
fn the_ensemble_has_eight_networks() {
    assert_eq!(OnlineEnsemble::new(&models()).unwrap().networks(), 8);
}

#[test]
fn a_stream_of_any_length_has_madmoms_frames() {
    // madmom frames a signal into ceil(len / 441) frames (`end='normal'`); the excerpts are whole
    // multiples of 441, so cut one short: every frame a stream completes is a prefix of Python's.
    let models = models();
    let g = golden("orpheus-galop");
    let pcm = g.f32("pcm").unwrap();
    let py = spectra(&g.f32("online.features").unwrap());
    for len in [pcm.len() - 1, pcm.len() - 200, 599 * HOP + 1, 599 * HOP] {
        let (frames, _) = run(&models, &pcm[..len]);
        assert_eq!(frames.len(), len.div_ceil(HOP), "{len} samples");
        let ours = spectra(&frames.concat());
        let (w, i) = worst(&ours, &py[..ours.len()], |i| tolerance(py[i]));
        assert!(w <= 1.0, "{len} samples: frame {}, band {}", i / 81, i % 81);
    }
}

#[test]
fn the_ensemble_takes_the_front_ends_features() {
    let models = models();
    let fe = OnlineFrontEnd::new(&models).unwrap();
    assert_eq!(
        OnlineEnsemble::new(&models).unwrap().input_width(),
        fe.width()
    );
}
