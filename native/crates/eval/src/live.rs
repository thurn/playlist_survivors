//! The live view (docs/design/native-app.md, "The engine in the app (G1)"): the sim engine on a
//! growing stream, and a minimal view of its state for drawing, kept by a [`Recorder`] as the
//! simulation reports it: the hero's pose, the camera, the enemies' latest positions, health, the
//! groove, and the moments a frame shows once (hits, deaths, presses, bites).

use std::cell::RefCell;
use std::collections::{BTreeMap, VecDeque};
use std::rc::Rc;

use indexmap::IndexMap;
use sim::content::level::{LevelError, load_level};
use sim::content::{ContentLibrary, SourceFile};
use sim::core::geometry::Shape;
use sim::engine::{AnimInfo, HERO_ANIMS, Sim, SimInput, Snapshot};
use sim::movement::InputKey;
use sim::player::{KeyEvent, KeyEventKind};
use sim::recorder::{
    BiteRecord, EnemyRecord, Fate, Hit, HitSource, Landing, PlanInput, PlanSwing, Recorder,
    SettleRecord, ZoneWaveRecord,
};
use sim::schedule::reactive_song_map;
use stream::StreamLog;

/// The hero as the simulation last placed it.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct HeroPose {
    pub x: f64,
    pub y: f64,
    pub z: f64,
    /// 0 up, 1 left, 2 down, 3 right (the atlases' order).
    pub facing: usize,
    pub anim: &'static str,
    pub frame: usize,
}

/// An enemy as last sampled.
#[derive(Clone, Debug, PartialEq)]
pub struct Foe {
    /// Its content id (`goblin`, `bat`, ...).
    pub kind: String,
    pub x: f64,
    pub y: f64,
    /// The sample before, for its heading.
    pub prev: (f64, f64),
    /// Samples taken (a walk cycle reads it).
    pub samples: usize,
    /// When it died (world time), if it has.
    pub death_w: Option<f64>,
}

/// A moment the frame shows once.
#[derive(Clone, Debug, PartialEq)]
pub enum Moment {
    /// A weapon or swing hit something at (x, y).
    Hit { x: f64, y: f64, lethal: bool },
    /// An enemy died where it was last seen (not recycled ones).
    Death { kind: String, x: f64, y: f64 },
    /// A swing started.
    Swing {
        x: f64,
        y: f64,
        dir: f64,
        kind: &'static str,
    },
    /// A press, with its provisional rating when judged (`perfect`, `great`, `good`, `miss`, ...),
    /// or unjudged (the beat unclear), and whether it was a stomp (Space).
    Press {
        rating: Option<&'static str>,
        /// How far off the grid (s): early negative.
        delta: f64,
        unjudged: bool,
        stomp: bool,
    },
    /// A press's settled judgment, once hindsight has spoken (2 s on).
    Settled {
        provisional: String,
        settled: String,
    },
    /// The hero was bitten.
    Bite { damage: f64 },
}

/// What a sound marks, at the song time it belongs to (`planner/emit.ts`'s sound calls; the app
/// places it on the beat grid, picks its sample and schedules it).
#[derive(Clone, Debug, PartialEq)]
pub enum Cause {
    /// A swing's strike at `s` (its whoosh comes just before), at the hero's x.
    Swing { s: f64, x: f64, kind: &'static str },
    /// An enemy killed by a hit at `s`.
    Kill { s: f64, x: f64 },
    /// A weapon's hit at `s`: as it fires (`caught` false), and on the first enemy it catches.
    Weapon {
        s: f64,
        x: f64,
        what: String,
        caught: bool,
    },
    /// The hero bitten at `s`.
    Hurt { s: f64 },
    /// The groove's tier rose or fell; its chime at `s`.
    Tier { s: f64, up: bool },
    /// A stride landed (not a stomp).
    Step { s: f64, x: f64 },
    /// A level gained at `s`.
    LevelUp { s: f64 },
}

/// A sound's cause and the song time the simulation knew it at.
#[derive(Clone, Debug, PartialEq)]
pub struct Caused {
    pub cause: Cause,
    pub known: f64,
}

/// What a frame draws.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct LiveView {
    pub hero: Option<HeroPose>,
    /// The camera's centre and zoom.
    pub camera: (f64, f64, f64),
    /// Enemies by id: alive, or dead within the last `DEAD_KEEP` entries.
    pub foes: BTreeMap<usize, Foe>,
    /// The hero's health, 0-1.
    pub health: f64,
    pub groove: f64,
    pub tier: f64,
    /// The experience bar's fill, 0-1, and the level (1 and up).
    pub xp: f64,
    pub level: u32,
    /// Enemies killed (not those that left the field).
    pub kills: usize,
    /// Presses rated perfect, great and good.
    pub ratings: [usize; 3],
    /// Moments since the drawer last took them.
    pub moments: Vec<Moment>,
    /// The song time shown (the latest record's).
    pub s: f64,
    /// The open field's zone (none: the level's first), and the transform wave sweeping in the
    /// newest, if any (it has replaced `zone` once it has passed).
    pub zone: Option<String>,
    pub wave: Option<ZoneWave>,
}

