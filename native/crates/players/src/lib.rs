//! Virtual players (pure): `src/sim/player.ts`'s virtual player, masher and holder, and
//! `src/reactive/players.ts`'s players for reactive tracks, which press against the track's
//! reference beats, as a person hears the music, never against committed beats.
//!
//! A player sees only the game state when it decides ([`sim::player::Observation`]), never the
//! simulation's future. The sim takes its keys as it takes a person's; [`drive`] runs a sim with one.
#![forbid(unsafe_code)]

mod others;
mod survivors;
pub use survivors::{SurvivorBot, SurvivorBotKind};
mod reactive;
mod virtual_player;

pub use others::{HolderPlayer, MasherPlayer};
pub use reactive::{Reference, ReferenceSection, reactive_player};
pub use virtual_player::{PROFILES, PlayerSection, ProfileSpec, Slot, VirtualPlayer, profile};

use sim::engine::Sim;
use sim::player::Player;

/// Runs `sim` with `player` to song time `until` (or the run's end): at each decision, the sim is
/// stepped to it, the player sees the game then, and its keys go in as a person's would.
pub fn drive(sim: &mut Sim, player: &mut dyn Player, until: f64) {
    let mut after = sim.s();
    loop {
        let t = player.next_decision(after);
        if !t.is_finite() || t >= until || sim.done() {
            break;
        }
        // (A decision is never in the sim's past.)
        let t = t.max(sim.s());
        sim.advance_to(t);
        if sim.done() {
            break;
        }
        for k in player.decide(&sim.observe()) {
            sim.key(sim::player::KeyEvent { s: k.s.max(t), ..k });
        }
        after = t + 1e-6;
    }
    sim.advance_to(until);
}
