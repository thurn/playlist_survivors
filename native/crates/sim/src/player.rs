//! `src/sim/player.ts`'s interface between the simulation and a player: the key events a player
//! makes, what it sees when it decides, and the decision schedule. The virtual players themselves
//! (`VirtualPlayer`, `MasherPlayer`, `HolderPlayer`) live in the players crate.

use crate::movement::InputKey;
use crate::upgrades::UpgradeItem;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum KeyEventKind {
    Press,
    Release,
}

/// A key press or release at song time `s`.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct KeyEvent {
    pub s: f64,
    pub kind: KeyEventKind,
    /// A direction, or Space (a stomp).
    pub key: InputKey,
    /// Play mode's log: which keyboard event this is (the simulation passes it on to its traces).
    pub id: Option<f64>,
}

/// The hero as a player sees it.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct HeroView {
    pub x: f64,
    pub y: f64,
    pub stepping: bool,
    /// With strides, how far a press would stride now (the budget).
    pub stride: Option<f64>,
    /// With press classes, the tier.
    pub tier: Option<f64>,
    /// With gems on the field, the pickup radius.
    pub pickup: Option<f64>,
    pub health: Option<f64>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct EnemyView {
    pub x: f64,
    pub y: f64,
    pub kind: String,
    pub health: f64,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ThreatKind {
    Arrow,
    Slam,
}

/// A visible threat: an arrow's line runs from (x, y) through its aim point (ax, ay); a slam marker
/// is a circle of `radius` (already grown by the hurtbox) around (x, y).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ThreatView {
    pub kind: ThreatKind,
    pub x: f64,
    pub y: f64,
    pub ax: Option<f64>,
    pub ay: Option<f64>,
    pub lands_at: f64,
    pub radius: Option<f64>,
    pub seen_at: f64,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Point {
    pub x: f64,
    pub y: f64,
}

/// A closing ring: its centre, radius now, and the angles of its gaps.
#[derive(Clone, Debug, PartialEq)]
pub struct RingView {
    pub x: f64,
    pub y: f64,
    pub r: f64,
    pub gaps: Vec<f64>,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct RunnerView {
    pub x: f64,
    pub y: f64,
    pub vx: f64,
    pub vy: f64,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct GemView {
    pub x: f64,
    pub y: f64,
    pub value: f64,
}

#[derive(Clone, Debug, PartialEq)]
pub struct OrbView {
    pub x: f64,
    pub y: f64,
    pub item: UpgradeItem,
}

/// What a player sees when it decides (`Observation`).
#[derive(Clone, Debug, PartialEq)]
pub struct Observation {
    pub run: Option<crate::survivors::RunState>,
    /// Song and world time now.
    pub s: f64,
    pub w: f64,
    pub hero: HeroView,
    pub enemies: Vec<EnemyView>,
    pub threats: Vec<ThreatView>,
    /// The boss, when it is in the fight.
    pub boss: Option<Point>,
    /// The middle of a formation still dancing in its slots.
    pub formation: Option<Point>,
    pub ring: Option<RingView>,
    pub runners: Option<Vec<RunnerView>>,
    /// Gems near the hero, and the nearest vacuum waiting to be touched.
    pub gems: Option<Vec<GemView>>,
    pub vacuum: Option<Point>,
    /// Upgrade orbs waiting to be walked into; with a full experience bar, when its level-up comes.
    pub orbs: Option<Vec<OrbView>>,
    pub level_up_at: Option<f64>,
    /// Chickens lying in sight.
    pub chickens: Option<Vec<Point>>,
    /// Unopened treasure on the visible field.
    pub chests: Option<Vec<Point>>,
    /// The hero's position and heading now.
    pub pace_point: crate::world::Frame,
}

/// A player (`Player`).
pub trait Player {
    /// Song time of the player's next decision after `after` (infinite when it is done).
    fn next_decision(&mut self, after: f64) -> f64;
    /// Decides at the observation's song time: the key events it makes before its next decision.
    fn decide(&mut self, obs: &Observation) -> Vec<KeyEvent>;
}