/// A transform wave: from the song time `s` its ring grows from (x, y), the new zone inside it.
#[derive(Clone, Debug, PartialEq)]
pub struct ZoneWave {
    pub s: f64,
    pub x: f64,
    pub y: f64,
    pub prev: Option<String>,
    pub zone: String,
}

impl LiveView {
    /// The moments since the last call.
    pub fn take_moments(&mut self) -> Vec<Moment> {
        std::mem::take(&mut self.moments)
    }

    /// The living enemies.
    pub fn alive(&self) -> impl Iterator<Item = (&usize, &Foe)> {
        self.foes.iter().filter(|(_, f)| f.death_w.is_none())
    }
}

impl LiveView {
    /// Applies a record reported at song time `s`.
    pub fn apply(&mut self, s: f64, rec: &Record) {
        self.s = s;
        match rec {
            Record::Hero(p) => self.hero = Some(*p),
            Record::Camera(x, y, zoom) => self.camera = (*x, *y, *zoom),
            Record::Spawn { id, kind } => {
                self.foes.insert(
                    *id,
                    Foe {
                        kind: kind.clone(),
                        x: f64::NAN,
                        y: f64::NAN,
                        prev: (f64::NAN, f64::NAN),
                        samples: 0,
                        death_w: None,
                    },
                );
            }
            Record::Sample { id, x, y } => {
                if let Some(f) = self.foes.get_mut(id) {
                    f.prev = if f.samples == 0 { (*x, *y) } else { (f.x, f.y) };
                    f.x = *x;
                    f.y = *y;
                    f.samples += 1;
                }
            }
            Record::Death { id, w, recycled } => self.death(*id, *w, *recycled),
            Record::Moment(m) => {
                if let Moment::Press { rating, .. } = m {
                    match *rating {
                        Some("perfect") => self.ratings[0] += 1,
                        Some("great") => self.ratings[1] += 1,
                        Some("good") => self.ratings[2] += 1,
                        _ => {}
                    }
                }
                self.moments.push(m.clone());
            }
            Record::Groove(v) => self.groove = *v,
            Record::Tier(v) => self.tier = *v,
            Record::Xp(v) => self.xp = *v,
            Record::LevelUp => self.level += 1,
            Record::Health(v) => self.health = *v,
            // (Sounds go to the scheduler from the speculation: `Engine::causes`.)
            Record::Sound(_) => {}
            Record::ZoneWave { s, x, y, zone } => {
                self.wave = Some(ZoneWave {
                    s: *s,
                    x: *x,
                    y: *y,
                    prev: self.zone.replace(zone.clone()),
                    zone: zone.clone(),
                });
            }
        }
    }

    fn death(&mut self, id: usize, w: f64, recycled: bool) {
        let Some(f) = self.foes.get_mut(&id) else {
            return;
        };
        if recycled {
            // (It leaves the field and comes back elsewhere as a new spawn.)
            self.foes.remove(&id);
            return;
        }
        f.death_w = Some(w);
        let moment = Moment::Death {
            kind: f.kind.clone(),
            x: f.x,
            y: f.y,
        };
        self.moments.push(moment);
        self.kills += 1;
        // Keep the dead only briefly (the drawer fades them).
        self.foes
            .retain(|_, f| !f.death_w.is_some_and(|d| d < w - 2.0));
    }
}

/// What the simulation reported, as the view applies it.
#[derive(Clone, Debug, PartialEq)]
pub enum Record {
    Hero(HeroPose),
    Camera(f64, f64, f64),
    Spawn {
        id: usize,
        kind: String,
    },
    Sample {
        id: usize,
        x: f64,
        y: f64,
    },
    Death {
        id: usize,
        w: f64,
        recycled: bool,
    },
    Moment(Moment),
    Groove(f64),
    Tier(f64),
    Xp(f64),
    LevelUp,
    Health(f64),
    Sound(Caused),
    ZoneWave {
        s: f64,
        x: f64,
        y: f64,
        zone: String,
    },
}

