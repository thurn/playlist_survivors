//! The gem field (native/evidence/vfx/ts-spec.md, "Gems"; the TS game's `evaluate.ts` and
//! `renderer.ts`): each gem bursts out of the enemy that dropped it and hangs there; one waiting on
//! the field rests until the hero's pickup radius reaches it, then flies in along an arc (a blast's
//! gems fly in on their own); the vacuums wait on the field, pulsing on the beat.

use eval::live::{Gem, LiveView};
use render::atlas::Atlases;

use super::fx::{rand01, rgb};
use super::gfx::Sprite;

fn smoothstep(x: f64) -> f64 {
    let x = x.clamp(0.0, 1.0);
    x * x * (3.0 - 2.0 * x)
}

/// Where gem `index` is at run time `s`, the hero at (hx, hy): bursting out over 0.25 s, then
/// hanging, then flying to the hero along an arc.
#[allow(clippy::many_single_char_names)]
pub fn place(index: usize, g: &Gem, s: f64, hx: f64, hy: f64) -> (f64, f64) {
    let ox = (rand01(index as u64, 1, 0x6e3) - 0.5) * 30.0;
    let oy = (rand01(index as u64, 2, 0x6e3) - 0.5) * 20.0 - 6.0;
    let pop = smoothstep((s - g.s0) / 0.25);
    let fly = match g.fly {
        // (Waiting on the field until it takes off.)
        Some(t) if s > t => smoothstep((s - t) / (g.s1 - t).max(0.05)),
        Some(_) => 0.0,
        None if !g.s1.is_finite() => 0.0,
        None => smoothstep(((s - g.s0) / (g.s1 - g.s0).max(0.05) - 0.55) / 0.45),
    };
    let bx = g.x0 + ox * pop;
    let by = g.y0 + oy * pop - 4.0 * (std::f64::consts::PI * pop).sin();
    (
        bx + (hx - bx) * fly,
        by + (hy - 16.0 - by) * fly - 30.0 * (std::f64::consts::PI * fly).sin(),
    )
}

/// The vacuums waiting (a glow and a big gem bobbing with the beat's `pulse`), and the gems.
#[allow(clippy::too_many_arguments)]
pub fn draw(
    view: &LiveView,
    art: &Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    s: f64,
    pulse: f64,
    out: &mut Vec<Sprite>,
) {
    let Some(hero) = view.hero else {
        return;
    };
    let sprite = |name: &str, x: f64, y: f64, k: f32, tint: [f32; 4], out: &mut Vec<Sprite>| {
        if let Some(&r) = art.proc.get(name) {
            let (sx, sy) = at(x, y);
            out.push(
                Sprite::centred(r, sx, sy, r.w as f32 * k * scale, r.h as f32 * k * scale)
                    .tinted(tint),
            );
        }
    };
    let p = pulse as f32;
    for &(x, y) in view.vacuums.values() {
        if let Some(&g) = art.proc.get("glow") {
            let (sx, sy) = at(x, y - 10.0);
            let d = g.w as f32 * (1.2 + 0.3 * p) * scale;
            let mut c = rgb(0x0080_ffe0);
            c[3] = 0.7;
            out.push(Sprite::centred(g, sx, sy, d, d).tinted(c).additive());
        }
        let y = y - 10.0 - 3.0 * pulse;
        sprite("gem-big", x, y, 1.6, rgb(0x00c0_fff0), out);
        sprite("gem-big-glint", x, y, 1.6, [1.0; 4], out);
    }
    if view.run.is_some() {
        draw_survivors(view, art, at, scale, s, p, out);
        return;
    }
    for (&index, g) in &view.gems {
        if g.s0 > s {
            continue;
        }
        let (x, y) = place(index, g, s, hero.x, hero.y);
        if g.big {
            sprite("gem-big", x, y, 1.0, rgb(0x00c0_8cff), out);
            sprite("gem-big-glint", x, y, 1.0, [1.0; 4], out);
        } else {
            sprite("gem", x, y, 1.0, rgb(0x007f_ffd0), out);
            sprite("gem-glint", x, y, 1.0, [1.0; 4], out);
        }
    }
}

