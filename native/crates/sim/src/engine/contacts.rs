//! Ephemeral exact-time broad phase. Never part of rollback state or iteration authority.
use super::Sim;
use crate::core::geometry::{Shape, shape_bounds};
const CELL: f64 = 64.0;
#[derive(Clone, Copy)]
pub(super) struct Contact {
    pub fi: usize,
    pub x: f64,
    pub y: f64,
    pub r: f64,
    order: usize,
}
/// The points' cells as one dense grid over their bounds: `ids[starts[c]..starts[c + 1]]` are
/// cell `c`'s points (`c = (y - lo.1) * cols + x - lo.0`), in order. Built twice a tick over every
/// live foe, so it allocates three vectors and hashes nothing.
pub(super) struct Contacts {
    points: Vec<Contact>,
    lo: (i64, i64),
    cols: i64,
    rows: i64,
    starts: Vec<usize>,
    ids: Vec<usize>,
    margin: f64,
}
impl Contacts {
    pub fn new(sim: &Sim) -> Self {
        let mut points = Vec::with_capacity(sim.st.alive.len());
        let mut margin = 0.0f64;
        for &fi in &sim.st.alive {
            if !sim.hittable(fi) {
                continue;
            }
            let (x, y) = sim.pos(fi, sim.st.w);
            let r = sim.st.foes[fi].r;
            let order = points.len();
            points.push(Contact { fi, x, y, r, order });
            margin = margin.max(r * 0.5);
        }
        Self::index(points, margin)
    }
    fn index(points: Vec<Contact>, margin: f64) -> Self {
        let cell = |p: &Contact| ((p.x / CELL).floor() as i64, (p.y / CELL).floor() as i64);
        let (mut lo, mut hi) = ((i64::MAX, i64::MAX), (i64::MIN, i64::MIN));
        for c in points.iter().map(cell) {
            lo = (lo.0.min(c.0), lo.1.min(c.1));
            hi = (hi.0.max(c.0), hi.1.max(c.1));
        }
        // (A far straggler would make the grid huge: then every query scans the points. In f64:
        // with no points, or cells near i64's ends, the spans overflow.)
        let span = |a: i64, b: i64| b as f64 - a as f64 + 1.0;
        let dense = !points.is_empty()
            && span(lo.0, hi.0) * span(lo.1, hi.1) <= 4.0 * points.len() as f64 + 64.0;
        let (cols, rows) = if dense {
            (hi.0 - lo.0 + 1, hi.1 - lo.1 + 1)
        } else {
            (0, 0)
        };
        let (mut starts, mut ids) = (Vec::new(), Vec::new());
        if dense {
            let at = |p: &Contact| {
                let c = cell(p);
                ((c.1 - lo.1) * cols + c.0 - lo.0) as usize
            };
            starts = vec![0; (cols * rows) as usize + 1];
            for p in &points {
                starts[at(p) + 1] += 1;
            }
            for i in 1..starts.len() {
                starts[i] += starts[i - 1];
            }
            let mut next = starts.clone();
            ids = vec![0; points.len()];
            for p in &points {
                let c = at(p);
                ids[next[c]] = p.order;
                next[c] += 1;
            }
        }
        Self {
            points,
            lo,
            cols,
            rows,
            starts,
            ids,
            margin,
        }
    }
    pub fn candidates(&self, shape: &Shape) -> Vec<Contact> {
        let mut b = shape_bounds(shape);
        let pad = 1e-9 + 16.0 * f64::EPSILON * b.iter().map(|x| x.abs()).fold(1.0, f64::max);
        for i in 0..2 {
            b[i] -= self.margin + pad;
            b[i + 2] += self.margin + pad;
        }
        let near = |p: &&Contact| p.x >= b[0] && p.x <= b[2] && p.y >= b[1] && p.y <= b[3];
        // The query's cells, clipped to the grid (in f64: a long sweep's bounds can be huge).
        let span = |v: f64, lo: i64, n: i64| ((v / CELL).floor() - lo as f64).clamp(-1.0, n as f64);
        let (x0, x1) = (
            span(b[0], self.lo.0, self.cols),
            span(b[2], self.lo.0, self.cols),
        );
        let (y0, y1) = (
            span(b[1], self.lo.1, self.rows),
            span(b[3], self.lo.1, self.rows),
        );
        let (x0, y0) = (x0.max(0.0) as i64, y0.max(0.0) as i64);
        let (x1, y1) = (
            x1.min(self.cols as f64 - 1.0) as i64,
            y1.min(self.rows as f64 - 1.0) as i64,
        );
        let mut found =
            if self.cols == 0 || (x1 - x0 + 1) * (y1 - y0 + 1) > self.points.len() as i64 {
                self.points.iter().filter(near).copied().collect::<Vec<_>>()
            } else if x0 > x1 || y0 > y1 {
                Vec::new()
            } else {
                let mut v = Vec::new();
                for y in y0..=y1 {
                    let row = (y * self.cols) as usize;
                    let cells = &self.ids
                        [self.starts[row + x0 as usize]..self.starts[row + x1 as usize + 1]];
                    v.extend(cells.iter().map(|&i| &self.points[i]).filter(near).copied());
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
    fn empty_and_straggling_contacts_answer_every_query() {
        let shape = Shape::Capsule {
            x0: -1e6,
            y0: -1e6,
            x1: 1e6,
            y1: 1e6,
            radius: 5.0,
        };
        assert!(
            Contacts::index(Vec::new(), 0.0)
                .candidates(&shape)
                .is_empty()
        );
        let points = [(0.0, 0.0), (1e300, -1e300)]
            .into_iter()
            .enumerate()
            .map(|(order, (x, y))| Contact {
                fi: order,
                x,
                y,
                r: 1.0,
                order,
            })
            .collect();
        let found = Contacts::index(points, 0.5).candidates(&shape);
        assert_eq!(found.iter().map(|p| p.fi).collect::<Vec<_>>(), [0]);
    }
    #[test]
    fn indexed_candidates_preserve_source_contact_order_at_boundaries_and_long_sweeps() {
        let (mut points, mut margin) = (Vec::new(), 0.0f64);
        for i in 0..400 {
            let x = (i % 20) as f64 * 31.999_999_999 - 320.0;
            let y = (i / 20) as f64 * 32.0 - 320.0;
            let r = (i % 7) as f64 * 3.0;
            let order = points.len();
            points.push(Contact {
                fi: 800 - i,
                x,
                y,
                r,
                order,
            });
            margin = margin.max(r * 0.5);
        }
        let c = Contacts::index(points, margin);
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
