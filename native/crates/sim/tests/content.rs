//! Every current content file loads. F2's reference hash checks the unchanged legacy subset;
//! new Survivors role data has direct source contracts in survivor_spawning.rs.
#![allow(
    clippy::many_single_char_names,
    clippy::unreadable_literal,
    clippy::case_sensitive_file_extension_comparisons
)]

use std::io::Read;
use std::path::{Path, PathBuf};

use serde_json::Value;
use sim::canonical::hash_hex;
use sim::content::{SourceFile, load_content};

fn root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..")
}

fn content_files() -> Vec<SourceFile> {
    let mut names: Vec<String> = std::fs::read_dir(root().join("content"))
        .unwrap()
        .map(|e| e.unwrap().file_name().to_string_lossy().into_owned())
        .filter(|n| n.ends_with(".yaml"))
        .collect();
    names.sort();
    names
        .into_iter()
        .map(|n| SourceFile {
            text: std::fs::read_to_string(root().join("content").join(&n)).unwrap(),
            name: format!("content/{n}"),
        })
        .collect()
}

/// The first path where two JSON values differ (numbers by f64 value).
fn first_difference(a: &Value, b: &Value, path: &str) -> Option<String> {
    match (a, b) {
        (Value::Object(x), Value::Object(y)) => {
            let mut keys: Vec<&String> = x.keys().chain(y.keys()).collect();
            keys.sort();
            keys.dedup();
            for k in keys {
                match (x.get(k), y.get(k)) {
                    (Some(p), Some(q)) => {
                        if let Some(d) = first_difference(p, q, &format!("{path}.{k}")) {
                            return Some(d);
                        }
                    }
                    (p, q) => return Some(format!("{path}.{k}: ours {p:?}, TS {q:?}")),
                }
            }
            None
        }
        (Value::Array(x), Value::Array(y)) => {
            if x.len() != y.len() {
                return Some(format!("{path}: {} items, TS {}", x.len(), y.len()));
            }
            x.iter()
                .zip(y)
                .enumerate()
                .find_map(|(i, (p, q))| first_difference(p, q, &format!("{path}[{i}]")))
        }
        (Value::Number(x), Value::Number(y)) => {
            (x.as_f64() != y.as_f64()).then(|| format!("{path}: ours {x}, TS {y}"))
        }
        _ => (a != b).then(|| format!("{path}: ours {a}, TS {b}")),
    }
}

#[test]
fn current_content_loads_and_the_legacy_subset_keeps_its_reference_hash() {
    let files = content_files();
    let current = load_content(&files).unwrap_or_else(|e| panic!("{e}"));
    assert_eq!(
        current
            .enemies
            .values()
            .filter(|e| e.id.starts_with("vs-"))
            .count(),
        22 // twenty roster/boss roles plus the two event-only swarms
    );
    assert!(current.sound_palettes.contains_key("survivors"));
    let legacy: Vec<_> = files
        .into_iter()
        .filter(|f| f.name != "content/survivors-enemies.yaml")
        .collect();
    let mut lib = load_content(&legacy).unwrap_or_else(|e| panic!("{e}"));
    // This reference covers legacy content, not deliberate survivors additions, nor classic mode's
    // gentler groove costs (2026-10-27): the TS game's costs stand in for them.
    lib.value["soundPalettes"]
        .as_object_mut()
        .unwrap()
        .remove("survivors");
    let ts = ts_content();
    for cost in ["counted", "offbeat", "stray"] {
        lib.value["hero"]["meter"][cost] = ts["hero"]["meter"][cost].clone();
    }
    assert!(!lib.enemies.is_empty() && !lib.weapons.is_empty() && !lib.effects.is_empty());
    let oracle: Value = serde_json::from_str(
        &std::fs::read_to_string(root().join("native/oracle/content.json")).unwrap(),
    )
    .unwrap();
    let want = oracle["hash"].as_str().unwrap();
    let got = hash_hex(&lib.value);
    assert!(
        got == want,
        "content hash {got}, TS {want}; first difference: {}",
        first_difference(&lib.value, &ts, "")
            .unwrap_or_else(|| "none found (number bits or key order?)".into())
    );
}

/// The TS game's content, as the reference hash was taken from it.
fn ts_content() -> Value {
    let mut gz = String::new();
    flate2::read::GzDecoder::new(
        std::fs::File::open(root().join("native/oracle/content.json.gz")).unwrap(),
    )
    .read_to_string(&mut gz)
    .unwrap();
    serde_json::from_str(&gz).unwrap()
}

