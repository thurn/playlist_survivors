//! `loadContent`: parses and validates content files, merges them by kind, resolves sound
//! palettes' `extends` and checks cross references, as `src/content/loader.ts` does. The library
//! keeps zod's output (`value`), which is what `native/oracle/content.json` hashes.
//!
//! Errors name the file and the path; unlike the TS loader they carry no line numbers.

use indexmap::IndexMap;
use serde::de::DeserializeOwned;
use serde_json::{Map, Value};

use super::schema;
use super::types::{
    EffectRecipe, EnemyDef, FormationDef, HeroDef, PassiveDef, SoundPalette, WeaponDef, WeaponKind,
    ZoneTheme,
};
use super::zod::{self, path_string};

#[derive(Clone, Debug)]
pub struct SourceFile {
    pub name: String,
    pub text: String,
}

#[derive(Clone, Debug, PartialEq)]
pub struct ContentIssue {
    pub file: String,
    pub path: String,
    pub message: String,
}

impl std::fmt::Display for ContentIssue {
    fn fmt(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        if self.path.is_empty() {
            write!(f, "{}: {}", self.file, self.message)
        } else {
            write!(f, "{}: {}: {}", self.file, self.path, self.message)
        }
    }
}

#[derive(Clone, Debug)]
pub struct ContentError {
    pub issues: Vec<ContentIssue>,
}

impl std::fmt::Display for ContentError {
    fn fmt(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        let lines: Vec<String> = self.issues.iter().map(ToString::to_string).collect();
        write!(f, "{}", lines.join("\n"))
    }
}

impl std::error::Error for ContentError {}

/// The loaded content, keyed by id in definition order (a JS Map's insertion order).
#[derive(Clone, Debug)]
pub struct ContentLibrary {
    pub hero: HeroDef,
    pub enemies: IndexMap<String, EnemyDef>,
    pub weapons: IndexMap<String, WeaponDef>,
    pub passives: IndexMap<String, PassiveDef>,
    pub formations: IndexMap<String, FormationDef>,
    pub effects: IndexMap<String, EffectRecipe>,
    pub sound_palettes: IndexMap<String, SoundPalette>,
    pub zones: IndexMap<String, ZoneTheme>,
    /// zod's output as the TS library holds it, Maps as objects keyed by id: what the canonical
    /// content hash covers (`crate::canonical::hash_hex`).
    pub value: Value,
}

const KINDS: [&str; 7] = [
    "enemies",
    "weapons",
    "passives",
    "formations",
    "effects",
    "soundPalettes",
    "zones",
];

/// YAML as the TS loader's `yaml` package reads it (YAML 1.2 core): only `true` and `false` are
/// booleans (`yes` and `no` stay strings), and `<<` is an ordinary key (which the strict schema
/// rejects). One difference remains: `-0` reads as 0, where JS keeps the sign; no content uses it.
fn yaml_options() -> serde_saphyr::Options {
    let mut options = serde_saphyr::Options::default();
    options.strict_booleans = true;
    options.merge_keys = serde_saphyr::MergeKeyPolicy::AsOrdinary;
    options
}

