//! The scene: the sim's live view drawn with the atlases (docs/design/native-app.md, "The engine in
//! the app (G1)"). World units are the TS game's pixels; the camera centres the view at its zoom.
//! G3 grows this into the game's whole look.

use std::collections::HashMap;

use eval::field::{self, CHUNK, ChunkContent, FieldProp};
use eval::live::{LiveView, Moment};
use render::atlas::{Atlases, Facing};
use render::post::{Grade, Light};
use sim::content::types::{ColorGrade, ZoneTheme};

use super::fx::{Effects, hex};
use super::gfx::{Sprite, rect, square};
use sim::content::ContentLibrary;

/// The view's width in world units at zoom 1 (the TS game's).
const VIEW_W: f32 = 960.0;
const VIEW_H: f32 = 640.0;

/// An enemy kind's look: its atlas sprite and scale.
#[derive(Clone, Debug)]
pub struct Look {
    pub atlas: String,
    pub scale: f32,
    /// Its death's effect recipe and debris colours.
    pub death: String,
    pub debris: Vec<String>,
}

/// What the scene keeps between frames: effects in flight, and when the hero was last bitten.
#[derive(Default)]
pub struct Scene {
    effects: Effects,
    bitten: f64,
    /// The open field's chunks near the camera.
    chunks: HashMap<(i64, i64), ChunkContent>,
}

/// The ground's zone and seed.
pub struct Field<'a> {
    pub seed: f64,
    pub theme: &'a ZoneTheme,
}

/// Where the music is: the phase (0 to 1) since the last beat and since the last bar's first.
#[derive(Clone, Copy, Debug)]
pub struct Pulse {
    pub beat: f64,
    pub bar: f64,
}

/// What the scene draws: the world's sprites, its lights, and the grade over them.
#[derive(Default)]
pub struct Layers {
    pub sprites: Vec<Sprite>,
    pub lights: Vec<Light>,
    pub grade: Grade,
}

/// A zone's colour grade (`renderer.ts`'s GRADES): multipliers, lift, contrast and saturation.
fn grade_of(g: ColorGrade) -> ([f32; 3], [f32; 3], f32, f32) {
    match g {
        ColorGrade::Twilight => ([0.86, 0.92, 1.12], [0.0, 0.01, 0.04], 1.05, 0.85),
        ColorGrade::Neutral => ([1.02, 1.0, 0.97], [0.0; 3], 1.04, 1.05),
        ColorGrade::Autumn => ([1.08, 0.98, 0.86], [0.02, 0.01, 0.0], 1.06, 1.1),
        ColorGrade::Ember => ([1.1, 0.94, 0.84], [0.02, 0.0, 0.0], 1.08, 1.0),
        ColorGrade::Infernal => ([1.12, 0.9, 0.82], [0.03, 0.0, 0.01], 1.1, 0.98),
    }
}

/// The zone's grade at a beat: the ambient light pulsing on beats, the vignette on bars. (The
/// TS game's reactive loudness and key look aren't in the app yet: as a mid loudness, neutral.)
fn grade(theme: &ZoneTheme, pulse: Pulse, now: f64, pixel: f32) -> Grade {
    let (mul, lift, contrast, saturation) = grade_of(theme.color_grade);
    let amb = hex(&theme.ambient_light);
    let beat = (-6.0 * pulse.beat.clamp(0.0, 1.0)).exp() as f32;
    let bar = (-6.0 * pulse.bar.clamp(0.0, 1.0)).exp() as f32;
    let ambient = |v: f32| (v * 1.1 + 0.08).min(1.2);
    Grade {
        ambient: [ambient(amb[0]), ambient(amb[1]), ambient(amb[2])],
        pulse: 1.0 + 0.1 * beat,
        bloom: 0.55,
        mul,
        lift,
        contrast,
        saturation,
        vignette: 0.45 + 0.25 * bar,
        haze: if theme.heat_haze { 1.0 } else { 0.0 },
        time: now as f32,
        pixel,
        ..Grade::default()
    }
}

/// What is drawn by depth: an enemy, the hero, or a chunk's tall prop.
enum Drawn {
    Foe(usize),
    Hero,
    Tall((i64, i64), usize),
}

