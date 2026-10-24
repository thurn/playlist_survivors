//! `sim::core` against the TS core's own outputs at `ts-freeze` (`tests/data/core-vectors.json`,
//! from `gen-core-vectors.mts`): hash32 and Rng, noise, shaping, the warp, geometry and the song
//! map on the Galop. Every number is compared by its bits.
#![allow(
    clippy::many_single_char_names,
    clippy::unreadable_literal,
    clippy::case_sensitive_file_extension_comparisons
)]

use std::path::Path;

use serde_json::Value;
use sim::core::geometry::{self as g, Shape};
use sim::core::rng::{Part, Rng, hash32, noise1, noise2, rand01};
use sim::core::shaping as sh;
use sim::core::songmap::{self as sm, BarBeat, Feel, MeterRegion, SongMap, SongSection};
use sim::core::warp::Warp;

fn data() -> Value {
    let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/data/core-vectors.json");
    serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap()
}

fn bits(v: &Value) -> f64 {
    f64::from_bits(u64::from_str_radix(v.as_str().unwrap(), 16).unwrap())
}

fn all(v: &Value) -> Vec<f64> {
    v.as_array().unwrap().iter().map(bits).collect()
}

#[track_caller]
fn same(got: f64, want: f64, what: &str) {
    assert!(
        got.to_bits() == want.to_bits() || (got.is_nan() && want.is_nan()),
        "{what}: {got:e} ({:016x}), TS {want:e} ({:016x})",
        got.to_bits(),
        want.to_bits()
    );
}

#[track_caller]
fn same_all(got: &[f64], want: &[f64], what: &str) {
    assert_eq!(got.len(), want.len(), "{what}: lengths");
    for (i, (a, b)) in got.iter().zip(want).enumerate() {
        same(*a, *b, &format!("{what}[{i}]"));
    }
}

fn parts(v: &Value) -> (Vec<String>, Vec<Option<f64>>) {
    // (Strings and numbers in order: a string part is Some(string), a number part None + value.)
    let mut strings = Vec::new();
    let mut nums = Vec::new();
    for p in v.as_array().unwrap() {
        if let Some(s) = p.get("s") {
            strings.push(s.as_str().unwrap().to_string());
            nums.push(None);
        } else {
            strings.push(String::new());
            nums.push(Some(bits(&p["n"])));
        }
    }
    (strings, nums)
}

fn with_parts<R>(v: &Value, f: impl FnOnce(&[Part]) -> R) -> R {
    let (strings, nums) = parts(v);
    let ps: Vec<Part> = strings
        .iter()
        .zip(&nums)
        .map(|(s, n)| match n {
            Some(n) => Part::Num(*n),
            None => Part::Str(s),
        })
        .collect();
    f(&ps)
}

fn seed_parts(v: &Value) -> Vec<Value> {
    v.as_array()
        .unwrap()
        .iter()
        .map(|p| {
            if let Some(s) = p.as_str() {
                serde_json::json!({ "s": s })
            } else {
                serde_json::json!({ "n": format!("{:016x}", p.as_f64().unwrap().to_bits()) })
            }
        })
        .collect()
}

#[test]
fn hash32_matches_ts() {
    for case in data()["hashes"].as_array().unwrap() {
        let got = with_parts(&case["parts"], hash32);
        same(
            got,
            bits(&case["out"]),
            &format!("hash32({})", case["parts"]),
        );
    }
}

#[test]
fn rng_matches_ts() {
    for case in data()["rngs"].as_array().unwrap() {
        let seed = Value::Array(seed_parts(&case["seed"]));
        let mut r = with_parts(&seed, Rng::new);
        let next: Vec<f64> = (0..40).map(|_| r.next()).collect();
        same_all(
            &next,
            &all(&case["next"]),
            &format!("Rng({}).next", case["seed"]),
        );
        let ops = [
            r.range(-5.0, 7.0),
            r.int(3.0, 9.0),
            r.weighted_index(3, &[0.2, 0.5, 0.3]) as f64,
            r.round_stochastic(2.4),
            r.signed(),
            [10.0, 20.0, 30.0, 40.0][r.pick_index(4)],
        ];
        same_all(
            &ops,
            &all(&case["ops"]),
            &format!("Rng({}) ops", case["seed"]),
        );
    }
}

#[test]
fn noise_and_shaping_match_ts() {
    let d = data();
    for case in d["noise"].as_array().unwrap() {
        let seed = case["seed"].as_f64().unwrap();
        let x = case["x"].as_f64().unwrap();
        same(
            rand01(seed, (x * 10.0).floor(), 3.0),
            bits(&case["rand01"]),
            "rand01",
        );
        same(noise1(seed, x), bits(&case["noise1"]), "noise1");
        same(
            noise2(seed, x, x * 0.7 - 1.0),
            bits(&case["noise2"]),
            "noise2",
        );
    }
    for case in d["shaping"].as_array().unwrap() {
        let p = case["p"].as_f64().unwrap();
        let got = [
            sh::beat_hit(p),
            sh::beat_swell(p),
            sh::amount(2.0, p),
            sh::smoothstep(p),
            sh::frac(p * 3.3),
            sh::lerp(1.0, 5.0, p),
            sh::clamp(p, 0.0, 1.0),
        ];
        same_all(&got, &all(&case["out"]), &format!("shaping({p})"));
    }
}

