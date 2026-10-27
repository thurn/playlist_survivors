//! The sim modules (timing, movement, strides, aim, tiers, upgrades, gait) against the TS at
//! ts-freeze, bit for bit: `sim-vectors.json.gz`, from `gen-sim-vectors.mts`, holds each function's
//! inputs and outputs on the shipped content's hero, weapons, passives and enemies.
#![allow(clippy::case_sensitive_file_extension_comparisons, clippy::float_cmp)]

use std::io::Read;
use std::path::{Path, PathBuf};
use std::sync::OnceLock;

use indexmap::IndexMap;
use serde_json::Value;
use sim::content::types::{Gait, Subdivision};
use sim::content::{ContentLibrary, SourceFile, load_content};
use sim::movement::{self, InputKey, Key, Rock, Step, TakenPress};
use sim::strides::{self, StrideState, StrideSteps};
use sim::timing::{self, KeyGroup, Rating};
use sim::upgrades::{self, ItemKind, Loadout, UpgradeItem, UpgradeRecord};
use sim::{aim, gait, tiers};

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn library() -> &'static ContentLibrary {
    static LIB: OnceLock<ContentLibrary> = OnceLock::new();
    LIB.get_or_init(|| {
        let mut names: Vec<String> = std::fs::read_dir(root().join("content"))
            .unwrap()
            .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
            .filter(|n| n.ends_with(".yaml"))
            .collect();
        names.sort();
        let files: Vec<SourceFile> = names
            .iter()
            .map(|n| SourceFile {
                name: format!("content/{n}"),
                text: std::fs::read_to_string(root().join("content").join(n)).unwrap(),
            })
            .collect();
        load_content(&files).unwrap()
    })
}

fn vectors() -> &'static Value {
    static V: OnceLock<Value> = OnceLock::new();
    V.get_or_init(|| {
        let raw = std::fs::read(
            Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/data/sim-vectors.json.gz"),
        )
        .unwrap();
        let mut text = String::new();
        flate2::read::GzDecoder::new(&raw[..])
            .read_to_string(&mut text)
            .unwrap();
        serde_json::from_str(&text).unwrap()
    })
}

/// f64 bits as written by the generator.
fn f(v: &Value) -> f64 {
    f64::from_bits(u64::from_str_radix(v.as_str().unwrap_or_else(|| panic!("{v}")), 16).unwrap())
}

fn of(v: &Value) -> Option<f64> {
    if v.is_null() { None } else { Some(f(v)) }
}

fn fs(v: &Value) -> Vec<f64> {
    v.as_array().unwrap().iter().map(f).collect()
}

fn arr(v: &Value) -> &Vec<Value> {
    v.as_array().unwrap()
}

/// Bit-equal (NaN equal to NaN, -0 distinct from 0).
#[track_caller]
fn same(ours: f64, ts: f64, what: &str) {
    assert_eq!(
        ours.to_bits(),
        ts.to_bits(),
        "{what}: ours {ours:?}, TS {ts:?}"
    );
}

#[track_caller]
fn same_all(ours: &[f64], ts: &[f64], what: &str) {
    assert_eq!(ours.len(), ts.len(), "{what}: lengths");
    for (i, (a, b)) in ours.iter().zip(ts).enumerate() {
        same(*a, *b, &format!("{what}[{i}]"));
    }
}

fn key(name: &str) -> Key {
    match name {
        "up" => Key::Up,
        "down" => Key::Down,
        "left" => Key::Left,
        "right" => Key::Right,
        _ => panic!("key {name}"),
    }
}

fn input_key(name: &str) -> InputKey {
    if name == "space" {
        InputKey::Space
    } else {
        key(name).into()
    }
}

fn rating_name(r: Option<Rating>) -> Option<&'static str> {
    r.map(|r| match r {
        Rating::Perfect => "perfect",
        Rating::Great => "great",
        Rating::Good => "good",
    })
}

fn group(name: &str) -> KeyGroup {
    if name == "stomp" {
        KeyGroup::Stomp
    } else {
        KeyGroup::Direction
    }
}

