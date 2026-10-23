/**
 * Play mode driven by scripted key events, for the movement tests: a simulation on a song, the
 * hero probed at song times, and a copy of the synthetic song with one section slowed down.
 */
import { plannerInput } from "../../scripts/lib/plan-node.ts";
import type { SongMap } from "../../src/core/songmap.ts";
import { prepare } from "../../src/planner/plan.ts";
import type { InputKey } from "../../src/sim/movement.ts";
import type { KeyEvent } from "../../src/sim/player.ts";
import { BatchRecorder } from "../../src/sim/recorder.ts";
import { Sim, worldFor } from "../../src/sim/simulate.ts";

const LEVEL = "levels/orpheus-galop.play.yaml";

/** A copy of the synthetic song with one section slowed to `bpm` (later times shift with it). */
export function slowSection(song: SongMap, label: string, bpm: number): SongMap {
  const sec = song.sections.find((x) => x.label === label)!;
  const bar0 = sec.bars[0];
  const bar1 = sec.bars[1] + 1;
  const beatsBefore = (bar: number) => (bar <= 15 ? (bar - 1) * 4 : 14 * 4 + (bar - 15) * 2);
  const a = song.beats[beatsBefore(bar0)];
  const b = song.beats[beatsBefore(bar1)];
  const k = 60 / bpm / (song.beats[beatsBefore(bar0) + 1] - a);
  const f = (t: number) => (t <= a ? t : t <= b ? a + (t - a) * k : t + (b - a) * (k - 1));
  const end = f(song.audio.duration);
  return { ...song, beats: song.beats.map(f), accents: song.accents.map((x) => ({ ...x, time: f(x.time) })), audio: { ...song.audio, cropEnd: end, duration: end } };
}

/** A simulation of play mode on a song, with the hero driven by `keys`; probe the hero at song times. */
export function play(song: SongMap) {
  const { ctx, sch } = prepare(plannerInput({ songMap: song, level: LEVEL }));
  const section = (label: string) => ctx.sections.find((s) => s.label === label)!;
  const run = (keys: KeyEvent[], probes: number[], rec = new BatchRecorder()) => {
    const sim = new Sim(ctx, sch, worldFor(), rec);
    const t0 = Math.min(...keys.map((k) => k.s), ...probes) - 0.01;
    sim.advanceTo(t0);
    for (const k of keys) sim.key(k);
    const at = probes.map((t) => {
      sim.advanceTo(t);
      return { ...sim.heroPosition(), w: sim.w };
    });
    return { at, inputs: rec.inputs.filter((i) => i.kind === "press") };
  };
  return { ctx, sch, section, run };
}

export const tap = (s: number, key: InputKey = "right"): KeyEvent[] => [
  { s, kind: "press", key },
  { s: s + 0.03, kind: "release", key },
];

/** Distances between successive probes, and the speed between the first and last (px per world second). */
export function travel(at: { x: number; y: number; w: number }[]) {
  const steps = at.slice(1).map((p, i) => Math.hypot(p.x - at[i].x, p.y - at[i].y));
  const last = at[at.length - 1];
  return { steps, speed: Math.hypot(last.x - at[0].x, last.y - at[0].y) / (last.w - at[0].w) };
}
