//! Exercise the fast driver against the committed Rust stream, then the actual
//! session replay implementation. No analyzer runs inside the gate.
use eval::{
    live::Engine,
    session::{AppliedKey, Arrival, RecordedRun, replay_run},
};
use sim::{
    content::{SourceFile, load_content},
    movement::InputKey,
    player::{KeyEvent, KeyEventKind, Observation, Player},
};
use std::{
    path::{Path, PathBuf},
    sync::OnceLock,
};
fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}
fn engine() -> Engine {
    let mut paths: Vec<_> = std::fs::read_dir(root().join("content"))
        .unwrap()
        .map(|p| p.unwrap().path())
        .filter(|p| p.extension().is_some_and(|x| x == "yaml"))
        .collect();
    paths.sort();
    let files: Vec<_> = paths
        .iter()
        .map(|p| SourceFile {
            name: p.display().to_string(),
            text: std::fs::read_to_string(p).unwrap(),
        })
        .collect();
    let lib = load_content(&files).unwrap();
    let level = SourceFile {
        name: "survivors.yaml".into(),
        text: std::fs::read_to_string(root().join("levels/survivors.yaml")).unwrap(),
    };
    Engine::new(
        &level,
        &lib,
        &indexmap::IndexMap::new(),
        0.2,
        true,
        true,
        f64::INFINITY,
    )
    .unwrap()
}
fn fixture() -> &'static [Arrival] {
    static DATA: OnceLock<Vec<Arrival>> = OnceLock::new();
    DATA.get_or_init(|| {
        #[derive(serde::Deserialize)]
        struct Fixture {
            arrivals: Vec<Arrival>,
        }
        let f: Fixture =
            serde_json::from_str(include_str!("../../../fixtures/survivors-3m.json")).unwrap();
        f.arrivals
    })
}
#[derive(Default)]
struct Held(bool);
impl Player for Held {
    fn next_decision(&mut self, after: f64) -> f64 {
        after.max(0.0) + 0.1
    }
    fn decide(&mut self, o: &Observation) -> Vec<KeyEvent> {
        if self.0 {
            Vec::new()
        } else {
            self.0 = true;
            vec![KeyEvent {
                s: o.s,
                key: InputKey::Right,
                kind: KeyEventKind::Press,
                id: None,
            }]
        }
    }
}
#[test]
fn a_fast_run_replays_the_rust_fixture_exactly() {
    let mut e = engine();
    e.key_log = Some(Vec::new());
    let r = eval::survive::drive(&mut e, fixture(), &mut Held::default(), 12.0, 12.0);
    assert!(r.survival_s > 10.0);
    assert!(r.peak_enemies > 0);
    assert!(r.settled_records > 100);
    let keys: Vec<_> = e
        .key_log
        .as_ref()
        .unwrap()
        .iter()
        .map(|k| AppliedKey {
            run: 0,
            s: k.s,
            key: format!("{:?}", k.key),
            pressed: k.kind == KeyEventKind::Press,
        })
        .collect();
    let run = RecordedRun {
        index: 0,
        s0: 0.0,
        end: r.song_s,
        hash: e.settled_hash,
        records: e.settled_count,
    };
    let replay = replay_run(&mut engine(), fixture(), &keys, &run, 20.0).unwrap();
    assert_eq!(replay.hash, Some(run.hash));
}
#[test]
fn future_stream_entries_cannot_change_a_driven_prefix() {
    let all = fixture();
    let prefix: Vec<_> = all
        .iter()
        .take_while(|a| a.arrived <= 8.0)
        .cloned()
        .collect();
    let a = eval::survive::drive(&mut engine(), all, &mut Held::default(), 8.0, 8.0);
    let b = eval::survive::drive(&mut engine(), &prefix, &mut Held::default(), 8.0, 8.0);
    assert_eq!(a.settled_hash, b.settled_hash);
    assert_eq!(a.settled_records, b.settled_records);
}

#[test]
fn positioning_survives_the_three_minute_rust_fixture_smoke() {
    use players::{SurvivorBot, SurvivorBotKind};
    let start = std::time::Instant::now();
    let report = eval::survive::drive(
        &mut engine(),
        fixture(),
        &mut SurvivorBot::new(SurvivorBotKind::Positioning, 1),
        180.0,
        180.0,
    );
    eprintln!(
        "SF9 gate smoke: {:.3} song s, {:.3} run s, HP min {:.3}, level {}, peak {}, records {}, {:.3} wall s",
        report.song_s,
        report.survival_s,
        report.lowest_hp_share,
        report.level,
        report.peak_enemies,
        report.settled_records,
        start.elapsed().as_secs_f64()
    );
    assert!(
        !report.fallen,
        "positioning must survive the fixture prefix"
    );
    // Level stays report-only; full-run HP-floor balance begins gating at SW7.
}

