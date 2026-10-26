//! Belief-stream types: the append-only stream log and serde for `stream.json` (pure).
//!
//! `src/reactive/stream.ts` at `ts-freeze`, for the live app (docs/design/native-app.md, "The
//! stream, live"): one store, [`StreamLog`], filled by every source. Replay feeds a `stream.json`'s
//! entries into it in `known_at` order as the run advances ([`Replay`]); live sources append
//! entries as they arrive. Ticks gain a `known_at` (their own `t` in Replay). The end of the stream
//! ([`StreamEnd`]) is known only in Replay. The file's reference (offline beats, sections) has no
//! type here: [`StreamFile::split`] hands it back as raw JSON, which only the virtual players and
//! checks interpret, so the sim cannot name it.
#![forbid(unsafe_code)]

pub mod playlist;

use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Beat {
    pub t: f64,
    pub known_at: f64,
    pub period: f64,
    pub evidence: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum Level {
    Quiet,
    Medium,
    Loud,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq)]
pub struct Bands {
    pub kick: f64,
    pub snare: f64,
    pub hat: f64,
    pub bass: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum Band {
    Kick,
    Snare,
    Hat,
    Bass,
}

/// One tick (every `tick_s`). In TS a tick has no `known_at`: readers find it by `t`. Here it does:
/// its own `t` in Replay, its arrival live.
#[derive(Clone, Debug, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Tick {
    pub t: f64,
    pub known_at: f64,
    pub evidence: f64,
    pub energy: f64,
    pub level: Option<Level>,
    pub key: Option<String>,
    pub bands: Option<Bands>,
    pub brightness: Option<f64>,
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq)]
struct FileTick {
    t: f64,
    evidence: f64,
    energy: f64,
    level: Option<Level>,
    key: Option<String>,
    bands: Option<Bands>,
    brightness: Option<f64>,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Onset {
    pub t: f64,
    pub known_at: f64,
    pub strength: f64,
    pub strong: bool,
    pub band: Option<Band>,
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Hindsight {
    pub known_at: f64,
    pub offline: Vec<f64>,
    pub online: Vec<f64>,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Timbre {
    pub known_at: f64,
    pub brightness: f64,
    pub flatness: f64,
    pub percussive: f64,
    pub density: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Change {
    pub t: f64,
    pub known_at: f64,
    pub strength: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Jump {
    pub t: f64,
    pub known_at: f64,
    pub db: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct End {
    pub t: f64,
    pub known_at: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Forecast {
    pub t: f64,
    pub known_at: f64,
    pub cycle: f64,
    pub confidence: f64,
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Audio {
    pub file: String,
    pub duration: f64,
    pub sample_rate: f64,
}

/// The analysis parameters the game reads; the rest stay in `raw`.
#[derive(Clone, Debug, PartialEq)]
pub struct Params {
    pub gate: f64,
    pub tick_s: f64,
    pub tool_version: f64,
    pub raw: Value,
}

/// Every group, append-only, each in `known_at` order; nothing is revised.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct StreamLog {
    pub beats: Vec<Beat>,
    pub ticks: Vec<Tick>,
    pub onsets: Vec<Onset>,
    /// `None` when the stream has no group (an older stream), as TS's optional fields.
    pub hindsight: Option<Vec<Hindsight>>,
    pub timbre: Option<Vec<Timbre>>,
    pub changes: Option<Vec<Change>>,
    pub jumps: Option<Vec<Jump>>,
    pub ends: Option<Vec<End>>,
    pub forecasts: Option<Vec<Forecast>>,
}

/// What only Replay knows: the song's length and each group's final count. Oracle Replay gives it at
/// time 0, so reads of the stream's end match TS; live it is absent (docs/design/native-app.md,
/// "Live exceptions").
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct StreamEnd {
    pub duration: f64,
    pub beats: usize,
    pub ticks: usize,
}

/// A whole `stream.json`: the stream as Python wrote it, its end, its parameters and audio.
#[derive(Clone, Debug, PartialEq)]
pub struct StreamFile {
    pub audio: Audio,
    pub params: Params,
    pub log: StreamLog,
}

#[derive(Debug)]
pub struct StreamError(pub String);

impl std::fmt::Display for StreamError {
    fn fmt(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        write!(f, "stream: {}", self.0)
    }
}

impl std::error::Error for StreamError {}

fn field<T: serde::de::DeserializeOwned>(v: &Value, key: &str) -> Result<T, StreamError> {
    serde_json::from_value(v.get(key).cloned().unwrap_or(Value::Null))
        .map_err(|e| StreamError(format!("{key}: {e}")))
}

fn optional<T: serde::de::DeserializeOwned>(
    v: Option<&Value>,
    key: &str,
) -> Result<Option<T>, StreamError> {
    match v {
        None | Some(Value::Null) => Ok(None),
        Some(x) => serde_json::from_value(x.clone())
            .map(Some)
            .map_err(|e| StreamError(format!("{key}: {e}"))),
    }
}

impl StreamFile {
    /// Reads a `stream.json` (version 1), without its reference. Ticks get `known_at = t`.
    pub fn parse(text: &str) -> Result<StreamFile, StreamError> {
        Self::split(text).map(|(file, _)| file)
    }

    /// Reads a `stream.json` and hands back its reference (offline beats, sections, accents) apart,
    /// as raw JSON, as TS's `splitStream` does.
    pub fn split(text: &str) -> Result<(StreamFile, Option<Value>), StreamError> {
        let v: Value = serde_json::from_str(text).map_err(|e| StreamError(e.to_string()))?;
        if v.get("version").and_then(Value::as_f64) != Some(1.0) {
            return Err(StreamError("version must be 1".into()));
        }
        let params = v.get("params").cloned().unwrap_or(Value::Null);
        let num = |k: &str| {
            params
                .get(k)
                .and_then(Value::as_f64)
                .ok_or_else(|| StreamError(format!("params.{k} is missing")))
        };
        let ticks: Vec<FileTick> = field(&v, "ticks")?;
        let structure = v.get("structure");
        let log = StreamLog {
            beats: field(&v, "beats")?,
            ticks: ticks
                .into_iter()
                .map(|t| Tick {
                    t: t.t,
                    known_at: t.t,
                    evidence: t.evidence,
                    energy: t.energy,
                    level: t.level,
                    key: t.key,
                    bands: t.bands,
                    brightness: t.brightness,
                })
                .collect(),
            onsets: field(&v, "onsets")?,
            hindsight: optional(v.get("hindsight"), "hindsight")?,
            timbre: optional(v.get("timbre"), "timbre")?,
            changes: optional(
                structure.and_then(|s| s.get("changes")),
                "structure.changes",
            )?,
            jumps: optional(structure.and_then(|s| s.get("jumps")), "structure.jumps")?,
            ends: optional(structure.and_then(|s| s.get("ends")), "structure.ends")?,
            forecasts: optional(
                v.get("accents").and_then(|a| a.get("forecast")),
                "accents.forecast",
            )?,
        };
        if log.beats.len() < 2 {
            return Err(StreamError("a stream needs at least 2 beats".into()));
        }
        let file = StreamFile {
            audio: field(&v, "audio")?,
            params: Params {
                gate: num("gate")?,
                tick_s: num("tickS")?,
                tool_version: num("toolVersion")?,
                raw: params.clone(),
            },
            log,
        };
        Ok((file, v.get("reference").cloned()))
    }

    pub fn end(&self) -> StreamEnd {
        StreamEnd {
            duration: self.audio.duration,
            beats: self.log.beats.len(),
            ticks: self.log.ticks.len(),
        }
    }
}

/// Feeds a whole stream into a log in `known_at` order as the run advances: [`Replay::feed_to`]
/// appends every entry known by a sim time, and never anything later.
#[derive(Clone, Debug)]
pub struct Replay {
    source: StreamLog,
    /// How many entries of each group are in the log (the snapshot state a rollback restores).
    pub cursor: Cursor,
}

/// Per group, how many entries have been fed.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Cursor {
    pub beats: usize,
    pub ticks: usize,
    pub onsets: usize,
    pub hindsight: usize,
    pub timbre: usize,
    pub changes: usize,
    pub jumps: usize,
    pub ends: usize,
    pub forecasts: usize,
}

/// Appends `from[*at..]`'s entries known by `s` to `to`.
fn feed<T: Clone>(from: &[T], to: &mut Vec<T>, at: &mut usize, s: f64, known: impl Fn(&T) -> f64) {
    while *at < from.len() && known(&from[*at]) <= s {
        to.push(from[*at].clone());
        *at += 1;
    }
}

fn feed_opt<T: Clone>(
    from: Option<&Vec<T>>,
    to: &mut Option<Vec<T>>,
    at: &mut usize,
    s: f64,
    known: impl Fn(&T) -> f64,
) {
    if let Some(from) = from {
        feed(from, to.get_or_insert_with(Vec::new), at, s, known);
    }
}

impl Replay {
    pub fn new(source: StreamLog) -> Replay {
        Replay {
            source,
            cursor: Cursor::default(),
        }
    }

    /// An empty log with the source's groups present (a stream without hindsight stays without).
    pub fn empty_log(&self) -> StreamLog {
        StreamLog {
            hindsight: self.source.hindsight.as_ref().map(|_| Vec::new()),
            timbre: self.source.timbre.as_ref().map(|_| Vec::new()),
            changes: self.source.changes.as_ref().map(|_| Vec::new()),
            jumps: self.source.jumps.as_ref().map(|_| Vec::new()),
            ends: self.source.ends.as_ref().map(|_| Vec::new()),
            forecasts: self.source.forecasts.as_ref().map(|_| Vec::new()),
            ..StreamLog::default()
        }
    }

    /// Appends to `log` every entry with `known_at <= s` not fed yet. Python's streams are in
    /// `known_at` order within each group. Ticks come within 1e-9, as TS reads them by `t` (and
    /// [`StreamLog::tick_index_at`] by `known_at`): a beat's energy at its `known_at` then sees the
    /// tick TS sees.
    pub fn feed_to(&mut self, log: &mut StreamLog, s: f64) {
        let c = &mut self.cursor;
        let src = &self.source;
        feed(&src.beats, &mut log.beats, &mut c.beats, s, |b| b.known_at);
        feed(&src.ticks, &mut log.ticks, &mut c.ticks, s + 1e-9, |t| {
            t.known_at
        });
        feed(&src.onsets, &mut log.onsets, &mut c.onsets, s, |o| {
            o.known_at
        });
        feed_opt(
            src.hindsight.as_ref(),
            &mut log.hindsight,
            &mut c.hindsight,
            s,
            |h| h.known_at,
        );
        feed_opt(
            src.timbre.as_ref(),
            &mut log.timbre,
            &mut c.timbre,
            s,
            |t| t.known_at,
        );
        feed_opt(
            src.changes.as_ref(),
            &mut log.changes,
            &mut c.changes,
            s,
            |x| x.known_at,
        );
        feed_opt(src.jumps.as_ref(), &mut log.jumps, &mut c.jumps, s, |x| {
            x.known_at
        });
        feed_opt(src.ends.as_ref(), &mut log.ends, &mut c.ends, s, |x| {
            x.known_at
        });
        feed_opt(
            src.forecasts.as_ref(),
            &mut log.forecasts,
            &mut c.forecasts,
            s,
            |x| x.known_at,
        );
    }

    /// The source's earliest unfed `known_at` (the next moment the log grows), or infinity.
    pub fn next_known_at(&self) -> f64 {
        let c = &self.cursor;
        let s = &self.source;
        let mut next = f64::INFINITY;
        let mut consider = |k: Option<f64>| {
            if let Some(k) = k {
                next = next.min(k);
            }
        };
        consider(s.beats.get(c.beats).map(|b| b.known_at));
        consider(s.ticks.get(c.ticks).map(|t| t.known_at));
        consider(s.onsets.get(c.onsets).map(|o| o.known_at));
        consider(
            s.hindsight
                .as_ref()
                .and_then(|h| h.get(c.hindsight))
                .map(|h| h.known_at),
        );
        consider(
            s.timbre
                .as_ref()
                .and_then(|h| h.get(c.timbre))
                .map(|h| h.known_at),
        );
        consider(
            s.changes
                .as_ref()
                .and_then(|h| h.get(c.changes))
                .map(|h| h.known_at),
        );
        consider(
            s.jumps
                .as_ref()
                .and_then(|h| h.get(c.jumps))
                .map(|h| h.known_at),
        );
        consider(
            s.ends
                .as_ref()
                .and_then(|h| h.get(c.ends))
                .map(|h| h.known_at),
        );
        consider(
            s.forecasts
                .as_ref()
                .and_then(|h| h.get(c.forecasts))
                .map(|h| h.known_at),
        );
        next
    }
}

impl StreamLog {
    /// Index of the latest tick at or before song time s and known by then (-1 before the first):
    /// TS's `tickIndexAt` (ticks by `t` within 1e-9), restricted to ticks with `known_at <= s`,
    /// which in Replay is the same set.
    #[allow(clippy::manual_midpoint)] // (the TS binary search, as written)
    pub fn tick_index_at(&self, s: f64) -> isize {
        let ticks = &self.ticks;
        let mut lo = 0usize;
        let mut hi = ticks.len();
        while lo < hi {
            let mid = (lo + hi) >> 1;
            if ticks[mid].t <= s + 1e-9 {
                lo = mid + 1;
            } else {
                hi = mid;
            }
        }
        let mut i = lo as isize - 1;
        while i >= 0 && ticks[i as usize].known_at > s + 1e-9 {
            i -= 1;
        }
        i
    }

    /// The energy at song time s: the latest known tick's at or before it (0 before the first).
    pub fn energy_at(&self, s: f64) -> f64 {
        let i = self.tick_index_at(s);
        if i < 0 {
            0.0
        } else {
            self.ticks[i as usize].energy
        }
    }

    /// The evidence at song time s (0 before the first tick).
    pub fn evidence_at(&self, s: f64) -> f64 {
        let i = self.tick_index_at(s);
        if i < 0 {
            0.0
        } else {
            self.ticks[i as usize].evidence
        }
    }
}