#[test]
fn warp_matches_ts() {
    for case in data()["warps"].as_array().unwrap() {
        let steps: Vec<(f64, f64)> = case["steps"]
            .as_array()
            .unwrap()
            .iter()
            .map(|st| (bits(&st[0]), bits(&st[1])))
            .collect();
        let w = Warp::from_rates(&steps);
        let breaks: Vec<f64> = w.breaks.iter().flat_map(|b| [b.s, b.w, b.rate]).collect();
        let want: Vec<f64> = case["breaks"]
            .as_array()
            .unwrap()
            .iter()
            .flat_map(all)
            .collect();
        same_all(&breaks, &want, "breaks");
        let ss = all(&case["s"]);
        same_all(
            &ss.iter().map(|&s| w.to_world(s)).collect::<Vec<_>>(),
            &all(&case["toWorld"]),
            "toWorld",
        );
        same_all(
            &ss.iter().map(|&s| w.rate_at(s)).collect::<Vec<_>>(),
            &all(&case["rateAt"]),
            "rateAt",
        );
        same_all(
            &ss.iter().map(|&s| w.to_song(s)).collect::<Vec<_>>(),
            &all(&case["toSong"]),
            "toSong",
        );
        let stops: Vec<f64> = w.hit_stops().iter().flat_map(|&(a, b)| [a, b]).collect();
        let want: Vec<f64> = case["hitStops"]
            .as_array()
            .unwrap()
            .iter()
            .flat_map(all)
            .collect();
        same_all(&stops, &want, "hitStops");
    }
}

fn shape(v: &Value) -> Shape {
    let f = |k: &str| v[k].as_f64().unwrap();
    match v["kind"].as_str().unwrap() {
        "circle" => Shape::Circle {
            x: f("x"),
            y: f("y"),
            radius: f("radius"),
        },
        "ring" => Shape::Ring {
            x: f("x"),
            y: f("y"),
            inner: f("inner"),
            outer: f("outer"),
        },
        "capsule" => Shape::Capsule {
            x0: f("x0"),
            y0: f("y0"),
            x1: f("x1"),
            y1: f("y1"),
            radius: f("radius"),
        },
        "arc" => Shape::Arc {
            x: f("x"),
            y: f("y"),
            radius: f("radius"),
            dir: f("dir"),
            half_angle: f("halfAngle"),
            inner: v.get("inner").and_then(Value::as_f64),
        },
        k => panic!("shape {k}"),
    }
}

#[test]
fn geometry_matches_ts() {
    let d = data();
    let mut points = Vec::new();
    for i in 0..40 {
        let i = f64::from(i);
        points.push((
            sim::jsmath::cos(i * 1.7) * (i * 3.1),
            sim::jsmath::sin(i * 2.3) * (i * 2.9),
        ));
    }
    points.extend([(0.0, 0.0), (10.0, -5.0), (80.0, 0.0)]);
    for case in d["geometry"].as_array().unwrap() {
        let s = shape(&case["shape"]);
        let what = format!("{}", case["shape"]);
        same(
            g::shape_area(&s),
            bits(&case["area"]),
            &format!("area {what}"),
        );
        let c = g::shape_centre(&s);
        same_all(
            &[c.x, c.y],
            &all(&case["centre"]),
            &format!("centre {what}"),
        );
        same_all(
            &g::shape_bounds(&s),
            &all(&case["bounds"]),
            &format!("bounds {what}"),
        );
        for (k, &(x, y)) in points.iter().enumerate() {
            let want = &case["contains"][k];
            assert_eq!(
                g::shape_contains(&s, x, y, 0.0),
                want[0].as_bool().unwrap(),
                "contains {what} {x},{y}"
            );
            assert_eq!(
                g::shape_contains(&s, x, y, 5.0),
                want[1].as_bool().unwrap(),
                "contains+5 {what} {x},{y}"
            );
            let p = g::push_out_dir(&s, x, y);
            same_all(
                &[p.x, p.y],
                &all(&case["pushOut"][k]),
                &format!("pushOut {what} {x},{y}"),
            );
            let n = g::nearest_inside(&s, x, y, 0.5);
            same_all(
                &[n.x, n.y],
                &all(&case["nearest"][k]),
                &format!("nearest {what} {x},{y}"),
            );
        }
        let kind = case["shape"]["kind"].as_str().unwrap();
        let mut r = Rng::new(&[Part::Str("shape"), Part::Str(kind)]);
        let mut draw = || r.next();
        for i in 0..12 {
            let v = g::sample_in_shape(&s, &mut draw, if i % 3 == 0 { 0.0 } else { 4.0 });
            same_all(
                &[v.x, v.y],
                &all(&case["samples"][i]),
                &format!("sample {what} {i}"),
            );
        }
        assert_eq!(
            g::move_shape(&s, 3.5, -1.25),
            shape(&case["moved"]),
            "moved {what}"
        );
    }
    for w in d["wrap"].as_array().unwrap() {
        same(g::wrap_angle(bits(&w[0])), bits(&w[1]), "wrapAngle");
    }
}

