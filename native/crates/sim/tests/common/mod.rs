//! Shared by the live-run and causality tests: the content, the streams and the fixtures' keys, and
//! live runs of the sim on a stream (never with the stream's end).
#![allow(dead_code, clippy::case_sensitive_file_extension_comparisons)]

use std::cell::RefCell;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::rc::Rc;
use std::sync::OnceLock;

use indexmap::IndexMap;
use serde_json::Value;
use sim::content::level::load_level;
use sim::content::{ContentLibrary, SourceFile, load_content};
use sim::engine::{AnimInfo, Sim, SimInput};
use sim::movement::InputKey;
use sim::player::{KeyEvent, KeyEventKind};
use sim::recorder::LogRecorder;
use sim::schedule::{grow_song_map, reactive_song_map};
use stream::{End, Level, StreamFile, StreamLog, Tick};

pub fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

pub fn source(rel: &str) -> SourceFile {
    SourceFile {
        name: rel.to_string(),
        text: std::fs::read_to_string(root().join(rel)).unwrap(),
    }
}

/// The content library, loaded once per test binary.
pub fn library() -> &'static ContentLibrary {
    static LIB: OnceLock<ContentLibrary> = OnceLock::new();
    LIB.get_or_init(|| {
        let mut names: Vec<String> = std::fs::read_dir(root().join("content"))
            .unwrap()
            .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
            .filter(|n| n.ends_with(".yaml"))
            .collect();
        names.sort();
        let files: Vec<SourceFile> = names
            .iter()
            .map(|n| source(&format!("content/{n}")))
            .collect();
        load_content(&files).unwrap()
    })
}

/// The hero atlas's animations.
pub fn atlas() -> IndexMap<String, AnimInfo> {
    let a: Value = serde_json::from_str(
        &std::fs::read_to_string(root().join("public/assets/characters.json")).unwrap(),
    )
    .unwrap();
    a["sprites"]["hero"]["animations"]
        .as_object()
        .unwrap()
        .iter()
        .map(|(k, v)| {
            (
                k.clone(),
                AnimInfo {
                    frames: v["frames"]["down"].as_array().unwrap().len() as f64,
                    impact_frame: v["impactFrame"].as_f64(),
                },
            )
        })
        .collect()
}

pub fn gz_lines(p: &Path) -> Vec<Value> {
    let raw = std::fs::read(p).unwrap();
    let mut text = String::new();
    flate2::read::GzDecoder::new(&raw[..])
        .read_to_string(&mut text)
        .unwrap();
    text.lines()
        .map(|l| serde_json::from_str(l).unwrap())
        .collect()
}

fn key(name: &str) -> InputKey {
    match name {
        "up" => InputKey::Up,
        "down" => InputKey::Down,
        "left" => InputKey::Left,
        "right" => InputKey::Right,
        "space" => InputKey::Space,
        _ => panic!("key {name}"),
    }
}

/// A fixture's key events: what that virtual player pressed on that track.
pub fn keys(track: &str, profile: &str) -> Vec<KeyEvent> {
    let dir = root().join(format!("native/oracle/fixtures/{track}/{profile}"));
    gz_lines(&dir.join("keys.jsonl.gz"))
        .iter()
        .map(|k| KeyEvent {
            s: k["s"].as_f64().unwrap(),
            kind: if k["kind"] == "press" {
                KeyEventKind::Press
            } else {
                KeyEventKind::Release
            },
            key: key(k["key"].as_str().unwrap()),
            id: None,
        })
        .collect()
}

pub fn stream_file(track: &str) -> StreamFile {
    StreamFile::parse(
        &std::fs::read_to_string(root().join(format!("public/reactive/{track}/stream.json")))
            .unwrap(),
    )
    .unwrap()
}

/// The stream as the live analyzer goes on giving it after the music stops: `secs` of silent ticks
/// past the file's last, and the `ends` entry 1.5 s into the silence that the analyzer gives.
/// (A Python stream stops at the file's end.)
pub fn with_silent_tail(mut log: StreamLog, secs: f64) -> StreamLog {
    let last = log.ticks.last().expect("ticks").clone();
    let mut t = last.t;
    while t < last.t + secs {
        t = (t * 20.0 + 1.0).round() / 20.0;
        log.ticks.push(Tick {
            t,
            known_at: t,
            evidence: 0.0,
            energy: 0.0,
            level: Some(Level::Quiet),
            ..last.clone()
        });
    }
    let ends = log.ends.get_or_insert_with(Vec::new);
    if !ends.last().is_some_and(|e| e.t >= last.t) {
        ends.push(End {
            t: last.t,
            known_at: last.t + 1.5,
        });
    }
    log
}

/// The stream as it stood at song time `c`: every entry known by then.
pub fn known_by(log: &StreamLog, c: f64) -> StreamLog {
    fn cut<T: Clone>(xs: &[T], known: impl Fn(&T) -> f64, c: f64) -> Vec<T> {
        xs.iter().filter(|x| known(x) <= c).cloned().collect()
    }
    StreamLog {
        beats: cut(&log.beats, |b| b.known_at, c),
        ticks: cut(&log.ticks, |t| t.known_at, c),
        onsets: cut(&log.onsets, |o| o.known_at, c),
        hindsight: log.hindsight.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        timbre: log.timbre.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        changes: log.changes.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        jumps: log.jumps.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        ends: log.ends.as_ref().map(|h| cut(h, |x| x.known_at, c)),
        forecasts: log.forecasts.as_ref().map(|h| cut(h, |x| x.known_at, c)),
    }
}

