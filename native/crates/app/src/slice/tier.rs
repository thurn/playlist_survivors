//! The tier's look and the weapons drawn (native/evidence/vfx/ts-spec.md, "Cleaves and tiers" and
//! "Weapons"; the TS game's `tier-look.ts` and `renderer.ts`): the hero's ground glow and, from
//! tier 1, an aura swelling on the beat; a ring at the pickup radius for a beat after a tier change;
//! the orbiting blades, the fire trail's flames and chain lightning, each in the tier's colours.

use eval::live::{HeroPose, LiveView};
use render::atlas::Atlases;
use sim::content::ContentLibrary;
use sim::content::types::WeaponKind;

use super::fx::{rand01, rgb};
use super::gfx::{Sprite, rect};

/// The orbiting blades: the blade and the glow around it, by tier.
const BLADE_TINTS: [u32; 4] = [0x00ff_ffff, 0x00d8_f4ff, 0x00d8_fff0, 0x00ff_f4c0];
const BLADE_GLOWS: [u32; 4] = [0x0080_c8ff, 0x0050_d8ff, 0x0060_f0c0, 0x00ff_d040];
/// Chain lightning's outer glow (its core stays white).
const BOLT_COLORS: [u32; 4] = [0x008f_b8ff, 0x0070_d8ff, 0x0080_ffd0, 0x00ff_e070];
/// The fire trail's flames, and a glow under them from tier 1.
const FLAME_TINTS: [u32; 4] = [0x00ff_ffff, 0x00ff_e8d0, 0x00e8_fff0, 0x00ff_f4b0];
const FLAME_GLOWS: [u32; 4] = [0, 0x0060_c0ff, 0x0070_f0c0, 0x00ff_d040];
/// The hero's aura: its colour and strength (none at tier 0).
const AURA_TINTS: [u32; 4] = [0, 0x0060_c0ff, 0x0070_f0c0, 0x00ff_d040];
const AURA_ALPHA: [f32; 4] = [0.0, 0.24, 0.34, 0.48];
/// The pickup radius by tier (px), where the tier change's ring shows.
const PICKUP_RADIUS: [f64; 4] = [40.0, 65.0, 95.0, 120.0];
/// A chain lightning fades this long after its last jump (s).
const BOLT_FADE_S: f64 = 0.18;
/// A bar's beats, for the blades' turn.
const BAR_BEATS: f64 = 4.0;

/// The tier as an index into the tables.
pub fn index(view: &LiveView) -> usize {
    (view.tier.max(0.0) as usize).min(3)
}

fn with_alpha(mut c: [f32; 4], a: f32) -> [f32; 4] {
    c[3] = a;
    c
}

/// The hero's ground glow (always, so the hero is easy to find in a crowd: from tier 1 in the
/// tier's colour), and the tier change's ring at the pickup radius, fading over `ring` (1 to 0).
pub fn ground(
    view: &LiveView,
    art: &Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    ring: f64,
    out: &mut Vec<Sprite>,
) {
    let Some(hero) = view.hero else {
        return;
    };
    let tier = index(view);
    let (sx, sy) = at(hero.x, hero.y);
    if let Some(&glow) = art.proc.get("glow") {
        let k = glow.w as f32 * scale;
        let c = if view.run.is_some() {
            let aura = super::atmosphere::aura(view);
            [
                aura.color[0],
                aura.color[1],
                aura.color[2],
                0.30 + 0.04 * aura.count as f32,
            ]
        } else if tier > 0 {
            with_alpha(rgb(AURA_TINTS[tier]), 0.7)
        } else {
            with_alpha(rgb(0x00ff_f0b0), 0.55)
        };
        out.push(Sprite::centred(glow, sx, sy, 1.6 * k, 0.8 * k).tinted(c));
    }
    if view.run.is_none()
        && ring > 0.0
        && let Some(&r) = art.proc.get("ring")
    {
        let d = (2.0 * PICKUP_RADIUS[tier]) as f32 * scale;
        let c = with_alpha(rgb(0x0080_ffe0), (0.6 * ring) as f32);
        out.push(Sprite::centred(r, sx, sy, d, d * 0.7).tinted(c));
    }
}

/// The aura behind the hero from tier 1, swelling with the beat's `pulse` (0-1).
pub fn aura(
    view: &LiveView,
    hero: &HeroPose,
    art: &Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    pulse: f64,
    out: &mut Vec<Sprite>,
) {
    let tier = index(view);
    let build = super::atmosphere::aura(view);
    let strength = if view.run.is_some() {
        build.alpha
    } else {
        AURA_ALPHA[tier]
    };
    let Some(&glow) = art.proc.get("glow").filter(|_| strength > 0.0) else {
        return;
    };
    let p = pulse as f32;
    let (sx, sy) = at(hero.x, hero.y - hero.z - 16.0);
    let k = glow.w as f32 * scale * if view.run.is_some() { build.size } else { 1.0 };
    let color = if view.run.is_some() {
        [build.color[0], build.color[1], build.color[2], 1.0]
    } else {
        rgb(AURA_TINTS[tier])
    };
    let c = with_alpha(color, strength * (0.8 + 0.2 * p));
    out.push(
        Sprite::centred(glow, sx, sy, (1.5 + 0.25 * p) * k, (1.9 + 0.3 * p) * k)
            .tinted(c)
            .additive(),
    );
}

