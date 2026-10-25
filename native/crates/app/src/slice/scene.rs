//! The scene: the sim's live view drawn with the atlases (docs/design/native-app.md, "The engine in
//! the app (G1)"). World units are the TS game's pixels; the camera centres the view at its zoom.
//! G3 grows this into the game's whole look.

use std::collections::HashMap;

use eval::field::{self, CHUNK, ChunkContent, FieldProp};
use eval::live::{LiveView, Moment};
use render::atlas::{Atlases, Facing};
use render::font::{self, Align};
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
    /// The hero's level last seen.
    level: u32,
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
    /// The top of the hero's head on screen, for the overhead display.
    pub head: Option<(f32, f32)>,
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

    /// Celebrates a level gained since the last call (the `level-up` recipe at the hero).
    pub fn level(&mut self, view: &LiveView, now: f64, lib: &ContentLibrary) {
        if self.level > 0
            && view.level > self.level
            && let (Some(h), Some(recipe)) = (view.hero, lib.effects.get("level-up"))
        {
            self.effects.start(recipe, h.x, h.y, 0.0, 1.0, &[], now);
        }
        self.level = view.level;
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
                    // (The sprites stand 64 tall, the head about 50 up.)
                    layers.head = Some(at(hero.x, hero.y - hero.z - 50.0));
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

/// The listening screen, between runs: the title, the evidence of a beat against the gate a run
/// starts at, and how to start one.
pub fn listening_card(
    w: f32,
    h: f32,
    evidence: f64,
    gate: f64,
    now: f64,
    bluetooth: bool,
    out: &mut Vec<Sprite>,
) {
    out.push(rect(0.0, 0.0, w, h, [0.02, 0.02, 0.04, 0.85]));
    let u = (w / 960.0).min(h / 540.0);
    let px = |k: f32| (k * u).round().max(1.0);
    let cx = w / 2.0;
    let top = h / 2.0 - 90.0 * u;
    font::text(
        "PLAYLIST SURVIVORS",
        cx,
        top,
        px(5.0),
        Align::Centre,
        [1.0, 0.85, 0.4, 1.0],
        out,
    );
    let dots = ".".repeat((now * 2.0) as usize % 4);
    font::text(
        &format!("LISTENING{dots:<3}"),
        cx,
        top + 60.0 * u,
        px(2.0),
        Align::Centre,
        [0.69, 0.69, 0.75, 1.0],
        out,
    );
    // The beat's evidence, with the gate marked: a run starts once it holds above it for 2 s.
    let (mw, mh) = (300.0 * u, 10.0 * u);
    let (mx, my) = (cx - mw / 2.0, top + 85.0 * u);
    out.push(rect(mx, my, mw, mh, [0.0, 0.0, 0.0, 0.6]));
    let clear = evidence >= gate;
    let color = if clear {
        [0.44, 0.94, 0.75, 1.0]
    } else {
        [0.44, 0.53, 0.66, 1.0]
    };
    out.push(rect(
        mx,
        my,
        mw * evidence.clamp(0.0, 1.0) as f32,
        mh,
        color,
    ));
    out.push(rect(
        mx + mw * gate as f32 - u,
        my - 3.0 * u,
        2.0 * u,
        mh + 6.0 * u,
        [1.0, 1.0, 1.0, 0.8],
    ));
    font::text(
        "PLAY MUSIC, OR PRESS ENTER TO START",
        cx,
        top + 120.0 * u,
        px(2.0),
        Align::Centre,
        [0.85, 0.85, 0.9, 1.0],
        out,
    );
    font::text(
        "C CALIBRATE YOUR TAPS   V CALIBRATE THE DISPLAY",
        cx,
        top + 145.0 * u,
        px(2.0),
        Align::Centre,
        [0.6, 0.6, 0.7, 1.0],
        out,
    );
    if bluetooth {
        font::text(
            "BLUETOOTH OUTPUT: SYNC IS BEST-EFFORT",
            cx,
            top + 175.0 * u,
            px(2.0),
            Align::Centre,
            [1.0, 0.75, 0.4, 1.0],
            out,
        );
    }
}

/// Why a run ended.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum End {
    Fell,
    /// A confirmed end: the song stopped and the finale played.
    Song,
    /// Esc or the window losing focus: the music moved on, so a new run starts on the next beat.
    Paused,
    /// The player ended it.
    Stopped,
    /// The output device changed (its latency with it).
    Device,
    /// The music's source changed (File or System mode, from the debug menu).
    Source,
}

/// The run's end card, in the HUD's pixel font: dims the window, says how the run ended, how long
/// it lasted and how it went, and how a new one starts.
pub fn end_card(
    w: f32,
    h: f32,
    age: f64,
    seconds: u64,
    end: End,
    view: &LiveView,
    out: &mut Vec<Sprite>,
) {
    let a = (age / 0.5).clamp(0.0, 1.0) as f32;
    out.push(rect(0.0, 0.0, w, h, [0.0, 0.0, 0.0, 0.7 * a]));
    let u = (w / 960.0).min(h / 540.0);
    let px = |k: f32| (k * u).round().max(1.0);
    let (title, color) = match end {
        End::Fell => ("YOU FELL", [1.0, 0.25, 0.31, a]),
        End::Song => ("THE SONG ENDED", [0.62, 0.72, 1.0, a]),
        End::Paused => ("PAUSED", [1.0, 0.85, 0.4, a]),
        End::Stopped => ("RUN ENDED", [0.8, 0.8, 0.85, a]),
        End::Device => ("THE OUTPUT CHANGED", [1.0, 0.85, 0.4, a]),
        End::Source => ("THE MUSIC CHANGED", [1.0, 0.85, 0.4, a]),
    };
    let cx = w / 2.0;
    let top = h / 2.0 - 100.0 * u;
    let grey = [0.69, 0.69, 0.75, a];
    font::text(title, cx, top, px(4.0), Align::Centre, color, out);
    font::text(
        "SURVIVED",
        cx,
        top + 50.0 * u,
        px(2.0),
        Align::Centre,
        grey,
        out,
    );
    font::text(
        &format!("{}:{:02}", seconds / 60, seconds % 60),
        cx,
        top + 72.0 * u,
        px(8.0),
        Align::Centre,
        [1.0, 1.0, 1.0, a],
        out,
    );
    let [perfect, great, good] = view.ratings;
    font::text(
        &format!(
            "KILLS {}   PERFECT {perfect}   GREAT {great}   GOOD {good}",
            view.kills
        ),
        cx,
        top + 140.0 * u,
        px(2.0),
        Align::Centre,
        grey,
        out,
    );
    let blink = if (age * 1.5).fract() < 0.7 { 1.0 } else { 0.4 };
    font::text(
        "W WATCH YOUR RUN   PLAY MUSIC OR PRESS ENTER FOR A NEW RUN",
        cx,
        top + 175.0 * u,
        px(2.0),
        Align::Centre,
        [0.85, 0.85, 0.9, a * blink],
        out,
    );
}
