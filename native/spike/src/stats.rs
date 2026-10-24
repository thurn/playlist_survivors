//! Median / p95 / max summaries, in milliseconds.

#[derive(serde::Serialize, Clone, Debug)]
pub struct Summary {
    pub n: usize,
    pub median: f64,
    pub p95: f64,
    pub p99: f64,
    pub p999: f64,
    pub max: f64,
}

pub fn summarize(xs: &[f64]) -> Summary {
    let mut v: Vec<f64> = xs.iter().copied().filter(|x| x.is_finite()).collect();
    v.sort_by(|a, b| a.partial_cmp(b).unwrap());
    let q = |p: f64| -> f64 {
        if v.is_empty() {
            return f64::NAN;
        }
        // Nearest rank.
        let k = ((p * v.len() as f64).ceil() as usize).clamp(1, v.len());
        v[k - 1]
    };
    Summary {
        n: v.len(),
        median: q(0.5),
        p95: q(0.95),
        p99: q(0.99),
        p999: q(0.999),
        max: v.last().copied().unwrap_or(f64::NAN),
    }
}

impl std::fmt::Display for Summary {
    fn fmt(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        write!(
            f,
            "n={} median={:.3} p95={:.3} p99={:.3} max={:.3}",
            self.n, self.median, self.p95, self.p99, self.max
        )
    }
}
