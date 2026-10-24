//! Stage parity, A2 (docs/design/native-app.md, "Stage parity"): the online DBN's beliefs, the
//! smoothed periods, the evidence and the committed beats, fed Python's own activations
//! (`native/models/golden/`), against Python's, to the bit.
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};

use analysis::beats::{BeatParams, Beliefs, Commit, Committer, Evidence, pairwise_sum};
use analysis::dbn::OnlineDbn;
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

struct Run {
    states: Vec<usize>,
    since: Vec<f64>,
    period: Vec<f64>,
    /// Evidence at each tick frame (5, 10, ...) the stream reaches.
    ticks: Vec<f64>,
    commits: Vec<Commit>,
}

/// The stream's beat stages over activations, frame by frame.
fn run(models: &Tensors, acts: &[f32]) -> Run {
    let p = BeatParams::default();
    let mut dbn = OnlineDbn::new(models, "dbn_online", p.fps).unwrap();
    let mut beliefs = Beliefs::new(&p);
    let mut evidence = Evidence::new(&p, dbn.max_period());
    let mut committer = Committer::new(&p);
    let mut out = Run {
        states: vec![],
        since: vec![],
        period: vec![],
        ticks: vec![],
        commits: vec![],
    };
    let mut current = 0.0;
    for (n, &a) in acts.iter().enumerate() {
        let state = dbn.step(a);
        let belief = beliefs.step(state);
        evidence.push(a);
        if n > 0 && n % p.tick_frames == 0 {
            current = evidence.at(belief.last, belief.period, n as f64 / p.fps);
            out.ticks.push(current);
        }
        committer.step(&belief, 0.0, current, &mut out.commits);
        out.states.push(state.state);
        out.since.push(state.since);
        out.period.push(belief.period);
    }
    out
}

fn bits(v: &[f64]) -> Vec<u64> {
    v.iter().map(|x| x.to_bits()).collect()
}

#[test]
fn params_are_the_streams() {
    let manifest: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(models_dir().join("manifest.json")).unwrap())
            .unwrap();
    let s = &manifest["streamParams"];
    let p = BeatParams::default();
    assert_eq!(s["fps"].as_f64(), Some(p.fps));
    assert_eq!(s["period"]["windowS"].as_f64(), Some(p.period_window_s));
    assert_eq!(s["period"]["band"].as_f64(), Some(p.period_band));
    assert_eq!(s["commit"]["periods"].as_f64(), Some(p.commit_periods));
    assert_eq!(s["commit"]["extraS"].as_f64(), Some(p.commit_extra_s));
    assert_eq!(s["commit"]["minLeadS"].as_f64(), Some(p.commit_min_lead_s));
    assert_eq!(s["biasS"].as_f64(), Some(p.bias_s));
    assert_eq!(
        s["evidence"]["beats"].as_u64(),
        Some(p.evidence_beats as u64)
    );
    assert_eq!(s["evidence"]["halfS"].as_f64(), Some(p.evidence_half_s));
    assert_eq!(
        (s["tickS"].as_f64().unwrap() * p.fps).round() as usize,
        p.tick_frames
    );
}

#[test]
fn beliefs_evidence_and_commits_match_python_to_the_bit() {
    let models = models();
    for excerpt in EXCERPTS {
        let g = golden(excerpt);
        let r = run(&models, &g.f32("online.activations").unwrap());
        let states: Vec<usize> = g
            .i32("online.states")
            .unwrap()
            .into_iter()
            .map(|s| s as usize)
            .collect();
        assert_eq!(r.states, states, "{excerpt}: most likely states");
        assert_eq!(
            bits(&r.since),
            bits(&g.f64("online.since").unwrap()),
            "{excerpt}: since"
        );
        assert_eq!(
            bits(&r.period),
            bits(&g.f64("online.period").unwrap()),
            "{excerpt}: smoothed periods"
        );
        // (Python's last tick, at frame len, reads the last frame at the stream's end: A3's.)
        let py_ticks = g.f64("ticks.evidence").unwrap();
        assert_eq!(
            bits(&r.ticks),
            bits(&py_ticks[..r.ticks.len()]),
            "{excerpt}: tick evidence"
        );
        assert_eq!(r.ticks.len(), py_ticks.len() - 1);
        let field = |f: fn(&Commit) -> f64| bits(&r.commits.iter().map(f).collect::<Vec<_>>());
        assert_eq!(
            field(|c| c.t),
            bits(&g.f64("commits.t").unwrap()),
            "{excerpt}: committed beats"
        );
        assert_eq!(
            field(|c| c.known_at),
            bits(&g.f64("commits.knownAt").unwrap()),
            "{excerpt}: knownAt"
        );
        assert_eq!(
            field(|c| c.period),
            bits(&g.f64("commits.period").unwrap()),
            "{excerpt}: periods"
        );
        assert_eq!(
            field(|c| c.evidence),
            bits(&g.f64("commits.evidence").unwrap()),
            "{excerpt}: evidence"
        );
        eprintln!(
            "{excerpt}: {} frames, {} committed beats, all bit-equal",
            r.states.len(),
            r.commits.len()
        );
    }
}