pub fn load_content(files: &[SourceFile]) -> Result<ContentLibrary, ContentError> {
    let mut issues: Vec<ContentIssue> = Vec::new();
    let mut hero: Option<(Value, String)> = None;
    let mut kinds: IndexMap<&str, IndexMap<String, (Value, String)>> =
        KINDS.iter().map(|k| (*k, IndexMap::new())).collect();
    let mut sorted: Vec<&SourceFile> = files.iter().collect();
    // (localeCompare; the content files' names are ASCII.)
    sorted.sort_by(|a, b| a.name.cmp(&b.name));
    let file_schema = schema::content_file();
    for f in sorted {
        let doc: Value = match serde_saphyr::from_str_with_options(&f.text, yaml_options()) {
            Ok(v) => v,
            Err(e) => {
                let first = e.to_string().lines().next().unwrap_or_default().to_string();
                issues.push(ContentIssue {
                    file: f.name.clone(),
                    path: String::new(),
                    message: first,
                });
                continue;
            }
        };
        let data = match zod::parse(&file_schema, &doc) {
            Ok(v) => v,
            Err(errs) => {
                issues.extend(errs.into_iter().map(|i| ContentIssue {
                    file: f.name.clone(),
                    path: path_string(&i.path),
                    message: i.message,
                }));
                continue;
            }
        };
        if let Some(h) = data.get("hero") {
            if let Some((_, first)) = &hero {
                issues.push(ContentIssue {
                    file: f.name.clone(),
                    path: "hero".into(),
                    message: format!("the hero is already defined in {first}"),
                });
            } else {
                hero = Some((h.clone(), f.name.clone()));
            }
        }
        for kind in KINDS {
            let Some(list) = data.get(kind).and_then(Value::as_array) else {
                continue;
            };
            for (i, def) in list.iter().enumerate() {
                let id = def["id"].as_str().unwrap_or_default().to_string();
                let map = kinds.get_mut(kind).unwrap();
                if let Some((_, first)) = map.get(&id) {
                    issues.push(ContentIssue {
                        file: f.name.clone(),
                        path: format!("{kind}[{i}].id"),
                        message: format!("duplicate id \"{id}\" (first defined in {first})"),
                    });
                    continue;
                }
                map.insert(id, (def.clone(), f.name.clone()));
            }
        }
    }

    resolve_extends(kinds.get_mut("soundPalettes").unwrap(), &mut issues);

    if hero.is_none() {
        issues.push(ContentIssue {
            file: "content".into(),
            path: "hero".into(),
            message: "no content file defines the hero".into(),
        });
    }
    cross_references(&kinds, &mut issues);
    if !issues.is_empty() {
        return Err(ContentError { issues });
    }

    let (hero_value, _) = hero.unwrap();
    let mut value = Map::new();
    value.insert("hero".into(), hero_value.clone());
    for kind in KINDS {
        let obj: Map<String, Value> = kinds[kind]
            .iter()
            .map(|(id, (v, _))| (id.clone(), v.clone()))
            .collect();
        value.insert(kind.into(), Value::Object(obj));
    }
    let typed = |kind: &str| -> Result<Vec<(String, Value)>, ContentError> {
        Ok(kinds[kind]
            .iter()
            .map(|(id, (v, _))| (id.clone(), v.clone()))
            .collect())
    };
    Ok(ContentLibrary {
        hero: from(&hero_value, "hero")?,
        enemies: typed_map(typed("enemies")?, "enemies")?,
        weapons: typed_map(typed("weapons")?, "weapons")?,
        passives: typed_map(typed("passives")?, "passives")?,
        formations: typed_map(typed("formations")?, "formations")?,
        effects: typed_map(typed("effects")?, "effects")?,
        sound_palettes: typed_map(typed("soundPalettes")?, "soundPalettes")?,
        zones: typed_map(typed("zones")?, "zones")?,
        value: Value::Object(value),
    })
}

/// A validated value as its Rust type; a failure is a bug in the types (zod already checked it).
fn from<T: DeserializeOwned>(v: &Value, what: &str) -> Result<T, ContentError> {
    serde_json::from_value(v.clone()).map_err(|e| ContentError {
        issues: vec![ContentIssue {
            file: "content".into(),
            path: what.into(),
            message: format!("internal: {e}"),
        }],
    })
}

fn typed_map<T: DeserializeOwned>(
    entries: Vec<(String, Value)>,
    kind: &str,
) -> Result<IndexMap<String, T>, ContentError> {
    entries
        .into_iter()
        .map(|(id, v)| Ok((id.clone(), from(&v, &format!("{kind}.{id}"))?)))
        .collect()
}