/// What later reports refer back to: the clock, recent hits (id, song time, source, weapon,
/// where), the tier. A mark carries a copy, so a rollback restores it with the records.
#[derive(Clone, Debug, Default, PartialEq)]
struct Notes {
    s: f64,
    hits: VecDeque<(f64, f64, HitSource, String, f64)>,
    tier: f64,
    hero_x: f64,
}

/// The simulation's records in the order reported, each with the song time of the clock then.
/// Records the view has applied and no rollback can reach are forgotten.
#[derive(Debug, Default)]
pub struct Journal {
    records: VecDeque<(f64, Record)>,
    /// The absolute index of the first record kept.
    base: usize,
    notes: Notes,
}

/// A point in the journal to rewind to.
#[derive(Clone, Debug, PartialEq)]
pub struct Mark {
    len: usize,
    notes: Notes,
}

impl Journal {
    /// Where the journal is now.
    pub fn mark(&self) -> Mark {
        Mark {
            len: self.end(),
            notes: self.notes.clone(),
        }
    }

    /// Drops the records after `m` and restores the notes then.
    pub fn rewind(&mut self, m: &Mark) {
        assert!(m.len >= self.base, "rewound past a forgotten record");
        self.records.truncate(m.len - self.base);
        self.notes = m.notes.clone();
    }

    /// The absolute index after the last record.
    pub fn end(&self) -> usize {
        self.base + self.records.len()
    }

    /// The record at absolute index `k`, if kept.
    pub fn get(&self, k: usize) -> Option<&(f64, Record)> {
        self.records.get(k.checked_sub(self.base)?)
    }

    /// Forgets the records before absolute index `k`.
    pub fn forget(&mut self, k: usize) {
        let n = k.saturating_sub(self.base).min(self.records.len());
        self.records.drain(..n);
        self.base += n;
    }

    fn push(&mut self, r: Record) {
        self.records.push_back((self.notes.s, r));
    }

    fn sound(&mut self, cause: Cause) {
        let known = self.notes.s;
        self.push(Record::Sound(Caused { cause, known }));
    }
}

/// The recorder that writes the [`Journal`].
pub struct LiveRecorder(pub Rc<RefCell<Journal>>);

impl Recorder for LiveRecorder {
    fn hero(&mut self, x: f64, y: f64, z: f64, facing: f64, anim: f64, frame: f64) {
        let anim = HERO_ANIMS.get(anim as usize).copied().unwrap_or("idle");
        let mut j = self.0.borrow_mut();
        j.notes.hero_x = x;
        j.push(Record::Hero(HeroPose {
            x,
            y,
            z,
            facing: (facing as usize).min(3),
            anim,
            frame: frame.max(0.0) as usize,
        }));
    }

    fn camera(&mut self, x: f64, y: f64, zoom: f64) {
        self.0.borrow_mut().push(Record::Camera(x, y, zoom));
    }

    fn spawn(&mut self, rec: EnemyRecord) {
        self.0.borrow_mut().push(Record::Spawn {
            id: rec.id as usize,
            kind: rec.kind,
        });
    }

    fn sample(&mut self, id: usize, x: f64, y: f64) {
        self.0.borrow_mut().push(Record::Sample { id, x, y });
    }

    fn fate(&mut self, _id: usize, fate: Fate) {
        let mut j = self.0.borrow_mut();
        j.push(Record::Moment(Moment::Hit {
            x: fate.x,
            y: fate.y,
            lethal: fate.lethal,
        }));
        let Some(k) = j.notes.hits.iter().position(|h| h.0 == fate.hit) else {
            return;
        };
        let (_, s, source, what, _) = j.notes.hits[k].clone();
        if fate.lethal {
            j.sound(Cause::Kill { s, x: fate.x });
        }
        // (A weapon sounds on the first enemy it catches; the hit is forgotten after.)
        if source == HitSource::Weapon {
            j.notes.hits.remove(k);
            j.sound(Cause::Weapon {
                s,
                x: fate.x,
                what,
                caught: true,
            });
        }
    }

    fn death(&mut self, id: usize, w: f64, recycled: bool) {
        self.0.borrow_mut().push(Record::Death { id, w, recycled });
    }

    fn hit(&mut self, h: Hit) {
        let mut j = self.0.borrow_mut();
        let x = match h.shape {
            Shape::Arc { x, .. } | Shape::Circle { x, .. } | Shape::Ring { x, .. } => x,
            Shape::Capsule { x1, .. } => x1,
            #[allow(unreachable_patterns)]
            _ => j.notes.hero_x,
        };
        if h.source == HitSource::Weapon {
            j.sound(Cause::Weapon {
                s: h.s,
                x,
                what: h.what.clone(),
                caught: false,
            });
        }
        j.notes.hits.push_back((h.id, h.s, h.source, h.what, x));
        if j.notes.hits.len() > 64 {
            j.notes.hits.pop_front();
        }
    }