fn look(value: f64) -> (&'static str, &'static str, f32, [f32; 4]) {
    use sim::survivors::{GemColor, gem_color};
    match gem_color(value) {
        GemColor::Blue => (
            "gem",
            "gem-glint",
            1.1 + 0.1 * (value - 1.0).clamp(0.0, 1.0) as f32,
            rgb(0x0030_aaff),
        ),
        GemColor::Green => (
            "gem",
            "gem-glint",
            1.3 + 0.05 * (value - 3.0).clamp(0.0, 6.0) as f32,
            rgb(0x0040_ff70),
        ),
        GemColor::Red => (
            "gem-big",
            "gem-big-glint",
            1.35 + 0.1 * (value / 10.0).ln_1p().min(5.0) as f32,
            rgb(0x00ff_4050),
        ),
    }
}
fn survivor_place(index: usize, g: &Gem, view: &LiveView, s: f64, hero: (f64, f64)) -> (f64, f64) {
    if let Some(p) = view.gem_poses.get(&index) {
        return (p.x, p.y);
    }
    if g.s1.is_finite() {
        return place(index, g, s, hero.0, hero.1);
    }
    let pop = smoothstep((s - g.s0) / 0.25);
    (
        g.x0 + (rand01(index as u64, 1, 0x6e3) - 0.5) * 4.0 * pop,
        g.y0 - 2.0 - 3.0 * (std::f64::consts::PI * pop).sin(),
    )
}
#[allow(clippy::too_many_arguments)]
fn draw_survivors(
    view: &LiveView,
    art: &Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    s: f64,
    pulse: f32,
    out: &mut Vec<Sprite>,
) {
    let Some(hero) = view.hero else {
        return;
    };
    let mut items: Vec<_> = view
        .gems
        .iter()
        .filter(|(_, g)| g.s0 <= s)
        .map(|(&id, g)| {
            let (x, y) = survivor_place(id, g, view, s, (hero.x, hero.y));
            (id, g, x, y)
        })
        .collect();
    let mut cells = std::collections::BTreeMap::new();
    for &(_, _, x, y) in &items {
        *cells
            .entry(((x / 24.0).floor() as i32, (y / 24.0).floor() as i32))
            .or_insert(0usize) += 1;
    }
    // Valuable cap-merged gems stay on top; local glow energy stays bounded in piles.
    items.sort_by(|a, b| a.1.value.total_cmp(&b.1.value).then(a.0.cmp(&b.0)));
    for (_, g, x, y) in items {
        let (body, glint, k, tint) = look(g.value);
        let (sx, sy) = at(x, y);
        if let (Some(glow), Some(r)) = (art.proc.get("glow"), art.proc.get(body)) {
            let n = cells[&((x / 24.0).floor() as i32, (y / 24.0).floor() as i32)] as f32;
            let d = r.h as f32 * k * 2.1 * scale;
            out.push(
                Sprite::centred(*glow, sx, sy, d, d)
                    .tinted([tint[0], tint[1], tint[2], (0.12 + 0.06 * pulse) / n])
                    .additive(),
            );
        }
        for (name, color) in [(body, tint), (glint, [1.0; 4])] {
            if let Some(r) = art.proc.get(name) {
                out.push(
                    Sprite::centred(*r, sx, sy, r.w as f32 * k * scale, r.h as f32 * k * scale)
                        .tinted(color),
                );
            }
        }
    }
    for &(x, y) in view.chickens.values() {
        let (sx, sy) = at(x, y);
        if let Some(shadow) = art.proc.get("shadow") {
            out.push(Sprite::centred(
                *shadow,
                sx,
                sy + 3.0 * scale,
                24.0 * scale,
                10.0 * scale,
            ));
        }
        let y = sy - (5.0 + pulse) * scale;
        for (name, tint) in [
            ("chicken-body", [0.84, 0.40, 0.15, 1.0]),
            ("chicken-bone", [1.0, 0.90, 0.70, 1.0]),
            ("chicken-glint", [1.0, 0.70, 0.32, 1.0]),
        ] {
            if let Some(r) = art.proc.get(name) {
                out.push(Sprite::centred(*r, sx, y, 18.0 * scale, 15.0 * scale).tinted(tint));
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn gem(fly: Option<f64>, s1: f64) -> Gem {
        Gem {
            value: 1.0,
            s0: 1.0,
            x0: 100.0,
            y0: 50.0,
            fly,
            s1,
            big: false,
        }
    }

    #[test]
    fn survivor_value_bands_scale_and_cap_the_merged_gem_size() {
        assert_eq!(look(1.0).3, look(2.0).3);
        assert_ne!(look(2.0).3, look(3.0).3);
        assert_eq!(look(3.0).3, look(9.0).3);
        assert_ne!(look(9.0).3, look(10.0).3);
        assert!(look(1.0).2 < look(2.0).2);
        assert!(look(3.0).2 < look(9.0).2);
        assert_eq!(look(10.0).0, "gem-big");
        assert!(look(100.0).2 < look(1000.0).2);
        assert_eq!(look(1e12).2, look(1e15).2);
    }

    #[test]
    fn survivor_gem_uses_recorded_physical_position_without_an_invented_arrival() {
        let g = gem(Some(1.1), f64::INFINITY);
        let mut view = LiveView::default();
        view.gem_poses.insert(
            3,
            sim::survivors::GemPose {
                id: 3,
                w: 0.25,
                x: 121.0,
                y: 40.0,
            },
        );
        assert_eq!(
            survivor_place(3, &g, &view, 1.25, (0.0, 0.0)),
            (121.0, 40.0)
        );
        assert_eq!(
            survivor_place(3, &g, &view, 9.0, (100.0, 100.0)),
            (121.0, 40.0)
        );
    }

    #[test]
    fn a_gem_waits_on_the_field_until_it_takes_off_then_reaches_the_hero() {
        let waiting = gem(None, f64::INFINITY);
        let (x, y) = place(3, &waiting, 9.0, 0.0, 0.0);
        assert!((x - 100.0).abs() <= 15.0 && (y - 50.0).abs() <= 16.0);
        let flying = gem(Some(9.0), 9.5);
        assert_eq!(place(3, &flying, 9.0, 0.0, 0.0), (x, y));
        let (x, y) = place(3, &flying, 9.5, 0.0, 0.0);
        assert!(x.abs() < 1e-9 && (y + 16.0).abs() < 1e-9);
    }

    #[test]
    fn a_blasts_gem_hangs_then_flies_in() {
        let g = gem(None, 3.0);
        let hang = place(5, &g, 1.5, 0.0, 0.0);
        assert!(hang.0 > 50.0);
        let (x, y) = place(5, &g, 3.0, 0.0, 0.0);
        assert!(x.abs() < 1e-9 && (y + 16.0).abs() < 1e-9);
    }
}
