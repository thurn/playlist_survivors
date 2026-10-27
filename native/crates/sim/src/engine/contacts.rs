//! Ephemeral exact-time broad phase. Never part of rollback state or iteration authority.
use super::Sim;
use crate::core::geometry::{Shape, shape_bounds};
use std::collections::HashMap;
const CELL: f64 = 64.0;
#[derive(Clone, Copy)]
pub(super) struct Contact {
    pub fi: usize,
    pub x: f64,
    pub y: f64,
    pub r: f64,
    order: usize,
}
pub(super) struct Contacts {
    points: Vec<Contact>,
    cells: HashMap<(i64, i64), Vec<usize>>,
    margin: f64,
}
impl Contacts {
    pub fn new(sim: &Sim) -> Self {
        let mut out = Self {
            points: Vec::with_capacity(sim.st.alive.len()),
            cells: HashMap::new(),
            margin: 0.0,
        };
        for &fi in &sim.st.alive {
            if !sim.hittable(fi) {
                continue;
            }
            let (x, y) = sim.pos(fi, sim.st.w);
            let r = sim.st.foes[fi].r;
            let order = out.points.len();
            out.points.push(Contact { fi, x, y, r, order });
            out.cells
                .entry(((x / CELL).floor() as i64, (y / CELL).floor() as i64))
                .or_default()
                .push(order);
            out.margin = out.margin.max(r * 0.5);
        }
        out
    }
    pub fn candidates(&self, shape: &Shape) -> Vec<Contact> {
        let mut b = shape_bounds(shape);
        let pad = 1e-9 + 16.0 * f64::EPSILON * b.iter().map(|x| x.abs()).fold(1.0, f64::max);
        for i in 0..2 {
            b[i] -= self.margin + pad;
            b[i + 2] += self.margin + pad;
        }
        let lo = ((b[0] / CELL).floor() as i64, (b[1] / CELL).floor() as i64);
        let hi = ((b[2] / CELL).floor() as i64, (b[3] / CELL).floor() as i64);
        let near = |p: &&Contact| p.x >= b[0] && p.x <= b[2] && p.y >= b[1] && p.y <= b[3];
        let mut found = if (hi.0 as f64 - lo.0 as f64 + 1.0) * (hi.1 as f64 - lo.1 as f64 + 1.0)
            > self.cells.len() as f64 * 4.0
        {
            self.points.iter().filter(near).copied().collect::<Vec<_>>()
        } else {
            let mut v = Vec::new();
            for y in lo.1..=hi.1 {
                for x in lo.0..=hi.0 {
                    if let Some(ids) = self.cells.get(&(x, y)) {
                        v.extend(ids.iter().map(|&i| &self.points[i]).filter(near).copied());
                    }
                }
            }
            v
        };
        found.sort_unstable_by_key(|p| p.order);
        found
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn indexed_candidates_preserve_source_contact_order_at_boundaries_and_long_sweeps() {
        let mut c = Contacts {
            points: Vec::new(),
            cells: HashMap::new(),
            margin: 0.0,
        };
        for i in 0..400 {
            let x = (i % 20) as f64 * 31.999_999_999 - 320.0;
            let y = (i / 20) as f64 * 32.0 - 320.0;
            let r = (i % 7) as f64 * 3.0;
            let order = c.points.len();
            c.points.push(Contact {
                fi: 800 - i,
                x,
                y,
                r,
                order,
            });
            c.cells
                .entry(((x / CELL).floor() as i64, (y / CELL).floor() as i64))
                .or_default()
                .push(order);
            c.margin = c.margin.max(r * 0.5);
        }
        for i in 0..2000 {
            let x = (i % 37) as f64 * 19.0 - 350.0;
            let y = (i % 53) as f64 * 13.0 - 350.0;
            let d = if i % 3 == 0 {
                0.0
            } else if i % 5 == 0 {
                10000.0
            } else {
                27.5
            };
            let shape = Shape::Capsule {
                x0: x,
                y0: y,
                x1: x + d,
                y1: y - d,
                radius: (i % 19) as f64,
            };
            let reference = c
                .points
                .iter()
                .filter(|p| crate::aim::catches(&shape, p.x, p.y, p.r))
                .map(|p| p.fi)
                .collect::<Vec<_>>();
            let indexed = c
                .candidates(&shape)
                .iter()
                .filter(|p| crate::aim::catches(&shape, p.x, p.y, p.r))
                .map(|p| p.fi)
                .collect::<Vec<_>>();
            assert_eq!(indexed, reference, "source capsule contact/order case{i}");
        }
    }
}
