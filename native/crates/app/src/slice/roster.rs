//! The final base roster's asset choices; no combat stats are changed here.
use super::scene::Look;
pub mod check;
use render::sprites::{Arc, Sprite};
use sim::content::types::EnemyDef;
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Style {
    Character,
    Brute,
    Plant,
}
pub fn look(d: &EnemyDef) -> Look {
    let (style, scale) = if d.atlas == "brute" {
        (Style::Brute, d.scale as f32 * 0.5)
    } else if d.atlas.starts_with("thorn-plant-") {
        (Style::Plant, d.scale as f32 * 0.6)
    } else {
        (Style::Character, d.scale as f32)
    };
    let tint = match d.id.as_str() {
        "vs-xlbat" => [0.7, 0.65, 0.85],
        "vs-boss-xlmantis" => [1.0, 0.75, 0.45],
        "vs-boss-werewolf" => [1.0, 0.55, 0.5],
        "vs-boss-xlmummy" => [0.95, 0.8, 0.5],
        "vs-boss-xlflower" => [0.8, 0.6, 1.0],
        "vs-xlmummy" => [0.95, 0.85, 0.7],
        "vs-xlmantis" => [0.8, 0.95, 0.8],
        _ => [1.0; 3],
    };
    Look {
        atlas: d.atlas.clone(),
        scale,
        tint,
        style,
        death: d.death.recipe.clone(),
        debris: d.death.debris.clone(),
    }
}
pub fn stomp(bar: f64, at: (f32, f32), scale: f32, share: f32, out: &mut Vec<Sprite>) {
    let phase = bar.rem_euclid(1.0);
    if phase >= 0.25 {
        return;
    }
    let p = (phase / 0.25) as f32;
    let fade = (1.0 - p).powi(2);
    out.push(Sprite::arc(
        at.0,
        at.1,
        (12.0 + 42.0 * p) * scale,
        Arc {
            head: 0.0,
            span: std::f32::consts::TAU,
            width: 2.0 * scale,
        },
        [0.65, 0.55, 0.4, 0.18 * fade * share],
    ));
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::slice::{World, live};
    use render::atlas::Atlases;
    #[test]
    fn roster_uses_baked_directional_art_or_anchored_thorn_props() {
        let world = World::load(None).unwrap();
        let art = Atlases::load(&live::resource("assets", "../../../public/assets")).unwrap();
        for (id, atlas, style, scale, cycle, pattern) in [
            ("vs-mudman1", "orc", Style::Character, 1.0, 2.0, "x."),
            ("vs-mudman2", "orc-elite", Style::Character, 1.0, 2.0, "x."),
            ("vs-werewolf", "orc-ember", Style::Character, 1.0, 1.0, "x"),
            ("vs-xlbat", "bat", Style::Character, 2.0, 1.0, "x"),
            (
                "vs-boss-xlmantis",
                "minotaur",
                Style::Character,
                2.0,
                1.0,
                "x",
            ),
            (
                "vs-boss-werewolf",
                "orc-elite",
                Style::Character,
                2.0,
                1.0,
                "x",
            ),
            ("vs-boss-xlmummy", "brute", Style::Brute, 1.5, 1.0, "x"),
            ("vs-boss-xlflower", "brute", Style::Brute, 1.5, 1.0, "x"),
            ("vs-xlmantis", "brute", Style::Brute, 1.0, 2.0, "x."),
            ("vs-xlmummy", "brute", Style::Brute, 1.0, 2.0, "x."),
            (
                "vs-flower",
                "thorn-plant-summer",
                Style::Plant,
                0.6,
                2.0,
                "x.",
            ),
            (
                "vs-xlflower",
                "thorn-plant-autumn",
                Style::Plant,
                1.2,
                2.0,
                "x.",
            ),
        ] {
            let d = &world.lib.enemies[id];
            let look = look(d);
            assert_eq!(look.atlas, atlas, "{id}");
            assert_eq!(look.style, style, "{id}");
            assert!((look.scale - scale).abs() < 1e-6, "{id}");
            assert!((d.walk_cycle_beats - cycle).abs() < 1e-6, "{id}");
            assert_eq!(d.gait.as_ref().unwrap().pattern, pattern, "{id}");
            if style == Style::Plant {
                let p = art.props[atlas][0];
                assert!(p.region.w > 0 && p.region.h > 0 && p.anchor_y > 0.0);
                assert_eq!(d.hop, Some(false));
            } else {
                for name in ["walk", "hurt"] {
                    let a = art.animation(atlas, name).unwrap();
                    for (frames, flash) in a.frames.iter().zip(&a.flash) {
                        assert!(!frames.is_empty(), "{id}/{name}");
                        assert_eq!(frames.len(), flash.len(), "{id}/{name}");
                    }
                }
            }
        }
    }
}
