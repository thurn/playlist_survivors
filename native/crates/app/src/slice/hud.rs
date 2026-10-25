//! The HUD (`src/runtime/hud.ts`, read as a reference): in HUD units of a 960 x 540 screen, in the
//! TS game's 5 x 7 pixel font. Experience and level along the top, health top left, kills and the
//! rated presses top right, Delayed listening's tag under health, the arrow keys bottom left,
//! and the beat track: the committed beats slide toward a beating heart, presses flash on it
//! early or late in their rating's colour, and the groove meter beside it shows the tier. Over the hero's head, `overhead.ts`'s display: life
//! over groove, and a gem keeping the beat as a ring closes on it.
// (Colours as the TS source writes them.)
#![allow(clippy::unreadable_literal, clippy::many_single_char_names)]

use eval::live::LiveView;
use render::font::{self, Align};

use super::gfx::{Sprite, rect};

/// Press ratings' colours: gold Perfect, white Great, pale blue Good (`RATING_COLORS`).
const PERFECT: [f32; 4] = rgb(0xffd040);
const GREAT: [f32; 4] = rgb(0xffffff);
const GOOD: [f32; 4] = rgb(0x9fd0ff);
const PLAIN: [f32; 4] = rgb(0x8890a8);
/// Each tier's colour on the groove meter: cool at 0, gold at 3 (`TIER_COLORS`).
const TIERS: [[f32; 4]; 4] = [rgb(0x7088a8), rgb(0x60c0ff), rgb(0x70f0c0), rgb(0xffd040)];

const fn rgb(c: u32) -> [f32; 4] {
    [
        ((c >> 16) & 0xff) as f32 / 255.0,
        ((c >> 8) & 0xff) as f32 / 255.0,
        (c & 0xff) as f32 / 255.0,
        1.0,
    ]
}

fn alpha(c: [f32; 4], a: f32) -> [f32; 4] {
    [c[0], c[1], c[2], c[3] * a]
}

/// A rating's colour.
pub fn rating_color(r: Option<&str>) -> [f32; 4] {
    match r {
        Some("perfect") => PERFECT,
        Some("great") => GREAT,
        Some("good") => GOOD,
        _ => PLAIN,
    }
}

/// A key on the display: lit while held, in its last press's colour.
#[derive(Clone, Copy, Debug, Default)]
pub struct KeyLight {
    pub held: bool,
    pub rating: Option<&'static str>,
}

/// A press on the beat track: how long ago (s), how far off the grid (s), its rating.
#[derive(Clone, Copy, Debug)]
pub struct PressMark {
    pub age: f64,
    pub delta: f64,
    pub rating: Option<&'static str>,
}

/// What the HUD reads besides the sim's view.
pub struct HudState<'a> {
    /// Up, left, down, right, and the stomp key.
    pub keys: [KeyLight; 5],
    pub presses: &'a [PressMark],
    /// The committed beats near now, as times from now (s).
    pub beats: &'a [f64],
    /// Since the last beat, over its period (0-1).
    pub beat_phase: f64,
    /// The run's elapsed time (s).
    pub elapsed: f64,
    /// When the hero was last bitten, seconds ago.
    pub bitten_ago: f64,
    /// The top of the hero's head on screen, if placed.
    pub head: Option<(f32, f32)>,
    /// Whether the next beat starts a bar.
    pub next_down: bool,
    /// The groove meter's flash after a tier up (1 to 0).
    pub tier_flash: f64,
    /// How late Delayed listening hears the music (s; 0 in Direct), tagged under health.
    pub delayed: f64,
    /// Zen mode (nothing judged, the default); legacy mode is tagged under health.
    pub zen: bool,
}

/// A small heart, 7 x 6 font pixels.
const HEART: [&str; 6] = [
    ".##.##.", "#######", "#######", ".#####.", "..###..", "...#...",
];

