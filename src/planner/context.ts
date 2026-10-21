/**
 * Shared planner state. Each stage reads the outputs of earlier stages from
 * the context and adds its own.
 */
import type { ContentLibrary, LevelScript, ResolvedSection } from "../content/loader.ts";
import type { MoveDef, MovesetDef } from "../content/schema.ts";
import type { Shape } from "../core/geometry.ts";
import { Rng } from "../core/rng.ts";
import { type SongIndex, type SongMap, type SongSection, indexSong, timeOfBeatIndex, beatIndexAt } from "../core/songmap.ts";
import type { Warp } from "../core/warp.ts";
import type { CameraTrack, EnemyRecord, HeroTrack, MapLayout } from "../plan/types.ts";

export class PlanError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "PlanError";
  }
}

/** Per-animation facts the planner needs from the hero atlas. */
export interface AnimInfo {
  frames: number;
  impactFrame?: number;
}

export interface PlannerInput {
  songMap: SongMap;
  level: LevelScript;
  content: ContentLibrary;
  seed: number;
  /** Hero animation frame counts and impact frames, from the character atlas. */
  heroAnims?: Record<string, AnimInfo>;
  /** Tiled landmark maps by id (parsed JSON). */
  landmarks?: Record<string, unknown>;
  /** Seconds from each sound sample's start to its perceived onset, from the sound manifest. */
  soundOnsets?: Record<string, number>;
  /** Build version, mixed into the plan hash. */
  version?: string;
}

export interface SectionInfo {
  index: number;
  label: string;
  song: SongSection;
  level: ResolvedSection;
  moveset: MovesetDef;
  s0: number;
  s1: number;
  /** Beat index range [b0, b1). */
  b0: number;
  b1: number;
}

export interface Slot {
  s: number;
  /** Fractional beat index. */
  bi: number;
  bar: number;
  /** Position in the bar, in grid steps. */
  slotInBar: number;
  section: number;
  energy: number;
  accent: number;
  accentLabel?: string;
  kind: "grid" | "anchor" | "continuous";
  anchorStrength?: number;
}

export type ActionKind = "attack" | "dodge" | "counter" | "sweep" | "finisher" | "blast";

/** A primary hero action, chosen in song time. */
export interface HeroAction {
  id: number;
  s: number;
  /** Fractional beat index of the impact. */
  bi: number;
  move: MoveDef;
  kind: ActionKind;
  section: number;
  energy: number;
  accent: number;
  accentLabel?: string;
  /** Threat this dodge escapes. */
  threat?: number;
  /** Hits the boss. */
  bossHit?: boolean;
  /** World time, set in Stage 3. */
  w: number;
  /** Direction in radians, set in Stage 4. */
  dir: number;
  /** Hero position at impact, set in Stage 4. */
  x: number;
  y: number;
  /** For dash-type moves: hero position when the move started. */
  x0: number;
  y0: number;
  /** Shape at impact, set in Stage 6. */
  shape?: Shape;
  /** Index into hits, set in Stage 6. */
  hit?: number;
  /** For sweeps: the formation being swept. */
  formation?: number;
  /** For dodges: when the evasive offset may start to relax. */
  holdUntilS?: number;
  /** Is this action on-beat (counts toward the combo)? */
  combo: boolean;
}

export interface WeaponFiring {
  weapon: string;
  s: number;
  bi: number;
  /** Subdivision step in beats, for moving out of hit-stops. */
  step: number;
  section: number;
  energy: number;
  w: number;
  /** Index into chains for chain lightning. */
}

export interface Threat {
  id: number;
  kind: "arrow" | "slam";
  /** Release (arrows) or telegraph start (slams), song time. */
  sRelease: number;
  sDodge: number;
  /** Near miss: arrow arrival or slam impact. */
  sMiss: number;
  /** Bearing relative to the route tangent (radians); the side the threat comes from. */
  bearing: number;
  archers?: number;
  dodgeAction?: number;
  counterAction?: number;
}

export interface FormationPlan {
  id: number;
  def: string;
  /** Song time the formation starts entering. */
  sStart: number;
  /** Release song time. */
  sRelease: number;
  section: number;
  members: number[];
  sweepAction?: number;
}

export interface BossPlan {
  type: string;
  sEnter: number;
  sDeath: number;
  hits: number;
  slams: number;
  enemy?: number;
}

