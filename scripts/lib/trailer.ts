/**
 * The trailer's pure parts (scripts/trailer.ts runs them): the spec (trailer/*.yaml), the edit's
 * timeline from output time to each run's song time, and the audio edit.
 */
import { readFileSync } from "node:fs";
import { parse } from "yaml";
import { z } from "zod";
import { indexSong, timeOf, type SongIndex, type SongMap } from "../../src/core/songmap.ts";

/** A song position: seconds, or "bar" / "bar:beat" (1-based, the beat may be fractional) in a run with a song map. */
const pos = z.union([z.number(), z.string().regex(/^\d+(:\d+(\.\d+)?)?$/, 'a song position is seconds, "bar" or "bar:beat"')]);

const run = z.strictObject({
  /** The game's URL parameters for the run filmed (after ?test). */
  query: z.string(),
  /** The run's song map (songs/<name>), for positions in bars; the filmed run must play it (as its song, or its reactive track). */
  songMap: z.string().optional(),
  /** The run's sound, before the edit's blend (to match one run's loudness to another's). */
  gain: z.number().positive().default(1),
});

export const trailerSpec = z.strictObject({
  title: z.string(),
  width: z.number().int().positive().default(1920),
  height: z.number().int().positive().default(1080),
  fps: z.number().int().positive().default(60),
  /** The runs filmed, by name. */
  runs: z.record(z.string(), run),
  /**
   * The edit: song ranges of the runs played back to back, picture and sound together. A join
   * crossfades over `crossfadeMs` (the cut's own, or the spec's), centred on the join: the
   * picture dissolves and the sound crossfades at equal power.
   */
  cuts: z.array(z.strictObject({ run: z.string(), from: pos, to: pos, crossfadeMs: z.number().min(0).optional() })).min(1),
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

/** A cut resolved: its run, its song range, where it starts in the output, and the half-length of the crossfade into it. */
export interface Cut {
  run: string;
  from: number;
  to: number;
  t0: number;
  half: number;
}

/** One run's picture at an output time: its song time, and its weight over the layers beneath it. */
export interface Layer {
  run: string;
  s: number;
  w: number;
}

/** The spec's edit resolved against the runs' song maps. */
export class Timeline {
  readonly cuts: Cut[] = [];
  readonly duration: number;

  constructor(
    readonly spec: TrailerSpec,
    songMaps: Record<string, SongMap>,
  ) {
    const index = new Map<string, SongIndex>();
    for (const [name, r] of Object.entries(spec.runs)) if (r.songMap) index.set(name, indexSong(songMaps[r.songMap]));
    const song = (runName: string, p: Pos): number => {
      if (typeof p === "number") return p;
      const idx = index.get(runName);
      if (!idx) throw new Error(`run ${runName} has no song map, so its positions are seconds (not "${p}")`);
      const [bar, beat] = p.split(":").map(Number);
      return timeOf(idx, { bar, beat: beat ?? 1 });
    };
    let t = 0;
    spec.cuts.forEach((c, i) => {
      if (!spec.runs[c.run]) throw new Error(`cut ${i + 1}: no run "${c.run}" (runs: ${Object.keys(spec.runs).join(", ")})`);
      const from = song(c.run, c.from);
      const to = song(c.run, c.to);
      if (!(to > from)) throw new Error(`cut ${i + 1} (${c.from} to ${c.to}) is empty`);
      const half = i === 0 ? 0 : (c.crossfadeMs ?? spec.crossfadeMs) / 2000;
      this.cuts.push({ run: c.run, from, to, t0: t, half });
      t += to - from;
    });
    this.duration = t;
    // Each crossfade stays inside both its cuts, clear of the next one.
    this.cuts.forEach((c, i) => {
      const next = this.cuts[i + 1]?.half ?? 0;
      if (c.half + next > c.to - c.from) throw new Error(`cut ${i + 1} is shorter than the crossfades into and out of it`);
    });
  }

  /** The cut playing at output time t (ignoring crossfades). */
  cutAt(t: number): number {
    let k = 0;
    this.cuts.forEach((c, i) => {
      if (t >= c.t0) k = i;
    });
    return k;
  }

  /**
   * The picture at output time t, bottom layer first: one run, or in a crossfade the outgoing cut
   * (played on past its end) under the incoming one (from before its start) at a rising weight.
   */
  layersAt(t: number): Layer[] {
    const k = this.cutAt(t);
    const c = this.cuts[k];
    const at = (cut: Cut, w: number): Layer => ({ run: cut.run, s: cut.from + (t - cut.t0), w });
    const next = this.cuts[k + 1];
    if (next && next.half > 0 && t >= next.t0 - next.half) return [at(c, 1), at(next, (t - (next.t0 - next.half)) / (2 * next.half))];
    if (c.half > 0 && t < c.t0 + c.half && k > 0) return [at(this.cuts[k - 1], 1), at(c, (t - (c.t0 - c.half)) / (2 * c.half))];
    return [at(c, 1)];
  }
}

/** Interleaved stereo float samples. */
export interface Stereo {
  sampleRate: number;
  data: Float32Array;
}

/** A run's sound: the game's full mix and its music alone. */
export interface RunAudio {
  mix: Stereo;
  music: Stereo;
}

/**
 * The edit's audio: each cut of its run's music blended toward the run's mix by `sfx`, joined
 * with equal-power crossfades centred on each join, then faded in and out.
 */
export function editAudio(timeline: Timeline, runs: Record<string, RunAudio>): Stereo {
  const sr = Object.values(runs)[0].mix.sampleRate;
  for (const [name, a] of Object.entries(runs)) if (a.mix.sampleRate !== sr || a.music.sampleRate !== sr) throw new Error(`run ${name}'s audio is not at ${sr} Hz`);
  const { audio, fadeIn, fadeOut } = timeline.spec;
  const n = Math.round(timeline.duration * sr);
  const out = new Float32Array(n * 2);
  timeline.cuts.forEach((c, ci) => {
    const { mix, music } = runs[c.run];
    const gain = audio.gain * timeline.spec.runs[c.run].gain;
    const sample = (frame: number, ch: number) => {
      if (frame < 0 || frame * 2 + ch >= mix.data.length) return 0;
      const m = music.data[frame * 2 + ch];
      return gain * (m + audio.sfx * (mix.data[frame * 2 + ch] - m));
    };
    const o0 = Math.round(c.t0 * sr);
    const len = Math.round((c.to - c.from) * sr);
    const src0 = Math.round(c.from * sr);
    const hIn = Math.round(c.half * sr);
    const hOut = Math.round((timeline.cuts[ci + 1]?.half ?? 0) * sr);
    for (let i = -hIn; i < len + hOut; i++) {
      const o = o0 + i;
      if (o < 0 || o >= n) continue;
      // Gain: rises over the crossfade at the start, falls over the one at the end (equal power).
      let g = 1;
      if (i < hIn) g *= Math.sin((Math.PI / 4) * (1 + (i + 0.5) / hIn));
      if (i >= len - hOut) g *= Math.cos((Math.PI / 4) * (1 + (i - len + 0.5) / hOut));
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