    fn landing(&mut self, l: Landing) {
        if l.stomp != Some(true) {
            self.0.borrow_mut().sound(Cause::Step { s: l.s, x: l.x });
        }
    }

    fn clock(&mut self, s: f64, _w: f64, between: bool) {
        // (Records take the time of the simulation's own last stop: a move to wherever a frame or
        // a snapshot asked would make them depend on how the run was cut up.)
        if !between {
            self.0.borrow_mut().notes.s = s;
        }
    }

    fn swing(&mut self, sw: PlanSwing) {
        let mut j = self.0.borrow_mut();
        j.sound(Cause::Swing {
            s: sw.s,
            x: sw.x,
            kind: sw.kind,
        });
        j.push(Record::Moment(Moment::Swing {
            x: sw.x,
            y: sw.y,
            dir: sw.dir,
            kind: sw.kind,
        }));
    }

    fn input(&mut self, i: PlanInput) {
        if i.kind == "press" {
            self.0.borrow_mut().push(Record::Moment(Moment::Press {
                rating: i.rating,
                delta: i.delta.unwrap_or(0.0),
                unjudged: i.unjudged == Some(true),
                stomp: i.stomp == Some(true) || i.key == "space",
            }));
        }
    }

    fn groove(&mut self, _s: f64, v: f64) {
        self.0.borrow_mut().push(Record::Groove(v));
    }

    fn tier(&mut self, _s: f64, _w: f64, tier: f64, sound_s: f64) {
        let mut j = self.0.borrow_mut();
        if tier != j.notes.tier {
            let up = tier > j.notes.tier;
            j.sound(Cause::Tier { s: sound_s, up });
        }
        j.notes.tier = tier;
        j.push(Record::Tier(tier));
    }

    fn xp(&mut self, _s: f64, fill: f64) {
        self.0.borrow_mut().push(Record::Xp(fill));
    }

    fn upgrade(&mut self, u: sim::upgrades::UpgradeRecord) {
        let mut j = self.0.borrow_mut();
        j.push(Record::LevelUp);
        j.sound(Cause::LevelUp { s: u.s });
    }

    fn health(&mut self, _s: f64, frac: f64) {
        self.0.borrow_mut().push(Record::Health(frac));
    }

    fn settle(&mut self, r: SettleRecord) {
        self.0.borrow_mut().push(Record::Moment(Moment::Settled {
            provisional: r.provisional,
            settled: r.settled,
        }));
    }

    fn zone_wave(&mut self, z: ZoneWaveRecord) {
        self.0.borrow_mut().push(Record::ZoneWave {
            s: z.s,
            x: z.x,
            y: z.y,
            zone: z.zone,
        });
    }

    fn bite(&mut self, b: BiteRecord) {
        let mut j = self.0.borrow_mut();
        j.push(Record::Moment(Moment::Bite { damage: b.damage }));
        j.sound(Cause::Hurt { s: b.s });
    }
}

/// No key or arrival can still come for times more than this far before now (song s), unless the
/// engine's `settle_s` says otherwise (the app widens it to its key delivery and calibration).
pub const SETTLE_S: f64 = 0.04;
/// Snapshot spacing along the simulation's run (song s).
pub const SNAP_DT: f64 = 1.0 / 30.0;

/// A point the runner can roll back to: the simulation's state, the journal's mark, and how many
/// keys had been applied.
struct Snap {
    s: f64,
    sim: Snapshot,
    mark: Mark,
    keys: usize,
}

/// The runner's counters, for checks and the latency report.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct RunnerStats {
    pub rollbacks: u64,
    /// Song time simulated again after rollbacks.
    pub replayed_s: f64,
    /// Keys applied later than their time (at the settled time).
    pub late_keys: u64,
    /// Wall time spent simulating again after rollbacks (s).
    pub resim_s: f64,
    /// Rollbacks caused by a key (the rest by an arrival), and the deepest (song s).
    pub key_rollbacks: u64,
    pub deepest_s: f64,
}

