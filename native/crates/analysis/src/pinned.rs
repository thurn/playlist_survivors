//! The library arithmetic the analyzer reproduces, pinned to the libraries' own bits
//! (LIBRARY-ARITHMETIC.md). `tests/fixtures/library-arithmetic.json` is written by
//! `uv run --project tools/analysis python -m ps_analysis.probe <it>`, and the analysis tool's
//! `tests/test_probe.py` fails when a numpy, scipy or madmom upgrade changes an entry.

use serde_json::Value;

use crate::dbn::log_densities;
use crate::frontend::hanning;
use crate::hindsight::{complex_mean, unit};
use crate::numeric::{pairwise_sum, pairwise_sum_f32, percentile_linear};
use crate::onsets::{OnsetParams, RunningMean, Strengths, Superflux, kept_after};

fn fixture() -> Value {
    serde_json::from_str(include_str!("../tests/fixtures/library-arithmetic.json"))
        .expect("the probe's fixture")
}

fn u64s(v: &Value) -> Vec<u64> {
    v.as_array()
        .expect("a list")
        .iter()
        .map(|x| x.as_u64().expect("bits"))
        .collect()
}

fn f64s(v: &Value) -> Vec<f64> {
    v.as_array()
        .expect("a list")
        .iter()
        .map(|x| x.as_f64().expect("a number"))
        .collect()
}

fn values64(n: u64) -> Vec<f64> {
    (0..n)
        .map(|i| ((i * 7919) % 1_000_003) as f64 / 1_000_003.0)
        .map(|x| x * x)
        .collect()
}

#[test]
fn sums_and_means_are_numpys() {
    let f = fixture();
    let v = values64(30000);
    for case in f["sums"]["f64"].as_array().unwrap() {
        let n = case[0].as_u64().unwrap() as usize;
        assert_eq!(
            pairwise_sum(&v[..n]).to_bits(),
            case[1].as_u64().unwrap(),
            "np.sum of {n} float64"
        );
    }
    let v32: Vec<f32> = (0..20000u64)
        .map(|i| (((i * 7919) % 1_000_003) as f64 / 1_000_003.0) as f32)
        .collect();
    for case in f["sums"]["f32"].as_array().unwrap() {
        let n = case[0].as_u64().unwrap() as usize;
        assert_eq!(
            u64::from(pairwise_sum_f32(&v32[..n]).to_bits()),
            case[1].as_u64().unwrap(),
            "np.sum of {n} float32"
        );
    }
    for case in f["sums"]["mean"].as_array().unwrap() {
        let n = case[0].as_u64().unwrap() as usize;
        assert_eq!(
            (pairwise_sum(&v[..n]) / n as f64).to_bits(),
            case[1].as_u64().unwrap(),
            "np.mean of {n}"
        );
    }
}

#[test]
fn percentiles_are_numpys() {
    let f = fixture();
    let cases = f["percentiles"].as_array().unwrap();
    let lists: Vec<Vec<f64>> = cases.last().unwrap()[1]
        .as_array()
        .unwrap()
        .iter()
        .map(f64s)
        .collect();
    for case in &cases[..cases.len() - 1] {
        let (i, q) = (
            case[0].as_u64().unwrap() as usize,
            case[1].as_f64().unwrap(),
        );
        let mut sorted = lists[i].clone();
        sorted.sort_by(f64::total_cmp);
        assert_eq!(
            percentile_linear(&sorted, q).to_bits(),
            case[2].as_u64().unwrap(),
            "list {i} at {q}"
        );
    }
}

#[test]
fn strong_flags_are_detect_onsets() {
    let f = &fixture()["strong_flags"];
    let mut st = Strengths::new(&OnsetParams::default());
    let flags: Vec<u64> = f64s(&f["times"])
        .iter()
        .zip(f64s(&f["strengths"]))
        .map(|(&t, s)| u64::from(st.onset(t, s as f32).strong))
        .collect();
    assert_eq!(flags, u64s(&f["flags"]));
}

#[test]
fn the_running_mean_is_scipys_uniform_filter() {
    let f = &fixture()["uniform_filter"];
    let mut mean = RunningMean::new(11);
    for _ in 0..3 {
        mean.step(0.0);
    }
    let got: Vec<u64> = f64s(&f["values"])
        .iter()
        .map(|&v| u64::from(mean.step(v as f32).to_bits()))
        .collect();
    assert_eq!(got, u64s(&f["means"]));
}

