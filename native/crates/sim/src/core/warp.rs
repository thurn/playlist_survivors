//! `src/core/warp.ts`: the time warp w = W(s), the running total of a piecewise-constant rate.

/// A rate change: from song time `s` (world time `w` there), `rate` world seconds per song second.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WarpBreak {
    pub s: f64,
    pub w: f64,
    pub rate: f64,
}

#[derive(Clone, Debug, PartialEq)]
pub struct Warp {
    pub breaks: Vec<WarpBreak>,
}

impl Warp {
    pub fn new(breaks: Vec<WarpBreak>) -> Warp {
        assert!(!breaks.is_empty(), "warp needs at least one breakpoint");
        Warp { breaks }
    }

    /// From (s, rate) steps starting at song time 0, world time 0.
    pub fn from_rates(steps: &[(f64, f64)]) -> Warp {
        let mut sorted = steps.to_vec();
        // (JS's sort is stable; so is sort_by. The comparator is a.s - b.s, NaN read as equal; with
        // non-finite song times its order is inconsistent and may differ from V8's: keep them finite.)
        sorted.sort_by(|a, b| {
            (a.0 - b.0)
                .partial_cmp(&0.0)
                .unwrap_or(std::cmp::Ordering::Equal)
        });
        let mut breaks: Vec<WarpBreak> = Vec::new();
        let mut w = 0.0;
        for &(s, rate) in &sorted {
            if let Some(&prev) = breaks.last() {
                w = prev.w + (s - prev.s) * prev.rate;
                if prev.rate == rate {
                    continue;
                }
                if s == prev.s {
                    let n = breaks.len();
                    breaks[n - 1] = WarpBreak { s, w: prev.w, rate };
                    continue;
                }
            }
            breaks.push(WarpBreak { s, w, rate });
        }
        Warp::new(breaks)
    }

    fn segment_at_song(&self, s: f64) -> usize {
        let b = &self.breaks;
        let mut lo = 0usize;
        let mut hi = b.len() - 1;
        if s < b[0].s {
            return 0;
        }
        while lo < hi {
            let mid = (lo + hi + 1) >> 1;
            if b[mid].s <= s {
                lo = mid;
            } else {
                hi = mid - 1;
            }
        }
        lo
    }

    pub fn to_world(&self, s: f64) -> f64 {
        let b = self.breaks[self.segment_at_song(s)];
        if s < self.breaks[0].s {
            let r = self.first_non_zero_rate();
            return b.w + (s - b.s) * r;
        }
        b.w + (s - b.s) * b.rate
    }

    pub fn rate_at(&self, s: f64) -> f64 {
        self.breaks[self.segment_at_song(s)].rate
    }

    fn first_non_zero_rate(&self) -> f64 {
        for b in &self.breaks {
            if b.rate > 0.0 {
                return b.rate;
            }
        }
        1.0
    }

    /// The inverse warp; inside a hit-stop, the song time at which it began.
    pub fn to_song(&self, w: f64) -> f64 {
        let b = &self.breaks;
        if w <= b[0].w {
            let r = if b[0].rate > 0.0 {
                b[0].rate
            } else {
                self.first_non_zero_rate()
            };
            return b[0].s + (w - b[0].w) / r;
        }
        // Largest k with b[k].w < w.
        let mut lo = 0usize;
        let mut hi = b.len() - 1;
        while lo < hi {
            let mid = (lo + hi + 1) >> 1;
            if b[mid].w < w {
                lo = mid;
            } else {
                hi = mid - 1;
            }
        }
        let seg = b[lo];
        let rate = if seg.rate > 0.0 {
            seg.rate
        } else {
            self.first_non_zero_rate()
        };
        seg.s + (w - seg.w) / rate
    }

    /// Song-time intervals where the rate is zero.
    pub fn hit_stops(&self) -> Vec<(f64, f64)> {
        let mut out = Vec::new();
        for (i, b) in self.breaks.iter().enumerate() {
            if b.rate == 0.0 {
                let end = self.breaks.get(i + 1).map_or(f64::INFINITY, |n| n.s);
                out.push((b.s, end));
            }
        }
        out
    }

    pub fn in_hit_stop(&self, s: f64) -> bool {
        self.rate_at(s) == 0.0
    }
}
