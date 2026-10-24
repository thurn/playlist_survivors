//! The HUD (`src/runtime/hud.ts`, read as a reference): in HUD units of a 960 x 540 screen, in the
//! TS game's 5 x 7 pixel font. Experience and level along the top, health top left, kills and the
//! rated presses top right, the arrow keys bottom left, and the beat track: the committed beats
//! slide toward a beating heart, presses flash on it early or late in their rating's colour, and
//! the groove meter beside it shows the tier.
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
}

/// A small heart, 7 x 6 font pixels.
const HEART: [&str; 6] = [
    ".##.##.", "#######", "#######", ".#####.", "..###..", "...#...",
];

pub fn draw(view: &LiveView, st: &HudState, w: f32, h: f32, out: &mut Vec<Sprite>) {
    let u = (w / 960.0).min(h / 540.0);
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
    for (i, (name, color)) in [("PERFECT", PERFECT), ("GREAT", GREAT), ("GOOD", GOOD)]
        .into_iter()
        .enumerate()
    {
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
    for p in st.presses {
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
        py(cy - 4.0),
        fs,
        Align::Left,
        TIERS[tier],
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