#[test]
fn timing_matches_ts() {
    let hero = &library().hero;
    let t = &vectors()["timing"];
    for row in arr(&t["rate"]) {
        let r = timing::rate_press(hero, f(&row[0]));
        assert_eq!(
            rating_name(r.rating),
            row[1].as_str(),
            "ratePress({})",
            f(&row[0])
        );
        same(r.delta, f(&row[0]), "ratePress delta");
    }
    for set in arr(&t["nearest"]) {
        let grid = fs(&set["grid"]);
        for p in arr(&set["probes"]) {
            let s = f(&p[0]);
            assert_eq!(
                timing::nearest_index(&grid, s) as i64,
                p[1].as_i64().unwrap(),
                "nearestIndex({s}) on {} times",
                grid.len()
            );
            assert_eq!(
                timing::first_at_or_after(&grid, s) as i64,
                p[2].as_i64().unwrap(),
                "firstAtOrAfter({s})"
            );
            if !grid.is_empty() {
                let (r, gi) = timing::rate_press_on_grid(hero, s, &grid);
                assert_eq!(gi as i64, p[1].as_i64().unwrap());
                assert_eq!(
                    rating_name(r.rating),
                    p[3].as_str(),
                    "ratePressOnGrid({s}) rating"
                );
            }
        }
    }
    // (Classic mode's press classes left the TS game's on purpose, 2026-10-27: the sim's
    // engine/tests/classic_tests.rs tests them.)
    for row in arr(&t["tierFor"]) {
        let tier = row[0].as_f64().unwrap();
        assert_eq!(
            timing::tier_for(hero, tier, f(&row[1])),
            row[2].as_f64().unwrap(),
            "tierFor({tier}, {})",
            f(&row[1])
        );
    }
    for row in arr(&t["drain"]) {
        same(
            timing::drain_per_beat(hero, f(&row[0])),
            f(&row[1]),
            "drainPerBeat",
        );
    }
    for row in arr(&t["groove"]) {
        let g = fs(row);
        same(
            timing::groove_after_change(g[0], g[1]),
            g[2],
            "grooveAfterChange",
        );
    }
    same(timing::NEVER_LATE, f(&t["neverLate"]), "NEVER_LATE");
    for row in arr(&t["keyGroups"]) {
        let g = timing::key_group(input_key(row[0].as_str().unwrap()));
        assert_eq!(g, group(row[1].as_str().unwrap()));
    }
}

fn decode_step(v: &Value) -> Step {
    let press = (!v["press"].is_null()).then(|| {
        let p = fs(&v["press"]);
        TakenPress {
            w: p[0],
            s: p[1],
            x: p[2],
            y: p[3],
        }
    });
    let rock = (!v["rock"].is_null()).then(|| {
        let p = fs(&v["rock"]);
        Rock {
            w: p[0],
            x: p[1],
            y: p[2],
            tx: p[3],
            ty: p[4],
        }
    });
    Step {
        w0: f(&v["w0"]),
        x0: f(&v["x0"]),
        y0: f(&v["y0"]),
        dx: f(&v["dx"]),
        dy: f(&v["dy"]),
        len1: f(&v["len1"]),
        ex: f(&v["ex"]),
        ey: f(&v["ey"]),
        bend_e: of(&v["bendE"]),
        keys: arr(&v["keys"])
            .iter()
            .map(|k| key(k.as_str().unwrap()))
            .collect(),
        len: of(&v["len"]),
        held: v["held"].as_bool(),
        s0: of(&v["s0"]),
        press,
        rock,
    }
}

#[track_caller]
fn same_step(ours: &Step, ts: &Step, what: &str) {
    let opt = |x: Option<f64>| x.unwrap_or(-1e300);
    let num = |s: &Step| {
        vec![
            s.w0,
            s.x0,
            s.y0,
            s.dx,
            s.dy,
            s.len1,
            s.ex,
            s.ey,
            opt(s.bend_e),
            opt(s.len),
            opt(s.s0),
        ]
    };
    same_all(&num(ours), &num(ts), what);
    let shape = |s: &Step| {
        (
            s.keys.clone(),
            s.held,
            s.press,
            s.rock,
            s.bend_e.is_some(),
            s.len.is_some(),
            s.s0.is_some(),
        )
    };
    assert_eq!(shape(ours), shape(ts), "{what}");
}

