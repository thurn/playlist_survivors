//! `src/content/schema.ts`'s content schemas (not the level script's), field for field, with
//! zod's defaults, bounds and patterns.

use serde_json::{Value, json};

use super::zod::{Field, Num, Schema, Str, def, opt, req};

pub(crate) fn num() -> Schema {
    Schema::Number(Num::default())
}

pub(crate) fn positive() -> Schema {
    Schema::Number(Num {
        positive: true,
        ..Num::default()
    })
}

pub(crate) fn min(m: f64) -> Schema {
    Schema::Number(Num {
        min: Some(m),
        ..Num::default()
    })
}

pub(crate) fn int_positive() -> Schema {
    Schema::Number(Num {
        int: true,
        positive: true,
        ..Num::default()
    })
}

pub(crate) fn int_min(m: f64) -> Schema {
    Schema::Number(Num {
        int: true,
        min: Some(m),
        ..Num::default()
    })
}

pub(crate) fn positive_max(m: f64) -> Schema {
    Schema::Number(Num {
        positive: true,
        max: Some(m),
        ..Num::default()
    })
}

pub(crate) fn min_max(lo: f64, hi: f64) -> Schema {
    Schema::Number(Num {
        min: Some(lo),
        max: Some(hi),
        ..Num::default()
    })
}

/// `z.number().min(0).max(1)`.
pub(crate) fn unit() -> Schema {
    min_max(0.0, 1.0)
}

pub(crate) fn string() -> Schema {
    Schema::String(Str::default())
}

pub(crate) fn bool() -> Schema {
    Schema::Bool
}

pub(crate) fn is_id(s: &str) -> bool {
    let b = s.as_bytes();
    !b.is_empty()
        && (b[0].is_ascii_lowercase() || b[0].is_ascii_digit())
        && b.iter()
            .all(|&c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == b'-')
}

fn is_color(s: &str) -> bool {
    let b = s.as_bytes();
    b.len() == 7 && b[0] == b'#' && b[1..].iter().all(u8::is_ascii_hexdigit)
}

/// `^[x.]*x[x.]*$`
fn is_gait_pattern(s: &str) -> bool {
    !s.is_empty() && s.bytes().all(|c| c == b'x' || c == b'.') && s.contains('x')
}

pub(crate) fn id() -> Schema {
    Schema::String(Str {
        check: Some((is_id, "ids are lower-case kebab-case")),
    })
}

pub(crate) fn color() -> Schema {
    Schema::String(Str {
        check: Some((is_color, "colors are #rrggbb")),
    })
}

/// `z.tuple([z.number(), z.number()])`.
pub(crate) fn range() -> Schema {
    Schema::Tuple(vec![num(), num()])
}

/// A number, or a [low, high] range interpolated by intensity.
pub(crate) fn param() -> Schema {
    Schema::Union(vec![num(), range()])
}

pub(crate) fn array(item: Schema) -> Schema {
    Schema::Array {
        item: Box::new(item),
        min: 0,
    }
}

pub(crate) fn array1(item: Schema) -> Schema {
    Schema::Array {
        item: Box::new(item),
        min: 1,
    }
}

pub(crate) fn object(fields: Vec<Field>) -> Schema {
    Schema::Object(fields)
}

pub(crate) fn lit(s: &'static str) -> Schema {
    Schema::Literal(s)
}

