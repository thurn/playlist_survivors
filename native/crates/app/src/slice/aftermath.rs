//! Catalog death decals from the current replayable view, never a stale frame-local kill.
use super::{fx, scene::Look};
use eval::live::{AFTERMATH_LIFE, LiveView};
use render::{atlas::Atlases, sprites::Sprite};
use sim::content::{ContentLibrary, types::EffectLayer};
use std::collections::HashMap;
pub fn draw(
    view: &LiveView,
    art: &Atlases,
    lib: &ContentLibrary,
    looks: &HashMap<String, Look>,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    out: &mut Vec<Sprite>,
) {
    let Some(run) = view.run else {
        return;
    };
    for mark in &view.aftermath {
        let age = run.world - mark.world;
        if !(0.0..AFTERMATH_LIFE).contains(&age) {
            continue;
        }
        let Some(look) = looks.get(&mark.kind) else {
            continue;
        };
        let Some(recipe) = lib.effects.get(&look.death) else {
            continue;
        };
        let fade = ((age - 0.6 * AFTERMATH_LIFE) / (0.4 * AFTERMATH_LIFE)).clamp(0.0, 1.0);
        let opacity = (20.0 * age).min(1.0) * (1.0 - fade * fade * (3.0 - 2.0 * fade)) * 0.55;
        for layer in &recipe.layers {
            let EffectLayer::Decal {
                art: name,
                size,
                tint,
                ..
            } = layer
            else {
                continue;
            };
            let Some(region) = fx::proc_art(art, name, mark.foe as u64) else {
                continue;
            };
            let mut color = match tint.as_deref() {
                Some("debris") => look
                    .debris
                    .first()
                    .map_or_else(|| fx::hex("#802020"), |c| fx::hex(c)),
                Some(c) => fx::hex(c),
                None => [1.0; 4],
            };
            color[3] *= opacity as f32;
            let k = fx::param(size, mark.intensity) as f32 * scale;
            let (x, y) = at(mark.x, mark.y);
            let angle = if name.ends_with("scorch") {
                0.0
            } else {
                (fx::rand01(mark.foe as u64, 99, 0) * std::f64::consts::TAU) as f32
            };
            out.push(
                Sprite::centred(region, x, y, region.w as f32 * k, region.h as f32 * k)
                    .tinted(color)
                    .rotated(angle),
            );
        }
    }
}