#[test]
fn movement_matches_ts() {
    let hero = &library().hero;
    let m = &vectors()["movement"];
    let steps: Vec<Step> = arr(&m["steps"]).iter().map(decode_step).collect();
    for (i, b) in arr(&m["bends"]).iter().enumerate() {
        let step = &steps[b["step"].as_u64().unwrap() as usize];
        let wall = of(&b["wall"]);
        let walls = move |_x: f64, _y: f64, _dx: f64, _dy: f64, len: f64| {
            wall.map_or(len, |w| sim::jsmath::min(len, w))
        };
        let ours = movement::bend_step(
            hero,
            step,
            f(&b["w"]),
            key(b["key"].as_str().unwrap()),
            &walls,
            f(&b["duration"]),
        );
        match (&ours, b["out"].is_null()) {
            (None, true) => {}
            (Some(o), false) => same_step(o, &decode_step(&b["out"]), &format!("bendStep {i}")),
            _ => panic!("bendStep {i}: ours {ours:?}, TS {}", b["out"]),
        }
    }
    let rule = strides::stride_rule(hero);
    for p in arr(&m["poses"]) {
        let step = &steps[p[0].as_u64().unwrap() as usize];
        let pose = movement::move_pose(step, f(&p[1]), rule.duration_s, rule.hop);
        same_all(
            &[pose.x, pose.y, pose.z, pose.u],
            &[f(&p[2]), f(&p[3]), f(&p[4]), f(&p[5])],
            &format!("movePose step {}", p[0]),
        );
    }
    for row in arr(&m["ease"]) {
        let x = fs(row);
        same(movement::ease_out(x[0]), x[1], "easeOut");
    }
    for row in arr(&m["walk"]) {
        let x = fs(row);
        same(
            movement::walk_frame(hero, x[0], x[1]),
            x[2],
            &format!("walkFrame({}, {})", x[0], x[1]),
        );
    }
    for row in arr(&m["opposite"]) {
        assert_eq!(
            movement::opposite(key(row[0].as_str().unwrap()), key(row[1].as_str().unwrap())),
            row[2].as_bool().unwrap()
        );
    }
    same(
        movement::step_seconds(hero),
        f(&m["stepSeconds"]),
        "stepSeconds",
    );
}

#[test]
fn strides_match_ts() {
    let hero = &library().hero;
    let s = &vectors()["strides"];
    let rule = strides::stride_rule(hero);
    same_all(
        &[
            rule.speed,
            rule.duration_s,
            rule.hop,
            rule.max_period_s,
            rule.free_period_s,
            rule.stomp_s,
        ],
        &fs(&s["rule"]),
        "strideRule",
    );
    for set in arr(&s["sets"]) {
        let ss: StrideSteps = strides::stride_steps(&fs(&set["grid"]), rule.max_period_s);
        same_all(&ss.s, &fs(&set["s"]), "strideSteps.s");
        same_all(&ss.period, &fs(&set["period"]), "strideSteps.period");
        let per: Vec<u8> = arr(&set["per"])
            .iter()
            .map(|x| x.as_u64().unwrap() as u8)
            .collect();
        assert_eq!(ss.per, per);
        for p in arr(&set["probes"]) {
            let x = fs(p);
            same(
                strides::period_at(&ss, x[0]),
                x[1],
                &format!("periodAt({})", x[0]),
            );
            same(
                strides::stomp_cancels_until(&ss, x[0]),
                x[2],
                &format!("stompCancelsUntil({})", x[0]),
            );
        }
    }
    for row in arr(&s["held"]) {
        let keys: Vec<Key> = arr(&row[0])
            .iter()
            .map(|k| key(k.as_str().unwrap()))
            .collect();
        let ours = strides::held_direction(&keys);
        match (ours, row[1].is_null()) {
            (None, true) => {}
            (Some(d), false) => same_all(
                &[d.x, d.y],
                &fs(&row[1]),
                &format!("heldDirection {keys:?}"),
            ),
            _ => panic!("heldDirection {keys:?}: ours {ours:?}"),
        }
    }
    let steps: Vec<Step> = arr(&vectors()["movement"]["steps"])
        .iter()
        .map(decode_step)
        .collect();
    for (i, b) in arr(&s["budgets"]).iter().enumerate() {
        let before = fs(&b["before"]);
        let mut state = StrideState {
            budget: before[0],
            w: before[1],
            walk_from: before[2],
            stomp_until: f64::NEG_INFINITY,
        };
        let (w, one) = (f(&b["w"]), f(&b["one"]));
        let stride = &steps[b["step"].as_u64().unwrap() as usize];
        same(
            strides::budget_at(&rule, &state, w, one),
            f(&b["at"]),
            &format!("budgetAt {i}"),
        );
        let it = fs(&b["interrupt"]);
        let back = strides::interrupt(&rule, &mut state, stride, it[0], it[1], it[2], one, it[3]);
        same(back, f(&b["back"]), &format!("interrupt {i}"));
        same_all(
            &[state.budget, state.w],
            &fs(&b["after"]),
            &format!("interrupt {i} state"),
        );
        let striding = arr(&b["striding"]);
        assert_eq!(
            strides::striding(&rule, Some(stride), w),
            striding[0].as_bool().unwrap(),
            "striding {i}"
        );
        assert!(!strides::striding(&rule, None, w));
    }
}

