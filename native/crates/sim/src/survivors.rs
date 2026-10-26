//! State the survivors sim publishes while song time continues through a freeze.

mod arsenal;
mod bible;
mod lightning;
pub use bible::*;
pub use lightning::*;
mod offers;
pub use offers::*;
mod spawning;
pub use spawning::*;
mod pickups;
mod progression;
pub use pickups::*;
mod stats;
mod weapons;
pub use arsenal::*;
pub use progression::*;
pub use stats::*;
pub use weapons::*;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FreezeReason {
    Paused,
    MusicStopped,
    LevelUp,
    Chest,
}

impl FreezeReason {
    pub fn banner(self) -> &'static str {
        match self {
            Self::Paused => "PAUSED",
            Self::MusicStopped => "MUSIC STOPPED",
            Self::LevelUp => "LEVEL UP",
            Self::Chest => "TREASURE",
        }
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct RunState {
    pub song: f64,
    pub presentation_s: f64,
    pub frozen_beat: Option<f64>,
    pub elapsed: f64,
    pub spawn_minute: Option<usize>,
    pub spawned: usize,
    pub offer: Option<Offer>,
    pub level: usize,
    pub max_hp: f64,
    pub world: f64,
    pub frozen: Option<FreezeReason>,
    pub selected: usize,
    /// Consumed by the offer/chest rules, never by movement.
    pub confirmed: bool,
}
