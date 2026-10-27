//! Classic mode's fairness: a full groove from the start, eighth notes as natural as the beat,
//! penalties only when the press is surely off a clear beat, and the view told when judging pauses.
use super::*;
use crate::timing::{CountedRating, KeyGroup, PressClass, PressMemo, classify_press, index_key};

fn hero() -> crate::content::types::HeroDef {
    sim_in(&stream(), false).env.input.lib.hero.clone()
}

/// A 120 bpm grid from 0.
fn grid() -> Vec<f64> {
    (0..16).map(|i| 0.5 * f64::from(i)).collect()
}

/// A grid at `bpm` from 0.
fn grid_at(bpm: f64) -> Vec<f64> {
    (0..40).map(|i| 60.0 / bpm * f64::from(i)).collect()
}

fn classify_on(
    hero: &crate::content::types::HeroDef,
    grid: &[f64],
    memo: &mut PressMemo,
    s: f64,
) -> crate::timing::PressJudgement {
    classify_press(
        hero,
        grid,
        s,
        false,
        memo,
        KeyGroup::Direction,
        &mut index_key,
    )
}

fn classify(
    hero: &crate::content::types::HeroDef,
    memo: &mut PressMemo,
    s: f64,
) -> crate::timing::PressJudgement {
    classify_press(
        hero,
        &grid(),
        s,
        false,
        memo,
        KeyGroup::Direction,
        &mut index_key,
    )
}

#[test]
fn eighth_notes_never_cost_groove() {
    let hero = hero();
    let mut memo = PressMemo::default();
    // On the beat, then the off-beat a little early or late (within Great), bar after bar.
    for (k, wobble) in [0.0, 0.06, -0.07, 0.03, -0.05, 0.08]
        .into_iter()
        .enumerate()
    {
        let beat = 0.5 * k as f64 + 1.0;
        let on = classify(&hero, &mut memo, beat + 0.01);
        assert_eq!(on.class, PressClass::Counted);
        assert!(on.change > 0.0, "the beat at {beat}");
        let off = classify(&hero, &mut memo, beat + 0.25 + wobble);
        assert_eq!(off.class, PressClass::Offbeat, "the off-beat after {beat}");
        assert!(
            off.change >= 0.0,
            "the off-beat after {beat} costs {}",
            off.change
        );
    }
}

#[test]
fn a_lone_off_beat_never_takes_the_next_beat() {
    let hero = hero();
    let mut memo = PressMemo::default();
    // Off-beats only (no press on the beat before), then the beat itself.
    let off = classify(&hero, &mut memo, 1.25);
    assert_eq!(off.class, PressClass::Offbeat);
    assert_eq!(off.change, 0.0);
    let on = classify(&hero, &mut memo, 1.5);
    assert_eq!(on.class, PressClass::Counted);
    assert_eq!(on.rating, Some(CountedRating::Perfect));
}

#[test]
fn finding_the_beat_after_a_loose_press_earns_it() {
    let hero = hero();
    let mut memo = PressMemo::default();
    let m = &hero.meter;
    // 150 ms early (loose, not near the off-beat), then on the beat.
    let loose = classify(&hero, &mut memo, 1.35);
    assert_eq!(loose.rating, Some(CountedRating::Loose));
    assert!(loose.change < 0.0);
    let found = classify(&hero, &mut memo, 1.5);
    assert_eq!(found.class, PressClass::Counted, "not a stray");
    assert_eq!(found.rating, Some(CountedRating::Perfect));
    assert_eq!(found.change, m.counted.perfect, "earned, never more");
    // A second press on a beat that is already well taken is still a stray.
    let stray = classify(&hero, &mut memo, 1.62);
    assert_eq!(stray.class, PressClass::Stray);
}

#[test]
fn classic_mode_starts_with_a_full_groove_at_the_top_tier() {
    let sim = sim_in(&stream(), false);
    let top = crate::timing::tier_for(&sim.env.input.lib.hero, 0.0, 1.0);
    assert_eq!(sim.st.groove, 1.0);
    assert_eq!(sim.st.tier, top);
}

