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

/** The movement model, timing windows, groove meter and swings. See docs/design/gameplay-redesign.md. */
export const heroDef = z.strictObject({
  step: z.strictObject({
    /** Step length in world pixels. */
    length: z.number().positive(),
    /** Step duration in world milliseconds. */
    durationMs: z.number().positive(),
    /** A perpendicular key this soon after the first bends the step into a diagonal. */
    diagonalWindowMs: z.number().min(0),
  }),
  /**
   * Fixed-speed strides (see docs/design/gameplay-redesign.md). The
   * walk speed sets the hero's speed; a stride spends the ground built up since the last one.
   */
  stride: z.strictObject({
    /** Stride duration in world milliseconds. */
    durationMs: z.number().positive(),
    /** Height of the hop arc in world pixels. */
    hop: z.number().min(0),
    /** The stride period is one step of the timing grid, halved while it is longer than this (seconds). */
    maxPeriodS: z.number().positive(),
    /** The stride period in sections without a steady beat (seconds). */
    freePeriodS: z.number().positive(),
    /** Two opposite keys this close together (ms) make one stomp: the hero stands still. */
    stompMs: z.number().min(0),
  }),
  walk: z.strictObject({
    /** World pixels per world-second. */
    speed: z.number().positive(),
    /** Time to reach full speed from standing, or to stop. */
    accelMs: z.number().positive(),
    /** World pixels travelled per full walk cycle. */
    strideCycle: z.number().positive(),
  }),
  /** The rating windows around each grid time (ms either side): Perfect, Great and Good. */
  timing: z.strictObject({
    perfectMs: z.number().positive(),
    greatMs: z.number().positive(),
    goodMs: z.number().positive(),
  }),
  /**
   * The groove meter (see "Timing and the Groove" in docs/design/gameplay-redesign.md): changes on
   * its 0-to-1 scale by press class and rating, the drain, the tier thresholds and their
   * hysteresis, and the double-tap window.
   */
  meter: z.strictObject({
    counted: z.strictObject({ perfect: z.number(), great: z.number(), good: z.number(), loose: z.number() }),
    offbeat: z.strictObject({ perfect: z.number(), great: z.number() }),
    doubleTap: z.number(),
    stray: z.number(),
    /** One bar after the latest press that raised it, the groove falls from full to empty in emptyInBars. */
    drain: z.strictObject({ afterBars: z.number().min(0), emptyInBars: z.number().positive() }),
    tiers: z.array(unit).min(1),
    hysteresis: z.number().min(0),
    doubleTapMs: z.number().min(0),
    /**
     * The drift: each press is judged less the median offset of its key group's latest `presses`
     * counted presses (once there are `minPresses`), less `deadMs` toward 0, within ±maxMs.
     */
    drift: z
      .strictObject({ presses: z.number().int().positive(), minPresses: z.number().int().min(1), deadMs: z.number().min(0), maxMs: z.number().min(0) })
      .refine((d) => d.minPresses <= d.presses, "drift.minPresses must be at most drift.presses (the drift keeps only that many)"),
    /** Every hero attack's damage is multiplied by its tier's. */
    damage: z.array(z.number().positive()).min(1),
  }),
  swings: z.strictObject({
    /** Wind-up before the impact frame, when there is time for it. */
    windupMs: z.number().positive(),
    light: z.strictObject({
      animations: z.array(z.string()).min(1),
      hitShape: arcShape,
      damage: z.number().positive(),
      /** The light swing's arc at tiers 0 to 3. */
      tiers: z.array(arcShape).min(1).optional(),
    }),
    heavy: z.strictObject({
      animation: z.string(),
      hitShape: arcShape,
      damage: z.number().positive(),
      hitStopMs: z.number().min(0).max(150),
      /** The crescent shockwave: bands of `band` px from the arc's edge out to `to` px, one per sixteenth note. */
      wave: z.strictObject({
        to: z.number().positive(),
        band: z.number().positive(),
        damage: z.number().positive(),
        /** How far the shockwave reaches at tiers 0 to 3. */
        tiers: z.array(z.number().positive()).min(1).optional(),
      }),
      /** The heavy impact's arc at tiers 0 to 3 (360 is a full spin). */
      tiers: z.array(arcShape).min(1).optional(),
    }),
  }),
  /** Gems on the field: the pickup radius (px) at tiers 0 to 3. */
  pickupRadius: z.array(z.number().positive()).min(1).optional(),
  /**
   * The hero's small health pool. Enemies touching the hero bite for their contactDamage; arrows
   * and slams deal their attacker's attackDamage. After a hurt the hero is invulnerable for
   * invulnerableMs (world ms), and a level-up heals levelUpHeal.
   */
  health: z.strictObject({
    max: z.number().positive(),
    invulnerableMs: z.number().positive().default(500),
    levelUpHeal: z.number().min(0).default(20),
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
  /** Health a touch takes from the hero (see the hero's health). */
  contactDamage: z.number().min(0).default(0),
  /** Health its attack takes from the hero when it lands: an archer's arrow, the boss's slam. */
  attackDamage: z.number().min(0).default(0),
});
export type EnemyDef = z.infer<typeof enemyDef>;

// ------------------------------------------------------------------- weapons

export const weaponDef = z.strictObject({
  id,
  name: z.string(),
  kind: z.enum(["orbit", "chain", "trail", "blast"]),
  fires: z.strictObject({ every: subdivision }),
  /** Tiers: each kind's area at tiers 0 to 3 replaces its count and radius, jumps and range, or hit radius. */
  orbit: z.strictObject({ count: z.number().int().positive(), radius: z.number().positive(), periodBars: z.number().positive(), tiers: z.array(z.strictObject({ count: z.number().int().positive(), radius: z.number().positive() })).min(1).optional() }).optional(),
  chain: z.strictObject({ jumps: range, step: subdivision, range: z.number().positive().default(110), tiers: z.array(z.strictObject({ jumps: z.number().int().positive(), range: z.number().positive() })).min(1).optional() }).optional(),
  trail: z.strictObject({ beats: z.number().positive(), spacing: z.number().positive().default(18), tiers: z.array(z.strictObject({ radius: z.number().positive() })).min(1).optional() }).optional(),
  blast: z.strictObject({ beats: z.number().positive(), step: subdivision, radius: z.number().positive() }).optional(),
  hitShape: z.strictObject({ kind: z.literal("circle"), radius: z.number().positive() }).optional(),
  /** Base damage per hit, multiplied by the groove like the sword's. */
  damage: z.number().positive(),
  weight,
  effect: id,
  sound: z.string(),
  icon: z.string(),
  color: color,
  /**
   * Evolution: a weapon upgrade a level-up can offer once the weapon
   * is owned: orbiting blades gain `count` more blades and an orbit `radiusScale` times as wide at
   * every tier; chain lightning fires `every` subdivision instead and gains `jumps` more jumps.
   */
  evolve: z
    .strictObject({
      name: z.string(),
      every: subdivision.optional(),
      count: z.number().int().min(0).default(0),
      radiusScale: z.number().positive().default(1),
      jumps: z.number().int().min(0).default(0),
    })
    .optional(),
});
export type WeaponDef = z.infer<typeof weaponDef>;

// ------------------------------------------------------------------ passives

/**
 * Passives: stat bonuses a level-up can offer, each taken any number
 * of times, the bonuses adding up. Max health also heals that much at once; armour takes from every
 * bite, never below 1.
 */
export const passiveDef = z.strictObject({
  id,
  name: z.string(),
  /** Max health this much higher (and healed this much at once). */
  maxHealth: z.number().min(0).default(0),
  /** The pickup radius this fraction wider. */
  pickup: z.number().min(0).default(0),
  /** Weapon and sword damage this fraction higher. */
  damage: z.number().min(0).default(0),
  /** Each enemy's bite takes this much less, at least 1. */
  armour: z.number().min(0).default(0),
  color: color,
});
export type PassiveDef = z.infer<typeof passiveDef>;

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
  z.strictObject({ kind: z.literal("burst"), art: z.string(), clock, count: param, speed: param, life: z.number().positive(), gravity: z.number().default(0), spread: z.number().default(360), size: param.default(1), additive: z.boolean().default(true), under: z.boolean().default(false), tint: z.union([color, z.literal("debris")]).optional(), drag: z.number().min(0).default(0) }),
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
  /** Props by density per tile; underfoot props are small ground decorations, scattered thicker than the rest. */
  props: z.array(z.strictObject({ sprite: z.string(), density: z.number().min(0), light: z.strictObject({ color, radius: z.number().positive() }).optional(), underfoot: z.boolean().default(false) })),
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
  passives: z.array(passiveDef).optional(),
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
  /** How fast the virtual player roams the open field, in world pixels per world-second. Inherited from the previous section. */
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
  /** Free movement: the sword swings by itself every this many beats (default 2). Not inherited. */
  autoSwingBeats: z.number().int().min(1).max(8).optional(),
  population: z.tuple([z.number().int().min(0).max(520), z.number().int().min(0).max(520)]),
  roster: z.record(z.string(), z.number().positive()).optional(),
  camera: z.strictObject({ zoom: z.number().min(0.7).max(1.4).optional(), lead: z.number().optional(), punchOnAccents: z.boolean().optional() }).optional(),
  sfxPalette: id.optional(),
  intensity: unit.optional(),
  banner: z.string().optional(),
});

