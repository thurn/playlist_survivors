//! `src/sim/simulate.ts`: the simulated playthrough. The game rules run forward in time, driven by
//! key presses, and everything they produce goes to a [`Recorder`].
//!
//! The rule that keeps it honest: the simulation may use the song so far and the past and present,
//! never a future press. Time advances in fixed steps of 1/120 s of song time; presses, strikes,
//! weapon hits and other events happen at their exact times between steps.
//!
//! **Streaming.** The TS simulation was built on the whole stream and pushed every stream-derived
//! event at its start. Here the stream grows: an entry is ingested once the loop's next stop reaches
//! its `known_at` (it is in the log, and readers still gate by `known_at` as the TS readers do), and
//! its events are queued then, with keys ([`QKey`]) that order them exactly as the TS queue ordered
//! its pushes at init: (time, kind's order, init phase, index), with every runtime push after them
//! in push order. Committed beats are known at least one beat ahead (every shipped beat is known by
//! the previous beat's time), so the growing song map, schedule and judging agree with TS's for the
//! reads around now. Where TS read a beat time further ahead (a downbeat, an orb's or a gem's grid
//! time, a cleave's bands, a chain's next jump, a stomp's last cancelled held stride), the event is
//! deferred until that beat arrives, keeping the place in push order it had (`push_at_beat`,
//! `defer_at_beat`): the time always falls at or after the beat before it, which is known by then.
//!
//! **Scope (B4, docs/design/native-app.md).** State, the queue, the hero and strides, swings, enemies
//! and their beat steps, weapons, damage, gems, orbs, chickens, level-ups, healing, and press judging
//! with the settled judgment. Set pieces, the boss, archers, zone waves, landmarks, the finale and
//! the run's end come with B6; the recording's emission (effects, sounds, UI) with B7.

#![allow(clippy::many_single_char_names, clippy::manual_midpoint)] // (the TS names and arithmetic, as written)

mod combat;
mod foes;
mod hero;
mod judge;
mod pickups;

use std::collections::HashSet;
use std::rc::Rc;

use indexmap::IndexMap;
use stream::{StreamEnd, StreamLog};

use crate::camera::CameraRig;
use crate::content::ContentLibrary;
use crate::content::level::LevelScript;
use crate::core::songmap::{SongIndex, SongMap, index_song};
use crate::core::warp::WarpBreak;
use crate::jsmath::{floor, max, min, round};
use crate::movement::{Key, Step};
use crate::player::{KeyEvent, KeyEventKind};
use crate::reactive::judging::Judging;
use crate::reactive::settle::{SettlePress, Settler};
use crate::recorder::{ChainPoint, Recorder};
use crate::schedule::{ReactiveSchedule, next_step_after, step_rate};
use crate::strides::{StrideRule, StrideState, fresh_strides, stride_rule};
use crate::timing::PressMemo;
use crate::upgrades::{Loadout, PassiveBonus, UpgradeItem, starting_loadout};

/// Fixed simulation steps per song second.
pub const SIM_RATE: f64 = 120.0;
/// Hero samples per world second.
pub const HERO_RATE: f64 = 60.0;
/// Enemy samples per world second.
pub const ENEMY_RATE: f64 = 60.0;
pub const HURTBOX_RADIUS: f64 = 10.0;
pub const HURTBOX_LIFT: f64 = 20.0;
const CONTACT_GAP: f64 = 4.0;

/// (hv-s4pt.12) The fairness knobs of the settled judgment.
pub struct Fair {
    pub benefit_of_doubt: bool,
    pub steady_run: f64,
    pub floor: f64,
    pub full_at: f64,
}
pub const FAIR: Fair = Fair {
    benefit_of_doubt: true,
    steady_run: 4.0,
    floor: 0.0,
    full_at: 0.55,
};

/// (hv-s4pt.13) Beatless credit and the lock-on grace.
pub struct Beatless {
    pub per_beat: f64,
    pub after_s: f64,
    pub grace_s: f64,
    pub unsteady_grace: f64,
}
pub const BEATLESS: Beatless = Beatless {
    per_beat: 0.08,
    after_s: 2.0,
    grace_s: 8.0,
    unsteady_grace: 0.5,
};

const FORECAST_CONFIDENCE: f64 = 0.6;
const FORECAST_WINDOW: f64 = 0.05;
const STEP_SEEN: f64 = 0.5;
const BITE_SLACK: f64 = 2.0;
pub const SPAWN_PER_BEAT: f64 = 120.0;
const SPAWN_FORWARD: f64 = 0.6;
pub const SPAWN_MARGIN: f64 = 16.0;
const SPAWN_EXTRA: f64 = 12.0;
const SEPARATION: f64 = 0.8;
const MIN_GAP: f64 = 0.6;
pub const KNOCKBACK: f64 = 10.0;
const KNOCK_TIME: f64 = 0.12;
const RECYCLE_VIEWS: f64 = 1.5;
pub const POP_CAP: f64 = 520.0;
pub const STREAM_SHARE: f64 = 0.6;
pub const LEVEL_PULSE: f64 = 160.0;
pub const ORB_RISE: f64 = 120.0;
pub const ORB_TOUCH: f64 = 24.0;
pub const ORB_BARS: f64 = 2.0;
pub const CHICKEN_CHANCE: f64 = 0.5;
pub const CHICKEN_BOSS: f64 = 2.0;
pub const CHICKEN_SPREAD: f64 = 28.0;
pub const CHICKEN_TOUCH: f64 = 24.0;
pub const CHICKEN_HEAL: f64 = 30.0;
const ORB_GRID_WAIT: f64 = 2.0;
pub const GEM_CAP: usize = 300;
pub const VACUUM_AHEAD: f64 = 150.0;
pub const VACUUM_TOUCH: f64 = 24.0;
pub const HITSTOP_MAX: f64 = 0.15;
pub const HITSTOP_MIN: f64 = 0.04;
const DIP_RATE: f64 = 0.4;
const CALM_CURL: f64 = crate::jsmath::PI / 3.0;
const DIP_RAMP: f64 = 0.04;
const DIP_HOLD: f64 = 0.07;
const ARCHER_RANGE: f64 = 220.0;
const CLEAVE_AIM_AHEAD: f64 = 160.0;
const CHARGE_SPEED: f64 = 4.0;
const HEADING_BEATS: f64 = 2.0;
const HEADING_MIN: f64 = 6.0;
const CLOCK_EPS: f64 = 1e-9;
const HERO_HISTORY: f64 = 10.0;
const CELL: f64 = 32.0;
const G: i64 = 192;

