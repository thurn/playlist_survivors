//! Effects from the content's recipes (`content/effects.yaml`), drawn as `plan/evaluate.ts` and
//! `runtime/renderer.ts` draw them (native/evidence/vfx/ts-spec.md): bursts of particles under drag
//! and gravity, rings, sprites (a swing's arc fitted to its hit's shape and tinted by its tier),
//! decals left on the ground, camera shake and the strongest flash; and for the post pass, lights,
//! chromatic aberration and shockwaves. The procedural arts are `render::proc`'s textures.
#![allow(
    clippy::many_single_char_names,
    clippy::too_many_arguments,
    clippy::too_many_lines
)]

use render::atlas::{Atlases, Region};
use render::font::{self, Align as TextAlign};
use render::post::{Grade, Light, MAX_WAVES};
use sim::content::types::{Align, EffectLayer, EffectRecipe, Param};
use sim::core::geometry::Shape;

use super::gfx::{Sprite, rect};

fn param(p: &Param, intensity: f64) -> f64 {
    match p {
        Param::Num(v) => *v,
        Param::Range([lo, hi]) => lo + (hi - lo) * intensity.clamp(0.0, 1.0),
    }
}

/// `#rrggbb` as RGBA.
pub fn hex(s: &str) -> [f32; 4] {
    let v = u32::from_str_radix(s.trim_start_matches('#'), 16).unwrap_or(0x00ff_ffff);
    [
        ((v >> 16) & 0xff) as f32 / 255.0,
        ((v >> 8) & 0xff) as f32 / 255.0,
        (v & 0xff) as f32 / 255.0,
        1.0,
    ]
}

/// A swing's colour by the tier at its wind-up (`SWORD_TINTS`).
pub const SWORD_TINTS: [u32; 4] = [0x00ff_ffff, 0x009f_e4ff, 0x009f_ffd8, 0x00ff_e070];
/// A swing hit's sprites' opacity and bursts' count by its tier (`TIER_ALPHA`, `TIER_COUNT`).
const TIER_ALPHA: [f32; 4] = [0.5, 0.7, 0.88, 1.0];
const TIER_COUNT: [f64; 4] = [0.5, 0.75, 1.0, 1.3];
/// How long a decal lies on the ground (s).
const DECAL_LIFE: f64 = 20.0;

pub fn rgb(c: u32) -> [f32; 4] {
    [
        ((c >> 16) & 0xff) as f32 / 255.0,
        ((c >> 8) & 0xff) as f32 / 255.0,
        (c & 0xff) as f32 / 255.0,
        1.0,
    ]
}

/// A number in [0, 1) from a seed and two indices (the particles' own randoms).
pub fn rand01(seed: u64, i: u64, k: u64) -> f64 {
    let mut z = seed
        .wrapping_add(i.wrapping_mul(0x9e37_79b9_7f4a_7c15))
        .wrapping_add(k.wrapping_mul(0xbf58_476d_1ce4_e5b9));
    z = (z ^ (z >> 30)).wrapping_mul(0xbf58_476d_1ce4_e5b9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94d0_49bb_1331_11eb);
    z ^= z >> 31;
    (z >> 11) as f64 / (1u64 << 53) as f64
}

/// How an effect starts besides its place and heading: a dying enemy's debris colours, a swing's
/// tier (its tint, sprites' opacity and bursts' count) and hit shape, and a size factor.
#[derive(Clone, Debug, Default)]
pub struct Start {
    pub debris: Vec<String>,
    pub tier: Option<usize>,
    pub shape: Option<Shape>,
    pub scale: Option<f64>,
    /// Survivors per-hit particle multiplier; long-lived hit decals are capped at1s.
    pub density: Option<f64>,
    /// Boss-role showpieces keep their recipe even when its name is an ordinary death.
    pub protected: bool,
}

/// One recipe playing at a place.
struct Instance {
    recipe: EffectRecipe,
    x: f64,
    y: f64,
    dir: f64,
    at: f64,
    intensity: f64,
    seed: u64,
    start: Start,
    until: f64,
}

