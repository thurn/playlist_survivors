//! Musical orbit: keep source speed unless a cardinal beat lattice is within 15%.
use std::f64::consts::FRAC_PI_2;
pub const BIBLE_OMEGA: f64 = 35.0 * 0.0625;
/// Equally spaced blades keep their source offsets. Integer quarter-turns per
/// beat work for counts dividing four; otherwise a quarter may span N beats,
/// with 4N divisible by the blade count. Thus every blade has cardinal poses
/// on the lattice; faster integer steps can cross extra cardinals between beats.
/// An incompatible three-blade constellation keeps the source speed.
pub fn bible_orbit_speed(speed: f64, period: f64, amount: usize) -> (f64, bool) {
    let raw = BIBLE_OMEGA * speed.max(0.0);
    if raw <= 0.0 || !period.is_finite() || period <= 0.0 || amount == 0 {
        return (raw, false);
    }
    let gcd = match amount % 4 {
        0 => 4,
        2 => 2,
        _ => 1,
    };
    let stride = (amount / gcd) as f64;
    let n = FRAC_PI_2 / (raw * period) / stride;
    let slow = FRAC_PI_2 / (n.ceil().max(1.0) * stride * period);
    let fast = FRAC_PI_2 / (n.floor().max(1.0) * stride * period);
    let mut best = if (slow - raw).abs() < (fast - raw).abs() {
        slow
    } else {
        fast
    };
    if 4usize.is_multiple_of(amount) {
        let k = (raw * period / FRAC_PI_2).round().max(1.0);
        let direct = k * FRAC_PI_2 / period;
        if (direct - raw).abs() < (best - raw).abs() {
            best = direct;
        }
    }
    if (best / raw - 1.0).abs() <= 0.15 + 1e-12 {
        (best, true)
    } else {
        (raw, false)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn bible_cardinal_lattice_uses_source_offsets_and_the_fifteen_percent_limit() {
        for (bpm, speed, amount, locked) in [
            (70.0, 1.0, 1, false),
            (128.0, 1.0, 1, false),
            (174.0, 1.0, 1, true),
            (70.0, 1.6, 4, true),
            (128.0, 1.6, 4, true),
            (174.0, 1.6, 4, false),
            (128.0, 1.6, 3, false),
            (326.0, 1.3, 3, true),
        ] {
            let period = 60.0 / bpm;
            let (omega, yes) = bible_orbit_speed(speed, period, amount);
            assert_eq!(yes, locked, "{bpm} {speed} {amount}");
            if yes {
                assert!((omega / (BIBLE_OMEGA * speed) - 1.0).abs() <= 0.15 + 1e-12);
                for blade in 0..amount {
                    assert!(
                        (0..64).any(|beat| {
                            let angle = omega * period * f64::from(beat)
                                + std::f64::consts::TAU * blade as f64 / amount as f64;
                            (angle / FRAC_PI_2 - (angle / FRAC_PI_2).round()).abs() < 1e-9
                        }),
                        "each evenly spaced blade reaches a cardinal on a beat"
                    );
                }
            } else {
                assert!((omega - BIBLE_OMEGA * speed).abs() < 1e-12);
            }
            eprintln!(
                "Bible orbit: {bpm} BPM, Speed{speed}, Amount{amount}, source{:.9} rad/s -> {omega:.9}, locked{yes}",
                BIBLE_OMEGA * speed
            );
        }
        // Reciprocal grids must compare both sides: rounding 3.48 to 3
        // would miss the valid slower four-beat quarter.
        let period = FRAC_PI_2 / (BIBLE_OMEGA * 3.48);
        let (omega, yes) = bible_orbit_speed(1.0, period, 1);
        assert!(yes);
        assert!((omega - FRAC_PI_2 / (4.0 * period)).abs() < 1e-12);
        for (ratio, locked) in [
            (0.85, true),
            (1.15, true),
            (0.84999, false),
            (1.15001, false),
        ] {
            let period = FRAC_PI_2 / (BIBLE_OMEGA * ratio);
            assert_eq!(
                bible_orbit_speed(1.0, period, 4).1,
                locked,
                "15% boundary {ratio}"
            );
        }
        assert!(!bible_orbit_speed(1.0, 0.0, 1).1);
        assert_eq!(bible_orbit_speed(0.0, 0.5, 4), (0.0, false));
    }
}