#[test]
fn start_stride_matches_ts() {
    let hero = &library().hero;
    let rule = strides::stride_rule(hero);
    let starts = arr(&vectors()["strides"]["starts"]);
    assert!(
        starts.iter().any(|c| c["key"].is_null()) && starts.iter().any(|c| !c["wall"].is_null())
    );
    for (i, c) in starts.iter().enumerate() {
        let before = fs(&c["before"]);
        let mut state = StrideState {
            budget: before[0],
            w: before[1],
            walk_from: before[2],
            stomp_until: f64::NEG_INFINITY,
        };
        let wall = of(&c["wall"]);
        let walls = move |_x: f64, _y: f64, _dx: f64, _dy: f64, len: f64| {
            wall.map_or(len, |w| sim::jsmath::min(len, w))
        };
        let dir = fs(&c["dir"]);
        let ours = strides::start_stride(
            &rule,
            &mut state,
            f(&c["w"]),
            f(&c["x"]),
            f(&c["y"]),
            movement::Dir {
                x: dir[0],
                y: dir[1],
            },
            f(&c["one"]),
            c["key"].as_str().map(key),
            c["held"].as_bool().unwrap(),
            &walls,
        );
        same_step(&ours, &decode_step(&c["out"]), &format!("startStride {i}"));
        same_all(
            &[state.budget, state.w],
            &fs(&c["after"]),
            &format!("startStride {i} state"),
        );
    }
}

#[test]
fn bent_steps_keep_their_stride_start() {
    let steps: Vec<Step> = arr(&vectors()["movement"]["steps"])
        .iter()
        .map(decode_step)
        .collect();
    assert!(
        steps.iter().any(|s| s.s0.is_some() && s.bend_e.is_some()),
        "no bent step with s0 in the vectors"
    );
}

#[test]
fn aim_matches_ts() {
    for (i, a) in arr(&vectors()["aim"]).iter().enumerate() {
        let targets: Vec<aim::AimTarget> = arr(&a["targets"])
            .iter()
            .map(|t| {
                let x = fs(t);
                aim::AimTarget {
                    x: x[0],
                    y: x[1],
                    r: x[2],
                    weight: x[3],
                }
            })
            .collect();
        let reach = fs(&a["reach"]);
        let reach = aim::ArcReach {
            radius: reach[0],
            half_angle: reach[1],
        };
        let (hx, hy) = (f(&a["hx"]), f(&a["hy"]));
        let out = aim::aim(hx, hy, f(&a["facing"]), &targets, reach);
        same(out, f(&a["out"]), &format!("aim {i}"));
        let arc = aim::swing_arc(hx, hy, out, reach);
        let catches: Vec<bool> = targets
            .iter()
            .map(|t| aim::catches(&arc, t.x, t.y, t.r))
            .collect();
        let want: Vec<bool> = arr(&a["catches"])
            .iter()
            .map(|x| x.as_bool().unwrap())
            .collect();
        assert_eq!(catches, want, "catches {i}");
    }
}

