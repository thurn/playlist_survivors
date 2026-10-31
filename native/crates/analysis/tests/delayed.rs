//! E1: Delayed listening's committed beats on the 30 s Galop excerpt, at the live `D`:
//! - every other group is Direct listening's, entry for entry;
//! - the streamed beats are [`delayed::replay`] of the Direct run's entries;
//! - the lead: every committed beat is known at least `H` (at its period) before it falls;
//! - stamps never go backwards;
//! - stamping every group (the Unity game's mode) changes only the other groups' stamps, each
//!   `M − A` before the analysis position that produced it;
//! - the stream's causality by audio position: the stream of audio cut at X holds exactly the
//!   full stream's beats committed from positions before X (`known_at + M − A`).
#![forbid(unsafe_code)]

use std::path::{Path, PathBuf};
use std::sync::OnceLock;

use analysis::analyzer::{Analyzer, Entry, Params};
use analysis::beats::{BeatParams, Commit};
use analysis::delayed::{self, DelayedParams, Listening, Stamps};
use analysis::flat::{self, Record};
use analysis::hindsight::HindsightParams;
use analysis::tensors::Tensors;

const SR: f64 = 44100.0;

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn models() -> &'static Tensors {
    static M: OnceLock<Tensors> = OnceLock::new();
    M.get_or_init(|| Tensors::load(&root().join("native/models/madmom.bin")).unwrap())
}

fn pcm() -> &'static [f32] {
    static P: OnceLock<Vec<f32>> = OnceLock::new();
    P.get_or_init(|| {
        Tensors::load(&root().join("native/models/golden/orpheus-galop-hindsight.bin"))
            .unwrap()
            .f32("pcm")
            .unwrap()
    })
}

fn params(listening: Listening) -> Params {
    Params {
        hindsight: HindsightParams::live(),
        listening,
        ..Params::default()
    }
}

fn analyse(samples: &[f32], listening: Listening) -> Vec<Entry> {
    let mut an = Analyzer::new(models(), params(listening)).unwrap();
    let mut out = Vec::new();
    for block in samples.chunks(4096) {
        an.push(block, &mut out);
    }
    an.finish(&mut out);
    out
}

fn chosen() -> DelayedParams {
    DelayedParams::chosen()
}

fn direct() -> &'static [Entry] {
    static D: OnceLock<Vec<Entry>> = OnceLock::new();
    D.get_or_init(|| analyse(pcm(), Listening::Direct))
}

fn delayed_run() -> &'static [Entry] {
    static D: OnceLock<Vec<Entry>> = OnceLock::new();
    D.get_or_init(|| analyse(pcm(), Listening::Delayed(chosen())))
}

fn every_group() -> DelayedParams {
    DelayedParams {
        stamps: Stamps::EveryGroup,
        ..chosen()
    }
}

fn beats(entries: &[Entry]) -> Vec<Commit> {
    entries
        .iter()
        .filter_map(|e| {
            if let Entry::Beat(c) = e {
                Some(*c)
            } else {
                None
            }
        })
        .collect()
}

#[test]
fn delayed_listening_changes_only_the_committed_beats() {
    let others = |es: &[Entry]| -> Vec<Entry> {
        es.iter()
            .filter(|e| !matches!(e, Entry::Beat(_)))
            .cloned()
            .collect()
    };
    assert!(
        others(delayed_run()) == others(direct()),
        "a group other than the beats changed"
    );
    assert_ne!(beats(delayed_run()), beats(direct()));
}

#[test]
fn streamed_beats_are_the_replay_of_direct_listening() {
    let end = pcm().len() as f64 / SR;
    let replayed = delayed::replay(
        direct(),
        end,
        chosen(),
        &HindsightParams::live(),
        &BeatParams::default(),
    );
    assert_eq!(beats(delayed_run()), replayed);
    assert!(replayed.len() > 40, "{} beats", replayed.len());
}

#[test]
fn every_beat_leads_by_the_horizon_and_stamps_never_go_back() {
    let b = BeatParams::default();
    let got = beats(delayed_run());
    for c in &got {
        let h = b.commit_periods * c.period + b.commit_extra_s;
        assert!(
            c.t - c.known_at >= h - 1e-9,
            "{c:?}: lead {} under H {h}",
            c.t - c.known_at
        );
    }
    assert!(
        got.windows(2)
            .all(|w| w[1].known_at >= w[0].known_at && w[1].t > w[0].t)
    );
}

fn records(entries: &[Entry]) -> Vec<Record> {
    let mut out = Vec::new();
    for e in entries {
        flat::push(e, &mut out);
    }
    out
}

#[test]
fn stamping_every_group_changes_only_stamps() {
    let p = every_group();
    let native = delayed_run();
    let game = analyse(pcm(), Listening::Delayed(p));
    assert_eq!(beats(&game), beats(native));
    let (want, got) = (records(native), records(&game));
    assert_eq!(got.len(), want.len());
    let mut kinds = std::collections::BTreeSet::new();
    for (g, w) in got.iter().zip(&want) {
        assert_eq!((g.kind, g.t.to_bits(), g.v), (w.kind, w.t.to_bits(), w.v));
        if g.kind == flat::BEAT {
            continue;
        }
        kinds.insert(g.kind);
        // Natively a tick and a timbre entry are known at their own time: their position.
        if matches!(w.kind, flat::TICK | flat::TIMBRE) {
            assert_eq!(w.known_at, w.t, "{w:?}");
        }
        assert_eq!(g.known_at, p.stamp(w.known_at), "{g:?} from {w:?}");
        assert!(
            (w.known_at - g.known_at - (p.m - p.a)).abs() < 1e-9,
            "{g:?} from {w:?}"
        );
    }
    // (The excerpt has no structure entries or accent forecasts; one rule restamps every group
    // but the beats.)
    for kind in [
        flat::TICK,
        flat::ONSET,
        flat::HINDSIGHT,
        flat::HINDSIGHT_BEAT,
        flat::TIMBRE,
    ] {
        assert!(kinds.contains(&kind), "no entry of kind {kind}: {kinds:?}");
    }
}

#[test]
#[ignore = "release: the Galop excerpt analysed once more, cut"]
fn the_stream_is_causal_by_audio_position() {
    let p = chosen();
    let full = beats(delayed_run());
    let cut = 12.37;
    let part = beats(&analyse(
        &pcm()[..(cut * SR).round() as usize],
        Listening::Delayed(p),
    ));
    let by = |bs: &[Commit]| -> Vec<Commit> {
        bs.iter()
            .copied()
            .filter(|c| c.known_at + p.m - p.a <= cut - 0.05 + 1e-9)
            .collect()
    };
    let (want, got) = (by(&full), by(&part));
    assert!(!want.is_empty());
    assert_eq!(got, want, "beats committed before {cut} differ");
}
