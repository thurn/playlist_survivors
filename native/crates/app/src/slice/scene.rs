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

use super::fx::{Effects, Start, hex, rand01, rgb};
use super::gems;
use super::gfx::{Sprite, rect, square};
use super::lane::{self, Lane};
use super::tier::{self, Arms};
use sim::content::ContentLibrary;
use stream::StreamLog;

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
    /// The open field's chunks near the camera, by zone.
    chunks: HashMap<(i64, i64, String), ChunkContent>,
    /// The hero's level last seen.
    level: u32,
    /// The reactive fast lane, and what it shows this frame.
    lane: Lane,
    fast: lane::Frame,
    /// The run time the fast lane last showed.
    run_s: f64,
    /// The hero's recent places (host s, x, y), for the fire trail.
    trail: std::collections::VecDeque<(f64, f64, f64)>,
    /// The last tier change: when (host s), and whether up.
    tier_change: Option<(f64, bool)>,
    /// Each enemy's last hit: when (host s), and whether it killed.
    struck: HashMap<usize, (f64, bool)>,
}

/// A hit flashes an enemy white this long; a killing blow knocks it back over this long, and it
/// falls and fades over this long (s).
const HIT_FLASH_S: f64 = 0.05;
const KNOCK_S: f64 = 0.22;
const DEATH_FALL_S: f64 = 0.5;

/// The tier change's ring lasts about a beat, and the groove meter flashes this long on a tier up
/// (s).
const TIER_RING_S: f64 = 0.5;
const TIER_FLASH_S: f64 = 0.6;

/// The ground's seed and zone, and the transform wave sweeping in the next (its centre and radius,
/// world px), if one is.
pub struct Field<'a> {
    pub seed: f64,
    pub theme: &'a ZoneTheme,
    pub wave: Option<(&'a ZoneTheme, f64, f64, f64)>,
}

impl Field<'_> {
    /// The zone at (x, y): the wave's inside its ring.
    fn zone_at(&self, x: f64, y: f64) -> &ZoneTheme {
        match self.wave {
            Some((z, wx, wy, r)) if (x - wx).powi(2) + (y - wy).powi(2) <= r * r => z,
            _ => self.theme,
        }
    }
}

