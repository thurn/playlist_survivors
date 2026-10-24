//! numpy's own float arithmetic where the analyzer must match it to the bit: its summation
//! (`np.add.reduce`, so `np.sum` and `np.mean`: pairwise within buffered chunks) and the linear
//! percentile (`np.percentile`).

/// numpy reduces a long array in buffered chunks of this many elements (`NPY_BUFSIZE`), adding
/// each chunk's pairwise sum to the running total in order.
const NPY_BUFSIZE: usize = 8192;

macro_rules! pairwise {
    ($name:ident, $block:ident, $t:ty) => {
        /// numpy's summation (`np.add.reduce`) in this float type: chunks of `NPY_BUFSIZE`
        /// elements, each summed pairwise, added in order.
        pub fn $name(a: &[$t]) -> $t {
            let mut chunks = a.chunks(NPY_BUFSIZE);
            let first = chunks.next().map_or(0.0, $block);
            chunks.fold(first, |s, c| s + $block(c))
        }

        /// numpy's pairwise summation of one chunk: a sequential sum under 8 values, eight
        /// accumulators per block of up to 128, halved recursively above that.
        fn $block(a: &[$t]) -> $t {
            let n = a.len();
            if n < 8 {
                return a.iter().fold(0.0, |s, &x| s + x);
            }
            if n <= 128 {
                let mut r = [0.0 as $t; 8];
                r.copy_from_slice(&a[..8]);
                let whole = n - n % 8;
                for block in a[8..whole].as_chunks::<8>().0 {
                    for (rj, x) in r.iter_mut().zip(block) {
                        *rj += x;
                    }
                }
                let mut res = ((r[0] + r[1]) + (r[2] + r[3])) + ((r[4] + r[5]) + (r[6] + r[7]));
                for &x in &a[whole..] {
                    res += x;
                }
                return res;
            }
            let mut half = n / 2;
            half -= half % 8;
            $block(&a[..half]) + $block(&a[half..])
        }
    };
}

pairwise!(pairwise_sum, pairwise_block, f64);
pairwise!(pairwise_sum_f32, pairwise_block_f32, f32);

/// `np.percentile(values, q)` with the default linear method: the virtual index `(n - 1) * q /
/// 100` between the sorted values, interpolated as numpy's `_lerp` does (from the upper value
/// when the weight is at least a half). `sorted` must be sorted ascending and non-empty.
pub fn percentile_linear(sorted: &[f64], q: f64) -> f64 {
    let count = sorted.len();
    assert!(count > 0, "the percentile of nothing");
    let virtual_index = (count - 1) as f64 * (q / 100.0);
    if virtual_index >= (count - 1) as f64 {
        return sorted[count - 1];
    }
    let below = virtual_index.floor() as usize;
    let gamma = virtual_index - below as f64;
    let (lo, hi) = (sorted[below], sorted[below + 1]);
    let diff = hi - lo;
    if gamma >= 0.5 {
        hi - diff * (1.0 - gamma)
    } else {
        lo + diff * gamma
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn long_sums_are_numpys_chunked_ones() {
        // np.sum of the same values (numpy 2.2, tools/analysis) across the 8192-element chunks.
        let v: Vec<f64> = (0..30000u64)
            .map(|i| ((i * 7919) % 1_000_003) as f64 / 1_000_003.0)
            .map(|x| x * x)
            .collect();
        let numpy: [(usize, u64); 5] = [
            (8192, 0x40a5_4294_cd85_da18),
            (8193, 0x40a5_441a_5880_f1ad),
            (16385, 0x40b5_4727_3148_5b30),
            (22050, 0x40bc_a5b3_f36b_e594),
            (30000, 0x40c3_7ff7_6680_7bf2),
        ];
        for (n, want) in numpy {
            assert_eq!(
                pairwise_sum(&v[..n]).to_bits(),
                want,
                "np.sum of {n} float64"
            );
        }
        let v32: Vec<f32> = (0..20000u64)
            .map(|i| (((i * 7919) % 1_000_003) as f64 / 1_000_003.0) as f32)
            .collect();
        for (n, want) in [
            (1024, 0x43fc_e286u32),
            (8200, 0x457f_f837),
            (20000, 0x461c_043c),
        ] {
            assert_eq!(
                pairwise_sum_f32(&v32[..n]).to_bits(),
                want,
                "np.sum of {n} float32"
            );
        }
    }

    #[test]
    fn percentiles_at_the_ends() {
        assert_eq!(percentile_linear(&[3.0], 98.0), 3.0);
        assert_eq!(percentile_linear(&[1.0, 2.0], 0.0), 1.0);
        assert_eq!(percentile_linear(&[1.0, 2.0], 100.0), 2.0);
        assert_eq!(percentile_linear(&[0.0, 10.0], 25.0), 2.5);
    }
}
