//! `src/content/schema.ts`'s level script schemas (`levelScript`, its sections, events, musical
//! references and the reactive director), field for field.

use serde_json::json;

use super::schema::{
    array, array1, bool, en, hero_def, id, int_min, int_positive, is_id, lit, min, min_max, num,
    object, positive, string,
};
use super::zod::{Field, Num, Schema, Str, def, opt, req};

fn int() -> Schema {
    Schema::Number(Num {
        int: true,
        ..Num::default()
    })
}

fn int_range(lo: f64, hi: f64) -> Schema {
    Schema::Number(Num {
        int: true,
        min: Some(lo),
        max: Some(hi),
        ..Num::default()
    })
}

fn unit_max(max: f64) -> Schema {
    min_max(0.0, max)
}

/// `^(\d+-\d+|all)$`
fn is_bar_range(s: &str) -> bool {
    if s == "all" {
        return true;
    }
    let Some((a, b)) = s.split_once('-') else {
        return false;
    };
    !a.is_empty()
        && !b.is_empty()
        && a.bytes().all(|c| c.is_ascii_digit())
        && b.bytes().all(|c| c.is_ascii_digit())
}

/// `^[x.]+$`
fn is_presses(s: &str) -> bool {
    !s.is_empty() && s.bytes().all(|c| c == b'x' || c == b'.')
}

/// `^\d+ (bars?|beats?)$`
fn is_every(s: &str) -> bool {
    let Some((n, unit)) = s.split_once(' ') else {
        return false;
    };
    !n.is_empty()
        && n.bytes().all(|c| c.is_ascii_digit())
        && matches!(unit, "bar" | "bars" | "beat" | "beats")
}

pub fn musical_ref() -> Schema {
    Schema::Union(vec![
        object(vec![
            req("bar", int()),
            def("beat", min(1.0), json!(1)),
            opt("offsetBeats", num()),
        ]),
        object(vec![
            req("section", string()),
            opt("phrase", int_positive()),
            req("point", en(&["start", "end", "finalAccent"])),
            opt("offsetBeats", num()),
        ]),
        object(vec![
            req("accent", string()),
            opt("fromEnd", int_positive()),
            opt("index", int_positive()),
            opt("offsetBeats", num()),
        ]),
    ])
}

fn time_scale() -> Schema {
    Schema::Union(vec![
        min_max(0.25, 1.0),
        object(vec![
            req("from", min_max(0.25, 1.0)),
            req("to", min_max(0.25, 1.0)),
        ]),
    ])
}

fn roster() -> Schema {
    Schema::Record {
        key: None,
        value: Box::new(positive()),
    }
}

pub fn level_section() -> Schema {
    object(vec![
        opt(
            "bars",
            Schema::String(Str {
                check: Some((
                    is_bar_range,
                    "bar ranges look like \"9-16\" (or \"all\", for a reactive level's one section)",
                )),
            }),
        ),
        opt("section", string()),
        opt("movement", en(&["steps", "free"])),
        opt("pace", min(0.0)),
        opt(
            "presses",
            Schema::String(Str {
                check: Some((is_presses, "presses use x and .")),
            }),
        ),
        opt("timingGrid", en(&["eighth", "beat", "bar"])),
        opt(
            "heavyOnAccents",
            object(vec![
                def("minStrength", unit_max(1.0), json!(0.7)),
                opt("label", string()),
            ]),
        ),
        opt("zone", id()),
        opt("timeScale", time_scale()),
        opt("calm", unit_max(0.9)),
        opt("autoSwingBeats", int_range(1.0, 8.0)),
        opt(
            "population",
            Schema::Tuple(vec![int_range(0.0, 520.0), int_range(0.0, 520.0)]),
        ),
        opt("roster", roster()),
        opt(
            "camera",
            object(vec![
                opt("zoom", min_max(0.7, 1.4)),
                opt("lead", num()),
                opt("punchOnAccents", bool()),
            ]),
        ),
        opt("sfxPalette", id()),
        opt("intensity", unit_max(1.0)),
        opt("banner", string()),
    ])
}

pub fn level_event() -> Schema {
    object(vec![
        req("at", musical_ref()),
        opt("levelUp", lit("offer")),
        opt("xp", positive()),
        opt("formation", id()),
        opt(
            "ring",
            object(vec![
                req("enemy", id()),
                req("count", int_positive()),
                def("gaps", int_range(0.0, 6.0), json!(3)),
                req("closesOn", musical_ref()),
            ]),
        ),
        opt(
            "stampede",
            object(vec![
                req("enemy", id()),
                req("count", int_positive()),
                def("rows", int_range(1.0, 4.0), json!(3)),
            ]),
        ),
        opt(
            "threats",
            object(vec![
                req("kind", en(&["archer-volley"])),
                req(
                    "every",
                    Schema::String(Str {
                        check: Some((
                            is_every,
                            "invalid string: must match pattern /^\\d+ (bars?|beats?)$/",
                        )),
                    }),
                ),
                req("until", musical_ref()),
                def("archers", int_positive(), json!(2)),
            ]),
        ),
        opt(
            "boss",
            object(vec![
                req("type", id()),
                req("slams", int_min(0.0)),
                req("slamsUntil", musical_ref()),
            ]),
        ),
        opt("banner", string()),
        opt("titleCard", object(vec![req("until", musical_ref())])),
        opt("landmark", id()),
        opt("timeScale", min_max(0.25, 1.0)),
        opt("blast", id()),
    ])
}