/// What a live run is made of, besides its stream.
#[derive(Clone, Copy, Debug)]
pub struct Spec<'a> {
    pub track: &'a str,
    pub profile: &'a str,
    pub level: &'a str,
    pub no_hero_damage: bool,
    pub leak_future_evidence: bool,
    /// Delayed listening at this M: the stream's beats replaced by E1's (see [`delayed_log`]).
    pub delayed: Option<f64>,
}

impl<'a> Spec<'a> {
    pub fn new(track: &'a str, profile: &'a str) -> Spec<'a> {
        Spec {
            track,
            profile,
            level: "levels/reactive.yaml",
            no_hero_damage: false,
            leak_future_evidence: false,
            delayed: None,
        }
    }
}

/// The stream with Delayed listening's committed beats at `m` (E1) in place of Direct listening's:
/// `analysis::delayed::replay` over the stream's own beats and hindsight entries, the hindsight
/// usable at the live `D`, in the analyzer's order (an entry before the commits known with it).
/// Every other group is the stream's.
pub fn delayed_log(log: &StreamLog, m: f64, end: f64) -> StreamLog {
    use analysis::analyzer::Entry;
    use analysis::beats::{BeatParams, Commit};
    use analysis::delayed::{DelayedParams, replay};
    use analysis::hindsight::{HindsightEntry, HindsightParams};
    let h = HindsightParams::live();
    let mut entries: Vec<(f64, u8, Entry)> = log
        .beats
        .iter()
        .map(|b| {
            let c = Commit {
                t: b.t,
                known_at: b.known_at,
                period: b.period,
                evidence: b.evidence,
            };
            (b.known_at, 1, Entry::Beat(c))
        })
        .collect();
    for x in log.hindsight.iter().flatten() {
        let e = HindsightEntry {
            known_at: x.known_at + h.delay_s,
            window_end: x.known_at,
            offline: x.offline.clone(),
            online: x.online.clone(),
        };
        entries.push((e.known_at, 0, Entry::Hindsight(e)));
    }
    entries.sort_by(|a, b| a.0.total_cmp(&b.0).then(a.1.cmp(&b.1)));
    let entries: Vec<Entry> = entries.into_iter().map(|e| e.2).collect();
    let p = DelayedParams {
        m,
        ..DelayedParams::chosen()
    };
    let beats = replay(&entries, end, p, &h, &BeatParams::default());
    StreamLog {
        beats: beats
            .iter()
            .map(|c| stream::Beat {
                t: c.t,
                known_at: c.known_at,
                period: c.period,
                evidence: c.evidence,
            })
            .collect(),
        ..log.clone()
    }
}

/// The stream a spec runs on: the track's (Delayed if the spec says so) with a silent tail.
pub fn log_for(spec: &Spec) -> StreamLog {
    let file = stream_file(spec.track);
    let log = match spec.delayed {
        Some(m) => delayed_log(&file.log, m, file.audio.duration),
        None => file.log,
    };
    with_silent_tail(log, 10.0)
}

/// A live sim on `log` with the spec's gate, level and checks and the fixture's keys queued, not yet
/// advanced. (The level is loaded against a song map with no beats: live, its range is provisional.)
pub fn start(spec: &Spec, log: &StreamLog) -> (Rc<RefCell<LogRecorder>>, Sim) {
    let rec = Rc::new(RefCell::new(LogRecorder::default()));
    let mut sim = Sim::new(
        input_for(spec, log),
        Rc::new(log.clone()),
        Box::new(Rc::clone(&rec)),
        None,
    );
    // (An empty profile queues no keys: the test feeds its own.)
    let queued = if spec.profile.is_empty() {
        Vec::new()
    } else {
        keys(spec.track, spec.profile)
    };
    for k in queued {
        sim.key(k);
    }
    (rec, sim)
}

/// The sim's input for a spec on `log`.
pub fn input_for(spec: &Spec, log: &StreamLog) -> SimInput {
    let lib = library();
    let file = stream_file(spec.track);
    let mut song = reactive_song_map(f64::INFINITY);
    grow_song_map(&mut song, log, 0);
    // (A level may extend another in levels/.)
    let resolve = |name: &str| {
        let rel = format!("levels/{name}.yaml");
        root().join(&rel).exists().then(|| source(&rel))
    };
    let level = load_level(&source(spec.level), lib, &song, Some(&resolve)).unwrap();
    let mut input = SimInput::new(
        level,
        lib,
        &atlas(),
        file.params.gate,
        file.log.hindsight.is_some(),
    );
    input.no_hero_damage = spec.no_hero_damage;
    input.leak_future_evidence = spec.leak_future_evidence;
    input
}

/// A live run of `track` with `profile`'s keys on `log`, advanced to `until` (or its end, if sooner).
pub fn live_run(
    track: &str,
    profile: &str,
    log: &StreamLog,
    until: f64,
) -> (Rc<RefCell<LogRecorder>>, Sim) {
    let (rec, mut sim) = start(&Spec::new(track, profile), log);
    sim.advance_to(until);
    (rec, sim)
}

/// The recording so far, as one comparable string.
pub fn recording(rec: &Rc<RefCell<LogRecorder>>) -> String {
    format!("{:?}", rec.borrow())
}

/// Where two recordings first differ, with some context, or None.
pub fn first_difference(a: &str, b: &str) -> Option<String> {
    if a == b {
        return None;
    }
    let i = a
        .bytes()
        .zip(b.bytes())
        .position(|(x, y)| x != y)
        .unwrap_or(a.len().min(b.len()));
    let lo = a[..i].char_indices().rev().nth(300).map_or(0, |(k, _)| k);
    let hi = |s: &str| {
        s[i..]
            .char_indices()
            .nth(200)
            .map_or(s.len(), |(k, _)| i + k)
    };
    Some(format!(
        "at byte {i}\n  one   …{}\n  other …{}",
        &a[lo..hi(a)],
        &b[lo..hi(b)]
    ))
}