pub(crate) fn en(options: &[&'static str]) -> Schema {
    Schema::Enum(options.to_vec())
}

const SUBDIVISION: [&str; 5] = ["sixteenth", "eighth", "beat", "bar", "two-bar"];

pub(crate) fn subdivision() -> Schema {
    en(&SUBDIVISION)
}

fn arc_shape() -> Schema {
    object(vec![
        req("kind", lit("arc")),
        req("radius", positive()),
        req("degrees", positive_max(360.0)),
    ])
}

pub fn hero_def() -> Schema {
    let drift = Schema::Refine(
        Box::new(object(vec![
            req("presses", int_positive()),
            req("minPresses", int_min(1.0)),
            req("deadMs", min(0.0)),
            req("maxMs", min(0.0)),
        ])),
        |d| {
            (d["minPresses"].as_f64() > d["presses"].as_f64()).then(|| {
                "drift.minPresses must be at most drift.presses (the drift keeps only that many)"
                    .to_string()
            })
        },
    );
    object(vec![
        req(
            "step",
            object(vec![
                req("length", positive()),
                req("durationMs", positive()),
                req("diagonalWindowMs", min(0.0)),
            ]),
        ),
        req(
            "stride",
            object(vec![
                req("durationMs", positive()),
                req("hop", min(0.0)),
                req("maxPeriodS", positive()),
                req("freePeriodS", positive()),
                req("stompMs", min(0.0)),
            ]),
        ),
        req(
            "walk",
            object(vec![
                req("speed", positive()),
                req("accelMs", positive()),
                req("strideCycle", positive()),
            ]),
        ),
        req(
            "timing",
            object(vec![
                req("perfectMs", positive()),
                req("greatMs", positive()),
                req("goodMs", positive()),
            ]),
        ),
        req(
            "meter",
            object(vec![
                req(
                    "counted",
                    object(vec![
                        req("perfect", num()),
                        req("great", num()),
                        req("good", num()),
                        req("loose", num()),
                    ]),
                ),
                req(
                    "offbeat",
                    object(vec![req("perfect", num()), req("great", num())]),
                ),
                req("doubleTap", num()),
                req("stray", num()),
                req(
                    "drain",
                    object(vec![
                        req("afterBars", min(0.0)),
                        req("emptyInBars", positive()),
                    ]),
                ),
                req("tiers", array1(unit())),
                req("hysteresis", min(0.0)),
                req("doubleTapMs", min(0.0)),
                req("drift", drift),
                req("damage", array1(positive())),
            ]),
        ),
        req(
            "swings",
            object(vec![
                req("windupMs", positive()),
                req(
                    "light",
                    object(vec![
                        req("animations", array1(string())),
                        req("hitShape", arc_shape()),
                        req("damage", positive()),
                        opt("tiers", array1(arc_shape())),
                    ]),
                ),
                req(
                    "heavy",
                    object(vec![
                        req("animation", string()),
                        req("hitShape", arc_shape()),
                        req("damage", positive()),
                        req("hitStopMs", min_max(0.0, 150.0)),
                        req(
                            "wave",
                            object(vec![
                                req("to", positive()),
                                req("band", positive()),
                                req("damage", positive()),
                                opt("tiers", array1(positive())),
                            ]),
                        ),
                        opt("tiers", array1(arc_shape())),
                    ]),
                ),
            ]),
        ),
        opt("pickupRadius", array1(positive())),
        req(
            "health",
            object(vec![
                req("max", positive()),
                def("invulnerableMs", positive(), json!(500)),
                def("levelUpHeal", min(0.0), json!(20)),
            ]),
        ),
        req(
            "aimWeights",
            object(vec![
                req("regular", positive()),
                req("elite", positive()),
                req("boss", positive()),
                req("formation", positive()),
            ]),
        ),
    ])
}

pub fn enemy_def() -> Schema {
    object(vec![
        opt(
            "survivors",
            object(vec![
                req("speed", min(0.0)),
                req("power", min(0.0)),
                req("xp", min(0.0)),
                def("knockback", min(0.0), json!(0)),
                def("maxKnockback", min(0.0), json!(3)),
                def("resDebuffs", min(0.0), json!(0)),
                def("levelScaled", bool(), json!(false)),
                def("brute", bool(), json!(false)),
            ]),
        ),
        req("id", id()),
        req("atlas", string()),
        req("radius", positive()),
        req("group", en(&["eighth", "beat", "bar", "two-bar"])),
        req("speed", positive()),
        req("health", positive()),
        req("size", en(&["regular", "elite", "boss"])),
        def("behavior", en(&["chase", "archer", "boss"]), json!("chase")),
        def("scale", positive(), json!(1)),
        def("hover", min(0.0), json!(0)),
        def("walkCycleBeats", positive(), json!(2)),
        opt("hop", bool()),
        opt(
            "gait",
            object(vec![
                def("every", en(&["sixteenth", "eighth", "beat"]), json!("beat")),
                def(
                    "pattern",
                    Schema::String(Str {
                        check: Some((
                            is_gait_pattern,
                            "invalid string: must match pattern /^[x.]*x[x.]*$/",
                        )),
                    }),
                    json!("x"),
                ),
                def("share", positive_max(1.0), json!(0.4)),
            ]),
        ),
        req(
            "death",
            object(vec![req("recipe", id()), req("debris", array1(color()))]),
        ),
        def("contactDamage", min(0.0), json!(0)),
        def("attackDamage", min(0.0), json!(0)),
    ])
}

pub fn weapon_def() -> Schema {
    let tiers = |fields: Vec<Field>| opt("tiers", array1(object(fields)));
    object(vec![
        req("id", id()),
        req("name", string()),
        req("kind", en(&["orbit", "chain", "trail", "blast"])),
        req("fires", object(vec![req("every", subdivision())])),
        opt(
            "orbit",
            object(vec![
                req("count", int_positive()),
                req("radius", positive()),
                req("periodBars", positive()),
                tiers(vec![
                    req("count", int_positive()),
                    req("radius", positive()),
                ]),
            ]),
        ),
        opt(
            "chain",
            object(vec![
                req("jumps", range()),
                req("step", subdivision()),
                def("range", positive(), json!(110)),
                tiers(vec![req("jumps", int_positive()), req("range", positive())]),
            ]),
        ),
        opt(
            "trail",
            object(vec![
                req("beats", positive()),
                def("spacing", positive(), json!(18)),
                tiers(vec![req("radius", positive())]),
            ]),
        ),
        opt(
            "blast",
            object(vec![
                req("beats", positive()),
                req("step", subdivision()),
                req("radius", positive()),
            ]),
        ),
        opt(
            "hitShape",
            object(vec![req("kind", lit("circle")), req("radius", positive())]),
        ),
        req("damage", positive()),
        req("weight", en(&["light", "medium", "heavy"])),
        req("effect", id()),
        req("sound", string()),
        req("icon", string()),
        req("color", color()),
        opt(
            "evolve",
            object(vec![
                req("name", string()),
                opt("every", subdivision()),
                def("count", int_min(0.0), json!(0)),
                def("radiusScale", positive(), json!(1)),
                def("jumps", int_min(0.0), json!(0)),
            ]),
        ),
    ])
}

pub fn passive_def() -> Schema {
    object(vec![
        req("id", id()),
        req("name", string()),
        def("maxHealth", min(0.0), json!(0)),
        def("pickup", min(0.0), json!(0)),
        def("damage", min(0.0), json!(0)),
        def("armour", min(0.0), json!(0)),
        req("color", color()),
    ])
}

pub fn formation_def() -> Schema {
    object(vec![
        req("id", id()),
        req(
            "shape",
            en(&["line", "ring", "spiral", "v-wedge", "kick-line"]),
        ),
        req("enemy", id()),
        req("count", int_positive()),
        req("spacing", positive()),
        req(
            "enterFrom",
            en(&["left", "right", "top", "bottom", "ahead"]),
        ),
        def("entranceBeats", positive(), json!(4)),
        def("step", subdivision(), json!("beat")),
        opt(
            "dance",
            object(vec![
                req("animation", string()),
                req("on", subdivision()),
                def("alternateLegs", bool(), json!(false)),
            ]),
        ),
        req(
            "release",
            Schema::Union(vec![
                object(vec![
                    req("at", lit("finalAccentOfPhrase")),
                    def("phrases", int_positive(), json!(2)),
                    def("mode", en(&["charge", "break"]), json!("charge")),
                ]),
                object(vec![
                    req("at", lit("afterBeats")),
                    req("beats", positive()),
                    def("mode", en(&["charge", "break"]), json!("break")),
                ]),
            ]),
        ),
    ])
}

fn clock() -> Schema {
    en(&["world", "song"])
}

fn tint_or_debris() -> Schema {
    Schema::Union(vec![color(), lit("debris")])
}

pub fn effect_layer() -> Schema {
    let song = || def("clock", lit("song"), json!("song"));
    Schema::Discriminated {
        key: "kind",
        variants: vec![
            (
                "sprite",
                object(vec![
                    req("kind", lit("sprite")),
                    req("art", string()),
                    req("clock", clock()),
                    req("life", positive()),
                    def("align", en(&["hitShape", "none", "up"]), json!("none")),
                    def("scale", param(), json!(1)),
                    def("additive", bool(), json!(false)),
                    opt("tint", color()),
                ]),
            ),
            (
                "burst",
                object(vec![
                    req("kind", lit("burst")),
                    req("art", string()),
                    req("clock", clock()),
                    req("count", param()),
                    req("speed", param()),
                    req("life", positive()),
                    def("gravity", num(), json!(0)),
                    def("spread", num(), json!(360)),
                    def("size", param(), json!(1)),
                    def("additive", bool(), json!(true)),
                    def("under", bool(), json!(false)),
                    opt("tint", tint_or_debris()),
                    def("drag", min(0.0), json!(0)),
                ]),
            ),
            (
                "decal",
                object(vec![
                    req("kind", lit("decal")),
                    req("art", string()),
                    def("clock", clock(), json!("world")),
                    def("life", positive(), json!(20)),
                    def("size", param(), json!(1)),
                    opt("tint", tint_or_debris()),
                ]),
            ),
            (
                "light",
                object(vec![
                    req("kind", lit("light")),
                    req("color", color()),
                    req("radius", param()),
                    req("life", positive()),
                    req("clock", clock()),
                    def("strength", param(), json!(1)),
                ]),
            ),
            (
                "shake",
                object(vec![
                    req("kind", lit("shake")),
                    req("trauma", param()),
                    song(),
                ]),
            ),
            (
                "flash",
                object(vec![
                    req("kind", lit("flash")),
                    req("opacity", param()),
                    req("life", positive()),
                    song(),
                    def("color", color(), json!("#ffffff")),
                ]),
            ),
            (
                "distortion",
                object(vec![
                    req("kind", lit("distortion")),
                    req("radius", param()),
                    req("life", positive()),
                    req("clock", clock()),
                    def("strength", param(), json!(1)),
                ]),
            ),
            (
                "ring",
                object(vec![
                    req("kind", lit("ring")),
                    req("color", color()),
                    req("radius", param()),
                    req("life", positive()),
                    req("clock", clock()),
                    def("width", param(), json!(3)),
                    def("additive", bool(), json!(true)),
                ]),
            ),
            (
                "aberration",
                object(vec![
                    req("kind", lit("aberration")),
                    req("strength", param()),
                    req("life", positive()),
                    song(),
                ]),
            ),
            (
                "text",
                object(vec![
                    req("kind", lit("text")),
                    req("text", string()),
                    req("color", color()),
                    req("life", positive()),
                    song(),
                    def("size", num(), json!(16)),
                ]),
            ),
        ],
    }
}

pub fn effect_recipe() -> Schema {
    object(vec![req("id", id()), req("layers", array1(effect_layer()))])
}

pub fn sound_palette() -> Schema {
    let sample = Schema::Union(vec![
        string(),
        object(vec![
            req("id", string()),
            opt("rootNote", string()),
            opt("gain", num()),
        ]),
    ]);
    object(vec![
        req("id", id()),
        opt("extends", id()),
        def("tuned", bool(), json!(false)),
        def("gain", num(), json!(1)),
        req(
            "sounds",
            Schema::Record {
                key: None,
                value: Box::new(array1(sample)),
            },
        ),
    ])
}

pub fn zone_theme() -> Schema {
    object(vec![
        req("id", id()),
        req("name", string()),
        req(
            "terrain",
            object(vec![
                req("ground", string()),
                req("path", string()),
                req("patch", string()),
            ]),
        ),
        req(
            "props",
            array(object(vec![
                req("sprite", string()),
                req("density", min(0.0)),
                opt(
                    "light",
                    object(vec![req("color", color()), req("radius", positive())]),
                ),
                def("underfoot", bool(), json!(false)),
            ])),
        ),
        req("ambientLight", color()),
        req(
            "colorGrade",
            en(&["twilight", "neutral", "autumn", "ember", "infernal"]),
        ),
        req(
            "particles",
            en(&["fireflies", "none", "falling-leaves", "rising-embers"]),
        ),
        def("tint", color(), json!("#ffffff")),
        def("heatHaze", bool(), json!(false)),
    ])
}

/// One content file: any of the kinds, each optional.
pub fn content_file() -> Schema {
    object(vec![
        opt("hero", hero_def()),
        opt("enemies", array(enemy_def())),
        opt("weapons", array(weapon_def())),
        opt("passives", array(passive_def())),
        opt("formations", array(formation_def())),
        opt("effects", array(effect_recipe())),
        opt("soundPalettes", array(sound_palette())),
        opt("zones", array(zone_theme())),
    ])
}

/// (`Value` is re-exported for the loader's convenience.)
pub type Doc = Value;
