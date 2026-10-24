//! `src/sim/tiers.ts`: what a tier changes (docs/design/gameplay-redesign.md, "What a tier
//! changes"): attack area, and a little damage. The content gives each value at tiers 0 to 3; a tier
//! past the last uses the last.

use crate::content::types::{ArcShape, HeroDef, OrbitTier, WeaponDef};
use crate::jsmath::{floor, max, min};

/// `values[max(0, min(len - 1, floor(tier)))]`. (A NaN tier reads undefined in TS and then throws;
/// here it panics.)
fn at<T>(values: &[T], tier: f64) -> &T {
    let i = max(0.0, min(values.len() as f64 - 1.0, floor(tier)));
    assert!(
        !i.is_nan() && !values.is_empty(),
        "tier {tier} of {} values",
        values.len()
    );
    &values[i as usize]
}

/// The light swing's arc at a tier (radius in px, degrees; 360 is a full spin).
pub fn swing_arc_at(hero: &HeroDef, tier: f64) -> ArcShape {
    let l = &hero.swings.light;
    l.tiers
        .as_ref()
        .map_or(&l.hit_shape, |t| at(t, tier))
        .clone()
}

/// The heavy impact's arc at a tier, or the heavy swing's own.
pub fn heavy_arc_at(hero: &HeroDef, tier: f64) -> ArcShape {
    let h = &hero.swings.heavy;
    h.tiers
        .as_ref()
        .map_or(&h.hit_shape, |t| at(t, tier))
        .clone()
}

/// How far a cleave's shockwave reaches at a tier, or the heavy swing's own.
pub fn wave_reach_at(hero: &HeroDef, tier: f64) -> f64 {
    let w = &hero.swings.heavy.wave;
    w.tiers.as_ref().map_or(w.to, |t| *at(t, tier))
}

/// Every hero attack's damage multiplier at a tier.
pub fn tier_damage(hero: &HeroDef, tier: f64) -> f64 {
    *at(&hero.meter.damage, tier)
}

/// Orbiting blades at a tier: how many, and the orbit's radius. (The weapon must orbit.)
pub fn orbit_at(wd: &WeaponDef, tier: f64) -> OrbitTier {
    let o = wd.orbit.as_ref().expect("an orbiting weapon");
    o.tiers.as_ref().map_or_else(
        || OrbitTier {
            count: o.count,
            radius: o.radius,
        },
        |t| at(t, tier).clone(),
    )
}

/// Chain lightning at a tier: its jumps, and how far each reaches; None without tiers.
pub fn chain_at(wd: &WeaponDef, tier: f64) -> Option<crate::content::types::ChainTier> {
    let c = wd.chain.as_ref().expect("a chain weapon");
    c.tiers.as_ref().map(|t| at(t, tier).clone())
}

/// The fire trail's hit radius at a tier.
pub fn trail_radius_at(wd: &WeaponDef, tier: f64) -> f64 {
    match wd.trail.as_ref().and_then(|t| t.tiers.as_ref()) {
        Some(t) => at(t, tier).radius,
        None => wd.hit_shape.as_ref().expect("a hit shape").radius,
    }
}
