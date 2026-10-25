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
use sim::engine::{AnimInfo, HERO_ANIMS, Sim, SimInput};
use sim::movement::InputKey;
use sim::player::{KeyEvent, KeyEventKind};
use sim::recorder::{
    BiteRecord, EnemyRecord, Fate, Hit, HitSource, Landing, PlanInput, PlanSwing, Recorder,
    SettleRecord,
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
    /// Sounds caused since the mixer's scheduler last took them.
    pub sounds: Vec<Caused>,
    /// The song time shown (the latest record's).
    pub s: f64,
}

impl LiveView {
    /// The moments since the last call.
    pub fn take_moments(&mut self) -> Vec<Moment> {
        std::mem::take(&mut self.moments)
    }

    /// The sounds caused since the last call.
    pub fn take_sounds(&mut self) -> Vec<Caused> {
        std::mem::take(&mut self.sounds)
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
            Record::Sound(c) => self.sounds.push(c.clone()),
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
    Spawn { id: usize, kind: String },
    Sample { id: usize, x: f64, y: f64 },
    Death { id: usize, w: f64, recycled: bool },
    Moment(Moment),
    Groove(f64),
    Tier(f64),
    Xp(f64),
    LevelUp,
    Health(f64),
    Sound(Caused),
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

    fn clock(&mut self, s: f64, _w: f64, _between: bool) {
        self.0.borrow_mut().notes.s = s;
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

    fn bite(&mut self, b: BiteRecord) {
        let mut j = self.0.borrow_mut();
        j.push(Record::Moment(Moment::Bite { damage: b.damage }));
        j.sound(Cause::Hurt { s: b.s });
    }
}

/// The sim engine on a stream that grows as it arrives, its view shared with the drawer.
pub struct Engine {
    pub sim: Sim,
    pub view: Rc<RefCell<LiveView>>,
    /// What the simulation reported, and how much of it the view has applied (an absolute index).
    pub journal: Rc<RefCell<Journal>>,
    shown: usize,
    /// The level's seed and zones (the open field's ground).
    pub seed: f64,
    pub zones: Vec<String>,
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
        let view = Rc::new(RefCell::new(LiveView {
            health: 1.0,
            level: 1,
            camera: (0.0, 0.0, 1.0),
            ..LiveView::default()
        }));
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
        Ok(Engine {
            sim,
            view,
            journal,
            shown: 0,
            seed,
            zones,
        })
    }

    /// The stream, to append arrivals to (each stamped with its arrival as `known_at`).
    pub fn log(&mut self) -> &mut StreamLog {
        self.sim.log_mut()
    }

    /// A key pressed or released at song time `s` (not before the simulation's time).
    pub fn key(&mut self, s: f64, key: InputKey, pressed: bool) {
        let s = s.max(self.sim.s());
        self.sim.key(KeyEvent {
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

    /// Runs the rules to song time `s` and shows what they reported up to it.
    pub fn advance_to(&mut self, s: f64) {
        if s > self.sim.s() {
            self.sim.advance_to(s);
        }
        self.show(s);
        let shown = self.shown;
        self.journal.borrow_mut().forget(shown);
    }

    /// Applies the records reported up to song time `now` to the view.
    pub fn show(&mut self, now: f64) {
        let j = self.journal.borrow();
        let mut v = self.view.borrow_mut();
        while let Some((s, rec)) = j.get(self.shown) {
            if *s > now + 1e-9 {
                break;
            }
            v.apply(*s, rec);
            self.shown += 1;
        }
    }

    /// Whether the hero has fallen.
    pub fn fallen(&self) -> bool {
        self.view.borrow().health <= 0.0
    }
}