/// The sim engine on a stream that grows as it arrives, run ahead of the time heard with rollback
/// (docs/design/native-app.md, "Arrivals roll back like late keys"; `live/runner.ts`): it runs to
/// `now + lead` assuming no new input, snapshotting as it goes; a key or an arrival for a time it
/// has passed restores the newest snapshot before it and runs ahead again. The view shows the
/// records up to now: those before the oldest snapshot kept (the newest at or before the settled
/// time) are final, the rest are laid over them afresh each frame, and moments are passed on once; sounds come from the speculation.
pub struct Engine {
    pub sim: Sim,
    /// What the drawer reads: the settled view with the unsettled records up to now laid over it.
    pub view: Rc<RefCell<LiveView>>,
    pub journal: Rc<RefCell<Journal>>,
    /// The records before the settled time, applied (and the absolute index after them).
    settled_view: LiveView,
    settled_idx: usize,
    /// The moments passed on whose records are not yet settled (to pass each on once).
    passed: Vec<(f64, Moment)>,
    /// The sounds the speculation causes, each with its record's journal index and known from the
    /// frame that first saw it; and when each cause was first seen (kept across rollbacks).
    causes: Vec<(usize, Caused)>,
    first_seen: Vec<(Cause, f64)>,
    /// The journal index from which sounds are looked for.
    sound_idx: usize,
    snaps: Vec<Snap>,
    /// Song time before which the state is final, this far behind now.
    settled: f64,
    pub settle_s: f64,
    /// The stamp of the last final record.
    final_stamp: f64,
    /// Keys waiting for the next frame, and every key as applied (from the oldest snapshot's).
    pending: Vec<KeyEvent>,
    applied: Vec<KeyEvent>,
    /// The earliest `known_at` of what arrived since the last frame.
    arrival: Option<f64>,
    pub stats: RunnerStats,
    /// The level's seed and zones (the open field's ground).
    pub seed: f64,
    pub zones: Vec<String>,
    /// The FNV-1a hash of the records settled so far (each as `{stamp:?} {record:?}`), and how
    /// many: a session keeps it, and a replay must settle the same.
    pub settled_hash: u64,
    pub settled_count: usize,
    /// Every record as it settles, and every key as applied, when kept (for determinism tests).
    pub settled_log: Option<Vec<(f64, Record)>>,
    pub key_log: Option<Vec<KeyEvent>>,
}

impl Engine {
    /// The reactive level `level` over `lib`, for a stream of evidence gate `gate`, with hindsight
    /// entries (`settling`) or not, of `file_duration` seconds (infinite live).
    pub fn new(
        level: &SourceFile,
        lib: &ContentLibrary,
        hero_anims: &IndexMap<String, AnimInfo>,
        gate: f64,
        settling: bool,
        file_duration: f64,
    ) -> Result<Engine, LevelError> {
        let song = reactive_song_map(file_duration);
        let level = load_level(level, lib, &song, None)?;
        let (seed, zones) = (level.seed, level.zones.clone());
        let input = SimInput::new(level, lib, hero_anims, gate, settling);
        let fresh = LiveView {
            health: 1.0,
            level: 1,
            camera: (0.0, 0.0, 1.0),
            ..LiveView::default()
        };
        let journal = Rc::new(RefCell::new(Journal::default()));
        let sim = Sim::new(
            input,
            Rc::new(StreamLog {
                hindsight: settling.then(Vec::new),
                timbre: Some(Vec::new()),
                changes: Some(Vec::new()),
                jumps: Some(Vec::new()),
                ends: Some(Vec::new()),
                forecasts: Some(Vec::new()),
                ..StreamLog::default()
            }),
            Box::new(LiveRecorder(Rc::clone(&journal))),
            None,
        );
        let first = Snap {
            s: sim.s(),
            sim: sim.snapshot(),
            mark: journal.borrow().mark(),
            keys: 0,
        };
        Ok(Engine {
            sim,
            view: Rc::new(RefCell::new(fresh.clone())),
            journal,
            settled_view: fresh,
            settled_idx: 0,
            passed: Vec::new(),
            causes: Vec::new(),
            first_seen: Vec::new(),
            sound_idx: 0,
            snaps: vec![first],
            settled: f64::NEG_INFINITY,
            settle_s: SETTLE_S,
            final_stamp: f64::NEG_INFINITY,
            pending: Vec::new(),
            applied: Vec::new(),
            arrival: None,
            stats: RunnerStats::default(),
            seed,
            zones,
            settled_hash: FNV_OFFSET,
            settled_count: 0,
            settled_log: None,
            key_log: None,
        })
    }

