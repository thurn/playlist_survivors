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

mod bible;
mod bounds;
mod pool;
mod separation_cache;
pub use bounds::Storage;
use pool::Pool;
mod boss_presentation;
mod chests;
mod combat;
mod contacts;
mod enemy_rhythm;
mod foes;
mod freeze;
mod hero;
mod judge;
mod lightning;
mod missiles;
mod observe;
mod offers;
mod pickups;
mod pieces;
mod plan;
mod spawning;
mod survivor_events;
mod weapons;

use crate::shared_vec::SharedVec;
use std::collections::HashSet;
use std::rc::Rc;

use indexmap::IndexMap;
use stream::StreamLog;

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
/// and enemy overrides, the hero atlas's animations and the stream's gate. (Never the stream's end:
/// the sim runs live.)
#[derive(Clone, Debug)]
#[allow(clippy::struct_excessive_bools)] // (independent switches)
pub struct SimInput {
    pub level: LevelScript,
    pub lib: ContentLibrary,
    pub hero_anims: IndexMap<String, AnimInfo>,
    pub seed: f64,
    /// The stream's evidence gate (`params.gate`).
    pub gate: f64,

    /// Check-only: damage to the hero is off.
    pub no_hero_damage: bool,
    /// Check-only, the causality checks' negative control: the director's population reads the
    /// evidence a second ahead in the whole log, whether it has arrived or not.
    pub leak_future_evidence: bool,
    /// Stage 2: the stream has hindsight entries, so presses settle 2 s late.
    pub settling: bool,
    /// Zen mode, the app's default: nothing is judged. Presses and held keys move the hero only on
    /// the stride steps, the groove stays full, and every accent's cleave launches by itself.
    /// Without it, legacy mode (the TS game's rules, kept for older sessions and the oracle and
    /// stage tests) judges every press against the grid. Legacy mode is not developed further.
    pub zen: bool,
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
    ) -> SimInput {
        let mut lib = content.clone();
        lib.hero = level.hero.clone();
        if level.survivors.is_some() {
            lib.hero.walk.speed = crate::survivors::HERO_SPEED;
            lib.hero.health.max = 120.0;
            lib.hero.health.invulnerable_ms = 120.0;
        }
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
        if level.survivors.is_some() {
            for e in lib.enemies.values_mut() {
                let data = *e
                    .survivors
                    .get_or_insert_with(|| crate::survivors::EnemyStats::for_art(&e.id));
                e.speed = crate::survivors::enemy_speed(data.speed);
                e.contact_damage = data.power;
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
            no_hero_damage: false,
            leak_future_evidence: false,
            settling,
            zen: false,
        }
    }
}

/// What never changes during a simulation: shared by every snapshot.
#[derive(Debug)]
struct Env {
    input: SimInput,
    cut_s: f64,
    rule: StrideRule,
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
    pub const ARCHERS: u16 = 6;
    pub const VOLLEYS: u16 = 7;
    pub const BOSS: u16 = 8;
    pub const FORMATIONS: u16 = 9;
    pub const KICK_VACUUM: u16 = 10;
    pub const RINGS: u16 = 11;
    pub const STAMPEDES: u16 = 12;
    pub const BLAST: u16 = 13;
    pub const LANDMARK: u16 = 22;
    pub const RETREAT: u16 = 23;
    pub const ZONE: u16 = 24;
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
    Freeze(crate::survivors::FreezeReason),
    FreePulse {
        generation: u64,
    },
    EnemyWind {
        peak: f64,
        period: f64,
        generation: Option<u64>,
    },
    MissileEnd(Box<crate::survivors::MissilePose>),
    WandBullet(Box<crate::survivors::Shot>),
    LightningStrike(Box<lightning::PendingLightning>),
    EnemyImpact {
        boss: bool,
        foe: usize,
        fate: crate::recorder::Fate,
        hit: Box<crate::recorder::Hit>,
    },
    Held,
    HitstopEnd,
    Recoil {
        foe: usize,
        multiplier: f64,
    },
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
    ChickenHeal {
        chicken: usize,
    },
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
    ZoneWave {
        zone: usize,
        section: Option<usize>,
    },
    ZoneFallback {
        at: f64,
    },
    Blast {
        band: usize,
    },
    Landmark {
        id: String,
    },
    BossSpawn,
    BossLand,
    BossRetreat,
    SlamCheck {
        i: usize,
    },
    SlamLand {
        k: usize,
    },
    Ring {
        k: usize,
    },
    RingClose {
        k: usize,
    },
    Formation {
        f: usize,
    },
    FormationRelease {
        f: usize,
    },
    DanceStart {
        f: usize,
    },
    Stampede {
        k: usize,
    },
    Archers {
        group: usize,
    },
    DrawCheck {
        group: usize,
        i: usize,
    },
    Release {
        v: usize,
    },
    Arrive {
        v: usize,
    },
    VacuumDrop,
}