#[test]
fn tiers_match_ts() {
    let lib = library();
    let hero = &lib.hero;
    for row in arr(&vectors()["tiers"]) {
        let t = f(&row["tier"]);
        let sw = tiers::swing_arc_at(hero, t);
        same_all(
            &[sw.radius, sw.degrees],
            &fs(&row["swing"]),
            &format!("swingArcAt({t})"),
        );
        let hv = tiers::heavy_arc_at(hero, t);
        same_all(
            &[hv.radius, hv.degrees],
            &fs(&row["heavy"]),
            &format!("heavyArcAt({t})"),
        );
        same(
            tiers::wave_reach_at(hero, t),
            f(&row["wave"]),
            "waveReachAt",
        );
        same(tiers::tier_damage(hero, t), f(&row["damage"]), "tierDamage");
        for w in arr(&row["weapons"]) {
            let wd = &lib.weapons[w["id"].as_str().unwrap()];
            if !w["orbit"].is_null() {
                let o = tiers::orbit_at(wd, t);
                same_all(
                    &[o.count, o.radius],
                    &fs(&w["orbit"]),
                    &format!("orbitAt {} {t}", wd.id),
                );
                let e = upgrades::evolved_orbit(wd, o, t > 1.0);
                same_all(&[e.count, e.radius], &fs(&w["evolved"]), "evolvedOrbit");
            }
            if !w["chain"].is_null() {
                match (tiers::chain_at(wd, t), w["chain"].as_str() == Some("none")) {
                    (None, true) => {}
                    (Some(c), false) => same_all(&[c.jumps, c.range], &fs(&w["chain"]), "chainAt"),
                    (c, _) => panic!("chainAt {} {t}: ours {c:?}", wd.id),
                }
            }
            if !w["trail"].is_null() {
                same(
                    tiers::trail_radius_at(wd, t),
                    f(&w["trail"]),
                    &format!("trailRadiusAt {} {t}", wd.id),
                );
            }
        }
    }
}

fn item(kind: &str, id: &str) -> UpgradeItem {
    let kind = match kind {
        "weapon" => ItemKind::Weapon,
        "evolve" => ItemKind::Evolve,
        "passive" => ItemKind::Passive,
        _ => panic!("{kind}"),
    };
    UpgradeItem {
        kind,
        id: id.to_string(),
    }
}

fn items(v: &Value) -> Vec<UpgradeItem> {
    arr(v)
        .iter()
        .map(|x| item(x[0].as_str().unwrap(), x[1].as_str().unwrap()))
        .collect()
}

fn strings(v: &Value) -> Vec<String> {
    arr(v)
        .iter()
        .map(|x| x.as_str().unwrap().to_string())
        .collect()
}

fn passives(v: &Value) -> IndexMap<String, f64> {
    arr(v)
        .iter()
        .map(|x| (x[0].as_str().unwrap().to_string(), f(&x[1])))
        .collect()
}

