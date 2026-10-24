//! `src/sim/recorder.ts`: the recording side of the simulation. The simulation never reads what it
//! has recorded: it reports each event to a [`Recorder`], which owns the data. The records serialize
//! as the TS plan's entries do (camelCase, absent optional fields, non-finite numbers as null), so
//! [`LogRecorder`] builds the oracle event log's categories that the simulation itself produces
//! (the emitted effects, sounds, UI and gems come with the recording and emit, B7).

#![allow(clippy::many_single_char_names)] // (the TS recorder's names)

use serde::Serialize;
use serde_json::{Value, json};

use crate::core::geometry::Shape;
use crate::core::warp::{Warp, WarpBreak};
use crate::upgrades::UpgradeRecord;

/// A hit shape as the plan writes it (`{kind: "arc", ...}`).
pub fn shape_json(s: &Shape) -> Value {
    match *s {
        Shape::Arc {
            x,
            y,
            radius,
            dir,
            half_angle,
            inner,
        } => {
            let mut v = json!({ "kind": "arc", "x": x, "y": y, "radius": radius, "dir": dir, "halfAngle": half_angle });
            if let Some(inner) = inner {
                v["inner"] = json!(inner);
            }
            v
        }
        Shape::Circle { x, y, radius } => {
            json!({ "kind": "circle", "x": x, "y": y, "radius": radius })
        }
        Shape::Capsule {
            x0,
            y0,
            x1,
            y1,
            radius,
        } => json!({ "kind": "capsule", "x0": x0, "y0": y0, "x1": x1, "y1": y1, "radius": radius }),
        Shape::Ring { x, y, inner, outer } => {
            json!({ "kind": "ring", "x": x, "y": y, "inner": inner, "outer": outer })
        }
    }
}