impl Scene {
    /// When the hero was last bitten (host s).
    pub fn bitten(&self) -> f64 {
        self.bitten
    }

    /// Turns the view's new moments into effects, heard now (host time `now`).
    pub fn moments(
        &mut self,
        moments: &[Moment],
        now: f64,
        lib: &ContentLibrary,
        looks: &HashMap<String, Look>,
    ) {
        let mut fire = |id: &str, x: f64, y: f64, dir: f64, intensity: f64, debris: &[String]| {
            if let Some(recipe) = lib.effects.get(id) {
                self.effects
                    .start(recipe, x, y, dir, intensity, debris, now);
            }
        };
        for m in moments {
            match m {
                // (As emit.ts: a swing's hit by its kind, a weapon's on an enemy it doesn't kill,
                // and the enemy's own death recipe with its debris.)
                Moment::Swing { x, y, dir, kind } => {
                    fire(&format!("hit-{kind}"), *x, *y, *dir, 0.8, &[]);
                }
                Moment::Hit {
                    x,
                    y,
                    lethal: false,
                } if x.is_finite() => fire("blades-hit", *x, *y, 0.0, 0.5, &[]),
                Moment::Death { kind, x, y } if x.is_finite() => {
                    let (recipe, debris) =
                        looks.get(kind).map_or(("death-humanoid", &[][..]), |l| {
                            (l.death.as_str(), &l.debris[..])
                        });
                    fire(recipe, *x, *y, -std::f64::consts::FRAC_PI_2, 0.7, debris);
                }
                Moment::Bite { .. } => self.bitten = now,
                _ => {}
            }
        }
    }