#[test]
fn upgrades_match_ts() {
    let lib = library();
    let u = &vectors()["upgrades"];
    let loadouts: Vec<Loadout> = arr(&u["loadouts"])
        .iter()
        .map(|l| Loadout {
            weapons: strings(&l["weapons"]),
            evolved: strings(&l["evolved"]),
            passives: passives(&l["passives"]),
        })
        .collect();
    let offer_lists: Vec<Vec<String>> = arr(&u["offerLists"]).iter().map(strings).collect();
    for o in arr(&u["offers"]) {
        let l = &loadouts[o["loadout"].as_u64().unwrap() as usize];
        let list = &offer_lists[o["offer"].as_u64().unwrap() as usize];
        let ours = upgrades::offers_for(f(&o["seed"]), f(&o["index"]), lib, list, l);
        assert_eq!(
            ours,
            items(&o["out"]),
            "offersFor seed {} index {}",
            f(&o["seed"]),
            f(&o["index"])
        );
    }
    let item_list = |offers: Vec<(&str, &str)>| {
        offers
            .into_iter()
            .map(|(k, i)| item(k, i))
            .collect::<Vec<_>>()
    };
    let rec = |s: f64, offers: Vec<(&str, &str)>, taken: i32, s_apply: Option<f64>| UpgradeRecord {
        s,
        offers: item_list(offers),
        taken,
        w: None,
        orbs: None,
        s_taken: None,
        w_taken: None,
        s_apply,
        walked: None,
    };
    let records = vec![
        rec(
            10.0,
            vec![("passive", "might"), ("weapon", "fire-trail")],
            1,
            None,
        ),
        rec(20.0, vec![("passive", "might")], 0, Some(25.0)),
        rec(30.0, vec![("evolve", "orbiting-blades")], -1, None),
        rec(
            40.0,
            vec![("passive", "armour"), ("passive", "might")],
            1,
            None,
        ),
    ];
    for row in arr(&u["loadoutAt"]) {
        let s = f(&row["s"]);
        let l = upgrades::loadout_at(&["orbiting-blades".to_string()], &records, s);
        assert_eq!(l.weapons, strings(&row["weapons"]), "loadoutAt({s})");
        assert_eq!(l.evolved, strings(&row["evolved"]));
        assert_eq!(l.passives, passives(&row["passives"]));
        let b = upgrades::passive_bonus(lib, &l.passives);
        same_all(
            &[b.max_health, b.pickup, b.damage, b.armour],
            &fs(&row["bonus"]),
            "passiveBonus",
        );
    }
    for p in arr(&u["preferred"]) {
        let offers = items(&p["offers"]);
        assert_eq!(
            upgrades::preferred_offer(&offers, f(&p["health"])) as u64,
            p["out"].as_u64().unwrap(),
            "preferredOffer {offers:?}"
        );
    }
    for row in arr(&u["bites"]) {
        let x = fs(row);
        same(upgrades::armoured_bite(x[0], x[1]), x[2], "armouredBite");
    }
    for row in arr(&u["names"]) {
        let it = item(row[0].as_str().unwrap(), row[1].as_str().unwrap());
        assert_eq!(upgrades::item_name(lib, &it), row[2].as_str().unwrap());
    }
}

#[test]
fn gait_matches_ts() {
    for g in arr(&vectors()["gait"]) {
        let every = if g["every"] == "eighth" {
            Subdivision::Eighth
        } else {
            Subdivision::Beat
        };
        let gt = Gait {
            every,
            pattern: g["pattern"].as_str().unwrap().to_string(),
            share: 0.5,
        };
        same(gait::gait_per_beat(&gt), f(&g["perBeat"]), "gaitPerBeat");
        same(gait::gait_ground(&gt), f(&g["ground"]), "gaitGround");
        // A fractional or NaN gait time is no step (TS reads pattern[2.5] as undefined).
        assert!(!gait::gait_steps(&gt, 2.5) && !gait::gait_steps(&gt, f64::NAN));
        for s in arr(&g["slots"]) {
            let x = fs(s);
            same(
                gait::gait_slot(&gt, x[0]),
                x[1],
                &format!("gaitSlot({})", x[0]),
            );
        }
        for k in arr(&g["ks"]) {
            let kk = k[0].as_f64().unwrap();
            assert_eq!(
                gait::gait_steps(&gt, kk),
                k[1].as_bool().unwrap(),
                "gaitSteps {} {kk}",
                gt.pattern
            );
            assert_eq!(
                gait::gait_step_at_or_before(&gt, kk),
                k[2].as_f64().unwrap()
            );
            assert_eq!(gait::gait_step_after(&gt, kk), k[3].as_f64().unwrap());
            same(gait::gait_beat(&gt, kk), f(&k[4]), "gaitBeat");
        }
    }
}