/// Where the hero was (x, y) at host time `t`, from its recent places (oldest first).
#[allow(clippy::many_single_char_names)]
fn hero_at(trail: &[(f64, f64, f64)], t: f64) -> Option<(f64, f64)> {
    let i = trail.partition_point(|p| p.0 <= t);
    match (i.checked_sub(1).map(|k| trail[k]), trail.get(i)) {
        (Some(a), Some(b)) if b.0 > a.0 => {
            let u = (t - a.0) / (b.0 - a.0);
            Some((a.1 + (b.1 - a.1) * u, a.2 + (b.2 - a.2) * u))
        }
        (Some(a), _) => Some((a.1, a.2)),
        (None, Some(b)) => Some((b.1, b.2)),
        (None, None) => None,
    }
}

/// A line from `a` to `b` on screen, `width` px wide, additive.
fn line(a: (f32, f32), b: (f32, f32), width: f32, color: [f32; 4]) -> Sprite {
    let (dx, dy) = (b.0 - a.0, b.1 - a.1);
    let len = dx.hypot(dy);
    let (mx, my) = (f32::midpoint(a.0, b.0), f32::midpoint(a.1, b.1));
    rect(mx - len / 2.0, my - width / 2.0, len, width, color)
        .rotated(dy.atan2(dx))
        .additive()
}

/// What the weapons are drawn from besides the view: the content, the song's beat index, host
/// time, the run time shown, and the hero's recent places (host s, x, y; oldest first).
pub struct Arms<'a> {
    pub lib: &'a ContentLibrary,
    pub beat: f64,
    pub now: f64,
    pub run_s: f64,
    pub trail: &'a [(f64, f64, f64)],
    pub trail_now: f64,
}

/// The weapons the hero holds, drawn over the characters: the orbiting blades (a turn of the orbit
/// a bar, as the sim places their hits), the fire trail's flames at the hero's past places, and
/// each chain lightning's jumps so far, fading after its last.
#[allow(clippy::many_single_char_names, clippy::too_many_lines)]
pub fn weapons(
    view: &LiveView,
    arms: &Arms,
    art: &Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    out: &mut Vec<Sprite>,
) {
    let Some(hero) = view.hero else {
        return;
    };
    let tier = index(view);
    let glow = art.proc.get("glow").copied();
    for (id, evolved) in &view.weapons {
        let Some(wd) = arms.lib.weapons.get(id.as_str()) else {
            continue;
        };
        match wd.kind {
            WeaponKind::Orbit => {
                let (Some(o), Some(&blade)) = (wd.orbit.as_ref(), art.proc.get("blade")) else {
                    continue;
                };
                let th0 = std::f64::consts::TAU * arms.beat / (o.period_bars * BAR_BEATS);
                let orbit =
                    sim::upgrades::evolved_orbit(wd, sim::tiers::orbit_at(wd, view.tier), *evolved);
                let n = orbit.count.max(0.0) as usize;
                for k in 0..n {
                    let th = th0 + k as f64 * std::f64::consts::TAU / n as f64;
                    let x = hero.x + th.cos() * orbit.radius;
                    let y = hero.y + th.sin() * orbit.radius * 0.8 - 18.0;
                    let (sx, sy) = at(x, y);
                    if let Some(g) = glow {
                        let d = g.w as f32 * (0.8 + 0.15 * tier as f32) * scale;
                        let c = with_alpha(rgb(BLADE_GLOWS[tier]), 0.6 + 0.05 * tier as f32);
                        out.push(Sprite::centred(g, sx, sy, d, d).tinted(c).additive());
                    }
                    let (w, h) = (blade.w as f32 * scale, blade.h as f32 * scale);
                    out.push(
                        Sprite::centred(blade, sx, sy, w, h)
                            .rotated((3.0 * th) as f32)
                            .tinted(rgb(BLADE_TINTS[tier])),
                    );
                }
            }
            WeaponKind::Trail => {
                let Some(fire) = art.effects.get("fire").filter(|f| !f.frames.is_empty()) else {
                    continue;
                };
                let (duration, radius) = if view.inventory.is_some() {
                    let Some(shot) = view.fire_trail else {
                        continue;
                    };
                    (shot.duration_s, shot.radius)
                } else {
                    (0.76, 20.0)
                };
                if duration <= 0.12 {
                    continue;
                }
                let count = if view.inventory.is_some() {
                    (duration / 0.07).ceil().clamp(10.0, 32.0) as u32
                } else {
                    10
                };
                for k in 0..count {
                    let t = if view.inventory.is_some() {
                        arms.trail_now
                            - 0.12
                            - f64::from(k) * (duration - 0.12) / f64::from(count - 1)
                    } else {
                        arms.now - 0.06 - f64::from(k) * 0.07
                    };
                    let Some((x, y)) = hero_at(arms.trail, t) else {
                        continue;
                    };
                    let x = x + (rand01(u64::from(k), (t * 14.0).floor() as u64, 7) - 0.5) * 8.0;
                    let a = 1.0 - f64::from(k) / f64::from(count);
                    let frame =
                        ((arms.now * 12.0 + f64::from(k) * 3.0) as usize) % fire.frames.len();
                    let (sx, sy) = at(x, y);
                    out.push(
                        Sprite::anchored(
                            fire.frames[frame],
                            sx,
                            sy,
                            (fire.anchor_x, fire.anchor_y),
                            0.8 * (radius / 20.0).clamp(0.5, 3.0) as f32 * scale,
                        )
                        .tinted(with_alpha(rgb(FLAME_TINTS[tier]), a as f32))
                        .additive(),
                    );
                    if let Some(g) = glow.filter(|_| tier > 0) {
                        let (gx, gy) = at(x, y - 8.0);
                        let d = g.w as f32 * 0.7 * scale;
                        let c = with_alpha(rgb(FLAME_GLOWS[tier]), (0.35 * a) as f32);
                        out.push(Sprite::centred(g, gx, gy, d, d).tinted(c).additive());
                    }
                }
            }
            WeaponKind::Chain | WeaponKind::Blast => {}
        }
    }
    // Chain lightning: jittered segments in the tier's colour, a white core along the jumps.
    for (&index, points) in &view.bolts {
        let Some(&(last, ..)) = points.last() else {
            continue;
        };
        if arms.run_s > last + BOLT_FADE_S {
            continue;
        }
        let shown: Vec<(f32, f32)> = points
            .iter()
            .filter(|p| p.0 <= arms.run_s + 1e-6)
            .map(|p| at(p.1, p.2))
            .collect();
        if shown.len() < 2 {
            continue;
        }
        let a = if arms.run_s > last {
            1.0 - (arms.run_s - last) / BOLT_FADE_S
        } else {
            1.0
        } as f32;
        let outer = with_alpha(rgb(BOLT_COLORS[tier]), (0.5 + 0.08 * tier as f32) * a);
        let width = (3.0 + tier as f32) * scale;
        bolt(&shown, index as u64, arms.now, width, outer, a, scale, out);
    }
}