fn serialize_shape<S: serde::Serializer>(s: &Shape, ser: S) -> Result<S::Ok, S::Error> {
    shape_json(s).serialize(ser)
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum HitSource {
    Move,
    Weapon,
    Wave,
    Ring,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Weight {
    Light,
    Medium,
    Heavy,
}

impl Weight {
    pub fn from_content(w: crate::content::types::Weight) -> Weight {
        match w {
            crate::content::types::Weight::Light => Weight::Light,
            crate::content::types::Weight::Medium => Weight::Medium,
            crate::content::types::Weight::Heavy => Weight::Heavy,
        }
    }

    /// `weightFactor`.
    pub fn factor(self) -> f64 {
        match self {
            Weight::Light => 0.4,
            Weight::Medium => 0.7,
            Weight::Heavy => 1.0,
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize)]
pub struct Hit {
    pub source: HitSource,
    pub what: String,
    pub weight: Weight,
    pub primary: bool,
    #[serde(serialize_with = "serialize_shape")]
    pub shape: Shape,
    pub dir: f64,
    pub accent: f64,
    pub damage: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub group: Option<f64>,
    pub id: f64,
    pub s: f64,
    pub w: f64,
    pub section: f64,
    pub energy: f64,
    pub intensity: f64,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct Fate {
    pub w: f64,
    pub x: f64,
    pub y: f64,
    pub hit: f64,
    pub lethal: bool,
    pub damage: f64,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct EnemyStep {
    pub w: f64,
    pub dur: f64,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EnemyRecord {
    pub id: f64,
    #[serde(rename = "type")]
    pub kind: String,
    pub health: f64,
    pub spawn_w: f64,
    pub death_w: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub recycled: Option<bool>,
    pub sample_count: f64,
    pub sample_w0: f64,
    pub fates: Vec<Fate>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub formation: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub entrance: Option<String>,
    /// Boss slams or archer releases: wind-up (or draw) start and release, as world times.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub attacks: Option<Vec<AttackRecord>>,
    /// A formation member's dance.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub dance: Option<DanceRecord>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub steps: Option<Vec<EnemyStep>>,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
pub struct AimSnapshot {
    pub x: f64,
    pub y: f64,
    pub facing: f64,
    pub targets: Vec<f64>,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct Reach {
    pub radius: f64,
    pub degrees: f64,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PlanSwing {
    pub s: f64,
    pub w: f64,
    pub s_armed: f64,
    pub s_wind: f64,
    pub kind: &'static str,
    pub anim: String,
    pub impact_frame: f64,
    pub dir: f64,
    pub x: f64,
    pub y: f64,
    pub hit: f64,
    pub accent: f64,
    pub aim: AimSnapshot,
    pub tier: f64,
    pub reach: Reach,
}

#[derive(Clone, Debug, Default, PartialEq, Serialize)]
pub struct PlanInput {
    pub s: f64,
    pub w: f64,
    pub kind: &'static str,
    pub key: &'static str,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rating: Option<&'static str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub delta: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub bend: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub extra: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub offbeat: Option<&'static str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub stride: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub class: Option<&'static str>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub unjudged: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub stomp: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rock: Option<bool>,
    pub steps: bool,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct Landing {
    pub s: f64,
    pub w: f64,
    pub x: f64,
    pub y: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub stomp: Option<bool>,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
pub struct BiteRecord {
    pub s: f64,
    pub w: f64,
    pub source: &'static str,
    pub enemy: String,
    pub damage: f64,
    pub until: f64,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
pub struct HealRecord {
    pub s: f64,
    pub amount: f64,
    pub source: &'static str,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ChickenRecord {
    pub s: f64,
    pub w: f64,
    pub x: f64,
    pub y: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub s_taken: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub w_taken: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub s_heal: Option<f64>,
}

/// A gem: dropped at (x0, y0) at world time w0, arriving at song and world time s1, w1 (infinite
/// while it waits on the field).
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GemRecord {
    pub w0: f64,
    pub x0: f64,
    pub y0: f64,
    pub s1: f64,
    pub w1: f64,
    pub value: f64,
    pub big: bool,
    pub enemy: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub w_t: Option<f64>,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct VacuumRecord {
    pub s: f64,
    pub w: f64,
    pub x: f64,
    pub y: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub s_touch: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub w_touch: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub gems: Option<f64>,
}

/// A cleave: an accent's counted press of Great or better launched its shockwave.
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct Cleave {
    pub s: f64,
    pub press: f64,
    pub band0: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub launch: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub onset: Option<bool>,
}

/// Reactive mode: a press's settled judgment.
#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SettleRecord {
    pub s: f64,
    pub p: f64,
    pub provisional: String,
    pub settled: String,
    pub change: f64,
    pub reading: String,
    pub switched: bool,
    pub known_at: f64,
    pub evidence: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub kept: Option<String>,
}

/// An enemy's attack: its wind-up from w0, released at w (provisional while only predicted).
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct AttackRecord {
    pub w0: f64,
    pub w: f64,
    pub kind: &'static str,
    pub dir: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub provisional: Option<bool>,
}

/// A formation member's dance: world times, its animation, and whether its legs alternate.
#[derive(Clone, Debug, PartialEq, Serialize)]
pub struct DanceRecord {
    pub w0: f64,
    pub w1: f64,
    pub anim: String,
    pub alternate: bool,
    pub phase: f64,
}

/// A set piece's release (a formation's, a ring's close, a stampede's entrance) and its members.
#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FormationRecord {
    pub def: String,
    pub s_start: f64,
    pub s_release: f64,
    pub members: Vec<f64>,
}

/// A transform wave sweeping out from the hero into the next zone (on a detected section, or a fallback).
#[derive(Clone, Debug, PartialEq, Serialize)]
pub struct ZoneWaveRecord {
    pub s: f64,
    pub w: f64,
    pub x: f64,
    pub y: f64,
    pub zone: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub section: Option<f64>,
}

/// A landmark rising centred on the hero.
#[derive(Clone, Debug, PartialEq, Serialize)]
pub struct LandmarkRecord {
    pub id: String,
    pub s: f64,
    pub w: f64,
    pub x: f64,
    pub y: f64,
}

/// A boss slam's marker appearing under the hero.
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SlamMarkRecord {
    pub x: f64,
    pub y: f64,
    pub s_mark: f64,
    pub w_mark: f64,
    pub s_land: f64,
    pub boss: usize,
    pub dir: f64,
}

/// A boss slam landing, and how it ended for the hero.
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SlamRecord {
    pub s_mark: f64,
    pub w_mark: f64,
    pub s_land: f64,
    pub w_land: f64,
    pub x: f64,
    pub y: f64,
    pub radius: f64,
    pub outcome: &'static str,
}

/// An arrow: released, aimed at the hero's hurtbox, and (once it arrives) where its flight ends.
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ArrowRecord {
    pub s_release: f64,
    pub w_release: f64,
    pub s_arrive: f64,
    pub w_arrive: f64,
    pub x0: f64,
    pub y0: f64,
    pub ax: f64,
    pub ay: f64,
    pub x1: f64,
    pub y1: f64,
    pub w1: f64,
    pub outcome: &'static str,
}

/// A threat (an arrow or a slam) and how it ended.
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct ThreatRecord {
    pub s: f64,
    pub w: f64,
    pub kind: &'static str,
    pub outcome: &'static str,
    pub x: f64,
    pub y: f64,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct ChainPoint {
    pub w: f64,
    pub x: f64,
    pub y: f64,
}

/// Receives every simulation event, in the order it happens (`Recorder`). The B6 and B7 events
/// (set pieces, the boss, arrows, zone waves, landmarks) join with their beads.
#[allow(unused_variables)]
pub trait Recorder {
    /// A warp break: appended, or replacing the last one.
    fn warp(&mut self, b: WarpBreak, replace_last: bool) {}
    fn hero(&mut self, x: f64, y: f64, z: f64, facing: f64, anim: f64, frame: f64) {}
    fn camera(&mut self, x: f64, y: f64, zoom: f64) {}
    /// A new enemy (its id is the next index); its first sample follows.
    fn spawn(&mut self, rec: EnemyRecord) {}
    fn sample(&mut self, id: usize, x: f64, y: f64) {}
    fn fate(&mut self, id: usize, fate: Fate) {}
    fn death(&mut self, id: usize, w: f64, recycled: bool) {}
    fn hit(&mut self, h: Hit) {}
    fn swing(&mut self, sw: PlanSwing) {}
    fn input(&mut self, i: PlanInput) {}
    fn groove(&mut self, s: f64, v: f64) {}
    fn tier(&mut self, s: f64, w: f64, tier: f64, sound_s: f64) {}
    fn health(&mut self, s: f64, frac: f64) {}
    fn landing(&mut self, l: Landing) {}
    fn weapon(&mut self, id: &str, s: f64) {}
    fn fire(&mut self, weapon: &str, s: f64) {}
    fn chain(&mut self, p: ChainPoint) {}
    fn chain_point(&mut self, chain: usize, p: ChainPoint) {}
    fn hit_stop(&mut self, s0: f64, s1: f64) {}
    fn hit_stop_cut(&mut self, s: f64) {}
    fn bite(&mut self, b: BiteRecord) {}
    fn heal(&mut self, h: HealRecord) {}
    fn chicken(&mut self, c: ChickenRecord) {}
    fn chicken_taken(&mut self, i: usize, s: f64, w: f64, s_heal: f64) {}
    fn gem(&mut self, g: GemRecord) {}
    fn gem_arrive(&mut self, gem: usize, w: f64) {}
    fn gem_fly(&mut self, gem: usize, w: f64, s1: f64) {}
    fn gem_grow(&mut self, gem: usize, value: f64, big: bool) {}
    fn vacuum(&mut self, v: VacuumRecord) {}
    fn vacuum_touch(&mut self, i: usize, s: f64, w: f64, gems: f64) {}
    fn upgrade(&mut self, u: UpgradeRecord) {}
    fn upgrade_taken(&mut self, i: usize, k: usize, s: f64, w: f64, s_apply: f64, walked: bool) {}
    fn xp(&mut self, s: f64, fill: f64) {}
    fn wave(&mut self, cleave: Cleave) {}
    fn wave_hit(&mut self, wave: usize, hit: usize) {}
    /// The clock moved to (s, w) (`between` the loop's own stops).
    fn clock(&mut self, s: f64, w: f64, between: bool) {}
    /// Beat steps: an enemy's step from world time w lasting dur, or (dur 0) its return to gliding.
    fn enemy_step(&mut self, id: usize, w: f64, dur: f64) {}
    fn settle(&mut self, r: SettleRecord) {}
    /// A set piece released (or a ring closing, a stampede entering), with its members.
    fn formation(&mut self, f: FormationRecord) {}
    fn zone_wave(&mut self, z: ZoneWaveRecord) {}
    fn landmark(&mut self, l: LandmarkRecord) {}
    /// A formation member's dance starts at song time `s_from`; its whole dance at its release.
    fn dance_start(&mut self, id: usize, s_from: f64, anim: &str, alternate: bool) {}
    fn dance(&mut self, id: usize, d: DanceRecord) {}
    /// An archer starts drawing at world time w0 for a release at song time `s_release`.
    fn draw_start(&mut self, id: usize, w0: f64, s_release: f64, dir: f64) {}
    fn attack(&mut self, id: usize, a: AttackRecord) {}
    fn arrow(&mut self, a: ArrowRecord) {}
    /// Arrow `arrow` arrives at world time `w_arrive`: its outcome and where its flight ends.
    #[allow(clippy::too_many_arguments)]
    fn arrow_end(
        &mut self,
        arrow: usize,
        w_arrive: f64,
        outcome: &'static str,
        x1: f64,
        y1: f64,
        w1: f64,
    ) {
    }
    fn threat(&mut self, t: ThreatRecord) {}
    /// The boss appears (enemy `id`), landing at song time `land_s`.
    fn boss(&mut self, id: usize, land_s: f64, x: f64, y: f64, s: f64) {}
    fn boss_health(&mut self, s: f64, frac: f64) {}
    fn boss_death(&mut self, s: f64, kill_hit: f64) {}
    fn slam_mark(&mut self, k: usize, m: SlamMarkRecord) {}
    fn slam(&mut self, s: SlamRecord) {}
}

/// A recorder shared with its reader: the app (or a test) keeps one handle and gives the sim another.
impl<R: Recorder + ?Sized> Recorder for std::rc::Rc<std::cell::RefCell<R>> {
    fn warp(&mut self, b: WarpBreak, replace_last: bool) {
        self.borrow_mut().warp(b, replace_last);
    }
    fn hero(&mut self, x: f64, y: f64, z: f64, facing: f64, anim: f64, frame: f64) {
        self.borrow_mut().hero(x, y, z, facing, anim, frame);
    }
    fn camera(&mut self, x: f64, y: f64, zoom: f64) {
        self.borrow_mut().camera(x, y, zoom);
    }
    fn spawn(&mut self, rec: EnemyRecord) {
        self.borrow_mut().spawn(rec);
    }
    fn sample(&mut self, id: usize, x: f64, y: f64) {
        self.borrow_mut().sample(id, x, y);
    }
    fn fate(&mut self, id: usize, fate: Fate) {
        self.borrow_mut().fate(id, fate);
    }
    fn death(&mut self, id: usize, w: f64, recycled: bool) {
        self.borrow_mut().death(id, w, recycled);
    }
    fn hit(&mut self, h: Hit) {
        self.borrow_mut().hit(h);
    }
    fn swing(&mut self, sw: PlanSwing) {
        self.borrow_mut().swing(sw);
    }
    fn input(&mut self, i: PlanInput) {
        self.borrow_mut().input(i);
    }
    fn groove(&mut self, s: f64, v: f64) {
        self.borrow_mut().groove(s, v);
    }
    fn tier(&mut self, s: f64, w: f64, tier: f64, sound_s: f64) {
        self.borrow_mut().tier(s, w, tier, sound_s);
    }
    fn health(&mut self, s: f64, frac: f64) {
        self.borrow_mut().health(s, frac);
    }
    fn landing(&mut self, l: Landing) {
        self.borrow_mut().landing(l);
    }
    fn weapon(&mut self, id: &str, s: f64) {
        self.borrow_mut().weapon(id, s);
    }
    fn fire(&mut self, weapon: &str, s: f64) {
        self.borrow_mut().fire(weapon, s);
    }
    fn chain(&mut self, p: ChainPoint) {
        self.borrow_mut().chain(p);
    }
    fn chain_point(&mut self, chain: usize, p: ChainPoint) {
        self.borrow_mut().chain_point(chain, p);
    }
    fn hit_stop(&mut self, s0: f64, s1: f64) {
        self.borrow_mut().hit_stop(s0, s1);
    }
    fn hit_stop_cut(&mut self, s: f64) {
        self.borrow_mut().hit_stop_cut(s);
    }
    fn bite(&mut self, b: BiteRecord) {
        self.borrow_mut().bite(b);
    }
    fn heal(&mut self, h: HealRecord) {
        self.borrow_mut().heal(h);
    }
    fn chicken(&mut self, c: ChickenRecord) {
        self.borrow_mut().chicken(c);
    }
    fn chicken_taken(&mut self, i: usize, s: f64, w: f64, s_heal: f64) {
        self.borrow_mut().chicken_taken(i, s, w, s_heal);
    }
    fn gem(&mut self, g: GemRecord) {
        self.borrow_mut().gem(g);
    }
    fn gem_arrive(&mut self, gem: usize, w: f64) {
        self.borrow_mut().gem_arrive(gem, w);
    }
    fn gem_fly(&mut self, gem: usize, w: f64, s1: f64) {
        self.borrow_mut().gem_fly(gem, w, s1);
    }
    fn gem_grow(&mut self, gem: usize, value: f64, big: bool) {
        self.borrow_mut().gem_grow(gem, value, big);
    }
    fn vacuum(&mut self, v: VacuumRecord) {
        self.borrow_mut().vacuum(v);
    }
    fn vacuum_touch(&mut self, i: usize, s: f64, w: f64, gems: f64) {
        self.borrow_mut().vacuum_touch(i, s, w, gems);
    }
    fn upgrade(&mut self, u: UpgradeRecord) {
        self.borrow_mut().upgrade(u);
    }
    fn upgrade_taken(&mut self, i: usize, k: usize, s: f64, w: f64, s_apply: f64, walked: bool) {
        self.borrow_mut().upgrade_taken(i, k, s, w, s_apply, walked);
    }
    fn xp(&mut self, s: f64, fill: f64) {
        self.borrow_mut().xp(s, fill);
    }
    fn wave(&mut self, cleave: Cleave) {
        self.borrow_mut().wave(cleave);
    }
    fn wave_hit(&mut self, wave: usize, hit: usize) {
        self.borrow_mut().wave_hit(wave, hit);
    }
    fn clock(&mut self, s: f64, w: f64, between: bool) {
        self.borrow_mut().clock(s, w, between);
    }
    fn enemy_step(&mut self, id: usize, w: f64, dur: f64) {
        self.borrow_mut().enemy_step(id, w, dur);
    }
    fn settle(&mut self, r: SettleRecord) {
        self.borrow_mut().settle(r);
    }
    fn formation(&mut self, f: FormationRecord) {
        self.borrow_mut().formation(f);
    }
    fn zone_wave(&mut self, z: ZoneWaveRecord) {
        self.borrow_mut().zone_wave(z);
    }
    fn landmark(&mut self, l: LandmarkRecord) {
        self.borrow_mut().landmark(l);
    }
    fn dance_start(&mut self, id: usize, s_from: f64, anim: &str, alternate: bool) {
        self.borrow_mut().dance_start(id, s_from, anim, alternate);
    }
    fn dance(&mut self, id: usize, d: DanceRecord) {
        self.borrow_mut().dance(id, d);
    }
    fn draw_start(&mut self, id: usize, w0: f64, s_release: f64, dir: f64) {
        self.borrow_mut().draw_start(id, w0, s_release, dir);
    }
    fn attack(&mut self, id: usize, a: AttackRecord) {
        self.borrow_mut().attack(id, a);
    }
    fn arrow(&mut self, a: ArrowRecord) {
        self.borrow_mut().arrow(a);
    }
    fn arrow_end(
        &mut self,
        arrow: usize,
        w_arrive: f64,
        outcome: &'static str,
        x1: f64,
        y1: f64,
        w1: f64,
    ) {
        self.borrow_mut()
            .arrow_end(arrow, w_arrive, outcome, x1, y1, w1);
    }
    fn threat(&mut self, t: ThreatRecord) {
        self.borrow_mut().threat(t);
    }
    fn boss(&mut self, id: usize, land_s: f64, x: f64, y: f64, s: f64) {
        self.borrow_mut().boss(id, land_s, x, y, s);
    }
    fn boss_health(&mut self, s: f64, frac: f64) {
        self.borrow_mut().boss_health(s, frac);
    }
    fn boss_death(&mut self, s: f64, kill_hit: f64) {
        self.borrow_mut().boss_death(s, kill_hit);
    }
    fn slam_mark(&mut self, k: usize, m: SlamMarkRecord) {
        self.borrow_mut().slam_mark(k, m);
    }
    fn slam(&mut self, s: SlamRecord) {
        self.borrow_mut().slam(s);
    }
}

/// A recorder that keeps nothing.
#[derive(Clone, Copy, Debug, Default)]
pub struct NullRecorder;

impl Recorder for NullRecorder {}

fn to_value<T: Serialize>(x: &T) -> Value {
    serde_json::to_value(x).expect("a record serializes")
}

/// The recording's simulation categories as the plan holds them (`LiveRecording` for these
/// categories), for the oracle's event log.
#[derive(Clone, Debug)]
pub struct LogRecorder {
    pub breaks: Vec<WarpBreak>,
    pub inputs: Vec<Value>,
    pub settles: Vec<Value>,
    pub swings: Vec<Value>,
    pub cleaves: Vec<Value>,
    pub groove: (Vec<f64>, Vec<f64>),
    pub tier: (Vec<f64>, Vec<f64>),
    pub health: (Vec<f64>, Vec<f64>),
    pub enemies: Vec<EnemyRecord>,
    pub hits: Vec<Value>,
    pub bites: Vec<Value>,
    pub heals: Vec<Value>,
    pub upgrades: Vec<UpgradeRecord>,
    pub chickens: Vec<ChickenRecord>,
    pub vacuums: Vec<VacuumRecord>,
    pub weapons: Vec<Value>,
    pub chains: Vec<Vec<ChainPoint>>,
    /// `hud.weaponFires`: each weapon's fire times, by weapon id.
    pub fires: std::collections::BTreeMap<String, Vec<f64>>,
    pub xp: (Vec<f64>, Vec<f64>),
    pub formations: Vec<FormationRecord>,
    pub zone_waves: Vec<ZoneWaveRecord>,
    pub landmarks: Vec<LandmarkRecord>,
    pub threats: Vec<ThreatRecord>,
    pub slams: Vec<SlamRecord>,
    pub arrows: Vec<ArrowRecord>,
    pub boss: Option<(usize, f64)>,
}

impl Default for LogRecorder {
    fn default() -> LogRecorder {
        LogRecorder {
            breaks: Vec::new(),
            inputs: Vec::new(),
            settles: Vec::new(),
            swings: Vec::new(),
            cleaves: Vec::new(),
            groove: (vec![0.0], vec![0.0]),
            tier: (vec![0.0], vec![0.0]),
            health: (vec![0.0], vec![1.0]),
            enemies: Vec::new(),
            hits: Vec::new(),
            bites: Vec::new(),
            heals: Vec::new(),
            upgrades: Vec::new(),
            chickens: Vec::new(),
            vacuums: Vec::new(),
            weapons: Vec::new(),
            chains: Vec::new(),
            fires: std::collections::BTreeMap::new(),
            xp: (vec![0.0], vec![0.0]),
            formations: Vec::new(),
            zone_waves: Vec::new(),
            landmarks: Vec::new(),
            threats: Vec::new(),
            slams: Vec::new(),
            arrows: Vec::new(),
            boss: None,
        }
    }
}

impl Recorder for LogRecorder {
    fn warp(&mut self, b: WarpBreak, replace_last: bool) {
        if replace_last {
            *self.breaks.last_mut().expect("a break to replace") = b;
        } else {
            self.breaks.push(b);
        }
    }
    fn spawn(&mut self, rec: EnemyRecord) {
        self.enemies.push(rec);
    }
    fn sample(&mut self, id: usize, _x: f64, _y: f64) {
        self.enemies[id].sample_count += 1.0;
    }
    fn fate(&mut self, id: usize, fate: Fate) {
        self.enemies[id].fates.push(fate);
    }
    fn death(&mut self, id: usize, w: f64, recycled: bool) {
        let e = &mut self.enemies[id];
        e.death_w = w;
        if recycled {
            e.recycled = Some(true);
        }
    }
    fn hit(&mut self, h: Hit) {
        self.hits.push(to_value(&h));
    }
    fn swing(&mut self, sw: PlanSwing) {
        self.swings.push(to_value(&sw));
    }
    fn input(&mut self, i: PlanInput) {
        self.inputs.push(to_value(&i));
    }
    fn groove(&mut self, s: f64, v: f64) {
        self.groove.0.push(s);
        self.groove.1.push(v);
    }
    fn tier(&mut self, s: f64, _w: f64, tier: f64, _sound_s: f64) {
        self.tier.0.push(s);
        self.tier.1.push(tier);
    }
    fn health(&mut self, s: f64, frac: f64) {
        self.health.0.push(s);
        self.health.1.push(frac);
    }
    fn weapon(&mut self, id: &str, s: f64) {
        self.weapons.push(json!({ "id": id, "s": s }));
    }
    fn fire(&mut self, weapon: &str, s: f64) {
        self.fires.entry(weapon.to_string()).or_default().push(s);
    }
    fn chain(&mut self, p: ChainPoint) {
        self.chains.push(vec![p]);
    }
    fn chain_point(&mut self, chain: usize, p: ChainPoint) {
        self.chains[chain].push(p);
    }
    fn bite(&mut self, b: BiteRecord) {
        self.bites.push(to_value(&b));
    }
    fn heal(&mut self, h: HealRecord) {
        self.heals.push(to_value(&h));
    }
    fn chicken(&mut self, c: ChickenRecord) {
        self.chickens.push(c);
    }
    fn chicken_taken(&mut self, i: usize, s: f64, w: f64, s_heal: f64) {
        let c = &mut self.chickens[i];
        c.s_taken = Some(s);
        c.w_taken = Some(w);
        c.s_heal = Some(s_heal);
    }
    fn vacuum(&mut self, v: VacuumRecord) {
        self.vacuums.push(v);
    }
    fn vacuum_touch(&mut self, i: usize, s: f64, w: f64, gems: f64) {
        let v = &mut self.vacuums[i];
        v.s_touch = Some(s);
        v.w_touch = Some(w);
        v.gems = Some(gems);
    }
    fn upgrade(&mut self, u: UpgradeRecord) {
        self.upgrades.push(u);
    }
    fn upgrade_taken(&mut self, i: usize, k: usize, s: f64, w: f64, s_apply: f64, walked: bool) {
        let u = &mut self.upgrades[i];
        u.taken = k as i32;
        u.s_taken = Some(s);
        u.w_taken = Some(w);
        u.s_apply = Some(s_apply);
        u.walked = Some(walked);
    }
    fn xp(&mut self, s: f64, fill: f64) {
        self.xp.0.push(s);
        self.xp.1.push(fill);
    }
    fn wave(&mut self, cleave: Cleave) {
        self.cleaves.push(to_value(&cleave));
    }
    fn enemy_step(&mut self, id: usize, w: f64, dur: f64) {
        self.enemies[id]
            .steps
            .get_or_insert_with(Vec::new)
            .push(EnemyStep { w, dur });
    }
    fn settle(&mut self, r: SettleRecord) {
        self.settles.push(to_value(&r));
    }
    fn formation(&mut self, f: FormationRecord) {
        self.formations.push(f);
    }
    fn zone_wave(&mut self, z: ZoneWaveRecord) {
        self.zone_waves.push(z);
    }
    fn landmark(&mut self, l: LandmarkRecord) {
        self.landmarks.push(l);
    }
    fn dance_start(&mut self, id: usize, s_from: f64, anim: &str, alternate: bool) {
        let w0 = Warp::new(self.breaks.clone()).to_world(s_from);
        self.enemies[id].dance = Some(DanceRecord {
            w0,
            w1: f64::INFINITY,
            anim: anim.to_string(),
            alternate,
            phase: 0.0,
        });
    }
    fn dance(&mut self, id: usize, d: DanceRecord) {
        self.enemies[id].dance = Some(d);
    }
    fn draw_start(&mut self, id: usize, w0: f64, s_release: f64, dir: f64) {
        let w = Warp::new(self.breaks.clone()).to_world(s_release);
        self.enemies[id]
            .attacks
            .get_or_insert_with(Vec::new)
            .push(AttackRecord {
                w0,
                w,
                kind: "shoot",
                dir,
                provisional: Some(true),
            });
    }
    fn attack(&mut self, id: usize, a: AttackRecord) {
        let list = self.enemies[id].attacks.get_or_insert_with(Vec::new);
        // The attack replaces its provisional wind-up.
        match list
            .iter()
            .rposition(|x| x.provisional == Some(true) && x.kind == a.kind)
        {
            Some(i) => list[i] = a,
            None => list.push(a),
        }
    }
    fn arrow(&mut self, a: ArrowRecord) {
        self.arrows.push(a);
    }
    fn arrow_end(
        &mut self,
        arrow: usize,
        w_arrive: f64,
        outcome: &'static str,
        x1: f64,
        y1: f64,
        w1: f64,
    ) {
        let a = &mut self.arrows[arrow];
        a.w_arrive = w_arrive;
        a.outcome = outcome;
        a.x1 = x1;
        a.y1 = y1;
        a.w1 = w1;
    }
    fn threat(&mut self, t: ThreatRecord) {
        self.threats.push(t);
    }
    fn boss(&mut self, id: usize, land_s: f64, _x: f64, _y: f64, _s: f64) {
        self.boss = Some((id, land_s));
    }
    fn slam_mark(&mut self, _k: usize, m: SlamMarkRecord) {
        let w_land = Warp::new(self.breaks.clone()).to_world(m.s_land);
        self.enemies[m.boss]
            .attacks
            .get_or_insert_with(Vec::new)
            .push(AttackRecord {
                w0: m.w_mark.max(w_land - 0.6),
                w: w_land,
                kind: "slam",
                dir: m.dir,
                provisional: Some(true),
            });
    }
    fn slam(&mut self, s: SlamRecord) {
        self.slams.push(s);
    }
}

/// An upgrade record as the plan writes it.
pub fn upgrade_json(u: &UpgradeRecord) -> Value {
    let mut v = json!({
        "s": u.s,
        "offers": u.offers.iter().map(|o| json!({ "kind": o.kind.name(), "id": o.id })).collect::<Vec<_>>(),
        "taken": u.taken,
    });
    let set = |v: &mut Value, k: &str, x: Option<f64>| {
        if let Some(x) = x {
            v[k] = json!(x);
        }
    };
    set(&mut v, "w", u.w);
    if let Some(orbs) = &u.orbs {
        v["orbs"] = orbs.iter().map(|p| json!({ "x": p.x, "y": p.y })).collect();
    }
    set(&mut v, "sTaken", u.s_taken);
    set(&mut v, "wTaken", u.w_taken);
    set(&mut v, "sApply", u.s_apply);
    if let Some(w) = u.walked {
        v["walked"] = json!(w);
    }
    v
}

/// Any record as a JSON value.
pub fn record_json<T: Serialize>(x: &T) -> Value {
    to_value(x)
}