pub fn draw(view: &LiveView, st: &HudState, w: f32, h: f32, out: &mut Vec<Sprite>) {
    let u = (w / 960.0).min(h / 540.0);
    overhead(view, st, w, h, u, out);
    let (ox, oy) = ((w - 960.0 * u) / 2.0, (h - 540.0 * u) / 2.0);
    let px = |x: f32| ox + x * u;
    let py = |y: f32| oy + y * u;
    let fs = u.round().max(1.0);
    let bg = [0.0, 0.0, 0.0, 0.55];
    // Experience along the top, the level beside it.
    let (xp_w, xp_x) = (520.0, 480.0 - 260.0);
    out.push(rect(
        px(xp_x - 2.0),
        py(6.0),
        (xp_w + 4.0) * u,
        10.0 * u,
        bg,
    ));
    out.push(rect(
        px(xp_x),
        py(8.0),
        xp_w * u * view.xp.clamp(0.0, 1.0) as f32,
        6.0 * u,
        rgb(0x60ffc0),
    ));
    font::text(
        &format!("LV {}", view.level),
        px(xp_x - 8.0),
        py(7.0),
        fs,
        Align::Right,
        rgb(0x60ffc0),
        out,
    );
    // Health, top left; a bite flashes it white.
    font::text(
        "HP",
        px(12.0),
        py(14.0),
        fs,
        Align::Left,
        rgb(0xff8090),
        out,
    );
    out.push(rect(px(12.0), py(24.0), 90.0 * u, 7.0 * u, bg));
    let hp = if st.bitten_ago < 0.25 {
        rgb(0xffffff)
    } else {
        rgb(0xff4050)
    };
    out.push(rect(
        px(13.0),
        py(25.0),
        88.0 * u * view.health.clamp(0.0, 1.0) as f32,
        5.0 * u,
        hp,
    ));
    if st.delayed > 0.0 {
        font::text(
            &format!("DELAYED {} S", st.delayed),
            px(12.0),
            py(36.0),
            fs,
            Align::Left,
            rgb(0x9fe6ff),
            out,
        );
    }
    if !st.zen {
        let y = if st.delayed > 0.0 { 46.0 } else { 36.0 };
        font::text(
            "LEGACY",
            px(12.0),
            py(y),
            fs,
            Align::Left,
            rgb(0xffc070),
            out,
        );
    }
    // Kills and the rated presses, top right.
    font::text(
        "KILLS",
        px(948.0),
        py(14.0),
        fs,
        Align::Right,
        rgb(0xb0b0c0),
        out,
    );
    font::text(
        &view.kills.to_string(),
        px(948.0),
        py(24.0),
        (2.0 * u).round().max(1.0),
        Align::Right,
        rgb(0xffffff),
        out,
    );
    // (Zen mode judges nothing: no rated presses, no press marks on the beat track.)
    let rated: &[(&str, [f32; 4])] = if st.zen {
        &[]
    } else {
        &[("PERFECT", PERFECT), ("GREAT", GREAT), ("GOOD", GOOD)]
    };
    for (i, &(name, color)) in rated.iter().enumerate() {
        let line = format!("{name} {}", view.ratings[i]);
        font::text(
            &line,
            px(948.0),
            py(44.0 + 10.0 * i as f32),
            fs,
            Align::Right,
            color,
            out,
        );
    }
    // The arrow keys and the stomp key, bottom left.
    let places = [(1.0, 0.0), (0.0, 1.0), (1.0, 1.0), (2.0, 1.0)];
    for (k, &(col, row)) in places.iter().enumerate() {
        key_face(
            st.keys[k],
            px(26.0 + col * 25.0),
            py(452.0 + row * 25.0),
            22.0 * u,
            22.0 * u,
            out,
        );
    }
    key_face(st.keys[4], px(104.0), py(481.0), 10.0 * u, 18.0 * u, out);
    key_face(st.keys[4], px(117.0), py(479.0), 10.0 * u, 18.0 * u, out);
    let elapsed = st.elapsed.max(0.0) as u64;
    font::text(
        &format!("{}:{:02}", elapsed / 60, elapsed % 60),
        px(20.0),
        py(520.0),
        fs,
        Align::Left,
        rgb(0xc0c0c0),
        out,
    );
    // The beat track: the beats slide toward the heart from both sides.
    let (cx, cy) = (480.0, 504.0);
    out.push(rect(
        px(cx - 200.0),
        py(cy - 9.0),
        400.0 * u,
        18.0 * u,
        [0.0, 0.0, 0.0, 0.45],
    ));
    for &dt in st.beats {
        let d = dt as f32 * 90.0;
        if d > 186.0 {
            continue;
        }
        let mut a = (1.0 - dt / 2.2).clamp(0.0, 1.0) as f32;
        if dt < 0.0 {
            a *= (1.0 + dt as f32 * 8.0).max(0.0);
        }
        let sz = 2.5 * u;
        for side in [-1.0, 1.0] {
            out.push(rect(
                px(cx + side * (14.0 + d)) - sz / 2.0,
                py(cy) - sz * 1.6,
                sz,
                sz * 3.2,
                [1.0, 1.0, 1.0, a],
            ));
        }
    }
    for p in st.presses.iter().filter(|_| !st.zen) {
        let a = (1.0 - p.age / 0.6).clamp(0.0, 1.0) as f32;
        if a <= 0.0 {
            continue;
        }
        let x = cx + (p.delta as f32 * 400.0).clamp(-70.0, 70.0);
        let r = (4.0 + 6.0 * (1.0 - a)) * u;
        out.push(rect(
            px(x) - r,
            py(cy - 20.0) - r,
            2.0 * r,
            2.0 * r,
            alpha(rating_color(p.rating), a * 0.35),
        ));
        out.push(rect(
            px(x) - 2.5 * u,
            py(cy - 20.0) - 2.5 * u,
            5.0 * u,
            5.0 * u,
            alpha(rating_color(p.rating), a),
        ));
    }
    let pulse = (-6.0 * st.beat_phase.clamp(0.0, 1.0)).exp() as f32;
    let hs = (1.4 + 0.6 * pulse) * u;
    let (hx, hy) = (px(cx) - 3.5 * hs, py(cy) - 3.0 * hs);
    for (r, row) in HEART.iter().enumerate() {
        for (c, b) in row.bytes().enumerate() {
            if b == b'#' {
                out.push(rect(
                    hx + c as f32 * hs,
                    hy + r as f32 * hs,
                    hs,
                    hs,
                    rgb(0xff3060),
                ));
            }
        }
    }
    // The groove meter and the tier.
    let tier = (view.tier.max(0.0) as usize).min(TIERS.len() - 1);
    let (gx, gw) = (cx + 212.0, 70.0);
    font::text(
        "GROOVE",
        px(gx),
        py(cy - 16.0),
        fs,
        Align::Left,
        rgb(0xb0b0c0),
        out,
    );
    // (A tier up flashes the meter in the tier's colour, and lifts its label in white.)
    let flash = st.tier_flash.clamp(0.0, 1.0) as f32;
    if flash > 0.0 {
        let mut c = TIERS[tier];
        c[3] = 0.45 * flash;
        out.push(rect(
            px(gx - 3.0),
            py(cy - 8.0),
            (gw + 6.0) * u,
            16.0 * u,
            c,
        ));
    }
    out.push(rect(px(gx), py(cy - 5.0), gw * u, 10.0 * u, bg));
    out.push(rect(
        px(gx + 1.0),
        py(cy - 4.0),
        (gw - 2.0) * u * view.groove.clamp(0.0, 1.0) as f32,
        8.0 * u,
        TIERS[tier],
    ));
    font::text(
        &format!("TIER {tier}"),
        px(gx + gw + 6.0),
        py(cy - 4.0 - 2.0 * flash),
        fs,
        Align::Left,
        if flash > 0.0 {
            rgb(0xffffff)
        } else {
            TIERS[tier]
        },
        out,
    );
}

