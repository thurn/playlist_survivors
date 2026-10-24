//! `src/sim/strides.ts`'s stride steps: the steps a held key strides on. The rest of strides.ts
//! (the budget, strides and stomps) comes with the simulation.

/// The steps a held key strides on (song times), the stride period at each, and how many steps its
/// grid step has.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct StrideSteps {
    pub s: Vec<f64>,
    pub period: Vec<f64>,
    pub per: Vec<u8>,
    /// Where the last grid time's steps start in `s`: they are provisional, since the last grid time
    /// keeps the step before it until the next grid time arrives.
    tail: usize,
}

impl StrideSteps {
    /// The steps for `grid`, which extends the grid these steps were built from (`from` is its old
    /// length): the provisional last grid time's steps are rebuilt, then the new ones appended.
    pub fn extend(&mut self, grid: &[f64], from: usize, max_period_s: f64) {
        let first = if from == 0 {
            0
        } else {
            self.s.truncate(self.tail);
            self.period.truncate(self.tail);
            self.per.truncate(self.tail);
            from - 1
        };
        for i in first..grid.len() {
            self.tail = self.s.len();
            let g0 = grid[i];
            let len = if i + 1 < grid.len() {
                grid[i + 1] - g0
            } else if i > 0 {
                g0 - grid[i - 1]
            } else {
                max_period_s
            };
            let mut n: u32 = 1;
            while len / f64::from(n) > max_period_s + 1e-9 {
                n *= 2;
            }
            for k in 0..n {
                self.s.push(g0 + (f64::from(k) * len) / f64::from(n));
                self.period.push(len / f64::from(n));
                // (TS stores it in a Uint8Array: the count wraps at 256.)
                self.per.push((n & 0xff) as u8);
            }
        }
    }
}

/// Every grid time, plus equal subdivisions of each grid step longer than `max_period_s`: the step
/// is halved until it is not. The last grid time keeps the step before it.
pub fn stride_steps(grid: &[f64], max_period_s: f64) -> StrideSteps {
    let mut out = StrideSteps::default();
    out.extend(grid, 0, max_period_s);
    out
}