/// The reactive players and the rest, as `PLAYER_NAMES`.
pub const PLAYER_NAMES: [&str; 16] = [
    "perfect",
    "skilled",
    "casual",
    "moderate",
    "eighths",
    "human",
    "late",
    "holder",
    "mash2",
    "mash4",
    "mash8",
    "reactive-moderate",
    "reactive-human",
    "reactive-half",
    "reactive-offbeat",
    "reactive-skilled",
];

fn by_level(value: impl Fn() -> Schema) -> Schema {
    object(vec![
        req("quiet", value()),
        req("medium", value()),
        req("loud", value()),
    ])
}

fn set_pieces() -> Schema {
    object(vec![
        def("gapS", positive(), json!(6)),
        req(
            "stampede",
            object(vec![
                req("enemy", id()),
                req("count", int_positive()),
                def("rows", int_range(1.0, 4.0), json!(2)),
                def("jumpDb", positive(), json!(6)),
                def("quietS", positive(), json!(8)),
            ]),
        ),
        req(
            "ring",
            object(vec![
                req("enemy", id()),
                req("count", int_positive()),
                def("gaps", int_range(0.0, 6.0), json!(3)),
                def("afterS", positive(), json!(3)),
                def("maxBeats", int_positive(), json!(12)),
            ]),
        ),
        req(
            "volleys",
            object(vec![
                def("archers", int_positive(), json!(2)),
                def("everyBeats", int_positive(), json!(4)),
            ]),
        ),
        req("wedge", id()),
        req(
            "kickLine",
            object(vec![
                req("formation", id()),
                def("mediumBeats", int_positive(), json!(16)),
                def("minDanceBeats", int_positive(), json!(8)),
                def("maxDanceBeats", int_positive(), json!(32)),
            ]),
        ),
        req(
            "boss",
            object(vec![
                req("type", id()),
                def("afterS", min(0.0), json!(90)),
                def("landAfterS", min(0.0), json!(0.6)),
                def("slamBeats", int_positive(), json!(4)),
                def("retreatS", positive(), json!(90)),
                opt("landmark", id()),
            ]),
        ),
        req(
            "finale",
            object(vec![
                req("blast", id()),
                opt("landmark", id()),
                def("windowS", positive(), json!(10)),
                def("swell", min_max(0.25, 1.0), json!(0.4)),
                def("swellS", positive(), json!(1.5)),
                def("epilogueS", positive(), json!(3)),
            ]),
        ),
    ])
}

pub fn director() -> Schema {
    object(vec![
        req(
            "population",
            object(vec![
                req("start", min(0.0)),
                req("perMinute", min(0.0)),
                req("max", int_range(0.0, 520.0)),
                req("levelScale", by_level(|| min(0.0))),
            ]),
        ),
        req("sections", array1(roster())),
        def(
            "sectionRule",
            object(vec![
                def("jumpDb", positive(), json!(6)),
                def("minGapS", positive(), json!(8)),
            ]),
            json!({ "jumpDb": 6, "minGapS": 8 }),
        ),
        def(
            "levelUps",
            array(object(vec![req("xp", positive())])),
            json!([]),
        ),
        def("levelUpWaitS", positive(), json!(20)),
        def("zoneFallbackS", positive(), json!(60)),
        def("smoothS", positive(), json!(2)),
        req(
            "camera",
            by_level(|| object(vec![req("zoom", min_max(0.7, 1.4)), req("lead", num())])),
        ),
        req("palettes", by_level(id)),
        def("paletteHoldS", positive(), json!(2)),
        req("calm", by_level(|| unit_max(0.9))),
        opt("setPieces", set_pieces()),
    ])
}

pub fn level_script() -> Schema {
    let fields: Vec<Field> = vec![
        opt("extends", string()),
        req("song", id()),
        req("seed", int()),
        def("player", en(&PLAYER_NAMES), json!("skilled")),
        opt(
            "upgrades",
            object(vec![
                def("loadout", array(id()), json!([])),
                def("offer", array(id()), json!([])),
            ]),
        ),
        opt(
            "enemies",
            Schema::Record {
                key: Some((is_id, "ids are lower-case kebab-case")),
                value: Box::new(object(vec![
                    opt("contactDamage", min(0.0)),
                    opt("attackDamage", min(0.0)),
                ])),
            },
        ),
        opt(
            "hero",
            Schema::Record {
                key: None,
                value: Box::new(Schema::Any),
            },
        ),
        opt(
            "title",
            object(vec![
                req("piece", string()),
                req("composer", string()),
                req("performer", string()),
            ]),
        ),
        req("zones", array1(id())),
        req("sections", array1(level_section())),
        def("events", array(level_event()), json!([])),
        def("reactive", bool(), json!(false)),
        opt("director", director()),
    ];
    object(fields)
}

/// The hero schema, for a level's merged hero.
pub fn hero() -> Schema {
    hero_def()
}