impl Ev {
    /// `ORDER`: event order at equal times.
    fn order(&self) -> f64 {
        match self {
            Ev::HitstopEnd => 0.0,
            Ev::EnemyWind { .. } | Ev::Wind(_) => 11.0,
            Ev::EnemyImpact { .. } | Ev::MissileEnd(_) | Ev::Appear { .. } => 22.0,
            Ev::Freeze(_) => 0.5,
            Ev::FreePulse { .. } => 6.6,
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
            Ev::Strike(_) => 12.0,
            Ev::Wave { .. } => 15.0,
            Ev::Fire { .. } | Ev::WandBullet(_) | Ev::LightningStrike(_) => 16.0,
            Ev::Chain { .. } => 17.0,
            Ev::Gem { .. } | Ev::ChickenHeal { .. } => 20.0,
            Ev::Beat { .. } | Ev::Drain { .. } => 21.0,
            Ev::ZoneWave { .. }
            | Ev::ZoneFallback { .. }
            | Ev::Landmark { .. }
            | Ev::BossSpawn
            | Ev::Ring { .. }
            | Ev::Formation { .. }
            | Ev::Stampede { .. }
            | Ev::Archers { .. }
            | Ev::VacuumDrop => 4.0,
            Ev::RingClose { .. } => 4.7,
            Ev::DanceStart { .. } => 4.75,
            Ev::BossRetreat => 4.8,
            Ev::DrawCheck { .. } => 7.0,
            Ev::Release { .. } | Ev::SlamCheck { .. } => 8.0,
            Ev::BossLand => 13.0,
            Ev::FormationRelease { .. } | Ev::Recoil { .. } => 14.0,
            Ev::Blast { .. } => 18.0,
            Ev::Arrive { .. } | Ev::SlamLand { .. } => 19.0,
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

    /// Drops every event that matches (the order among the rest rests on their keys).
    fn remove(&mut self, drop: &dyn Fn(&Ev) -> bool) {
        let kept: Vec<Entry> = std::mem::take(&mut self.heap)
            .into_iter()
            .filter(|e| !drop(&e.ev))
            .collect();
        for e in kept {
            self.insert(e);
        }
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
        // Exact-time batches are already in kind/sequence order in the heap.
        // Only drain/reinsert when tolerance admits a genuinely earlier kind or
        // sequence at a slightly later time. In particular, hundreds of reactions
        // on one grid point must not rebuild their entire batch for each event.
        let earlier_due = self.heap.iter().any(|e| {
            e.key.s <= limit
                && (e.key.o < first.key.o
                    || (e.key.o == first.key.o && e.key.rank_less(&first.key)))
        });
        if !earlier_due {
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
#[allow(clippy::struct_excessive_bools)]
struct Foe {
    id: usize,
    /// The enemy's index in the library's enemies.
    def: usize,
    r: f64,
    health: f64,
    survivor_boss: bool,
    swarm_dir: Option<(f64, f64)>,
    chest_loot: Option<crate::survivors::ChestLoot>,
    attack: Option<enemy_rhythm::Lunge>,
    x: f64,
    y: f64,
    vx: f64,
    vy: f64,
    last_w: f64,
    alive: bool,
    final_sample: bool,
    death_w: f64,
    last_fate: Option<crate::recorder::Fate>,
    sx: f64,
    sy: f64,
    knock_x: f64,
    knock_y: f64,
    knock_t: f64,
    recoil_until: f64,
    recoil_v: (f64, f64),
    /// Mutable VS susceptibility; Garlic can increase it on a surviving hit.
    knockback: f64,
    weight: f64,
    charge_until: f64,
    land_s: Option<f64>,
    step_k: Option<f64>,
    step: Option<FoeStep>,
    health0: f64,
    /// A set piece's member: a formation's, a ring's or a stampede's.
    member: Option<Member>,
    /// A formation member's slot, followed until `slot_until`.
    slot: Option<FoeSlot>,
    slot_until: f64,
    /// A ring member (pinned while its ring holds), and its path on the ring's circle.
    has_at: bool,
    ring_path: Option<RingPath>,
    /// A stampede's runner (or the retreating boss): its fixed velocity.
    run: Option<(f64, f64)>,
    retreat: bool,
    archer_group: Option<usize>,
    archer_slot: Option<usize>,
    /// An archer drawing since this world time.
    drawing: Option<f64>,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Member {
    Formation(usize),
    Ring(usize),
    Stampede(usize),
    SurvivorEvent(usize),
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum SlotPlace {
    KickLine { d: f64 },
    Wedge { arm: f64, side: f64, spacing: f64 },
}

/// A formation member's slot: an offset from the formation's anchor by song time.
#[derive(Clone, Copy, Debug, PartialEq)]
struct FoeSlot {
    place: SlotPlace,
    dir: (f64, f64),
    start: (f64, f64),
    b_start: f64,
    entrance_beats: f64,
    steps: f64,
}

/// A ring member on its circle about (x, y), from radius `from` to `to`, `u` of the way.
#[derive(Clone, Copy, Debug, PartialEq)]
struct RingPath {
    x: f64,
    y: f64,
    c: f64,
    sn: f64,
    from: f64,
    to: f64,
    u: f64,
    du: f64,
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct FormationDir {
    x: f64,
    y: f64,
    until: f64,
    fi: usize,
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct Flying {
    v: usize,
    index: usize,
    x0: f64,
    y0: f64,
    ax: f64,
    ay: f64,
    w_release: f64,
    ux: f64,
    uy: f64,
    damage: f64,
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
    /// A later beat the event reads: it waits for that beat too, and fires no earlier than its knownAt.
    need: Option<usize>,
    slot: Slot,
    then: Then,
}

/// A deferred event's place in the queue: in push order, or an init phase and index.
#[derive(Clone, Copy, Debug)]
enum Slot {
    Runtime(u64),
    Init(u16, u64),
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
    /// Unwarped elapsed run time, independent of song boundaries (SF3 freezes it).
    run_s: f64,
    freeze: freeze::Freeze,
    survivor: Option<crate::survivors::Progress>,
    spawner: crate::survivors::Spawner,
    boss_presentation: crate::survivors::BossPresentation,
    events: survivor_events::Events,
    chests: Vec<crate::survivors::FieldChest>,
    n_chests: usize,
    treasure_minutes: u32,
    roulette: Option<crate::survivors::Roulette>,
    garlic: Option<weapons::GarlicAura>,
    last_bloody_heal: f64,
    soul_recovered: f64,
    missiles: Vec<missiles::Missile>,
    bibles: Vec<bible::BibleCycle>,
    next_missile: usize,
    next_lightning_volley: usize,
    next_lightning_strike: usize,
    w: f64,
    rate_s: f64,
    q: Queue,
    breaks: SharedVec<WarpBreak>,
    stop_start: f64,
    stop_end: f64,
    rest_pose: (f64, f64),
    step: Option<Step>,
    strides: StrideState,
    /// The keys held, in the order they were pressed.
    held: Vec<Key>,
    /// Zen mode: the direction keys pressed since the last held stride (a tap released before the
    /// next stride step still strides on it), and a stomp waiting for the next stride step.
    zen_taps: Vec<Key>,
    zen_stomp: bool,
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
    swing_pool: Pool<Swing>,
    swings: Vec<usize>,
    live_swings: Vec<usize>,
    taken_slots: HashSet<usize>,
    light_count: usize,
    n_inputs: usize,
    n_hits: usize,
    n_gems: usize,
    waves: Pool<WaveState>,
    chains: Pool<ChainState>,
    field: Vec<FieldGem>,
    flying_gems: Vec<pickups::FlyingGem>,
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
    foes: Pool<Foe>,
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
    fixed_k: f64,
    last_fixed_w: f64,
    recycle_k: u64,
    // The stream as taken in, and what grows from it.
    ingested: Ingested,
    song: SongMap,
    idx: Rc<SongIndex>,
    sch: ReactiveSchedule,
    judging: Judging,
    /// The strong onsets taken in (indices into the log's onsets), and the confident forecasts.
    strong_onsets: SharedVec<usize>,
    forecasts: SharedVec<usize>,
    /// Stride steps whose held strides are queued (the last grid time's are provisional).
    held_queued: usize,
    /// Events waiting for the beats their times need.
    deferred: Vec<Deferred>,
    /// The latest stomp's cancelled held strides: up to the stride step at this index (its time read
    /// when a held stride asks, since TS read it from the whole stream's steps), or a time.
    stomp_target: Option<StompTarget>,
    /// The ingestion horizon: every entry known by then has been taken in.
    horizon: f64,
    /// The run's end (unbounded live until a confirmed end's finale), and the caller's stop.
    end_s: f64,
    until_s: f64,
    /// Set pieces: the planner, and the pieces in play.
    plan: plan::Plan,
    swell_steps: Option<Vec<crate::schedule::RateStep>>,
    swelling: bool,
    formation_dir: Option<FormationDir>,
    formation_members: Vec<Vec<usize>>,
    rings: Vec<pieces::RingState>,
    ring_members: Vec<(usize, f64, Vec<usize>)>,
    stampedes: Vec<(usize, bool)>,
    n_arrows: usize,
    flying: Vec<Flying>,
    slam_marks: Vec<(usize, f64, f64, f64, f64)>,
    n_vacuums: usize,
    /// Set pieces in the order they entered (their ids in the recording).
    piece_ids: Vec<Member>,
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

/// Rebuilt separation bins; no game rule reads their contents across physics passes.
struct Spatial {
    head: Vec<i32>,
    stamp: Vec<i32>,
    next_arr: Vec<i32>,
    generation: i32,
    cache: separation_cache::Cache,
}
impl Default for Spatial {
    fn default() -> Self {
        Self {
            head: vec![0; (G * G) as usize],
            stamp: vec![0; (G * G) as usize],
            next_arr: Vec::new(),
            generation: 0,
            cache: separation_cache::Cache::default(),
        }
    }
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
    spatial: Spatial,
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
        // The run plays on until the finale's epilogue has ended (a confirmed end's), or `until_s`.
        let until_s = until_s.unwrap_or(f64::INFINITY);
        let end_s = until_s;
        let rule = stride_rule(&input.lib.hero);
        let env = Rc::new(Env {
            input,
            cut_s: f64::INFINITY,
            rule,
            director,
        });
        let mut st = fresh_state(&env);
        st.end_s = end_s;
        st.until_s = until_s;
        let mut sim = Sim {
            env,
            st,
            log,
            rec,
            spatial: Spatial::default(),
            finished: false,
        };
        sim.init();
        sim
    }

    pub fn s(&self) -> f64 {
        self.st.s
    }

    /// Static check configuration, set before the first clock step. Snapshots keep the same
    /// environment, so rollback cannot accidentally re-enable damage in a spawn-only smoke.
    pub fn disable_hero_damage_for_check(&mut self) {
        assert!(self.st.s == 0.0, "check configuration must precede the run");
        Rc::get_mut(&mut self.env)
            .expect("fresh simulation environment")
            .input
            .no_hero_damage = true;
    }

    pub fn run_s(&self) -> f64 {
        self.st.run_s
    }

    pub fn w(&self) -> f64 {
        self.st.w
    }

    pub fn end_s(&self) -> f64 {
        self.st.end_s
    }

    pub fn done(&self) -> bool {
        self.st.s >= self.st.end_s
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
        if e.s <= self.env.cut_s + 1e-12 {
            self.st.q.push(e.s, Ev::Key(e));
        }
    }

    /// The stream, for a live source to append its arrivals to.
    pub fn log_mut(&mut self) -> &mut StreamLog {
        Rc::make_mut(&mut self.log)
    }

    /// Runs the game rules up to song time t: every event before t happens, and the clock moves to t.
    pub fn advance_to(&mut self, t: f64) {
        if t >= self.st.end_s {
            self.run_to_end();
            return;
        }
        while self.st.s < self.st.end_s && self.next_stop() < t - CLOCK_EPS {
            self.step();
        }
        // (The end may have been decided on the way: never past it.)
        if t >= self.st.end_s {
            self.run_to_end();
            return;
        }
        if t - self.st.s > CLOCK_EPS && self.next_stop() - t > CLOCK_EPS {
            self.move_clock(t, false, true);
        }
    }

    /// Runs to the end.
    pub fn run_to_end(&mut self) {
        while self.st.s < self.st.end_s {
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
        self.ingest_entries(t);
        self.st.horizon = max(self.st.horizon, t + 1e-9);
        // What the entries taken in decide: sections, zone waves, set pieces, the finale.
        self.plan_freeze();
        if self.st.freeze.frozen
            && (self.st.freeze.resume.is_none()
                || self.st.freeze.resume_grid == freeze::ResumeGrid::Free)
            && self.st.freeze.reasons_empty()
        {
            self.request_resume();
        }
        self.requeue_held();
        self.plan();
        self.prepare_weapons();
    }

    fn ingest_entries(&mut self, t: f64) {
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
        st.idx = Rc::new(index_song(&st.song));
        if env.input.level.survivors.is_some() {
            for bi in from..st.ingested.beats {
                self.queue_beat(bi);
            }
            self.arm_free_pulse();
        } else {
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
                    - hero.swings.windup_ms
                        / 1000.0
                        / max(0.05, step_rate(&st.sch.base_steps, sl.s));
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
        }
        // A held key strides on every step of the stride period: the last grid time's steps wait for
        // the next beat.
        let st = &mut self.st;
        let upto = st.sch.stride_steps.tail();
        if !st.freeze.frozen {
            for k in st.held_queued..upto {
                let at = st.sch.stride_steps.s[k];
                // A newly bounded interval can start before its second beat is known.
                // Free-pulse strides already covered that time; never replay a missed stride.
                if env.input.level.survivors.is_none() || at >= st.s - 1e-9 {
                    st.q.push_init(at, Ev::Held, phase::HELD, k as u64);
                }
            }
        }
        st.held_queued = st.held_queued.max(upto);
        self.release_deferred();
    }

    /// Whether the song map as grown gives fractional beat index bi its final time: it holds that
    /// beat (at a whole index) or the beat after it.
    fn beat_time_known(&self, bi: f64) -> bool {
        let n = self.st.song.beats.len() as f64;
        n >= if bi.fract() == 0.0 {
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
        let slot = Slot::Runtime(self.st.q.reserve());
        self.defer_slot(bi, slot, then);
    }

    /// Queues an event at a beat time with an init key, now or once the beat is known.
    fn push_init_at_beat(&mut self, bi: f64, ev: Ev, phase: u16, index: u64) {
        self.defer_slot(bi, Slot::Init(phase, index), Then::Push(ev));
    }

    /// Queues an event with an init key at beat bi's time, or at beat `need`'s knownAt if that is
    /// later, once both are known: a decision there that reads beat `need`.
    fn push_init_at_beat_needing(&mut self, bi: f64, need: usize, ev: Ev, phase: u16, index: u64) {
        let d = Deferred {
            bi,
            need: Some(need),
            slot: Slot::Init(phase, index),
            then: Then::Push(ev),
        };
        match self.deferred_time(&d) {
            Some(t) => self.run_then(t, d.slot, d.then),
            None => self.st.deferred.push(d),
        }
    }

    /// A deferred event's time, once what it waits for is known.
    fn deferred_time(&self, d: &Deferred) -> Option<f64> {
        if !self.beat_time_known(d.bi) {
            return None;
        }
        let t = crate::core::songmap::time_of_beat_index(&self.st.song, d.bi);
        match d.need {
            None => Some(t),
            Some(k) if k < self.st.ingested.beats => Some(max(t, self.log.beats[k].known_at)),
            Some(_) => None,
        }
    }

    /// Queues an event with an init key on the first downbeat at or after t, once known.
    fn push_init_at_downbeat(&mut self, t: f64, ev: Ev, phase: u16, index: u64) {
        let k = self.downbeat_index_at_or_after(t);
        self.push_init_at_beat(k as f64, ev, phase, index);
    }

    fn defer_slot(&mut self, bi: f64, slot: Slot, then: Then) {
        let d = Deferred {
            bi,
            need: None,
            slot,
            then,
        };
        match self.deferred_time(&d) {
            Some(t) => self.run_then(t, d.slot, d.then),
            None => self.st.deferred.push(d),
        }
    }

    fn push_slot(&mut self, t: f64, ev: Ev, slot: Slot) {
        match slot {
            Slot::Runtime(seq) => self.st.q.push_reserved(t, ev, seq),
            Slot::Init(phase, index) => self.st.q.push_init(t, ev, phase, index),
        }
    }

    fn run_then(&mut self, t: f64, slot: Slot, then: Then) {
        match then {
            Then::Push(ev) => self.push_slot(t, ev, slot),
            Then::OrbTaken {
                u,
                k,
                s,
                w,
                walked,
                item,
            } => {
                self.rec.upgrade_taken(u, k, s, w, t, walked);
                self.push_slot(t, Ev::OrbApply { item }, slot);
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
        if self.st.freeze.frozen {
            return;
        }
        let waiting = std::mem::take(&mut self.st.deferred);
        for d in waiting {
            match self.deferred_time(&d) {
                Some(t) => self.run_then(t, d.slot, d.then),
                None => self.st.deferred.push(d),
            }
        }
    }

    // -------------------------------------------------------------- time

    /// The base time scale's steps: the finale's swell's once it is swelling.
    fn base_steps(&self) -> &[crate::schedule::RateStep] {
        match (&self.st.swell_steps, self.st.swelling) {
            (Some(s), true) => s,
            _ => &self.st.sch.base_steps,
        }
    }

    fn base_rate(&self, t: f64) -> f64 {
        if self.env.input.level.survivors.is_some() {
            1.0
        } else {
            step_rate(self.base_steps(), t)
        }
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
        if self.st.freeze.frozen {
            0.0
        } else if self.env.input.level.survivors.is_some() {
            1.0
        } else if self.in_stop(t) {
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

    // A landing must be one of the simulation's own stops. Otherwise a partial frame
    // can record it before/after the next hero sample and change the settled journal.
    fn survivor_landing_stop(&self) -> f64 {
        if self.st.survivor.is_none() || self.st.landed {
            return f64::INFINITY;
        }
        let Some(step) = &self.st.step else {
            return f64::INFINITY;
        };
        let rate = self.rate_at(self.st.rate_s);
        if rate <= 0.0 {
            return f64::INFINITY;
        }
        let target = step.w0 + self.env.rule.duration_s;
        let (s, w) = self
            .st
            .breaks
            .last()
            .filter(|b| b.rate == rate)
            .map_or((self.st.s, self.st.w), |b| (b.s, b.w));
        (s + (target - w) / rate).max(self.st.s)
    }

    /// Song time of the loop's next stop: the next event, fixed step or rate change.
    fn next_stop_now(&self) -> f64 {
        let t_fixed = self.st.fixed_k / SIM_RATE;
        let t_rate = min(
            min(
                next_step_after(self.base_steps(), self.st.s),
                if self.in_stop(self.st.s) {
                    self.st.stop_end
                } else {
                    f64::INFINITY
                },
            ),
            self.next_dip_cut(self.st.s),
        );
        min(
            min(
                min(
                    min(
                        min(
                            min(
                                min(self.st.q.peek(), self.weapon_stop()),
                                self.survivor_landing_stop(),
                            ),
                            self.spawn_stop()
                                .min(self.event_stop())
                                .min(self.boss_notice_stop()),
                        ),
                        t_fixed,
                    ),
                    t_rate,
                ),
                self.freeze_stop(),
            ),
            self.st.end_s,
        )
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
        if let Some(run) = self.env.input.level.survivors
            && !self.st.freeze.frozen
        {
            self.st.run_s = (t - self.st.freeze.total_s).min(run.duration_s);
        }
        let elapsed_world = if fixed {
            (w1 - self.st.last_fixed_w).max(0.0)
        } else {
            0.0
        };
        self.st.s = t;
        self.st.w = w1;
        if let Some(p) = &self.st.survivor {
            let recovery = p.stats.finalized().recovery;
            if elapsed_world > 0.0 && recovery > 0.0 {
                self.heal(recovery * elapsed_world, "recovery");
            }
        }
        if !between {
            self.st.rate_s = t;
        }
        if fixed && self.st.w > self.st.last_fixed_w {
            let w = self.st.w;
            self.move_foes(w);
            self.collect_gems();
            self.touch_orbs();
            self.touch_chickens();
            self.touch_chests();
            self.st.last_fixed_w = self.st.w;
        }
        self.rec.clock(self.st.s, self.st.w, between);
        if !between {
            self.publish_run();
        }
    }

    // -------------------------------------------------------------- the loop

    /// Song-owned events and the first samples: a fresh simulation's song time 0.
    fn init(&mut self) {
        // The stream's entries known at song time 0.
        self.ingest(0.0);
        if self.st.ingested.beats == 0 {
            self.arm_free_pulse();
        }
        for (i, lu) in self.st.sch.level_ups.iter().enumerate() {
            self.st
                .q
                .push_init(lu.s, Ev::LevelDue { i }, phase::LEVEL_DUE, i as u64);
        }
        // The starting loadout fires from the start, without a level-up.
        for id in self.st.loadout.weapons.clone() {
            self.grant_weapon(id.as_str());
        }
        self.publish_inventory();
        // The first camera and hero samples.
        let r0 = self.rate_at(0.0);
        self.push_break(r0);
        self.sample_hero(0.0, 0.0);
        self.st.next_hero_k = 1.0;
        self.record_foes(0.0);
        self.st.next_foe_k = 1.0;
        // Zen mode: the groove is full from the start, at the top tier.
        if self.env.input.zen && self.st.survivor.is_none() {
            self.st.groove = 1.0;
            self.rec.groove(0.0, 1.0);
            self.set_tier();
        }
    }

    /// One turn of the loop: move to the next stop, then run every event due there.
    fn step(&mut self) {
        let t_fixed = self.st.fixed_k / SIM_RATE;
        let t = self.next_stop();
        self.move_clock(t, t == t_fixed, false);
        self.tick_chest();
        self.maybe_resume();
        if t == t_fixed {
            self.st.fixed_k += 1.0;
            self.st.recycle_k += 1;
            if self.st.recycle_k.is_multiple_of(30) && !self.st.freeze.frozen {
                self.recycle();
                self.trim_state();
            }
        }
        while self.st.q.peek() <= self.st.s + 1e-12 {
            let entry = self.st.q.pop_due(self.st.s + 1e-12);
            self.dispatch_entry(entry);
        }
        self.tick_weapons();
        self.tick_enemy_bites();
        self.tick_boss_notice();
        self.tick_spawner();
        self.tick_survivor_events();
    }

    fn handle(&mut self, ev: Ev) {
        if self.env.input.level.survivors.is_some()
            && self.st.blasting
            && matches!(ev, Ev::Key(_) | Ev::Held)
        {
            return;
        }
        match ev {
            Ev::Freeze(reason) => self.enter_freeze(reason),
            Ev::FreePulse { generation } => self.free_pulse(generation),
            Ev::EnemyWind {
                peak,
                period,
                generation,
            } => self.enemy_wind(peak, period, generation),
            Ev::EnemyImpact {
                boss,
                foe,
                fate,
                hit,
            } => {
                if fate.lethal && boss {
                    self.boss_cue(crate::survivors::BossCueKind::Defeated, foe, fate.x, fate.y);
                }
                self.rec.enemy_impact(foe, fate, &hit);
            }
            Ev::MissileEnd(pose) => self.rec.missile(*pose),
            Ev::WandBullet(shot) => self.missile_burst_shot(*shot),
            Ev::LightningStrike(pending) => self.lightning_strike(*pending),
            Ev::HitstopEnd => {}
            Ev::Recoil { foe, multiplier } => self.survivor_recoil(foe, multiplier),
            Ev::Key(e) => {
                if self.freeze_key(&e) {
                    return;
                }
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
                if self.st.survivor.is_some() {
                    self.sword_beat(bi);
                    return;
                }
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
            Ev::ChickenHeal { chicken } => {
                let before = self.st.health;
                self.heal(CHICKEN_HEAL, "chicken");
                if self.st.survivor.is_some() {
                    self.rec.floor_chicken(
                        self.st.s,
                        crate::survivors::FloorChicken::Healed {
                            id: chicken,
                            amount: self.st.health - before,
                        },
                    );
                }
            }
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
            Ev::ZoneWave { zone, section } => self.zone_wave(zone, section),
            Ev::ZoneFallback { at } => self.zone_fallback(at),
            Ev::Blast { band } => {
                if band == 0 {
                    self.st.blasting = true;
                    if self.env.input.level.survivors.is_some() {
                        if let Some(zone) = self
                            .env
                            .input
                            .level
                            .zones
                            .iter()
                            .position(|z| z == "infernal-grove")
                        {
                            self.zone_wave(zone, None);
                        }
                        let pose = self.hero_pose(self.st.w);
                        self.st.rest_pose = (pose.x, pose.y);
                        self.st.step = None;
                    }
                    // The finale's swell starts now.
                    if self.st.swell_steps.is_some() {
                        self.st.swelling = true;
                    }
                }
                self.blast_band(band);
            }
            Ev::Landmark { id } => self.landmark(&id),
            Ev::BossSpawn => self.boss_spawn(),
            Ev::BossLand => self.boss_land(),
            Ev::BossRetreat => self.boss_retreat(),
            Ev::SlamCheck { i } => self.slam_check(i),
            Ev::SlamLand { k } => self.slam_land(k),
            Ev::Ring { k } => self.ring_enter(k),
            Ev::RingClose { k } => self.ring_close(k),
            Ev::Formation { f } => self.formation_enter(f),
            Ev::FormationRelease { f } => self.formation_release(f),
            Ev::DanceStart { f } => self.dance_start(f),
            Ev::Stampede { k } => self.stampede_enter(k),
            Ev::Archers { group } => {
                let n = self.st.plan.archer_groups[group].archers;
                self.spawn_archers(group, n);
            }
            Ev::DrawCheck { group, i } => self.draw_check(group, i),
            Ev::Release { v } => self.loose(v),
            Ev::Arrive { v } => self.arrive(v),
            Ev::VacuumDrop => self.vacuum_drop(),
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
    let sch = ReactiveSchedule::new(level, f64::INFINITY);
    let song = sch.song.clone();
    let idx = index_song(&song);
    SimState {
        s: 0.0,
        run_s: 0.0,
        spawner: crate::survivors::Spawner::default(),
        boss_presentation: crate::survivors::BossPresentation::default(),
        events: survivor_events::Events::default(),
        chests: Vec::new(),
        n_chests: 0,
        treasure_minutes: 0,
        roulette: None,
        garlic: None,
        last_bloody_heal: f64::NEG_INFINITY,
        soul_recovered: 0.0,
        missiles: Vec::new(),
        bibles: Vec::new(),
        next_missile: 0,
        next_lightning_volley: 0,
        next_lightning_strike: 0,
        freeze: freeze::Freeze::default(),
        survivor: level
            .survivors
            .map(|_| crate::survivors::Progress::default()),
        w: 0.0,
        rate_s: 0.0,
        q: Queue::default(),
        breaks: SharedVec::new(),
        stop_start: -1.0,
        stop_end: -1.0,
        rest_pose: (0.0, 0.0),
        step: None,
        strides: fresh_strides(),
        held: Vec::new(),
        zen_taps: Vec::new(),
        zen_stomp: false,
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
        swing_pool: Pool::default(),
        swings: Vec::new(),
        live_swings: Vec::new(),
        taken_slots: HashSet::new(),
        light_count: 0,
        n_inputs: 0,
        n_hits: 0,
        n_gems: 0,
        waves: Pool::default(),
        chains: Pool::default(),
        field: Vec::new(),
        flying_gems: Vec::new(),
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
        foes: Pool::default(),
        foe_samples: Vec::new(),
        blasting: false,
        health: if level.survivors.is_some() {
            120.0
        } else {
            env.input.lib.hero.health.max
        },
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
        fixed_k: 1.0,
        last_fixed_w: 0.0,
        recycle_k: 0,
        ingested: Ingested::default(),
        song,
        idx: Rc::new(idx),
        sch,
        judging: Judging::new(env.input.gate),
        strong_onsets: SharedVec::new(),
        forecasts: SharedVec::new(),
        held_queued: 0,
        deferred: Vec::new(),
        stomp_target: None,
        horizon: 0.0,
        end_s: f64::INFINITY,
        until_s: f64::INFINITY,
        plan: plan::Plan::new(),
        swell_steps: None,
        swelling: false,
        formation_dir: None,
        formation_members: Vec::new(),
        rings: Vec::new(),
        ring_members: Vec::new(),
        stampedes: Vec::new(),
        n_arrows: 0,
        flying: Vec::new(),
        slam_marks: Vec::new(),
        n_vacuums: 0,
        piece_ids: Vec::new(),
    }
}

#[cfg(test)]
mod tests {
    mod evolution_tests;
    use std::path::Path;
    use std::rc::Rc;

    use stream::{Audio, Beat, Params, StreamFile, StreamLog, Tick};

    use super::{ChainState, Ev, Sim, SimInput, WaveState};
    use crate::content::level::load_level;
    use crate::content::{SourceFile, load_content};
    use crate::movement::InputKey;
    use crate::player::{KeyEvent, KeyEventKind};
    use crate::recorder::NullRecorder;
    use crate::schedule::{grow_song_map, reactive_song_map};
    use std::collections::HashSet;

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
        sim_in(file, false)
    }

    fn sim_in(file: &StreamFile, zen: bool) -> Sim {
        sim_with(file, zen, false)
    }

    fn sim_with(file: &StreamFile, zen: bool, settling: bool) -> Sim {
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
        let input = SimInput::new(
            level,
            &lib,
            &indexmap::IndexMap::new(),
            file.params.gate,
            settling,
        );
        let input = SimInput { zen, ..input };
        Sim::new(
            input,
            Rc::new(file.log.clone()),
            Box::new(NullRecorder),
            None,
        )
    }

    #[test]
    fn survivors_cannot_fall_while_waiting_for_the_victory_downbeat() {
        let mut sim = sim(&stream());
        Rc::get_mut(&mut sim.env).unwrap().input.level.survivors =
            Some(crate::content::level::Survivors {
                duration_s: 2.0,
                contact_damage: 0.5,
                brute_chicken_chance: 0.05,
                boss_chickens: 1.0,
                maxed_heal: 30.0,
            });
        sim.st.run_s = 1.9;
        let hp = sim.st.health;
        sim.hurt(1.0, "contact", "slime");
        assert_eq!(sim.st.health, hp - 1.0);
        sim.st.invuln_until = 0.0;
        sim.st.run_s = 2.0;
        sim.hurt(hp, "contact", "slime");
        assert_eq!(sim.st.health, hp - 1.0);
    }

    #[test]
    fn a_queued_fire_keeps_its_remaining_run_time_through_a_freeze() {
        use crate::recorder::LogRecorder;
        use crate::survivors::FreezeReason;
        use std::cell::RefCell;
        let mut sim = sim_in(&stream(), true);
        Rc::get_mut(&mut sim.env).unwrap().input.level.survivors =
            Some(crate::content::level::Survivors {
                duration_s: 30.0,
                contact_damage: 0.5,
                brute_chicken_chance: 0.05,
                boss_chickens: 1.0,
                maxed_heal: 30.0,
            });
        let rec = Rc::new(RefCell::new(LogRecorder::default()));
        sim.rec = Box::new(Rc::clone(&rec));
        sim.st.fire_specs.clear();
        sim.st.q.remove(&|ev| matches!(ev, Ev::Fire { .. }));
        let weapon = sim
            .env
            .input
            .lib
            .weapons
            .get_index_of("orbiting-blades")
            .unwrap();
        sim.st.q.push(1.4, Ev::Fire { weapon, bi: 1.8 });
        sim.freeze_at(1.1, FreezeReason::Paused);
        key(&mut sim, 3.1, KeyEventKind::Press, InputKey::Pause);
        key(&mut sim, 3.2, KeyEventKind::Release, InputKey::Pause);
        sim.advance_to(3.9);
        assert!(!rec.borrow().fires.contains_key("orbiting-blades"));
        sim.advance_to(4.1);
        assert_eq!(rec.borrow().fires["orbiting-blades"], vec![4.0]);
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
    fn past_the_last_bar_a_downbeat_waits_for_its_beat() {
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
        // (Live: the stream's end is never known, so the next downbeat is pending, never now.)
        assert!(queued.is_empty(), "queued at {queued:?}");
        assert_eq!(sim.st.deferred.len(), 1);
    }

    fn key(sim: &mut Sim, s: f64, kind: KeyEventKind, key: InputKey) {
        sim.advance_to(s);
        sim.key(KeyEvent {
            s,
            kind,
            key,
            id: None,
        });
    }

    /// Song time the stride in progress started, if any.
    fn stride_s0(sim: &Sim) -> Option<f64> {
        sim.st.step.as_ref().and_then(|st| st.s0)
    }

    fn on_a_step(sim: &Sim, s: f64) -> bool {
        sim.st
            .sch
            .stride_steps
            .s
            .iter()
            .any(|t| (t - s).abs() < 1e-9)
    }

    #[test]
    fn zen_mode_judges_nothing_and_keeps_the_groove_full() {
        let file = stream();
        let mut sim = sim_in(&file, true);
        let top = crate::timing::tier_for(&sim.env.input.lib.hero, 0.0, 1.0);
        assert!(top > 0.0);
        // Presses all over the place, on and off the beat.
        for k in 0..40u32 {
            let s = 0.3 + 0.137 * f64::from(k);
            let pressed = [InputKey::Right, InputKey::Up, InputKey::Space][k as usize % 3];
            key(&mut sim, s, KeyEventKind::Press, pressed);
            key(&mut sim, s + 0.05, KeyEventKind::Release, pressed);
            assert_eq!(sim.st.groove, 1.0, "the groove at {s}");
            assert_eq!(sim.st.tier, top, "the tier at {s}");
        }
        sim.advance_to(6.9);
        assert_eq!(sim.st.groove, 1.0);
        assert!(sim.st.memo.counted.is_empty(), "no press was counted");
        assert!(
            !sim.st.q.heap.iter().any(|e| matches!(e.ev, Ev::Settle(_))),
            "no press settles"
        );
    }

    #[test]
    fn a_zen_press_moves_on_the_next_stride_step_and_a_tap_strides_once() {
        let file = stream();
        let mut sim = sim_in(&file, true);
        // Off the beat (1.5 and 2.0 are beats): nothing moves at the press.
        key(&mut sim, 1.62, KeyEventKind::Press, InputKey::Right);
        sim.advance_to(1.63);
        assert_eq!(stride_s0(&sim), None, "the hero waits for the beat");
        key(&mut sim, 1.66, KeyEventKind::Release, InputKey::Right);
        sim.advance_to(2.01);
        let s0 = stride_s0(&sim).expect("the tap strides on the next step");
        assert!(s0 > 1.62 && on_a_step(&sim, s0), "strode at {s0}");
        // Released: no more strides.
        sim.advance_to(3.6);
        assert_eq!(stride_s0(&sim), Some(s0));
    }

    #[test]
    fn a_held_key_in_zen_mode_strides_on_every_step() {
        let file = stream();
        let mut sim = sim_in(&file, true);
        key(&mut sim, 1.62, KeyEventKind::Press, InputKey::Right);
        let mut starts = Vec::new();
        let mut s = 1.62;
        while s < 4.0 {
            s += 0.01;
            sim.advance_to(s);
            if let Some(s0) = stride_s0(&sim)
                && starts.last() != Some(&s0)
            {
                starts.push(s0);
            }
        }
        assert!(starts.len() >= 4, "strides at {starts:?}");
        assert!(starts.iter().all(|&t| on_a_step(&sim, t)), "{starts:?}");
    }

    #[test]
    fn zen_mode_judges_nothing_while_settling_either() {
        let file = stream();
        let mut sim = sim_with(&file, true, true);
        for k in 0..30u32 {
            let s = 0.3 + 0.171 * f64::from(k);
            key(&mut sim, s, KeyEventKind::Press, InputKey::Left);
            key(&mut sim, s + 0.05, KeyEventKind::Release, InputKey::Left);
            assert_eq!(sim.st.groove, 1.0, "the groove at {s}");
        }
        sim.advance_to(6.9);
        assert_eq!(sim.st.groove, 1.0);
        assert!(sim.st.prov_memo.counted.is_empty() && sim.st.memo.counted.is_empty());
        assert!(!sim.st.q.heap.iter().any(|e| matches!(e.ev, Ev::Settle(_))));
    }

    #[test]
    fn a_zen_tap_bends_a_held_stride_into_a_diagonal() {
        let file = stream();
        let mut sim = sim_in(&file, true);
        key(&mut sim, 1.2, KeyEventKind::Press, InputKey::Right);
        sim.advance_to(1.51);
        key(&mut sim, 1.62, KeyEventKind::Press, InputKey::Up);
        key(&mut sim, 1.66, KeyEventKind::Release, InputKey::Up);
        let s0 = stride_s0(&sim);
        while stride_s0(&sim) == s0 && sim.st.s < 6.0 {
            let s = sim.st.s + 0.01;
            sim.advance_to(s);
        }
        let st = sim.st.step.as_ref().unwrap();
        assert!(st.dx > 0.5 && st.dy < -0.5, "({}, {})", st.dx, st.dy);
    }

    #[test]
    fn a_zen_stomp_waits_for_the_step_and_stamps_in_place() {
        let file = stream();
        let mut sim = sim_in(&file, true);
        key(&mut sim, 1.2, KeyEventKind::Press, InputKey::Right);
        sim.advance_to(1.62);
        key(&mut sim, 1.62, KeyEventKind::Release, InputKey::Right);
        key(&mut sim, 1.63, KeyEventKind::Press, InputKey::Space);
        key(&mut sim, 1.66, KeyEventKind::Release, InputKey::Space);
        key(&mut sim, 1.67, KeyEventKind::Press, InputKey::Down);
        key(&mut sim, 1.69, KeyEventKind::Release, InputKey::Down);
        let before = stride_s0(&sim);
        assert!(sim.st.zen_stomp, "the stomp waits");
        while (!sim.st.landed || stride_s0(&sim) == before) && sim.st.s < 6.0 {
            let s = sim.st.s + 0.01;
            sim.advance_to(s);
        }
        let st = sim.st.step.as_ref().unwrap();
        let at = st.s0.unwrap();
        assert!(on_a_step(&sim, at), "stomped at {at}");
        assert!(st.rock.is_none() && st.len == Some(0.0), "stamped in place");
        // The tap strides after the stomp's grid step.
        while stride_s0(&sim) == Some(at) && sim.st.s < 6.0 {
            let s = sim.st.s + 0.01;
            sim.advance_to(s);
        }
        let st = sim.st.step.as_ref().unwrap();
        assert!(st.dy > 0.9 && on_a_step(&sim, st.s0.unwrap()));
    }

    #[test]
    fn a_zen_wait_survives_a_snapshot() {
        let file = stream();
        let mut a = sim_in(&file, true);
        key(&mut a, 1.62, KeyEventKind::Press, InputKey::Right);
        key(&mut a, 1.64, KeyEventKind::Press, InputKey::Space);
        key(&mut a, 1.66, KeyEventKind::Release, InputKey::Right);
        let snap = a.snapshot();
        a.advance_to(4.0);
        let mut b = sim_in(&file, true);
        b.advance_to(0.5);
        b.restore(&snap);
        b.advance_to(4.0);
        assert_eq!(format!("{:?}", a.st.step), format!("{:?}", b.st.step));
        assert_eq!(a.st.zen_taps, b.st.zen_taps);
    }

    #[test]
    fn without_zen_mode_a_press_moves_at_once() {
        let file = stream();
        let mut sim = sim(&file);
        key(&mut sim, 1.62, KeyEventKind::Press, InputKey::Right);
        sim.advance_to(1.63);
        assert_eq!(stride_s0(&sim), Some(1.62));
    }
    fn survivor_sim() -> Sim {
        survivor_sim_with(stream())
    }

    fn survivor_sim_with(file: StreamFile) -> Sim {
        let base = sim_in(&file, true);
        let lib = &base.env.input.lib;
        let mut level = load_level(
            &source("levels/survivors.yaml"),
            lib,
            &reactive_song_map(f64::INFINITY),
            None,
        )
        .unwrap();
        level.survivor_schedule.clear();
        let director = level.director.as_mut().unwrap();
        director.population.start = 0.0;
        director.population.per_minute = 0.0;
        let mut input = SimInput::new(level, lib, &indexmap::IndexMap::new(), 0.5, false);
        input.zen = true;
        let mut sim = Sim::new(input, Rc::new(file.log), Box::new(NullRecorder), None);
        sim.st.q.remove(&|ev| matches!(ev, Ev::Auto { .. }));
        sim
    }

    fn rhythm_foe(sim: &mut Sim, x: f64) -> usize {
        let def = sim.env.input.lib.enemies.get_index_of("vs-zombie").unwrap();
        let stats = Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def]
            .survivors
            .as_mut()
            .unwrap();
        stats.knockback = 0.0; // Isolate contact attacks from the hero's sword recoil.
        sim.spawn_foe(def, x, 0.0, Some(100_000.0))
    }

    #[test]
    fn survivor_lunge_has_notice_bites_on_the_peak_and_only_once_per_beat() {
        use crate::recorder::LogRecorder;
        use std::cell::RefCell;
        let mut sim = survivor_sim();
        let rec = Rc::new(RefCell::new(LogRecorder::default()));
        sim.rec = Box::new(Rc::clone(&rec));
        let id = rhythm_foe(&mut sim, 30.0);
        sim.advance_to(0.26);
        let attack = sim.st.foes[id].attack.expect("offbeat wind-up");
        let at_open = attack.offset(sim.st.w);
        sim.advance_to(0.35);
        let back = sim.st.foes[id].attack.unwrap().offset(sim.st.w);
        assert!(back.0 > at_open.0, "wind-up leans away from the hero");
        assert!(sim.pos(id, sim.st.w).0.is_finite());
        sim.advance_to(0.499);
        assert_eq!(sim.st.health, 120.0, "continuous contact cannot bite");
        sim.advance_to(0.501);
        assert_eq!(sim.st.health, 116.0);
        assert!(sim.st.foes[id].attack.unwrap().attempted);
        sim.st.invuln_until = 0.0;
        sim.tick_enemy_bites();
        sim.enemy_wind(0.5, 0.5, None);
        sim.tick_enemy_bites();
        assert_eq!(
            sim.st.health, 116.0,
            "no second bite even if invulnerability is removed"
        );
        sim.advance_to(1.1);
        let rec = rec.borrow();
        assert!(!rec.bites.is_empty());
        for bite in &rec.bites {
            let s = bite["s"].as_f64().unwrap();
            assert!(
                (s / 0.5 - (s / 0.5).round()).abs() < 1e-8,
                "bite off-grid: {s}"
            );
        }
    }

    #[test]
    fn survivor_lunge_rechecks_contact_at_the_peak_and_has_no_surprise_resume_bite() {
        let mut sim = survivor_sim();
        let id = rhythm_foe(&mut sim, 30.0);
        sim.advance_to(0.26);
        assert!(sim.st.foes[id].attack.is_some());
        // The hero escaped the notice: a queued attack has no right to deal damage.
        sim.st.foes[id].x = 1000.0;
        sim.advance_to(0.51);
        assert_eq!(sim.st.health, 120.0);
        sim.st.foes[id].x = 30.0;
        sim.advance_to(0.76);
        assert!(sim.st.foes[id].attack.is_some());
        sim.freeze_at(0.77, crate::survivors::FreezeReason::Paused);
        sim.advance_to(1.2);
        let held = sim.pos(id, sim.st.w);
        sim.advance_to(1.4);
        assert_eq!(held, sim.pos(id, sim.st.w));
        key(&mut sim, 1.4, KeyEventKind::Press, InputKey::Pause);
        key(&mut sim, 1.41, KeyEventKind::Release, InputKey::Pause);
        sim.advance_to(1.501);
        assert!(!sim.st.freeze.frozen);
        assert!(
            sim.st.foes[id].attack.is_none(),
            "a new offbeat must give fresh notice"
        );
        assert_eq!(sim.st.health, 120.0);
    }

    #[test]
    fn survivor_early_roles_step_at_the_design_subdivisions_and_stay_finite() {
        use crate::recorder::LogRecorder;
        use std::cell::RefCell;
        let mut sim = survivor_sim();
        let rec = Rc::new(RefCell::new(LogRecorder::default()));
        sim.rec = Box::new(Rc::clone(&rec));
        for (i, (role, steps)) in [
            ("vs-bat1", 2.0),
            ("vs-bat2", 2.0),
            ("vs-bat3", 2.0),
            ("vs-zombie", 1.0),
            ("vs-ghost", 4.0),
            ("vs-skeleton", 1.0),
        ]
        .into_iter()
        .enumerate()
        {
            let def = sim.env.input.lib.enemies.get_index_of(role).unwrap();
            let data = &sim.env.input.lib.enemies[def];
            assert_eq!(
                crate::gait::gait_per_beat(data.gait.as_ref().unwrap()),
                steps,
                "{role}"
            );
            assert_eq!(data.walk_cycle_beats, 1.0 / steps, "{role}");
            sim.spawn_foe(def, 300.0, i as f64 * 70.0, Some(100_000.0));
        }
        for i in 1..=240 {
            sim.advance_to(i as f64 / 60.0);
            for &id in &sim.st.alive {
                let (x, y) = sim.pos(id, sim.st.w);
                assert!(x.is_finite() && y.is_finite());
            }
        }
        for enemy in &rec.borrow().enemies {
            let steps = enemy
                .steps
                .as_ref()
                .expect("gait starts after evidence locks");
            assert!(
                steps.iter().any(|s| s.dur > 0.0),
                "{} never stepped",
                enemy.kind
            );
        }
    }

    #[test]
    fn survivor_lunges_and_gaits_never_read_the_uncommitted_tail() {
        use crate::recorder::LogRecorder;
        use std::cell::RefCell;
        let cut = 1.6;
        let mut poisoned = stream();
        for (i, beat) in poisoned.log.beats.iter_mut().enumerate() {
            if beat.known_at > cut {
                beat.t += i as f64 * 0.3;
                beat.period = 0.9;
                beat.evidence = 0.0;
            }
        }
        for tick in &mut poisoned.log.ticks {
            if tick.known_at > cut {
                tick.evidence = 0.0;
                tick.energy = 0.0;
            }
        }
        let run = |file| {
            let mut sim = survivor_sim_with(file);
            let rec = Rc::new(RefCell::new(LogRecorder::default()));
            sim.rec = Box::new(Rc::clone(&rec));
            let id = rhythm_foe(&mut sim, 30.0);
            for i in 1..=160 {
                sim.advance_to(i as f64 / 100.0);
                let (x, y) = sim.pos(id, sim.st.w);
                assert!(
                    x.is_finite() && y.is_finite(),
                    "opening movement must stay finite"
                );
            }
            assert!(!rec.borrow().bites.is_empty());
            let output = format!("{:?}", rec.borrow());
            output
        };
        assert_eq!(run(stream()), run(poisoned));
    }

    #[test]
    fn survivor_lunge_free_pulse_is_generation_guarded_and_reactions_use_sixteenths() {
        let mut sim = survivor_sim();
        let id = rhythm_foe(&mut sim, 30.0);
        let mut grid = survivor_sim();
        grid.advance_to(0.6);
        assert!((grid.next_enemy_sixteenth(0.6) - 0.625).abs() < 1e-9);
        sim.st.s = 0.25;
        sim.st.w = 0.25;
        let generation = sim.st.freeze.pulse_generation;
        sim.enemy_wind(0.5, 0.5, Some(generation));
        sim.st.s = 0.5;
        sim.st.w = 0.5;
        sim.st.freeze.pulse_generation += 1;
        sim.tick_enemy_bites();
        assert_eq!(
            sim.st.health, 120.0,
            "new committed grid invalidates the old free pulse"
        );
        sim.st.foes[id].attack = None;
        let generation = sim.st.freeze.pulse_generation;
        sim.st.s = 0.75;
        sim.st.w = 0.75;
        sim.enemy_wind(1.0, 0.5, Some(generation));
        sim.st.s = 1.0;
        sim.st.w = 1.0;
        sim.tick_enemy_bites();
        assert_eq!(sim.st.health, 116.0);
    }

    #[test]
    fn survivor_health_uses_armor_lockout_and_heals_the_added_hollow_heart_maximum() {
        use crate::survivors::StatDelta;
        let mut sim = survivor_sim();
        assert_eq!(sim.st.health, 120.0);
        sim.hurt(10.0, "bite", "skeleton");
        assert_eq!(sim.st.health, 116.0);
        sim.advance_to(0.119);
        sim.hurt(10.0, "bite", "skeleton");
        assert_eq!(sim.st.health, 116.0);
        sim.advance_to(0.121);
        sim.hurt(10.0, "bite", "skeleton");
        assert_eq!(sim.st.health, 112.0);
        sim.apply_survivor_stats(StatDelta {
            max_hp: 0.2,
            ..StatDelta::default()
        });
        assert_eq!(sim.health_max(), 144.0);
        assert_eq!(sim.st.health, 136.0);
        for _ in 0..4 {
            sim.apply_survivor_stats(StatDelta {
                max_hp: 0.2,
                ..StatDelta::default()
            });
        }
        assert!((sim.health_max() - 298.5984).abs() < 1e-9);
        assert!((sim.st.health - (298.5984 - 8.0)).abs() < 1e-9);
    }

    #[test]
    fn survivor_world_time_ignores_stops_dips_and_rates_but_holds_recovery_in_freeze() {
        use crate::survivors::{FreezeReason, StatDelta};
        let mut a = survivor_sim();
        let mut b = survivor_sim();
        let ar = Rc::new(std::cell::RefCell::new(
            crate::recorder::LogRecorder::default(),
        ));
        let br = Rc::new(std::cell::RefCell::new(
            crate::recorder::LogRecorder::default(),
        ));
        a.rec = Box::new(Rc::clone(&ar));
        b.rec = Box::new(Rc::clone(&br));
        for sim in [&mut a, &mut b] {
            sim.st.health = 100.0;
            sim.apply_survivor_stats(StatDelta {
                recovery: 1.0,
                ..StatDelta::default()
            });
            sim.st.stop_start = 0.0;
            sim.st.stop_end = 9.0;
            sim.st.dip_start = 0.0;
            sim.st.sch.base_steps[0].rate = 0.2;
            sim.start_hit_stop(0.15);
            sim.freeze_at(1.0, FreezeReason::Paused);
            key(sim, 3.1, KeyEventKind::Press, InputKey::Pause);
            key(sim, 3.2, KeyEventKind::Release, InputKey::Pause);
        }
        a.advance_to(2.0);
        assert!((a.w() - 1.0).abs() < 1e-9);
        let health = a.st.health;
        a.advance_to(3.0);
        assert_eq!(a.st.health, health);
        a.advance_to(5.0);
        for k in 1..=500 {
            b.advance_to(f64::from(k) / 100.0);
        }
        assert!((a.w() - a.run_s()).abs() < 1e-9);
        assert_eq!(a.st.health, b.st.health);
        assert!((a.st.health - (100.0 + a.w())).abs() < 0.01);
        assert_eq!(format!("{:?}", ar.borrow()), format!("{:?}", br.borrow()));
    }

    #[test]
    fn pickup_source_magnet_body_overlap_kicks_out_then_accelerates_to_collection() {
        let mut sim = survivor_sim();
        let rec = Rc::new(std::cell::RefCell::new(
            crate::recorder::LogRecorder::default(),
        ));
        sim.rec = Box::new(Rc::clone(&rec));
        for (fi, x) in [(0, 60.0), (1, 62.0)] {
            sim.drop_on_field(
                fi,
                crate::recorder::Fate {
                    critical: false,
                    w: 0.0,
                    x,
                    y: 0.0,
                    hit: 0.0,
                    lethal: true,
                    damage: 0.0,
                },
                1.0,
                false,
            );
        }
        assert!((sim.pickup_radius() - 0.3 * crate::survivors::PX_PER_VS_UNIT).abs() < 1e-9);
        sim.collect_gems();
        assert_eq!(
            sim.st.field.len(),
            1,
            "0.3U magnet plus0.1U gem body, not magnet alone"
        );
        assert_eq!(sim.st.flying_gems.len(), 1);
        sim.advance_to(0.2);
        assert!(
            rec.borrow()
                .gem_poses
                .iter()
                .any(|p| p.id == 0 && p.x > 65.0),
            "negative start gives an outward kick"
        );
        assert!(
            sim.st.survivor.as_ref().unwrap().xp == 0.0,
            "not collected on an arbitrary early beat"
        );
        sim.advance_to(1.2);
        let r = rec.borrow();
        assert_eq!(sim.st.survivor.as_ref().unwrap().xp, 1.0);
        assert_eq!(sim.st.field.len(), 1);
        assert!(r.xp.0.last().is_some_and(|s| *s > 0.5));
        eprintln!(
            "source magnet46.280992px +body15.426997px: x60 attracted, x62 waits; first gem collected at world{:.6}, poses{}",
            r.xp.0.last().copied().unwrap(),
            r.gem_poses.len()
        );
        drop(r);
        sim.apply_survivor_stats(crate::survivors::StatDelta {
            magnet: 0.5,
            ..Default::default()
        });
        sim.collect_gems();
        assert!(
            sim.st.field.is_empty(),
            "magnet modifiers apply to magnet radius, not gem body"
        );
    }
    #[test]
    fn pickup_motion_and_food_wait_through_a_fractional_world_freeze() {
        let mut sim = survivor_sim();
        let rec = Rc::new(std::cell::RefCell::new(
            crate::recorder::LogRecorder::default(),
        ));
        sim.rec = Box::new(Rc::clone(&rec));
        sim.drop_on_field(
            0,
            crate::recorder::Fate {
                critical: false,
                w: 0.0,
                x: 60.0,
                y: 0.0,
                hit: 0.0,
                lethal: true,
                damage: 0.0,
            },
            1.0,
            false,
        );
        sim.freeze_at(0.203, crate::survivors::FreezeReason::Paused);
        sim.advance_to(0.21);
        let before = rec.borrow().gem_poses.clone();
        rec.borrow_mut()
            .chickens
            .push(crate::recorder::ChickenRecord {
                s: sim.st.s,
                w: sim.st.w,
                x: 0.0,
                y: 0.0,
                s_taken: None,
                w_taken: None,
                s_heal: None,
            });
        sim.st.chickens.push(super::Spot {
            i: 0,
            x: 0.0,
            y: 0.0,
        });
        sim.advance_to(2.0);
        assert_eq!(rec.borrow().gem_poses, before);
        assert!(rec.borrow().floor_chickens.is_empty(), "no paused pickup");
        assert_eq!(sim.st.survivor.as_ref().unwrap().xp, 0.0);
        key(&mut sim, 2.1, KeyEventKind::Press, InputKey::Pause);
        key(&mut sim, 2.11, KeyEventKind::Release, InputKey::Pause);
        sim.advance_to(3.7);
        assert_eq!(sim.st.survivor.as_ref().unwrap().xp, 1.0);
        assert!(
            rec.borrow()
                .floor_chickens
                .iter()
                .any(|e| matches!(e, crate::survivors::FloorChicken::Healed { .. }))
        );
    }
    #[test]
    fn pickup_boss_chicken_drops_heals_thirty_caps_missing_hp_and_protects_for_300ms() {
        for (start, expected) in [(80.0, 110.0), (115.0, 120.0)] {
            let (rec, mut sim, def) = garlic_measurement(120.0, 1);
            Rc::get_mut(&mut sim.env).unwrap().input.no_hero_damage = false;
            sim.st.health = start;
            let fi = sim.spawn_foe(def, 100.0, 0.0, Some(1.0));
            sim.st.foes[fi].survivor_boss = true;
            let hit = sim.add_hit(
                super::combat::HitSpec {
                    source: crate::recorder::HitSource::Weapon,
                    what: "sword".into(),
                    weight: crate::recorder::Weight::Light,
                    primary: true,
                    shape: crate::core::geometry::Shape::Circle {
                        x: 100.0,
                        y: 0.0,
                        radius: 1.0,
                    },
                    dir: 0.0,
                    accent: 0.0,
                    damage: 1.0,
                    group: None,
                },
                None,
            );
            sim.damage(fi, &hit);
            assert_eq!(rec.borrow().chickens.len(), 1, "level bossChickens1");
            assert!(matches!(
                rec.borrow().floor_chickens[0],
                crate::survivors::FloorChicken::Drop {
                    id: 0,
                    x: 100.0,
                    y: 0.0
                }
            ));
            sim.st.rest_pose = (100.0, 0.0);
            sim.touch_chickens();
            assert!(sim.st.chickens.is_empty());
            sim.advance_to(0.126);
            assert_eq!(sim.st.health, expected);
            assert!(rec.borrow().floor_chickens.iter().any(|e|matches!(e,crate::survivors::FloorChicken::Healed{id:0,amount} if (*amount-(expected-start)).abs()<1e-9)));
            sim.hurt(10.0, "bite", "test");
            assert_eq!(sim.st.health, expected, "food pickup immunity is300ms");
            sim.advance_to(0.301);
            sim.hurt(10.0, "bite", "test");
            assert_eq!(sim.st.health, expected - 4.0);
            eprintln!(
                "boss chicken: HP{start} -> {expected}, actual heal{}, immunity through0.300s then normal bite4HP",
                expected - start
            );
        }
    }

    #[test]
    fn survivor_field_keeps_400_gems_and_merges_xp_into_the_farthest_from_the_hero() {
        use crate::recorder::Fate;
        let mut sim = survivor_sim();
        for k in 0..400 {
            sim.drop_on_field(
                k,
                Fate {
                    critical: false,
                    w: 0.0,
                    x: k as f64,
                    y: 0.0,
                    hit: 0.0,
                    lethal: true,
                    damage: 1.0,
                },
                1.0,
                false,
            );
        }
        // A new drop by the near gem must still merge into the far gem.
        sim.drop_on_field(
            400,
            Fate {
                critical: false,
                w: 0.0,
                x: 0.0,
                y: 0.0,
                hit: 0.0,
                lethal: true,
                damage: 1.0,
            },
            10.0,
            true,
        );
        assert_eq!(sim.st.field.len(), 400);
        assert_eq!(sim.st.n_gems, 400);
        assert_eq!(sim.st.field[0].value, 1.0);
        assert_eq!(sim.st.field[399].value, 11.0);
        assert!(sim.st.field[399].big);
        sim.gem_arrives(399, 45.0);
        let progress = sim.survivor_progress().unwrap();
        assert_eq!(
            (progress.level, progress.xp, progress.pending_levels),
            (4, 45.0, 3)
        );
    }

    #[test]
    fn survivor_zero_xp_drops_no_gem_and_recoil_starts_on_the_next_sixteenth() {
        use super::combat::HitSpec;
        use crate::core::geometry::Shape;
        use crate::recorder::{HitSource, Weight};
        let mut sim = survivor_sim();
        let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
        Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def]
            .survivors
            .as_mut()
            .unwrap()
            .xp = 0.0;
        sim.advance_to(0.55);
        let enemy = sim.spawn_foe(def, 100.0, 0.0, Some(100.0));
        sim.advance_to(0.6);
        let hit = sim.add_hit(
            HitSpec {
                source: HitSource::Weapon,
                what: "sword".into(),
                weight: Weight::Light,
                primary: true,
                shape: Shape::Circle {
                    x: 100.0,
                    y: 0.0,
                    radius: 1000.0,
                },
                dir: 0.0,
                accent: 0.0,
                damage: 1.0,
                group: None,
            },
            Some(1.0),
        );
        sim.damage(enemy, &hit);
        assert!(sim.st.foes[enemy].recoil_until.is_infinite());
        sim.advance_to(0.624);
        assert!(sim.st.foes[enemy].recoil_until.is_infinite());
        sim.advance_to(0.626);
        assert!((sim.st.foes[enemy].recoil_until - 0.745).abs() < 1e-9);
        assert!(
            (sim.st.foes[enemy]
                .recoil_v
                .0
                .hypot(sim.st.foes[enemy].recoil_v.1)
                - 196.0)
                .abs()
                < 1e-9
        );
        sim.advance_to(0.76);
        assert_eq!(sim.st.foes[enemy].recoil_until, f64::NEG_INFINITY);
        Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def]
            .survivors
            .as_mut()
            .unwrap()
            .knockback = 0.0;
        let immune = sim.spawn_foe(def, 300.0, 0.0, Some(100.0));
        sim.damage(immune, &hit);
        sim.advance_to(0.9);
        assert_eq!(sim.st.foes[immune].recoil_until, f64::NEG_INFINITY);
        let mut lethal = hit;
        lethal.damage = 1000.0;
        sim.damage(enemy, &lethal);
        assert!(sim.st.field.is_empty());
        assert_eq!(sim.st.n_gems, 0);
    }
    fn sword_measurement_base(
        bpm: f64,
        rank: u8,
        delta: crate::survivors::StatDelta,
        accent: bool,
    ) -> (Rc<std::cell::RefCell<crate::recorder::LogRecorder>>, Sim) {
        let mut sim = survivor_sim();
        let period = 60.0 / bpm;
        let mut log = StreamLog {
            beats: (0..((62.0 / period).ceil() as usize + 2))
                .map(|i| {
                    let t = i as f64 * period;
                    Beat {
                        t,
                        known_at: (t - period).max(0.0),
                        period,
                        evidence: 1.0,
                    }
                })
                .collect(),
            ..StreamLog::default()
        };
        log.ticks = (0..1240)
            .map(|i| Tick {
                t: f64::from(i) / 20.0,
                known_at: f64::from(i) / 20.0,
                evidence: 1.0,
                energy: 0.5,
                level: None,
                key: None,
                bands: None,
                brightness: None,
            })
            .collect();
        if accent {
            log.forecasts = Some(
                log.beats
                    .iter()
                    .skip(1)
                    .step_by(4)
                    .map(|b| stream::Forecast {
                        t: b.t,
                        known_at: (b.t - period).max(0.0),
                        cycle: 4.0,
                        confidence: 1.0,
                    })
                    .collect(),
            );
        }
        let env = Rc::get_mut(&mut sim.env).unwrap();
        env.input.no_hero_damage = true;
        let def = env.input.lib.enemies.get_index_of("skeleton").unwrap();
        env.input.lib.enemies[def].speed = 0.0;
        env.input.lib.enemies[def]
            .survivors
            .as_mut()
            .unwrap()
            .knockback = 0.0;
        sim.log = Rc::new(log);
        sim.st = super::fresh_state(&sim.env);
        let rec = Rc::new(std::cell::RefCell::new(
            crate::recorder::LogRecorder::default(),
        ));
        sim.rec = Box::new(Rc::clone(&rec));
        sim.init();
        for _ in 1..rank {
            sim.learn_survivor_weapon(crate::survivors::WeaponId::Sword)
                .unwrap();
        }
        sim.apply_survivor_stats(delta);
        (rec, sim)
    }

    fn sword_measurement(
        bpm: f64,
        rank: u8,
        delta: crate::survivors::StatDelta,
        accent: bool,
    ) -> (Rc<std::cell::RefCell<crate::recorder::LogRecorder>>, Sim) {
        let (rec, mut sim) = sword_measurement_base(bpm, rank, delta, accent);
        let def = sim.env.input.lib.enemies.get_index_of("skeleton").unwrap();
        sim.spawn_foe(def, 25.0, 0.0, Some(1e9));
        (rec, sim)
    }

    #[test]
    fn survivor_sword_tuned_radius_has_a_real_near_and_far_boundary() {
        let (rec, mut sim) =
            sword_measurement_base(128.0, 1, crate::survivors::StatDelta::default(), false);
        let def = sim
            .env
            .input
            .lib
            .enemies
            .get_index_of("vs-skeleton")
            .unwrap();
        let data = &mut Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def];
        data.speed = 0.0;
        let stats = data.survivors.as_mut().unwrap();
        stats.speed = 0.0;
        stats.knockback = 0.0;
        sim.spawn_foe(def, 40.0, 0.0, Some(1e9));
        sim.spawn_foe(def, 70.0, 0.0, Some(1e9));
        sim.advance_to(2.0);
        let log = rec.borrow();
        assert!(!log.enemies[0].fates.is_empty());
        assert!(
            log.enemies[1].fates.is_empty(),
            "the 70px target must be outside the shortened spin"
        );
    }

    #[test]
    fn survivor_sword_ttk_and_personal_space_measurements() {
        use crate::survivors::{StatDelta, WeaponId, weapon_catalog};
        eprintln!(
            "stationary TTK: BPM, role, source HP, actual hits, first hit s, death s, full-cycle TTK s, VS Whip hits, VS Whip full-cycle s"
        );
        for bpm in [70.0, 128.0, 174.0] {
            for kind in ["vs-bat3", "vs-skeleton"] {
                let (rec, mut sim) = sword_measurement_base(bpm, 1, StatDelta::default(), false);
                let def = sim.env.input.lib.enemies.get_index_of(kind).unwrap();
                let hp = sim.env.input.lib.enemies[def].health;
                let data = &mut Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def];
                data.speed = 0.0;
                let stats = data.survivors.as_mut().unwrap();
                stats.speed = 0.0;
                stats.knockback = 0.0;
                stats.xp = 0.0;
                sim.spawn_foe(def, 25.0, 0.0, None);
                sim.advance_to(5.0);
                let log = rec.borrow();
                let target = &log.enemies[0];
                let death = target
                    .fates
                    .iter()
                    .find(|f| f.lethal)
                    .expect("the target dies")
                    .w;
                let whip = weapon_catalog().get(WeaponId::Sword).level(1);
                let whip_hits = (hp / whip.power).ceil();
                let whip_s = whip_hits * whip.interval_ms / 1000.0;
                let first = target.fates[0].w;
                let full_cycle = death - first + 60.0 / bpm;
                eprintln!(
                    "{bpm},{kind},{hp},{},{first:.9},{death:.9},{full_cycle:.9},{whip_hits},{whip_s:.9}",
                    target.fates.len()
                );
                assert!(
                    full_cycle <= whip_s + 1e-6,
                    "{bpm} {kind}: {death} > Whip {whip_s}"
                );
            }
        }
        eprintln!(
            "approach: BPM, role, chosen radius, spawn phase, first-hit distance, kill distance, death s, hits, hero bites"
        );
        for bpm in [70.0, 128.0, 174.0] {
            for kind in ["vs-bat3", "vs-skeleton"] {
                for radius in [48.0, 56.0, 64.0, 80.0] {
                    for phase in [0.0, 0.25, 0.5, 0.75] {
                        let (rec, mut sim) = sword_measurement_base(
                            bpm,
                            1,
                            StatDelta {
                                area: radius / crate::survivors::SWORD_BASE_RADIUS - 1.0,
                                ..StatDelta::default()
                            },
                            false,
                        );
                        Rc::get_mut(&mut sim.env).unwrap().input.no_hero_damage = false;
                        let def = sim.env.input.lib.enemies.get_index_of(kind).unwrap();
                        sim.advance_to(phase * 60.0 / bpm);
                        sim.spawn_foe(def, 150.0, 0.0, None);
                        sim.advance_to(20.0);
                        let log = rec.borrow();
                        let target = &log.enemies[0];
                        let first = target.fates.first().expect("target reached the sword");
                        let last = target
                            .fates
                            .iter()
                            .find(|f| f.lethal)
                            .expect("approaching target dies");
                        eprintln!(
                            "{bpm},{kind},{radius},{phase},{:.3},{:.3},{:.9},{},{}",
                            first.x.hypot(first.y),
                            last.x.hypot(last.y),
                            last.w,
                            target.fates.len(),
                            log.bites.len()
                        );
                    }
                }
            }
        }
    }

    fn garlic_measurement(
        bpm: f64,
        rank: u8,
    ) -> (
        Rc<std::cell::RefCell<crate::recorder::LogRecorder>>,
        Sim,
        usize,
    ) {
        let (rec, mut sim) =
            sword_measurement_base(bpm, 1, crate::survivors::StatDelta::default(), false);
        sim.st.survivor.as_mut().unwrap().arsenal =
            crate::survivors::Arsenal::measurement_weapon(crate::survivors::WeaponId::Garlic, rank);
        let def = sim
            .env
            .input
            .lib
            .enemies
            .get_index_of("vs-skeleton")
            .unwrap();
        let data = &mut Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def];
        data.speed = 0.0;
        let stats = data.survivors.as_mut().unwrap();
        stats.speed = 0.0;
        stats.xp = 0.0;
        (rec, sim, def)
    }
    fn wand_measurement(
        rank: u8,
    ) -> (
        Rc<std::cell::RefCell<crate::recorder::LogRecorder>>,
        Sim,
        usize,
    ) {
        wand_measurement_at(120.0, rank)
    }
    fn wand_measurement_at(
        bpm: f64,
        rank: u8,
    ) -> (
        Rc<std::cell::RefCell<crate::recorder::LogRecorder>>,
        Sim,
        usize,
    ) {
        let (rec, mut sim, def) = garlic_measurement(bpm, 1);
        sim.st.survivor.as_mut().unwrap().arsenal = crate::survivors::Arsenal::measurement_weapon(
            crate::survivors::WeaponId::MagicWand,
            rank,
        );
        Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def]
            .survivors
            .as_mut()
            .unwrap()
            .knockback = 0.0;
        (rec, sim, def)
    }
    fn bible_measurement(
        bpm: f64,
        rank: u8,
    ) -> (
        Rc<std::cell::RefCell<crate::recorder::LogRecorder>>,
        Sim,
        usize,
    ) {
        let (rec, mut sim, def) = wand_measurement_at(bpm, 1);
        sim.st.survivor.as_mut().unwrap().arsenal = crate::survivors::Arsenal::measurement_weapon(
            crate::survivors::WeaponId::KingBible,
            rank,
        );
        (rec, sim, def)
    }
    fn lightning_measurement(
        rank: u8,
    ) -> (
        Rc<std::cell::RefCell<crate::recorder::LogRecorder>>,
        Sim,
        usize,
    ) {
        lightning_measurement_at(120.0, rank)
    }
    fn lightning_measurement_at(
        bpm: f64,
        rank: u8,
    ) -> (
        Rc<std::cell::RefCell<crate::recorder::LogRecorder>>,
        Sim,
        usize,
    ) {
        let (rec, mut sim, def) = wand_measurement_at(bpm, 1);
        sim.st.survivor.as_mut().unwrap().arsenal = crate::survivors::Arsenal::measurement_weapon(
            crate::survivors::WeaponId::LightningRing,
            rank,
        );
        (rec, sim, def)
    }
    fn lightning_target(sim: &mut Sim, def: usize, x: f64) -> usize {
        let fi = sim.spawn_foe(def, x, 0.0, Some(10_000.0));
        sim.st.foes[fi].has_at = true;
        sim.st.foes[fi].slot_until = f64::INFINITY;
        fi
    }
    #[test]
    fn lightning_source_ranks_apply_area_damage_once_per_strike_on_sixteenths() {
        for (rank, amount, power, area) in [(1, 2, 1.5, 1.0), (8, 6, 6.5, 4.0)] {
            let (rec, mut sim, def) = lightning_measurement(rank);
            lightning_target(&mut sim, def, 120.0);
            lightning_target(&mut sim, def, 126.0);
            sim.advance_to((amount - 1) as f64 * 0.125 + 0.01);
            let r = rec.borrow();
            assert_eq!(r.lightning_strikes.len(), amount);
            for (i, strike) in r.lightning_strikes.iter().enumerate() {
                assert!((strike.song - i as f64 * 0.125).abs() < 1e-9);
                assert!(
                    (strike.radius - 0.08 * crate::survivors::PX_PER_VS_UNIT * area).abs() < 1e-9
                );
            }
            for enemy in &r.enemies {
                assert_eq!(
                    enemy.fates.len(),
                    amount,
                    "the nearby second enemy receives AoE too"
                );
                assert!(enemy.fates.iter().all(|f| (f.damage - power).abs() < 1e-9));
            }
            eprintln!(
                "Lightning rank{rank}: {amount} strikes, power{power}, area{area}, radius{:.9}, times{:?}, both nearby targets take{amount} hits",
                r.lightning_strikes[0].radius,
                r.lightning_strikes
                    .iter()
                    .map(|s| s.song)
                    .collect::<Vec<_>>()
            );
        }
    }
    #[test]
    fn lightning_targets_are_visible_and_chosen_at_each_actual_strike() {
        let (rec, mut sim, def) = lightning_measurement(1);
        lightning_target(&mut sim, def, 1500.0);
        sim.advance_to(0.14);
        assert!(
            rec.borrow().lightning_strikes.is_empty(),
            "offscreen-only casts create no bolt or strike cue"
        );
        let (rec, mut sim, def) = lightning_measurement(1);
        sim.advance_to(0.1);
        let fi = lightning_target(&mut sim, def, 120.0);
        sim.advance_to(0.14);
        assert_eq!(
            rec.borrow().lightning_strikes.len(),
            1,
            "a target entering after the cast is selected at strike time"
        );
        assert!((rec.borrow().lightning_strikes[0].song - 0.125).abs() < 1e-9);
        assert_eq!(rec.borrow().enemies[0].fates.len(), 1);
        assert!(sim.st.foes[fi].alive);
        let (rec, mut sim, def) = lightning_measurement(1);
        let fi = lightning_target(&mut sim, def, 120.0);
        sim.advance_to(0.06);
        sim.st.foes[fi].x = 300.0;
        sim.advance_to(0.14);
        let r = rec.borrow();
        assert!((r.lightning_strikes[0].x - 120.0).abs() < 1e-8);
        assert!(
            (r.lightning_strikes[1].x - 300.0).abs() < 1e-8,
            "later strike never reuses a stale cast position"
        );
        eprintln!(
            "Lightning visibility: no strikes with only offscreen targets; later entry collected at0.125s; moved target positions120 then300"
        );
    }
    #[test]
    fn lightning_burst_holds_through_fractional_freeze_and_ignores_generic_duration_speed_and_hit_delay()
     {
        let (rec, mut sim, def) = lightning_measurement(8);
        lightning_target(&mut sim, def, 120.0);
        sim.freeze_at(0.203, crate::survivors::FreezeReason::Paused);
        sim.advance_to(2.0);
        assert_eq!(rec.borrow().lightning_strikes.len(), 2);
        key(&mut sim, 2.1, KeyEventKind::Press, InputKey::Pause);
        key(&mut sim, 2.11, KeyEventKind::Release, InputKey::Pause);
        sim.advance_to(3.2);
        let r = rec.borrow();
        assert_eq!(r.lightning_strikes.len(), 6);
        assert!(
            r.lightning_strikes
                .iter()
                .skip(2)
                .all(|s| s.song >= 2.5 && (s.song * 8.0 - (s.song * 8.0).round()).abs() < 1e-8)
        );
        eprintln!(
            "Lightning fractional freeze preserves all strikes: {:?}",
            r.lightning_strikes
                .iter()
                .map(|s| s.song)
                .collect::<Vec<_>>()
        );
        drop(r);
        let (rec, mut sim, def) = lightning_measurement(8);
        lightning_target(&mut sim, def, 120.0);
        sim.apply_survivor_stats(crate::survivors::StatDelta {
            amount: 2.0,
            might: 0.5,
            area: 0.5,
            cooldown: -0.4,
            duration: -0.999,
            projectile_speed: -1.0,
            ..Default::default()
        });
        sim.advance_to(1.0);
        let r = rec.borrow();
        assert_eq!(r.lightning_strikes.len(), 8);
        assert_eq!(
            r.enemies[0].fates.len(),
            8,
            "generic one-second target delay is per projectile, not across the volley"
        );
        assert!(
            r.enemies[0]
                .fates
                .iter()
                .all(|f| (f.damage - 9.75).abs() < 1e-9)
        );
        assert!(
            (r.lightning_strikes[0].radius - 0.48 * crate::survivors::PX_PER_VS_UNIT).abs() < 1e-9
        );
        drop(r);
        sim.advance_to(3.01);
        let r = rec.borrow();
        assert_eq!(r.weapon_shots.len(), 2);
        assert!((r.weapon_shots[1].ready_run - 2.7).abs() < 1e-9);
        assert!((r.weapon_shots[1].song - 3.0).abs() < 1e-9);
        eprintln!(
            "Lightning bonuses: Amount8, damage9.75, Area6, radius{:.9}; duration1ms and Speed0 do not remove strikes; raw2.7s volley restarts at beat3.0",
            r.lightning_strikes[0].radius
        );
    }
    #[test]
    fn lightning_grids_follow_three_tempos_and_a_newly_committed_grid_replaces_a_free_draft() {
        for bpm in [70.0, 128.0, 174.0] {
            let (rec, mut sim, def) = lightning_measurement_at(bpm, 8);
            lightning_target(&mut sim, def, 120.0);
            let period = 60.0 / bpm;
            sim.advance_to(period * 1.25 + 0.01);
            let r = rec.borrow();
            assert_eq!(r.lightning_strikes.len(), 6);
            for (i, s) in r.lightning_strikes.iter().enumerate() {
                assert!((s.song - i as f64 * period / 4.0).abs() < 1e-9);
            }
            eprintln!(
                "Lightning {bpm} BPM: {:?}",
                r.lightning_strikes
                    .iter()
                    .map(|s| s.song)
                    .collect::<Vec<_>>()
            );
        }
        let (rec, mut sim, def) = lightning_measurement(8);
        sim.log = Rc::new(StreamLog {
            beats: vec![
                Beat {
                    t: 0.0,
                    known_at: 0.0,
                    period: 0.6,
                    evidence: 1.0,
                },
                Beat {
                    t: 0.6,
                    known_at: 0.1,
                    period: 0.6,
                    evidence: 1.0,
                },
                Beat {
                    t: 1.2,
                    known_at: 0.6,
                    period: 0.6,
                    evidence: 1.0,
                },
            ],
            ..StreamLog::default()
        });
        sim.st = super::fresh_state(&sim.env);
        sim.init();
        sim.st.survivor.as_mut().unwrap().arsenal = crate::survivors::Arsenal::measurement_weapon(
            crate::survivors::WeaponId::LightningRing,
            8,
        );
        lightning_target(&mut sim, def, 120.0);
        sim.advance_to(0.8);
        let r = rec.borrow();
        let times: Vec<_> = r.lightning_strikes.iter().map(|s| s.song).collect();
        let free = sim.env.rule.free_period_s / 4.0;
        let expected = [0.0, free, 0.3, 0.45, 0.6, 0.75];
        assert_eq!(times.len(), expected.len());
        for (s, t) in times.iter().zip(expected) {
            assert!((s - t).abs() < 1e-9, "{times:?}");
        }
        eprintln!(
            "Lightning late-known grid: {times:?}; first free strike before the bounding beat is known, later draft retimed causally to committed sixteenths"
        );
    }

    #[test]
    fn lightning_seeded_target_choices_repeat_across_frame_cuts_and_change_with_seed() {
        let run = |seed: f64, chunks: bool| {
            let (rec, mut sim, def) = lightning_measurement(8);
            Rc::get_mut(&mut sim.env).unwrap().input.seed = seed;
            for x in [-200.0, 120.0, 280.0] {
                lightning_target(&mut sim, def, x);
            }
            if chunks {
                for i in 1..=2000 {
                    sim.advance_to(f64::from(i) * 0.01);
                }
            } else {
                sim.advance_to(20.0);
            }
            rec.borrow().lightning_strikes.clone()
        };
        let a = run(2026.0, false);
        let b = run(2026.0, true);
        let c = run(2027.0, false);
        assert_eq!(a, b);
        assert_ne!(
            a.iter().map(|s| s.x).collect::<Vec<_>>(),
            c.iter().map(|s| s.x).collect::<Vec<_>>()
        );
        assert_eq!(a.len(), 30);
        let mut counts = std::collections::BTreeMap::new();
        for s in a {
            *counts.entry(s.x as i32).or_insert(0) += 1;
        }
        eprintln!(
            "Lightning deterministic20s: 30 actual strikes across3 visible enemies, seed2026 counts{counts:?}, frame cuts identical; seed2027 changes target order"
        );
    }

    #[test]
    fn bible_source_damage_amount_lifetime_and_periodic_hit_memory() {
        for (rank, amount, power, duration, interval) in
            [(1, 1, 1.0, 3.0, 6.0), (8, 4, 3.0, 4.0, 7.0)]
        {
            let (rec, mut sim, def) = bible_measurement(120.0, rank);
            let foe = sim.spawn_foe(def, 0.0, 0.0, Some(1000.0));
            sim.st.foes[foe].r = 200.0;
            sim.st.foes[foe].has_at = true;
            sim.st.foes[foe].slot_until = f64::INFINITY; // Pin the giant test collider against hero separation.
            sim.advance_to(duration + 0.49);
            let r = rec.borrow();
            let hits = &r.enemies[0].fates;
            let windows = if rank == 1 { 2 } else { 3 };
            assert_eq!(
                hits.len(),
                amount * windows,
                "rank{rank}, world{}, fates{hits:?}, pose{:?}, foe{:?}",
                sim.w(),
                r.missiles.last(),
                sim.pos(foe, sim.w())
            );
            eprintln!(
                "Bible rank{rank} hit windows: {:?}",
                hits.iter().map(|f| (f.w, f.damage)).collect::<Vec<_>>()
            );
            for (window, chunk) in hits.chunks(amount).enumerate() {
                assert!(chunk.iter().all(|f| (if window == 0 {
                    (0.0..=0.5).contains(&f.w)
                } else {
                    (f.w - window as f64 * 1.7).abs() < 0.011
                }) && (f.damage - power).abs() < 1e-9));
            }
            let poses: Vec<_> = r
                .missiles
                .iter()
                .filter(|p| p.w >= 0.5 && p.w <= 0.55)
                .collect();
            assert!(!poses.is_empty());
            let area = if rank == 1 { 1.0 } else { 1.5 };
            for p in poses {
                let omega = if rank == 1 {
                    2.1875
                } else {
                    std::f64::consts::PI
                };
                assert!(
                    (p.vx.hypot(p.vy) / p.x.hypot(p.y) - omega).abs() < 1e-9,
                    "actual orbit uses the selected source/cardinal speed"
                );
                assert!(
                    (p.x.hypot(p.y) - 0.5 * area * crate::survivors::PX_PER_VS_UNIT).abs() < 1e-8
                );
                assert!(
                    (p.radius - 0.08 * crate::survivors::PX_PER_VS_UNIT * (1.0 + 0.3 * area)).abs()
                        < 1e-8
                );
            }
            eprintln!(
                "Bible rank{rank}: {amount} blades, damage{power}, active{duration}s, periodic1.7s windows{windows}, hits{}, raw cycle{interval}s",
                hits.len()
            );
            drop(r);
            sim.advance_to(interval + 0.01);
            assert_eq!(rec.borrow().weapon_shots.len(), 2);
            assert!((rec.borrow().weapon_shots[1].song - interval).abs() < 1e-8);
        }
        let (rec, mut sim, def) = bible_measurement(120.0, 1);
        sim.advance_to(1.6);
        let foe = sim.spawn_foe(def, 0.0, 0.0, Some(1000.0));
        sim.st.foes[foe].r = 200.0;
        sim.st.foes[foe].has_at = true;
        sim.st.foes[foe].slot_until = f64::INFINITY;
        sim.advance_to(1.69);
        assert_eq!(rec.borrow().enemies[0].fates.len(), 1);
        sim.advance_to(1.71);
        assert_eq!(
            rec.borrow().enemies[0].fates.len(),
            2,
            "memory clears at the cycle's 1.7s boundary, not 1.7s after late contact"
        );
    }
    #[test]
    fn bible_lethal_contact_frees_the_population_slot_in_the_same_step() {
        let (rec, mut sim, def) = bible_measurement(120.0, 1);
        let radius = 0.5 * crate::survivors::PX_PER_VS_UNIT;
        let foe = sim.spawn_foe(def, radius, 0.0, Some(0.5));
        sim.advance_to(0.01);
        assert!(rec.borrow().enemies[0].fates.iter().any(|f| f.lethal));
        assert!(!sim.st.foes[foe].alive);
        assert!(
            !sim.st.alive.contains(&foe),
            "a Bible kill cannot occupy the alive population until the following weapon tick"
        );
    }

    #[test]
    fn bible_bonuses_radius_cap_and_freeze_follow_active_world_time() {
        let (rec, mut sim, _) = bible_measurement(120.0, 8);
        sim.apply_survivor_stats(crate::survivors::StatDelta {
            might: 0.5,
            amount: 2.0,
            area: 9.0,
            projectile_speed: 2.0,
            duration: 0.5,
            cooldown: -0.4,
            ..Default::default()
        });
        sim.advance_to(0.6);
        let shot = rec.borrow().weapon_shots[0];
        assert_eq!(shot.amount, 6);
        assert!((shot.damage - 4.5).abs() < 1e-9);
        assert!((shot.duration_s - 6.0).abs() < 1e-9);
        let p = *rec.borrow().missiles.last().unwrap();
        let radius = 2.25 * crate::survivors::PX_PER_VS_UNIT;
        assert!(
            (p.x.hypot(p.y) - radius).abs() < 1e-8,
            "orbit caps Area at4.5"
        );
        assert!((p.vx.hypot(p.vy) - radius * 10.5).abs() < 1e-8);
        sim.freeze_at(0.613, crate::survivors::FreezeReason::Paused);
        sim.advance_to(0.62);
        let held = rec.borrow().missiles.clone();
        sim.advance_to(2.0);
        assert_eq!(rec.borrow().missiles, held);
        key(&mut sim, 2.1, KeyEventKind::Press, InputKey::Pause);
        key(&mut sim, 2.11, KeyEventKind::Release, InputKey::Pause);
        sim.advance_to(3.0);
        assert_eq!(
            sim.st.bibles.len(),
            1,
            "song pause did not consume six-second duration"
        );
        let offset = sim.s() - sim.w();
        sim.advance_to(offset + 6.6);
        assert!(sim.st.bibles.is_empty());
        assert!(
            !rec.borrow().missiles.last().unwrap().alive,
            "view receives cleanup after the half-second visual fade"
        );
        eprintln!(
            "Bible bonuses: damage4.5, Amount6, active6s, Area15 orbit capped2.25U, speed10.5rad/s; fractional freeze holds samples and world lifetime"
        );
    }

    #[test]
    fn wand_source_rows_amount_and_burst_cadence() {
        for (rank, amount, power, pierce) in [(1, 1, 1.0, 1.0), (8, 4, 3.0, 2.0)] {
            let (rec, mut sim, def) = wand_measurement(rank);
            sim.spawn_foe(def, 100.0, 0.0, Some(1000.0));
            sim.advance_to(0.95);
            let r = rec.borrow();
            assert_eq!(r.weapon_shots.len(), amount);
            assert_eq!(r.enemies[0].fates.len(), amount);
            assert!(
                r.enemies[0]
                    .fates
                    .iter()
                    .all(|f| (f.damage - power).abs() < 1e-9)
            );
            assert!(r.weapon_shots.iter().all(|s| s.amount == 1
                && s.pierce == pierce
                && (s.radius - 0.08 * crate::survivors::PX_PER_VS_UNIT).abs() < 1e-9));
            let fires: Vec<_> = r.weapon_shots.iter().map(|s| s.song).collect();
            assert_eq!(
                fires,
                if rank == 1 {
                    vec![0.0]
                } else {
                    vec![0.0, 0.1, 0.2, 0.3]
                }
            );
            let initial = r.missiles[0];
            assert!(
                (initial.vx.hypot(initial.vy) - 1.65 * crate::survivors::PX_PER_VS_UNIT).abs()
                    < 1e-9
            );
            eprintln!(
                "Wand rank{rank}: amount{amount} power{power} pierce{pierce}, fires{fires:?}, target hits{}, radius{:.6}, speed{:.6}",
                r.enemies[0].fates.len(),
                initial.radius,
                initial.vx.hypot(initial.vy)
            );
        }
        let (rec, mut sim, _) = wand_measurement(8);
        sim.log = Rc::new(StreamLog::default());
        sim.st = super::fresh_state(&sim.env);
        sim.init();
        sim.st.survivor.as_mut().unwrap().arsenal =
            crate::survivors::Arsenal::measurement_weapon(crate::survivors::WeaponId::MagicWand, 8);
        sim.advance_to(0.5);
        let fires: Vec<_> = rec.borrow().weapon_shots.iter().map(|s| s.song).collect();
        assert_eq!(fires.len(), 4);
        for (i, t) in fires.iter().enumerate() {
            assert!((*t - i as f64 * 0.1).abs() < 1e-9);
        }
        eprintln!("Wand rank8 without grid: raw100ms burst {fires:?}");
    }
    #[test]
    fn wand_swept_collision_orders_targets_and_spends_pierce_only_on_survivors() {
        for near_health in [0.5, 100.0] {
            let (rec, mut sim, def) = wand_measurement(1);
            // Far target created first: collision order must follow distance, not arena ID.
            sim.spawn_foe(def, 160.0, 0.0, Some(100.0));
            sim.spawn_foe(def, 80.0, 0.0, Some(near_health));
            sim.advance_to(0.001);
            // Controlled long integration step stresses one swept segment crossing
            // both bodies; ordinary Stats clamps would hide a fake high-speed test.
            sim.st.w += 1.0;
            sim.tick_missiles();
            let r = rec.borrow();
            assert_eq!(r.enemies[1].fates.len(), 1);
            assert_eq!(r.enemies[0].fates.len(), usize::from(near_health < 1.0));
        }
        let (rec, mut sim, def) = wand_measurement(8);
        for x in [80.0, 130.0, 180.0] {
            sim.spawn_foe(def, x, 0.0, Some(100.0));
        }
        sim.advance_to(0.01);
        sim.st.survivor.as_mut().unwrap().arsenal = crate::survivors::Arsenal::default(); // isolate one missile
        sim.st.q.remove(&|e| matches!(e, Ev::WandBullet(_))); // isolate this one travelling missile
        sim.advance_to(0.9);
        let r = rec.borrow();
        assert_eq!(
            r.enemies.iter().map(|f| f.fates.len()).collect::<Vec<_>>(),
            [1, 1, 0]
        );
        eprintln!(
            "rank8 one missile: exactly2 surviving targets, third untouched; lethal overlaps preserve rank1 pierce; long-step swept hits are ordered near-first"
        );
    }
    #[test]
    fn wand_spent_head_waits_for_its_grid_aligned_impact() {
        let (rec, mut sim, def) = wand_measurement(1);
        sim.apply_survivor_stats(crate::survivors::StatDelta {
            projectile_speed: 30.0,
            ..Default::default()
        });
        sim.spawn_foe(def, 80.0, 0.0, Some(100.0));
        sim.advance_to(0.1);
        {
            let r = rec.borrow();
            assert_eq!(r.enemies[0].fates.len(), 1);
            let head = r.missiles.last().unwrap();
            assert!(
                head.alive && head.vx == 0.0 && head.vy == 0.0,
                "spent head holds before the next sixteenth"
            );
            assert!(
                head.x < 80.0,
                "stops at swept contact instead of overshooting the target"
            );
            assert!(!r.missiles.iter().any(|p| !p.alive));
        }
        sim.advance_to(0.126);
        assert!(
            !rec.borrow().missiles.last().unwrap().alive,
            "head removed on the impact sixteenth"
        );
    }

    #[test]
    fn wand_turns_back_after_target_hit_memory_expires_and_respects_stat_bonuses() {
        let (rec, mut sim, def) = wand_measurement(8);
        sim.spawn_foe(def, 80.0, 0.0, Some(1000.0));
        sim.advance_to(0.01);
        sim.st.survivor.as_mut().unwrap().arsenal = crate::survivors::Arsenal::default();
        sim.st.q.remove(&|e| matches!(e, Ev::WandBullet(_))); // isolate this one travelling missile
        sim.advance_to(1.0);
        assert_eq!(
            rec.borrow().enemies[0].fates.len(),
            1,
            "per-projectile1000ms target memory"
        );
        sim.advance_to(2.8);
        let r = rec.borrow();
        assert_eq!(
            r.enemies[0].fates.len(),
            2,
            "homing must turn180degrees when the original target is behind"
        );
        assert!(r.enemies[0].fates[1].w - r.enemies[0].fates[0].w >= 1.0);
        drop(r);
        let (rec, mut sim, def) = wand_measurement(1);
        sim.apply_survivor_stats(crate::survivors::StatDelta {
            might: 0.5,
            projectile_speed: 0.5,
            area: 0.5,
            amount: 2.0,
            cooldown: -0.4,
            ..Default::default()
        });
        sim.spawn_foe(def, 100.0, 0.0, Some(1000.0));
        sim.advance_to(0.65);
        let r = rec.borrow();
        assert_eq!(r.weapon_shots.len(), 3);
        assert_eq!(r.enemies[0].fates.len(), 3);
        assert!(
            r.enemies[0]
                .fates
                .iter()
                .all(|f| (f.damage - 1.5).abs() < 1e-9)
        );
        assert!(
            (r.missiles[0].radius - 0.08 * crate::survivors::PX_PER_VS_UNIT * 1.5).abs() < 1e-9
        );
        assert!(
            (r.missiles[0].vx.hypot(r.missiles[0].vy)
                - 1.65 * crate::survivors::PX_PER_VS_UNIT * 1.5)
                .abs()
                < 1e-9
        );
    }

    #[test]
    fn wand_homes_on_current_targets_holds_in_freeze_and_culls_without_a_generic_lifespan() {
        use crate::survivors::FreezeReason;
        let (rec, mut sim, def) = wand_measurement(1);
        let target = sim.spawn_foe(def, 300.0, 0.0, Some(100.0));
        sim.advance_to(0.05);
        sim.st.foes[target].y = 100.0;
        sim.advance_to(0.2);
        assert!(
            rec.borrow().missiles.iter().any(|p| p.vy > 1.0),
            "homes after target moves"
        );
        sim.freeze_at(0.21, FreezeReason::Paused);
        sim.advance_to(0.22);
        let held = rec.borrow().missiles.last().copied().unwrap();
        sim.advance_to(2.0);
        assert_eq!(
            rec.borrow().missiles.last(),
            Some(&held),
            "no samples/motion while frozen"
        );
        let (rec, mut sim, _) = wand_measurement(1);
        sim.advance_to(0.01);
        sim.st.survivor.as_mut().unwrap().arsenal = crate::survivors::Arsenal::default();
        sim.st.q.remove(&|e| matches!(e, Ev::WandBullet(_))); // isolate this one travelling missile
        sim.advance_to(1.1);
        assert!(
            rec.borrow().missiles.last().unwrap().alive,
            "generic1000ms duration is not a lifespan"
        );
        sim.advance_to(6.0);
        assert!(
            !rec.borrow().missiles.last().unwrap().alive,
            "finite view/travel culling"
        );
    }

    #[test]
    fn wand_zero_speed_projectiles_still_have_a_finite_life() {
        let (rec, mut sim, _) = wand_measurement(1);
        sim.apply_survivor_stats(crate::survivors::StatDelta {
            projectile_speed: -1.0,
            ..Default::default()
        });
        sim.advance_to(0.001);
        sim.st.survivor.as_mut().unwrap().arsenal = crate::survivors::Arsenal::default();
        sim.st.q.remove(&|e| matches!(e, Ev::WandBullet(_)));
        sim.advance_to(13.0);
        let r = rec.borrow();
        assert_eq!(r.missiles[0].vx, 0.0);
        assert!(
            !r.missiles.last().unwrap().alive,
            "zero speed cannot evade the active-world-time cull"
        );
    }

    #[test]
    fn wand_max_burst_throughput_stays_bounded_at_three_tempos_and_holds_mid_burst() {
        for bpm in [70.0, 128.0, 174.0] {
            let (rec, mut sim, _) = wand_measurement_at(bpm, 8);
            sim.apply_survivor_stats(crate::survivors::StatDelta {
                amount: 2.0,
                cooldown: -0.4,
                ..Default::default()
            });
            sim.advance_to(40.0);
            let fired = rec.borrow().weapon_shots.len();
            assert!(
                (388..=406).contains(&fired),
                "{bpm}: expected400 source bullets with at most two bursts of boundary delay, got{fired}"
            );
            let pending = sim
                .st
                .q
                .heap
                .iter()
                .filter(|e| matches!(e.ev, Ev::WandBullet(_)))
                .count();
            assert!(pending <= 12, "no growing sixteenth-serialization backlog");
            eprintln!(
                "{bpm}BPM rank8 +Amount2/Cooldown0.6: {fired}/40s vs400 source bullets, {pending} burst bullets pending"
            );
        }
        let (rec, mut sim, _) = wand_measurement(8);
        sim.freeze_at(0.15, crate::survivors::FreezeReason::Paused);
        sim.advance_to(2.0);
        assert_eq!(rec.borrow().weapon_shots.len(), 2);
        key(&mut sim, 2.1, KeyEventKind::Press, InputKey::Pause);
        key(&mut sim, 2.11, KeyEventKind::Release, InputKey::Pause);
        sim.advance_to(2.9);
        let r = rec.borrow();
        let resume = r
            .run_states
            .iter()
            .find(|r| r.song > 2.0 && r.frozen.is_none())
            .unwrap()
            .song;
        assert!((r.weapon_shots[2].song - resume - 0.05).abs() < 1e-8);
        assert!((r.weapon_shots[3].song - r.weapon_shots[2].song - 0.1).abs() < 1e-8);
        eprintln!(
            "mid-burst freeze: resume{resume:.6}, remaining bullets {:.6},{:.6} preserve50ms then100ms",
            r.weapon_shots[2].song, r.weapon_shots[3].song
        );
    }

    #[test]
    fn garlic_source_rows_and_persistent_target_cadence() {
        for (rank, power, area, interval) in [(1, 0.5, 1.0, 1.3), (8, 1.5, 2.0, 1.0)] {
            let (rec, mut sim, def) = garlic_measurement(120.0, rank);
            let radius = 0.32 * crate::survivors::PX_PER_VS_UNIT * area;
            sim.spawn_foe(def, 20.0, 0.0, Some(1000.0));
            sim.spawn_foe(def, 0.0, 25.0, Some(1000.0));
            sim.spawn_foe(def, radius + 50.0, 0.0, Some(1000.0));
            sim.advance_to(0.2);
            sim.spawn_foe(def, 30.0, 0.0, Some(1000.0)); // enters an already existing aura
            sim.advance_to(0.9);
            {
                let r = rec.borrow();
                for i in [0, 1, 3] {
                    assert_eq!(r.enemies[i].fates.len(), 1);
                    assert!((r.enemies[i].fates[0].damage - power).abs() < 1e-9);
                }
                assert!(r.enemies[2].fates.is_empty());
                let shot = r.weapon_shots[0];
                assert!((shot.radius - radius).abs() < 1e-9);
                assert_eq!(shot.amount, 1);
                assert_eq!(shot.knockback, 0.0);
            }
            sim.advance_to(1.51);
            let r = rec.borrow();
            assert_eq!(r.weapon_shots.len(), 2);
            assert!((r.weapon_shots[1].ready_run - interval).abs() < 1e-9);
            assert_eq!(r.weapon_shots[1].song, if rank == 1 { 1.5 } else { 1.0 });
            for i in [0, 1, 3] {
                assert_eq!(r.enemies[i].fates.len(), 2);
            }
            eprintln!(
                "Garlic rank{rank}: power{power} area{area} radius{radius:.6} raw interval{interval}s, shots {:?}, per-target hits2 (including mid-cycle entry), outside0",
                r.weapon_shots.iter().map(|s| s.song).collect::<Vec<_>>()
            );
        }
        let (rec, mut sim, def) = garlic_measurement(174.0, 8);
        sim.apply_survivor_stats(crate::survivors::StatDelta {
            cooldown: -0.4,
            ..Default::default()
        });
        sim.spawn_foe(def, 20.0, 0.0, Some(1000.0));
        sim.advance_to(1.4);
        let r = rec.borrow();
        assert_eq!(
            r.enemies[0].fates.len(),
            3,
            "1000ms generic hitDelay must not throttle aura incarnations"
        );
        assert!((r.weapon_shots[1].song - 120.0 / 174.0).abs() < 1e-9);
        eprintln!(
            "Garlic rank8 cooldown0.6 at174BPM: {:?} hits (generic1000ms delay ignored)",
            r.enemies[0].fates.iter().map(|f| f.w).collect::<Vec<_>>()
        );
    }
    #[test]
    fn garlic_persistent_aura_moves_with_the_hero_between_pulses() {
        let (rec, mut sim, def) = garlic_measurement(120.0, 1);
        sim.spawn_foe(def, 80.0, 0.0, Some(1000.0));
        sim.advance_to(0.1);
        assert!(rec.borrow().enemies[0].fates.is_empty());
        sim.key(KeyEvent {
            s: 0.15,
            key: InputKey::Right,
            kind: KeyEventKind::Press,
            id: None,
        });
        sim.advance_to(0.9);
        let r = rec.borrow();
        assert_eq!(r.weapon_shots.len(), 1, "no second aura incarnation yet");
        assert_eq!(
            r.enemies[0].fates.len(),
            1,
            "existing aura follows hero into target"
        );
        assert!(r.enemies[0].fates[0].w > 0.1);
    }

    #[test]
    fn garlic_debuff_obeys_source_eligibility_and_increases_later_recoil() {
        for (start, resistance, after) in [
            (0.0, 0.0, 0.3),
            (2.9, 0.0, 3.2),
            (3.0, 0.0, 3.0),
            (0.0, 1.0, 0.0),
        ] {
            let (_, mut sim, def) = garlic_measurement(120.0, 1);
            let stats = Rc::get_mut(&mut sim.env).unwrap().input.lib.enemies[def]
                .survivors
                .as_mut()
                .unwrap();
            stats.knockback = start;
            stats.max_knockback = 3.0;
            stats.res_debuffs = resistance;
            let fi = sim.spawn_foe(def, 20.0, 0.0, Some(1000.0));
            sim.advance_to(0.1);
            assert!((sim.st.foes[fi].knockback - after).abs() < 1e-9);
            assert!(
                sim.st.foes[fi].recoil_until.is_infinite(),
                "Garlic own knockback is zero"
            );
            let hit = sim.add_hit(
                super::combat::HitSpec {
                    source: crate::recorder::HitSource::Weapon,
                    what: "sword".into(),
                    weight: crate::recorder::Weight::Light,
                    primary: true,
                    shape: crate::core::geometry::Shape::Circle {
                        x: 0.0,
                        y: 0.0,
                        radius: 100.0,
                    },
                    dir: 0.0,
                    accent: 0.0,
                    damage: 0.1,
                    group: None,
                },
                None,
            );
            sim.damage(fi, &hit);
            sim.advance_to(0.13);
            assert_eq!(sim.st.foes[fi].recoil_until.is_finite(), after > 0.0);
        }
    }

    #[test]
    fn survivor_sword_actual_damage_matches_whip_dps_at_three_tempos() {
        use crate::survivors::{StatDelta, WeaponId, weapon_catalog};
        for bpm in [70.0, 128.0, 174.0] {
            for (rank, delta) in [
                (1, StatDelta::default()),
                (
                    8,
                    StatDelta {
                        might: 0.5,
                        amount: 2.0,
                        area: 0.5,
                        cooldown: -0.4,
                        ..StatDelta::default()
                    },
                ),
            ] {
                let (rec, mut sim) = sword_measurement(bpm, rank, delta, false);
                sim.advance_to(60.0);
                let rec = rec.borrow();
                let damage: f64 = rec.enemies[0].fates.iter().map(|f| f.damage).sum();
                let weapon = weapon_catalog().get(WeaponId::Sword).level(rank);
                let stats = sim.survivor_progress().unwrap().stats.finalized();
                let expected =
                    weapon.power * weapon.amount(stats) as f64 / 1.35 * stats.might * 60.0;
                assert!(
                    (damage / expected - 1.0).abs() < 0.01,
                    "{bpm} rank{rank}: {damage} vs {expected}"
                );
                assert_eq!(rec.enemies[0].fates.len(), rec.weapon_shots.len());
                let radius = crate::survivors::SWORD_BASE_RADIUS * weapon.area * stats.area;
                assert!(
                    rec.weapon_shots
                        .iter()
                        .all(|s| (s.radius - radius).abs() < 1e-9)
                );
                assert!(rec.swings.is_empty());
                assert!(rec.cleaves.is_empty());
                assert!(
                    rec.weapon_shots
                        .iter()
                        .all(|s| s.id == WeaponId::Sword && s.amount == 1)
                );
                eprintln!(
                    "{bpm} BPM sword rank{rank}: {} actual target hits, {:.9} damage/60s, expected{expected:.9}, radius{radius:.3}",
                    rec.enemies[0].fates.len(),
                    damage
                );
            }
        }
    }

    #[test]
    fn survivor_accent_shots_are_marked_without_changing_damage() {
        let (plain, mut a) =
            sword_measurement(128.0, 1, crate::survivors::StatDelta::default(), false);
        let (accent, mut b) =
            sword_measurement(128.0, 1, crate::survivors::StatDelta::default(), true);
        a.advance_to(8.0);
        b.advance_to(8.0);
        let a = plain.borrow();
        let b = accent.borrow();
        assert!(b.weapon_shots.iter().any(|s| s.accent));
        assert!(a.weapon_shots.iter().all(|s| !s.accent));
        assert_eq!(
            a.enemies[0]
                .fates
                .iter()
                .map(|f| f.damage)
                .collect::<Vec<_>>(),
            b.enemies[0]
                .fates
                .iter()
                .map(|f| f.damage)
                .collect::<Vec<_>>()
        );
    }
    #[test]
    fn survivor_interval_framework_streaming_equals_batch_across_frame_cuts_and_freeze() {
        use crate::survivors::{Arsenal, FreezeReason, WeaponId};
        let setup = || {
            let mut sim = survivor_sim();
            let period = 60.0 / 70.0;
            let log = StreamLog {
                beats: (0..40)
                    .map(|i| {
                        let t = f64::from(i) * period;
                        Beat {
                            t,
                            known_at: (t - period).max(0.0),
                            period,
                            evidence: 1.0,
                        }
                    })
                    .collect(),
                ..StreamLog::default()
            };
            sim.log = Rc::new(log);
            sim.st = super::fresh_state(&sim.env);
            let p = sim.st.survivor.as_mut().unwrap();
            p.arsenal = Arsenal::measurement_weapon(WeaponId::HolyWand, 1);
            p.stats.cooldown = 0.6;
            let rec = Rc::new(std::cell::RefCell::new(
                crate::recorder::LogRecorder::default(),
            ));
            sim.rec = Box::new(Rc::clone(&rec));
            sim.init();
            sim.freeze_at(1.1, FreezeReason::Paused);
            key(&mut sim, 3.1, KeyEventKind::Press, InputKey::Pause);
            key(&mut sim, 3.2, KeyEventKind::Release, InputKey::Pause);
            (sim, rec)
        };
        let (mut a, ar) = setup();
        let (mut b, br) = setup();
        let full = b.log.as_ref().clone();
        a.advance_to(12.0);
        for k in 1..=325 {
            let t = (f64::from(k) * 0.037).min(12.0);
            *b.log_mut() = StreamLog {
                beats: full
                    .beats
                    .iter()
                    .copied()
                    .filter(|b| b.known_at <= t)
                    .collect(),
                ..StreamLog::default()
            };
            b.advance_to(t);
        }
        assert_eq!(format!("{:?}", ar.borrow()), format!("{:?}", br.borrow()));
        assert_eq!(a.st.survivor, b.st.survivor);
        assert!(
            ar.borrow()
                .weapon_shots
                .iter()
                .all(|s| s.song <= 1.1 || s.song >= a.st.freeze.total_s + 1.1)
        );
    }
    #[test]
    fn survivor_spawner_all_minutes_obeys_source_rosters_minimum_cap_and_scaled_hp() {
        let mut sim = survivor_sim();
        let level = load_level(
            &source("levels/survivors.yaml"),
            &sim.env.input.lib,
            &reactive_song_map(f64::INFINITY),
            None,
        )
        .unwrap();
        let schedule = level.survivor_schedule.clone();
        Rc::get_mut(&mut sim.env).unwrap().input.level = level;
        sim.st.survivor.as_mut().unwrap().level = 20;
        sim.log = Rc::new(StreamLog {
            beats: (0..3602)
                .map(|k| Beat {
                    t: f64::from(k) * 0.5,
                    known_at: 0.0,
                    period: 0.5,
                    evidence: 1.0,
                })
                .collect(),
            ..StreamLog::default()
        });
        sim.st.ingested.beats = sim.log.beats.len();
        for minute in &schedule {
            // Isolate each source row for one minute, including an empty field refill. The
            // separate clock test covers real minute transitions and inherited fields.
            sim.st.foes.clear();
            sim.st.alive.clear();
            sim.st.foe_samples.clear();
            sim.st.spawner = crate::survivors::Spawner::default();
            let mut phase = minute.clone();
            phase.minute = 0;
            Rc::get_mut(&mut sim.env)
                .unwrap()
                .input
                .level
                .survivor_schedule = vec![phase];
            let start = 0;
            let t0 = 0.0;
            for k in 0..=1199 {
                let t = t0 + f64::from(k) * 0.05;
                sim.st.s = t;
                sim.st.run_s = t;
                sim.st.w = t;
                sim.tick_spawner();
                let alive = sim
                    .st
                    .alive
                    .iter()
                    .filter(|&&i| sim.st.foes[i].alive)
                    .count();
                assert!(alive <= 300, "minute {}: {alive}", minute.minute);
                if k == 10 && !minute.enemies.as_ref().unwrap().is_empty() {
                    assert!(
                        alive >= minute.minimum.unwrap(),
                        "minute {}: {alive}",
                        minute.minute
                    );
                }
            }
            let mut counts = std::collections::BTreeMap::new();
            let mut bosses = 0;
            for f in sim.st.foes.iter().skip(start) {
                let def = &sim.env.input.lib.enemies[f.def];
                *counts.entry(def.id.as_str()).or_insert(0usize) += 1;
                let scaled = f.survivor_boss || def.survivors.unwrap().level_scaled;
                assert_eq!(f.health0, def.health * if scaled { 20.0 } else { 1.0 });
                if f.survivor_boss {
                    bosses += 1;
                    assert!(minute.bosses.contains(&def.id));
                } else {
                    assert!(minute.enemies.as_ref().unwrap().contains(&def.id));
                }
            }
            // At 30:00 admission stops for victory; no Reaper or inherited boss.
            assert_eq!(bosses, minute.bosses.len());
            for role in minute.enemies.as_ref().unwrap() {
                assert!(counts.contains_key(role.as_str()), "missing {role}");
            }
            eprintln!(
                "minute {} minimum {} alive {} bosses {} actual {:?}",
                minute.minute,
                minute.minimum.unwrap(),
                sim.st.alive.len(),
                bosses,
                counts
            );
        }
    }

    #[test]
    fn survivor_spawner_holds_grid_admissions_and_does_not_catch_up_frozen_ticks() {
        use crate::survivors::FreezeReason;
        let setup = || {
            let mut sim = survivor_sim();
            let mut level = load_level(
                &source("levels/survivors.yaml"),
                &sim.env.input.lib,
                &reactive_song_map(f64::INFINITY),
                None,
            )
            .unwrap();
            level.survivor_schedule.truncate(1);
            let env = Rc::get_mut(&mut sim.env).unwrap();
            env.input.level = level;
            env.input.no_hero_damage = true;
            sim.freeze_at(1.1, FreezeReason::Paused);
            key(&mut sim, 3.1, KeyEventKind::Press, InputKey::Pause);
            key(&mut sim, 3.2, KeyEventKind::Release, InputKey::Pause);
            sim
        };
        let mut a = setup();
        let mut b = setup();
        a.advance_to(3.0);
        let spawned = a.st.spawner.spawned;
        a.advance_to(3.05);
        assert_eq!(spawned, a.st.spawner.spawned);
        a.advance_to(6.0);
        for k in 1..=600 {
            b.advance_to(f64::from(k) / 100.0);
        }
        assert_eq!(a.st.spawner, b.st.spawner);
        assert_eq!(format!("{:?}", a.st.foes), format!("{:?}", b.st.foes));
        assert!(a.st.spawner.spawned < 25, "no frozen-time debt");
    }
    #[test]
    fn survivor_full_field_preserves_a_boss_and_recycling_keeps_its_hp_and_reward() {
        use super::combat::HitSpec;
        use crate::core::geometry::Shape;
        use crate::recorder::{HitSource, LogRecorder, Weight};
        let mut sim = survivor_sim();
        sim.log = Rc::new(StreamLog::default());
        sim.st.ingested.beats = 0;
        let rec = Rc::new(std::cell::RefCell::new(LogRecorder::default()));
        sim.rec = Box::new(Rc::clone(&rec));
        let mut level = load_level(
            &source("levels/survivors.yaml"),
            &sim.env.input.lib,
            &reactive_song_map(f64::INFINITY),
            None,
        )
        .unwrap();
        level.survivor_schedule.truncate(2);
        level.survivor_schedule[0].minimum = Some(300);
        Rc::get_mut(&mut sim.env).unwrap().input.level = level;
        sim.st.survivor.as_mut().unwrap().level = 20;
        sim.tick_spawner();
        assert_eq!(sim.st.alive.len(), 300);
        sim.st.run_s = 60.0;
        sim.st.s = 60.0;
        sim.st.w = 60.0;
        sim.tick_spawner();
        assert_eq!(sim.st.spawner.waiting_bosses[0].role, "vs-bat4");
        sim.kill(0);
        sim.tick_spawner();
        assert!(sim.st.spawner.waiting_bosses.is_empty());
        assert_eq!(
            sim.st
                .alive
                .iter()
                .filter(|&&i| sim.st.foes[i].alive)
                .count(),
            300
        );
        let boss = sim.st.foes.len() - 1;
        assert!(sim.st.foes[boss].survivor_boss);
        assert_eq!(sim.st.foes[boss].health, 100.0);
        sim.st.foes[boss].health = 42.0;
        sim.st.foes[boss].x = 1e6;
        sim.recycle();
        let boss = sim.st.foes.len() - 1;
        assert!(sim.st.foes[boss].survivor_boss);
        assert_eq!(sim.st.foes[boss].health, 42.0);
        let hit = sim.add_hit(
            HitSpec {
                source: HitSource::Weapon,
                what: "sword".into(),
                weight: Weight::Light,
                primary: true,
                shape: Shape::Circle {
                    x: 0.0,
                    y: 0.0,
                    radius: 1.0,
                },
                dir: 0.0,
                accent: 0.0,
                damage: 1000.0,
                group: None,
            },
            None,
        );
        sim.damage(boss, &hit);
        assert_eq!(
            rec.borrow().chickens.len(),
            1,
            "a scheduled boss drops exactly one chicken"
        );
    }
    #[test]
    fn survivor_card_picks_wait_for_reveal_chain_and_apply_once() {
        let mut sim = survivor_sim();
        sim.st.health = 80.0;
        let initial = sim.survivor_inventory().unwrap();
        sim.gem_arrives(0, 45.0);
        assert_eq!(sim.st.survivor.as_ref().unwrap().pending_levels, 3);
        assert_eq!(sim.run_state().offer.unwrap().level, 2);
        let ready = sim.run_state().offer.unwrap().reveals[2];
        key(
            &mut sim,
            ready - 0.02,
            KeyEventKind::Press,
            InputKey::Confirm,
        );
        key(
            &mut sim,
            ready - 0.01,
            KeyEventKind::Release,
            InputKey::Confirm,
        );
        assert_eq!(
            sim.survivor_inventory().unwrap(),
            initial,
            "early confirm applies nothing"
        );
        for level in 2..=4 {
            let offer = sim.run_state().offer.unwrap();
            assert_eq!(offer.level, level);
            let t = offer.reveals[2] + 0.01;
            key(&mut sim, t, KeyEventKind::Press, InputKey::Confirm);
            sim.advance_to(t + 0.001);
            let applied = sim.survivor_inventory().unwrap();
            assert_eq!(offer.cards[0].item.rank(applied), offer.cards[0].after);
            let total: usize = applied
                .weapons
                .iter()
                .map(|s| usize::from(s.rank))
                .sum::<usize>()
                + applied
                    .passives
                    .iter()
                    .map(|s| usize::from(s.rank))
                    .sum::<usize>();
            assert_eq!(total, level, "exactly one rank per confirmed offer");
            // A held/repeated confirm cannot select the next chained card.
            key(&mut sim, t + 0.002, KeyEventKind::Press, InputKey::Confirm);
            key(
                &mut sim,
                t + 0.003,
                KeyEventKind::Release,
                InputKey::Confirm,
            );
            assert_eq!(
                sim.survivor_inventory().unwrap(),
                applied,
                "held confirm applies no second pick"
            );
        }
        assert!(sim.run_state().offer.is_none());
        assert_eq!(sim.st.survivor.as_ref().unwrap().pending_levels, 0);
        sim.advance_to(sim.s() + 1.0);
        assert!(!sim.st.freeze.frozen);
    }
    #[test]
    fn survivor_all_passive_ranks_apply_source_values_through_real_card_picks() {
        use crate::survivors::{Item, PassiveId, total_xp};
        let cases: [(PassiveId, &[f64]); 6] = [
            (PassiveId::Spinach, &[1.1, 1.2, 1.3, 1.4, 1.5]),
            (
                PassiveId::HollowHeart,
                &[144.0, 172.8, 207.36, 248.832, 298.5984],
            ),
            (PassiveId::Pummarola, &[0.2, 0.4, 0.6, 0.8, 1.0]),
            (PassiveId::EmptyTome, &[0.92, 0.84, 0.76, 0.68, 0.6]),
            (PassiveId::Spellbinder, &[1.1, 1.2, 1.3, 1.4, 1.5]),
            (PassiveId::Duplicator, &[1.0, 2.0]),
        ];
        for (passive, expected) in cases {
            let mut sim = survivor_sim();
            max_built_survivor_weapons(&mut sim);
            // Max other owned items through their actual stat path so the real
            // offer table has only this passive left. No authored table changes.
            for (other, _) in cases {
                if other != passive {
                    for _ in 0..other.max_rank() {
                        sim.st
                            .survivor
                            .as_mut()
                            .unwrap()
                            .arsenal
                            .learn_passive(other)
                            .unwrap();
                        sim.apply_survivor_stats(other.delta());
                    }
                }
            }
            sim.st.health = 80.0;
            let baseline = sim.survivor_progress().unwrap().stats;
            assert_eq!(usize::from(passive.max_rank()), expected.len());
            sim.gem_arrives(0, total_xp(expected.len()));
            for (i, &value) in expected.iter().enumerate() {
                let offer = sim.run_state().offer.unwrap();
                assert!(offer.cards.iter().all(|c| c.item == Item::Passive(passive)));
                assert_eq!(
                    (offer.cards[0].before, offer.cards[0].after),
                    (i as u8, i as u8 + 1)
                );
                let t = offer.reveals[2] + 0.01;
                key(&mut sim, t, KeyEventKind::Press, InputKey::Confirm);
                key(
                    &mut sim,
                    t + 0.001,
                    KeyEventKind::Release,
                    InputKey::Confirm,
                );
                sim.advance_to(t + 0.002);
                let stats = sim.survivor_progress().unwrap().stats;
                let mut want = baseline;
                match passive {
                    PassiveId::Spinach => want.might = value,
                    PassiveId::HollowHeart => want.max_hp = value,
                    PassiveId::Pummarola => want.recovery = value,
                    PassiveId::EmptyTome => want.cooldown = value,
                    PassiveId::Spellbinder => want.duration = value,
                    PassiveId::Duplicator => want.amount = value,
                }
                // All fields, not just the desired effect: a rank must not
                // silently apply some second passive's delta.
                for (actual, target) in [
                    (stats.might, want.might),
                    (stats.max_hp, want.max_hp),
                    (stats.recovery, want.recovery),
                    (stats.cooldown, want.cooldown),
                    (stats.duration, want.duration),
                    (stats.amount, want.amount),
                ] {
                    assert!(
                        (actual - target).abs() < 1e-9,
                        "{passive:?} rank{}: {actual} vs {target}",
                        i + 1
                    );
                }
                assert_eq!(
                    (
                        stats.area,
                        stats.projectile_speed,
                        stats.armor,
                        stats.magnet,
                        stats.move_speed,
                        stats.luck,
                        stats.greed,
                        stats.curse,
                        stats.growth
                    ),
                    (
                        want.area,
                        want.projectile_speed,
                        want.armor,
                        want.magnet,
                        want.move_speed,
                        want.luck,
                        want.greed,
                        want.curse,
                        want.growth
                    )
                );
                assert_eq!(
                    sim.survivor_inventory().unwrap().passives.rank(passive),
                    Some(i as u8 + 1)
                );
                let hp = if passive == PassiveId::HollowHeart {
                    80.0 + value - 120.0
                } else {
                    80.0
                };
                assert!(
                    (sim.st.health - hp).abs() < 1e-9,
                    "only added max HP heals during the chained freeze"
                );
                eprintln!(
                    "{} rank{}: source total{value:.6}, actual HP{:.6}, via level{} card",
                    passive.as_str(),
                    i + 1,
                    sim.st.health,
                    offer.level
                );
            }
            assert!(sim.run_state().offer.is_none());
            let stats = sim.survivor_progress().unwrap().stats;
            assert_eq!(
                sim.st
                    .survivor
                    .as_mut()
                    .unwrap()
                    .arsenal
                    .learn_passive(passive),
                Err(crate::survivors::SlotError::Maxed)
            );
            assert_eq!(sim.survivor_progress().unwrap().stats, stats);
        }
    }

    #[test]
    fn survivor_passive_card_applies_the_real_delta_and_heals_added_maximum() {
        use crate::survivors::{Item, PassiveId};
        let mut sim = survivor_sim();
        max_built_survivor_weapons(&mut sim);
        sim.st.health = 80.0;
        sim.gem_arrives(0, 5.0);
        let offer = sim.run_state().offer.unwrap();
        let index = offer
            .cards
            .iter()
            .position(|c| c.item == Item::Passive(PassiveId::HollowHeart))
            .expect("maxed weapons leave Hollow Heart offerable");
        let select = [InputKey::Left, InputKey::Up, InputKey::Right][index];
        let t = offer.reveals[2] + 0.01;
        key(&mut sim, t, KeyEventKind::Press, select);
        key(&mut sim, t + 0.001, KeyEventKind::Release, select);
        key(&mut sim, t + 0.002, KeyEventKind::Press, InputKey::Confirm);
        sim.advance_to(t + 0.01);
        assert_eq!(sim.health_max(), 144.0);
        assert_eq!(sim.st.health, 104.0);
        assert_eq!(
            sim.survivor_inventory()
                .unwrap()
                .passives
                .rank(PassiveId::HollowHeart),
            Some(1)
        );
    }
    #[test]
    fn survivor_cards_resume_on_free_pulse_without_a_beat() {
        let mut sim = survivor_sim();
        sim.log = Rc::new(StreamLog::default());
        sim.st = super::fresh_state(&sim.env);
        sim.init();
        sim.gem_arrives(0, 5.0);
        let t = sim.run_state().offer.unwrap().reveals[2] + 0.01;
        key(&mut sim, t, KeyEventKind::Press, InputKey::Confirm);
        sim.advance_to(t + 1.0);
        assert!(sim.run_state().offer.is_none());
        assert!(!sim.st.freeze.frozen);
    }
    #[test]
    fn survivor_new_committed_beat_replaces_a_pending_free_pulse_resume() {
        let mut sim = survivor_sim();
        sim.log = Rc::new(StreamLog {
            beats: vec![Beat {
                t: 1.5,
                known_at: 1.2,
                period: 0.5,
                evidence: 1.0,
            }],
            ..StreamLog::default()
        });
        sim.st = super::fresh_state(&sim.env);
        sim.init();
        sim.gem_arrives(0, 5.0);
        key(&mut sim, 1.06, KeyEventKind::Press, InputKey::Confirm);
        key(&mut sim, 1.07, KeyEventKind::Release, InputKey::Confirm);
        sim.advance_to(1.45);
        assert!(sim.st.freeze.frozen);
        sim.advance_to(1.6);
        assert!(!sim.st.freeze.frozen);
    }
    fn max_built_survivor_weapons(sim: &mut Sim) {
        for def in crate::survivors::weapon_catalog()
            .weapons
            .values()
            .filter(|d| d.implemented && d.evolution_of.is_none())
        {
            while sim
                .survivor_inventory()
                .unwrap()
                .weapons
                .rank(def.id)
                .unwrap_or(0)
                < def.max_rank()
            {
                sim.learn_survivor_weapon(def.id).unwrap();
            }
        }
    }
    #[test]
    fn survivor_everything_maxed_card_heals_the_level_knob_once() {
        use crate::survivors::{Item, PassiveId};
        let mut sim = survivor_sim();
        max_built_survivor_weapons(&mut sim);
        for passive in [
            PassiveId::Spinach,
            PassiveId::HollowHeart,
            PassiveId::Pummarola,
            PassiveId::Duplicator,
            PassiveId::EmptyTome,
            PassiveId::Spellbinder,
        ] {
            for _ in 0..passive.max_rank() {
                sim.st
                    .survivor
                    .as_mut()
                    .unwrap()
                    .arsenal
                    .learn_passive(passive)
                    .unwrap();
                sim.apply_survivor_stats(passive.delta());
            }
        }
        sim.st.health = 40.0;
        sim.gem_arrives(0, 5.0);
        let offer = sim.run_state().offer.unwrap();
        assert!(offer.cards.iter().all(|c| c.item == Item::Heal));
        let t = offer.reveals[2] + 0.01;
        key(&mut sim, t, KeyEventKind::Press, InputKey::Confirm);
        sim.advance_to(t + 0.001);
        assert_eq!(sim.st.health, 70.0);
        key(&mut sim, t + 0.002, KeyEventKind::Press, InputKey::Confirm);
        sim.advance_to(t + 0.003);
        assert_eq!(sim.st.health, 70.0);
    }
    #[test]
    fn retired_survivor_ids_stay_absent_and_a_snapshot_restores_the_old_identity() {
        let mut sim = survivor_sim();
        let first = rhythm_foe(&mut sim, 400.0);
        let old = sim.snapshot();
        for _ in 0..100 {
            let id = *sim.st.alive.last().unwrap();
            sim.st.foes[id].alive = false;
            sim.st.foes[id].final_sample = true;
            sim.record_foes(sim.st.w);
            sim.st.alive.clear();
            sim.trim_state();
            assert_eq!(sim.st.foes.stored(), 0);
            assert!(
                !sim.hittable(id),
                "retired missile targets are not hittable"
            );
            let new = rhythm_foe(&mut sim, 400.0);
            assert!(new > id);
            let before = sim.st.foes[new].recoil_until;
            sim.survivor_recoil(id, 1.0); // A stale queued recoil cannot affect the new foe.
            assert_eq!(sim.st.foes[new].recoil_until, before);
        }
        assert_eq!(sim.st.foes.stored(), 1);
        assert_eq!(sim.st.foe_samples.len(), 1);
        sim.restore(&old);
        assert!(sim.st.foes[first].alive);
        assert_eq!(sim.st.foes.len(), first + 1);
    }

    #[test]
    fn effect_pools_keep_queued_and_deferred_references_then_retire_them() {
        let mut sim = survivor_sim();
        sim.st.waves.push(WaveState {
            x: 0.0,
            y: 0.0,
            dir: 0.0,
            group: 0,
            mult: 1.0,
            inner: 0.0,
            half_angle: 1.0,
            to: 100.0,
            hit: HashSet::new(),
        });
        sim.st.chains.push(ChainState {
            last: crate::recorder::ChainPoint {
                w: 0.0,
                x: 0.0,
                y: 0.0,
            },
            index: 0,
            hit: HashSet::new(),
            jumps: 2.0,
            energy: 1.0,
            bi: 0.0,
            range: None,
            mult: 1.0,
        });
        sim.st.q.push(100.0, Ev::Wave { group: 0, band: 0 });
        sim.push_at_beat(
            100.0,
            Ev::Chain {
                weapon: 0,
                chain: 0,
                jump: 0,
            },
        );
        sim.trim_state();
        assert_eq!(sim.st.waves.stored(), 1);
        assert_eq!(sim.st.chains.stored(), 1);
        sim.st.q.remove(&|ev| matches!(ev, Ev::Wave { .. }));
        sim.st.deferred.clear();
        sim.trim_state();
        assert_eq!(sim.st.waves.stored(), 0);
        assert_eq!(sim.st.chains.stored(), 0);
        assert_eq!(sim.st.chains.len(), 1); // Recording identity remains monotonic.
    }
    #[test]
    fn separation_cache_matches_uncached_physics_after_restore_and_changed_keys() {
        use crate::recorder::LogRecorder;
        use std::cell::RefCell;
        let mut a = survivor_sim();
        let mut b = survivor_sim();
        b.spatial.cache.disabled = true;
        let ar = Rc::new(RefCell::new(LogRecorder::default()));
        let br = Rc::new(RefCell::new(LogRecorder::default()));
        a.rec = Box::new(Rc::clone(&ar));
        b.rec = Box::new(Rc::clone(&br));
        for k in 0..40 {
            for sim in [&mut a, &mut b] {
                rhythm_foe(sim, (k as f64 - 20.0) * 2.0);
            }
        }
        a.advance_to(1.0);
        b.advance_to(1.0);
        let (sa, sb) = (a.snapshot(), b.snapshot());
        let (base_ar, base_br) = (ar.borrow().clone(), br.borrow().clone());
        for changed in [false, false, true] {
            a.restore(&sa);
            b.restore(&sb);
            *ar.borrow_mut() = base_ar.clone();
            *br.borrow_mut() = base_br.clone();
            if changed {
                for sim in [&mut a, &mut b] {
                    key(sim, 1.01, KeyEventKind::Press, InputKey::Right);
                }
            }
            a.advance_to(1.25);
            b.advance_to(1.25);
            assert_eq!(format!("{:?}", ar.borrow()), format!("{:?}", br.borrow()));
            assert_eq!(format!("{:?}", a.st.foes), format!("{:?}", b.st.foes));
        }
        assert!(a.spatial.cache.hits > 0);
    }
    #[test]
    fn beatless_gems_chickens_and_vacuums_never_queue_nan_or_stall_the_clock() {
        #[derive(Default)]
        struct Arrivals(Vec<usize>);
        impl crate::recorder::Recorder for Arrivals {
            fn gem_arrive(&mut self, gem: usize, _w: f64) {
                self.0.push(gem);
            }
        }
        for beats in [0, 1] {
            for vacuum in [false, true] {
                let mut file = stream();
                file.log.beats.truncate(beats);
                let mut sim = survivor_sim_with(file);
                let arrived = Rc::new(std::cell::RefCell::new(Arrivals::default()));
                sim.rec = Box::new(Rc::clone(&arrived));
                sim.st.health = 60.0;
                sim.st.chickens.push(super::Spot {
                    i: 0,
                    x: 0.0,
                    y: 0.0,
                });
                let x = if vacuum { 150.0 } else { 0.0 };
                sim.drop_on_field(
                    0,
                    crate::recorder::Fate {
                        critical: false,
                        w: 0.0,
                        x,
                        y: 0.0,
                        hit: 0.0,
                        lethal: true,
                        damage: 1.0,
                    },
                    1.0,
                    false,
                );
                let gem = sim.st.field.last().unwrap().gem;
                if vacuum {
                    sim.st.vacuums.push(super::Spot {
                        i: 0,
                        x: 0.0,
                        y: 0.0,
                    });
                }
                sim.advance_to(1.5);
                assert!(sim.s() > 1.4, "{beats} beats, vacuum {vacuum}: {}", sim.s());
                assert!(sim.st.field.is_empty(), "{beats} beats, vacuum {vacuum}");
                assert!(sim.st.vacuums.is_empty());
                assert!(arrived.borrow().0.contains(&gem));
                assert!(sim.survivor_progress().unwrap().xp > 0.0);
                assert!(sim.st.health > 60.0);
                assert!(sim.st.q.heap.iter().all(|e| !e.key.s.is_nan()));
            }
        }
    }

    #[test]
    fn beatless_victory_clears_high_hp_foes_and_finishes_its_gem_flights() {
        let mut file = stream();
        file.log.beats.clear();
        let mut sim = survivor_sim_with(file);
        Rc::get_mut(&mut sim.env)
            .unwrap()
            .input
            .level
            .survivors
            .as_mut()
            .unwrap()
            .duration_s = 2.2;
        sim.disable_hero_damage_for_check();
        let id = rhythm_foe(&mut sim, 400.0);
        assert!(sim.st.foes[id].health > 2000.0);
        sim.advance_to(6.0);
        assert!(sim.done(), "stuck at {}", sim.s());
        assert!((sim.end_s() - 5.45).abs() < 1e-8);
        assert_eq!(sim.storage().alive, 0);
        assert!(sim.st.q.heap.iter().all(|e| !e.key.s.is_nan()));
    }

    #[test]
    fn binary_grid_search_matches_the_full_known_prefix_scan() {
        let mut sim = survivor_sim();
        sim.advance_to(1.1);
        for step in [0.25, 0.5, 1.0, 2.0, 4.0] {
            for due in [-1.0, 0.0, 0.125, 0.5, 0.99, 1.0, 1.1, 1.25, 1.5, 2.0, 100.0] {
                let due = f64::max(due, sim.st.s);
                let beats = &sim.log.beats[..sim.st.ingested.beats];
                let slow = beats.iter().enumerate().find_map(|(i, beat)| {
                    let mut bi = (i as f64 / step - 1e-9).ceil() * step;
                    while bi < i as f64 + 1.0 - 1e-9 {
                        let at = if bi.fract().abs() < 1e-9 {
                            beat.t
                        } else {
                            let next = beats.get(i + 1)?;
                            beat.t + (next.t - beat.t) * bi.fract()
                        };
                        if at >= due - 1e-9 {
                            return Some((bi, at));
                        }
                        bi += step;
                    }
                    None
                });
                assert_eq!(
                    sim.known_grid_after(due, step),
                    slow,
                    "due {due} step {step}"
                );
            }
        }
    }
    #[test]
    fn due_queue_shortcut_preserves_full_drain_order_with_tolerance_and_new_events() {
        use super::{Entry, Queue, phase};
        use crate::core::rng::{Part, Rng};
        fn reference(q: &mut Queue, limit: f64) -> Entry {
            let first = q.pop();
            if q.peek() > limit {
                return first;
            }
            let mut due = vec![first];
            while q.peek() <= limit {
                due.push(q.pop());
            }
            let mut best = 0;
            for i in 1..due.len() {
                let (a, b) = (&due[i].key, &due[best].key);
                if a.o < b.o || (a.o == b.o && a.rank_less(b)) {
                    best = i;
                }
            }
            let chosen = due.swap_remove(best);
            for e in due {
                q.insert(e);
            }
            chosen
        }
        let mut rng = Rng::new(&[Part::Str("ci42-due-queue")]);
        let mut count = 0;
        for mode in 0..4 {
            let mut a = Queue::default();
            let mut b = Queue::default();
            for batch in 0..32 {
                for i in 0..64 {
                    let jitter = if mode == 0 {
                        0.0
                    } else {
                        rng.int(-5.0, 5.0) * 1e-13
                    };
                    let at = batch as f64 * 0.125 + jitter;
                    let ev = match i % 4 {
                        0 => Ev::Recoil {
                            foe: i,
                            multiplier: 1.0,
                        },
                        1 => Ev::EnemyWind {
                            peak: at + 0.25,
                            period: 0.5,
                            generation: None,
                        },
                        2 => Ev::Beat { bi: i },
                        _ => Ev::Held,
                    };
                    let phase = if mode == 3 {
                        (i % 7) as u16
                    } else {
                        phase::RUNTIME
                    };
                    a.push_init(at, ev.clone(), phase, i as u64 + batch * 100);
                    b.push_init(at, ev, phase, i as u64 + batch * 100);
                }
                while a.peek() <= batch as f64 * 0.125 + 1e-9 {
                    let limit = a.peek() + 1e-12;
                    let left = a.pop_due(limit);
                    let right = reference(&mut b, limit);
                    assert_eq!(format!("{left:?}"), format!("{right:?}"));
                    count += 1;
                    if mode >= 2 && rng.next() < 0.08 {
                        let ev = Ev::Recoil {
                            foe: 999,
                            multiplier: 0.5,
                        };
                        a.push(limit, ev.clone());
                        b.push(limit, ev);
                    }
                    assert_eq!(a.heap.len(), b.heap.len());
                    assert_eq!(a.peek(), b.peek());
                }
            }
            assert!(a.heap.is_empty());
            assert!(b.heap.is_empty());
        }
        eprintln!(
            "CI42: {count} queue selections equal the original drain algorithm; exact timestamps, +/-0.5ps, mixed phases/kinds and events inserted during dispatch"
        );
    }
    mod chest_tests {
        use super::*;
        use crate::survivors::{ChestEvent, FreezeReason, Item, WeaponId};
        fn fixture(beats: usize) -> (Sim, Rc<std::cell::RefCell<crate::recorder::LogRecorder>>) {
            let mut file = stream();
            file.log = StreamLog {
                beats: (0..beats)
                    .map(|i| Beat {
                        t: i as f64 * 0.5,
                        known_at: (i as f64 * 0.5 - 0.6).max(0.0),
                        period: 0.5,
                        evidence: 1.0,
                    })
                    .collect(),
                ..Default::default()
            };
            let mut s = survivor_sim_with(file);
            let r = Rc::new(std::cell::RefCell::new(
                crate::recorder::LogRecorder::default(),
            ));
            s.rec = Box::new(Rc::clone(&r));
            s.advance_to(0.13);
            let h = s.hero_pose(s.st.w);
            s.drop_chest(h.x, h.y, 3, 10);
            s.touch_chests();
            (s, r)
        }
        #[test]
        fn chest_two_bars_tick_on_eighths_and_land_on_downbeat_without_confirm() {
            let (mut s, r) = fixture(30);
            let run = s.st.run_s;
            key(&mut s, 0.2, KeyEventKind::Press, InputKey::Confirm);
            key(&mut s, 0.21, KeyEventKind::Release, InputKey::Confirm);
            s.advance_to(2.99);
            assert_eq!(s.run_state().frozen, Some(FreezeReason::Chest));
            assert_eq!(s.st.run_s, run);
            assert_eq!(
                s.st.survivor
                    .as_ref()
                    .unwrap()
                    .arsenal
                    .inventory()
                    .weapons
                    .rank(WeaponId::Sword),
                Some(1)
            );
            s.advance_to(3.001);
            let q = s.run_state().roulette.unwrap();
            assert_eq!(q.landed, Some(3.0));
            assert_eq!(q.rewards, [Some(Item::Weapon(WeaponId::Sword)); 5]);
            assert_eq!(
                s.st.survivor
                    .as_ref()
                    .unwrap()
                    .arsenal
                    .inventory()
                    .weapons
                    .rank(WeaponId::Sword),
                Some(6)
            );
            let ticks: Vec<_> = r
                .borrow()
                .chests
                .iter()
                .filter_map(|(s, e)| matches!(e, ChestEvent::Tick { .. }).then_some(*s))
                .collect();
            assert_eq!(
                ticks,
                (0..8).map(|i| 1.0 + i as f64 * 0.25).collect::<Vec<_>>()
            );
            s.advance_to(3.51);
            assert!(!s.st.freeze.frozen);
            assert!(s.st.run_s > run);
            eprintln!(
                "SH10: tier3, 8 ticks at1.0..2.75, landing3.0, source upgrades Sword1->6, run frozen{run:.6}"
            );
        }
        #[test]
        fn chest_snapshot_repeats_reward_timing_inventory_and_all_events() {
            let (mut s, r) = fixture(30);
            s.advance_to(1.37);
            let snap = s.snapshot();
            let before = r.borrow().clone();
            s.advance_to(4.2);
            let expected = r.borrow().chests.clone();
            let inv = s.st.survivor.as_ref().unwrap().arsenal.inventory();
            let state = s.run_state();
            s.restore(&snap);
            *r.borrow_mut() = before;
            for i in 138..=420 {
                s.advance_to(i as f64 / 100.0);
            }
            assert_eq!(r.borrow().chests, expected);
            assert_eq!(s.st.survivor.as_ref().unwrap().arsenal.inventory(), inv);
            assert_eq!(s.run_state(), state);
        }
        #[test]
        fn chest_zero_one_and_poisoned_uncommitted_beats_stay_finite_and_causal() {
            for beats in [0, 1] {
                let (mut s, _) = fixture(beats);
                s.advance_to(4.0);
                assert!(!s.st.freeze.frozen);
                assert!(s.st.run_s > 0.13);
                assert!(s.st.q.peek().is_finite());
                assert_eq!(
                    s.st.survivor
                        .as_ref()
                        .unwrap()
                        .arsenal
                        .inventory()
                        .weapons
                        .rank(WeaponId::Sword),
                    Some(6)
                );
            }
            let (mut a, ra) = fixture(2);
            let (mut b, rb) = fixture(2);
            Rc::get_mut(&mut b.log)
                .unwrap()
                .beats
                .extend((2..30).map(|i| Beat {
                    t: i as f64 * 0.731,
                    known_at: 100.0,
                    period: 0.731,
                    evidence: 1.0,
                }));
            a.advance_to(4.0);
            b.advance_to(4.0);
            assert_eq!(ra.borrow().chests, rb.borrow().chests);
        }
        #[test]
        fn chest_pause_does_not_skip_to_prizes_and_pending_levels_wait() {
            let (mut s, rec) = fixture(40);
            s.st.survivor.as_mut().unwrap().pending_levels = 1;
            s.st.survivor.as_mut().unwrap().level = 2;
            s.open_survivor_offer();
            assert!(s.st.survivor.as_ref().unwrap().offer.is_none());
            key(&mut s, 1.1, KeyEventKind::Press, InputKey::Pause);
            key(&mut s, 1.11, KeyEventKind::Release, InputKey::Pause);
            s.advance_to(4.0);
            assert!(s.st.roulette.unwrap().landed.is_none());
            key(&mut s, 4.1, KeyEventKind::Press, InputKey::Pause);
            key(&mut s, 4.11, KeyEventKind::Release, InputKey::Pause);
            s.advance_to(7.01);
            assert!(
                rec.borrow()
                    .chests
                    .iter()
                    .any(|(_, e)| matches!(e, ChestEvent::Landed { .. }))
            );
            assert!(s.st.survivor.as_ref().unwrap().offer.is_some());
            assert_eq!(s.run_state().frozen, Some(FreezeReason::LevelUp));
        }

        #[test]
        fn late_grid_bounds_cannot_backdate_roulette_during_stop_lookahead() {
            let (mut s, rec) = fixture(30);
            let log = Rc::get_mut(&mut s.log).unwrap();
            // A new bound at1.7 is not known until1.75. The midpoint1.6
            // cannot pull next_stop backwards when it ingests at1.75.
            log.beats[4].t = 1.7;
            log.beats[4].known_at = 1.75;
            s.advance_to(1.74);
            assert!(
                rec.borrow()
                    .chests
                    .iter()
                    .filter(|(_, event)| matches!(event, ChestEvent::Tick { .. }))
                    .all(|(at, _)| *at <= 1.5)
            );
            assert!(s.st.roulette.unwrap().landed.is_none());
            s.advance_to(4.1);
            assert!(
                rec.borrow()
                    .chests
                    .iter()
                    .any(|(_, e)| matches!(e, ChestEvent::Landed { .. }))
            );
            assert_eq!(
                s.st.roulette.unwrap().landed,
                Some(4.0),
                "relocked spin finishes on the next downbeat, never a leftover eighth"
            );
        }
        #[test]
        fn music_relock_removes_only_the_music_reason_and_finishes_the_chest_spin() {
            let (mut s, rec) = fixture(40);
            Rc::get_mut(&mut s.log).unwrap().ticks = (0..200)
                .map(|i| {
                    let t = f64::from(i) * 0.05;
                    Tick {
                        t,
                        known_at: t,
                        evidence: 1.0,
                        energy: 0.5,
                        level: None,
                        key: None,
                        bands: None,
                        brightness: None,
                    }
                })
                .collect();
            s.freeze_at(1.1, FreezeReason::MusicStopped);
            s.advance_to(2.9);
            assert!(s.st.freeze.reasons.contains(&FreezeReason::MusicStopped));
            assert!(s.st.roulette.unwrap().landed.is_none());
            s.advance_to(4.9);
            assert_eq!(s.run_state().frozen, Some(FreezeReason::Chest));
            assert!(!s.st.freeze.reasons.contains(&FreezeReason::MusicStopped));
            assert!((s.st.run_s - 0.13).abs() < 1e-9);
            assert!(
                rec.borrow()
                    .chests
                    .iter()
                    .filter(|(_, event)| matches!(event, ChestEvent::Tick { .. }))
                    .all(|(at, _)| *at < 1.1 || *at >= 3.0)
            );
            s.advance_to(5.01);
            assert!(!s.st.freeze.frozen);
            assert_eq!(s.st.roulette.unwrap().landed, Some(5.0));
            assert_eq!(
                s.st.survivor
                    .as_ref()
                    .unwrap()
                    .arsenal
                    .inventory()
                    .weapons
                    .rank(WeaponId::Sword),
                Some(6)
            );
        }
        #[test]
        fn early_chest_applies_real_passive_delta_then_three_heals_and_caps_health() {
            use crate::survivors::PassiveId;
            let (mut s, _) = fixture(30);
            s.st.roulette.as_mut().unwrap().minute = 1;
            while s
                .st
                .survivor
                .as_ref()
                .unwrap()
                .arsenal
                .inventory()
                .weapons
                .rank(WeaponId::Sword)
                != Some(7)
            {
                s.learn_survivor_weapon(WeaponId::Sword).unwrap();
            }
            for _ in 0..4 {
                s.st.survivor
                    .as_mut()
                    .unwrap()
                    .arsenal
                    .learn_passive(PassiveId::HollowHeart)
                    .unwrap();
                s.apply_survivor_stats(PassiveId::HollowHeart.delta());
            }
            let before = s.health_max();
            s.st.health = 1.0;
            s.advance_to(3.001);
            let after = s.health_max();
            assert!((after - before * 1.2).abs() < 1e-9);
            assert!(
                (s.st.health
                    - (1.0 + after - before
                        + 3.0 * s.env.input.level.survivors.unwrap().maxed_heal))
                    .abs()
                    < 1e-9
            );
            assert_eq!(
                s.st.roulette.unwrap().rewards,
                [
                    Some(Item::Weapon(WeaponId::Sword)),
                    Some(Item::Passive(PassiveId::HollowHeart)),
                    Some(Item::Heal),
                    Some(Item::Heal),
                    Some(Item::Heal)
                ]
            );
            let (mut maxed, _) = fixture(30);
            maxed.st.roulette.as_mut().unwrap().minute = 1;
            while maxed
                .st
                .survivor
                .as_ref()
                .unwrap()
                .arsenal
                .inventory()
                .weapons
                .rank(WeaponId::Sword)
                != Some(8)
            {
                maxed.learn_survivor_weapon(WeaponId::Sword).unwrap();
            }
            maxed.st.health = 1.0;
            maxed.advance_to(3.001);
            assert_eq!(maxed.st.roulette.unwrap().rewards, [Some(Item::Heal); 5]);
            assert_eq!(maxed.st.health, maxed.health_max());
        }
    }
    #[test]
    fn scheduled_boss_treasure_uses_its_origin_minute_once_and_survives_recycling() {
        use crate::survivors::ScheduledBoss;
        let mut s = survivor_sim();
        s.log = Rc::new(StreamLog::default());
        s.st.ingested.beats = 0;
        let mut level = load_level(
            &source("levels/survivors.yaml"),
            &s.env.input.lib,
            &reactive_song_map(f64::INFINITY),
            None,
        )
        .unwrap();
        // Two holders exercise the source's first-success-only rule; no ordinary population.
        for row in &mut level.survivor_schedule {
            row.minimum = Some(0);
            row.enemies = Some(Vec::new());
        }
        level.survivor_schedule[1].treasure[0].tier = Some(3);
        Rc::get_mut(&mut s.env).unwrap().input.level = level;
        s.st.survivor.as_mut().unwrap().level = 7;
        // A held boss carries its origin even if admission happens during minute8,
        // whose XLBAT source row explicitly has no treasure.
        s.st.spawner.minute = Some(8);
        s.st.spawner.waiting_bosses = vec![
            ScheduledBoss {
                role: "vs-bat4".into(),
                minute: 1,
            },
            ScheduledBoss {
                role: "vs-bat5".into(),
                minute: 1,
            },
            ScheduledBoss {
                role: "vs-xlbat".into(),
                minute: 8,
            },
        ];
        s.st.s = 480.0;
        s.st.run_s = 480.0;
        s.st.w = 480.0;
        s.tick_spawner();
        assert_eq!(s.st.foes[0].health, 35.0);
        assert_eq!(s.st.foes[1].health, 35.0);
        assert_eq!(s.st.foes[2].health, 189.0);
        assert_eq!(
            s.st.foes[0].chest_loot,
            Some(crate::survivors::ChestLoot { tier: 3, minute: 1 })
        );
        assert_eq!(s.st.foes[1].chest_loot, None);
        assert_eq!(s.st.foes[2].chest_loot, None);
        assert_eq!(s.st.treasure_minutes, 1 << 1);
        s.st.foes[0].health = 19.0;
        s.st.foes[0].x = 1e6;
        s.recycle();
        let replacement = s.st.foes.len() - 1;
        assert!(s.st.foes[replacement].survivor_boss);
        assert_eq!(s.st.foes[replacement].health, 19.0);
        assert_eq!(
            s.st.foes[replacement].chest_loot,
            Some(crate::survivors::ChestLoot { tier: 3, minute: 1 })
        );
        assert_eq!(
            s.st.treasure_minutes,
            1 << 1,
            "recycling does not reroll treasure"
        );
    }
    #[test]
    fn late_boss_gait_uses_bar_slots_instead_of_the_ordinary_roles_slots() {
        let mut file = stream();
        file.log = StreamLog {
            beats: (0..180)
                .map(|i| Beat {
                    t: f64::from(i) * 0.5,
                    known_at: 0.0,
                    period: 0.5,
                    evidence: 1.0,
                })
                .collect(),
            ticks: (0..1800)
                .map(|i| {
                    let t = f64::from(i) * 0.05;
                    Tick {
                        t,
                        known_at: t,
                        evidence: 1.0,
                        energy: 0.5,
                        level: None,
                        key: None,
                        bands: None,
                        brightness: None,
                    }
                })
                .collect(),
            ..Default::default()
        };
        let mut s = survivor_sim_with(file);
        s.advance_to(60.25);
        let id = s.spawn_foe(
            s.env.input.lib.enemies.get_index_of("vs-xlmantis").unwrap(),
            300.0,
            0.0,
            Some(10000.0),
        );
        s.mark_survivor_boss(id, true);
        assert_eq!(s.st.foes[id].step_k, Some(120.0));
        s.advance_to(60.99);
        assert!(s.st.foes[id].step.is_none(), "wait for bar");
        s.advance_to(61.02);
        assert!(
            s.st.foes[id].step.is_some(),
            "minute5/8-style spawn does not wait dozens of bars"
        );
        s.freeze_at(61.2, crate::survivors::FreezeReason::Paused);
        key(&mut s, 62.1, KeyEventKind::Press, InputKey::Pause);
        key(&mut s, 62.11, KeyEventKind::Release, InputKey::Pause);
        s.advance_to(62.99);
        assert_eq!(
            s.st.foes[id].step.unwrap().w0,
            61.0,
            "resume on odd beat finishes held step, does not start an extra charge"
        );
        s.advance_to(63.02);
        assert!(
            (s.st.foes[id].step.unwrap().w0 - 61.7).abs() < 1e-8,
            "next native bar starts the next charge"
        );
    }
}
