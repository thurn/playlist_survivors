/**
 * The plan: the recording of a simulated playthrough, a complete, immutable
 * timeline of a level. The runtime evaluates it at any song time without
 * stepping state.
 */
import type { Shape } from "../core/geometry.ts";
import type { WarpBreak } from "../core/warp.ts";
import type { Facing } from "../assets/types.ts";
import type { LandmarkStamp, ZoneWave } from "../mapgen/openfield.ts";

/** Hero animation ids used in the hero track. Indexes into Plan.hero.animNames. */
export interface HeroTrack {
  /** Samples per world-second. */
  rate: number;
  /** World time of sample 0. */
  w0: number;
  x: Float32Array;
  y: Float32Array;
  /** Hop height above the ground, in world pixels. */
  z: Float32Array;
  facing: Uint8Array;
  anim: Uint8Array;
  frame: Uint8Array;
  animNames: string[];
  /** Afterimage strength 0–1 (dashes). */
  trail: Float32Array;
}

export interface CameraTrack {
  rate: number;
  w0: number;
  x: Float32Array;
  y: Float32Array;
  zoom: Float32Array;
}

export interface Fate {
  w: number;
  x: number;
  y: number;
  /** Index into Plan.hits. */
  hit: number;
  lethal: boolean;
  /** Damage this hit dealt (after the groove bonus). */
  damage: number;
}

export interface EnemyRecord {
  id: number;
  type: string;
  /** Health at spawn. */
  health: number;
  spawnW: number;
  /** World time of death, or of leaving the field (recycled), or Infinity. */
  deathW: number;
  /** Recycled: it left the field far behind the hero without dying (no death animation). */
  recycled?: boolean;
  /** Offset of this enemy's (x, y) pairs in Plan.enemySamples, in samples. */
  sampleOffset: number;
  sampleCount: number;
  /** World time of the first stored sample. */
  sampleW0: number;
  /** A live recording's own samples for this enemy (sampleOffset is then 0). */
  samples?: Float32Array;
  fates: Fate[];
  /** Formation members follow analytic paths; their samples are still baked. */
  formation?: number;
  /** Spawn has a visible effect (boss landing, summoning). */
  entrance?: "land" | "summon";
  /** Boss slams or archer releases: wind-up (or draw) start and release, as world times. */
  attacks?: { w0: number; w: number; kind: "shoot" | "slam"; dir: number }[];
  /** Planned dance (kick) times for formation members. */
  dance?: { w0: number; w1: number; anim: string; alternate: boolean; phase: number };
}

/** The sword (a swing), an automatic weapon, a cleave's shockwave band, or a finale-blast band. */
export type HitSource = "move" | "weapon" | "wave" | "ring";

/** The small health pool: a hurt (a bite, an arrow or a slam), and the world time the hero is invulnerable until after it. */
export interface BiteRecord {
  s: number;
  w: number;
  source: "bite" | "arrow" | "slam";
  enemy: string;
  damage: number;
  until: number;
}

/** The small health pool: a heal (the health it gave, after the maximum) and where it came from. */
export interface HealRecord {
  s: number;
  amount: number;
  source: "chicken" | "level-up" | "max-health";
}

/** The small health pool: a chicken dropped (song and world time, place), and when it was eaten and healed. */
export interface ChickenRecord {
  s: number;
  w: number;
  x: number;
  y: number;
  sTaken?: number;
  wTaken?: number;
  sHeal?: number;
}

/** The upgrade pool: one item, a new weapon, an owned weapon's evolution, or a passive. */
export interface UpgradeItem {
  kind: "weapon" | "evolve" | "passive";
  id: string;
}

/** The upgrade pool: a level-up's offers and the one taken (an index into them, -1 if none), at song time s. */
export interface UpgradeRecord {
  s: number;
  offers: UpgradeItem[];
  taken: number;
  /**
   * Upgrade orbs: where each offer's orb rose (world time w), when one was taken (song and world
   * time; walked into, or else the one ahead after two bars), and the grid time it applied.
   */
  w?: number;
  orbs?: { x: number; y: number }[];
  sTaken?: number;
  wTaken?: number;
  sApply?: number;
  walked?: boolean;
}