    /// Draws `view` at host time `now`, on the music's `pulse`, into a `w` x `h` window (the HUD
    /// is `hud`'s).
    #[allow(
        clippy::too_many_arguments,
        clippy::single_match_else,
        clippy::many_single_char_names
    )]
    pub fn draw(
        &mut self,
        view: &LiveView,
        ground: &Field,
        art: &Atlases,
        looks: &HashMap<String, Look>,
        now: f64,
        pulse: Pulse,
        w: f32,
        h: f32,
        layers: &mut Layers,
    ) {
        let out = &mut layers.sprites;
        let lights = &mut layers.lights;
        let (cx, cy, zoom) = view.camera;
        let (shx, shy) = self.effects.shake(now);
        let (cx, cy) = (cx + shx, cy + shy);
        let scale = (w / VIEW_W).min(h / VIEW_H) * zoom as f32;
        layers.grade = grade(ground.theme, pulse, now, scale);
        let at = |x: f64, y: f64| {
            (
                w / 2.0 + (x - cx) as f32 * scale,
                h / 2.0 + (y - cy) as f32 * scale,
            )
        };
        // The ground: the zone's tiles on the world grid around the camera.
        let half_w = f64::from(w / scale / 2.0) + 64.0;
        let half_h = f64::from(h / scale / 2.0) + 64.0;
        let t = field::TILE;
        match art.tiles.get(&ground.theme.terrain.ground) {
            Some(tiles) => {
                let (i0, i1) = (
                    ((cx - half_w) / t).floor() as i64,
                    ((cx + half_w) / t).ceil() as i64,
                );
                let (j0, j1) = (
                    ((cy - half_h) / t).floor() as i64,
                    ((cy + half_h) / t).ceil() as i64,
                );
                for j in j0..j1 {
                    for i in i0..i1 {
                        let v = field::tile_variant(ground.seed, i, j);
                        let (sx, sy) = at(i as f64 * t, j as f64 * t);
                        let mut s =
                            Sprite::anchored(tiles[v % tiles.len()], sx, sy, (0.0, 0.0), scale);
                        s.w += 0.5;
                        s.h += 0.5;
                        out.push(s);
                    }
                }
            }
            None => out.push(rect(0.0, 0.0, w, h, [0.1, 0.2, 0.1, 1.0])),
        }
        // The chunks in view (generated once, dropped when far).
        let (c0, c1) = (
            ((cx - half_w - 128.0) / CHUNK).floor() as i64,
            ((cx + half_w + 128.0) / CHUNK).floor() as i64,
        );
        let (r0, r1) = (
            ((cy - half_h - 128.0) / CHUNK).floor() as i64,
            ((cy + half_h + 128.0) / CHUNK).floor() as i64,
        );
        self.chunks
            .retain(|&(a, b), _| a >= c0 - 1 && a <= c1 + 1 && b >= r0 - 1 && b <= r1 + 1);
        let mut visible = Vec::new();
        for b in r0..=r1 {
            for a in c0..=c1 {
                self.chunks
                    .entry((a, b))
                    .or_insert_with(|| field::chunk_content(ground.seed, a, b, ground.theme));
                visible.push((a, b));
            }
        }
        let prop = |p: &FieldProp, tint: [f32; 4], out: &mut Vec<Sprite>| {
            if let Some(variants) = art.props.get(&p.sprite).filter(|v| !v.is_empty()) {
                let v = variants[p.variant % variants.len()];
                let (sx, sy) = at(p.x, p.y);
                let s = Sprite::anchored(v.region, sx, sy, (v.anchor_x, v.anchor_y), scale)
                    .tinted(tint);
                out.push(if p.flip { s.mirrored() } else { s });
            }
        };
        for key in &visible {
            for p in &self.chunks[key].ground {
                prop(p, [1.0; 4], out);
            }
        }
        let mut order: Vec<(f64, Drawn)> = view
            .foes
            .iter()
            .filter(|(_, f)| f.x.is_finite())
            .map(|(&id, f)| (f.y, Drawn::Foe(id)))
            .collect();
        if let Some(hero) = view.hero {
            order.push((hero.y, Drawn::Hero));
        }
        for key in &visible {
            for (k, p) in self.chunks[key].tall.iter().enumerate() {
                order.push((p.y, Drawn::Tall(*key, k)));
            }
        }
        order.sort_by(|a, b| a.0.total_cmp(&b.0));
        let hero_at = view.hero.map(|h| (h.x, h.y));
        for (_, who) in order {
            match who {
                Drawn::Tall(key, k) => {
                    let p = &self.chunks[&key].tall[k];
                    let fade =
                        hero_at.map_or(1.0, |(hx, hy)| field::prop_fade(p.x, p.y, hx, hy)) as f32;
                    prop(p, [1.0, 1.0, 1.0, fade], out);
                    // A lamp's or brazier's light, flickering.
                    if let Some(l) = ground
                        .theme
                        .props
                        .iter()
                        .find(|q| q.sprite == p.sprite)
                        .and_then(|q| q.light.as_ref())
                    {
                        let (sx, sy) = at(p.x, p.y - 20.0);
                        let flick = 0.85 + 0.15 * (now * 9.0 + p.x).sin();
                        let c = hex(&l.color);
                        lights.push(Light {
                            x: sx,
                            y: sy,
                            radius: (l.radius * flick) as f32 * scale,
                            color: [c[0], c[1], c[2]],
                            strength: 0.8 * fade,
                        });
                    }
                }
                Drawn::Foe(id) => {
                    let f = &view.foes[&id];
                    let look = looks.get(&f.kind);
                    let sprite = look.map_or("goblin", |l| l.atlas.as_str());
                    let size = look.map_or(1.0, |l| l.scale);
                    let (sx, sy) = at(f.x, f.y);
                    let facing = Facing::toward((f.x - f.prev.0) as f32, (f.y - f.prev.1) as f32);
                    let dead = f.death_w.is_some();
                    let tint = if dead {
                        [1.0, 1.0, 1.0, 0.35]
                    } else {
                        [1.0; 4]
                    };
                    if let Some(walk) = art.animation(sprite, "walk") {
                        // (Enemies are sampled 60 times a second; a walk frame every 6 samples.)
                        let frame = f.samples / 6;
                        out.push(
                            Sprite::anchored(
                                walk.frame(facing, frame, false),
                                sx,
                                sy,
                                (walk.anchor_x, walk.anchor_y),
                                size * scale,
                            )
                            .tinted(tint),
                        );
                    } else {
                        out.push(square(sx, sy, 20.0 * scale, [0.9, 0.25, 0.3, tint[3]]));
                    }
                }
                Drawn::Hero => {
                    let hero = view.hero.expect("placed");
                    // The hero carries a soft light.
                    let (lx, ly) = at(hero.x, hero.y - 16.0);
                    lights.push(Light {
                        x: lx,
                        y: ly,
                        radius: 144.0 * scale,
                        color: [1.0, 0.91, 0.75],
                        strength: 0.35,
                    });
                    let (sx, sy) = at(hero.x, hero.y - hero.z);
                    let facing =
                        [Facing::Up, Facing::Left, Facing::Down, Facing::Right][hero.facing.min(3)];
                    let flash =
                        now - self.bitten < 0.6 && ((now - self.bitten) * 12.0) as i64 % 2 == 0;
                    match art
                        .animation("hero", hero.anim)
                        .or_else(|| art.animation("hero", "idle"))
                    {
                        Some(a) => out.push(Sprite::anchored(
                            a.frame(facing, hero.frame, flash),
                            sx,
                            sy,
                            (a.anchor_x, a.anchor_y),
                            scale,
                        )),
                        None => out.push(square(sx, sy, 26.0 * scale, [0.4, 0.85, 1.0, 1.0])),
                    }
                }
            }
        }
        self.effects.draw(art, now, &at, scale, w, h, out);
        self.effects
            .post(now, &at, scale, lights, &mut layers.grade);
        if now - self.bitten < 0.6 {
            let a = (1.0 - (now - self.bitten) / 0.6) as f32;
            out.push(rect(0.0, 0.0, w, h, [0.8, 0.1, 0.1, 0.25 * a]));
        }
    }
}