#[test]
fn thirty_seconds_of_the_galop_with_its_tempo_flips_match_python() {
    // The hindsight golden excerpt (3000 frames, periods flipping between 0.28 and 1.07 s): past
    // the evidence window, with the believed last beat moving backwards at each flip. Its
    // `commits_unphased` are the commit rule without A4's phase check.
    let models = models();
    let g = golden("orpheus-galop-hindsight");
    let r = run(&models, &g.f32("online.activations").unwrap());
    assert_eq!(
        bits(&r.since),
        bits(&g.f64("online.since").unwrap()),
        "since"
    );
    assert_eq!(
        bits(&r.period),
        bits(&g.f64("online.period").unwrap()),
        "smoothed periods"
    );
    let py_ticks = g.f64("ticks.evidence").unwrap();
    assert_eq!(
        bits(&r.ticks),
        bits(&py_ticks[..r.ticks.len()]),
        "tick evidence"
    );
    let fields: [Field; 4] = [
        ("t", |c| c.t),
        ("knownAt", |c| c.known_at),
        ("period", |c| c.period),
        ("evidence", |c| c.evidence),
    ];
    for (name, f) in fields {
        let ours = bits(&r.commits.iter().map(f).collect::<Vec<_>>());
        assert_eq!(
            ours,
            bits(&g.f64(&format!("commits_unphased.{name}")).unwrap()),
            "commits {name}"
        );
    }
    eprintln!(
        "orpheus-galop 30 s: {} frames, {} committed beats, all bit-equal",
        r.since.len(),
        r.commits.len()
    );
}

#[test]
fn the_next_beat_is_known_when_a_beat_arrives() {
    // The commit invariant the evaluator relies on (docs/design/native-app.md, F1's inventory).
    let models = models();
    for excerpt in EXCERPTS {
        let r = run(&models, &golden(excerpt).f32("online.activations").unwrap());
        for w in r.commits.windows(2) {
            assert!(
                w[1].known_at <= w[0].t + 1e-9,
                "{excerpt}: beat at {} known only at {}",
                w[1].t,
                w[1].known_at
            );
            assert!(w[1].t > w[0].t, "{excerpt}: beats strictly increase");
        }
    }
}

#[test]
fn pairwise_sum_is_numpys() {
    // np.sum of the same values (numpy 2.2, tools/analysis), at lengths across every branch: under
    // 8 (sequential), 8 to 128 (eight accumulators), above 128 (halved recursively).
    let v: Vec<f64> = (0..300)
        .map(|i| f64::from(27 + (i * 37) % 83) / 100.0)
        .collect();
    let numpy: [(usize, u64); 9] = [
        (5, 0x400b_1eb8_51eb_851e),
        (8, 0x4014_3333_3333_3334),
        (9, 0x4017_28f5_c28f_5c2a),
        (17, 0x4025_d70a_3d70_a3d7),
        (64, 0x4045_6147_ae14_7ae1),
        (128, 0x4055_928f_5c28_f5c3),
        (129, 0x4055_a70a_3d70_a3d7),
        (200, 0x4060_ed1e_b851_eb86),
        (300, 0x4069_6a3d_70a3_d70a),
    ];
    for (n, want) in numpy {
        assert_eq!(
            pairwise_sum(&v[..n]).to_bits(),
            want,
            "np.sum of {n} values"
        );
    }
}

#[test]
fn from_pcm_the_whole_chain_commits_pythons_beats() {
    // A1's front end and ensemble (activations within float32 rounding of Python's) feeding the
    // DBN and the commit rule: the near-ties of the forward step never flip a state here.
    let models = models();
    for excerpt in EXCERPTS {
        let g = golden(excerpt);
        let mut fe = analysis::frontend::OnlineFrontEnd::new(&models).unwrap();
        let mut nn = analysis::rnn::OnlineEnsemble::new(&models).unwrap();
        let mut acts = Vec::new();
        fe.push(&g.f32("pcm").unwrap(), |fr| acts.push(nn.step(fr.features)));
        let r = run(&models, &acts);
        let states: Vec<usize> = g
            .i32("online.states")
            .unwrap()
            .into_iter()
            .map(|s| s as usize)
            .collect();
        let flipped = r.states.iter().zip(&states).filter(|(a, b)| a != b).count();
        let py_t = g.f64("commits.t").unwrap();
        eprintln!(
            "{excerpt} from PCM: {flipped} of {} states differ; {} of {} committed beats",
            states.len(),
            r.commits.len(),
            py_t.len()
        );
        assert_eq!(flipped, 0, "{excerpt}: states from Rust's activations");
        assert_eq!(
            bits(&r.commits.iter().map(|c| c.t).collect::<Vec<_>>()),
            bits(&py_t),
            "{excerpt}: committed beats from PCM"
        );
    }
}
