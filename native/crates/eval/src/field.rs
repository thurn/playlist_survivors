//! The open field (`src/mapgen/openfield.ts`, read as a reference): ground in 512 px chunks around
//! the camera, each a deterministic function of the seed, its coordinates and its zone: worn
//! patches, underfoot decorations (the densest) and sparse tall props, which fade when the hero
//! comes close.

use sim::content::types::ZoneTheme;
use sim::core::rng::{Part, Rng, hash32};

/// Chunk size in world pixels (16 tiles).
pub const CHUNK: f64 = 512.0;
pub const TILE: f64 = 32.0;
/// Tall props fade to this opacity when the hero comes within FADE_NEAR (from FADE_FAR).
pub const PROP_FADE: f64 = 0.3;
pub const FADE_NEAR: f64 = 80.0;
pub const FADE_FAR: f64 = 120.0;

/// A prop placed in the world.
#[derive(Clone, Debug, PartialEq)]
pub struct FieldProp {
    pub sprite: String,
    pub variant: usize,
    pub x: f64,
    pub y: f64,
    pub flip: bool,
}

/// What a chunk holds: props drawn under everything, and tall props drawn by depth.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct ChunkContent {
    pub ground: Vec<FieldProp>,
    pub tall: Vec<FieldProp>,
}

/// Whether a prop stands tall, and its footprint (`DEFAULT_PROPS`).
pub fn prop_info(sprite: &str) -> (bool, f64) {
    match sprite {
        "tree-summer" | "tree-autumn" | "ruin-wall" => (true, 30.0),
        "tree-dead" => (true, 24.0),
        "bush-summer" | "bush-autumn" | "standing-stone" | "ruin-pillar" => (true, 16.0),
        "mushroom-big" => (true, 18.0),
        "rock-big" => (true, 20.0),
        "brazier" => (true, 10.0),
        "mushroom" | "flower-summer" | "flower-autumn" | "grass-tuft" => (false, 6.0),
        "rock-small" => (false, 8.0),
        "campfire" => (false, 14.0),
        "lava-pool" | "patch-dirt-summer" | "patch-dirt-autumn" | "patch-dirt-ember"
        | "patch-lava" => (false, 40.0),
        _ => (false, 10.0),
    }
}

/// The ground tile's variant at tile (tx, ty): the same in every zone.
pub fn tile_variant(seed: f64, tx: i64, ty: i64) -> usize {
    (hash32(&[
        Part::Num(seed),
        Part::Str("open-tile"),
        Part::Num(tx as f64),
        Part::Num(ty as f64),
    ]) as u32
        & 0xff) as usize
}

/// Chunk (cx, cy)'s props in a zone.
pub fn chunk_content(seed: f64, cx: i64, cy: i64, theme: &ZoneTheme) -> ChunkContent {
    let mut r = Rng::new(&[
        Part::Num(seed),
        Part::Str("open-chunk"),
        Part::Num(cx as f64),
        Part::Num(cy as f64),
        Part::Str(&theme.id),
    ]);
    let (x0, y0) = (cx as f64 * CHUNK, cy as f64 * CHUNK);
    let mut out = ChunkContent::default();
    let mut taken: Vec<(f64, f64, f64)> = Vec::new();
    let patches = 4 + r.int(0.0, 3.0) as usize;
    for _ in 0..patches {
        let variant = r.int(0.0, 255.0) as usize;
        let x = (x0 + r.next() * CHUNK).round();
        let y = (y0 + r.next() * CHUNK).round();
        out.ground.push(FieldProp {
            sprite: theme.terrain.patch.clone(),
            variant,
            x,
            y,
            flip: r.next() < 0.5,
        });
    }
    let attempts = ((CHUNK / TILE) * (CHUNK / TILE) * 0.9) as usize;
    for _ in 0..attempts {
        let x = x0 + r.next() * CHUNK;
        let y = y0 + r.next() * CHUNK;
        let roll = r.next();
        let variant = r.int(0.0, 255.0) as usize;
        let flip = r.next() < 0.5;
        let mut acc = 0.0;
        let chosen = theme.props.iter().find(|c| {
            let (tall, _) = prop_info(&c.sprite);
            // Underfoot decorations thickest; trees, bushes and ruins much sparser.
            acc += c.density
                * if c.underfoot {
                    0.45
                } else if tall {
                    0.1
                } else {
                    0.3
                };
            roll < acc
        });
        let Some(chosen) = chosen else {
            continue;
        };
        let (tall, radius) = prop_info(&chosen.sprite);
        let rad = if tall { radius * 3.0 } else { radius };
        if !taken
            .iter()
            .all(|&(ox, oy, or)| (ox - x).powi(2) + (oy - y).powi(2) >= (or + rad).powi(2))
        {
            continue;
        }
        taken.push((x, y, rad));
        let p = FieldProp {
            sprite: chosen.sprite.clone(),
            variant,
            x: x.round(),
            y: y.round(),
            flip,
        };
        if tall || chosen.light.is_some() {
            out.tall.push(p);
        } else {
            out.ground.push(p);
        }
    }
    out.tall
        .sort_by(|a, b| a.y.total_cmp(&b.y).then(a.x.total_cmp(&b.x)));
    out
}

/// A tall prop's opacity with the hero at (hx, hy).
pub fn prop_fade(x: f64, y: f64, hx: f64, hy: f64) -> f64 {
    let d = (x - hx).hypot(y - hy);
    if d >= FADE_FAR {
        1.0
    } else if d <= FADE_NEAR {
        PROP_FADE
    } else {
        PROP_FADE + (1.0 - PROP_FADE) * (d - FADE_NEAR) / (FADE_FAR - FADE_NEAR)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fading_is_full_far_and_least_near() {
        assert_eq!(prop_fade(0.0, 0.0, 200.0, 0.0), 1.0);
        assert_eq!(prop_fade(0.0, 0.0, 10.0, 0.0), PROP_FADE);
        assert!((prop_fade(0.0, 0.0, 100.0, 0.0) - 0.65).abs() < 1e-12);
    }
}