#[test]
fn penalties_wait_for_a_confident_beat() {
    for (evidence, costs) in [(0.3, false), (0.9, true)] {
        let mut file = stream();
        // The app's gate: judged from 0.2, but only sure of the beat from FAIR.from.
        file.params.gate = 0.2;
        for t in &mut file.log.ticks {
            t.evidence = evidence;
        }
        let mut sim = sim_in(&file, false);
        sim.advance_to(1.9);
        assert!(sim.clear_at(1.9), "judged at evidence {evidence}");
        let before = sim.st.groove;
        // Loose presses (150 ms off the beat, far from the off-beat), and no press raising it.
        for b in 4..10 {
            let s = f64::from(b) * 0.5 + 0.65;
            key(&mut sim, s, KeyEventKind::Press, InputKey::Right);
            key(&mut sim, s + 0.05, KeyEventKind::Release, InputKey::Right);
        }
        sim.advance_to(6.0);
        assert_eq!(
            sim.st.groove < before,
            costs,
            "evidence {evidence}: {before} -> {}",
            sim.st.groove
        );
    }
}

#[test]
fn the_view_hears_when_classic_judging_pauses() {
    use std::cell::RefCell;
    #[derive(Default)]
    struct Judging(Vec<(f64, bool)>);
    impl crate::recorder::Recorder for Judging {
        fn judging(&mut self, s: f64, judged: bool) {
            self.0.push((s, judged));
        }
    }
    for zen in [false, true] {
        let mut file = stream();
        // The beat goes unclear from 4 s.
        for t in &mut file.log.ticks {
            if t.t >= 4.0 {
                t.evidence = 0.0;
            }
        }
        let mut sim = sim_in(&file, zen);
        let rec = Rc::new(RefCell::new(Judging::default()));
        sim.rec = Box::new(rec.clone());
        sim.advance_to(6.5);
        let seen = rec.borrow().0.clone();
        if zen {
            assert!(seen.is_empty(), "zen mode judges nothing: {seen:?}");
            continue;
        }
        let flips: Vec<bool> = seen.iter().map(|x| x.1).collect();
        assert_eq!(flips, [false, true, false], "{seen:?}");
        assert!(seen[2].0 >= 4.0 && seen[2].0 < 4.2, "{seen:?}");
    }
}

#[test]
fn trackers_that_disagree_make_the_beat_ambiguous() {
    use crate::reactive::settle::{AMBIGUITY_SPAN_S, grids_agree_near};
    let beats: Vec<f64> = (0..20).map(|i| 0.5 * f64::from(i)).collect();
    let entry = |online: Vec<f64>| stream::Hindsight {
        known_at: 10.0,
        offline: beats.clone(),
        online,
    };
    let agree = |online: Vec<f64>| grids_agree_near(&entry(online), 5.0, AMBIGUITY_SPAN_S);
    assert!(
        agree(beats.iter().map(|t| t + 0.02).collect()),
        "the same beat"
    );
    assert!(
        !agree(beats.iter().step_by(2).copied().collect()),
        "half time"
    );
    assert!(
        !agree(beats.iter().map(|t| t + 0.25).collect()),
        "the off-beat"
    );
    assert!(!agree(Vec::new()), "no online beat");
}

#[test]
fn early_or_late_players_on_a_fast_beat_are_counted_for_the_drift() {
    let hero = hero();
    let grid = grid_at(174.0);
    for lead in [-0.1, 0.1] {
        let mut memo = PressMemo::default();
        let mut deltas = Vec::new();
        for k in 4..24 {
            let j = classify_on(&hero, &grid, &mut memo, grid[k] + lead);
            assert_eq!(j.class, PressClass::Counted, "{lead} s off beat {k}");
            assert_eq!(j.grid_index, k as isize);
            deltas.push(j.delta.abs());
        }
        // The drift (at most 80 ms) takes most of a steady 100 ms off once it has 8 presses.
        assert!(deltas[0] > 0.09 && deltas[19] < 0.03, "{lead}: {deltas:?}");
    }
}

