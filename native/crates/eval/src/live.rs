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
    /// The simulation's song time (the latest clock).
    pub s: f64,
    /// Recent hits (id, song time, source, weapon, where): fates name them.
    hits: VecDeque<(f64, f64, HitSource, String, f64)>,
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

    fn sound(&mut self, cause: Cause) {
        let known = self.s;
        self.sounds.push(Caused { cause, known });
    }

    /// The living enemies.
    pub fn alive(&self) -> impl Iterator<Item = (&usize, &Foe)> {
        self.foes.iter().filter(|(_, f)| f.death_w.is_none())
    }
}

/// The recorder that keeps a [`LiveView`] (shared with the drawer).
pub struct LiveRecorder(pub Rc<RefCell<LiveView>>);

impl Recorder for LiveRecorder {
    fn hero(&mut self, x: f64, y: f64, z: f64, facing: f64, anim: f64, frame: f64) {
        let anim = HERO_ANIMS.get(anim as usize).copied().unwrap_or("idle");
        self.0.borrow_mut().hero = Some(HeroPose {
            x,
            y,
            z,
            facing: (facing as usize).min(3),
            anim,
            frame: frame.max(0.0) as usize,
        });
    }

    fn camera(&mut self, x: f64, y: f64, zoom: f64) {
        self.0.borrow_mut().camera = (x, y, zoom);
    }

    fn spawn(&mut self, rec: EnemyRecord) {
        self.0.borrow_mut().foes.insert(
            rec.id as usize,
            Foe {
                kind: rec.kind,
                x: f64::NAN,
                y: f64::NAN,
                prev: (f64::NAN, f64::NAN),
                samples: 0,
                death_w: None,
            },
        );
    }

    fn sample(&mut self, id: usize, x: f64, y: f64) {
        if let Some(f) = self.0.borrow_mut().foes.get_mut(&id) {
            f.prev = if f.samples == 0 { (x, y) } else { (f.x, f.y) };
            f.x = x;
            f.y = y;
            f.samples += 1;
        }
    }

    fn fate(&mut self, _id: usize, fate: Fate) {
        let mut v = self.0.borrow_mut();
        v.moments.push(Moment::Hit {
            x: fate.x,
            y: fate.y,
            lethal: fate.lethal,
        });
        let Some(k) = v.hits.iter().position(|h| h.0 == fate.hit) else {
            return;
        };
        let (_, s, source, what, _) = v.hits[k].clone();
        if fate.lethal {
            v.sound(Cause::Kill { s, x: fate.x });
        }
        // (A weapon sounds on the first enemy it catches; the hit is forgotten after.)
        if source == HitSource::Weapon {
            v.hits.remove(k);
            v.sound(Cause::Weapon {
                s,
                x: fate.x,
                what,
                caught: true,
            });
        }
    }

    fn death(&mut self, id: usize, w: f64, recycled: bool) {
        let mut v = self.0.borrow_mut();
        let Some(f) = v.foes.get_mut(&id) else {
            return;
        };
        if recycled {
            // (It leaves the field and comes back elsewhere as a new spawn.)
            v.foes.remove(&id);
            return;
        }
        f.death_w = Some(w);
        let moment = Moment::Death {
            kind: f.kind.clone(),
            x: f.x,
            y: f.y,
        };
        v.moments.push(moment);
        v.kills += 1;
        // Keep the dead only briefly (the drawer fades them).
        let dead: Vec<usize> = v
            .foes
            .iter()
            .filter(|(_, f)| f.death_w.is_some_and(|d| d < w - 2.0))
            .map(|(&i, _)| i)
            .collect();
        for i in dead {
            v.foes.remove(&i);
        }
    }

    fn hit(&mut self, h: Hit) {
        let mut v = self.0.borrow_mut();
        let x = match h.shape {
            Shape::Arc { x, .. } | Shape::Circle { x, .. } | Shape::Ring { x, .. } => x,
            Shape::Capsule { x1, .. } => x1,
            #[allow(unreachable_patterns)]
            _ => v.hero.map_or(0.0, |p| p.x),
        };
        if h.source == HitSource::Weapon {
            v.sound(Cause::Weapon {
                s: h.s,
                x,
                what: h.what.clone(),
                caught: false,
            });
        }
        v.hits.push_back((h.id, h.s, h.source, h.what, x));
        if v.hits.len() > 64 {
            v.hits.pop_front();
        }
    }

    fn landing(&mut self, l: Landing) {
        if l.stomp != Some(true) {
            self.0.borrow_mut().sound(Cause::Step { s: l.s, x: l.x });
        }
    }

    fn clock(&mut self, s: f64, _w: f64, _between: bool) {
        self.0.borrow_mut().s = s;
    }

    fn swing(&mut self, sw: PlanSwing) {
        self.0.borrow_mut().sound(Cause::Swing {
            s: sw.s,
            x: sw.x,
            kind: sw.kind,
        });
        self.0.borrow_mut().moments.push(Moment::Swing {
            x: sw.x,
            y: sw.y,
            dir: sw.dir,
            kind: sw.kind,
        });
    }

    fn input(&mut self, i: PlanInput) {
        if i.kind == "press" {
            let mut v = self.0.borrow_mut();
            match i.rating {
                Some("perfect") => v.ratings[0] += 1,
                Some("great") => v.ratings[1] += 1,
                Some("good") => v.ratings[2] += 1,
                _ => {}
            }
            v.moments.push(Moment::Press {
                rating: i.rating,
                delta: i.delta.unwrap_or(0.0),
                unjudged: i.unjudged == Some(true),
                stomp: i.stomp == Some(true) || i.key == "space",
            });
        }
    }

    fn groove(&mut self, _s: f64, v: f64) {
        self.0.borrow_mut().groove = v;
    }

    fn tier(&mut self, _s: f64, _w: f64, tier: f64, sound_s: f64) {
        let mut v = self.0.borrow_mut();
        let up = tier > v.tier;
        if tier != v.tier {
            v.sound(Cause::Tier { s: sound_s, up });
        }
        v.tier = tier;
    }

    fn xp(&mut self, _s: f64, fill: f64) {
        self.0.borrow_mut().xp = fill;
    }

    fn upgrade(&mut self, u: sim::upgrades::UpgradeRecord) {
        let mut v = self.0.borrow_mut();
        v.level += 1;
        v.sound(Cause::LevelUp { s: u.s });
    }

    fn health(&mut self, _s: f64, frac: f64) {
        self.0.borrow_mut().health = frac;
    }

    fn settle(&mut self, r: SettleRecord) {
        self.0.borrow_mut().moments.push(Moment::Settled {
            provisional: r.provisional,
            settled: r.settled,
        });
    }

    fn bite(&mut self, b: BiteRecord) {
        let mut v = self.0.borrow_mut();
        v.moments.push(Moment::Bite { damage: b.damage });
        v.sound(Cause::Hurt { s: b.s });
    }
}

/// The sim engine on a stream that grows as it arrives, its view shared with the drawer.
pub struct Engine {
    pub sim: Sim,
    pub view: Rc<RefCell<LiveView>>,
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
            Box::new(LiveRecorder(Rc::clone(&view))),
            None,
        );
        Ok(Engine {
            sim,
            view,
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

    /// Runs the rules to song time `s`.
    pub fn advance_to(&mut self, s: f64) {
        if s > self.sim.s() {
            self.sim.advance_to(s);
        }
    }

    /// Whether the hero has fallen.
    pub fn fallen(&self) -> bool {
        self.view.borrow().health <= 0.0
    }
}