/// Sound palettes' `extends`: the child's fields over its parent's, their sounds merged (the
/// child's win), `extends` dropped.
#[allow(clippy::items_after_statements)] // (the TS's visit closure, as a nested fn)
fn resolve_extends(defs: &mut IndexMap<String, (Value, String)>, issues: &mut Vec<ContentIssue>) {
    let keys: Vec<String> = defs.keys().cloned().collect();
    let mut done: std::collections::HashSet<String> = std::collections::HashSet::new();
    fn visit(
        key: &str,
        stack: &mut Vec<String>,
        defs: &mut IndexMap<String, (Value, String)>,
        done: &mut std::collections::HashSet<String>,
        issues: &mut Vec<ContentIssue>,
    ) -> Option<Value> {
        let (d, file) = defs.get(key)?.clone();
        let parent_id = d.get("extends").and_then(Value::as_str).map(str::to_string);
        if done.contains(key) || parent_id.is_none() {
            done.insert(key.to_string());
            return Some(d);
        }
        let parent_id = parent_id.unwrap();
        if stack.iter().any(|s| s == key) {
            let mut chain = stack.clone();
            chain.push(key.to_string());
            issues.push(ContentIssue {
                file,
                path: format!("soundPalettes.{key}.extends"),
                message: format!("circular extends: {}", chain.join(" → ")),
            });
            return Some(d);
        }
        stack.push(key.to_string());
        let parent = visit(&parent_id, stack, defs, done, issues);
        stack.pop();
        let Some(parent) = parent else {
            issues.push(ContentIssue {
                file,
                path: format!("soundPalettes.{key}.extends"),
                message: format!("unknown soundPalettes \"{parent_id}\""),
            });
            done.insert(key.to_string());
            return Some(d);
        };
        // {...parent, ...child, sounds: {...parent.sounds, ...child.sounds}, extends: undefined}
        let mut merged = parent.as_object().cloned().unwrap_or_default();
        for (k, v) in d.as_object().unwrap() {
            merged.insert(k.clone(), v.clone());
        }
        let mut sounds = parent["sounds"].as_object().cloned().unwrap_or_default();
        for (k, v) in d["sounds"].as_object().unwrap() {
            sounds.insert(k.clone(), v.clone());
        }
        merged.insert("sounds".into(), Value::Object(sounds));
        merged.remove("extends");
        let merged = Value::Object(merged);
        defs.insert(key.to_string(), (merged.clone(), file));
        done.insert(key.to_string());
        Some(merged)
    }
    for key in keys {
        visit(&key, &mut Vec::new(), defs, &mut done, issues);
    }
}

fn cross_references(
    kinds: &IndexMap<&str, IndexMap<String, (Value, String)>>,
    issues: &mut Vec<ContentIssue>,
) {
    let has = |kind: &str, id: &str| kinds[kind].contains_key(id);
    let mut refer =
        |kind: &str, def_id: &str, file: &str, field: &str, target: &str, value: Option<&str>| {
            if let Some(v) = value
                && !has(target, v)
            {
                issues.push(ContentIssue {
                    file: file.to_string(),
                    path: format!("{kind}.{def_id}.{field}"),
                    message: format!("unknown {} \"{v}\"", target.trim_end_matches('s')),
                });
            }
        };
    for (k, (e, file)) in &kinds["enemies"] {
        refer(
            "enemies",
            k,
            file,
            "death.recipe",
            "effects",
            e["death"]["recipe"].as_str(),
        );
    }
    let mut weapon_issues = Vec::new();
    for (k, (w, file)) in &kinds["weapons"] {
        refer(
            "weapons",
            k,
            file,
            "effect",
            "effects",
            w["effect"].as_str(),
        );
        let kind: WeaponKind =
            serde_json::from_value(w["kind"].clone()).unwrap_or(WeaponKind::Blast);
        let name = w["kind"].as_str().unwrap_or_default();
        let mut push = |path: String, message: String| {
            weapon_issues.push(ContentIssue {
                file: file.clone(),
                path,
                message,
            });
        };
        if w.get(name).is_none() {
            push(
                format!("weapons.{k}.{name}"),
                format!("{name} weapons need a \"{name}\" block"),
            );
        }
        if kind != WeaponKind::Blast && w.get("hitShape").is_none() {
            push(
                format!("weapons.{k}.hitShape"),
                "weapons need a hitShape".into(),
            );
        }
        if let Some(e) = w.get("evolve") {
            if kind == WeaponKind::Blast {
                push(
                    format!("weapons.{k}.evolve"),
                    "the finale blast can't evolve".into(),
                );
            }
            if kind != WeaponKind::Orbit
                && (e["count"].as_f64() != Some(0.0) || e["radiusScale"].as_f64() != Some(1.0))
            {
                push(
                    format!("weapons.{k}.evolve"),
                    "only orbit weapons evolve their count and radiusScale".into(),
                );
            }
            if kind != WeaponKind::Chain && e["jumps"].as_f64() != Some(0.0) {
                push(
                    format!("weapons.{k}.evolve.jumps"),
                    "only chain weapons evolve their jumps".into(),
                );
            }
        }
    }
    for (k, (f, file)) in &kinds["formations"] {
        refer(
            "formations",
            k,
            file,
            "enemy",
            "enemies",
            f["enemy"].as_str(),
        );
    }
    issues.extend(weapon_issues);
}