/// Shared chain/sky bolt tessellation: jittered outer segments and a white core.
#[allow(clippy::too_many_arguments)]
pub(super) fn bolt(
    shown: &[(f32, f32)],
    seed: u64,
    now: f64,
    width: f32,
    outer: [f32; 4],
    a: f32,
    scale: f32,
    out: &mut Vec<Sprite>,
) {
    let flick = (now * 30.0).floor() as u64;
    for (k, pair) in shown.windows(2).enumerate() {
        let (p0, p1) = (pair[0], pair[1]);
        let mut prev = p0;
        for j in 1..=6u64 {
            let u = j as f32 / 6.0;
            let jit = if j == 6 {
                0.0
            } else {
                (rand01(seed * 64 + k as u64, j, flick) as f32 - 0.5) * 12.0 * scale
            };
            let next = (
                p0.0 + (p1.0 - p0.0) * u + jit,
                p0.1 + (p1.1 - p0.1) * u + jit * 0.6,
            );
            out.push(line(prev, next, width, outer));
            prev = next;
        }
        out.push(line(
            p0,
            p1,
            scale.max(1.0),
            with_alpha(rgb(0x00ff_ffff), a),
        ));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_trail_finds_the_hero_between_its_samples() {
        let trail = [(0.0, 0.0, 0.0), (1.0, 10.0, 20.0)];
        assert_eq!(hero_at(&trail, 0.5), Some((5.0, 10.0)));
        assert_eq!(hero_at(&trail, 2.0), Some((10.0, 20.0)));
        assert_eq!(hero_at(&trail, -1.0), Some((0.0, 0.0)));
        assert_eq!(hero_at(&[], 0.0), None);
    }

    #[test]
    fn a_line_spans_its_ends() {
        let s = line((0.0, 0.0), (0.0, 10.0), 2.0, [1.0; 4]);
        assert!((s.w - 10.0).abs() < 1e-5 && (s.h - 2.0).abs() < 1e-5);
        assert!((s.x + s.w / 2.0).abs() < 1e-5 && (s.y + s.h / 2.0 - 5.0).abs() < 1e-5);
        assert!((s.rot - std::f32::consts::FRAC_PI_2).abs() < 1e-5);
    }
}
