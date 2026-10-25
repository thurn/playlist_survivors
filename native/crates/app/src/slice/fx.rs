//! Effects from the content's recipes (`content/effects.yaml`, as `plan/evaluate.ts` plays them):
//! bursts of procedural particles under drag and gravity (sparks, glows, debris in the dying
//! enemy's colours), expanding rings, animations from the effects atlas, camera shake and screen
//! flashes; and for the post pass, lights, chromatic aberration and shockwaves.
#![allow(clippy::many_single_char_names, clippy::too_many_arguments)]

use sim::content::types::{EffectLayer, EffectRecipe, Param};
use sim::core::rng::{Part, Rng};

use super::gfx::{Sprite, rect};
use render::atlas::Atlases;
use render::post::{Grade, Light, MAX_WAVES};

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

struct Particle {
    vx: f64,
    vy: f64,
    size: f64,
    tint: [f32; 4],
}

/// One recipe playing at a place.
struct Instance {
    recipe: EffectRecipe,
    x: f64,
    y: f64,
    at: f64,
    intensity: f64,
    /// Each burst layer's particles, by layer index.
    particles: Vec<Vec<Particle>>,
}

/// The effects playing, and the camera's shake.
#[derive(Default)]
pub struct Effects {
    live: Vec<Instance>,
    trauma: f64,
    trauma_at: f64,
}

impl Effects {
    /// Starts `recipe` at (x, y), heading `dir`, at host time `now`; `debris` colours a dying
    /// enemy's bursts.
    #[allow(clippy::too_many_arguments)]
    pub fn start(
        &mut self,
        recipe: &EffectRecipe,
        x: f64,
        y: f64,
        dir: f64,
        intensity: f64,
        debris: &[String],
        now: f64,
    ) {
        let mut r = Rng::new(&[
            Part::Str(&recipe.id),
            Part::Num((x * 4.0).round()),
            Part::Num((y * 4.0).round()),
            Part::Num((now * 1e3).round()),
        ]);
        let mut particles = Vec::with_capacity(recipe.layers.len());
        for layer in &recipe.layers {
            let mut list = Vec::new();
            match layer {
                EffectLayer::Burst {
                    art,
                    count,
                    speed,
                    spread,
                    size,
                    tint,
                    ..
                } => {
                    let n = param(count, intensity).round() as usize;
                    for _ in 0..n {
                        // Spread is a cone around the heading (degrees); 360 or none is every way.
                        let cone = if *spread > 0.0 {
                            spread.to_radians()
                        } else {
                            std::f64::consts::TAU
                        };
                        let a = dir + (r.next() - 0.5) * cone;
                        let v = param(speed, r.next());
                        let color =
                            match tint.as_deref() {
                                Some("debris") if !debris.is_empty() => hex(&debris
                                    [(r.next() * debris.len() as f64) as usize % debris.len()]),
                                Some(c) if c.starts_with('#') => hex(c),
                                _ => match art.as_str() {
                                    "proc:spark" => [1.0, 0.95, 0.75, 1.0],
                                    "proc:glow" => [1.0, 1.0, 1.0, 0.8],
                                    "proc:dust" => [0.75, 0.7, 0.6, 0.7],
                                    _ => [0.9, 0.9, 0.9, 1.0],
                                },
                            };
                        list.push(Particle {
                            vx: a.cos() * v,
                            vy: a.sin() * v,
                            size: param(size, r.next()),
                            tint: color,
                        });
                    }
                }
                EffectLayer::Shake { trauma, .. } => {
                    self.add_trauma(param(trauma, intensity), now);
                }
                _ => {}
            }
            particles.push(list);
        }
        self.live.push(Instance {
            recipe: recipe.clone(),
            x,
            y,
            at: now,
            intensity,
            particles,
        });
        self.live.retain(|i| now - i.at < 3.0);
    }

    fn add_trauma(&mut self, amount: f64, now: f64) {
        self.trauma = (self.current_trauma(now) + amount).min(1.0);
        self.trauma_at = now;
    }

    fn current_trauma(&self, now: f64) -> f64 {
        (self.trauma - 1.5 * (now - self.trauma_at)).max(0.0)
    }

    /// The camera's shake offset at `now` (world px).
    pub fn shake(&self, now: f64) -> (f64, f64) {
        let t = self.current_trauma(now);
        let amp = t * t * 14.0;
        (amp * (now * 61.0).sin(), amp * (now * 47.0 + 1.3).sin())
    }