fn smoothstep(x: f32) -> f32 {
    let x = x.clamp(0.0, 1.0);
    x * x * (3.0 - 2.0 * x)
}

/// A filled diamond at (x, y), `r` tall and `rx` wide each way, in rows `p` high.
fn diamond(x: f32, y: f32, r: f32, rx: f32, p: f32, color: [f32; 4], out: &mut Vec<Sprite>) {
    let rows = (r / p).ceil() as i32;
    for k in -rows..rows {
        let mid = (k as f32 + 0.5) * p;
        let half = rx * (1.0 - mid.abs() / r);
        if half > 0.0 {
            out.push(rect(x - half, y + k as f32 * p, 2.0 * half, p, color));
        }
    }
}

/// A diamond's outline, `t` thick.
fn diamond_edge(x: f32, y: f32, r: f32, rx: f32, t: f32, color: [f32; 4], out: &mut Vec<Sprite>) {
    let n = ((r + rx) / t).ceil().max(4.0) as i32;
    for k in 0..n {
        let f = k as f32 / n as f32;
        for (sx, sy) in [(1.0, 1.0), (1.0, -1.0), (-1.0, 1.0), (-1.0, -1.0)] {
            let (dx, dy) = (sx * rx * f, sy * r * (1.0 - f));
            out.push(rect(x + dx - t / 2.0, y + dy - t / 2.0, t, t, color));
        }
    }
}

/// A filled disc, in rows `p` high.
fn disc(x: f32, y: f32, r: f32, p: f32, color: [f32; 4], out: &mut Vec<Sprite>) {
    let rows = (r / p).ceil() as i32;
    for k in -rows..rows {
        let mid = (k as f32 + 0.5) * p;
        let half = (r * r - mid * mid).max(0.0).sqrt();
        if half > 0.0 {
            out.push(rect(x - half, y + k as f32 * p, 2.0 * half, p, color));
        }
    }
}

/// A circle's outline, `t` thick.
fn ring(x: f32, y: f32, r: f32, t: f32, color: [f32; 4], out: &mut Vec<Sprite>) {
    let n = (std::f32::consts::TAU * r / t).ceil().max(12.0) as i32;
    for k in 0..n {
        let a = k as f32 / n as f32 * std::f32::consts::TAU;
        out.push(rect(
            x + a.cos() * r - t / 2.0,
            y + a.sin() * r - t / 2.0,
            t,
            t,
            color,
        ));
    }
}

