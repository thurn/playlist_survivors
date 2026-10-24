//! `jsmath` against V8's vectors: F2's (`native/oracle/math-vectors.jsonl.gz`, at `ts-freeze`) and
//! 44000 more from the same Node (`tests/data/math-vectors-extra.jsonl.gz`, over the game's ranges
//! and random bits). Every function must return the same bits for every vector.

use std::collections::BTreeMap;
use std::io::{BufRead, BufReader};
use std::path::Path;

use sim::jsmath;

fn bits(hex: &str) -> f64 {
    f64::from_bits(u64::from_str_radix(hex, 16).unwrap())
}

#[test]
fn jsmath_matches_every_v8_vector() {
    check(
        &Path::new(env!("CARGO_MANIFEST_DIR")).join("../../oracle/math-vectors.jsonl.gz"),
        17,
    );
}

#[test]
fn jsmath_matches_the_extra_v8_vectors() {
    check(
        &Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/data/math-vectors-extra.jsonl.gz"),
        8,
    );
}

fn check(path: &Path, functions: usize) {
    let file = std::fs::File::open(path).unwrap_or_else(|e| panic!("{}: {e}", path.display()));
    let reader = BufReader::new(flate2::read::GzDecoder::new(file));
    let mut counts: BTreeMap<String, (usize, usize)> = BTreeMap::new();
    let mut examples = Vec::new();
    for line in reader.lines() {
        let line = line.unwrap();
        let v: serde_json::Value = serde_json::from_str(&line).unwrap();
        let name = v["fn"].as_str().unwrap().to_string();
        let args: Vec<f64> = v["args"]
            .as_array()
            .unwrap()
            .iter()
            .map(|a| bits(a.as_str().unwrap()))
            .collect();
        let want = bits(v["out"].as_str().unwrap());
        let a = |i: usize| args.get(i).copied().unwrap_or(f64::NAN);
        let got = match name.as_str() {
            "abs" => jsmath::abs(a(0)),
            "atan2" => jsmath::atan2(a(0), a(1)),
            "ceil" => jsmath::ceil(a(0)),
            "cos" => jsmath::cos(a(0)),
            "exp" => jsmath::exp(a(0)),
            "floor" => jsmath::floor(a(0)),
            "hypot" => jsmath::hypot_n(&args),
            "imul" => jsmath::imul(a(0), a(1)),
            "log" => jsmath::log(a(0)),
            "log2" => jsmath::log2(a(0)),
            "max" => jsmath::max(a(0), a(1)),
            "min" => jsmath::min(a(0), a(1)),
            "pow" => jsmath::pow(a(0), a(1)),
            "round" => jsmath::round(a(0)),
            "sign" => jsmath::sign(a(0)),
            "sin" => jsmath::sin(a(0)),
            "sqrt" => jsmath::sqrt(a(0)),
            other => panic!("no jsmath function for {other}"),
        };
        let entry = counts.entry(name.clone()).or_default();
        entry.0 += 1;
        // (Every NaN is one value in JS: compare NaNs as NaN, everything else by bits.)
        let same = (got.is_nan() && want.is_nan()) || got.to_bits() == want.to_bits();
        if !same {
            entry.1 += 1;
            if examples.len() < 20 {
                examples.push(format!(
                    "{name}{args:?} = {got:e} ({:016x}), V8 {want:e} ({:016x})",
                    got.to_bits(),
                    want.to_bits()
                ));
            }
        }
    }
    let failed: Vec<_> = counts.iter().filter(|(_, c)| c.1 > 0).collect();
    assert!(
        counts.len() >= functions,
        "only {} functions in {}",
        counts.len(),
        path.display()
    );
    assert!(
        failed.is_empty(),
        "mismatches (function: (vectors, mismatches)): {failed:?}\n{}",
        examples.join("\n")
    );
}

#[test]
fn to_int32_wraps_like_ecmascript() {
    assert_eq!(jsmath::to_int32(2_147_483_648.0), -2_147_483_648);
    assert_eq!(jsmath::to_int32(4_294_967_297.5), 1);
    assert_eq!(jsmath::to_int32(-1.9), -1);
    assert_eq!(jsmath::to_int32(-4_294_967_297.0), -1);
    assert_eq!(jsmath::to_int32(f64::NAN), 0);
    assert_eq!(jsmath::to_int32(f64::NEG_INFINITY), 0);
    assert_eq!(jsmath::to_uint32(-1.0), 4_294_967_295);
}