fn retained_life(recipe: &EffectRecipe, start: &Start, intensity: f64) -> f64 {
    if start.density.is_none() {
        return DECAL_LIFE + 1.0;
    }
    recipe
        .layers
        .iter()
        .map(|layer| match layer {
            EffectLayer::Decal { .. } => 1.0,
            EffectLayer::Shake { trauma, .. } => param(trauma, intensity) / 1.6,
            EffectLayer::Sprite { life, .. }
            | EffectLayer::Burst { life, .. }
            | EffectLayer::Light { life, .. }
            | EffectLayer::Flash { life, .. }
            | EffectLayer::Distortion { life, .. }
            | EffectLayer::Ring { life, .. }
            | EffectLayer::Aberration { life, .. }
            | EffectLayer::Text { life, .. } => *life,
        })
        .fold(0.0, f64::max)
}
/// The effects playing.
#[derive(Default)]
pub struct Effects {
    live: Vec<Instance>,
    count: u64,
}

impl Effects {
    /// Starts `recipe` at (x, y), heading `dir`, at intensity `intensity` (0-1), at host time
    /// `now`.
    pub fn start(
        &mut self,
        recipe: &EffectRecipe,
        x: f64,
        y: f64,
        dir: f64,
        intensity: f64,
        start: Start,
        now: f64,
    ) {
        self.count += 1;
        let seed = (x * 4.0).round().to_bits()
            ^ (y * 4.0).round().to_bits().rotate_left(17)
            ^ self.count.wrapping_mul(0x2545_f491_4f6c_dd1d);
        let until = now + retained_life(recipe, &start, intensity);
        self.live.push(Instance {
            until,
            recipe: recipe.clone(),
            x,
            y,
            dir,
            at: now,
            intensity,
            seed,
            start,
        });
        // (The longest layer is a decal's.)
        self.live.retain(|i| now < i.until);
    }

    /// The camera's shake offset at `now` (world px): `9 trauma^2`, the trauma each shake layer's
    /// less 1.6 a second, summed.
    pub fn shake(&self, now: f64) -> (f64, f64) {
        let mut trauma = 0.0;
        for inst in &self.live {
            let age = now - inst.at;
            for layer in &inst.recipe.layers {
                if let EffectLayer::Shake { trauma: t, .. } = layer {
                    trauma += (param(t, inst.intensity) - 1.6 * age).max(0.0);
                }
            }
        }
        let shake = 9.0 * trauma.min(1.0f64).powi(2);
        (shake * (now * 61.0).sin(), shake * (now * 47.0 + 1.3).sin())
    }

    /// The effects' lights, aberration and shockwaves at `now`; `at` maps a world point to the
    /// screen, `scale` world to screen.
    pub fn post(
        &self,
        now: f64,
        at: &dyn Fn(f64, f64) -> (f32, f32),
        scale: f32,
        lights: &mut Vec<Light>,
        grade: &mut Grade,
    ) {
        let mut waves = 0;
        for inst in &self.live {
            let age = now - inst.at;
            let i = inst.intensity;
            for layer in &inst.recipe.layers {
                match layer {
                    EffectLayer::Light {
                        color,
                        radius,
                        life,
                        strength,
                        ..
                    } if age <= *life => {
                        let u = age / life;
                        let (x, y) = at(inst.x, inst.y - 12.0);
                        let c = hex(color);
                        lights.push(Light {
                            x,
                            y,
                            radius: (param(radius, i) * (1.0 + 0.3 * u)) as f32 * scale,
                            color: [c[0], c[1], c[2]],
                            strength: (param(strength, i) * (1.0 - u).powf(1.5)) as f32,
                        });
                    }
                    // (The only layer the I < 0.8 cut touches.)
                    EffectLayer::Aberration { strength, life, .. }
                        if age <= *life && (i >= 0.8 || inst.recipe.id == "finale-blast") =>
                    {
                        let a = (param(strength, i) * (1.0 - age / life)) as f32;
                        grade.aberration = grade.aberration.max(a);
                    }
                    EffectLayer::Distortion {
                        radius,
                        life,
                        strength,
                        ..
                    } if age <= *life && waves < MAX_WAVES => {
                        let u = age / life;
                        let (x, y) = at(inst.x, inst.y);
                        grade.waves[waves] = [
                            x,
                            y,
                            (param(radius, i) * u) as f32 * scale,
                            (param(strength, i) * (1.0 - u)) as f32,
                        ];
                        waves += 1;
                    }
                    _ => {}
                }
            }
        }
    }