pub const HERO_ANIMS: [&str; 11] = [
    "idle",
    "walk",
    "run",
    "jump",
    "slash",
    "backslash",
    "halfslash",
    "thrust",
    "spellcast",
    "slash-oversize",
    "hurt",
];

/// Per-animation facts from the hero atlas.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct AnimInfo {
    pub frames: f64,
    pub impact_frame: Option<f64>,
}

/// `DEFAULT_HERO_ANIMS` (src/planner/context.ts).
pub fn default_hero_anims() -> IndexMap<String, AnimInfo> {
    let a = |frames: f64, impact: Option<f64>| AnimInfo {
        frames,
        impact_frame: impact,
    };
    [
        ("idle", a(2.0, None)),
        ("walk", a(9.0, None)),
        ("run", a(8.0, None)),
        ("jump", a(5.0, None)),
        ("slash", a(6.0, Some(4.0))),
        ("backslash", a(7.0, Some(4.0))),
        ("halfslash", a(6.0, Some(2.0))),
        ("thrust", a(8.0, Some(5.0))),
        ("spellcast", a(7.0, Some(4.0))),
        ("slash-oversize", a(6.0, Some(4.0))),
        ("hurt", a(6.0, None)),
    ]
    .into_iter()
    .map(|(k, v)| (k.to_string(), v))
    .collect()
}

/// What a simulation is built from: the reactive level, the content library with the level's hero
/// and enemy overrides, the hero atlas's animations, the stream's gate and, in Replay, its end.
#[derive(Clone, Debug)]
pub struct SimInput {
    pub level: LevelScript,
    pub lib: ContentLibrary,
    pub hero_anims: IndexMap<String, AnimInfo>,
    pub seed: f64,
    /// The stream's evidence gate (`params.gate`).
    pub gate: f64,
    /// The stream's end, known in Replay: its duration and final counts.
    pub end: Option<StreamEnd>,
    /// Check-only: damage to the hero is off.
    pub no_hero_damage: bool,
    /// Stage 2: the stream has hindsight entries, so presses settle 2 s late.
    pub settling: bool,
}

impl SimInput {
    /// The input for a reactive level: the level's hero and enemy overrides applied to the library
    /// (`makeCtx`), the atlas's animations over the defaults, the level's seed.
    pub fn new(
        level: LevelScript,
        content: &ContentLibrary,
        atlas: &IndexMap<String, AnimInfo>,
        gate: f64,
        settling: bool,
        end: Option<StreamEnd>,
    ) -> SimInput {
        let mut lib = content.clone();
        lib.hero = level.hero.clone();
        for (id, over) in &level.enemies {
            if let Some(e) = lib.enemies.get_mut(id) {
                if let Some(c) = over.contact_damage {
                    e.contact_damage = c;
                }
                if let Some(a) = over.attack_damage {
                    e.attack_damage = a;
                }
            }
        }
        let mut hero_anims = default_hero_anims();
        for (k, v) in atlas {
            hero_anims.insert(k.clone(), *v);
        }
        let seed = level.seed;
        SimInput {
            level,
            lib,
            hero_anims,
            seed,
            gate,
            end,
            no_hero_damage: false,
            settling,
        }
    }

    /// The song's length, known in Replay (`ctx.duration`); infinite live.
    pub fn duration(&self) -> f64 {
        self.end.map_or(f64::INFINITY, |e| e.duration)
    }
}

/// What never changes during a simulation: shared by every snapshot.
#[derive(Debug)]
struct Env {
    input: SimInput,
    end_s: f64,
    cut_s: f64,
    rule: StrideRule,
    /// Stage 4: the director's detected section starts (B6) are not known yet: none.
    director: crate::content::level::Director,
}

// ------------------------------------------------------------------ the queue

/// An event's place in the queue (docs/design/native-app.md, "Event queue keys"): its time, its
/// kind's order at equal times, then its init phase and index (events the TS queue pushed at init),
/// or, for events pushed while running, `RUNTIME` and a counter in push order.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct QKey {
    pub s: f64,
    pub o: f64,
    pub phase: u16,
    pub index: u64,
}

/// Init phases, in the order the TS `init` pushed them.
mod phase {
    pub const AUTO: u16 = 2;
    pub const HELD: u16 = 3;
    pub const BEAT: u16 = 4;
    pub const LEVEL_DUE: u16 = 5;
    pub const REACTIVE_BEAT: u16 = 20;
    pub const ONSET: u16 = 21;
    pub const RUNTIME: u16 = u16::MAX;
}

impl QKey {
    /// After `b` at equal times (TS's seq, here the phase, then the index or push counter).
    fn rank_less(&self, b: &QKey) -> bool {
        self.phase < b.phase || (self.phase == b.phase && self.index < b.index)
    }

    fn less(&self, b: &QKey) -> bool {
        self.s < b.s || (self.s == b.s && (self.o < b.o || (self.o == b.o && self.rank_less(b))))
    }
}

#[derive(Clone, Debug)]
enum Ev {
    Key(KeyEvent),
    Held,
    HitstopEnd,
    Wind(usize),
    Strike(usize),
    Fire {
        weapon: usize,
        bi: f64,
    },
    Chain {
        weapon: usize,
        chain: usize,
        jump: usize,
    },
    Beat {
        bi: usize,
    },
    Appear {
        kind: usize,
    },
    Auto {
        slot: usize,
    },
    Wave {
        group: usize,
        band: usize,
    },
    Gem {
        gem: usize,
        value: f64,
    },
    LevelDue {
        i: usize,
    },
    LevelUp {
        level: Option<usize>,
    },
    OrbApply {
        item: UpgradeItem,
    },
    ChickenHeal,
    LevelWait {
        level: usize,
    },
    AccentOnset {
        i: usize,
    },
    ReactiveBeat {
        bi: usize,
    },
    Settle(Box<PendingSettle>),
    Drain {
        bi: usize,
        b: f64,
    },
}

impl Ev {
    /// `ORDER`: event order at equal times.
    fn order(&self) -> f64 {
        match self {
            Ev::HitstopEnd => 0.0,
            Ev::LevelDue { .. } => 2.0,
            Ev::ReactiveBeat { .. } => 2.4,
            Ev::LevelUp { .. } => 3.0,
            Ev::OrbApply { .. } => 3.5,
            Ev::LevelWait { .. } => 4.5,
            Ev::AccentOnset { .. } => 4.6,
            Ev::Key(_) => 6.0,
            Ev::Settle(_) => 6.1,
            Ev::Held => 6.5,
            Ev::Auto { .. } => 10.0,
            Ev::Wind(_) => 11.0,
            Ev::Strike(_) => 12.0,
            Ev::Wave { .. } => 15.0,
            Ev::Fire { .. } => 16.0,
            Ev::Chain { .. } => 17.0,
            Ev::Gem { .. } | Ev::ChickenHeal => 20.0,
            Ev::Beat { .. } | Ev::Drain { .. } => 21.0,
            Ev::Appear { .. } => 22.0,
        }
    }
}