export interface Hit {
  id: number;
  /** Song and world time of the impact. */
  s: number;
  w: number;
  source: HitSource;
  /** Move or weapon id. */
  what: string;
  weight: "light" | "medium" | "heavy";
  primary: boolean;
  shape: Shape;
  /** Direction of the hit (radians). */
  dir: number;
  energy: number;
  accent: number;
  section: number;
  intensity: number;
  /** Damage dealt to each enemy caught (after the groove bonus). */
  damage: number;
  /** Hits of one cleave shockwave share a group: each enemy takes at most one of them. */
  group?: number;
}

export interface EffectEvent {
  recipe: string;
  /** Song and world time of the start. */
  s: number;
  w: number;
  x: number;
  y: number;
  dir: number;
  intensity: number;
  seed: number;
  /** Hit index for hitShape-aligned sprites. */
  hit?: number;
  /** Debris colors for tint: debris. */
  debris?: string[];
  /** Scale for shape-dependent effects (e.g. slam radius). */
  scale?: number;
}

export interface SoundEvent {
  sample: string;
  s: number;
  gain: number;
  pan: number;
  /** Playback rate (1 = original pitch). */
  rate: number;
  priority: number;
  /** Duck the music under this sound. */
  duck?: boolean;
  /**
   * Seconds before `s` at which the sample starts, so that its perceived onset
   * (a slow attack, a swell) is heard at `s`.
   */
  lead?: number;
  /** Grid sounds: the emission key (kill, hit-light, weapon-blades...), and the slot it moved from when known too late for it. */
  key?: string;
  movedFrom?: number;
}

export interface GemEvent {
  /** World time and position of the drop. */
  w0: number;
  x0: number;
  y0: number;
  /** Song and world time of arrival at the hero (Infinity for a gem on the field not yet picked up). */
  s1: number;
  w1: number;
  value: number;
  big: boolean;
  seed: number;
  /** Gems on the field: when it took off for the hero (world time). */
  wT?: number;
}

export type UiEvent =
  | { kind: "banner"; s: number; text: string; dur: number }
  | { kind: "title"; s: number; until: number }
  | { kind: "levelUp"; s: number; weapon: string }
  | { kind: "perfect"; s: number; x: number; y: number }
  | { kind: "weaponFire"; s: number; weapon: string }
  /** The boss's health bar: song time and remaining health (0–1) after each hit. */
  | { kind: "bossBar"; s: number; until: number; health: [number, number][]; name: string }
  | { kind: "end"; s: number };

export interface Projectile {
  kind: "arrow";
  /** World times of release and of passing out of range. */
  w0: number;
  w1: number;
  x0: number;
  y0: number;
  x1: number;
  y1: number;
  /** World time of the near miss. */
  wMiss: number;
}

export interface SlamMarker {
  w0: number;
  w1: number;
  x: number;
  y: number;
  radius: number;
}

export interface Chain {
  /** Weapon firing: lightning bolt segments, each shown from its jump time. */
  points: { w: number; x: number; y: number }[];
}

export interface HudSeries {
  /** Song times of each kill (sorted). */
  kills: ArrayLike<number>;

  perfect: ArrayLike<number>;
  /** Song time of each gem arrival and the XP bar fraction right after it. */
  xpTimes: ArrayLike<number>;
  xpFill: ArrayLike<number>;

  levelUps: number[];
  /** Song times each weapon fired, for icon flashes. */
  weaponFires: Record<string, ArrayLike<number>>;
}

export interface MapProp {
  sprite: string;
  variant: number;
  x: number;
  y: number;
  tall: boolean;
  flip: boolean;
  light?: { color: string; radius: number };
}

export interface MapZone {
  id: string;
  /** Route distance range the zone covers. */
  d0: number;
  d1: number;
}

/** The level's map: the open field's zones in order (its ground is generated in chunks as it is seen). */
export interface MapLayout {
  zones: MapZone[];
  hash: string;
}