#[test]
fn all_survivor_bots_replay_their_real_keys_and_only_see_known_input() {
    use players::{SurvivorBot, SurvivorBotKind};
    for kind in [
        SurvivorBotKind::Positioning,
        SurvivorBotKind::Afk,
        SurvivorBotKind::RandomPick,
    ] {
        let mut e = engine();
        e.key_log = Some(Vec::new());
        let r = eval::survive::drive(
            &mut e,
            fixture(),
            &mut SurvivorBot::new(kind, 7),
            60.0,
            60.0,
        );
        let keys: Vec<_> = e
            .key_log
            .as_ref()
            .unwrap()
            .iter()
            .map(|k| AppliedKey {
                run: 0,
                s: k.s,
                key: format!("{:?}", k.key),
                pressed: k.kind == KeyEventKind::Press,
            })
            .collect();
        let run = RecordedRun {
            index: 0,
            s0: 0.0,
            end: r.song_s,
            hash: e.settled_hash,
            records: e.settled_count,
        };
        let replay = replay_run(&mut engine(), fixture(), &keys, &run, 70.0).unwrap();
        assert_eq!(replay.hash, Some(run.hash), "{} replay", kind.as_str());
        let prefix: Vec<_> = fixture()
            .iter()
            .take_while(|a| a.arrived <= 60.0)
            .cloned()
            .collect();
        let short = eval::survive::drive(
            &mut engine(),
            &prefix,
            &mut SurvivorBot::new(kind, 7),
            60.0,
            60.0,
        );
        assert_eq!(
            r.settled_hash,
            short.settled_hash,
            "{} future-input leak",
            kind.as_str()
        );
    }
}

/// The app's showcase (ps-8sb.4): Delayed listening's arrivals (most 3 s before they are known,
/// beats committed late as they are known), frames at a display's uneven times drawn 45 ms ahead
/// of the time heard, the simulation run 0.2 s ahead of that, and the virtual player pressing
/// where the simulation has run to. `replay_run` (120 Hz, no lead, the keys fed at their times)
/// must settle the same records. It once did not: a key at the simulation's very stop, the clock
/// moved between stops to where a frame or a snapshot asked, and an arrival heard before the drawn
/// frame's settled time each made the two differ.
#[test]
fn a_virtual_player_pressing_at_the_frontier_replays_under_delayed_arrivals() {
    use eval::session::{Entry, known_floor, rebase_append};
    use players::{SurvivorBot, SurvivorBotKind};
    let end = 60.0;
    let mut arrivals: Vec<Arrival> = fixture()
        .iter()
        .map(|a| Arrival {
            arrived: a.entry.known_at()
                - if matches!(a.entry, Entry::Beat(_)) {
                    0.0
                } else {
                    3.0
                },
            entry: a.entry.clone(),
        })
        .filter(|a| a.arrived <= end)
        .collect();
    arrivals.sort_by(|a, b| a.arrived.total_cmp(&b.arrived));
    let mut heard = Vec::new();
    let mut t = 0.0;
    for i in 0..u32::MAX {
        t += (0.6 + 0.8 * (f64::from(i) * 12.9898).sin().abs()) / 120.0;
        if t > end {
            break;
        }
        heard.push(t);
    }
    let mut e = engine();
    e.key_log = Some(Vec::new());
    let mut bot = SurvivorBot::new(SurvivorBotKind::Positioning, 1);
    let mut next = 0;
    for &heard in &heard {
        // (Stamped as the app stamps them: never before the run's settled time.)
        let n = arrivals[next..]
            .iter()
            .take_while(|a| a.arrived <= heard)
            .count();
        let floor = known_floor(e.settled_s(), 0.0);
        for a in &mut arrivals[next..next + n] {
            a.entry = a.entry.clone().stamped(floor);
        }
        e.feed(|log| {
            for a in &arrivals[next..next + n] {
                rebase_append(log, &a.entry, 0.0);
            }
        });
        next += n;
        e.frame(heard + 0.045, 0.2);
        for k in bot.decide(&e.sim.observe()) {
            e.key(k.s, k.key, k.kind == KeyEventKind::Press);
        }
    }
    let keys: Vec<_> = e
        .key_log
        .as_ref()
        .unwrap()
        .iter()
        .map(|k| AppliedKey {
            run: 0,
            s: k.s,
            key: format!("{:?}", k.key),
            pressed: k.kind == KeyEventKind::Press,
        })
        .collect();
    assert!(keys.len() > 20, "{} keys", keys.len());
    let run = RecordedRun {
        index: 0,
        s0: 0.0,
        end,
        hash: e.settled_hash,
        records: e.settled_count,
    };
    let replay = replay_run(&mut engine(), &arrivals, &keys, &run, 20.0).unwrap();
    assert_eq!(
        replay.hash,
        Some(run.hash),
        "{} records settled live",
        run.records
    );
}
