//! The procedural textures the effects draw with (`src/runtime/assets.ts`'s `PROC`, read as a
//! reference): soft glows and lights, a ring, crescent arcs, a streak, sparks and the like, drawn
//! white (tinted when used) into one atlas page at load.
#![allow(clippy::many_single_char_names)]

use std::collections::HashMap;

use crate::atlas::{Page, Region};

/// A texture's pixels, white with alpha (premultiplied later by the tint).
struct Img {
    w: u32,
    h: u32,
    a: Vec<f32>,
}

impl Img {
    fn new(w: u32, h: u32, f: impl Fn(f32, f32) -> f32) -> Img {
        let mut a = Vec::with_capacity((w * h) as usize);
        for y in 0..h {
            for x in 0..w {
                a.push(f(x as f32 + 0.5, y as f32 + 0.5).clamp(0.0, 1.0));
            }
        }
        Img { w, h, a }
    }
}

/// A `w` x `h` image filled in rectangles (x, y, w, h).
fn pixels(w: u32, h: u32, rects: &'static [(u32, u32, u32, u32)]) -> Img {
    Img::new(w, h, move |x, y| {
        let inside = rects.iter().any(|&(rx, ry, rw, rh)| {
            x >= rx as f32 && x < (rx + rw) as f32 && y >= ry as f32 && y < (ry + rh) as f32
        });
        f32::from(u8::from(inside))
    })
}

/// A radial gradient through (radius fraction, alpha) stops, linearly between them.
fn radial(w: u32, h: u32, stops: &[(f32, f32)]) -> Img {
    let (cx, cy) = (w as f32 / 2.0, h as f32 / 2.0);
    Img::new(w, h, |x, y| {
        let d = (((x - cx) / cx).powi(2) + ((y - cy) / cy).powi(2)).sqrt();
        let mut prev = stops[0];
        for &s in stops {
            if d <= s.0 {
                let t = if s.0 > prev.0 {
                    (d - prev.0) / (s.0 - prev.0)
                } else {
                    0.0
                };
                return prev.1 + (s.1 - prev.1) * t;
            }
            prev = s;
        }
        0.0
    })
}

/// A crescent of `degrees`, pointing right: a bright leading edge thinning toward the tail.
fn arc(size: u32, degrees: f32) -> Img {
    let c = size as f32 / 2.0;
    let r = c - 2.0;
    let half = degrees.to_radians() / 2.0;
    Img::new(size, size, |x, y| {
        let (dx, dy) = (x - c, y - c);
        let d = (dx * dx + dy * dy).sqrt();
        let a = dy.atan2(dx);
        if a.abs() > half {
            return 0.0;
        }
        // (0 at the tail, 1 at the leading edge.)
        let t = (a + half) / (2.0 * half);
        let thick = r * (0.08 + 0.22 * (std::f32::consts::PI * t).sin());
        if d <= r && d >= r - thick {
            0.25 + 0.75 * t
        } else {
            0.0
        }
    })
}

