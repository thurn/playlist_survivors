//! A short original brass-like warning, generated at the output rate; no asset bake.
pub(super) fn horn(rate: f64) -> Vec<f32> {
    (0..(rate * 0.75) as usize)
        .map(|i| {
            let t = i as f64 / rate;
            let envelope = (t / 0.025).min(1.0) * (1.0 - t / 0.75).max(0.0).powf(0.6);
            let phase = std::f64::consts::TAU * 146.832 * t;
            (0.15
                * envelope
                * (phase.sin() + 0.45 * (phase * 2.0).sin() + 0.22 * (phase * 3.0).sin()))
                as f32
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn warning_horn_is_finite_short_and_below_full_scale_at_output_rates() {
        for rate in [8000.0, 44100.0, 48000.0] {
            let pcm = horn(rate);
            assert_eq!(pcm.len(), (0.75 * rate) as usize);
            assert!(pcm.iter().all(|s| s.is_finite() && s.abs() < 0.26));
            let rms =
                (pcm.iter().map(|s| f64::from(*s).powi(2)).sum::<f64>() / pcm.len() as f64).sqrt();
            assert!((0.05..0.09).contains(&rms));
        }
    }
}
