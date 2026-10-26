//! `src/sim/movement.ts`: the movement model, the only thing that moves the hero. Every function is
//! a pure function of the input so far. Times are world seconds and distances world pixels.

#![allow(clippy::many_single_char_names)] // (the TS's names)

use std::f64::consts::SQRT_2;

use crate::content::types::HeroDef;
use crate::core::shaping::{clamp, smoothstep};
use crate::jsmath::{PI, abs, floor, max, pow, sin};

/// A direction key.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Key {
    Up,
    Down,
    Left,
    Right,
}

/// A gameplay key: a direction, or Space (a stomp).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum InputKey {
    Up,
    Down,
    Left,
    Right,
    Space,
    Confirm,
    Pause,
}

impl InputKey {
    pub const ALL: [Self; 7] = [
        Self::Up,
        Self::Left,
        Self::Down,
        Self::Right,
        Self::Space,
        Self::Confirm,
        Self::Pause,
    ];

    pub fn slot(self) -> usize {
        Self::ALL
            .iter()
            .position(|&key| key == self)
            .expect("every key has a slot")
    }

    pub fn parse_recorded(name: &str) -> Result<Self, String> {
        match name {
            "Up" => Ok(Self::Up),
            "Left" => Ok(Self::Left),
            "Down" => Ok(Self::Down),
            "Right" => Ok(Self::Right),
            "Space" => Ok(Self::Space),
            "Confirm" => Ok(Self::Confirm),
            "Pause" => Ok(Self::Pause),
            _ => Err(format!("unknown recorded input key {name:?}")),
        }
    }

    /// The direction, or None for Space.
    pub fn direction(self) -> Option<Key> {
        match self {
            InputKey::Up => Some(Key::Up),
            InputKey::Down => Some(Key::Down),
            InputKey::Left => Some(Key::Left),
            InputKey::Right => Some(Key::Right),
            InputKey::Space | InputKey::Confirm | InputKey::Pause => None,
        }
    }

    /// The name TS gives it (`"up"`, ..., `"space"`).
    pub fn name(self) -> &'static str {
        match self {
            InputKey::Up => "up",
            InputKey::Down => "down",
            InputKey::Left => "left",
            InputKey::Right => "right",
            InputKey::Space => "space",
            InputKey::Confirm => "confirm",
            InputKey::Pause => "pause",
        }
    }
}

impl From<Key> for InputKey {
    fn from(k: Key) -> InputKey {
        match k {
            Key::Up => InputKey::Up,
            Key::Down => InputKey::Down,
            Key::Left => InputKey::Left,
            Key::Right => InputKey::Right,
        }
    }
}

/// A unit vector in world coordinates (y grows downward).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Dir {
    pub x: f64,
    pub y: f64,
}

/// Unit vector of each key (`KEY_DIR`).
pub fn key_dir(k: Key) -> Dir {
    match k {
        Key::Up => Dir { x: 0.0, y: -1.0 },
        Key::Down => Dir { x: 0.0, y: 1.0 },
        Key::Left => Dir { x: -1.0, y: 0.0 },
        Key::Right => Dir { x: 1.0, y: 0.0 },
    }
}

pub const KEYS: [Key; 4] = [Key::Up, Key::Left, Key::Down, Key::Right];

/// Two keys on opposite sides?
pub fn opposite(a: Key, b: Key) -> bool {
    let (da, db) = (key_dir(a), key_dir(b));
    da.x == -db.x && da.y == -db.y
}

/// Wall rule: the longest distance (0 to `len`) a straight move from (x, y) along the unit vector
/// (dx, dy) can cover while staying walkable.
pub type Clamp = dyn Fn(f64, f64, f64, f64, f64) -> f64;

/// No walls: every move covers its whole length.
pub fn no_walls(_x: f64, _y: f64, _dx: f64, _dy: f64, len: f64) -> f64 {
    len
}

/// The press a held stride took over from: its world and song time and where its stride began.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct TakenPress {
    pub w: f64,
    pub s: f64,
    pub x: f64,
    pub y: f64,
}

/// A stomp turned the stride into a rock step at world time w, at (x, y), back to (tx, ty).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Rock {
    pub w: f64,
    pub x: f64,
    pub y: f64,
    pub tx: f64,
    pub ty: f64,
}