#[test]
fn fast_eighth_notes_on_the_off_beat_stay_free() {
    let hero = hero();
    let grid = grid_at(174.0);
    let mut memo = PressMemo::default();
    for k in 4..12 {
        let on = classify_on(&hero, &grid, &mut memo, grid[k] + 0.01);
        assert_eq!(on.class, PressClass::Counted);
        let mid = (grid[k] + grid[k + 1]) / 2.0;
        let off = classify_on(&hero, &grid, &mut memo, mid + 0.03);
        assert_eq!(off.class, PressClass::Offbeat, "the off-beat after {k}");
        assert_eq!(off.change, 0.0);
    }
}

#[test]
fn a_double_tap_is_free_and_never_takes_the_beat() {
    let hero = hero();
    let m = &hero.meter;
    let mut memo = PressMemo::default();
    // Good (100 ms early), then 60 ms later a Perfect-looking tap: a double tap, not a takeover.
    let good = classify(&hero, &mut memo, 1.4);
    assert_eq!(good.rating, Some(CountedRating::Good));
    let double = classify(&hero, &mut memo, 1.46);
    assert_eq!(double.class, PressClass::Double);
    assert_eq!(double.change, m.double_tap);
    // On the next beat: loose early, then a Perfect beyond the double tap takes the beat, once.
    let early = classify(&hero, &mut memo, 1.85);
    assert_eq!(early.rating, Some(CountedRating::Loose));
    let found = classify(&hero, &mut memo, 2.01);
    assert_eq!(found.class, PressClass::Counted);
    assert_eq!(found.rating, Some(CountedRating::Perfect));
    let again = classify(&hero, &mut memo, 2.12);
    assert_eq!(again.class, PressClass::Stray, "a taken beat is taken once");
    assert_eq!(again.change, m.stray);
}

#[test]
fn settled_penalties_wait_for_the_trackers_to_agree() {
    use crate::recorder::LogRecorder;
    use std::cell::RefCell;
    let mut changes = Vec::new();
    for disagree in [false, true] {
        let mut file = stream();
        let beats: Vec<f64> = file.log.beats.iter().map(|b| b.t).collect();
        // A hindsight entry every half second: the online tracker on the beat, or on the off-beat.
        file.log.hindsight = Some(
            (2..14)
                .map(|k| stream::Hindsight {
                    known_at: 0.5 * f64::from(k),
                    offline: beats.clone(),
                    online: beats
                        .iter()
                        .map(|t| if disagree { t + 0.25 } else { *t })
                        .collect(),
                })
                .collect(),
        );
        let mut sim = sim_with(&file, false, true);
        let rec = Rc::new(RefCell::new(LogRecorder::default()));
        sim.rec = Box::new(rec.clone());
        // Loose presses, 150 ms late (far from the off-beat).
        for beat in &beats[2..7] {
            let s = beat + 0.15;
            key(&mut sim, s, KeyEventKind::Press, InputKey::Right);
            key(&mut sim, s + 0.05, KeyEventKind::Release, InputKey::Right);
        }
        sim.advance_to(6.9);
        let settled: Vec<(String, f64)> = rec
            .borrow()
            .settles
            .iter()
            .map(|r| {
                (
                    r["settled"].as_str().unwrap_or("").to_string(),
                    r["change"].as_f64().unwrap(),
                )
            })
            .collect();
        assert!(
            settled.iter().any(|(label, _)| label == "loose"),
            "disagree {disagree}: {settled:?}"
        );
        changes.push(settled.iter().map(|x| x.1).sum::<f64>());
    }
    assert!(
        changes[0] < 0.0,
        "agreeing trackers: loose presses cost ({changes:?})"
    );
    assert_eq!(
        changes[1], 0.0,
        "disagreeing trackers: nothing costs ({changes:?})"
    );
}