    /// The decals on the ground at `now` (drawn under the characters).
    pub fn draw_decals(
        &self,
        art: &Atlases,
        now: f64,
        at: &dyn Fn(f64, f64) -> (f32, f32),
        scale: f32,
        out: &mut Vec<Sprite>,
    ) {
        for inst in &self.live {
            let age = now - inst.at;
            for layer in &inst.recipe.layers {
                let EffectLayer::Decal {
                    art: name,
                    size,
                    tint,
                    ..
                } = layer
                else {
                    continue;
                };
                let Some(region) = proc_art(art, name, inst.seed) else {
                    continue;
                };
                let s = param(size, inst.intensity) * inst.start.scale.unwrap_or(1.0);
                let l = if inst.start.density.is_some() {
                    1.0
                } else {
                    DECAL_LIFE
                };
                if age >= l {
                    continue;
                }
                let fade = ((age - 0.6 * l) / (0.4 * l)).clamp(0.0, 1.0);
                let alpha = (20.0 * age).min(1.0) * (1.0 - fade * fade * (3.0 - 2.0 * fade)) * 0.85;
                let mut color = match tint.as_deref() {
                    Some("debris") => inst
                        .start
                        .debris
                        .first()
                        .map_or_else(|| hex("#802020"), |c| hex(c)),
                    Some(c) => hex(c),
                    None => [1.0; 4],
                };
                color[3] *= alpha as f32;
                let (sx, sy) = at(inst.x, inst.y);
                let k = s as f32 * scale;
                let rot = if name.ends_with("scorch") {
                    0.0
                } else {
                    (rand01(inst.seed, 99, 0) * std::f64::consts::TAU) as f32
                };
                out.push(
                    Sprite::centred(region, sx, sy, region.w as f32 * k, region.h as f32 * k)
                        .tinted(color)
                        .rotated(rot),
                );
            }
        }
    }