export interface Plan {
  version: string;
  seed: number;
  song: string;
  duration: number;
  warp: WarpBreak[];
  /** World beat grid. */
  worldBeats: Float64Array;
  songBeats: Float64Array;
  /** Song-map energy (0–1) per song beat. */
  energy: Float32Array;
  /** Beat index of each bar start (1-based bars). */
  barStart: number[];
  hero: HeroTrack;
  camera: CameraTrack;
  hits: Hit[];
  enemies: EnemyRecord[];
  /** (x, y) pairs at enemySampleRate per world-second. */
  enemySamples: Float32Array;
  enemySampleRate: number;
  events: { effects: EffectEvent[]; sounds: SoundEvent[]; gems: GemEvent[]; ui: UiEvent[] };
  projectiles: Projectile[];
  slams: SlamMarker[];
  chains: Chain[];
  /** Per bucket of activeBucket world-seconds: ids of enemies alive (or dying) then. */
  activeBucket: number;
  activeEnemies: Uint32Array[];
  /** Per bucket: indices of effect events alive then. */
  activeEffects: Uint32Array[];
  hud: HudSeries;
  map: MapLayout;
  /** Per section: start/end song time, label, zone, zoom, intensity, feel, palette. */
  sections: PlanSection[];
  /** Weapons in the order they were granted. */
  weapons: { id: string; s: number }[];
  /** Song times of accents with the camera punch flag. */
  accents: { s: number; strength: number; punch: boolean }[];
  title: { piece: string; composer: string; performer: string };
  /** The virtual player's skill profile. */
  profile: string;
  /** Every timing-grid time (song time): swings strike on these. */
  grid: Float64Array;
  /** Sword swings in strike order. */
  swings: PlanSwing[];
  /** Every key press and release, in time order. */
  inputs: PlanInput[];
  /** The groove meter (0–1) after each change, at song times. */
  groove: { s: ArrayLike<number>; v: ArrayLike<number> };
  /** Gems on the field: the vacuums that follow set pieces, and the pickup radius by tier (px). */
  vacuums: { s: number; w: number; x: number; y: number; sTouch?: number; wTouch?: number; gems?: number }[];
  pickupRadius: number[];
  /** The upgrade pool: each level-up's offers and the one taken. */
  upgrades: UpgradeRecord[];
  /** The small health pool: every hurt, every heal, and the chickens. */
  bites: BiteRecord[];
  heals: HealRecord[];
  chickens: ChickenRecord[];
  /** Press cleaves: each cleave a press on an eligible accent launched (see Cleave in src/sim/recorder.ts). */
  cleaves: { s: number; press: number; band0: number; launch?: number; onset?: true }[];
  /** The tier (0–3) after each change, at song times; it starts at 0. */
  tier: { s: ArrayLike<number>; v: ArrayLike<number> };
  /** The hero's health (fraction of its maximum) after each change; it starts full. */
  health: { s: ArrayLike<number>; v: ArrayLike<number> };
  /** Near misses. */
  nearMisses: { s: number; w: number; kind: "arrow" | "slam" }[];
  /** Every threat (arrow or boss slam) and how it ended. */
  threats: { s: number; kind: "arrow" | "slam"; outcome: "near-miss" | "touch" | "miss" }[];
  /** The boss, and when it died (if it did). */
  boss?: { id: number; deathS?: number };
  /** Formations: their releases and members (enemy ids). */
  formations: { def: string; sRelease: number; members: number[] }[];
  /** Song-time beat index of every sixteenth slot is beatIndex × 4. */
  stats: PlanStats;
  /** The open field: its transform waves and landmarks. */
  open: OpenFieldRecord;
  /** Grid times whose accent a counted press of Great or better turns into a cleave (song time, strength). */
  cleaveAccents: { s: number; strength: number }[];
  /** Level-ups at their scheduled musical moments. */
  levelUpsDue: { id: string; s: number }[];
  /**
   * Reactive mode (stage 3): the stream's ticks (time, evidence, energy, each known at its time) and
   * the accent flashes (strong onsets' known-at times, at most one every 0.5 s), for the frames.
   */
  reactiveFx?: { gate: number; tickT: Float64Array; evidence: Float32Array; energy: Float32Array; flashes: Float64Array };
  /** Reactive mode with hindsight grids: each press's settled judgment, 2 s after it. */
  settles?: SettleRecord[];
  /** Reactive mode: when each song beat (a committed beat) became known; the HUD shows a beat only from then. */
  beatsKnownAt?: Float64Array;
  /** Reactive mode: the audio file's length for the progress bar, given apart from the stream (its song map's duration ends the run). */
  progressS?: number;
  /**
   * Fixed-speed strides: the walk speed (px per world second), a stride's duration and hop height,
   * the stride period without a steady beat, and the steps a held key strides on (song times) with
   * the stride period at each and how many steps their grid step has (a stomp cancels that many).
   */
  strides: { speed: number; durationS: number; hop: number; freePeriodS: number; steps: Float64Array; periods: Float64Array; per?: Uint8Array };
}