/// Where the music is: the phase (0 to 1) since the last beat and since the last bar's first, and
/// the beat index (beats heard less one, plus the phase).
#[derive(Clone, Copy, Debug)]
pub struct Pulse {
    pub beat: f64,
    pub bar: f64,
    pub index: f64,
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

/// The zone's grade at a beat, as the fast lane shows it: the ambient light following the
/// loudness and pulsing on beats, the vignette on bars, the key's look over the zone's.
fn grade(theme: &ZoneTheme, pulse: Pulse, now: f64, pixel: f32, fast: &lane::Frame) -> Grade {
    let (mul, lift, contrast, saturation) = grade_of(theme.color_grade);
    let amb = hex(&theme.ambient_light);
    let beat = fast.pulsed((-6.0 * pulse.beat.clamp(0.0, 1.0)).exp()) as f32;
    let bar = fast.pulsed((-6.0 * pulse.bar.clamp(0.0, 1.0)).exp()) as f32;
    let loud = fast.loudness.clamp(0.0, 1.0) as f32;
    let intensity = 0.4 + 0.6 * loud;
    let ambient = |v: f32| (v * 1.1 * (0.8 + 0.4 * loud) + 0.08).min(1.2);
    let look = fast.look;
    Grade {
        ambient: [ambient(amb[0]), ambient(amb[1]), ambient(amb[2])],
        pulse: 1.0 + (0.05 + 0.1 * loud) * intensity * beat,
        bloom: 0.55,
        mul: [0, 1, 2].map(|i| mul[i] * look.mul[i]),
        lift: [0, 1, 2].map(|i| lift[i] + look.lift[i]),
        contrast,
        saturation: saturation * look.sat,
        vignette: 0.45 + 0.25 * bar * intensity,
        haze: if theme.heat_haze { 1.0 } else { 0.0 },
        time: now as f32,
        pixel,
        ..Grade::default()
    }
}

/// The fast lane's horde: its squash on the beat, bob (px) and flinch at onsets, and each kind's
/// step behind the beat (beats).
const ENEMY_SQUASH: f64 = 0.16;
const ENEMY_BOB: f64 = 4.0;
const ENEMY_FLINCH: f64 = 0.14;
const TYPE_PHASE_STEP: f64 = 0.12;

/// An enemy kind's step behind the beat: 0, 1 or 2 steps by a hash of its name.
fn type_phase(kind: &str) -> f64 {
    let h = kind.bytes().fold(0x811c_9dc5u32, |h, b| {
        (h ^ u32::from(b)).wrapping_mul(0x0100_0193)
    });
    f64::from(h % 3) * TYPE_PHASE_STEP
}

/// A sprite stretched by (kx, ky) about the screen point it is anchored at.
fn stretch(mut s: Sprite, (ax, ay): (f32, f32), (kx, ky): (f32, f32)) -> Sprite {
    s.x = ax - (ax - s.x) * kx;
    s.w *= kx;
    s.y = ay - (ay - s.y) * ky;
    s.h *= ky;
    s
}

/// The fast lane's own sprites and lights: a ripple from each press at the hero, the ground's
/// glow with the good ones, and each onset's burst of sparks and light around the hero in the
/// song's tint (faded while the beat is unclear).
#[allow(clippy::many_single_char_names)]
fn draw_fast(
    fast: &lane::Frame,
    view: &LiveView,
    art: &Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    out: &mut Vec<Sprite>,
    lights: &mut Vec<Light>,
) {
    let Some(hero) = view.hero else {
        return;
    };
    if let Some(&ring) = art.proc.get("ring") {
        let (sx, sy) = at(hero.x, hero.y - 12.0);
        for &(age, mut c) in &fast.ripples {
            let u = age / lane::RIPPLE_S;
            let d = (2.0 * (14.0 + 150.0 * age)) as f32 * scale;
            c[3] = (0.7 * (1.0 - u)) as f32;
            out.push(Sprite::centred(ring, sx, sy, d, d).tinted(c).additive());
        }
    }
    if fast.glow > 0.0 {
        let (sx, sy) = at(hero.x, hero.y + 6.0);
        let c = rgb(0x00ff_f0b0);
        lights.push(Light {
            x: sx,
            y: sy,
            radius: (90.0 + 60.0 * fast.glow) as f32 * scale,
            color: [c[0], c[1], c[2]],
            strength: (0.55 * fast.glow) as f32,
        });
    }
    let fade = lane::BURST_FLOOR + (1.0 - lane::BURST_FLOOR) * fast.amp * fast.mix;
    let tint = fast.tint;
    let (glow, spark) = (art.proc.get("glow"), art.proc.get("spark"));
    for b in &fast.bursts {
        let (j, n) = (b.index as u64, b.n);
        let u = b.age / lane::BURST_LIFE;
        let a = rand01(0x0b5e7, j, 1) * std::f64::consts::TAU;
        let r = 50.0 + 120.0 * rand01(0x0b5e7, j, 2);
        let (cx, cy) = (hero.x + a.cos() * r, hero.y - 10.0 + a.sin() * r * 0.75);
        let (sx, sy) = at(cx, cy);
        if let Some(&g) = glow {
            let k = g.w as f32 * (0.5 + 0.9 * n) as f32 * scale;
            let alpha = fade * 0.6 * n * (1.0 - u).powi(2);
            out.push(
                Sprite::centred(g, sx, sy, k, k)
                    .tinted([tint[0], tint[1], tint[2], alpha as f32])
                    .additive(),
            );
        }
        if let Some(&sp) = spark {
            let spread = 1.0 - (1.0 - u).powi(2);
            let k = sp.w as f32 * (0.8 + 0.8 * n) as f32 * scale;
            let alpha = (fade * (0.5 + 0.5 * n) * (1.0 - u).powf(1.5)) as f32;
            for i in 0..(3.0 + 7.0 * n).round() as u64 {
                let a = rand01(0x0b5e8, j, i) * std::f64::consts::TAU;
                let d = (10.0 + 30.0 * rand01(0x0b5e9, j, i)) * (0.5 + n) * spread;
                let (px, py) = at(cx + a.cos() * d, cy + a.sin() * d * 0.75);
                out.push(
                    Sprite::centred(sp, px, py, k, k)
                        .tinted([tint[0], tint[1], tint[2], alpha])
                        .additive(),
                );
            }
        }
        lights.push(Light {
            x: sx,
            y: sy,
            radius: (30.0 + 50.0 * n) as f32 * scale,
            color: [tint[0], tint[1], tint[2]],
            strength: (0.4 * n * fade * (1.0 - u).powi(2)) as f32,
        });
    }
}

/// What is drawn by depth: an enemy, the hero, or a chunk's tall prop.
enum Drawn {
    Foe(usize),
    Hero,
    Tall((i64, i64, String), usize),
}

impl Scene {
    /// When the hero was last bitten (host s).
    pub fn bitten(&self) -> f64 {
        self.bitten
    }

