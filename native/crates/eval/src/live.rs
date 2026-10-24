//! The live view (docs/design/native-app.md, "The engine in the app (G1)"): the sim engine on a
//! growing stream, and a minimal view of its state for drawing, kept by a [`Recorder`] as the
//! simulation reports it: the hero's pose, the camera, the enemies' latest positions, health, the
//! groove, and the moments a frame shows once (hits, deaths, presses, bites).

use std::cell::RefCell;
use std::collections::BTreeMap;
use std::rc::Rc;

use indexmap::IndexMap;
use sim::content::level::{LevelError, load_level};
use sim::content::{ContentLibrary, SourceFile};
use sim::engine::{AnimInfo, HERO_ANIMS, Sim, SimInput};
use sim::movement::InputKey;
use sim::player::{KeyEvent, KeyEventKind};
use sim::recorder::{
    BiteRecord, EnemyRecord, Fate, Hit, PlanInput, PlanSwing, Recorder, SettleRecord,
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
    /// Moments since the drawer last took them.
    pub moments: Vec<Moment>,
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
        self.0.borrow_mut().moments.push(Moment::Hit {
            x: fate.x,
            y: fate.y,
            lethal: fate.lethal,
        });
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

    fn hit(&mut self, _h: Hit) {}

    fn swing(&mut self, sw: PlanSwing) {
        self.0.borrow_mut().moments.push(Moment::Swing {
            x: sw.x,
            y: sw.y,
            dir: sw.dir,
            kind: sw.kind,
        });
    }

    fn input(&mut self, i: PlanInput) {
        if i.kind == "press" {
            self.0.borrow_mut().moments.push(Moment::Press {
                rating: i.rating,
                unjudged: i.unjudged == Some(true),
                stomp: i.stomp == Some(true) || i.key == "space",
            });
        }
    }

    fn groove(&mut self, _s: f64, v: f64) {
        self.0.borrow_mut().groove = v;
    }

    fn tier(&mut self, _s: f64, _w: f64, tier: f64, _sound_s: f64) {
        self.0.borrow_mut().tier = tier;
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
        self.0
            .borrow_mut()
            .moments
            .push(Moment::Bite { damage: b.damage });
    }
}

/// The sim engine on a stream that grows as it arrives, its view shared with the drawer.
pub struct Engine {
    pub sim: Sim,
    pub view: Rc<RefCell<LiveView>>,
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
        let input = SimInput::new(level, lib, hero_anims, gate, settling, None);
        let view = Rc::new(RefCell::new(LiveView {
            health: 1.0,
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
        Ok(Engine { sim, view })
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