/// A press waiting to settle, as the settle step and the record see it.
#[derive(Clone, Debug)]
struct PendingSettle {
    press: SettlePress,
    provisional: String,
    provisional_change: f64,
    steady: bool,
}

#[derive(Clone, Debug)]
struct Entry {
    key: QKey,
    ev: Ev,
}

#[derive(Clone, Debug, Default)]
struct Queue {
    heap: Vec<Entry>,
    seq: u64,
}

impl Queue {
    fn push(&mut self, s: f64, ev: Ev) {
        let key = QKey {
            s,
            o: ev.order(),
            phase: phase::RUNTIME,
            index: self.seq,
        };
        self.seq += 1;
        self.insert(Entry { key, ev });
    }

    /// A place in push order, for an event whose time is known only later (`push_reserved`).
    fn reserve(&mut self) -> u64 {
        self.seq += 1;
        self.seq - 1
    }

    /// An event at the place in push order reserved for it.
    fn push_reserved(&mut self, s: f64, ev: Ev, seq: u64) {
        let key = QKey {
            s,
            o: ev.order(),
            phase: phase::RUNTIME,
            index: seq,
        };
        self.insert(Entry { key, ev });
    }

    /// An event the TS queue pushed at init: its phase and index there.
    fn push_init(&mut self, s: f64, ev: Ev, phase: u16, index: u64) {
        let key = QKey {
            s,
            o: ev.order(),
            phase,
            index,
        };
        self.insert(Entry { key, ev });
    }

    fn insert(&mut self, entry: Entry) {
        let h = &mut self.heap;
        h.push(entry);
        let mut c = h.len() - 1;
        while c > 0 {
            let p = (c - 1) >> 1;
            if !h[c].key.less(&h[p].key) {
                break;
            }
            h.swap(c, p);
            c = p;
        }
    }

    fn peek(&self) -> f64 {
        self.heap.first().map_or(f64::INFINITY, |e| e.key.s)
    }

    fn pop(&mut self) -> Entry {
        let h = &mut self.heap;
        let last = h.pop().expect("a queued event");
        if h.is_empty() {
            return last;
        }
        let top = std::mem::replace(&mut h[0], last);
        let mut c = 0;
        loop {
            let l = 2 * c + 1;
            let r = l + 1;
            let mut m = c;
            if l < h.len() && h[l].key.less(&h[m].key) {
                m = l;
            }
            if r < h.len() && h[r].key.less(&h[m].key) {
                m = r;
            }
            if m == c {
                break;
            }
            h.swap(m, c);
            c = m;
        }
        top
    }

    /// The next of the events due by `limit`: they all happen at the clock's moment, so they go in
    /// kind and push order, however their times differ by a hair.
    fn pop_due(&mut self, limit: f64) -> Entry {
        let first = self.pop();
        if self.peek() > limit {
            return first;
        }
        let mut due = vec![first];
        while self.peek() <= limit {
            due.push(self.pop());
        }
        let mut best = 0;
        for i in 1..due.len() {
            let (a, b) = (&due[i].key, &due[best].key);
            if a.o < b.o || (a.o == b.o && a.rank_less(b)) {
                best = i;
            }
        }
        let chosen = due.swap_remove(best);
        // (swap_remove moved the last into `best`; reinsertion order does not matter: keys are unique.)
        for e in due {
            self.insert(e);
        }
        chosen
    }
}

// ------------------------------------------------------------------ state

/// A beat step: from world time w0, easing out over dur, (dx, dy) plus the separation push over span.
#[derive(Clone, Copy, Debug, PartialEq)]
struct FoeStep {
    w0: f64,
    dur: f64,
    dx: f64,
    dy: f64,
    span: f64,
}