    /// Draws the effects at `now` (all but decals): the bursts marked `under` (beneath the
    /// characters) or the rest; `at` maps a world point to the screen, `scale` world to screen.
    pub fn draw(
        &self,
        art: &Atlases,
        now: f64,
        at: &dyn Fn(f64, f64) -> (f32, f32),
        scale: f32,
        w: f32,
        h: f32,
        under: bool,
        out: &mut Vec<Sprite>,
    ) {
        let mut flash: Option<(f32, [f32; 4])> = None;
        for inst in &self.live {
            let age = now - inst.at;
            let i = inst.intensity;
            let tier = inst.start.tier;
            for (li, layer) in inst.recipe.layers.iter().enumerate() {
                // (A burst marked `under` goes beneath the characters; everything else over them.)
                if matches!(layer, EffectLayer::Burst { under: true, .. }) != under {
                    continue;
                }
                match layer {
                    EffectLayer::Burst {
                        art: name,
                        count,
                        speed,
                        life,
                        gravity,
                        spread,
                        size,
                        additive,
                        tint,
                        drag,
                        ..
                    } => {
                        if age > *life || age < 0.0 {
                            continue;
                        }
                        let u = age / life;
                        let n = (param(count, i)
                            * tier.map_or(1.0, |t| TIER_COUNT[t])
                            * inst.start.density.unwrap_or(1.0))
                        .round();
                        let debris_art = name.ends_with("gib") || name.ends_with("bone");
                        for p in 0..n as u64 {
                            let (r1, r2, r3) = (
                                rand01(inst.seed, p, li as u64 * 7 + 1),
                                rand01(inst.seed, p, li as u64 * 7 + 2),
                                rand01(inst.seed, p, li as u64 * 7 + 3),
                            );
                            let a = if *spread >= 360.0 {
                                r1 * std::f64::consts::TAU
                            } else {
                                inst.dir + (r1 - 0.5) * spread.to_radians()
                            };
                            let sp = param(speed, i) * (0.45 + 1.1 * r2);
                            let dist = if *drag > 0.0 {
                                sp * (1.0 - (-drag * age).exp()) / drag
                            } else {
                                sp * age
                            };
                            let mut x = inst.x + a.cos() * dist;
                            let mut y =
                                inst.y - 12.0 + a.sin() * dist * 0.75 + 0.5 * gravity * age * age;
                            if *gravity > 0.0 {
                                y = y.min(inst.y + 4.0 + 8.0 * r3);
                            }
                            if name.ends_with("ember") || name.ends_with("dust") {
                                x += (age * 3.0 + 6.0 * r3).sin() * 4.0;
                            }
                            let Some(region) = burst_art(art, name, r3) else {
                                continue;
                            };
                            let mut color = match tint.as_deref() {
                                Some("debris") if !inst.start.debris.is_empty() => {
                                    hex(&inst.start.debris[(r3 * inst.start.debris.len() as f64)
                                        as usize
                                        % inst.start.debris.len()])
                                }
                                Some(c) if c.starts_with('#') => hex(c),
                                _ => [1.0; 4],
                            };
                            color[3] *= if name.ends_with("spark") {
                                (1.0 - u).powf(1.5)
                            } else {
                                1.0 - u
                            } as f32;
                            color[3] *= inst.start.density.unwrap_or(1.0).sqrt() as f32;
                            let mut k = param(size, i)
                                * (0.7 + 0.6 * r3)
                                * inst.start.density.map_or(1.0, f64::sqrt);
                            if name.ends_with("dust") {
                                k *= 1.0 + u;
                            }
                            let k = k as f32 * scale;
                            let (sx, sy) = at(x, y);
                            let mut s = Sprite::centred(
                                region,
                                sx,
                                sy,
                                region.w as f32 * k,
                                region.h as f32 * k,
                            )
                            .tinted(color);
                            if debris_art {
                                s = s.rotated((6.0 * r3 + age * 8.0 * (1.0 - u)) as f32);
                            } else if *additive {
                                s = s.additive();
                            }
                            out.push(s);
                        }
                    }
                    EffectLayer::Ring {
                        color,
                        radius,
                        life,
                        width,
                        additive,
                        ..
                    } => {
                        if age > *life || age < 0.0 {
                            continue;
                        }
                        let u = age / life;
                        let e = 1.0 - (1.0 - u).powi(3);
                        let r = param(radius, i) * inst.start.scale.unwrap_or(1.0) * e;
                        let wd = param(width, i) * (1.0 - 0.6 * u);
                        let mut c = hex(color);
                        c[3] = ((1.0 - u * u) * (wd / 4.0).clamp(0.4, 1.0)) as f32;
                        if let Some(&ring) = art.proc.get("ring") {
                            let (sx, sy) = at(inst.x, inst.y - 8.0);
                            let d = (2.0 * r) as f32 * scale;
                            c[3] *= inst.start.density.unwrap_or(1.0).sqrt() as f32;
                            let s = Sprite::centred(ring, sx, sy, d, d * 0.75).tinted(c);
                            out.push(if *additive { s.additive() } else { s });
                        }
                    }
                    EffectLayer::Sprite {
                        art: name,
                        life,
                        align,
                        scale: size,
                        additive,
                        tint,
                        ..
                    } => {
                        if age > *life || age < 0.0 {
                            continue;
                        }
                        let u = age / life;
                        let mut color = match (tier, tint) {
                            (Some(t), _) => rgb(SWORD_TINTS[t]),
                            (None, Some(c)) => hex(c),
                            (None, None) => [1.0; 4],
                        };
                        color[3] = ((1.0 - u * u) as f32) * tier.map_or(1.0, |t| TIER_ALPHA[t]);
                        color[3] *= inst.start.density.unwrap_or(1.0).sqrt() as f32;
                        let k = param(size, i) * inst.start.scale.unwrap_or(1.0);
                        let shaped = (*align == Align::HitShape)
                            .then_some(inst.start.shape)
                            .flatten();
                        let sprite = if let Some(shape) = shaped {
                            shape_sprite(art, name, shape, k, u, at, scale)
                        } else {
                            let lift = if *align == Align::Up {
                                10.0 + 40.0 * u
                            } else {
                                0.0
                            };
                            plain_sprite(art, name, age, inst, k, lift, at, scale)
                        };
                        if let Some(s) = sprite {
                            let s = s.tinted(color);
                            out.push(if *additive { s.additive() } else { s });
                        }
                    }
                    EffectLayer::Text {
                        text,
                        color,
                        life,
                        size,
                        ..
                    } => {
                        if age > *life || age < 0.0 {
                            continue;
                        }
                        let u = age / life;
                        let smooth = |x: f64| {
                            let x = x.clamp(0.0, 1.0);
                            x * x * (3.0 - 2.0 * x)
                        };
                        let rise = 18.0 * smooth((u * 3.0).min(1.0));
                        let (sx, sy) = at(inst.x, inst.y - 40.0 - rise);
                        let mut c = hex(color);
                        c[3] = (1.0 - smooth((u - 0.6) / 0.4)) as f32;
                        let k = if *size >= 24.0 { 2.0 } else { 1.0 };
                        font::text(
                            text,
                            sx,
                            sy,
                            (k * scale).round().max(1.0),
                            TextAlign::Centre,
                            c,
                            out,
                        );
                    }
                    EffectLayer::Flash {
                        opacity,
                        life,
                        color,
                        ..
                    } => {
                        if age > *life || age < 0.0 {
                            continue;
                        }
                        let a = (param(opacity, i) * (1.0 - age / life)) as f32;
                        // (The strongest flash of the frame wins, with its colour.)
                        if flash.is_none_or(|(b, _)| a > b) {
                            flash = Some((a, hex(color)));
                        }
                    }
                    _ => {}
                }
            }
        }
        if let Some((a, mut c)) = flash {
            c[3] = a;
            out.push(rect(0.0, 0.0, w, h, c));
        }
    }
}

