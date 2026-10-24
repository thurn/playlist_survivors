//! Every shipped `stream.json` parses, each group is in `known_at` order (which Replay feeding
//! relies on), and Replay fed moment by moment never adds an entry before it is known and ends with
//! the whole stream.

use std::path::{Path, PathBuf};

use stream::{Replay, StreamFile, StreamLog};

fn streams() -> Vec<(String, StreamFile)> {
    let dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../public/reactive");
    let mut paths: Vec<PathBuf> = std::fs::read_dir(&dir)
        .unwrap()
        .map(|e| e.unwrap().path().join("stream.json"))
        .filter(|p| p.exists())
        .collect();
    paths.sort();
    assert!(
        paths.len() >= 60,
        "{} streams under {}",
        paths.len(),
        dir.display()
    );
    paths
        .into_iter()
        .map(|p| {
            let name = p
                .parent()
                .unwrap()
                .file_name()
                .unwrap()
                .to_string_lossy()
                .into_owned();
            let file = StreamFile::parse(&std::fs::read_to_string(&p).unwrap())
                .unwrap_or_else(|e| panic!("{name}: {e}"));
            (name, file)
        })
        .collect()
}

/// Each group's `known_at`s, by name.
fn known(log: &StreamLog) -> Vec<(&'static str, Vec<f64>)> {
    let opt = |v: Option<Vec<f64>>| v.unwrap_or_default();
    vec![
        ("beats", log.beats.iter().map(|x| x.known_at).collect()),
        ("ticks", log.ticks.iter().map(|x| x.known_at).collect()),
        ("onsets", log.onsets.iter().map(|x| x.known_at).collect()),
        (
            "hindsight",
            opt(log
                .hindsight
                .as_ref()
                .map(|v| v.iter().map(|x| x.known_at).collect())),
        ),
        (
            "timbre",
            opt(log
                .timbre
                .as_ref()
                .map(|v| v.iter().map(|x| x.known_at).collect())),
        ),
        (
            "changes",
            opt(log
                .changes
                .as_ref()
                .map(|v| v.iter().map(|x| x.known_at).collect())),
        ),
        (
            "jumps",
            opt(log
                .jumps
                .as_ref()
                .map(|v| v.iter().map(|x| x.known_at).collect())),
        ),
        (
            "ends",
            opt(log
                .ends
                .as_ref()
                .map(|v| v.iter().map(|x| x.known_at).collect())),
        ),
        (
            "forecasts",
            opt(log
                .forecasts
                .as_ref()
                .map(|v| v.iter().map(|x| x.known_at).collect())),
        ),
    ]
}

#[test]
fn groups_are_in_known_at_order() {
    for (name, file) in streams() {
        for (group, ks) in known(&file.log) {
            if let Some(i) = ks.windows(2).position(|w| w[1] < w[0]) {
                panic!(
                    "{name}: {group}[{}] is known at {} before {group}[{i}] at {}",
                    i + 1,
                    ks[i + 1],
                    ks[i]
                );
            }
        }
        // A tick is known at its own time in Replay.
        assert!(file.log.ticks.iter().all(|t| t.known_at == t.t), "{name}");
    }
}

#[test]
fn replay_feeds_each_entry_once_known_and_ends_whole() {
    for (name, file) in streams() {
        let mut replay = Replay::new(file.log.clone());
        let mut log = replay.empty_log();
        let mut s = replay.next_known_at();
        let mut moments = 0;
        while s.is_finite() {
            replay.feed_to(&mut log, s);
            for (group, ks) in known(&log) {
                let tolerance = if group == "ticks" { 1e-9 } else { 0.0 };
                assert!(
                    ks.iter().all(|&k| k <= s + tolerance),
                    "{name}: {group} has an entry not known by {s}"
                );
            }
            let next = replay.next_known_at();
            assert!(
                next > s,
                "{name}: feeding to {s} left an entry known by then"
            );
            s = next;
            moments += 1;
        }
        assert!(moments > 1, "{name}");
        assert_eq!(
            log, file.log,
            "{name}: Replay did not end with the whole stream"
        );
    }
}

#[test]
fn split_hands_back_the_reference_apart() {
    let dir = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../public/reactive/orpheus-galop/stream.json");
    let text = std::fs::read_to_string(dir).unwrap();
    let (file, reference) = StreamFile::split(&text).unwrap();
    assert_eq!(file, StreamFile::parse(&text).unwrap());
    assert!(reference.is_some_and(|r| r.get("beats").is_some()));
}

#[test]
fn ticks_are_read_by_time_and_arrival() {
    let (_, file) = streams()
        .into_iter()
        .find(|(n, _)| n == "orpheus-galop")
        .unwrap();
    let log = &file.log;
    let t5 = log.ticks[5].t;
    assert_eq!(log.tick_index_at(t5), 5);
    assert_eq!(log.tick_index_at(t5 - 1e-6), 4);
    assert_eq!(log.tick_index_at(log.ticks[0].t - 1.0), -1);
    assert_eq!(log.energy_at(log.ticks[0].t - 1.0), 0.0);
    // Live, a tick that arrives after its time is not read before it arrives.
    let mut late = log.clone();
    late.ticks[5].known_at = t5 + 0.5;
    assert_eq!(late.tick_index_at(t5), 4);
    // The latest tick both past and known: the next one, once it is.
    assert_eq!(late.tick_index_at(log.ticks[6].t), 6);
}
