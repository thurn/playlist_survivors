use super::{Arrival, rebase_append};
use serde::{Deserialize, Serialize};

/// A key as the engine applied it (`keys.jsonl`): its run, run time, key and whether pressed.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct AppliedKey {
    pub run: usize,
    pub s: f64,
    pub key: String,
    pub pressed: bool,
}

/// A recorded run to replay: its start and end (session time) and its settled records' hash and
/// count as the live run left them.
#[derive(Clone, Debug, PartialEq)]
pub struct RecordedRun {
    pub index: usize,
    pub s0: f64,
    pub end: f64,
    pub hash: u64,
    pub records: usize,
}

/// What a replay found: the hash of its first `records` settled records (`None` if it settled
/// fewer), and how many it settled.
#[derive(Clone, Debug, PartialEq)]
pub struct Replayed {
    pub hash: Option<u64>,
    pub settled: usize,
    pub rollbacks: u64,
}

/// Replays `run` on `engine` (fresh, built as the app builds it): the session's arrivals fed at
/// their arrival times through [`rebase_append`] (those of the last `recent_s` before the start
/// first, as the app feeds them), the keys at the times the live engine applied them, 120 frames a
/// second without lookahead, to past the run's end. Keys applied at any time settle as they were
/// applied, so the settled records must be the live run's.
pub fn replay_run(
    engine: &mut crate::live::Engine,
    arrivals: &[Arrival],
    keys: &[AppliedKey],
    run: &RecordedRun,
    recent_s: f64,
) -> Result<Replayed, String> {
    use sim::movement::InputKey;
    engine.track_prefix(run.records);
    let mut next = arrivals
        .iter()
        .position(|a| a.arrived >= run.s0 - recent_s)
        .unwrap_or(arrivals.len());
    let keys: Vec<(&AppliedKey, InputKey)> = keys
        .iter()
        .filter(|k| k.run == run.index)
        .map(|k| InputKey::parse_recorded(&k.key).map(|key| (k, key)))
        .collect::<Result<_, _>>()?;
    let mut next_key = 0;
    let span = run.end - run.s0 + 1.0;
    let mut frame = 0u32;
    loop {
        frame += 1;
        let now = f64::from(frame) / 120.0;
        let fed = &arrivals[next..];
        let n = fed.iter().take_while(|a| a.arrived - run.s0 <= now).count();
        if n > 0 {
            let s0 = run.s0;
            engine.feed(|log| {
                for a in &fed[..n] {
                    rebase_append(log, &a.entry, s0);
                }
            });
            next += n;
        }
        while next_key < keys.len() && keys[next_key].0.s <= now {
            let (k, key) = keys[next_key];
            engine.key(k.s, key, k.pressed);
            next_key += 1;
        }
        engine.frame(now, 0.0);
        // An offline replay consumes state/hash, not transient presentation events.
        engine.view.borrow_mut().take_moments();
        if now > span {
            break;
        }
    }
    Ok(Replayed {
        hash: engine.tracked_prefix(),
        settled: engine.settled_count,
        rollbacks: engine.stats.rollbacks,
    })
}