export interface Ctx {
  input: PlannerInput;
  song: SongMap;
  idx: SongIndex;
  level: LevelScript;
  lib: ContentLibrary;
  seed: number;
  rng: (...k: (number | string)[]) => Rng;
  sections: SectionInfo[];
  duration: number;
  heroAnims: Record<string, AnimInfo>;
  soundOnsets: Record<string, number>;
  // Stage 1
  slots: Slot[][];
  events: ResolvedEvent[];
  // Stage 2
  actions: HeroAction[];
  firings: WeaponFiring[];
  threats: Threat[];
  formations: FormationPlan[];
  boss?: BossPlan;
  weaponsGranted: { id: string; s: number }[];
  blastS?: number;
  blastWeapon?: string;
  timeScaleEvents: { s: number; rate: number }[];
  // Stage 3
  warp?: Warp;
  worldBeats?: Float64Array;
  /** Route progress at world time, sampled at routeRate Hz from w=0. */
  routeProgress?: Float64Array;
  routeRate: number;
  // Map
  map?: MapLayout;
  // Stage 4
  hero?: HeroTrack;
  // Stage 5
  camera?: CameraTrack;
  // Stage 6–7
  enemies: EnemyRecord[];
}

export interface ResolvedEvent {
  index: number;
  s: number;
  line?: number;
  ev: LevelScript["events"][number];
}

export const DEFAULT_HERO_ANIMS: Record<string, AnimInfo> = {
  idle: { frames: 2 },
  walk: { frames: 9 },
  run: { frames: 8 },
  jump: { frames: 5, impactFrame: 3 },
  slash: { frames: 6, impactFrame: 3 },
  backslash: { frames: 13, impactFrame: 6 },
  halfslash: { frames: 6, impactFrame: 3 },
  thrust: { frames: 8, impactFrame: 5 },
  spellcast: { frames: 7, impactFrame: 4 },
  "slash-oversize": { frames: 6, impactFrame: 3 },
  hurt: { frames: 6 },
};

export function makeCtx(input: PlannerInput): Ctx {
  const song = input.songMap;
  const idx = indexSong(song);
  const level = input.level;
  const lib = input.content;
  const sections: SectionInfo[] = level.sections.map((ls, i) => {
    const ss = song.sections.find((x) => x.label === ls.label)!;
    const moveset = lib.movesets.get(ls.moveset)!;
    const b0 = idx.barStart[ss.bars[0]];
    const b1 = ss.bars[1] + 1 <= idx.barCount ? idx.barStart[ss.bars[1] + 1] : song.beats.length;
    return {
      index: i,
      label: ls.label,
      song: ss,
      level: ls,
      moveset,
      s0: i === 0 ? 0 : timeOfBeatIndex(song, b0),
      s1: timeOfBeatIndex(song, b1),
      b0,
      b1,
    };
  });
  // The last section runs to the end of the audio.
  sections[sections.length - 1].s1 = Math.max(sections[sections.length - 1].s1, song.audio.duration);
  const seed = input.seed;
  return {
    input,
    song,
    idx,
    level,
    lib,
    seed,
    rng: (...k) => new Rng(seed, ...k),
    sections,
    duration: song.audio.duration,
    heroAnims: { ...DEFAULT_HERO_ANIMS, ...(input.heroAnims ?? {}) },
    soundOnsets: input.soundOnsets ?? {},
    slots: [],
    events: [],
    actions: [],
    firings: [],
    threats: [],
    formations: [],
    weaponsGranted: [],
    timeScaleEvents: [],
    routeRate: 120,
    enemies: [],
  };
}

export function sectionIndexAt(ctx: Ctx, s: number): number {
  const secs = ctx.sections;
  for (let i = secs.length - 1; i >= 0; i--) if (s >= secs[i].s0 - 1e-9) return i;
  return 0;
}

export function beatDurationAt(ctx: Ctx, s: number): number {
  const bi = beatIndexAt(ctx.song, s);
  const k = Math.max(0, Math.min(ctx.song.beats.length - 2, Math.floor(bi)));
  return ctx.song.beats[k + 1] - ctx.song.beats[k];
}

/** Steps per beat for a subdivision, given the bar length at that point. */
export function subdivisionBeats(sub: string, beatsPerBar: number): number {
  switch (sub) {
    case "sixteenth":
      return 0.25;
    case "eighth":
      return 0.5;
    case "beat":
      return 1;
    case "bar":
      return beatsPerBar;
    case "two-bar":
      return beatsPerBar * 2;
    default:
      return 1;
  }
}

export function weightFactor(w: "light" | "medium" | "heavy"): number {
  return w === "light" ? 0.4 : w === "medium" ? 0.7 : 1.0;
}