    /// Appends arrivals to the stream through `f` (each stamped with its arrival as `known_at`),
    /// noting the earliest, so the next frame rolls back to it if the simulation has passed it.
    /// An arrival stamped before the settled time (a stall) is stamped at it: nothing before the
    /// settled time may change.
    pub fn feed(&mut self, f: impl FnOnce(&mut StreamLog)) {
        let settled = self.settled;
        let log = self.sim.log_mut();
        let before = lengths(log);
        f(log);
        restamp_since(log, before, settled);
        if let Some(t) = earliest_since(log, before) {
            self.arrival = Some(self.arrival.map_or(t, |a| a.min(t)));
        }
    }

    /// The stream as it has arrived.
    pub fn stream(&mut self) -> &StreamLog {
        self.sim.log_mut()
    }

    /// A key pressed or released at song time `s`, applied at the next frame.
    pub fn key(&mut self, s: f64, key: InputKey, pressed: bool) {
        self.pending.push(KeyEvent {
            s,
            kind: if pressed {
                KeyEventKind::Press
            } else {
                KeyEventKind::Release
            },
            key,
            id: None,
        });
    }

    /// The keys as applied, at their effective times.
    pub fn applied(&self) -> &[KeyEvent] {
        &self.applied
    }

    /// The sounds the simulation causes, as it now speculates: those a rollback undid are gone
    /// (until the second after their time; the scheduler cancels what hasn't started).
    pub fn causes(&self) -> impl Iterator<Item = &Caused> {
        self.causes.iter().map(|(_, c)| c)
    }

    /// The song time the simulation has run to (the speculation's frontier).
    pub fn frontier(&self) -> f64 {
        self.sim.s()
    }

    /// One frame at song time `now`: applies the keys and arrivals since the last (rolling back
    /// when the simulation has passed them), runs ahead to `now + lead` and shows `now`.
    pub fn frame(&mut self, now: f64, lead: f64) {
        let mut keys = std::mem::take(&mut self.pending);
        keys.sort_by(|a, b| a.s.total_cmp(&b.s));
        let first_key = keys.first().map(|k| k.s);
        let earliest = first_key
            .into_iter()
            .chain(self.arrival.take())
            .reduce(f64::min);
        let mut resim_to = None;
        if let Some(t) = earliest
            && t < self.sim.s() - 1e-12
        {
            resim_to = Some(self.sim.s());
            if first_key == Some(t) {
                self.stats.key_rollbacks += 1;
            }
            self.stats.deepest_s = self.stats.deepest_s.max(self.sim.s() - t);
            self.roll_back(t.max(self.settled));
        }
        for mut k in keys {
            // (A very late key, a main-thread stall's, is applied at the settled time instead.)
            let t = k.s.max(self.settled).max(self.snaps[0].s);
            if t > k.s {
                self.stats.late_keys += 1;
            }
            // (Queued ahead of the clock: the simulation reaches it at its own stop, as a run fed
            // the key in time would. Running the clock onto it first would change the record.)
            k.s = t;
            self.sim.key(k);
            self.applied.push(k);
            if let Some(log) = &mut self.key_log {
                log.push(k);
            }
        }
        if let Some(to) = resim_to {
            // (Back to where the simulation had been: the rollback's cost.)
            let t0 = std::time::Instant::now();
            self.run(to.min(now + lead));
            self.stats.resim_s += t0.elapsed().as_secs_f64();
        }
        self.run(now + lead);
        self.settled = self.settled.max(now - self.settle_s);
        // Snapshots older than the settled time go, except the newest one before it.
        let k = self
            .snaps
            .iter()
            .rposition(|sn| sn.s <= self.settled)
            .unwrap_or(0);
        if k > 0 {
            self.snaps.drain(..k);
            let drop = self.snaps[0].keys;
            self.applied.drain(..drop);
            for sn in &mut self.snaps {
                sn.keys -= drop;
            }
        }
        self.show(now);
        let keep = self
            .settled_idx
            .min(self.snaps[0].mark.len)
            .min(self.sound_idx);
        self.journal.borrow_mut().forget(keep);
    }

    /// Runs the rules to song time `s` without looking ahead, and shows it (a replay that feeds
    /// keys in time: no rollback is ever needed).
    pub fn advance_to(&mut self, s: f64) {
        self.frame(s, 0.0);
    }