export const levelEvent = z.strictObject({
  at: musicalRef,
  /** A level-up: `offer`, three items from the upgrade pool. */
  levelUp: z.literal("offer").optional(),
  /** Experience the level-up needs (gems collected since the previous one). */
  xp: z.number().positive().optional(),
  formation: id.optional(),
  /**
   * A closing ring: `count` enemies evenly spaced on a circle just outside
   * the view, around where the hero stands, with `gaps` gaps of three missing places (two rows past
   * 40); it closes at a steady speed to 100 px from its centre on `closesOn`, then its members chase.
   */
  ring: z.strictObject({ enemy: id, count: z.number().int().positive(), gaps: z.number().int().min(0).max(6).default(3), closesOn: musicalRef }).optional(),
  /**
   * A stampede: `count` enemies at 2.5 times their speed run straight
   * across the view from one side, in `rows` rows with a gap in each, ignoring the hero.
   */
  stampede: z.strictObject({ enemy: id, count: z.number().int().positive(), rows: z.number().int().min(1).max(4).default(3) }).optional(),
  threats: z
    .strictObject({ kind: z.enum(["archer-volley"]), every: z.string().regex(/^\d+ (bars?|beats?)$/), until: musicalRef, archers: z.number().int().positive().default(2) })
    .optional(),
  boss: z.strictObject({ type: id, slams: z.number().int().min(0), slamsUntil: musicalRef }).optional(),
  banner: z.string().optional(),
  titleCard: z.strictObject({ until: musicalRef }).optional(),
  landmark: id.optional(),
  timeScale: z.number().min(0.25).max(1).optional(),
  blast: id.optional(),
});

