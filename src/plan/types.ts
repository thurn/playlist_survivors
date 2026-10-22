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
}

export interface GemEvent {
  /** World time and position of the drop. */
  w0: number;
  x0: number;
  y0: number;
  /** Song and world time of arrival at the hero. */
  s1: number;
  w1: number;
  value: number;
  big: boolean;
  seed: number;
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

export interface MapLayout {
  /** Tile grid origin in world pixels and size in tiles. */
  originX: number;
  originY: number;
  cols: number;
  rows: number;
  /** Tile id index per tile (into tileIds), and variant. */
  tiles: Uint16Array;
  tileVariants: Uint8Array;
  tileIds: string[];
  /** Zone index (into zones) for each tile, for tinting. */
  tileZone: Uint8Array;
  /** Route polyline sampled every routeStep pixels of distance. */
  routeStep: number;
  routeX: Float32Array;
  routeY: Float32Array;
  routeLength: number;
  corridorHalfWidth: number;
  zones: MapZone[];
  /** Ground patches and short props (drawn on the ground layer), and tall props (depth-sorted). */
  props: MapProp[];
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
  /** The pace point's route distance, sampled `rate` times per song second. */
  pace: { rate: number; d: Float32Array };
  /** Every timing-grid time (song time): swings strike on these. */
  grid: Float64Array;
  /** Sword swings in strike order. */
  swings: PlanSwing[];
  /** Every key press and release, in time order. */
  inputs: PlanInput[];
  /** The groove meter (0–1) after each change, at song times. */
  groove: { s: ArrayLike<number>; v: ArrayLike<number> };
  /** Near misses. */
  nearMisses: { s: number; w: number; kind: "arrow" | "slam" }[];
  /** Every threat (arrow or boss slam) and how it ended. */
  threats: { s: number; kind: "arrow" | "slam"; outcome: "near-miss" | "touch" | "miss" }[];
  /** The boss: its death chord and when it died. */
  boss?: { id: number; chordS: number; deathS?: number };
  /** Formations: their releases and members (enemy ids). */
  formations: { def: string; sRelease: number; members: number[] }[];
  /** Song-time beat index of every sixteenth slot is beatIndex × 4. */
  stats: PlanStats;
  /** Play mode's open field (absent in the demo's corridor): its transform waves and landmarks. */
  open?: OpenFieldRecord;
  /** Grid times whose accent a swing armed by a press rated Great or better turns into a cleave (song time, strength). */
  cleaveAccents?: { s: number; strength: number }[];
  /** Level-ups at their scheduled musical moments. */
  levelUpsDue?: { id: string; s: number }[];
  /** Play mode's rule changes (absent in the demo). */
  rules?: { oneStepPerSlot: boolean; accentTelegraphs: boolean };
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
  /** Song time the swing was armed (its press, or the automatic trigger) and its wind-up began. */
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
  /** Index into Plan.inputs of the press that armed it (undefined for automatic swings). */
  press?: number;
  /** Accent strength at the strike (0 if none). */
  accent: number;
  aim: AimSnapshot;
}

export interface PlanInput {
  s: number;
  w: number;
  kind: "press" | "release";
  key: "up" | "down" | "left" | "right";
  /** Rated presses (step movement): the rating, accuracy and offset from the grid (song seconds). */
  rating?: "perfect" | "great" | "good";
  accuracy?: number;
  delta?: number;
  /** A diagonal's second key, which bent the step instead of starting a new one. */
  bend?: boolean;
  /** One step per grid slot: an extra press for a grid time that already has its step (it does nothing). */
  ignored?: boolean;
  /** Did this press arm a swing? */
  armed?: boolean;
  /** Step movement (true) or free movement (false). */
  steps: boolean;
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
  planMs: number;
  simMs: number;
}

export type { Facing };