#[test]
fn superflux_is_madmoms() {
    let mut spec: Vec<Vec<f32>> = (0..4u64)
        .map(|f| {
            (0..1024u64)
                .map(|b| ((((f * 1031 + b * 7919) % 997) as f64 / 997.0).powf(3.0) * 50.0) as f32)
                .collect()
        })
        .collect();
    spec[2][..3].copy_from_slice(&[60.0, 0.0, 5.0]);
    spec[2][1021..].copy_from_slice(&[1.0, 70.0, 2.0]);
    let mut sf = Superflux::new(&OnsetParams::default(), 1);
    let got: Vec<u64> = spec
        .iter()
        .map(|s| u64::from(sf.step(s).to_bits()))
        .collect();
    assert_eq!(got, u64s(&fixture()["superflux"]));
}

#[test]
fn combining_onsets_is_madmoms() {
    let f = &fixture()["combine_events"];
    let mut last = None;
    let mut kept = Vec::new();
    for frame in u64s(&f["frames"]) {
        let t = frame as f64 / 100.0;
        if kept_after(last, t, 0.03) {
            last = Some(t);
            kept.push(t.to_bits());
        }
    }
    assert_eq!(kept, u64s(&f["kept"]));
}

#[test]
fn the_phase_arithmetic_is_numpys() {
    let f = &fixture()["complex_phase"];
    let period = f["period"].as_f64().unwrap();
    let z: Vec<(f64, f64)> = f64s(&f["xs"]).iter().map(|&x| unit(x, period)).collect();
    let units: Vec<Vec<u64>> = f["units"].as_array().unwrap().iter().map(u64s).collect();
    assert_eq!(
        z.iter()
            .map(|&(r, i)| vec![r.to_bits(), i.to_bits()])
            .collect::<Vec<_>>(),
        units
    );
    for case in f["means"].as_array().unwrap() {
        let n = case[0].as_u64().unwrap() as usize;
        let (re, im) = complex_mean(&z[..n]);
        assert_eq!(
            [
                re.to_bits(),
                im.to_bits(),
                re.hypot(im).to_bits(),
                im.atan2(re).to_bits()
            ],
            [
                case[1].as_u64().unwrap(),
                case[2].as_u64().unwrap(),
                case[3].as_u64().unwrap(),
                case[4].as_u64().unwrap()
            ],
            "mean of {n}"
        );
    }
    let big: Vec<(f64, f64)> = (0..40).map(|i| unit(f64::from(i) * 0.37, period)).collect();
    let (re, im) = complex_mean(&big);
    assert_eq!(vec![re.to_bits(), im.to_bits()], u64s(&f["mean40"]));
}

#[test]
fn hanning_is_numpys() {
    let f = &fixture()["hanning"];
    assert_eq!(
        hanning(8).iter().map(|x| x.to_bits()).collect::<Vec<_>>(),
        u64s(&f["m8"])
    );
    let w = hanning(2048);
    for case in f["m2048"].as_array().unwrap() {
        let i = case[0].as_u64().unwrap() as usize;
        assert_eq!(
            w[i].to_bits(),
            case[1].as_u64().unwrap(),
            "hanning(2048)[{i}]"
        );
    }
}

#[test]
fn log_densities_are_madmoms() {
    let f = &fixture()["log_densities"];
    let acts: Vec<f32> = f64s(&f["activations"]).iter().map(|&a| a as f32).collect();
    let (non, beat): (Vec<f64>, Vec<f64>) = acts.iter().map(|&a| log_densities(a, 16.0)).unzip();
    assert_eq!(
        non.iter().map(|x| x.to_bits()).collect::<Vec<_>>(),
        u64s(&f["non_beat"])
    );
    assert_eq!(
        beat.iter().map(|x| x.to_bits()).collect::<Vec<_>>(),
        u64s(&f["beat"])
    );
    assert_eq!(
        non.iter().map(|x| x.exp().to_bits()).collect::<Vec<_>>(),
        u64s(&f["exp_non_beat"])
    );
    assert_eq!(
        beat.iter().map(|x| x.exp().to_bits()).collect::<Vec<_>>(),
        u64s(&f["exp_beat"])
    );
}

#[test]
fn rounding_is_pythons() {
    for case in fixture()["rounding"].as_array().unwrap() {
        let (x, want) = (case[0].as_f64().unwrap(), case[1].as_i64().unwrap());
        assert_eq!(x.round_ties_even() as i64, want, "round({x})");
    }
}