/// Per-enemy simulation state (`Foe`); its id is its index in the state's arena.
#[derive(Clone, Debug)]
struct Foe {
    id: usize,
    /// The enemy's index in the library's enemies.
    def: usize,
    r: f64,
    health: f64,
    x: f64,
    y: f64,
    vx: f64,
    vy: f64,
    last_w: f64,
    alive: bool,
    final_sample: bool,
    death_w: f64,
    last_fate: Option<crate::recorder::Fate>,
    formation: Option<usize>,
    sx: f64,
    sy: f64,
    knock_x: f64,
    knock_y: f64,
    knock_t: f64,
    weight: f64,
    charge_until: f64,
    land_s: Option<f64>,
    step_k: Option<f64>,
    step: Option<FoeStep>,
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct PendingCleave {
    gi: f64,
    s: f64,
    x: f64,
    y: f64,
    dir: f64,
    tier: f64,
}

#[derive(Clone, Debug)]
#[allow(clippy::struct_excessive_bools)]
struct Swing {
    s_armed: f64,
    s_wind: f64,
    s_strike: f64,
    heavy: bool,
    anim: String,
    impact: f64,
    frames: f64,
    tier: f64,
    accent: f64,
    dir: f64,
    aimed: bool,
    struck: bool,
    w_strike: f64,
    undecided: bool,
    fps: f64,
    record: Option<crate::recorder::PlanSwing>,
}

#[derive(Clone, Debug)]
struct WaveState {
    x: f64,
    y: f64,
    dir: f64,
    group: usize,
    mult: f64,
    inner: f64,
    half_angle: f64,
    to: f64,
    hit: HashSet<usize>,
}

#[derive(Clone, Debug)]
struct ChainState {
    last: ChainPoint,
    index: usize,
    hit: HashSet<usize>,
    jumps: f64,
    energy: f64,
    bi: f64,
    range: Option<f64>,
    mult: f64,
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct Punch {
    s: f64,
    amt: f64,
    w: Option<f64>,
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct FieldGem {
    gem: usize,
    x: f64,
    y: f64,
    value: f64,
    big: bool,
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct Spot {
    i: usize,
    x: f64,
    y: f64,
}

#[derive(Clone, Debug)]
struct Orbs {
    u: usize,
    offers: Vec<UpgradeItem>,
    pts: Vec<(f64, f64)>,
    deadline_beat: f64,
}

#[derive(Clone, Debug)]
struct FireSpec {
    weapon: usize,
    every: crate::content::types::Subdivision,
    skip: Option<crate::content::types::Subdivision>,
}

/// An event TS queued at a beat time it read ahead of the beats known: it is queued, at the place in
/// push order it would have had, once the beats that time needs have arrived (every such time falls
/// at or after the beat before it, and each beat is known by then).
#[derive(Clone, Debug)]
struct Deferred {
    /// The fractional beat index whose song time the event is at.
    bi: f64,
    seq: u64,
    then: Then,
}

/// What a deferred beat time is for.
#[derive(Clone, Debug)]
enum Then {
    /// Queue the event at the beat's time.
    Push(Ev),
    /// An upgrade orb taken: record it applying at the beat's time (the first grid time at least
    /// NEVER_LATE after it was taken), and queue its application there.
    OrbTaken {
        u: usize,
        k: usize,
        s: f64,
        w: f64,
        walked: bool,
        item: UpgradeItem,
    },
    /// A cleave launched: record it, its first band at the beat's time.
    Cleave {
        s: f64,
        press: f64,
        launch: f64,
        onset: bool,
    },
}

/// How many entries of each stream group the simulation has taken in (snapshot state).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
struct Ingested {
    beats: usize,
    ticks: usize,
    onsets: usize,
    hindsight: usize,
    forecasts: usize,
}

/// Everything a simulation carries from one moment to the next (`SimState`): a snapshot is a clone.
#[derive(Clone, Debug)]
#[allow(clippy::struct_excessive_bools)]
struct SimState {
    s: f64,
    w: f64,
    rate_s: f64,
    q: Queue,
    breaks: Vec<WarpBreak>,
    stop_start: f64,
    stop_end: f64,
    rest_pose: (f64, f64),
    step: Option<Step>,
    strides: StrideState,
    /// The keys held, in the order they were pressed.
    held: Vec<Key>,
    walked: f64,
    last_sample: Option<(f64, f64)>,
    landed: bool,
    facing_angle: f64,
    groove: f64,
    last_press_s: f64,
    memo: PressMemo,
    tier: f64,
    last_raise_s: f64,
    pending_cleave: Option<PendingCleave>,
    onset_cleave: Option<(PendingCleave, f64)>,
    last_great: Option<(f64, bool)>,
    swing_pool: Vec<Swing>,
    swings: Vec<usize>,
    live_swings: Vec<usize>,
    taken_slots: HashSet<usize>,
    light_count: usize,
    n_inputs: usize,
    n_hits: usize,
    n_gems: usize,
    waves: Vec<WaveState>,
    chains: Vec<ChainState>,
    field: Vec<FieldGem>,
    vacuums: Vec<Spot>,
    loadout: Loadout,
    bonus: PassiveBonus,
    orbs: Option<Orbs>,
    fire_specs: Vec<FireSpec>,
    settler: Settler,
    prov_memo: PressMemo,
    press_log: Vec<(f64, bool)>,
    settle_run: f64,
    steady_run: f64,
    applying: Vec<UpgradeItem>,
    invuln_until: f64,
    chickens: Vec<Spot>,
    n_chickens: usize,
    punches: Vec<Punch>,
    alive: Vec<usize>,
    pending: f64,
    foes: Vec<Foe>,
    foe_samples: Vec<usize>,
    blasting: bool,
    health: f64,
    boss_foe: Option<usize>,
    xp_now: f64,
    level: usize,
    overdue: bool,
    section: usize,
    level_ready: bool,
    heading: (f64, f64),
    hero_x: Vec<f64>,
    hero_y: Vec<f64>,
    hero_base: usize,
    camera: CameraRig,
    next_hero_k: f64,
    next_foe_k: f64,
    dip_start: f64,
    head: Vec<i32>,
    stamp: Vec<i32>,
    next_arr: Vec<i32>,
    generation: i32,
    fixed_k: f64,
    last_fixed_w: f64,
    recycle_k: u64,
    // The stream as taken in, and what grows from it.
    ingested: Ingested,
    song: SongMap,
    idx: SongIndex,
    sch: ReactiveSchedule,
    judging: Judging,
    /// The strong onsets taken in (indices into the log's onsets), and the confident forecasts.
    strong_onsets: Vec<usize>,
    forecasts: Vec<usize>,
    /// Stride steps whose held strides are queued (the last grid time's are provisional).
    held_queued: usize,
    /// Events waiting for the beats their times need.
    deferred: Vec<Deferred>,
    /// The latest stomp's cancelled held strides: up to the stride step at this index (its time read
    /// when a held stride asks, since TS read it from the whole stream's steps), or a time.
    stomp_target: Option<StompTarget>,
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum StompTarget {
    Index(usize),
    Time(f64),
}

/// A rollback snapshot: the simulation's whole state (`Sim::snapshot`).
#[derive(Clone, Debug)]
pub struct Snapshot {
    st: SimState,
    finished: bool,
}

/// A steppable simulation (`Sim`). `advance_to` runs the game rules up to a song time over the
/// stream as known; `key` applies a key event at its exact song time; `snapshot` and `restore`
/// roll it back.
pub struct Sim {
    env: Rc<Env>,
    st: SimState,
    /// The stream as arrived (in Replay, the whole stream): the state's cursors say how much of it
    /// the simulation has taken in.
    log: Rc<StreamLog>,
    pub rec: Box<dyn Recorder>,
    finished: bool,
}

impl Sim {
    /// A fresh simulation at song time 0, its song-owned events queued. `until_s` stops it early.
    pub fn new(
        input: SimInput,
        log: Rc<StreamLog>,
        rec: Box<dyn Recorder>,
        until_s: Option<f64>,
    ) -> Sim {
        assert!(input.level.reactive, "the native sim runs reactive levels");
        let director = input
            .level
            .director
            .clone()
            .expect("a reactive level has a director");
        // (Reactive mode: the run plays on until the finale's epilogue has ended (B6).)
        let end_s = min(until_s.unwrap_or(f64::INFINITY), input.duration() + 1.0);
        let rule = stride_rule(&input.lib.hero);
        let env = Rc::new(Env {
            input,
            end_s,
            cut_s: f64::INFINITY,
            rule,
            director,
        });
        let st = fresh_state(&env);
        let mut sim = Sim {
            env,
            st,
            log,
            rec,
            finished: false,
        };
        sim.init();
        sim
    }

    pub fn s(&self) -> f64 {
        self.st.s
    }

    pub fn w(&self) -> f64 {
        self.st.w
    }

    pub fn end_s(&self) -> f64 {
        self.env.end_s
    }

    pub fn done(&self) -> bool {
        self.st.s >= self.env.end_s
    }

    /// A copy of the simulation's state for rollback (the stream and the recorder are not part of it).
    pub fn snapshot(&self) -> Snapshot {
        Snapshot {
            st: self.st.clone(),
            finished: self.finished,
        }
    }

    /// Goes back to a snapshot this simulation took.
    pub fn restore(&mut self, snap: &Snapshot) {
        self.st = snap.st.clone();
        self.finished = snap.finished;
    }

    /// Applies a key event at its exact song time (at or after the simulation's time).
    pub fn key(&mut self, e: KeyEvent) {
        assert!(
            e.s >= self.st.s - 1e-12,
            "key event at s={} is before the simulation's time {}",
            e.s,
            self.st.s
        );
        if e.s <= self.env.cut_s + 1e-12 && e.s < self.env.end_s {
            self.st.q.push(e.s, Ev::Key(e));
        }
    }

    /// The stream, for a live source to append its arrivals to.
    pub fn log_mut(&mut self) -> &mut StreamLog {
        Rc::make_mut(&mut self.log)
    }

    /// Runs the game rules up to song time t: every event before t happens, and the clock moves to t.
    pub fn advance_to(&mut self, t: f64) {
        if t >= self.env.end_s {
            self.run_to_end();
            return;
        }
        while self.st.s < self.env.end_s && self.next_stop() < t - CLOCK_EPS {
            self.step();
        }
        if t - self.st.s > CLOCK_EPS && self.next_stop() - t > CLOCK_EPS {
            self.move_clock(t, false, true);
        }
    }

    /// Runs to the end.
    pub fn run_to_end(&mut self) {
        while self.st.s < self.env.end_s {
            self.step();
        }
        if !self.finished {
            self.finished = true;
            self.finish();
        }
    }

    // -------------------------------------------------------------- ingestion

    /// Takes in every entry known by song time `t` (within the readers' 1e-9) that has not been yet.
    fn ingest(&mut self, t: f64) {
        let env = Rc::clone(&self.env);
        let log = Rc::clone(&self.log);
        let log = &*log;
        let t = t + 1e-9;
        let st = &mut self.st;
        let n_ticks = log
            .ticks
            .iter()
            .skip(st.ingested.ticks)
            .take_while(|x| x.known_at <= t)
            .count();
        if n_ticks > 0 {
            st.ingested.ticks += n_ticks;
            st.judging.extend(&log.ticks[..st.ingested.ticks]);
        }
        let n_hind = log.hindsight.as_ref().map_or(0, |h| {
            h.iter()
                .skip(st.ingested.hindsight)
                .take_while(|x| x.known_at <= t)
                .count()
        });
        st.ingested.hindsight += n_hind;
        if let Some(fc) = &log.forecasts {
            while st.ingested.forecasts < fc.len() && fc[st.ingested.forecasts].known_at <= t {
                if fc[st.ingested.forecasts].confidence >= FORECAST_CONFIDENCE - 1e-9 {
                    st.forecasts.push(st.ingested.forecasts);
                }
                st.ingested.forecasts += 1;
            }
        }
        while st.ingested.onsets < log.onsets.len() && log.onsets[st.ingested.onsets].known_at <= t
        {
            let o = log.onsets[st.ingested.onsets];
            if o.strong {
                let i = st.strong_onsets.len();
                st.strong_onsets.push(st.ingested.onsets);
                st.q.push_init(o.known_at, Ev::AccentOnset { i }, phase::ONSET, i as u64);
            }
            st.ingested.onsets += 1;
        }
        let from = st.ingested.beats;
        let n_beats = log
            .beats
            .iter()
            .skip(from)
            .take_while(|b| b.known_at <= t)
            .count();
        if n_beats == 0 {
            return;
        }
        st.ingested.beats += n_beats;
        st.sch.grow_to(log, st.ingested.beats);
        st.song = st.sch.song.clone();
        st.idx = index_song(&st.song);
        let hero = &env.input.lib.hero;
        for bi in from..st.ingested.beats {
            let b = log.beats[bi];
            // (Every beat is known by its predecessor's time, the analyzer's commit rule: the
            // streaming queue relies on it.)
            assert!(
                b.t >= st.s - 1e-9,
                "beat {bi} at {} arrived after the simulation passed it ({})",
                b.t,
                st.s
            );
            let sl = &st.sch.grid_slots[bi];
            // The sword swings by itself on every slot, never before the slot's beat is known.
            let windup = sl.s
                - hero.swings.windup_ms / 1000.0 / max(0.05, step_rate(&st.sch.base_steps, sl.s));
            let at = max(max(0.0, windup), b.known_at);
            st.q.push_init(at, Ev::Auto { slot: bi }, phase::AUTO, bi as u64);
            st.q.push_init(b.t, Ev::Beat { bi }, phase::BEAT, bi as u64);
            st.q.push_init(
                b.t,
                Ev::ReactiveBeat { bi },
                phase::REACTIVE_BEAT,
                bi as u64,
            );
        }
        // A held key strides on every step of the stride period: the last grid time's steps wait for
        // the next beat, or the stream's end.
        let whole = env.input.end.is_some_and(|e| st.ingested.beats >= e.beats);
        let upto = if whole {
            st.sch.stride_steps.s.len()
        } else {
            st.sch.stride_steps.tail()
        };
        for k in st.held_queued..upto {
            st.q.push_init(st.sch.stride_steps.s[k], Ev::Held, phase::HELD, k as u64);
        }
        st.held_queued = st.held_queued.max(upto);
        self.release_deferred();
    }

    /// Whether the song map as grown gives fractional beat index bi the time the whole stream's
    /// does: it holds that beat (at a whole index) or the beat after it, or every beat.
    fn beat_time_known(&self, bi: f64) -> bool {
        let n = self.st.song.beats.len() as f64;
        let whole = self
            .env
            .input
            .end
            .is_some_and(|e| self.st.ingested.beats >= e.beats);
        whole
            || n >= if bi.fract() == 0.0 {
                bi + 1.0
            } else {
                floor(bi) + 2.0
            }
    }

    /// Queues an event at the song time of beat index bi as the whole stream gives it: now when the
    /// beats are known, else when they arrive, at the place in push order it takes now.
    fn push_at_beat(&mut self, bi: f64, ev: Ev) {
        self.defer_at_beat(bi, Then::Push(ev));
    }

    /// Does `then` at the song time of beat index bi as the whole stream gives it, now or once known.
    fn defer_at_beat(&mut self, bi: f64, then: Then) {
        let seq = self.st.q.reserve();
        if self.beat_time_known(bi) {
            let t = crate::core::songmap::time_of_beat_index(&self.st.song, bi);
            self.run_then(t, seq, then);
        } else {
            self.st.deferred.push(Deferred { bi, seq, then });
        }
    }

    fn run_then(&mut self, t: f64, seq: u64, then: Then) {
        match then {
            Then::Push(ev) => self.st.q.push_reserved(t, ev, seq),
            Then::OrbTaken {
                u,
                k,
                s,
                w,
                walked,
                item,
            } => {
                self.rec.upgrade_taken(u, k, s, w, t, walked);
                self.st.q.push_reserved(t, Ev::OrbApply { item }, seq);
            }
            Then::Cleave {
                s,
                press,
                launch,
                onset,
            } => {
                self.rec.wave(crate::recorder::Cleave {
                    s,
                    press,
                    band0: t,
                    launch: Some(launch),
                    onset: onset.then_some(true),
                });
            }
        }
    }

    fn release_deferred(&mut self) {
        let waiting = std::mem::take(&mut self.st.deferred);
        for d in waiting {
            if self.beat_time_known(d.bi) {
                let t = crate::core::songmap::time_of_beat_index(&self.st.song, d.bi);
                self.run_then(t, d.seq, d.then);
            } else {
                self.st.deferred.push(d);
            }
        }
    }

    /// The stream's whole beat count when known (Replay), else unbounded.
    fn total_beats(&self) -> usize {
        self.env.input.end.map_or(usize::MAX, |e| e.beats)
    }

    // -------------------------------------------------------------- time

    fn base_rate(&self, t: f64) -> f64 {
        step_rate(&self.st.sch.base_steps, t)
    }

    fn in_stop(&self, t: f64) -> bool {
        t >= self.st.stop_start - 1e-12 && t < self.st.stop_end - 1e-12
    }

    fn dip_cuts() -> [f64; 10] {
        let d = [0.0, 0.25, 0.5, 0.75, 1.0].map(|k| k * DIP_RAMP);
        let mut out = [0.0; 10];
        for i in 0..5 {
            out[i] = d[i];
            out[5 + i] = DIP_RAMP + DIP_HOLD + d[i];
        }
        out
    }

    fn dip_factor(&self, t: f64) -> f64 {
        let d = t - self.st.dip_start;
        if d < 0.0 || d >= 2.0 * DIP_RAMP + DIP_HOLD - 1e-12 {
            return 1.0;
        }
        if d < DIP_RAMP {
            return 1.0 - (1.0 - DIP_RATE) * ((floor((d / DIP_RAMP) * 4.0 + 1e-9) + 1.0) / 4.0);
        }
        if d < DIP_RAMP + DIP_HOLD {
            return DIP_RATE;
        }
        DIP_RATE
            + (1.0 - DIP_RATE)
                * ((floor(((d - DIP_RAMP - DIP_HOLD) / DIP_RAMP) * 4.0 + 1e-9) + 1.0) / 4.0)
    }

    fn next_dip_cut(&self, t: f64) -> f64 {
        for c in Sim::dip_cuts() {
            if self.st.dip_start + c > t + 1e-12 {
                return self.st.dip_start + c;
            }
        }
        f64::INFINITY
    }

    fn rate_at(&self, t: f64) -> f64 {
        if self.in_stop(t) {
            0.0
        } else {
            round(self.base_rate(t) * self.dip_factor(t) * 1e6) / 1e6
        }
    }

    fn push_break(&mut self, rate: f64) {
        let st = &mut self.st;
        if let Some(last) = st.breaks.last()
            && last.rate == rate
        {
            return;
        }
        let b = WarpBreak {
            s: st.s,
            w: st.w,
            rate,
        };
        if let Some(last) = st.breaks.last_mut()
            && (last.s - st.s).abs() < 1e-12
        {
            *last = b;
            self.rec.warp(b, true);
            return;
        }
        st.breaks.push(b);
        self.rec.warp(b, false);
    }

    /// World time of a song time at or before now, from the warp so far.
    fn world_of(&self, t: f64) -> f64 {
        for b in self.st.breaks.iter().rev() {
            if b.s <= t + 1e-12 {
                return b.w + (t - b.s) * b.rate;
            }
        }
        0.0
    }

    /// Song time of the loop's next stop: the next event, fixed step or rate change.
    fn next_stop_now(&self) -> f64 {
        let t_fixed = self.st.fixed_k / SIM_RATE;
        let t_rate = min(
            min(
                next_step_after(&self.st.sch.base_steps, self.st.s),
                if self.in_stop(self.st.s) {
                    self.st.stop_end
                } else {
                    f64::INFINITY
                },
            ),
            self.next_dip_cut(self.st.s),
        );
        min(min(min(self.st.q.peek(), t_fixed), t_rate), self.env.end_s)
    }

    /// The next stop, with every stream entry known by then taken in first (taking one in may queue
    /// an earlier event).
    fn next_stop(&mut self) -> f64 {
        loop {
            let t = self.next_stop_now();
            let before = self.st.ingested;
            self.ingest(t);
            if self.st.ingested == before || self.next_stop_now() >= t {
                return min(t, self.next_stop_now());
            }
        }
    }

    /// Moves the clock to song time t. World time comes from the warp breaks, and the hero and enemy
    /// samples are taken at their exact times from the state so far.
    fn move_clock(&mut self, t: f64, fixed: bool, between: bool) {
        let rate = self.rate_at(self.st.rate_s);
        self.push_break(rate);
        let b = *self.st.breaks.last().expect("a warp break");
        let w1 = b.w + (t - b.s) * b.rate;
        if w1 > self.st.w {
            while self.st.next_hero_k / HERO_RATE <= w1 + 1e-12 {
                let wk = self.st.next_hero_k / HERO_RATE;
                self.sample_hero(wk, b.s + (wk - b.w) / rate);
                self.st.next_hero_k += 1.0;
            }
            // Step landings, after the samples up to them.
            if let Some(step) = &self.st.step
                && !self.st.landed
                && step.w0 + self.env.rule.duration_s <= w1 + 1e-12
            {
                let wl = step.w0 + self.env.rule.duration_s;
                let end = crate::movement::step_end(step);
                self.rec.landing(crate::recorder::Landing {
                    s: b.s + (wl - b.w) / rate,
                    w: wl,
                    x: end.x,
                    y: end.y,
                    stomp: None,
                });
                self.st.landed = true;
            }
            while self.st.next_foe_k / ENEMY_RATE <= w1 + 1e-12 {
                self.record_foes(self.st.next_foe_k / ENEMY_RATE);
                self.st.next_foe_k += 1.0;
            }
        }
        self.st.s = t;
        self.st.w = w1;
        if !between {
            self.st.rate_s = t;
        }
        if fixed && self.st.w > self.st.last_fixed_w {
            let w = self.st.w;
            self.move_foes(w);
            self.collect_gems();
            self.touch_orbs();
            self.touch_chickens();
            self.st.last_fixed_w = self.st.w;
        }
        self.rec.clock(self.st.s, self.st.w, between);
    }

    // -------------------------------------------------------------- the loop

    /// Song-owned events and the first samples: a fresh simulation's song time 0.
    fn init(&mut self) {
        // The stream's entries known at song time 0.
        self.ingest(0.0);
        for (i, lu) in self.st.sch.level_ups.iter().enumerate() {
            self.st
                .q
                .push_init(lu.s, Ev::LevelDue { i }, phase::LEVEL_DUE, i as u64);
        }
        // The starting loadout fires from the start, without a level-up.
        for id in self.st.loadout.weapons.clone() {
            self.grant_weapon(id.as_str());
        }
        // The first camera and hero samples.
        let r0 = self.rate_at(0.0);
        self.push_break(r0);
        self.sample_hero(0.0, 0.0);
        self.st.next_hero_k = 1.0;
        self.record_foes(0.0);
        self.st.next_foe_k = 1.0;
    }

    /// One turn of the loop: move to the next stop, then run every event due there.
    fn step(&mut self) {
        let t_fixed = self.st.fixed_k / SIM_RATE;
        let t = self.next_stop();
        self.move_clock(t, t == t_fixed, false);
        if t == t_fixed {
            self.st.fixed_k += 1.0;
            self.st.recycle_k += 1;
            if self.st.recycle_k.is_multiple_of(30) {
                self.recycle();
            }
        }
        while self.st.q.peek() <= self.st.s + 1e-12 {
            let Entry { ev, .. } = self.st.q.pop_due(self.st.s + 1e-12);
            self.handle(ev);
        }
    }

    fn handle(&mut self, ev: Ev) {
        match ev {
            Ev::HitstopEnd => {}
            Ev::Key(e) => {
                if e.kind == KeyEventKind::Press {
                    self.press(&e);
                } else {
                    self.release(&e);
                }
            }
            Ev::Held => self.held_stride(),
            Ev::Auto { slot } => {
                self.swing_for(slot, self.st.s);
            }
            Ev::Wind(sw) => {
                if !self.st.swing_pool[sw].aimed {
                    self.do_wind(sw);
                }
            }
            Ev::Strike(sw) => self.do_strike(sw),
            Ev::Fire { weapon, bi } => self.fire(weapon, bi),
            Ev::Chain {
                weapon,
                chain,
                jump,
            } => self.chain_jump(weapon, chain, jump),
            Ev::Beat { bi } => {
                self.spawn_director(bi as f64);
                // (Stage 2: the drain runs 2 s later, as of this beat, on the settled groove's timeline.)
                let s = self.st.s;
                if self.env.input.settling {
                    self.st.q.push(
                        s + crate::reactive::settle::SETTLE_DELAY,
                        Ev::Drain { bi, b: s },
                    );
                } else {
                    self.drain(bi, s);
                }
            }
            Ev::Appear { kind } => self.appear(kind),
            Ev::LevelUp { level } => {
                if level.is_none() || level == Some(self.st.level) {
                    self.grant();
                }
            }
            Ev::ChickenHeal => self.heal(CHICKEN_HEAL, "chicken"),
            Ev::OrbApply { item } => {
                let i = self
                    .st
                    .applying
                    .iter()
                    .position(|x| x.kind == item.kind && x.id == item.id);
                // (JS's splice(-1, 1) removes the last when none matches.)
                match i {
                    Some(i) => {
                        self.st.applying.remove(i);
                    }
                    None => {
                        self.st.applying.pop();
                    }
                }
                self.take_upgrade(&item);
            }
            Ev::LevelDue { i } => self.level_due(i),
            Ev::Gem { gem, value } => self.gem_arrives(gem, value),
            Ev::Wave { group, band } => self.wave_band(group, band),
            Ev::AccentOnset { i } => self.onset_accent(i),
            Ev::LevelWait { level } => {
                // No section started in the director's wait since the experience was reached: the
                // level-up falls due on the next downbeat.
                if level == self.st.level && self.st.level_ready {
                    let s = self.st.s;
                    self.push_at_downbeat(
                        s,
                        Ev::LevelUp {
                            level: Some(self.st.level),
                        },
                    );
                }
            }
            Ev::Settle(p) => self.settle_press(&p),
            Ev::Drain { bi, b } => self.drain(bi, b),
            Ev::ReactiveBeat { bi } => {
                // An upgrade orb's deadline, counted in committed beats; then the weapons' slots of this beat.
                if self
                    .st
                    .orbs
                    .as_ref()
                    .is_some_and(|o| bi as f64 >= o.deadline_beat)
                {
                    self.take_orb(0, false);
                }
                let specs = self.st.fire_specs.clone();
                let s = self.st.s;
                for f in specs {
                    self.fire_beat(f.weapon, f.every, f.skip, bi as f64, s);
                }
            }
        }
    }

    /// Closes the tracks: every enemy's last sample.
    fn finish(&mut self) {
        let w_end = self.st.w;
        while self.st.next_foe_k / ENEMY_RATE <= w_end + 1.0 / ENEMY_RATE {
            self.record_foes(self.st.next_foe_k / ENEMY_RATE);
            self.st.next_foe_k += 1.0;
        }
    }
}

fn fresh_state(env: &Env) -> SimState {
    let level = &env.input.level;
    let sch = ReactiveSchedule::new(level, env.input.duration());
    let song = sch.song.clone();
    let idx = index_song(&song);
    SimState {
        s: 0.0,
        w: 0.0,
        rate_s: 0.0,
        q: Queue::default(),
        breaks: Vec::new(),
        stop_start: -1.0,
        stop_end: -1.0,
        rest_pose: (0.0, 0.0),
        step: None,
        strides: fresh_strides(),
        held: Vec::new(),
        walked: 0.0,
        last_sample: None,
        landed: true,
        facing_angle: 0.0,
        groove: 0.0,
        last_press_s: f64::NEG_INFINITY,
        memo: PressMemo::default(),
        tier: 0.0,
        last_raise_s: f64::NEG_INFINITY,
        pending_cleave: None,
        onset_cleave: None,
        last_great: None,
        swing_pool: Vec::new(),
        swings: Vec::new(),
        live_swings: Vec::new(),
        taken_slots: HashSet::new(),
        light_count: 0,
        n_inputs: 0,
        n_hits: 0,
        n_gems: 0,
        waves: Vec::new(),
        chains: Vec::new(),
        field: Vec::new(),
        vacuums: Vec::new(),
        loadout: starting_loadout(&level.upgrades.loadout),
        bonus: PassiveBonus::default(),
        orbs: None,
        fire_specs: Vec::new(),
        settler: Settler::default(),
        prov_memo: PressMemo::default(),
        press_log: Vec::new(),
        settle_run: f64::NAN,
        steady_run: 0.0,
        applying: Vec::new(),
        invuln_until: f64::NEG_INFINITY,
        chickens: Vec::new(),
        n_chickens: 0,
        punches: Vec::new(),
        alive: Vec::new(),
        pending: 0.0,
        foes: Vec::new(),
        foe_samples: Vec::new(),
        blasting: false,
        health: env.input.lib.hero.health.max,
        boss_foe: None,
        xp_now: 0.0,
        level: 0,
        overdue: false,
        section: 0,
        level_ready: false,
        heading: (1.0, 0.0),
        hero_x: Vec::new(),
        hero_y: Vec::new(),
        hero_base: 0,
        camera: CameraRig::default(),
        next_hero_k: 0.0,
        next_foe_k: 0.0,
        dip_start: f64::NEG_INFINITY,
        head: vec![0; (G * G) as usize],
        stamp: vec![-1; (G * G) as usize],
        next_arr: vec![0; 1024],
        generation: 0,
        fixed_k: 1.0,
        last_fixed_w: 0.0,
        recycle_k: 0,
        ingested: Ingested::default(),
        song,
        idx,
        sch,
        judging: Judging::new(env.input.gate),
        strong_onsets: Vec::new(),
        forecasts: Vec::new(),
        held_queued: 0,
        deferred: Vec::new(),
        stomp_target: None,
    }
}

#[cfg(test)]
mod tests {
    use std::path::Path;
    use std::rc::Rc;

    use stream::{Audio, Beat, Params, StreamEnd, StreamFile, StreamLog, Tick};

    use super::{Ev, Sim, SimInput};
    use crate::content::level::load_level;
    use crate::content::{SourceFile, load_content};
    use crate::recorder::NullRecorder;
    use crate::schedule::{grow_song_map, reactive_song_map};

    fn source(rel: &str) -> SourceFile {
        let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
        SourceFile {
            name: rel.to_string(),
            text: std::fs::read_to_string(root.join(rel)).unwrap(),
        }
    }

    /// A steady stream: a beat every 0.5 s known 0.6 s ahead, except beat 4, known just before beat 3.
    fn stream() -> StreamFile {
        let beats: Vec<Beat> = (0..12)
            .map(|i| {
                let t = 0.5 + 0.5 * f64::from(i);
                let known_at = if i == 4 {
                    t - 0.5 - 1e-3
                } else {
                    (t - 0.6).max(0.0)
                };
                Beat {
                    t,
                    known_at,
                    period: 0.5,
                    evidence: 0.9,
                }
            })
            .collect();
        let ticks: Vec<Tick> = (0..140)
            .map(|j| {
                let t = 0.05 * f64::from(j);
                Tick {
                    t,
                    known_at: t,
                    evidence: 0.9,
                    energy: 0.5,
                    level: None,
                    key: None,
                    bands: None,
                    brightness: None,
                }
            })
            .collect();
        let log = StreamLog {
            beats,
            ticks,
            ..StreamLog::default()
        };
        StreamFile {
            audio: Audio {
                file: "test".into(),
                duration: 7.0,
                sample_rate: 44100.0,
            },
            params: Params {
                gate: 0.5,
                tick_s: 0.05,
                tool_version: 1.0,
                raw: serde_json::Value::Null,
            },
            log,
        }
    }

    fn sim(file: &StreamFile) -> Sim {
        let mut names: Vec<String> =
            std::fs::read_dir(Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../content"))
                .unwrap()
                .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
                .filter(|n| Path::new(n).extension().is_some_and(|x| x == "yaml"))
                .collect();
        names.sort();
        let lib = load_content(
            &names
                .iter()
                .map(|n| source(&format!("content/{n}")))
                .collect::<Vec<_>>(),
        )
        .unwrap();
        let mut song = reactive_song_map(file.audio.duration);
        grow_song_map(&mut song, &file.log, file.log.beats.len());
        let level = load_level(&source("levels/reactive.yaml"), &lib, &song, None).unwrap();
        let end = Some(StreamEnd {
            duration: file.audio.duration,
            beats: file.log.beats.len(),
            ticks: file.log.ticks.len(),
        });
        let input = SimInput::new(
            level,
            &lib,
            &indexmap::IndexMap::new(),
            file.params.gate,
            false,
            end,
        );
        Sim::new(
            input,
            Rc::new(file.log.clone()),
            Box::new(NullRecorder),
            None,
        )
    }

    #[test]
    fn a_downbeat_not_yet_known_waits_for_its_beat() {
        let file = stream();
        let mut sim = sim(&file);
        // Just after beat 2 (1.5 s), the next downbeat is beat 4 (2.5 s), known only at 1.999 s.
        sim.advance_to(1.51);
        assert_eq!(sim.st.ingested.beats, 4, "beats 0-3 known");
        let s = sim.st.s;
        sim.push_at_downbeat(s, Ev::LevelUp { level: None });
        assert_eq!(sim.st.deferred.len(), 1, "the downbeat waits for beat 4");
        sim.advance_to(2.2);
        assert!(sim.st.deferred.is_empty());
        let queued: Vec<f64> = sim
            .st
            .q
            .heap
            .iter()
            .filter(|e| matches!(e.ev, Ev::LevelUp { .. }))
            .map(|e| e.key.s)
            .collect();
        assert_eq!(
            queued,
            vec![2.5],
            "TS's downbeat: beat 4's time, not the time asked at"
        );
    }

    #[test]
    fn past_the_last_bar_a_downbeat_is_the_time_itself() {
        let file = stream();
        let mut sim = sim(&file);
        sim.advance_to(6.6);
        let s = sim.st.s;
        sim.push_at_downbeat(s, Ev::LevelUp { level: None });
        let queued: Vec<f64> = sim
            .st
            .q
            .heap
            .iter()
            .filter(|e| matches!(e.ev, Ev::LevelUp { .. }))
            .map(|e| e.key.s)
            .collect();
        assert_eq!(queued, vec![s]);
    }
}
