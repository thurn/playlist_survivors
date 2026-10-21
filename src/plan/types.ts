/**
 * The plan: the planner's complete, immutable timeline of a level. The
 * runtime evaluates it at any song time without stepping state.
 */
import type { Shape } from "../core/geometry.ts";
import type { WarpBreak } from "../core/warp.ts";
import type { Facing } from "../assets/types.ts";

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
}

export interface EnemyRecord {
  id: number;
  type: string;
  spawnW: number;
  deathW: number;
  /** Offset of this enemy's (x, y) pairs in Plan.enemySamples, in samples. */
  sampleOffset: number;
  sampleCount: number;
  /** World time of the first stored sample. */
  sampleW0: number;
  fates: Fate[];
  /** Formation members follow analytic paths; their samples are still baked. */
  formation?: number;
  /** Spawn has a visible effect (boss landing, summoning). */
  entrance?: "land" | "summon";
  /** Boss slams or archer releases, as world times. */
  attacks?: { w: number; kind: "shoot" | "slam"; dir: number }[];
  /** Planned dance (kick) times for formation members. */
  dance?: { w0: number; w1: number; anim: string; alternate: boolean; phase: number };
}

export type HitSource = "move" | "weapon" | "sweep" | "ring";

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
  | { kind: "bossBar"; s: number; until: number; hits: number[]; name: string }
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
  kills: Float64Array;
  /** Song times of each on-beat hero action (sorted). */
  combo: Float64Array;
  perfect: Float64Array;
  /** Song time of each gem arrival and the XP bar fraction right after it. */
  xpTimes: Float64Array;
  xpFill: Float32Array;
  /** Hero action markers for the beat track: song time and size (1 light … 3 heavy). */
  markers: { s: number; size: number; kind: "attack" | "dodge" | "weapon" }[];
  levelUps: number[];
  /** Song times each weapon fired, for icon flashes. */
  weaponFires: Record<string, Float64Array>;
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
  /** Hero actions (primary events) in time order. */
  actions: PlanAction[];
  /** Near misses (primary events). */
  nearMisses: { s: number; w: number; kind: "arrow" | "slam" }[];
  /** Song-time beat index of every sixteenth slot is beatIndex × 4. */
  stats: PlanStats;
}

export interface PlanAction {
  s: number;
  /** Beat index of the musical slot the action was chosen for. */
  bi: number;
  w: number;
  kind: string;
  move: string;
  weight: "light" | "medium" | "heavy";
  anim: string;
  impactFrame: number;
  hit?: number;
  x: number;
  y: number;
  dir: number;
}

export interface PlanSection {
  label: string;
  population: [number, number];
  s0: number;
  s1: number;
  zone: string;
  feel: "pulsed" | "free";
  intensity: number;
  sfxPalette: string;
  key?: string;
  banner?: string;
}

export interface PlanStats {
  peakPopulation: number;
  killsPerSection: Record<string, number>;
  totalHitStop: number;
  longestGapWithoutPrimary: number;
  enemies: number;
  hits: number;
  planMs: number;
  /** Enemies that lunged more than 24 px in their final blend after every crowd re-run. */
  stragglers: number;
}

export type { Facing };
