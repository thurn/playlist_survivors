//! The scene: the sim's live view drawn with the atlases (docs/design/native-app.md, "The engine in
//! the app (G1)"). World units are the TS game's pixels; the camera centres the view at its zoom.
//! G3 grows this into the game's whole look.

use std::collections::HashMap;

use eval::live::{LiveView, Moment};
use render::atlas::{Atlases, Facing};

use super::gfx::{Sprite, rect, square};

/// The view's width in world units at zoom 1 (the TS game's).
const VIEW_W: f32 = 960.0;
const VIEW_H: f32 = 640.0;
const TILE: f32 = 32.0;

/// An enemy kind's look: its atlas sprite and scale.
#[derive(Clone, Debug)]
pub struct Look {
    pub atlas: String,
    pub scale: f32,
}

/// A one-shot effect started at host time `at`, at a world position.
struct Fx {
    name: &'static str,
    x: f64,
    y: f64,
    at: f64,
    scale: f32,
    tint: [f32; 4],
}

/// What the scene keeps between frames: effects in flight, and when the hero was last bitten.
#[derive(Default)]
pub struct Scene {
    fx: Vec<Fx>,
    bitten: f64,
}

impl Scene {
    /// When the hero was last bitten (host s).
    pub fn bitten(&self) -> f64 {
        self.bitten
    }

    /// Turns the view's new moments into effects, heard now (host time `now`).
    pub fn moments(&mut self, moments: &[Moment], now: f64) {
        for m in moments {
            match m {
                Moment::Hit { x, y, lethal } => self.fx.push(Fx {
                    name: "sparks",
                    x: *x,
                    y: *y,
                    at: now,
                    scale: if *lethal { 1.6 } else { 1.0 },
                    tint: [1.0; 4],
                }),
                Moment::Death { x, y, .. } if x.is_finite() => self.fx.push(Fx {
                    name: "explosion-small",
                    x: *x,
                    y: *y,
                    at: now,
                    scale: 1.6,
                    tint: [1.0; 4],
                }),
                Moment::Swing { x, y, .. } => self.fx.push(Fx {
                    name: "slash-sweep",
                    x: *x,
                    y: *y,
                    at: now,
                    scale: 1.4,
                    tint: [1.0, 1.0, 1.0, 0.85],
                }),
                Moment::Bite { .. } => self.bitten = now,
                _ => {}
            }
        }
        self.fx.retain(|f| now - f.at < 2.0);
    }

    /// Draws `view` at host time `now` into a `w` x `h` window (the HUD is `hud`'s).
    #[allow(clippy::too_many_arguments, clippy::single_match_else)]
    pub fn draw(
        &self,
        view: &LiveView,
        art: &Atlases,
        looks: &HashMap<String, Look>,
        now: f64,
        w: f32,
        h: f32,
        out: &mut Vec<Sprite>,
    ) {
        let (cx, cy, zoom) = view.camera;
        let scale = (w / VIEW_W).min(h / VIEW_H) * zoom as f32;
        let at = |x: f64, y: f64| {
            (
                w / 2.0 + (x - cx) as f32 * scale,
                h / 2.0 + (y - cy) as f32 * scale,
            )
        };
        // The ground: grass tiles on the world grid around the camera.
        if let Some(grass) = art.tiles.get("grass-summer") {
            let half_w = f64::from(w / scale / 2.0) + f64::from(TILE);
            let half_h = f64::from(h / scale / 2.0) + f64::from(TILE);
            let t = f64::from(TILE);
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
                    let hash = (i.wrapping_mul(73_856_093) ^ j.wrapping_mul(19_349_663))
                        .unsigned_abs() as usize;
                    let (sx, sy) = at(i as f64 * t, j as f64 * t);
                    let mut s =
                        Sprite::anchored(grass[hash % grass.len()], sx, sy, (0.0, 0.0), scale);
                    s.w += 0.5;
                    s.h += 0.5;
                    out.push(s.tinted([0.85, 0.85, 0.85, 1.0]));
                }
            }
        } else {
            out.push(rect(0.0, 0.0, w, h, [0.1, 0.2, 0.1, 1.0]));
        }
        // Characters from back to front: enemies, and the hero among them by depth.
        let mut order: Vec<(f64, Option<usize>)> = view
            .foes
            .iter()
            .filter(|(_, f)| f.x.is_finite())
            .map(|(&id, f)| (f.y, Some(id)))
            .collect();
        if let Some(hero) = view.hero {
            order.push((hero.y, None));
        }
        order.sort_by(|a, b| a.0.total_cmp(&b.0));
        for (_, who) in order {
            match who {
                Some(id) => {
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
                None => {
                    let hero = view.hero.expect("placed");
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
        for fx in &self.fx {
            let Some(anim) = art.effects.get(fx.name) else {
                continue;
            };
            let frame = ((now - fx.at) * f64::from(anim.fps)).max(0.0) as usize;
            if let Some(&region) = anim.frames.get(frame) {
                let (sx, sy) = at(fx.x, fx.y);
                out.push(
                    Sprite::anchored(
                        region,
                        sx,
                        sy,
                        (anim.anchor_x, anim.anchor_y),
                        fx.scale * scale,
                    )
                    .tinted(fx.tint),
                );
            }
        }
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