/// The run's end card: dims the window and shows `seconds` survived in seven-segment digits.
pub fn end_card(w: f32, h: f32, age: f64, seconds: u64, fell: bool, out: &mut Vec<Sprite>) {
    let a = (age / 0.5).clamp(0.0, 1.0) as f32;
    out.push(rect(0.0, 0.0, w, h, [0.0, 0.0, 0.0, 0.7 * a]));
    let ui = (w / VIEW_W).min(h / VIEW_H);
    let color = if fell {
        [0.95, 0.3, 0.4, a]
    } else {
        [0.6, 0.7, 0.9, a]
    };
    out.push(square(w / 2.0, h / 2.0 - 110.0 * ui, 40.0 * ui, color));
    let size = 80.0 * ui;
    let width = digit_count(seconds) as f32 * size * 0.75;
    digits(
        seconds,
        w / 2.0 + width / 2.0,
        h / 2.0 - 40.0 * ui,
        size,
        [1.0, 1.0, 1.0, a],
        out,
    );
}

fn digit_count(mut n: u64) -> usize {
    let mut k = 1;
    while n >= 10 {
        n /= 10;
        k += 1;
    }
    k
}

/// `n` in seven-segment digits of height `size`, right-aligned at `right`, top at `top`.
fn digits(n: u64, right: f32, top: f32, size: f32, color: [f32; 4], out: &mut Vec<Sprite>) {
    // Segments a-g, as bits of each digit.
    const SEGMENTS: [u8; 10] = [
        0b011_1111, 0b000_0110, 0b101_1011, 0b100_1111, 0b110_0110, 0b110_1101, 0b111_1101,
        0b000_0111, 0b111_1111, 0b110_1111,
    ];
    let (w, t) = (size * 0.5, size * 0.1);
    let half = size / 2.0;
    let mut n = n;
    let mut x = right - w;
    loop {
        let seg = SEGMENTS[(n % 10) as usize];
        let bars = [
            (x, top, w, t),
            (x + w - t, top, t, half),
            (x + w - t, top + half, t, half),
            (x, top + size - t, w, t),
            (x, top + half, t, half),
            (x, top, t, half),
            (x, top + half - t / 2.0, w, t),
        ];
        for (i, &(bx, by, bw, bh)) in bars.iter().enumerate() {
            if seg & (1 << i) != 0 {
                out.push(rect(bx, by, bw, bh, color));
            }
        }
        n /= 10;
        if n == 0 {
            break;
        }
        x -= size * 0.75;
    }
}
