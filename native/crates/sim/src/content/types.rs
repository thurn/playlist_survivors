//! The content definitions as Rust types, read from zod's output (`super::zod::parse`), so every
//! default is already filled in. Numbers stay f64, as in JS.

use indexmap::IndexMap;
use serde::Deserialize;

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "kebab-case")]
pub enum Subdivision {
    Sixteenth,
    Eighth,
    Beat,
    Bar,
    TwoBar,
}

/// A number, or a [low, high] range interpolated by intensity.
#[derive(Clone, Copy, Debug, Deserialize, PartialEq)]
#[serde(untagged)]
pub enum Param {
    Num(f64),
    Range([f64; 2]),
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum Clock {
    World,
    Song,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct ArcShape {
    pub radius: f64,
    pub degrees: f64,
}

// ---------------------------------------------------------------------- hero

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct HeroDef {
    pub step: Step,
    pub stride: Stride,
    pub walk: Walk,
    pub timing: Timing,
    pub meter: Meter,
    pub swings: Swings,
    pub pickup_radius: Option<Vec<f64>>,
    pub health: HeroHealth,
    pub aim_weights: AimWeights,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Step {
    pub length: f64,
    pub duration_ms: f64,
    pub diagonal_window_ms: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Stride {
    pub duration_ms: f64,
    pub hop: f64,
    pub max_period_s: f64,
    pub free_period_s: f64,
    pub stomp_ms: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Walk {
    pub speed: f64,
    pub accel_ms: f64,
    pub stride_cycle: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Timing {
    pub perfect_ms: f64,
    pub great_ms: f64,
    pub good_ms: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Meter {
    pub counted: Counted,
    pub offbeat: Offbeat,
    pub double_tap: f64,
    pub stray: f64,
    pub drain: Drain,
    pub tiers: Vec<f64>,
    pub hysteresis: f64,
    pub double_tap_ms: f64,
    pub drift: Drift,
    pub damage: Vec<f64>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Counted {
    pub perfect: f64,
    pub great: f64,
    pub good: f64,
    pub loose: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Offbeat {
    pub perfect: f64,
    pub great: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Drain {
    pub after_bars: f64,
    pub empty_in_bars: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Drift {
    pub presses: f64,
    pub min_presses: f64,
    pub dead_ms: f64,
    pub max_ms: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Swings {
    pub windup_ms: f64,
    pub light: LightSwing,
    pub heavy: HeavySwing,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct LightSwing {
    pub animations: Vec<String>,
    pub hit_shape: ArcShape,
    pub damage: f64,
    pub tiers: Option<Vec<ArcShape>>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct HeavySwing {
    pub animation: String,
    pub hit_shape: ArcShape,
    pub damage: f64,
    pub hit_stop_ms: f64,
    pub wave: Wave,
    pub tiers: Option<Vec<ArcShape>>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Wave {
    pub to: f64,
    pub band: f64,
    pub damage: f64,
    pub tiers: Option<Vec<f64>>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct HeroHealth {
    pub max: f64,
    pub invulnerable_ms: f64,
    pub level_up_heal: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct AimWeights {
    pub regular: f64,
    pub elite: f64,
    pub boss: f64,
    pub formation: f64,
}

// ------------------------------------------------------------------- enemies

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum Size {
    Regular,
    Elite,
    Boss,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum Behavior {
    Chase,
    Archer,
    Boss,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct EnemyDef {
    pub id: String,
    pub atlas: String,
    pub radius: f64,
    pub group: Subdivision,
    pub speed: f64,
    pub health: f64,
    pub size: Size,
    pub behavior: Behavior,
    pub scale: f64,
    pub hover: f64,
    pub walk_cycle_beats: f64,
    pub hop: Option<bool>,
    pub gait: Option<Gait>,
    pub death: Death,
    pub contact_damage: f64,
    pub attack_damage: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Gait {
    pub every: Subdivision,
    pub pattern: String,
    pub share: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Death {
    pub recipe: String,
    pub debris: Vec<String>,
}

// ------------------------------------------------------------------- weapons

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum WeaponKind {
    Orbit,
    Chain,
    Trail,
    Blast,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum Weight {
    Light,
    Medium,
    Heavy,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct WeaponDef {
    pub id: String,
    pub name: String,
    pub kind: WeaponKind,
    pub fires: Fires,
    pub orbit: Option<Orbit>,
    pub chain: Option<Chain>,
    pub trail: Option<Trail>,
    pub blast: Option<Blast>,
    pub hit_shape: Option<CircleShape>,
    pub damage: f64,
    pub weight: Weight,
    pub effect: String,
    pub sound: String,
    pub icon: String,
    pub color: String,
    pub evolve: Option<Evolve>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Fires {
    pub every: Subdivision,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Orbit {
    pub count: f64,
    pub radius: f64,
    pub period_bars: f64,
    pub tiers: Option<Vec<OrbitTier>>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct OrbitTier {
    pub count: f64,
    pub radius: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Chain {
    pub jumps: [f64; 2],
    pub step: Subdivision,
    pub range: f64,
    pub tiers: Option<Vec<ChainTier>>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct ChainTier {
    pub jumps: f64,
    pub range: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Trail {
    pub beats: f64,
    pub spacing: f64,
    pub tiers: Option<Vec<TrailTier>>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct TrailTier {
    pub radius: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Blast {
    pub beats: f64,
    pub step: Subdivision,
    pub radius: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct CircleShape {
    pub radius: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Evolve {
    pub name: String,
    pub every: Option<Subdivision>,
    pub count: f64,
    pub radius_scale: f64,
    pub jumps: f64,
}

// ------------------------------------------------------------------ passives

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct PassiveDef {
    pub id: String,
    pub name: String,
    pub max_health: f64,
    pub pickup: f64,
    pub damage: f64,
    pub armour: f64,
    pub color: String,
}

// ---------------------------------------------------------------- formations

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "kebab-case")]
pub enum FormationShape {
    Line,
    Ring,
    Spiral,
    VWedge,
    KickLine,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum EnterFrom {
    Left,
    Right,
    Top,
    Bottom,
    Ahead,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum ReleaseMode {
    Charge,
    Break,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct FormationDef {
    pub id: String,
    pub shape: FormationShape,
    pub enemy: String,
    pub count: f64,
    pub spacing: f64,
    pub enter_from: EnterFrom,
    pub entrance_beats: f64,
    pub step: Subdivision,
    pub dance: Option<Dance>,
    pub release: Release,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Dance {
    pub animation: String,
    pub on: Subdivision,
    pub alternate_legs: bool,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(tag = "at", rename_all = "camelCase")]
pub enum Release {
    FinalAccentOfPhrase { phrases: f64, mode: ReleaseMode },
    AfterBeats { beats: f64, mode: ReleaseMode },
}

// ------------------------------------------------------------ effect recipes

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub enum Align {
    HitShape,
    None,
    Up,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(tag = "kind", rename_all = "lowercase")]
pub enum EffectLayer {
    Sprite {
        art: String,
        clock: Clock,
        life: f64,
        align: Align,
        scale: Param,
        additive: bool,
        tint: Option<String>,
    },
    Burst {
        art: String,
        clock: Clock,
        count: Param,
        speed: Param,
        life: f64,
        gravity: f64,
        spread: f64,
        size: Param,
        additive: bool,
        under: bool,
        /// A color, or "debris" for the dying enemy's debris colors.
        tint: Option<String>,
        drag: f64,
    },
    Decal {
        art: String,
        clock: Clock,
        life: f64,
        size: Param,
        tint: Option<String>,
    },
    Light {
        color: String,
        radius: Param,
        life: f64,
        clock: Clock,
        strength: Param,
    },
    Shake {
        trauma: Param,
        clock: Clock,
    },
    Flash {
        opacity: Param,
        life: f64,
        clock: Clock,
        color: String,
    },
    Distortion {
        radius: Param,
        life: f64,
        clock: Clock,
        strength: Param,
    },
    Ring {
        color: String,
        radius: Param,
        life: f64,
        clock: Clock,
        width: Param,
        additive: bool,
    },
    Aberration {
        strength: Param,
        life: f64,
        clock: Clock,
    },
    Text {
        text: String,
        color: String,
        life: f64,
        clock: Clock,
        size: f64,
    },
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct EffectRecipe {
    pub id: String,
    pub layers: Vec<EffectLayer>,
}

// ------------------------------------------------------------ sound palettes

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(untagged)]
pub enum Sample {
    Name(String),
    #[serde(rename_all = "camelCase")]
    Detailed {
        id: String,
        root_note: Option<String>,
        gain: Option<f64>,
    },
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct SoundPalette {
    pub id: String,
    /// Always None once loaded: extends are resolved.
    pub extends: Option<String>,
    pub tuned: bool,
    pub gain: f64,
    pub sounds: IndexMap<String, Vec<Sample>>,
}

// --------------------------------------------------------------- zone themes

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum ColorGrade {
    Twilight,
    Neutral,
    Autumn,
    Ember,
    Infernal,
}

#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "kebab-case")]
pub enum Particles {
    Fireflies,
    None,
    FallingLeaves,
    RisingEmbers,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ZoneTheme {
    pub id: String,
    pub name: String,
    pub terrain: Terrain,
    pub props: Vec<Prop>,
    pub ambient_light: String,
    pub color_grade: ColorGrade,
    pub particles: Particles,
    pub tint: String,
    pub heat_haze: bool,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Terrain {
    pub ground: String,
    pub path: String,
    pub patch: String,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Prop {
    pub sprite: String,
    pub density: f64,
    pub light: Option<PropLight>,
    pub underfoot: bool,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct PropLight {
    pub color: String,
    pub radius: f64,
}