/// Every procedural texture by name.
fn textures() -> Vec<(&'static str, Img)> {
    let cross = |w: u32, bar: u32| {
        Img::new(w, w, move |x, y| {
            let (lo, hi) = ((w - bar) as f32 / 2.0, (w + bar) as f32 / 2.0);
            f32::from(u8::from((x > lo && x < hi) || (y > lo && y < hi)))
        })
    };
    vec![
        (
            "glow",
            radial(32, 32, &[(0.0, 1.0), (0.35, 0.55), (1.0, 0.0)]),
        ),
        (
            "light",
            radial(64, 64, &[(0.0, 1.0), (0.5, 0.4), (1.0, 0.0)]),
        ),
        (
            "dust",
            radial(12, 12, &[(0.0, 0.8), (0.6, 0.35), (1.0, 0.0)]),
        ),
        ("ring", {
            let c = 64.0;
            Img::new(128, 128, move |x, y| {
                let d = ((x - c).powi(2) + (y - c).powi(2)).sqrt();
                (2.5 - (d - (c - 3.0)).abs()).clamp(0.0, 1.0)
            })
        }),
        ("arc120", arc(128, 120.0)),
        ("arc150", arc(160, 150.0)),
        ("arc200", arc(200, 200.0)),
        ("streak", {
            Img::new(64, 16, |x, y| {
                let e = ((x - 32.0) / 32.0).powi(2) + ((y - 8.0) / 6.0).powi(2);
                if e <= 1.0 { x / 64.0 } else { 0.0 }
            })
        }),
        // The gems (`assets.ts`): each body, and its white glint drawn over it.
        ("gem", pixels(5, 7, &[(1, 0, 3, 7), (0, 2, 5, 3)])),
        ("gem-glint", pixels(5, 7, &[(1, 1, 1, 2)])),
        (
            "gem-big",
            pixels(7, 10, &[(2, 0, 3, 10), (0, 3, 7, 4), (1, 1, 5, 8)]),
        ),
        ("gem-big-glint", pixels(7, 10, &[(2, 2, 1, 3)])),
        ("spark", cross(6, 2)),
        ("shard", cross(4, 2)),
        ("ember", cross(3, 1)),
        ("cross", cross(16, 2)),
        ("wisp", {
            Img::new(12, 20, |x, y| {
                let e = ((x - 6.0) / 6.0).powi(2) + ((y - 12.0) / 9.0).powi(2);
                (0.95 * (1.0 - e.sqrt())).max(0.0)
            })
        }),
        ("blade", {
            // A short blade pointing up: a tapered bar with a bright edge.
            Img::new(8, 20, |x, y| {
                let half = 3.0 * (1.0 - (20.0 - y) / 22.0).max(0.0) + 0.5;
                if (x - 4.0).abs() <= half { 1.0 } else { 0.0 }
            })
        }),
        ("shadow", {
            Img::new(24, 10, |x, y| {
                let e = ((x - 12.0) / 12.0).powi(2) + ((y - 5.0) / 5.0).powi(2);
                if e <= 1.0 { 0.38 } else { 0.0 }
            })
        }),
        (
            "scorch",
            radial(40, 26, &[(0.0, 0.85), (0.7, 0.5), (1.0, 0.0)]),
        ),
    ]
}

/// The procedural textures packed into one page (index `page`), and where each lies.
pub fn page(page: u32) -> (Page, HashMap<String, Region>) {
    let size = 512u32;
    let mut rgba = vec![0u8; (size * size * 4) as usize];
    let mut regions = HashMap::new();
    let (mut x, mut y, mut row) = (0u32, 0u32, 0u32);
    for (name, img) in textures() {
        if x + img.w > size {
            x = 0;
            y += row + 1;
            row = 0;
        }
        for j in 0..img.h {
            for i in 0..img.w {
                let a = img.a[(j * img.w + i) as usize];
                let o = (((y + j) * size + x + i) * 4) as usize;
                // (White, its alpha in the alpha channel: the sprite's tint colours it.)
                rgba[o..o + 4].copy_from_slice(&[255, 255, 255, (a * 255.0).round() as u8]);
            }
        }
        regions.insert(
            (*name).to_string(),
            Region {
                page,
                x,
                y,
                w: img.w,
                h: img.h,
            },
        );
        x += img.w + 1;
        row = row.max(img.h);
    }
    (
        Page {
            name: "proc".into(),
            width: size,
            height: size,
            rgba,
        },
        regions,
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_texture_fits_the_page_without_overlap() {
        let (page, regions) = page(3);
        assert_eq!(page.rgba.len(), 512 * 512 * 4);
        let rs: Vec<&Region> = regions.values().collect();
        for (k, a) in rs.iter().enumerate() {
            assert!(a.x + a.w <= 512 && a.y + a.h <= 512);
            for b in &rs[k + 1..] {
                let apart =
                    a.x + a.w <= b.x || b.x + b.w <= a.x || a.y + a.h <= b.y || b.y + b.h <= a.y;
                assert!(apart, "{a:?} and {b:?} overlap");
            }
        }
        // The glow is opaque at its centre and clear at its corner.
        let g = regions["glow"];
        let at = |x: u32, y: u32| page.rgba[(((g.y + y) * 512 + g.x + x) * 4 + 3) as usize];
        assert!(at(16, 16) > 240 && at(0, 0) == 0);
    }
}
