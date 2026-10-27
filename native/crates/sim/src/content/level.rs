//! `loadLevel`: a level script, merged over the level it `extends`, validated (the level schema,
//! the content library and the song map), its sections resolved with field inheritance, as
//! `src/content/loader.ts` does. The level keeps the TS `LevelScript` as a value (`value`), which
//! the oracle's `levelHash` hashes.
//!
//! Errors name the file and the path; unlike the TS loader they carry no line numbers, and an
//! event's line (`eventLines`) is always absent (the reactive level has no events).

use indexmap::IndexMap;
use serde::Deserialize;
use serde_json::{Map, Value, json};

use super::level_schema;
use super::load::{ContentIssue, ContentLibrary, SourceFile};
use super::types::HeroDef;
use super::zod::{self, path_string};
use crate::core::songmap::{Feel, SongMap};

/// Free movement: the sword swings by itself this often (beats) unless a section sets it.
pub const AUTO_SWING_BEATS: f64 = 2.0;

const EVENT_KINDS: [&str; 11] = [
    "levelUp",
    "formation",
    "ring",
    "stampede",
    "threats",
    "boss",
    "banner",
    "titleCard",
    "landmark",
    "timeScale",
    "blast",
];

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum Movement {
    Steps,
    Free,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum TimingGrid {
    Eighth,
    Beat,
    Bar,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct HeavyOnAccents {
    pub min_strength: f64,
    pub label: Option<String>,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
pub struct TimeScale {
    pub from: f64,
    pub to: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Camera {
    pub zoom: f64,
    pub lead: f64,
    pub punch_on_accents: bool,
}

/// A level section with every inherited field resolved.
#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ResolvedSection {
    pub label: String,
    pub bars: [f64; 2],
    pub movement: Movement,
    pub pace: f64,
    pub presses: String,
    pub timing_grid: TimingGrid,
    pub heavy_on_accents: HeavyOnAccents,
    pub zone: String,
    pub time_scale: TimeScale,
    pub calm: f64,
    pub auto_swing_beats: f64,
    pub population: [f64; 2],
    pub roster: IndexMap<String, f64>,
    pub camera: Camera,
    pub sfx_palette: String,
    pub intensity: f64,
    pub banner: Option<String>,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
pub struct ByLevel<T> {
    pub quiet: T,
    pub medium: T,
    pub loud: T,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Population {
    pub start: f64,
    pub per_minute: f64,
    pub max: f64,
    pub level_scale: ByLevel<f64>,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct SectionRule {
    pub jump_db: f64,
    pub min_gap_s: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
pub struct LevelUpXp {
    pub xp: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
pub struct ZoomLead {
    pub zoom: f64,
    pub lead: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct StampedePiece {
    pub enemy: String,
    pub count: f64,
    pub rows: f64,
    pub jump_db: f64,
    pub quiet_s: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct RingPiece {
    pub enemy: String,
    pub count: f64,
    pub gaps: f64,
    pub after_s: f64,
    pub max_beats: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct VolleysPiece {
    pub archers: f64,
    pub every_beats: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct KickLinePiece {
    pub formation: String,
    pub medium_beats: f64,
    pub min_dance_beats: f64,
    pub max_dance_beats: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct BossPiece {
    #[serde(rename = "type")]
    pub kind: String,
    pub after_s: f64,
    pub land_after_s: f64,
    pub slam_beats: f64,
    pub retreat_s: f64,
    pub landmark: Option<String>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct FinalePiece {
    pub blast: String,
    pub landmark: Option<String>,
    pub window_s: f64,
    pub swell: f64,
    pub swell_s: f64,
    pub epilogue_s: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct SetPieces {
    pub gap_s: f64,
    pub stampede: StampedePiece,
    pub ring: RingPiece,
    pub volleys: VolleysPiece,
    pub wedge: String,
    pub kick_line: KickLinePiece,
    pub boss: BossPiece,
    pub finale: FinalePiece,
}

/// A reactive level's director.
#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Director {
    pub population: Population,
    pub sections: Vec<IndexMap<String, f64>>,
    pub section_rule: SectionRule,
    pub level_ups: Vec<LevelUpXp>,
    pub level_up_wait_s: f64,
    pub zone_fallback_s: f64,
    pub smooth_s: f64,
    pub camera: ByLevel<ZoomLead>,
    pub palettes: ByLevel<String>,
    pub palette_hold_s: f64,
    pub calm: ByLevel<f64>,
    pub set_pieces: Option<SetPieces>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Title {
    pub piece: String,
    pub composer: String,
    pub performer: String,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Upgrades {
    pub loadout: Vec<String>,
    pub offer: Vec<String>,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct EnemyOverride {
    pub contact_damage: Option<f64>,
    pub attack_damage: Option<f64>,
}

/// Survivors runs use their own clock across song boundaries.
#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Survivors {
    pub duration_s: f64,
    pub contact_damage: f64,
    pub brute_chicken_chance: f64,
    pub boss_chickens: f64,
    pub maxed_heal: f64,
}

/// The loaded level script.
#[derive(Clone, Debug)]
pub struct LevelScript {
    pub song: String,
    pub seed: f64,
    pub player: String,
    pub title: Title,
    pub reactive: bool,
    pub survivors: Option<Survivors>,
    pub survivor_schedule: Vec<crate::survivors::SpawnMinute>,
    pub director: Option<Director>,
    pub zones: Vec<String>,
    pub sections: Vec<ResolvedSection>,
    /// The level-script events as zod output (a reactive level has none).
    pub events: Vec<Value>,
    pub event_files: Vec<String>,
    pub file: String,
    pub extends: Option<String>,
    /// content/hero.yaml with this level's overrides.
    pub hero: HeroDef,
    pub upgrades: Upgrades,
    pub enemies: IndexMap<String, EnemyOverride>,
    /// The TS `LevelScript` as a value: what the oracle's `levelHash` hashes.
    pub value: Value,
}

/// Finds another level script by name, for `extends`.
pub type LevelResolver<'a> = &'a dyn Fn(&str) -> Option<SourceFile>;

#[derive(Clone, Debug)]
pub struct LevelError {
    pub issues: Vec<ContentIssue>,
}

impl std::fmt::Display for LevelError {
    fn fmt(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        let lines: Vec<String> = self.issues.iter().map(ToString::to_string).collect();
        write!(f, "{}", lines.join("\n"))
    }
}

impl std::error::Error for LevelError {}

fn level_name(file: &str) -> String {
    let base = file.rsplit('/').next().unwrap_or(file);
    base.strip_suffix(".yaml")
        .or_else(|| base.strip_suffix(".yml"))
        .unwrap_or(base)
        .to_string()
}

/// Plain objects merge key by key; anything else in `over` replaces.
fn deep_merge(base: &Value, over: &Value) -> Value {
    match (base, over) {
        (Value::Object(b), Value::Object(o)) => {
            let mut out = b.clone();
            for (k, v) in o {
                let merged = match b.get(k) {
                    Some(bv) => deep_merge(bv, v),
                    None => v.clone(),
                };
                out.insert(k.clone(), merged);
            }
            Value::Object(out)
        }
        _ => over.clone(),
    }
}

/// `over`'s fields replace `base`'s, except that the `nested` objects merge key by key.
fn merge_fields(base: &Value, over: &Map<String, Value>, nested: &[&str]) -> Value {
    let Some(b) = base.as_object() else {
        return Value::Object(over.clone());
    };
    let mut out = b.clone();
    for (k, v) in over {
        out.insert(k.clone(), v.clone());
    }
    for k in nested {
        if let (Some(Value::Object(bk)), Some(Value::Object(ok))) = (b.get(*k), over.get(*k)) {
            let mut m = bk.clone();
            for (kk, vv) in ok {
                m.insert(kk.clone(), vv.clone());
            }
            out.insert((*k).to_string(), Value::Object(m));
        }
    }
    Value::Object(out)
}

/// A canonical string for comparing musical references (keys sorted), as TS's `canonical`, whose
/// leaves are `JSON.stringify`'s: a whole number prints without a fraction (`9.0` is `9`).
fn canonical_ref(v: &Value) -> String {
    match v {
        Value::Object(m) => {
            let mut keys: Vec<&String> = m.keys().collect();
            keys.sort();
            let parts: Vec<String> = keys
                .iter()
                .map(|k| format!("{k}:{}", canonical_ref(&m[*k])))
                .collect();
            format!("{{{}}}", parts.join(","))
        }
        Value::Number(n) => {
            let x = n.as_f64().unwrap_or(f64::NAN);
            if x.fract() == 0.0 && x.abs() < 1e21 {
                // (Exact below 1e21, and -0 prints as 0, as in JS.)
                format!("{}", x as i128)
            } else {
                format!("{x}")
            }
        }
        _ => v.to_string(),
    }
}

/// A level's merged data, and the file each event came from.
struct LevelSource {
    data: Value,
    event_files: Vec<String>,
}

fn parse_yaml(f: &SourceFile, issues: &mut Vec<ContentIssue>) -> Option<Value> {
    let mut options = serde_saphyr::Options::default();
    options.strict_booleans = true;
    options.merge_keys = serde_saphyr::MergeKeyPolicy::AsOrdinary;
    match serde_saphyr::from_str_with_options(&f.text, options) {
        Ok(v) => Some(v),
        Err(e) => {
            let first = e.to_string().lines().next().unwrap_or_default().to_string();
            issues.push(ContentIssue {
                file: f.name.clone(),
                path: String::new(),
                message: first,
            });
            None
        }
    }
}

fn read_level_source(
    f: &SourceFile,
    resolve: Option<LevelResolver>,
    issues: &mut Vec<ContentIssue>,
    stack: &[String],
) -> Option<LevelSource> {
    let data = parse_yaml(f, issues)?;
    let own_events = data
        .get("events")
        .and_then(Value::as_array)
        .map_or(0, Vec::len);
    let own = LevelSource {
        data: data.clone(),
        event_files: vec![f.name.clone(); own_events],
    };
    let Some(parent_name) = data
        .get("extends")
        .and_then(Value::as_str)
        .map(str::to_string)
    else {
        return Some(own);
    };
    let here = |message: String| ContentIssue {
        file: f.name.clone(),
        path: "extends".into(),
        message,
    };
    if stack.contains(&parent_name) {
        let mut chain = stack.to_vec();
        chain.push(parent_name);
        issues.push(here(format!("circular extends: {}", chain.join(" → "))));
        return None;
    }
    let Some(pf) = resolve.and_then(|r| r(&parent_name)) else {
        issues.push(here(format!("unknown level \"{parent_name}\"")));
        return None;
    };
    let mut next = stack.to_vec();
    next.push(parent_name);
    let parent = read_level_source(&pf, resolve, issues, &next)?;
    if !parent.data.is_object() {
        return Some(parent);
    }
    Some(merge_level(&parent, &data, &f.name, issues))
}

fn merge_level(
    parent: &LevelSource,
    child: &Value,
    child_file: &str,
    issues: &mut Vec<ContentIssue>,
) -> LevelSource {
    let pd = parent.data.as_object().unwrap();
    let cd = child.as_object().cloned().unwrap_or_default();
    let mut out = pd.clone();
    for (k, v) in &cd {
        if k == "sections" || k == "events" {
            continue;
        }
        let merged = if k == "hero" {
            deep_merge(pd.get(k).unwrap_or(&Value::Null), v)
        } else {
            v.clone()
        };
        out.insert(k.clone(), merged);
    }
    // Sections: by label, field by field.
    let mut secs: Vec<Value> = pd
        .get("sections")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let child_secs: Vec<Value> = cd
        .get("sections")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    for (ci, cs) in child_secs.iter().enumerate() {
        let label = cs.get("section").and_then(Value::as_str);
        let idx = secs
            .iter()
            .position(|ps| ps.get("section").and_then(Value::as_str) == label && label.is_some());
        if let (Some(_), Some(i), Some(obj)) = (label, idx, cs.as_object()) {
            secs[i] = merge_fields(&secs[i], obj, &["camera", "heavyOnAccents"]);
        } else {
            let known: Vec<&str> = secs
                .iter()
                .filter_map(|ps| ps.get("section").and_then(Value::as_str))
                .collect();
            issues.push(ContentIssue {
                file: child_file.to_string(),
                path: format!("sections[{ci}].section"),
                message: format!(
                    "unknown section \"{}\" in the level this extends ({})",
                    label.unwrap_or("undefined"),
                    known.join(", ")
                ),
            });
        }
    }
    out.insert("sections".into(), Value::Array(secs));
    // Events: by `at` and kind; the rest are new.
    let mut evs: Vec<Value> = pd
        .get("events")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let mut files = parent.event_files.clone();
    let mut from_child: Vec<bool> = vec![false; evs.len()];
    for ce in cd
        .get("events")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default()
    {
        let Some(obj) = ce.as_object() else { continue };
        let kind = EVENT_KINDS.iter().find(|k| obj.contains_key(**k));
        let idx = evs.iter().enumerate().position(|(i, pe)| {
            !from_child[i]
                && pe.is_object()
                && canonical_ref(pe.get("at").unwrap_or(&Value::Null))
                    == canonical_ref(obj.get("at").unwrap_or(&Value::Null))
                && kind.is_none_or(|k| pe.get(*k).is_some())
        });
        if let Some(i) = idx {
            evs[i] = merge_fields(&evs[i], obj, &[]);
            from_child[i] = true;
            files[i] = child_file.to_string();
        } else {
            evs.push(ce.clone());
            from_child.push(true);
            files.push(child_file.to_string());
        }
    }
    out.insert("events".into(), Value::Array(evs));
    LevelSource {
        data: Value::Object(out),
        event_files: files,
    }
}

fn typed<T: serde::de::DeserializeOwned>(
    file: &str,
    what: &str,
    x: &Value,
) -> Result<T, LevelError> {
    serde_json::from_value(x.clone()).map_err(|e| LevelError {
        issues: vec![issue(file, what, format!("internal: {e}"))],
    })
}

fn issue(file: &str, path: &str, message: impl Into<String>) -> ContentIssue {
    ContentIssue {
        file: file.to_string(),
        path: path.to_string(),
        message: message.into(),
    }
}

/// Why a musical reference cannot resolve against this song map, or None.
pub fn ref_error(song: &SongMap, r: &Value) -> Option<String> {
    let last_bar = song.sections.last()?.bars[1];
    if let Some(bar) = r.get("bar").and_then(Value::as_f64) {
        return (bar < 1.0 || bar > last_bar + 1.0)
            .then(|| format!("bar {bar} is outside the song (bars 1-{last_bar})"));
    }
    if let Some(section) = r.get("section").and_then(Value::as_str) {
        let Some(sec) = song.sections.iter().find(|s| s.label == section) else {
            return Some(format!("unknown song-map section \"{section}\""));
        };
        if let Some(phrase) = r.get("phrase").and_then(Value::as_f64) {
            let n = song
                .phrases
                .iter()
                .filter(|p| p[0] >= sec.bars[0] && p[1] <= sec.bars[1])
                .count();
            if phrase > n as f64 {
                return Some(format!("section \"{section}\" has only {n} phrases"));
            }
        } else if r.get("point").and_then(Value::as_str) == Some("finalAccent") {
            return Some("finalAccent needs a phrase".into());
        }
        return None;
    }
    let accent = r.get("accent").and_then(Value::as_str).unwrap_or_default();
    let matching = song
        .accents
        .iter()
        .filter(|a| a.label.as_deref() == Some(accent))
        .count();
    if matching == 0 {
        return Some(format!("no accents labelled \"{accent}\""));
    }
    let n = r
        .get("fromEnd")
        .or_else(|| r.get("index"))
        .and_then(Value::as_f64)
        .unwrap_or(1.0);
    (n > matching as f64).then(|| format!("only {matching} accents labelled \"{accent}\""))
}

fn kind_has(lib: &ContentLibrary, kind: &str, id: &str) -> bool {
    match kind {
        "enemies" => lib.enemies.contains_key(id),
        "weapons" => lib.weapons.contains_key(id),
        "passives" => lib.passives.contains_key(id),
        "formations" => lib.formations.contains_key(id),
        "effects" => lib.effects.contains_key(id),
        "soundPalettes" => lib.sound_palettes.contains_key(id),
        "zones" => lib.zones.contains_key(id),
        _ => false,
    }
}

/// Validates a level script against the schema, the content library and the song map, and resolves
/// its sections with field inheritance.
#[allow(clippy::too_many_lines)]
pub fn load_level(
    f: &SourceFile,
    lib: &ContentLibrary,
    song: &SongMap,
    resolve: Option<LevelResolver>,
) -> Result<LevelScript, LevelError> {
    let mut issues: Vec<ContentIssue> = Vec::new();
    let file = f.name.clone();
    let src = read_level_source(f, resolve, &mut issues, &[level_name(&f.name)]);
    let Some(src) = src.filter(|_| issues.is_empty()) else {
        return Err(LevelError { issues });
    };
    let data = match zod::parse(&level_schema::level_script(), &src.data) {
        Ok(d) => d,
        Err(errs) => {
            return Err(LevelError {
                issues: errs
                    .into_iter()
                    .map(|i| issue(&file, &path_string(&i.path), i.message))
                    .collect(),
            });
        }
    };
    let at = |path: &str, message: String| issue(&file, path, message);
    let need = |issues: &mut Vec<ContentIssue>, path: &str, kind: &str, value: Option<&str>| {
        if let Some(v) = value
            && !kind_has(lib, kind, v)
        {
            issues.push(at(
                path,
                format!("unknown {} \"{v}\"", kind.trim_end_matches('s')),
            ));
        }
    };

    // The hero: content/hero.yaml with this level's overrides, checked against the hero schema.
    let lib_hero = &lib.value["hero"];
    let mut level_hero = lib_hero.clone();
    if let Some(h) = data.get("hero") {
        match zod::parse(&level_schema::hero(), &deep_merge(lib_hero, h)) {
            Ok(merged) => level_hero = merged,
            Err(errs) => issues.extend(errs.into_iter().map(|i| {
                let rest = path_string(&i.path);
                at(
                    &if rest.is_empty() {
                        "hero".to_string()
                    } else {
                        format!("hero.{rest}")
                    },
                    i.message,
                )
            })),
        }
    }

    let reactive = data["reactive"].as_bool().unwrap_or(false);
    if data.get("survivors").is_some() && !reactive {
        issues.push(at("survivors", "survivors needs a reactive level".into()));
    }
    if data["song"].as_str() != Some(song.song.as_str()) {
        issues.push(at(
            "song",
            format!(
                "level is for song \"{}\" but the song map is \"{}\"",
                data["song"].as_str().unwrap_or_default(),
                song.song
            ),
        ));
    }
    let zones: Vec<String> = data["zones"]
        .as_array()
        .unwrap()
        .iter()
        .map(|z| z.as_str().unwrap().to_string())
        .collect();
    for (i, z) in zones.iter().enumerate() {
        need(&mut issues, &format!("zones[{i}]"), "zones", Some(z));
    }

    let last_bar = song.sections.last().map_or(0.0, |s| s.bars[1]);
    let mut sections: Vec<Value> = Vec::new();
    let level_sections = data["sections"].as_array().unwrap().clone();
    let mut prev: Option<Map<String, Value>> = None;
    for (i, s) in level_sections.iter().enumerate() {
        let path = format!("sections[{i}]");
        let opt_str = |k: &str| s.get(k).and_then(Value::as_str);
        // (TS tests `s.section` for truth: an empty label is no label.)
        let section_name = opt_str("section").filter(|x| !x.is_empty());
        let (label, bars): (String, [f64; 2]) = if opt_str("bars") == Some("all") {
            if !reactive || song.sections.len() != 1 {
                issues.push(at(
                    &format!("{path}.bars"),
                    "bars \"all\" is only for a reactive level's one section".into(),
                ));
                continue;
            }
            let sec = &song.sections[0];
            if let Some(name) = section_name
                && name != sec.label
            {
                issues.push(at(
                    &format!("{path}.section"),
                    format!("bars all are section \"{}\", not \"{name}\"", sec.label),
                ));
            }
            (sec.label.clone(), sec.bars)
        } else if let Some(range) = opt_str("bars") {
            let (a, b) = range.split_once('-').unwrap();
            let (a, b): (f64, f64) = (a.parse().unwrap(), b.parse().unwrap());
            if a < 1.0 || b > last_bar || a > b {
                issues.push(at(
                    &format!("{path}.bars"),
                    format!("bar range {range} is outside the song (bars 1-{last_bar})"),
                ));
                continue;
            }
            let Some(sec) = song
                .sections
                .iter()
                .find(|x| x.bars[0] == a && x.bars[1] == b)
            else {
                let have: Vec<String> = song
                    .sections
                    .iter()
                    .map(|x| format!("{} {}-{}", x.label, x.bars[0], x.bars[1]))
                    .collect();
                issues.push(at(
                    &format!("{path}.bars"),
                    format!(
                        "bar range {range} does not match a song-map section ({})",
                        have.join(", ")
                    ),
                ));
                continue;
            };
            if let Some(name) = section_name
                && name != sec.label
            {
                issues.push(at(
                    &format!("{path}.section"),
                    format!("bars {range} are section \"{}\", not \"{name}\"", sec.label),
                ));
            }
            (sec.label.clone(), [a, b])
        } else if let Some(name) = section_name {
            let Some(sec) = song.sections.iter().find(|x| x.label == name) else {
                issues.push(at(
                    &format!("{path}.section"),
                    format!("unknown song-map section \"{name}\""),
                ));
                continue;
            };
            (sec.label.clone(), sec.bars)
        } else {
            issues.push(at(&path, "a section needs bars or section".into()));
            continue;
        };
        need(
            &mut issues,
            &format!("{path}.zone"),
            "zones",
            opt_str("zone"),
        );
        need(
            &mut issues,
            &format!("{path}.sfxPalette"),
            "soundPalettes",
            opt_str("sfxPalette"),
        );
        if let Some(roster) = s.get("roster").and_then(Value::as_object) {
            for t in roster.keys() {
                need(
                    &mut issues,
                    &format!("{path}.roster.{t}"),
                    "enemies",
                    Some(t),
                );
            }
        }
        if let Some(z) = opt_str("zone")
            && !zones.iter().any(|x| x == z)
        {
            issues.push(at(
                &format!("{path}.zone"),
                format!("zone \"{z}\" is not in the level's zones list"),
            ));
        }
        let inherit = |k: &str| {
            s.get(k)
                .cloned()
                .or_else(|| prev.as_ref().and_then(|p| p.get(k).cloned()))
        };
        let zone = inherit("zone");
        let roster = inherit("roster");
        let sfx = inherit("sfxPalette");
        // (TS tests these for truth, so a later section inheriting the first's "" is told again.)
        let unset = |v: &Option<Value>| v.as_ref().is_none_or(|x| x.as_str() == Some(""));
        if unset(&zone) {
            issues.push(at(&path, "the first section must set zone".into()));
        }
        if roster.is_none() && !reactive {
            issues.push(at(&path, "the first section must set roster".into()));
        }
        if unset(&sfx) {
            issues.push(at(&path, "the first section must set sfxPalette".into()));
        }
        let time_scale = match s.get("timeScale") {
            None => json!({ "from": 1, "to": 1 }),
            Some(v) if v.is_number() => json!({ "from": v, "to": v }),
            Some(v) => v.clone(),
        };
        let mut camera = Map::new();
        camera.insert("zoom".into(), json!(1));
        camera.insert("lead".into(), json!(60));
        camera.insert("punchOnAccents".into(), json!(false));
        for src_cam in [prev.as_ref().and_then(|p| p.get("camera")), s.get("camera")]
            .into_iter()
            .flatten()
        {
            for (k, v) in src_cam.as_object().unwrap() {
                camera.insert(k.clone(), v.clone());
            }
        }
        let feel = song
            .sections
            .iter()
            .find(|x| x.label == label)
            .map_or(Feel::Pulsed, |x| x.feel);
        let movement = opt_str("movement")
            .map_or(if feel == Feel::Free { "free" } else { "steps" }, |m| m)
            .to_string();
        let pace = inherit("pace");
        let presses = inherit("presses");
        if pace.is_none() {
            issues.push(at(&path, "the first section must set pace".into()));
        }
        if presses.is_none() && movement == "steps" {
            issues.push(at(
                &path,
                "a step section needs presses (set here or on an earlier section)".into(),
            ));
        }
        if s.get("population").is_none() && !reactive {
            issues.push(at(
                &format!("{path}.population"),
                "required field is missing".into(),
            ));
        }
        let mut heavy = Map::new();
        heavy.insert(
            "minStrength".into(),
            s.get("heavyOnAccents")
                .and_then(|h| h.get("minStrength"))
                .cloned()
                .unwrap_or(json!(0.7)),
        );
        if let Some(l) = s.get("heavyOnAccents").and_then(|h| h.get("label")) {
            heavy.insert("label".into(), l.clone());
        }
        let mut r = Map::new();
        r.insert("label".into(), json!(label));
        r.insert("bars".into(), json!(bars));
        r.insert("movement".into(), json!(movement));
        r.insert("pace".into(), pace.unwrap_or(json!(0)));
        r.insert("presses".into(), presses.unwrap_or(json!("x")));
        r.insert(
            "timingGrid".into(),
            s.get("timingGrid").cloned().unwrap_or(json!("beat")),
        );
        r.insert("heavyOnAccents".into(), Value::Object(heavy));
        r.insert("zone".into(), zone.unwrap_or(json!("")));
        r.insert("timeScale".into(), time_scale);
        r.insert("calm".into(), s.get("calm").cloned().unwrap_or(json!(0)));
        r.insert(
            "autoSwingBeats".into(),
            s.get("autoSwingBeats")
                .cloned()
                .unwrap_or(json!(AUTO_SWING_BEATS)),
        );
        r.insert(
            "population".into(),
            s.get("population").cloned().unwrap_or(json!([0, 0])),
        );
        r.insert("roster".into(), roster.unwrap_or(json!({})));
        r.insert("camera".into(), Value::Object(camera));
        r.insert("sfxPalette".into(), sfx.unwrap_or(json!("")));
        r.insert(
            "intensity".into(),
            s.get("intensity").cloned().unwrap_or(json!(1)),
        );
        if let Some(b) = s.get("banner") {
            r.insert("banner".into(), b.clone());
        }
        sections.push(Value::Object(r.clone()));
        prev = Some(r);
    }

    // Sections must cover the song in order.
    let expected: Vec<&str> = song.sections.iter().map(|x| x.label.as_str()).collect();
    let got: Vec<&str> = sections
        .iter()
        .map(|x| x["label"].as_str().unwrap())
        .collect();
    if issues.is_empty() && got != expected {
        issues.push(at(
            "sections",
            format!(
                "sections must cover every song-map section in order: expected {}; got {}",
                expected.join(", "),
                got.join(", ")
            ),
        ));
    }

    let events = data["events"].as_array().unwrap().clone();
    for (i, e) in events.iter().enumerate() {
        let path = format!("events[{i}]");
        let check_ref = |issues: &mut Vec<ContentIssue>, p: &str, r: &Value| {
            if let Some(err) = ref_error(song, r) {
                issues.push(at(p, err));
            }
        };
        check_ref(&mut issues, &format!("{path}.at"), &e["at"]);
        need(
            &mut issues,
            &format!("{path}.formation"),
            "formations",
            e.get("formation").and_then(Value::as_str),
        );
        if let Some(ring) = e.get("ring") {
            need(
                &mut issues,
                &format!("{path}.ring.enemy"),
                "enemies",
                ring["enemy"].as_str(),
            );
            check_ref(
                &mut issues,
                &format!("{path}.ring.closesOn"),
                &ring["closesOn"],
            );
        }
        if let Some(st) = e.get("stampede") {
            need(
                &mut issues,
                &format!("{path}.stampede.enemy"),
                "enemies",
                st["enemy"].as_str(),
            );
        }
        need(
            &mut issues,
            &format!("{path}.blast"),
            "weapons",
            e.get("blast").and_then(Value::as_str),
        );
        if let Some(b) = e.get("boss") {
            need(
                &mut issues,
                &format!("{path}.boss.type"),
                "enemies",
                b["type"].as_str(),
            );
            check_ref(
                &mut issues,
                &format!("{path}.boss.slamsUntil"),
                &b["slamsUntil"],
            );
        }
        if let Some(t) = e.get("threats") {
            check_ref(&mut issues, &format!("{path}.threats.until"), &t["until"]);
        }
        if e.get("levelUp").is_some() && e.get("xp").is_none() {
            issues.push(at(
                &path,
                "a levelUp needs xp (the experience it takes)".into(),
            ));
        }
        if e.get("xp").is_some() && e.get("levelUp").is_none() {
            issues.push(at(
                &format!("{path}.xp"),
                "xp only applies to a levelUp".into(),
            ));
        }
        if let Some(t) = e.get("titleCard") {
            check_ref(&mut issues, &format!("{path}.titleCard.until"), &t["until"]);
        }
        if !EVENT_KINDS.iter().any(|k| e.get(*k).is_some()) {
            issues.push(at(&path, "event does nothing (add levelUp, formation, ring, stampede, threats, boss, banner, titleCard, landmark, timeScale or blast)".into()));
        }
    }

    if let Some(en) = data.get("enemies").and_then(Value::as_object) {
        for k in en.keys() {
            need(&mut issues, &format!("enemies.{k}"), "enemies", Some(k));
        }
    }

    // Reactive levels: a director, one section and no level-script events.
    if data.get("title").is_none() && !reactive {
        issues.push(at("", "a level needs a title".into()));
    }
    if data.get("director").is_some() && !reactive {
        issues.push(at(
            "director",
            "only a reactive level (reactive: true) has a director".into(),
        ));
    }
    if data.get("survivors").is_some() && data["director"].get("setPieces").is_none() {
        issues.push(at(
            "survivors",
            "survivors needs director.setPieces for its finale".into(),
        ));
    }
    if reactive {
        if data.get("director").is_none() {
            issues.push(at("", "a reactive level needs a director".into()));
        }
        if level_sections.len() != 1 {
            issues.push(at("sections", "a reactive level has one section".into()));
        }
        if !events.is_empty() {
            issues.push(at(
                "events",
                "a reactive level has no level-script events".into(),
            ));
        }
        for (i, sec) in level_sections.iter().enumerate() {
            for k in [
                "timeScale",
                "timingGrid",
                "movement",
                "autoSwingBeats",
                "population",
                "roster",
                "heavyOnAccents",
            ] {
                let Some(v) = sec.get(k) else { continue };
                let the_default =
                    (k == "timingGrid" && v == "beat") || (k == "movement" && v == "steps");
                if !the_default {
                    issues.push(at(&format!("sections[{i}].{k}"), format!("a reactive level's section has no {k} (its director and the committed beats decide it)")));
                }
            }
        }
        if let Some(d) = data.get("director") {
            for (i, r) in d["sections"].as_array().unwrap().iter().enumerate() {
                for t in r.as_object().unwrap().keys() {
                    need(
                        &mut issues,
                        &format!("director.sections[{i}].{t}"),
                        "enemies",
                        Some(t),
                    );
                }
            }
            for (lv, pal) in d["palettes"].as_object().unwrap() {
                need(
                    &mut issues,
                    &format!("director.palettes.{lv}"),
                    "soundPalettes",
                    pal.as_str(),
                );
            }
            if let Some(sp) = d.get("setPieces") {
                need(
                    &mut issues,
                    "director.setPieces.stampede.enemy",
                    "enemies",
                    sp["stampede"]["enemy"].as_str(),
                );
                need(
                    &mut issues,
                    "director.setPieces.ring.enemy",
                    "enemies",
                    sp["ring"]["enemy"].as_str(),
                );
                need(
                    &mut issues,
                    "director.setPieces.wedge",
                    "formations",
                    sp["wedge"].as_str(),
                );
                need(
                    &mut issues,
                    "director.setPieces.kickLine.formation",
                    "formations",
                    sp["kickLine"]["formation"].as_str(),
                );
                need(
                    &mut issues,
                    "director.setPieces.boss.type",
                    "enemies",
                    sp["boss"]["type"].as_str(),
                );
                let blast = sp["finale"]["blast"].as_str().unwrap_or_default();
                need(
                    &mut issues,
                    "director.setPieces.finale.blast",
                    "weapons",
                    Some(blast),
                );
                if let Some(w) = lib.weapons.get(blast)
                    && w.kind != super::types::WeaponKind::Blast
                {
                    issues.push(at(
                        "director.setPieces.finale.blast",
                        "the finale needs a blast weapon".into(),
                    ));
                }
            }
        }
    }

    // The upgrade pool's weapons: known, not the finale blast, and each named once.
    let mut named: Vec<String> = Vec::new();
    for key in ["loadout", "offer"] {
        let list = data
            .get("upgrades")
            .and_then(|u| u.get(key))
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default();
        for (i, w) in list.iter().enumerate() {
            let w = w.as_str().unwrap();
            let path = format!("upgrades.{key}[{i}]");
            need(&mut issues, &path, "weapons", Some(w));
            if lib
                .weapons
                .get(w)
                .is_some_and(|x| x.kind == super::types::WeaponKind::Blast)
            {
                issues.push(at(
                    &path,
                    format!("the finale blast \"{w}\" can't be in the upgrade pool"),
                ));
            }
            if named.iter().any(|n| n == w) {
                issues.push(at(
                    &path,
                    format!("weapon \"{w}\" is named twice in upgrades"),
                ));
            }
            named.push(w.to_string());
        }
    }

    if let Some(schedule) = data
        .get("survivors")
        .and_then(|s| s.get("schedule"))
        .and_then(Value::as_array)
    {
        for (i, minute) in schedule.iter().enumerate() {
            if minute["minute"].as_f64() != Some(i as f64) {
                issues.push(at(
                    "survivors.schedule",
                    "minutes must be contiguous from zero".into(),
                ));
            }
            if i == 0
                && ["enemies", "minimum", "frequency"]
                    .iter()
                    .any(|k| minute.get(k).is_none())
            {
                issues.push(at(
                    "survivors.schedule",
                    "minute zero needs enemies, minimum and frequency".into(),
                ));
            }
            for t in minute["treasure"].as_array().into_iter().flatten() {
                if t["chances"].as_array().is_none_or(|c| c.len() != 3) {
                    issues.push(at(
                        "survivors.schedule",
                        "treasure needs three tier chances".into(),
                    ));
                }
            }
            for key in ["enemies", "bosses"] {
                for id in minute
                    .get(key)
                    .and_then(Value::as_array)
                    .into_iter()
                    .flatten()
                {
                    if !lib.enemies.contains_key(id.as_str().unwrap_or_default()) {
                        issues.push(at("survivors.schedule", format!("unknown enemy {id}")));
                    }
                }
            }
        }
    }
    if !issues.is_empty() {
        return Err(LevelError { issues });
    }

    // The TS LevelScript, as a value.
    let mut v = Map::new();
    v.insert("song".into(), data["song"].clone());
    v.insert("seed".into(), data["seed"].clone());
    v.insert("player".into(), data["player"].clone());
    v.insert(
        "title".into(),
        data.get("title")
            .cloned()
            .unwrap_or(json!({ "piece": "", "composer": "", "performer": "" })),
    );
    v.insert("reactive".into(), json!(reactive));
    if let Some(s) = data.get("survivors") {
        v.insert("survivors".into(), s.clone());
    }
    if let Some(d) = data.get("director") {
        v.insert("director".into(), d.clone());
    }
    v.insert("zones".into(), data["zones"].clone());
    v.insert("sections".into(), Value::Array(sections.clone()));
    v.insert("events".into(), Value::Array(events.clone()));
    v.insert("eventFiles".into(), json!(src.event_files));
    v.insert(
        "eventLines".into(),
        Value::Array(vec![Value::Null; events.len()]),
    );
    v.insert("file".into(), json!(file));
    if let Some(x) = data.get("extends") {
        v.insert("extends".into(), x.clone());
    }
    v.insert("hero".into(), level_hero.clone());
    let upgrades = json!({
        "loadout": data.get("upgrades").and_then(|u| u.get("loadout")).cloned().unwrap_or(json!([])),
        "offer": data.get("upgrades").and_then(|u| u.get("offer")).cloned().unwrap_or(json!([])),
    });
    v.insert("upgrades".into(), upgrades.clone());
    let enemies = data.get("enemies").cloned().unwrap_or(json!({}));
    v.insert("enemies".into(), enemies.clone());
    let value = Value::Object(v);

    Ok(LevelScript {
        song: data["song"].as_str().unwrap().to_string(),
        seed: data["seed"].as_f64().unwrap(),
        player: data["player"].as_str().unwrap().to_string(),
        title: typed(&file, "title", &value["title"])?,
        reactive,
        survivors: data
            .get("survivors")
            .map(|s| typed(&file, "survivors", s))
            .transpose()?,
        survivor_schedule: data
            .get("survivors")
            .and_then(|s| s.get("schedule"))
            .map(|s| typed(&file, "survivors.schedule", s))
            .transpose()?
            .unwrap_or_default(),
        director: data
            .get("director")
            .map(|d| typed(&file, "director", d))
            .transpose()?,
        zones,
        sections: typed(&file, "sections", &Value::Array(sections))?,
        events,
        event_files: src.event_files,
        file: file.clone(),
        extends: data
            .get("extends")
            .and_then(Value::as_str)
            .map(str::to_string),
        hero: typed(&file, "hero", &level_hero)?,
        upgrades: typed(&file, "upgrades", &upgrades)?,
        enemies: typed(&file, "enemies", &enemies)?,
        value,
    })
}