/// A procedural art (`proc:<name>`) or an atlas effect's first frame, for a decal.
fn proc_art(art: &Atlases, name: &str, _seed: u64) -> Option<Region> {
    let key = name.strip_prefix("proc:").unwrap_or(name);
    art.proc.get(key).copied()
}

/// A burst particle's art: a procedural texture, or debris (`gib`, `bone`) from the effects atlas.
fn burst_art(art: &Atlases, name: &str, r3: f64) -> Option<Region> {
    let key = name.strip_prefix("proc:").unwrap_or(name);
    let fx = |id: String| art.effects.get(&id).and_then(|e| e.frames.first().copied());
    match key {
        "gib" => fx(format!("gib-{}", 1 + (6.0 * r3) as usize)),
        "bone" => fx(format!("bone-{}", 1 + (4.0 * r3) as usize)),
        k => match k.strip_prefix("fx:") {
            Some(id) => fx(id.to_string()),
            None => art.proc.get(k).copied(),
        },
    }
}

/// A sprite layer's art fitted to a hit's shape (`align: hitShape`): a full spin or a circle a
/// ring, an arc the crescent nearest its angle turned to its heading, a capsule a streak along it.
fn shape_sprite(
    art: &Atlases,
    name: &str,
    shape: Shape,
    k: f64,
    u: f64,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
) -> Option<Sprite> {
    let grow = 0.85 + 0.25 * u;
    match shape {
        Shape::Arc {
            x,
            y,
            radius,
            dir,
            half_angle,
            ..
        } => {
            if half_angle >= std::f64::consts::PI - 1e-6 {
                let ring = *art.proc.get("ring")?;
                let d = (radius / 62.0 * k * grow) as f32 * ring.w as f32 * scale;
                let (sx, sy) = at(x, y - 14.0);
                return Some(Sprite::centred(ring, sx, sy, d, d));
            }
            if !name.ends_with("arc") {
                return None;
            }
            let deg = half_angle.to_degrees() * 2.0;
            let (id, base) = if deg < 135.0 {
                ("arc120", 62.0)
            } else if deg < 180.0 {
                ("arc150", 78.0)
            } else {
                ("arc200", 98.0)
            };
            let region = *art.proc.get(id)?;
            let d = (radius / base * k * grow) as f32 * region.w as f32 * scale;
            let (sx, sy) = at(x, y - 14.0);
            Some(Sprite::centred(region, sx, sy, d, d).rotated(dir as f32))
        }
        Shape::Circle { x, y, radius }
        | Shape::Ring {
            x,
            y,
            outer: radius,
            ..
        } => {
            let ring = *art.proc.get("ring")?;
            let d = (radius / 62.0 * (0.8 + 0.3 * u)) as f32 * ring.w as f32 * scale;
            let (sx, sy) = at(x, y);
            Some(Sprite::centred(ring, sx, sy, d, d))
        }
        Shape::Capsule {
            x0,
            y0,
            x1,
            y1,
            radius,
        } => {
            let streak = *art.proc.get("streak")?;
            let len = ((x1 - x0).powi(2) + (y1 - y0).powi(2)).sqrt();
            let (sx, sy) = at(f64::midpoint(x0, x1), f64::midpoint(y0, y1) - 14.0);
            Some(
                Sprite::centred(
                    streak,
                    sx,
                    sy,
                    len as f32 * scale,
                    (2.0 * radius) as f32 * scale,
                )
                .rotated((y1 - y0).atan2(x1 - x0) as f32),
            )
        }
    }
}