export interface OpenFieldRecord {
  /** Zone index (into map.zones) before the first wave. */
  firstZone: number;
  waves: ZoneWave[];
  landmarks: LandmarkStamp[];
}

export interface AimSnapshot {
  /** Hero position and facing (radians) at the start of the wind-up. */
  x: number;
  y: number;
  facing: number;
  /** Enemies near enough to matter: x, y, body radius and aim weight per enemy. */
  targets: number[];
}

export interface PlanSwing {
  /** Song and world time of the strike (on the grid). */
  s: number;
  w: number;
  /** Song time the swing was armed (by itself) and its wind-up began. */
  sArmed: number;
  sWind: number;
  kind: "light" | "heavy";
  anim: string;
  impactFrame: number;
  /** Aim direction (radians), chosen at the start of the wind-up. */
  dir: number;
  /** Hero position at the strike. */
  x: number;
  y: number;
  /** Index into Plan.hits. */
  hit: number;
  /** Accent strength at the strike (0 if none). */
  accent: number;
  aim: AimSnapshot;
  /** The tier read when the wind-up started, and the reach its aim used (a light swing's arc; a cleave's aim, past its shockwave). */
  tier?: number;
  reach?: { radius: number; degrees: number };
}

export interface PlanInput {
  s: number;
  w: number;
  kind: "press" | "release";
  /** A direction, or Space (a stomp). */
  key: "up" | "down" | "left" | "right" | "space";
  /** Rated presses (step movement): the rating and offset from the grid (song seconds). */
  rating?: "perfect" | "great" | "good";
  delta?: number;
  /** A diagonal's second key, which bent the step instead of starting a new one. */
  bend?: boolean;
  /** A press that is not counted (an off-beat, a double tap or a stray press): it strides, unrated. */
  extra?: boolean;
  /** An off-beat: a press rated against the half-way point between grid times. */
  offbeat?: "perfect" | "great";
  /** Strides: the length of the stride this press started (px; absent for a bend, which keeps the stride's). */
  stride?: number;
  /** Press classes: what the press is, judged against the grid (see PressClass in src/sim/timing.ts). */
  class?: "partner" | "offbeat" | "counted" | "double" | "stray";
  /** Reactive mode: pressed while the beat was unclear (evidence below the gate), so not judged; it strides and changes nothing. */
  unjudged?: true;
  /** Strides: the press made a stomp (Space, or the second of two opposite keys within 40 ms). */
  stomp?: boolean;
  /** Strides: the stomp turned the stride in progress into a rock step back to where it began. */
  rock?: boolean;
  /** Step movement (true) or free movement (false). */
  steps: boolean;
}

/**
 * Reactive mode: a press's settled judgment (docs/design/reactive-audio.md, "Stage 2"). Ratings are
 * labels as the key display names them: perfect, great, good, loose, offbeat-perfect,
 * offbeat-great, double, stray, partner or unjudged.
 */
export interface SettleRecord {
  /** When it settled (the press's time plus 2 s), and the press's time. */
  s: number;
  p: number;
  provisional: string;
  settled: string;
  /** The groove change it made, and the reading it settled on (source and name), with whether it switched. */
  change: number;
  reading: string;
  switched: boolean;
  /** The hindsight entry's time, and the evidence at the press. */
  knownAt: number;
  evidence: number;
  /** The settled reading's own judgment, when the provisional one was kept as better (the benefit of the doubt). */
  kept?: string;
}

export interface PlanSection {
  label: string;
  population: [number, number];
  s0: number;
  s1: number;
  zone: string;
  feel: "pulsed" | "free";
  movement: "steps" | "free";
  intensity: number;
  sfxPalette: string;
  key?: string;
  banner?: string;
}

export interface PlanStats {
  peakPopulation: number;
  killsPerSection: Record<string, number>;
  totalHitStop: number;
  enemies: number;
  hits: number;
  swings: number;
  presses: number;
  ratings: { perfect: number; great: number; good: number; none: number };
  /** Mean groove over step sections. */
  averageGroove: number;
  /** The lowest the hero's health fell (fraction of its maximum), and when it ran out, if it did. */
  minHealth: number;
  deathS?: number;
  planMs: number;
  simMs: number;
}

export type { Facing };

/**
 * A press that belongs to the one before it rather than counting on its own: a diagonal's second
 * key (a bend), or the second of two opposite keys (a stomp's rock step).
 */
export const partnerPress = (i: PlanInput): boolean => !!i.bend || (!!i.rock && i.key !== "space");
