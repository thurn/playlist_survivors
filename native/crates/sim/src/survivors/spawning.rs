//! Mad Forest timing and grid admission. All deadlines belong to the held run clock.
use serde::Deserialize;
use stream::Beat;
fn count<'de, D: serde::Deserializer<'de>>(d: D) -> Result<usize, D::Error> {
    let n = f64::deserialize(d)?;
    if !n.is_finite() || n < 0.0 || n.fract() != 0.0 || n >= usize::MAX as f64 {
        return Err(serde::de::Error::custom(
            "expected a nonnegative integer count",
        ));
    }
    Ok(n as usize)
}
fn optional_count<'de, D: serde::Deserializer<'de>>(d: D) -> Result<Option<usize>, D::Error> {
    count(d).map(Some)
}

pub const SURVIVOR_CAP: usize = 300;

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct SpawnEvent {
    pub kind: String,
    pub delay_ms: f64,
    #[serde(deserialize_with = "count")]
    pub extra_repeats: usize,
    /// Absent and source zero both mean no chance gate (SW6 executes these).
    pub chance: Option<f64>,
    pub duration_ms: f64,
    #[serde(deserialize_with = "count")]
    pub count: usize,
}
#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Treasure {
    pub chances: Vec<f64>,
    #[serde(default, deserialize_with = "optional_count")]
    pub tier: Option<usize>,
    #[serde(deserialize_with = "count")]
    pub level: usize,
    pub prize_types: String,
    pub fixed_prizes: Vec<String>,
}
#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct SpawnMinute {
    #[serde(deserialize_with = "count")]
    pub minute: usize,
    pub enemies: Option<Vec<String>>,
    #[serde(default, deserialize_with = "optional_count")]
    pub minimum: Option<usize>,
    /// Source milliseconds.
    pub frequency: Option<f64>,
    pub bosses: Vec<String>,
    pub events: Vec<SpawnEvent>,
    pub treasure: Vec<Treasure>,
}
#[derive(Clone, Debug, Default, PartialEq)]
pub struct SpawnRoster {
    pub enemies: Vec<String>,
    pub minimum: usize,
    pub frequency_s: f64,
}
impl SpawnRoster {
    fn apply(&mut self, minute: &SpawnMinute) {
        if let Some(enemies) = &minute.enemies {
            self.enemies.clone_from(enemies);
        }
        if let Some(minimum) = minute.minimum {
            self.minimum = minimum;
        }
        if let Some(frequency) = minute.frequency {
            self.frequency_s = frequency / 1000.0;
        }
    }
}
#[derive(Clone, Debug, PartialEq)]
pub struct ScheduledBoss {
    pub role: String,
    pub minute: usize,
}
#[derive(Clone, Debug, PartialEq)]
struct Pending {
    ready: f64,
    song: f64,
    roster: SpawnRoster,
    bosses: Vec<ScheduledBoss>,
}
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Spawner {
    pub minute: Option<usize>,
    pub roster: SpawnRoster,
    next_tick: f64,
    pending: Vec<Pending>,
    /// A full field holds scheduled bosses until a slot becomes free. Regular ticks have no debt.
    pub waiting_bosses: Vec<ScheduledBoss>,
    pub spawned: usize,
    boss_song: Option<f64>,
}
#[derive(Clone, Debug)]
pub struct SpawnBatch {
    pub roster: SpawnRoster,
    pub bosses: Vec<ScheduledBoss>,
}
impl Spawner {
    pub fn next_stop(&self, schedule: &[SpawnMinute], song: f64, offset: f64) -> f64 {
        if schedule.is_empty() {
            return f64::INFINITY;
        }
        let phase = self.minute.map_or(0, |m| m + 1);
        let boundary = schedule
            .get(phase)
            .map_or(f64::INFINITY, |m| m.minute as f64 * 60.0);
        let deadline = boundary.min(if self.minute.is_some() {
            self.next_tick
        } else {
            f64::INFINITY
        });
        (deadline + offset)
            .max(song)
            .min(
                self.pending
                    .iter()
                    .map(|p| p.song)
                    .fold(f64::INFINITY, f64::min),
            )
            .min(self.boss_song.unwrap_or(f64::INFINITY))
    }
    /// Resolve each tick using only the grid known when it becomes ready. Later observations
    /// cannot pull a queued spawn into the past. No future grid means the raw deadline.
    pub fn prepare(&mut self, schedule: &[SpawnMinute], run: f64, song: f64, beats: &[Beat]) {
        let next = self.minute.map_or(0, |m| m + 1);
        let mut bosses = Vec::new();
        if let Some(phase) = schedule
            .get(next)
            .filter(|p| p.minute as f64 * 60.0 <= run + 1e-9)
        {
            self.minute = Some(next);
            self.roster.apply(phase);
            bosses.extend(phase.bosses.iter().map(|role| ScheduledBoss {
                role: role.clone(),
                minute: phase.minute,
            }));
        }
        if self.minute.is_some() && self.next_tick <= run + 1e-9 {
            self.pending.push(Pending {
                ready: self.next_tick,
                song: snap(song, beats),
                roster: self.roster.clone(),
                bosses,
            });
            self.next_tick += self.roster.frequency_s;
        } else if !bosses.is_empty() {
            self.pending.push(Pending {
                ready: run,
                song: snap(song, beats),
                roster: SpawnRoster::default(),
                bosses,
            });
        }
    }
    /// A boss blocked by the population cap gets a fresh grid admission when room opens.
    pub fn bosses_due(&mut self, song: f64, beats: &[Beat], room: bool) -> bool {
        if self.waiting_bosses.is_empty() || !room {
            self.boss_song = None;
            return false;
        }
        let due = *self
            .boss_song
            .get_or_insert_with(|| super::boss_bar(song, song, beats).0);
        if due <= song + 1e-9 {
            self.boss_song = None;
            true
        } else {
            false
        }
    }
    pub fn take_due(&mut self, song: f64) -> Vec<SpawnBatch> {
        let mut due = Vec::new();
        self.pending.retain(|p| {
            if p.song > song + 1e-9 {
                return true;
            }
            due.push(SpawnBatch {
                roster: p.roster.clone(),
                bosses: p.bosses.clone(),
            });
            false
        });
        due
    }
    /// In-flight grid admissions are regenerated after a freeze. Ready deadlines stay in run time.
    pub fn hold(&mut self) {
        self.boss_song = None;
        for p in &mut self.pending {
            p.song = f64::INFINITY;
        }
    }
    pub fn resume(&mut self, run: f64, song: f64, beats: &[Beat]) {
        for p in &mut self.pending {
            if p.song.is_infinite() {
                p.song = snap(song + (p.ready - run).max(0.0), beats);
            }
        }
    }
}
fn snap(song: f64, beats: &[Beat]) -> f64 {
    let committed = beats
        .iter()
        .filter(|b| b.known_at <= song + 1e-9)
        .map(|b| b.t);
    let eighths = beats
        .windows(2)
        .filter(|b| b[0].known_at <= song + 1e-9 && b[1].known_at <= song + 1e-9)
        .map(|b| b[0].t.midpoint(b[1].t));
    committed
        .chain(eighths)
        .filter(|t| *t >= song - 1e-9)
        .min_by(f64::total_cmp)
        .unwrap_or(song)
        .max(song)
}
