/**
 * The trailer's pure parts (scripts/trailer.ts runs them): the spec (trailer/*.yaml), the edit's
 * timeline from output time to song time, and the audio edit.
 */
import { readFileSync } from "node:fs";
import { parse } from "yaml";
import { z } from "zod";
import { indexSong, timeOf, type SongIndex, type SongMap } from "../../src/core/songmap.ts";

/** A song position: seconds, or "bar" / "bar:beat" (1-based, the beat may be fractional). */
const pos = z.union([z.number(), z.string().regex(/^\d+(:\d+(\.\d+)?)?$/, 'a song position is seconds, "bar" or "bar:beat"')]);

export const trailerSpec = z.strictObject({
  title: z.string(),
  song: z.string(),
  /** The game's URL parameters for the run filmed (after ?test). */
  query: z.string(),
  width: z.number().int().positive().default(1920),
  height: z.number().int().positive().default(1080),
  fps: z.number().int().positive().default(60),
  /** The edit: song ranges played back to back, picture and sound together. */
  cuts: z.array(z.strictObject({ from: pos, to: pos })).min(1),
  /** The sound's crossfade at each join (the picture cuts). */
  crossfadeMs: z.number().min(0).default(20),
  /** Fades from and to black (and silence), in seconds. */
  fadeIn: z.number().min(0).default(0),
  fadeOut: z.number().min(0).default(1),
  /**
   * The game's sound effects as a blend between the music alone (0) and the game's own mix (1),
   * and an overall gain. (The game's mix runs through its limiter, so the effects cannot be
   * separated from it exactly.)
   */
  audio: z.strictObject({ sfx: z.number().min(0).max(1).default(1), gain: z.number().positive().default(1) }).default({ sfx: 1, gain: 1 }),
});

export type TrailerSpec = z.infer<typeof trailerSpec>;
export type Pos = z.infer<typeof pos>;

export function readTrailerSpec(path: string): TrailerSpec {
  const r = trailerSpec.safeParse(parse(readFileSync(path, "utf8")));
  if (!r.success) throw new Error(`${path}:\n${r.error.issues.map((i) => `  ${i.path.join(".")}: ${i.message}`).join("\n")}`);
  return r.data;
}

const clamp01 = (x: number) => (x < 0 ? 0 : x > 1 ? 1 : x);

/** The spec's edit resolved against the song: each cut in song seconds and where it starts in the output. */
export class Timeline {
  readonly idx: SongIndex;
  readonly cuts: { from: number; to: number; t0: number }[] = [];
  readonly duration: number;

  constructor(
    readonly spec: TrailerSpec,
    map: SongMap,
  ) {
    this.idx = indexSong(map);
    let t = 0;
    for (const c of spec.cuts) {
      const from = this.song(c.from);
      const to = this.song(c.to);
      if (!(to > from)) throw new Error(`cut ${JSON.stringify(c)} is empty`);
      this.cuts.push({ from, to, t0: t });
      t += to - from;
    }
    this.duration = t;
  }

  /** Song seconds of a position. */
  song(p: Pos): number {
    if (typeof p === "number") return p;
    const [bar, beat] = p.split(":").map(Number);
    return timeOf(this.idx, { bar, beat: beat ?? 1 });
  }

  /** Song seconds at output time t. */
  songAt(t: number): number {
    let c = this.cuts[0];
    for (const x of this.cuts) if (t >= x.t0) c = x;
    return c.from + (t - c.t0);
  }
}

/** Interleaved stereo float samples. */
export interface Stereo {
  sampleRate: number;
  data: Float32Array;
}

/**
 * The edit's audio: each cut of the music blended toward the game's mix by `sfx`, joined with
 * equal-power crossfades centred on each join, then faded in and out.
 */
export function editAudio(timeline: Timeline, mix: Stereo, music: Stereo): Stereo {
  const sr = mix.sampleRate;
  const { audio, crossfadeMs, fadeIn, fadeOut } = timeline.spec;
  const n = Math.round(timeline.duration * sr);
  const out = new Float32Array(n * 2);
  const half = Math.round((crossfadeMs / 1000) * sr * 0.5);
  const sample = (frame: number, ch: number) => {
    if (frame < 0 || frame * 2 + ch >= mix.data.length) return 0;
    const m = music.data[frame * 2 + ch];
    return audio.gain * (m + audio.sfx * (mix.data[frame * 2 + ch] - m));
  };
  timeline.cuts.forEach((c, ci) => {
    const o0 = Math.round(c.t0 * sr);
    const len = Math.round((c.to - c.from) * sr);
    const src0 = Math.round(c.from * sr);
    const first = ci === 0;
    const lastCut = ci === timeline.cuts.length - 1;
    for (let i = first ? 0 : -half; i < len + (lastCut ? 0 : half); i++) {
      const o = o0 + i;
      if (o < 0 || o >= n) continue;
      // Gain: rises over the crossfade at the start, falls over the one at the end (equal power).
      let g = 1;
      if (!first && i < half) g *= Math.sin((Math.PI / 4) * (1 + (i + 0.5) / half));
      if (!lastCut && i >= len - half) g *= Math.cos((Math.PI / 4) * (1 + (i - len + 0.5) / half));
      out[o * 2] += g * sample(src0 + i, 0);
      out[o * 2 + 1] += g * sample(src0 + i, 1);
    }
  });
  for (let f = 0; f < n; f++) {
    const t = f / sr;
    const g = Math.min(fadeIn > 0 ? clamp01(t / fadeIn) : 1, fadeOut > 0 ? clamp01((timeline.duration - t) / fadeOut) : 1);
    out[f * 2] *= g;
    out[f * 2 + 1] *= g;
  }
  return { sampleRate: sr, data: out };
}

/** A 32-bit float WAV file of interleaved stereo samples. */
export function wavBytes(a: Stereo): Buffer {
  const bytes = a.data.length * 4;
  const b = Buffer.alloc(44 + bytes);
  b.write("RIFF", 0);
  b.writeUInt32LE(36 + bytes, 4);
  b.write("WAVEfmt ", 8);
  b.writeUInt32LE(16, 16);
  b.writeUInt16LE(3, 20);
  b.writeUInt16LE(2, 22);
  b.writeUInt32LE(a.sampleRate, 24);
  b.writeUInt32LE(a.sampleRate * 8, 28);
  b.writeUInt16LE(8, 32);
  b.writeUInt16LE(32, 34);
  b.write("data", 36);
  b.writeUInt32LE(bytes, 40);
  Buffer.from(a.data.buffer, a.data.byteOffset, bytes).copy(b, 44);
  return b;
}

/** Reads a WAV written by wavBytes. */
export function readWav(b: Buffer): Stereo {
  const sampleRate = b.readUInt32LE(24);
  const bytes = b.readUInt32LE(40);
  const data = new Float32Array(bytes / 4);
  Buffer.from(data.buffer).set(b.subarray(44, 44 + bytes));
  return { sampleRate, data };
}