    /// The effects' lights, aberration and shockwaves at `now`, as `evaluate.ts` plays them;
    /// `at` maps a world point to the screen, `scale` world to screen.
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
                        let (x, y) = at(inst.x, inst.y);
                        let c = hex(color);
                        lights.push(Light {
                            x,
                            y,
                            radius: (param(radius, i) * (1.0 + 0.3 * u)) as f32 * scale,
                            color: [c[0], c[1], c[2]],
                            strength: (param(strength, i) * (1.0 - u).powf(1.5)) as f32,
                        });
                    }
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

    /// Draws the effects at `now`; `at` maps a world point to the screen, `scale` world to screen.
    pub fn draw(
        &self,
        art: &Atlases,
        now: f64,
        at: &dyn Fn(f64, f64) -> (f32, f32),
        scale: f32,
        w: f32,
        h: f32,
        out: &mut Vec<Sprite>,
    ) {
        for inst in &self.live {
            let age = now - inst.at;
            for (k, layer) in inst.recipe.layers.iter().enumerate() {
                match layer {
                    EffectLayer::Burst {
                        art: name,
                        life,
                        drag,
                        gravity,
                        ..
                    } => {
                        if age > *life {
                            continue;
                        }
                        let u = age / life;
                        let fade = if name == "proc:spark" {
                            (1.0 - u).powf(1.5)
                        } else {
                            1.0 - u
                        };
                        // Under drag k a particle covers v (1 - e^-kt) / k; gravity pulls it down.
                        let travel = if *drag > 0.0 {
                            (1.0 - (-drag * age).exp()) / drag
                        } else {
                            age
                        };
                        for p in &inst.particles[k] {
                            let x = inst.x + p.vx * travel;
                            let y = inst.y + p.vy * travel + 0.5 * gravity * age * age;
                            let (sx, sy) = at(x, y);
                            let px = (p.size * if name == "proc:glow" { 5.0 } else { 2.5 }) as f32
                                * scale;
                            let mut tint = p.tint;
                            tint[3] *= fade as f32;
                            out.push(rect(sx - px / 2.0, sy - px / 2.0, px, px, tint));
                        }
                    }
                    EffectLayer::Ring {
                        color,
                        radius,
                        life,
                        width,
                        ..
                    } => {
                        if age > *life {
                            continue;
                        }
                        let u = age / life;
                        let r = param(radius, inst.intensity) * (0.4 + 0.6 * u);
                        let wd = (param(width, inst.intensity) * (1.0 - u)).max(1.0) as f32 * scale;
                        let mut tint = hex(color);
                        tint[3] = (1.0 - u) as f32 * 0.9;
                        let n = 40;
                        for i in 0..n {
                            let a = i as f64 / f64::from(n) * std::f64::consts::TAU;
                            let (sx, sy) = at(inst.x + a.cos() * r, inst.y + a.sin() * r * 0.8);
                            out.push(rect(sx - wd / 2.0, sy - wd / 2.0, wd, wd, tint));
                        }
                    }
                    EffectLayer::Sprite {
                        art: name,
                        life,
                        scale: size,
                        tint,
                        ..
                    } => {
                        if age > *life {
                            continue;
                        }
                        // The atlas's own animations, and the nearest for procedural arts.
                        let id = match name.strip_prefix("fx:") {
                            Some(id) => id,
                            None => match name.as_str() {
                                "proc:arc" => "slash-sweep",
                                "proc:streak" => "slash-streak",
                                "proc:cross" => "sparkle",
                                "proc:wisp" => "soul-wisp",
                                _ => continue,
                            },
                        };
                        let Some(anim) = art.effects.get(id) else {
                            continue;
                        };
                        let frame = ((age / life) * anim.frames.len() as f64) as usize;
                        if let Some(&region) = anim.frames.get(frame) {
                            let (sx, sy) = at(inst.x, inst.y);
                            let mut t = tint.as_deref().map_or([1.0; 4], hex);
                            t[3] *= 0.9;
                            let s = param(size, inst.intensity) as f32;
                            out.push(
                                Sprite::anchored(
                                    region,
                                    sx,
                                    sy,
                                    (anim.anchor_x, anim.anchor_y),
                                    s * scale,
                                )
                                .tinted(t),
                            );
                        }
                    }
                    EffectLayer::Flash {
                        opacity,
                        life,
                        color,
                        ..
                    } => {
                        if age > *life {
                            continue;
                        }
                        let mut t = hex(color);
                        t[3] = (param(opacity, inst.intensity) * (1.0 - age / life)) as f32;
                        out.push(rect(0.0, 0.0, w, h, t));
                    }
                    _ => {}
                }
            }
        }
    }
}
