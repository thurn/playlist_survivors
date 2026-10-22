/**
 * Schemas for content files (shared by all levels) and level scripts. Content
 * is YAML, validated at load. Durations are world seconds and distances are
 * world pixels unless stated otherwise.
 */
import { z } from "zod";

const id = z.string().regex(/^[a-z0-9][a-z0-9-]*$/, "ids are lower-case kebab-case");
const unit = z.number().min(0).max(1);
const range = z.tuple([z.number(), z.number()]);
/** A number, or a [low, high] range interpolated by intensity. */
const param = z.union([z.number(), range]);
const color = z.string().regex(/^#[0-9a-fA-F]{6}$/, "colors are #rrggbb");

export const subdivision = z.enum(["sixteenth", "eighth", "beat", "bar", "two-bar"]);
export type Subdivision = z.infer<typeof subdivision>;

export const weight = z.enum(["light", "medium", "heavy"]);
export type Weight = z.infer<typeof weight>;

// ---------------------------------------------------------------- hit shapes

export const hitShapeDef = z.discriminatedUnion("kind", [
  z.strictObject({ kind: z.literal("arc"), radius: z.number().positive(), degrees: z.number().positive().max(360) }),
  z.strictObject({ kind: z.literal("circle"), radius: z.number().positive(), offset: z.number().optional() }),
  z.strictObject({ kind: z.literal("line"), length: z.number().positive(), width: z.number().positive().default(24) }),
  z.strictObject({ kind: z.literal("capsule"), length: range, radius: z.number().positive() }),
  z.strictObject({ kind: z.literal("sweep"), radius: z.number().positive() }),
  z.strictObject({ kind: z.literal("ring"), width: z.number().positive() }),
]);
export type HitShapeDef = z.infer<typeof hitShapeDef>;

// ---------------------------------------------------------------------- hero

const arcShape = z.strictObject({ kind: z.literal("arc"), radius: z.number().positive(), degrees: z.number().positive().max(360) });

/** The movement model, timing windows, groove and swings. See docs/design/beat-steps.md. */
export const heroDef = z.strictObject({
  step: z.strictObject({
    /** Step length in world pixels. */
    length: z.number().positive(),
    /** Step duration in world milliseconds. */
    durationMs: z.number().positive(),
    /** Height of the hop arc in world pixels. */
    hop: z.number().min(0),
    /** A perpendicular key this soon after the first bends the step into a diagonal. */
    diagonalWindowMs: z.number().min(0),
  }),
  walk: z.strictObject({
    /** World pixels per world-second. */
    speed: z.number().positive(),
    /** Time to reach full speed from standing, or to stop. */
    accelMs: z.number().positive(),
    /** World pixels travelled per full walk cycle. */
    strideCycle: z.number().positive(),
  }),
  timing: z.strictObject({
    perfectMs: z.number().positive(),
    greatMs: z.number().positive(),
    goodMs: z.number().positive(),
    /** The lowest accuracy any press earns. */
    floor: unit,
  }),
  groove: z.strictObject({
    /** Each rated press moves the groove this fraction of the way toward its accuracy. */
    follow: unit,
    /** With no presses in step movement, the groove halves every this many bars. */
    idleHalfLifeBars: z.number().positive(),
    /** Damage bonus at full groove (0.2 is +20%). */
    maxBonus: z.number().min(0),
  }),
  swings: z.strictObject({
    /** The armed swing strikes on the first grid time at least this long after the press. */
    minLeadMs: z.number().positive(),
    /** Wind-up before the impact frame, when there is time for it. */
    windupMs: z.number().positive(),
    light: z.strictObject({ animations: z.array(z.string()).min(1), hitShape: arcShape, damage: z.number().positive() }),
    heavy: z.strictObject({
      animation: z.string(),
      hitShape: arcShape,
      damage: z.number().positive(),
      hitStopMs: z.number().min(0).max(150),
      /** The crescent shockwave: bands of `band` px from the arc's edge out to `to` px, one per sixteenth note. */
      wave: z.strictObject({ to: z.number().positive(), band: z.number().positive(), damage: z.number().positive() }),
    }),
  }),
  /** What the sword aims for: the summed weight of the enemies a swing (or a cleave's shockwave) would catch. */
  aimWeights: z.strictObject({ regular: z.number().positive(), elite: z.number().positive(), boss: z.number().positive(), formation: z.number().positive() }),
});
export type HeroDef = z.infer<typeof heroDef>;

// ------------------------------------------------------------------- enemies

export const enemyDef = z.strictObject({
  id,
  /** Sprite id in the character atlas. */
  atlas: z.string(),
  radius: z.number().positive(),
  group: subdivision.exclude(["sixteenth"]),
  /** Chasing speed in world pixels per world-second, before the group's surge. */
  speed: z.number().positive(),
  /** Damage it takes to kill (a light swing deals 1.0 before the groove bonus). */
  health: z.number().positive(),
  size: z.enum(["regular", "elite", "boss"]),
  behavior: z.enum(["chase", "archer", "boss"]).default("chase"),
  scale: z.number().positive().default(1),
  /** Flying enemies hover; the shadow is drawn below them. */
  hover: z.number().min(0).default(0),
  /** How many beats one full walk cycle spans (LPC walks contain two strides). */
  walkCycleBeats: z.number().positive().default(2),
  /** Hop locomotion with squash and stretch (slimes). */
  hop: z.boolean().optional(),
  death: z.strictObject({ recipe: id, debris: z.array(color).min(1) }),
});
export type EnemyDef = z.infer<typeof enemyDef>;

// ------------------------------------------------------------------- weapons

export const weaponDef = z.strictObject({
  id,
  name: z.string(),
  kind: z.enum(["orbit", "chain", "trail", "blast"]),
  fires: z.strictObject({ every: subdivision }),
  orbit: z.strictObject({ count: z.number().int().positive(), radius: z.number().positive(), periodBars: z.number().positive() }).optional(),
  chain: z.strictObject({ jumps: range, step: subdivision, range: z.number().positive().default(110) }).optional(),
  trail: z.strictObject({ beats: z.number().positive(), spacing: z.number().positive().default(18) }).optional(),
  blast: z.strictObject({ beats: z.number().positive(), step: subdivision, radius: z.number().positive() }).optional(),
  hitShape: z.strictObject({ kind: z.literal("circle"), radius: z.number().positive() }).optional(),
  /** Base damage per hit, multiplied by the groove like the sword's. */
  damage: z.number().positive(),
  weight,
  effect: id,
  sound: z.string(),
  icon: z.string(),
  color: color,
});
export type WeaponDef = z.infer<typeof weaponDef>;

// ---------------------------------------------------------------- formations

export const formationDef = z.strictObject({
  id,
  shape: z.enum(["line", "ring", "spiral", "v-wedge", "kick-line"]),
  enemy: id,
  count: z.number().int().positive(),
  spacing: z.number().positive(),
  enterFrom: z.enum(["left", "right", "top", "bottom", "ahead"]),
  /** Beats spent walking in before the dance. */
  entranceBeats: z.number().positive().default(4),
  step: subdivision.default("beat"),
  dance: z.strictObject({ animation: z.string(), on: subdivision, alternateLegs: z.boolean().default(false) }).optional(),
  /** At the release the surviving members become normal chasing enemies; a charge rushes the hero first. */
  release: z.union([
    z.strictObject({
      at: z.literal("finalAccentOfPhrase"),
      /** Phrase number within the event's section, counted from the formation start. */
      phrases: z.number().int().positive().default(2),
      mode: z.enum(["charge", "break"]).default("charge"),
    }),
    z.strictObject({ at: z.literal("afterBeats"), beats: z.number().positive(), mode: z.enum(["charge", "break"]).default("break") }),
  ]),
});
export type FormationDef = z.infer<typeof formationDef>;

// ------------------------------------------------------------ effect recipes

const clock = z.enum(["world", "song"]);
export const effectLayer = z.discriminatedUnion("kind", [
  z.strictObject({ kind: z.literal("sprite"), art: z.string(), clock, life: z.number().positive(), align: z.enum(["hitShape", "none", "up"]).default("none"), scale: param.default(1), additive: z.boolean().default(false), tint: color.optional() }),
  z.strictObject({ kind: z.literal("burst"), art: z.string(), clock, count: param, speed: param, life: z.number().positive(), gravity: z.number().default(0), spread: z.number().default(360), size: param.default(1), additive: z.boolean().default(true), tint: z.union([color, z.literal("debris")]).optional(), drag: z.number().min(0).default(0) }),
  z.strictObject({ kind: z.literal("decal"), art: z.string(), clock: clock.default("world"), life: z.number().positive().default(20), size: param.default(1), tint: z.union([color, z.literal("debris")]).optional() }),
  z.strictObject({ kind: z.literal("light"), color, radius: param, life: z.number().positive(), clock, strength: param.default(1) }),
  z.strictObject({ kind: z.literal("shake"), trauma: param, clock: z.literal("song").default("song") }),
  z.strictObject({ kind: z.literal("flash"), opacity: param, life: z.number().positive(), clock: z.literal("song").default("song"), color: color.default("#ffffff") }),
  z.strictObject({ kind: z.literal("distortion"), radius: param, life: z.number().positive(), clock, strength: param.default(1) }),
  z.strictObject({ kind: z.literal("ring"), color, radius: param, life: z.number().positive(), clock, width: param.default(3), additive: z.boolean().default(true) }),
  z.strictObject({ kind: z.literal("aberration"), strength: param, life: z.number().positive(), clock: z.literal("song").default("song") }),
  z.strictObject({ kind: z.literal("text"), text: z.string(), color, life: z.number().positive(), clock: z.literal("song").default("song"), size: z.number().default(16) }),
]);
export type EffectLayer = z.infer<typeof effectLayer>;

export const effectRecipe = z.strictObject({ id, layers: z.array(effectLayer).min(1) });
export type EffectRecipe = z.infer<typeof effectRecipe>;

// ------------------------------------------------------------ sound palettes

const sample = z.union([z.string(), z.strictObject({ id: z.string(), rootNote: z.string().optional(), gain: z.number().optional() })]);
export const soundPalette = z.strictObject({
  id,
  extends: id.optional(),
  tuned: z.boolean().default(false),
  gain: z.number().default(1),
  sounds: z.record(z.string(), z.array(sample).min(1)),
});
export type SoundPalette = z.infer<typeof soundPalette>;

// --------------------------------------------------------------- zone themes

export const zoneTheme = z.strictObject({
  id,
  name: z.string(),
  terrain: z.strictObject({ ground: z.string(), path: z.string(), patch: z.string() }),
  props: z.array(z.strictObject({ sprite: z.string(), density: z.number().min(0), light: z.strictObject({ color, radius: z.number().positive() }).optional(), corridor: z.boolean().default(false) })),
  ambientLight: color,
  colorGrade: z.enum(["twilight", "neutral", "autumn", "ember", "infernal"]),
  particles: z.enum(["fireflies", "none", "falling-leaves", "rising-embers"]),
  /** Ground tint multiplier for the zone (twilight is bluish). */
  tint: color.default("#ffffff"),
  heatHaze: z.boolean().default(false),
});
export type ZoneTheme = z.infer<typeof zoneTheme>;

// ------------------------------------------------------------- content files

export const contentFile = z.strictObject({
  hero: heroDef.optional(),
  enemies: z.array(enemyDef).optional(),
  weapons: z.array(weaponDef).optional(),
  formations: z.array(formationDef).optional(),
  effects: z.array(effectRecipe).optional(),
  soundPalettes: z.array(soundPalette).optional(),
  zones: z.array(zoneTheme).optional(),
});

// -------------------------------------------------------------- level script

export const musicalRef = z.union([
  z.strictObject({ bar: z.number().int(), beat: z.number().min(1).default(1), offsetBeats: z.number().optional() }),
  z.strictObject({
    section: z.string(),
    phrase: z.number().int().positive().optional(),
    point: z.enum(["start", "end", "finalAccent"]),
    offsetBeats: z.number().optional(),
  }),
  z.strictObject({
    accent: z.string(),
    fromEnd: z.number().int().positive().optional(),
    index: z.number().int().positive().optional(),
    offsetBeats: z.number().optional(),
  }),
]);
export type MusicalRef = z.infer<typeof musicalRef>;

const barRange = z.string().regex(/^\d+-\d+$/, 'bar ranges look like "9-16"');

const timeScale = z.union([z.number().min(0.25).max(1), z.strictObject({ from: z.number().min(0.25).max(1), to: z.number().min(0.25).max(1) })]);

export const levelSection = z.strictObject({
  bars: barRange.optional(),
  section: z.string().optional(),
  /** Step movement (one step per press) or free movement (held keys walk); defaults from the song map's feel. */
  movement: z.enum(["steps", "free"]).optional(),
  /** Route speed of the pace point, in world pixels per world-second. Inherited from the previous section. */
  pace: z.number().min(0).optional(),
  /** The skilled player's presses: one character per timing-grid slot of each bar, x presses and . rests. Inherited. */
  presses: z.string().regex(/^[x.]+$/, "presses use x and .").optional(),
  /** The grid presses are rated against and swings strike on. */
  timingGrid: z.enum(["eighth", "beat", "bar"]).optional(),
  /** Swings striking on accents this strong (and with this label) become heavy cleaves. */
  heavyOnAccents: z.strictObject({ minStrength: unit.default(0.7), label: z.string().optional() }).optional(),
  zone: id.optional(),
  timeScale: timeScale.optional(),
  /** How calm the enemies are, 0 (default) to 1: they move slower and spiral in rather than charge. Not inherited. */
  calm: unit.max(0.9).optional(),
  population: z.tuple([z.number().int().min(0).max(520), z.number().int().min(0).max(520)]),
  roster: z.record(z.string(), z.number().positive()).optional(),
  camera: z.strictObject({ zoom: z.number().min(0.7).max(1.4).optional(), lead: z.number().optional(), punchOnAccents: z.boolean().optional() }).optional(),
  sfxPalette: id.optional(),
  intensity: unit.optional(),
  banner: z.string().optional(),
});

export const levelEvent = z.strictObject({
  at: musicalRef,
  levelUp: id.optional(),
  /** Experience the level-up needs (gems collected since the previous one). */
  xp: z.number().positive().optional(),
  formation: id.optional(),
  threats: z
    .strictObject({ kind: z.enum(["archer-volley"]), every: z.string().regex(/^\d+ (bars?|beats?)$/), until: musicalRef, archers: z.number().int().positive().default(2) })
    .optional(),
  boss: z.strictObject({ type: id, slams: z.number().int().min(0), diesAt: musicalRef }).optional(),
  banner: z.string().optional(),
  titleCard: z.strictObject({ until: musicalRef }).optional(),
  landmark: id.optional(),
  timeScale: z.number().min(0.25).max(1).optional(),
  blast: id.optional(),
});

/** The virtual players: the demo's three, and play mode's stand-in for a moderately competent human. */
export const skillProfile = z.enum(["perfect", "skilled", "casual", "moderate"]);
export type SkillProfileName = z.infer<typeof skillProfile>;

export const levelScript = z.strictObject({
  /** Another level script this one restates only the differences from (play mode's tuning). */
  extends: z.string().optional(),
  song: id,
  seed: z.number().int(),
  /** The virtual player's default skill profile (`?player=` overrides it). */
  player: skillProfile.default("skilled"),
  /** The demo's forest corridor along a route, or play mode's open field. */
  world: z.enum(["corridor", "open"]).default("corridor"),
  /** Play-mode rule changes. */
  rules: z.strictObject({ oneStepPerSlot: z.boolean().default(false), accentTelegraphs: z.boolean().default(false) }).optional(),
  /** Overrides merged over content/hero.yaml for this level only (checked against the hero schema). */
  hero: z.record(z.string(), z.unknown()).optional(),
  title: z.strictObject({ piece: z.string(), composer: z.string(), performer: z.string() }),
  zones: z.array(id).min(1),
  sections: z.array(levelSection).min(1),
  events: z.array(levelEvent).default([]),
});
export type LevelScriptInput = z.infer<typeof levelScript>;