    /// Runs the simulation to song time `t`, snapshotting on the way.
    fn run(&mut self, t: f64) {
        while self.sim.s() < t - 1e-9 && !self.sim.done() {
            let last = self.snaps.last().map_or(f64::NEG_INFINITY, |sn| sn.s);
            let next = t.min(last + SNAP_DT);
            let before = self.sim.s();
            self.sim.advance_to(next);
            if self.sim.s() <= before {
                // (One of the simulation's own stops sits right at `next`, and its clock waits just
                // short of it: step through it, unless `next` is where this run ends.)
                if next >= t {
                    break;
                }
                self.sim.advance_to(t.min(next + 1e-6));
                if self.sim.s() <= before {
                    break;
                }
            }
            if self.sim.s() >= last + SNAP_DT - 1e-6 {
                let snap = Snap {
                    s: self.sim.s(),
                    sim: self.sim.snapshot(),
                    mark: self.journal.borrow().mark(),
                    keys: self.applied.len(),
                };
                self.snaps.push(snap);
            }
        }
    }

    /// Restores the newest snapshot before song time `t` and rewinds the journal to it.
    fn roll_back(&mut self, t: f64) {
        let i = self.snaps.iter().rposition(|sn| sn.s < t).unwrap_or(0);
        let snap = &self.snaps[i];
        debug_assert!(
            self.settled_idx <= snap.mark.len,
            "rolled back past a final record"
        );
        self.stats.rollbacks += 1;
        self.stats.replayed_s += self.sim.s() - snap.s;
        self.sim.restore(&snap.sim);
        self.journal.borrow_mut().rewind(&snap.mark);
        self.sound_idx = self.sound_idx.min(snap.mark.len);
        let len = snap.mark.len;
        self.causes.retain(|(k, _)| *k < len);
        // (Records before the settled time come back the same: the settled view keeps them.)
        let keys = snap.keys;
        self.snaps.truncate(i + 1);
        // Keys applied after the snapshot go back in, in the order they were applied.
        for k in &self.applied[keys..] {
            self.sim.key(*k);
        }
    }

    /// Shows song time `now`: settles the records before the settled time, lays the rest up to
    /// `now` over them, and passes on new moments and sounds.
    fn show(&mut self, now: f64) {
        let j = self.journal.borrow();
        // Moments with the song time of the record that made them.
        let mut fresh: Vec<(f64, Moment)> = Vec::new();
        // Final are the records before the oldest snapshot's mark: no rollback can rewind them
        // (one restores that snapshot at the earliest), so they never need simulating alike again.
        let last_final = self.snaps[0].mark.len;
        while self.settled_idx < last_final
            && let Some((s, rec)) = j.get(self.settled_idx)
        {
            self.settled_view.apply(*s, rec);
            self.final_stamp = *s;
            self.settled_hash = fnv(self.settled_hash, record_bytes(*s, rec).as_bytes());
            self.settled_count += 1;
            fresh.extend(self.settled_view.moments.drain(..).map(|m| (*s, m)));
            if let Some(log) = &mut self.settled_log {
                log.push((*s, rec.clone()));
            }
            self.settled_idx += 1;
        }
        let mut shown = self.settled_view.clone();
        let mut k = self.settled_idx;
        while let Some((s, rec)) = j.get(k) {
            if *s > now + 1e-9 {
                break;
            }
            shown.apply(*s, rec);
            fresh.extend(shown.moments.drain(..).map(|m| (*s, m)));
            k += 1;
        }
        // The speculation's sounds, each known from the frame that first saw it.
        let mut k = self.sound_idx;
        while let Some((_, rec)) = j.get(k) {
            if let Record::Sound(c) = rec {
                let known =
                    if let Some(&(_, t)) = self.first_seen.iter().find(|(d, _)| *d == c.cause) {
                        t
                    } else {
                        self.first_seen.push((c.cause.clone(), now));
                        now
                    };
                self.causes.push((
                    k,
                    Caused {
                        cause: c.cause.clone(),
                        known,
                    },
                ));
            }
            k += 1;
        }
        self.sound_idx = k;
        drop(j);
        let settled = self.settled;
        self.first_seen.retain(|(c, _)| c.s() > settled - 1.0);
        self.causes.retain(|(_, c)| c.cause.s() > settled - 1.0);
        // Each moment once: those an earlier frame's overlay passed on are not passed again.
        let mut v = self.view.borrow_mut();
        let mut moments = std::mem::take(&mut v.moments);
        for (s, m) in fresh {
            if !self.passed.iter().any(|(ps, pm)| *ps == s && *pm == m) {
                moments.push(m.clone());
                self.passed.push((s, m));
            }
        }
        // (Records after the last final one are stamped no earlier than it: a moment passed on
        // is remembered until its record is final, whatever the snapshot's own time.)
        let final_stamp = self.final_stamp;
        self.passed.retain(|(s, _)| *s >= final_stamp);
        *v = shown;
        v.moments = moments;
    }