/// The display over the hero's head (`overhead.ts`, `hud.ts`): life over groove, and the gem
/// keeping the beat, a ring closing on it toward each beat (pale before a bar); without beats,
/// the gem breathes. `hud` is the HUD unit.
fn overhead(view: &LiveView, st: &HudState, w: f32, h: f32, hud: f32, out: &mut Vec<Sprite>) {
    let Some((hx, hy)) = st.head else { return };
    let a = smoothstep((st.elapsed as f32 - 0.5) / 1.5);
    if a <= 0.0 {
        return;
    }
    let u = 1.3 * hud;
    let margin = 30.0 * u;
    let x = hx.clamp(margin, (w - margin).max(margin));
    let y = (hy - 16.0 * hud).clamp(16.0 * hud, (h - 16.0 * hud).max(16.0 * hud));
    let tier = TIERS[(view.tier.max(0.0) as usize).min(TIERS.len() - 1)];
    let line = u.max(1.0);
    let (half, bh) = (26.0 * u, 4.0 * u);
    let (life_y, groove_y) = (y - bh - u, y + u);
    out.push(rect(
        x - half - line,
        life_y - line,
        2.0 * (half + line),
        2.0 * bh + 2.0 * u + 2.0 * line,
        [0.02, 0.016, 0.04, 0.65 * a],
    ));
    let health = view.health.clamp(0.0, 1.0) as f32;
    let danger = (1.0 - health / 0.3).clamp(0.0, 1.0);
    let hit = (-8.0 * st.beat_phase.clamp(0.0, 1.0)).exp() as f32;
    let life = if st.bitten_ago < 0.15 {
        rgb(0xffffff)
    } else if danger * hit > 0.5 {
        rgb(0xff9098)
    } else {
        rgb(0xff4050)
    };
    out.push(rect(
        x - half,
        life_y,
        2.0 * half * health,
        bh,
        alpha(life, a),
    ));
    out.push(rect(
        x - half,
        groove_y,
        2.0 * half * view.groove.clamp(0.0, 1.0) as f32,
        bh,
        alpha(tier, a),
    ));
    let dark = [0.02, 0.016, 0.04, 0.8 * a];
    if st.beats.is_empty() {
        // Free movement: the gem breathes.
        let free = rgb(0x9fb8ff);
        let breathe = (0.5 + 0.5 * (st.elapsed as f32 * 3.0).cos()).powi(2);
        let r = (3.5 + 1.5 * breathe) * u;
        disc(
            x,
            y,
            r + 3.0 * u * breathe,
            line,
            alpha(free, 0.25 * a),
            out,
        );
        disc(x, y, r + line, line, dark, out);
        disc(x, y, r, line, alpha(free, a), out);
        return;
    }
    let phase = st.beat_phase.clamp(0.0, 1.0) as f32;
    let ring_color = if st.next_down {
        rgb(0xffe8b0)
    } else {
        rgb(0xd8e4ff)
    };
    ring(
        x,
        y,
        (5.0 + 13.0 * (1.0 - phase)) * u,
        if st.next_down { 1.75 * line } else { line },
        alpha(ring_color, 0.85 * smoothstep(phase / 0.55) * a),
        out,
    );
    let burst = (1.0 - phase * 2.0).max(0.0);
    if burst > 0.0 {
        let b2 = burst * burst;
        let r = (6.0 + 12.0 * (1.0 - b2)) * u;
        diamond(x, y, r * 0.9, r * 0.9, line, alpha(tier, 0.3 * b2 * a), out);
        diamond_edge(x, y, r, r, 2.0 * line, alpha(tier, b2 * a), out);
    }
    let r = (4.5 + 2.0 * hit) * u;
    diamond(x, y, r + line, r + line, line, dark, out);
    let gem = if hit > 0.6 { rgb(0xffffff) } else { tier };
    diamond(x, y, r, r, line, alpha(gem, a), out);
    diamond(
        x,
        y,
        r * 0.45,
        r * 0.45,
        line,
        alpha(rgb(0xffffff), (0.35 + 0.65 * hit) * a),
        out,
    );
}

fn key_face(key: KeyLight, x: f32, y: f32, w: f32, h: f32, out: &mut Vec<Sprite>) {
    let color = rating_color(key.rating);
    let fill = if key.held { color } else { rgb(0x101018) };
    let edge = if key.held { color } else { rgb(0x606878) };
    out.push(rect(x, y, w, h, alpha(edge, 0.9)));
    let b = (w.min(h) * 0.08).max(1.0);
    out.push(rect(
        x + b,
        y + b,
        w - 2.0 * b,
        h - 2.0 * b,
        alpha(fill, if key.held { 0.9 } else { 0.25 }),
    ));
}