/**
 * The virtual players: the demo's three, play mode's stand-in for a moderately competent human, the
 * eighths player (every eighth note), the human and late players (timed like real play logs, the
 * late one with an uncalibrated slow key), and the players the groove must not reward: the holder
 * (holds directions, never taps) and mashers at 2, 4 and 8 presses a second.
 */
export const PLAYER_NAMES = ["perfect", "skilled", "casual", "moderate", "eighths", "human", "late", "holder", "mash2", "mash4", "mash8"] as const;
export const skillProfile = z.enum(PLAYER_NAMES);
export type SkillProfileName = z.infer<typeof skillProfile>;

export const levelScript = z.strictObject({
  /** Another level script this one restates only the differences from (play mode's tuning). */
  extends: z.string().optional(),
  song: id,
  seed: z.number().int(),
  /** The virtual player's default skill profile (`?player=` overrides it). */
  player: skillProfile.default("skilled"),
  /**
   * The upgrade pool: the weapons the run starts with beside the sword, and the new weapons a
   * level-up can offer (owned weapons' evolutions and the passives join them).
   */
  upgrades: z.strictObject({ loadout: z.array(id).default([]), offer: z.array(id).default([]) }).optional(),
  /** Overrides of enemies' damage for this level only (play mode's values). */
  enemies: z.record(id, z.strictObject({ contactDamage: z.number().min(0).optional(), attackDamage: z.number().min(0).optional() })).optional(),
  /** Overrides merged over content/hero.yaml for this level only (checked against the hero schema). */
  hero: z.record(z.string(), z.unknown()).optional(),
  title: z.strictObject({ piece: z.string(), composer: z.string(), performer: z.string() }),
  zones: z.array(id).min(1),
  sections: z.array(levelSection).min(1),
  events: z.array(levelEvent).default([]),
});
export type LevelScriptInput = z.infer<typeof levelScript>;