/// One step, from its press to its landing.
#[derive(Clone, Debug, PartialEq)]
pub struct Step {
    /// World time of the press that started it.
    pub w0: f64,
    pub x0: f64,
    pub y0: f64,
    /// Unit direction of the first key.
    pub dx: f64,
    pub dy: f64,
    /// Distance the first key's leg covers (shortened at walls).
    pub len1: f64,
    /// Final displacement from (x0, y0).
    pub ex: f64,
    pub ey: f64,
    /// Ease progress at which a perpendicular key bent the step toward the diagonal.
    pub bend_e: Option<f64>,
    pub keys: Vec<Key>,
    /// Strides: the stride's length before walls, and whether a held key started it.
    pub len: Option<f64>,
    pub held: Option<bool>,
    /// Strides: the song time it started.
    pub s0: Option<f64>,
    pub press: Option<TakenPress>,
    pub rock: Option<Rock>,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct StepPose {
    pub x: f64,
    pub y: f64,
    /// Hop height.
    pub z: f64,
    /// Time progress 0–1.
    pub u: f64,
}

pub fn step_seconds(hero: &HeroDef) -> f64 {
    hero.step.duration_ms / 1000.0
}

/// Cubic ease-out: the hero leaves fast and lands firmly.
pub fn ease_out(u: f64) -> f64 {
    1.0 - pow(1.0 - u, 3.0)
}

/// A perpendicular key soon after the first bends the rest of the step toward the diagonal; the step
/// still lands one step length from where it started. None when the key does not bend this step.
pub fn bend_step(
    hero: &HeroDef,
    step: &Step,
    w: f64,
    key: Key,
    walls: &Clamp,
    duration_s: f64,
) -> Option<Step> {
    let since = step.press.map_or(step.w0, |p| p.w);
    if step.bend_e.is_some()
        || step.held == Some(true)
        || step.rock.is_some()
        || step.keys.is_empty()
        || w - since > hero.step.diagonal_window_ms / 1000.0 + 1e-9
    {
        return None;
    }
    let d = key_dir(key);
    if abs(d.x * step.dx + d.y * step.dy) > 1e-9 {
        return None;
    }
    let u = clamp((w - step.w0) / duration_s, 0.0, 1.0);
    let gx = (step.dx + d.x) / SQRT_2;
    let gy = (step.dy + d.y) / SQRT_2;
    let len = walls(
        step.x0,
        step.y0,
        gx,
        gy,
        step.len.unwrap_or(hero.step.length),
    );
    let mut keys = step.keys.clone();
    keys.push(key);
    Some(Step {
        ex: gx * len,
        ey: gy * len,
        bend_e: Some(ease_out(u)),
        keys,
        ..step.clone()
    })
}

/// Where a step or stride lasting `duration_s`, with a hop of `hop` px, has taken the hero at w.
pub fn move_pose(step: &Step, w: f64, duration_s: f64, hop: f64) -> StepPose {
    let u = clamp((w - step.w0) / duration_s, 0.0, 1.0);
    if let Some(rock) = step.rock
        && w >= rock.w
    {
        let g = ease_out(clamp(
            (w - rock.w) / max(1e-9, step.w0 + duration_s - rock.w),
            0.0,
            1.0,
        ));
        return StepPose {
            x: rock.x + (rock.tx - rock.x) * g,
            y: rock.y + (rock.ty - rock.y) * g,
            z: hop * sin(PI * u),
            u,
        };
    }
    let e = ease_out(u);
    let mut x = step.x0 + step.dx * step.len1 * e;
    let mut y = step.y0 + step.dy * step.len1 * e;
    if let Some(bend_e) = step.bend_e
        && e > bend_e
    {
        // Curve smoothly from the first key's line onto the diagonal landing spot.
        let g = smoothstep((e - bend_e) / max(1e-6, 1.0 - bend_e));
        x += (step.ex - step.dx * step.len1) * g;
        y += (step.ey - step.dy * step.len1) * g;
    }
    StepPose {
        x,
        y,
        z: hop * sin(PI * u),
        u,
    }
}

/// Landing spot of a step.
pub fn step_end(step: &Step) -> Dir {
    Dir {
        x: step.x0 + step.ex,
        y: step.y0 + step.ey,
    }
}

/// Walk-cycle frame for the distance walked: one full cycle per strideCycle pixels.
pub fn walk_frame(hero: &HeroDef, distance: f64, frames: f64) -> f64 {
    let c = distance / hero.walk.stride_cycle;
    // (JS's % keeps the dividend's sign, as Rust's does.)
    floor((c - floor(c)) * frames) % frames
}