fn with_change(
    file: &str,
    from: &str,
    to: &str,
) -> Result<sim::content::ContentLibrary, sim::content::ContentError> {
    let files: Vec<SourceFile> = content_files()
        .into_iter()
        .map(|mut f| {
            if f.name == file {
                assert!(f.text.contains(from), "{file} has no {from:?}");
                f.text = f.text.replacen(from, to, 1);
            }
            f
        })
        .collect();
    load_content(&files)
}

fn messages(r: Result<sim::content::ContentLibrary, sim::content::ContentError>) -> Vec<String> {
    r.expect_err("the change should fail to load")
        .issues
        .iter()
        .map(ToString::to_string)
        .collect()
}

#[test]
fn defaults_fill_in_as_zod_does() {
    let lib = load_content(&content_files()).unwrap();
    let hero = &lib.value["hero"]["health"];
    assert!(hero.get("invulnerableMs").is_some() && hero.get("levelUpHeal").is_some());
    for (id, e) in &lib.enemies {
        assert!(e.scale > 0.0, "{id}");
        let v = &lib.value["enemies"][id];
        for field in [
            "behavior",
            "scale",
            "hover",
            "walkCycleBeats",
            "contactDamage",
            "attackDamage",
        ] {
            assert!(v.get(field).is_some(), "{id}.{field}");
        }
    }
    for p in lib.sound_palettes.values() {
        assert!(p.extends.is_none(), "{}", p.id);
    }
}

#[test]
fn invalid_content_is_reported_with_its_file_and_path() {
    let unknown = messages(with_change(
        "content/hero.yaml",
        "hop: 5,",
        "hop: 5, bounce: 2,",
    ));
    assert!(
        unknown.iter().any(|m| m.contains("content/hero.yaml")
            && m.contains("hero.stride.bounce")
            && m.contains("unknown field")),
        "{unknown:?}"
    );

    let missing = messages(with_change("content/hero.yaml", "hop: 5, ", ""));
    assert!(
        missing
            .iter()
            .any(|m| m.contains("hero.stride.hop") && m.contains("required field is missing")),
        "{missing:?}"
    );

    let negative = messages(with_change(
        "content/hero.yaml",
        "speed: 140",
        "speed: -140",
    ));
    assert!(
        negative
            .iter()
            .any(|m| m.contains("hero.walk.speed") && m.contains(">0")),
        "{negative:?}"
    );

    let dup = content_files()
        .into_iter()
        .find(|f| f.name == "content/hero.yaml")
        .unwrap();
    let mut twice = content_files();
    twice.push(SourceFile {
        name: "content/zz-hero.yaml".into(),
        text: dup.text,
    });
    let again = messages(load_content(&twice));
    assert!(
        again
            .iter()
            .any(|m| m.contains("the hero is already defined in content/hero.yaml")),
        "{again:?}"
    );
}

#[test]
fn cross_references_and_extends_are_checked() {
    let lib = load_content(&content_files()).unwrap();
    let (id, e) = lib.enemies.iter().next().unwrap();
    let recipe = format!("recipe: {}", e.death.recipe);
    let file = content_files()
        .into_iter()
        .find(|f| f.name.ends_with("enemies.yaml"))
        .unwrap();
    assert!(file.text.contains(&recipe), "{id}");
    let bad = messages(with_change(
        "content/enemies.yaml",
        &recipe,
        "recipe: no-such-effect",
    ));
    assert!(
        bad.iter()
            .any(|m| m.contains("death.recipe") && m.contains("unknown effect \"no-such-effect\"")),
        "{bad:?}"
    );
}

#[test]
fn yaml_reads_as_the_ts_yaml_package_does() {
    // Only true and false are booleans: `no` stays a string, `yes` is not a boolean.
    let file = content_files()
        .into_iter()
        .find(|f| f.name == "content/weapons.yaml")
        .unwrap();
    let sound = file
        .text
        .lines()
        .find(|l| l.trim_start().starts_with("sound:"))
        .unwrap()
        .trim()
        .to_string();
    let lib =
        with_change("content/weapons.yaml", &sound, "sound: no").unwrap_or_else(|e| panic!("{e}"));
    assert!(lib.weapons.values().any(|w| w.sound == "no"));
    let yes = messages(with_change("content/hero.yaml", "hop: 5,", "hop: yes,"));
    assert!(
        yes.iter()
            .any(|m| m.contains("hero.stride.hop") && m.contains("expected number")),
        "{yes:?}"
    );
    // A merge key is an ordinary key, which the strict schema rejects.
    let merge = messages(with_change(
        "content/hero.yaml",
        "hop: 5,",
        "hop: 5, <<: { x: 1 },",
    ));
    assert!(
        merge.iter().any(|m| m.contains("unknown field \"<<\"")),
        "{merge:?}"
    );
}
