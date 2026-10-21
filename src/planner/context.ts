/**
 * Shared planner state: the inputs, the song index and the resolved sections.
 * The song schedule, the simulation and the emission stage read it.
 */
import type { ContentLibrary, LevelScript, ResolvedSection } from "../content/loader.ts";
import { Rng } from "../core/rng.ts";
import { type SongIndex, type SongMap, type SongSection, indexSong, timeOfBeatIndex, beatIndexAt } from "../core/songmap.ts";

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
  /** The virtual player's skill profile (default: the level script's). */
  profile?: "perfect" | "skilled" | "casual";
}

export interface SectionInfo {
  index: number;
  label: string;
  song: SongSection;
  level: ResolvedSection;
  s0: number;
  s1: number;
  /** Beat index range [b0, b1). */
  b0: number;
  b1: number;
}

export interface ResolvedEvent {
  index: number;
  s: number;
  line?: number;
  ev: LevelScript["events"][number];
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
  /** Level-script events, resolved to song time and sorted. */
  events: ResolvedEvent[];
}

export const DEFAULT_HERO_ANIMS: Record<string, AnimInfo> = {
  idle: { frames: 2 },
  walk: { frames: 9 },
  run: { frames: 8 },
  jump: { frames: 5 },
  slash: { frames: 6, impactFrame: 4 },
  backslash: { frames: 7, impactFrame: 4 },
  halfslash: { frames: 6, impactFrame: 2 },
  thrust: { frames: 8, impactFrame: 5 },
  spellcast: { frames: 7, impactFrame: 4 },
  "slash-oversize": { frames: 6, impactFrame: 4 },
  hurt: { frames: 6 },
};

export function makeCtx(input: PlannerInput): Ctx {
  const song = input.songMap;
  const idx = indexSong(song);
  const level = input.level;
  const lib = input.content;
  const sections: SectionInfo[] = level.sections.map((ls, i) => {
    const ss = song.sections.find((x) => x.label === ls.label)!;
    const b0 = idx.barStart[ss.bars[0]];
    const b1 = ss.bars[1] + 1 <= idx.barCount ? idx.barStart[ss.bars[1] + 1] : song.beats.length;
    return { index: i, label: ls.label, song: ss, level: ls, s0: i === 0 ? 0 : timeOfBeatIndex(song, b0), s1: timeOfBeatIndex(song, b1), b0, b1 };
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
    events: [],
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