/// A sprite layer's art at the effect's place: an atlas animation (`fx:`, or the nearest for a
/// procedural art the atlas draws) at 12 frames a second, or a procedural texture.
fn plain_sprite(
    art: &Atlases,
    name: &str,
    age: f64,
    inst: &Instance,
    k: f64,
    lift: f64,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
) -> Option<Sprite> {
    let (sx, sy) = at(inst.x, inst.y - lift);
    if let Some(id) = name.strip_prefix("fx:") {
        let anim = art.effects.get(id)?;
        let frame = (age * 12.0) as usize;
        let region = *anim
            .frames
            .get(frame.min(anim.frames.len().saturating_sub(1)))?;
        return Some(Sprite::anchored(
            region,
            sx,
            sy,
            (anim.anchor_x, anim.anchor_y),
            k as f32 * scale,
        ));
    }
    let key = name.strip_prefix("proc:").unwrap_or(name);
    // (An arc without a hit's shape: the middle crescent, turned to the heading.)
    let key = if key == "arc" { "arc150" } else { key };
    let region = *art.proc.get(key)?;
    let d = k as f32 * scale;
    Some(
        Sprite::centred(region, sx, sy, region.w as f32 * d, region.h as f32 * d).rotated(
            if key.starts_with("arc") || key == "streak" {
                inst.dir as f32
            } else {
                0.0
            },
        ),
    )
}

#[cfg(test)]
mod density_tests {
    use super::*;
    #[test]
    fn source_boss_debris_lives_past_one_second_and_only_hit_decals_shorten() {
        let world = crate::slice::World::load(None).unwrap();
        let recipe = &world.lib.effects["death-boss"];
        let ordinary = Start {
            density: Some(0.2),
            ..Start::default()
        };
        assert_eq!(retained_life(recipe, &ordinary, 1.0), 1.8);
        assert_eq!(retained_life(recipe, &Start::default(), 1.0), 21.0);
        let mut fx = Effects::default();
        fx.start(recipe, 0.0, 0.0, 0.0, 1.0, ordinary, 0.0);
        fx.start(
            &world.lib.effects["blades-hit"],
            100.0,
            0.0,
            0.0,
            0.6,
            Start::default(),
            1.1,
        );
        assert!(
            fx.live.iter().any(|i| i.at == 0.0),
            "a later impact must not truncate source1.4s/1.8s debris"
        );
        fx.start(
            &world.lib.effects["blades-hit"],
            200.0,
            0.0,
            0.0,
            0.6,
            Start::default(),
            1.9,
        );
        assert!(
            fx.live.iter().all(|i| i.at != 0.0),
            "ordinary per-hit decals must not retain the recipe for20s"
        );
    }
}