fn galop() -> SongMap {
    let path =
        Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../songs/orpheus-galop/songmap.json");
    let v: Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    let nums = |v: &Value| -> Vec<f64> {
        v.as_array()
            .unwrap()
            .iter()
            .map(|x| x.as_f64().unwrap())
            .collect()
    };
    SongMap {
        meter: v["meter"]
            .as_array()
            .unwrap()
            .iter()
            .map(|m| MeterRegion {
                from_bar: m["fromBar"].as_f64().unwrap(),
                beats_per_bar: m["beatsPerBar"].as_f64().unwrap(),
            })
            .collect(),
        beats: nums(&v["beats"]),
        first_downbeat_index: v["firstDownbeatIndex"].as_f64().unwrap(),
        sections: v["sections"]
            .as_array()
            .unwrap()
            .iter()
            .map(|s| SongSection {
                label: s["label"].as_str().unwrap().to_string(),
                bars: [
                    s["bars"][0].as_f64().unwrap(),
                    s["bars"][1].as_f64().unwrap(),
                ],
                feel: if s["feel"] == "free" {
                    Feel::Free
                } else {
                    Feel::Pulsed
                },
                key: None,
                anchors: None,
            })
            .collect(),
        energy: nums(&v["energy"]),
        accents: Vec::new(),
    }
}

#[test]
fn songmap_matches_ts_on_the_galop() {
    let d = &data()["songmap"];
    let map = galop();
    let idx = sm::index_song(&map);
    same_all(&idx.bar_start, &all(&d["barStart"]), "barStart");
    same_all(&idx.beats_per_bar, &all(&d["beatsPerBar"]), "beatsPerBar");
    assert_eq!(idx.bar_count, d["barCount"].as_f64().unwrap());
    let times = all(&d["times"]);
    same_all(
        &times
            .iter()
            .map(|&s| sm::beat_index_at(&map, s))
            .collect::<Vec<_>>(),
        &all(&d["beatIndexAt"]),
        "beatIndexAt",
    );
    same_all(
        &times
            .iter()
            .map(|&s| sm::energy_at(&map, s))
            .collect::<Vec<_>>(),
        &all(&d["energyAt"]),
        "energyAt",
    );
    let labels: Vec<&str> = times
        .iter()
        .map(|&s| sm::section_at_time(&map, &idx, s).label.as_str())
        .collect();
    let want: Vec<&str> = d["sectionAtTime"]
        .as_array()
        .unwrap()
        .iter()
        .map(|v| v.as_str().unwrap())
        .collect();
    assert_eq!(labels, want, "sectionAtTime");
    let beat_indices = all(&d["beatIndices"]);
    same_all(
        &beat_indices
            .iter()
            .map(|&i| sm::time_of_beat_index(&map, i))
            .collect::<Vec<_>>(),
        &all(&d["timeOfBeatIndex"]),
        "timeOfBeatIndex",
    );
    let bars: Vec<f64> = [-3.0, 0.0, 1.0, 2.999999, 100.5, 300.0, 600.0]
        .iter()
        .map(|&i| sm::bar_of_beat_index(&idx, i))
        .collect();
    same_all(&bars, &all(&d["barOfBeatIndex"]), "barOfBeatIndex");
    let positions = [
        (-1.0, 1.0),
        (0.0, 2.0),
        (1.0, 1.0),
        (1.0, 2.5),
        (30.0, 1.0),
        (177.0, 2.0),
        (178.0, 1.0),
        (200.0, 1.5),
    ];
    let got: Vec<f64> = positions
        .iter()
        .map(|&(bar, beat)| sm::time_of(&map, &idx, BarBeat { bar, beat }))
        .collect();
    same_all(&got, &all(&d["timeOf"]), "timeOf");
    for (sec, want) in map
        .sections
        .iter()
        .zip(d["sectionTimes"].as_array().unwrap())
    {
        let (a, b) = sm::section_times(&map, &idx, sec);
        same_all(&[a, b], &all(want), &format!("sectionTimes {}", sec.label));
    }
    let at_bar: Vec<Option<&str>> = [0.0, 1.0, 14.0, 15.0, 108.0, 177.0, 300.0]
        .iter()
        .map(|&bar| sm::section_at_bar(&map, bar).map(|s| s.label.as_str()))
        .collect();
    let want: Vec<Option<&str>> = d["sectionAtBar"]
        .as_array()
        .unwrap()
        .iter()
        .map(Value::as_str)
        .collect();
    assert_eq!(at_bar, want, "sectionAtBar");
}