    /// The fast lane's frame at run time `s` (host time `now`), from the stream known by then and
    /// its evidence gate, and the start of the view's newest zone wave.
    pub fn fast_lane(&mut self, log: &StreamLog, gate: f64, s: f64, now: f64, wave: Option<f64>) {
        self.fast = self.lane.frame(log, gate, s, now, wave);
        self.run_s = s;
    }

    /// The groove meter's flash after a tier up (1 to 0), at host time `now`.
    pub fn tier_flash(&self, now: f64) -> f64 {
        match self.tier_change {
            Some((at, true)) if (0.0..TIER_FLASH_S).contains(&(now - at)) => {
                1.0 - (now - at) / TIER_FLASH_S
            }
            _ => 0.0,
        }
    }

    /// Turns the view's new moments into effects, heard now (host time `now`).
    pub fn moments(
        &mut self,
        moments: &[Moment],
        now: f64,
        lib: &ContentLibrary,
        looks: &HashMap<String, Look>,
        view: &LiveView,
    ) {
        let mut fire = |id: &str, x: f64, y: f64, dir: f64, intensity: f64, start: Start| {
            if let Some(recipe) = lib.effects.get(id) {
                self.effects.start(recipe, x, y, dir, intensity, start, now);
            }
        };
        let groove = view.groove.clamp(0.0, 1.0);
        self.struck.retain(|_, s| now - s.0 < 1.0);
        // (The hero's places, for the fire trail.)
        if let Some(h) = view.hero {
            self.trail.push_back((now, h.x, h.y));
            while self.trail.front().is_some_and(|p| now - p.0 > 1.0) {
                self.trail.pop_front();
            }
        }
        let view_tier = (view.tier.max(0.0) as usize).min(3);
        for m in moments {
            // (Each enemy's last hit, for its flash, knockback and fall.)
            if let Moment::Hit { foe, lethal, .. } = m {
                self.struck.insert(*foe, (now, *lethal));
            }
            match m {
                // (As emit.ts fires them; native/evidence/vfx/ts-spec.md.) A swing's arc by its
                // kind, brighter with the groove, in its tier's colour and fitted to its hit.
                Moment::Swing {
                    x,
                    y,
                    dir,
                    kind,
                    tier,
                    intensity,
                    shape,
                } => {
                    let i = (intensity * (0.55 + 0.45 * groove) + 0.25 * groove).min(1.0);
                    fire(
                        &format!("hit-{kind}"),
                        *x,
                        *y,
                        *dir,
                        i,
                        Start {
                            tier: Some(*tier),
                            shape: *shape,
                            ..Start::default()
                        },
                    );
                }
                // A hit an enemy survives: a swing's by its weight, any other's the blades'.
                Moment::Hit {
                    x,
                    y,
                    lethal: false,
                    source,
                    weight,
                    dir,
                    intensity,
                    ..
                } if x.is_finite() => {
                    let id = if *source == "move" {
                        format!("hit-{weight}")
                    } else {
                        "blades-hit".to_string()
                    };
                    fire(&id, *x, *y, *dir, 0.6 * intensity, Start::default());
                }
                // A kill: the finale's ring blasts, anything else the enemy's own death.
                Moment::Hit {
                    x,
                    y,
                    lethal: true,
                    foe,
                    source,
                    dir,
                    intensity,
                    ..
                } if x.is_finite() => {
                    let look = view.foes.get(foe).and_then(|f| looks.get(&f.kind));
                    let debris = look.map(|l| l.debris.clone()).unwrap_or_default();
                    let recipe = if *source == "ring" {
                        "blast-hit".to_string()
                    } else {
                        look.map_or_else(|| "death-humanoid".to_string(), |l| l.death.clone())
                    };
                    fire(
                        &recipe,
                        *x,
                        *y,
                        *dir,
                        intensity.max(0.4),
                        Start {
                            debris,
                            ..Start::default()
                        },
                    );
                }
                // A weapon's own effect: a chain on every jump, the others when they catch.
                Moment::WeaponHit {
                    x,
                    y,
                    what,
                    dir,
                    intensity,
                    caught,
                } => {
                    if let Some(wd) = lib.weapons.get(what) {
                        let chain = wd.kind == sim::content::types::WeaponKind::Chain;
                        if chain != *caught {
                            fire(&wd.effect, *x, *y, *dir, *intensity, Start::default());
                        }
                    }
                }
                Moment::WaveBand { x, y, dir } => fire(
                    "shockwave",
                    *x,
                    *y,
                    *dir,
                    0.8,
                    Start {
                        tier: Some(view_tier),
                        ..Start::default()
                    },
                ),
                Moment::Landing { x, y, stomp: true } => {
                    fire("stomp", *x, *y, 0.0, 1.0, Start::default());
                }
                Moment::Landing { x, y, stomp: false } => {
                    fire("dust", *x, *y, 0.0, 0.5, Start::default());
                }
                Moment::Bite { .. } => self.bitten = now,
                Moment::TierChange { up, .. } => self.tier_change = Some((now, *up)),
                Moment::GemPickup => {
                    if let Some(h) = view.hero {
                        fire("gem-pickup", h.x, h.y - 16.0, 0.0, 1.0, Start::default());
                    }
                }
                Moment::Press {
                    rating, unjudged, ..
                } => self.lane.press(now, *rating, *unjudged),
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
            self.effects
                .start(recipe, h.x, h.y - 16.0, 0.0, 1.0, Start::default(), now);
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
        lib: &ContentLibrary,
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
        let fast = std::mem::take(&mut self.fast);
        // (The camera punches in on each accent.)
        let scale = (w / VIEW_W).min(h / VIEW_H) * (zoom * (1.0 + fast.punch)) as f32;
        layers.grade = grade(ground.theme, pulse, now, scale, &fast);
        // (As the wave's ring passes the camera, the grade blends into the new zone's.)
        if let Some((z, wx, wy, r)) = ground.wave {
            let d = ((cx - wx).powi(2) + (cy - wy).powi(2)).sqrt();
            let t = ((r - d + 150.0) / 300.0).clamp(0.0, 1.0);
            let t = (t * t * (3.0 - 2.0 * t)) as f32;
            let next = grade(z, pulse, now, scale, &fast);
            let g = &mut layers.grade;
            let mix = |a: f32, b: f32| a + (b - a) * t;
            for k in 0..3 {
                g.ambient[k] = mix(g.ambient[k], next.ambient[k]);
                g.mul[k] = mix(g.mul[k], next.mul[k]);
                g.lift[k] = mix(g.lift[k], next.lift[k]);
            }
            g.contrast = mix(g.contrast, next.contrast);
            g.saturation = mix(g.saturation, next.saturation);
            g.haze = mix(g.haze, next.haze);
        }
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
                // (Each tile in the zone at its centre: the wave's inside its ring.)
                let zone = ground.zone_at(i as f64 * t + t / 2.0, j as f64 * t + t / 2.0);
                let (sx, sy) = at(i as f64 * t, j as f64 * t);
                match art
                    .tiles
                    .get(&zone.terrain.ground)
                    .filter(|v| !v.is_empty())
                {
                    Some(tiles) => {
                        let v = field::tile_variant(ground.seed, i, j);
                        let mut s =
                            Sprite::anchored(tiles[v % tiles.len()], sx, sy, (0.0, 0.0), scale);
                        s.w += 0.5;
                        s.h += 0.5;
                        out.push(s);
                    }
                    None => {
                        let k = (t as f32) * scale + 0.5;
                        out.push(rect(sx, sy, k, k, [0.1, 0.2, 0.1, 1.0]));
                    }
                }
            }
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
            .retain(|(a, b, _), _| *a >= c0 - 1 && *a <= c1 + 1 && *b >= r0 - 1 && *b <= r1 + 1);
        let mut zones = vec![ground.theme];
        if let Some((z, ..)) = ground.wave {
            zones.push(z);
        }
        let mut visible = Vec::new();
        for zone in &zones {
            for b in r0..=r1 {
                for a in c0..=c1 {
                    let key = (a, b, zone.id.clone());
                    self.chunks
                        .entry(key.clone())
                        .or_insert_with(|| field::chunk_content(ground.seed, a, b, zone));
                    visible.push(key);
                }
            }
        }
        // (A prop shows where its own zone is: the wave's inside its ring, the old one outside.)
        let shows = |zone: &str, p: &FieldProp| ground.zone_at(p.x, p.y).id == zone;
        // Trees, bushes and mushrooms sway with the bar, as strongly as the pulse shows (each a
        // little differently).
        let swell = 0.5 + 0.5 * (std::f64::consts::TAU * pulse.bar.clamp(0.0, 1.0)).cos();
        let sway = (swell - 0.5) * (fast.groove() + (1.0 - fast.mix) * fast.press);
        let prop = |p: &FieldProp, tint: [f32; 4], out: &mut Vec<Sprite>| {
            if let Some(variants) = art.props.get(&p.sprite).filter(|v| !v.is_empty()) {
                let v = variants[p.variant % variants.len()];
                let (sx, sy) = at(p.x, p.y);
                let sways = ["tree", "bush", "mushroom"]
                    .iter()
                    .any(|k| p.sprite.starts_with(k));
                let skew = if sways {
                    sway * 0.06 * (0.6 + 0.4 * rand01(p.x.to_bits(), p.y.to_bits(), 0x5a7))
                } else {
                    0.0
                };
                let s = Sprite::anchored(v.region, sx, sy, (v.anchor_x, v.anchor_y), scale)
                    .tinted(tint)
                    .skewed(skew as f32);
                out.push(if p.flip { s.mirrored() } else { s });
            }
        };
        for key in &visible {
            for p in self.chunks[key].ground.iter().filter(|p| shows(&key.2, p)) {
                prop(p, [1.0; 4], out);
            }
        }
        // Decals on the ground, under everyone; the hero's glow and the tier change's ring.
        self.effects.draw_decals(art, now, &at, scale, out);
        let ring = self
            .tier_change
            .map_or(0.0, |(t, _)| 1.0 - (now - t) / TIER_RING_S)
            .clamp(0.0, 1.0);
        tier::ground(view, art, &at, scale, ring, out);
        // (Bursts marked to stay under the characters, as the gems' pickups.)
        self.effects.draw(art, now, &at, scale, w, h, true, out);
        // The wave's edge: a ring in the new zone's light, and a wider one in its tint.
        if let Some((z, wx, wy, r)) = ground.wave {
            let light = hex(&z.ambient_light);
            let tint = hex(&z.tint);
            for (radius, color, alpha, width) in
                [(r, light, 0.75, 10.0), (r + 30.0, tint, 0.35, 24.0)]
            {
                let n = ((radius * 0.25) as usize).clamp(48, 720);
                let k = (width as f32 * scale).max(2.0);
                for i in 0..n {
                    let a = i as f64 / n as f64 * std::f64::consts::TAU;
                    let (sx, sy) = at(wx + a.cos() * radius, wy + a.sin() * radius);
                    out.push(rect(
                        sx - k / 2.0,
                        sy - k / 2.0,
                        k,
                        k,
                        [color[0], color[1], color[2], alpha],
                    ));
                }
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
                if shows(&key.2, p) {
                    order.push((p.y, Drawn::Tall(key.clone(), k)));
                }
            }
        }
        order.sort_by(|a, b| a.0.total_cmp(&b.0));
        let hero_at = view.hero.map(|h| (h.x, h.y));
        for (_, who) in order {
            match who {
                Drawn::Tall(key, k) => {
                    let p = &self.chunks[&key].tall[k];
                    let key = &key;
                    let fade =
                        hero_at.map_or(1.0, |(hx, hy)| field::prop_fade(p.x, p.y, hx, hy)) as f32;
                    prop(p, [1.0, 1.0, 1.0, fade], out);
                    // A lamp's or brazier's light, flickering.
                    if let Some(l) = zones
                        .iter()
                        .find(|z| z.id == key.2)
                        .map_or(&ground.theme.props, |z| &z.props)
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
                    let facing = Facing::toward((f.x - f.prev.0) as f32, (f.y - f.prev.1) as f32);
                    let dead = f.death_w.is_some();
                    // The fast lane: the living horde hops between the beats, landing in a squash
                    // on each (each kind a step later, so it ripples), and flinches at onsets.
                    let (mut kx, mut ky, mut bob) = (1.0, 1.0, 0.0);
                    if !dead {
                        let u = (pulse.beat.clamp(0.0, 1.0) - type_phase(&f.kind)).rem_euclid(1.0);
                        let land = (-6.0 * u).exp();
                        let g = fast.groove();
                        bob = ENEMY_BOB * g * (std::f64::consts::PI * u).sin();
                        kx = (1.0 + ENEMY_SQUASH * g * land)
                            * (1.0 - 0.5 * ENEMY_FLINCH * fast.flinch);
                        ky = (1.0 - ENEMY_SQUASH * g * land) * (1.0 + ENEMY_FLINCH * fast.flinch);
                    }
                    // A hit flashes it white; the killing blow knocks the body back from the
                    // hero, squashed, and it falls (its hurt frames) and fades.
                    let struck = self.struck.get(&id).map(|&(t, lethal)| (now - t, lethal));
                    let flash = struck.is_some_and(|(age, _)| (0.0..HIT_FLASH_S).contains(&age));
                    let (mut x, mut y) = (f.x, f.y - bob);
                    if let (Some((age, true)), Some(h)) = (struck, view.hero)
                        && (0.0..KNOCK_S).contains(&age)
                    {
                        let left = 1.0 - age / KNOCK_S;
                        let (dx, dy) = (f.x - h.x, f.y - h.y);
                        let d = dx.hypot(dy).max(1e-6);
                        x += dx / d * left * left * 10.0;
                        y += dy / d * left * left * 10.0;
                        kx *= 1.0 + 0.15 * left;
                        ky *= 1.0 - 0.12 * left;
                    }
                    let (sx, sy) = at(x, y);
                    // (A body whose killing blow went unseen is gone.)
                    let fall = match struck {
                        Some((age, true)) if dead => Some(age.max(0.0)),
                        _ if dead => None,
                        _ => Some(-1.0),
                    };
                    let Some(fall) = fall else {
                        continue;
                    };
                    let alpha = if fall >= 0.0 {
                        let u =
                            ((fall - DEATH_FALL_S * 0.5) / (DEATH_FALL_S * 0.5)).clamp(0.0, 1.0);
                        (1.0 - u * u * (3.0 - 2.0 * u)) as f32
                    } else {
                        1.0
                    };
                    if alpha <= 0.0 {
                        continue;
                    }
                    let tint = [1.0, 1.0, 1.0, alpha];
                    let hurt = art.animation(sprite, "hurt").filter(|_| fall >= 0.0);
                    if let Some(walk) = hurt.or_else(|| art.animation(sprite, "walk")) {
                        // (Enemies are sampled 60 times a second; a walk frame every 6 samples;
                        // a fall's six frames over its length.)
                        let frame = if hurt.is_some() {
                            ((fall / DEATH_FALL_S * 6.0) as usize).min(5)
                        } else {
                            f.samples / 6
                        };
                        out.push(stretch(
                            Sprite::anchored(
                                walk.frame(facing, frame, flash),
                                sx,
                                sy,
                                (walk.anchor_x, walk.anchor_y),
                                size * scale,
                            )
                            .tinted(tint),
                            (sx, sy),
                            (kx as f32, ky as f32),
                        ));
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
                    // The tier's aura behind the hero, swelling with the beat.
                    let swell = 0.5 + 0.5 * (std::f64::consts::TAU * pulse.beat.min(1.0)).cos();
                    tier::aura(view, &hero, art, &at, scale, fast.pulsed(swell), out);
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
        let arms = Arms {
            lib,
            beat: pulse.index,
            now,
            run_s: self.run_s,
            trail: self.trail.make_contiguous(),
        };
        tier::weapons(view, &arms, art, &at, scale, out);
        let swell = 0.5 + 0.5 * (std::f64::consts::TAU * pulse.beat.min(1.0)).cos();
        gems::draw(view, art, &at, scale, self.run_s, fast.pulsed(swell), out);
        self.effects.draw(art, now, &at, scale, w, h, false, out);
        self.effects
            .post(now, &at, scale, lights, &mut layers.grade);
        draw_fast(&fast, view, art, &at, scale, out, lights);
        // (The accent flash, unless an effect's is stronger.)
        let fa = fast.flash as f32;
        if fa > layers.grade.flash[3] {
            layers.grade.flash = [1.0, 1.0, 1.0, fa];
        }
        self.fast = fast;
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