    /// Whether the run has ended for good: the finale's epilogue (a confirmed end's) is over in
    /// the final state.
    pub fn done(&self) -> bool {
        self.sim.end_s() <= self.settled
    }

    /// Whether the hero has fallen, for good (in the final records: a fall a late key or arrival
    /// could still undo doesn't end the run).
    pub fn fallen(&self) -> bool {
        self.settled_view.health <= 0.0
    }
}

impl Cause {
    /// The song time the sound belongs to.
    pub fn s(&self) -> f64 {
        match self {
            Cause::Swing { s, .. }
            | Cause::Kill { s, .. }
            | Cause::Weapon { s, .. }
            | Cause::Hurt { s }
            | Cause::Tier { s, .. }
            | Cause::Step { s, .. }
            | Cause::LevelUp { s } => *s,
        }
    }
}

const FNV_OFFSET: u64 = 0xcbf2_9ce4_8422_2325;

/// A settled record as its hash reads it.
fn record_bytes(s: f64, rec: &Record) -> String {
    format!("{s:?} {rec:?}")
}

/// The hash of settled records, as [`Engine::settled_hash`] runs it.
pub fn hash_records(records: &[(f64, Record)]) -> u64 {
    records.iter().fold(FNV_OFFSET, |h, (s, rec)| {
        fnv(h, record_bytes(*s, rec).as_bytes())
    })
}

/// FNV-1a over `bytes`, from `h`.
fn fnv(mut h: u64, bytes: &[u8]) -> u64 {
    for &b in bytes {
        h ^= u64::from(b);
        h = h.wrapping_mul(0x0100_0000_01b3);
    }
    h
}

/// Each stream group's length.
fn lengths(log: &StreamLog) -> [usize; 9] {
    fn n<T>(v: Option<&Vec<T>>) -> usize {
        v.map_or(0, Vec::len)
    }
    [
        log.beats.len(),
        log.ticks.len(),
        log.onsets.len(),
        n(log.hindsight.as_ref()),
        n(log.timbre.as_ref()),
        n(log.changes.as_ref()),
        n(log.jumps.as_ref()),
        n(log.ends.as_ref()),
        n(log.forecasts.as_ref()),
    ]
}

/// Stamps the entries past `before`'s lengths known before `settled` at it.
fn restamp_since(log: &mut StreamLog, before: [usize; 9], settled: f64) {
    fn up<T>(v: Option<&mut Vec<T>>, from: usize, settled: f64, k: impl Fn(&mut T) -> &mut f64) {
        if let Some(v) = v
            && let Some(new) = v.get_mut(from..)
        {
            for e in new {
                let known = k(e);
                *known = known.max(settled);
            }
        }
    }
    up(Some(&mut log.beats), before[0], settled, |e| {
        &mut e.known_at
    });
    up(Some(&mut log.ticks), before[1], settled, |e| {
        &mut e.known_at
    });
    up(Some(&mut log.onsets), before[2], settled, |e| {
        &mut e.known_at
    });
    up(log.hindsight.as_mut(), before[3], settled, |e| {
        &mut e.known_at
    });
    up(log.timbre.as_mut(), before[4], settled, |e| &mut e.known_at);
    up(log.changes.as_mut(), before[5], settled, |e| {
        &mut e.known_at
    });
    up(log.jumps.as_mut(), before[6], settled, |e| &mut e.known_at);
    up(log.ends.as_mut(), before[7], settled, |e| &mut e.known_at);
    up(log.forecasts.as_mut(), before[8], settled, |e| {
        &mut e.known_at
    });
}

/// The earliest `known_at` of the entries past `before`'s lengths.
fn earliest_since(log: &StreamLog, before: [usize; 9]) -> Option<f64> {
    fn min<T>(v: Option<&Vec<T>>, from: usize, k: impl Fn(&T) -> f64) -> Option<f64> {
        v?.get(from..)?.iter().map(k).reduce(f64::min)
    }
    [
        min(Some(&log.beats), before[0], |e| e.known_at),
        min(Some(&log.ticks), before[1], |e| e.known_at),
        min(Some(&log.onsets), before[2], |e| e.known_at),
        min(log.hindsight.as_ref(), before[3], |e| e.known_at),
        min(log.timbre.as_ref(), before[4], |e| e.known_at),
        min(log.changes.as_ref(), before[5], |e| e.known_at),
        min(log.jumps.as_ref(), before[6], |e| e.known_at),
        min(log.ends.as_ref(), before[7], |e| e.known_at),
        min(log.forecasts.as_ref(), before[8], |e| e.known_at),
    ]
    .into_iter()
    .flatten()
    .reduce(f64::min)
}
