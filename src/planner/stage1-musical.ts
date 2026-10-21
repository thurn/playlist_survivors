/** Stage 1: resolve musical references to song time and build each section's slots. */
import type { MusicalRef } from "../content/schema.ts";
import { beatIndexAt, beatIndexOf, energyAt, timeOf, timeOfBeatIndex, type SongIndex } from "../core/songmap.ts";
import { PlanError, subdivisionBeats, type Ctx, type Slot } from "./context.ts";

/** Phrases (bar ranges) inside a section, in order. */
export function sectionPhrases(idx: SongIndex, label: string): [number, number][] {
  const sec = idx.map.sections.find((s) => s.label === label);
  if (!sec) return [];
  return idx.map.phrases.filter((p) => p[0] >= sec.bars[0] && p[1] <= sec.bars[1]).sort((a, b) => a[0] - b[0]);
}

/** The strongest accent in a phrase's last bar, or the first beat of that bar. */
export function phraseFinalAccent(idx: SongIndex, phrase: [number, number]): number {
  const bar = phrase[1];
  const t0 = timeOf(idx, { bar, beat: 1 });
  const t1 = timeOf(idx, { bar: bar + 1, beat: 1 });
  let best: number | undefined;
  let bestStrength = -1;
  for (const a of idx.map.accents) {
    if (a.time >= t0 - 0.02 && a.time < t1 - 0.02 && a.strength > bestStrength) {
      best = a.time;
      bestStrength = a.strength;
    }
  }
  return best ?? t0;
}

/** Resolve a musical reference to song time. */
export function resolveRef(idx: SongIndex, ref: MusicalRef): number {
  let t: number;
  if ("bar" in ref) {
    t = timeOf(idx, { bar: ref.bar, beat: ref.beat ?? 1 });
  } else if ("section" in ref) {
    const sec = idx.map.sections.find((s) => s.label === ref.section);
    if (!sec) throw new PlanError(`unknown section "${ref.section}"`);
    let bars: [number, number] = sec.bars;
    if (ref.phrase !== undefined) {
      const ph = sectionPhrases(idx, ref.section);
      const p = ph[ref.phrase - 1];
      if (!p) throw new PlanError(`section "${ref.section}" has no phrase ${ref.phrase}`);
      bars = p;
    }
    if (ref.point === "start") t = timeOf(idx, { bar: bars[0], beat: 1 });
    else if (ref.point === "end") t = timeOf(idx, { bar: bars[1] + 1, beat: 1 });
    else t = phraseFinalAccent(idx, bars);
  } else {
    const matching = idx.map.accents.filter((a) => a.label === ref.accent).sort((a, b) => a.time - b.time);
    const n = ref.fromEnd ?? ref.index ?? 1;
    const a = ref.fromEnd !== undefined ? matching[matching.length - n] : matching[n - 1];
    if (!a) throw new PlanError(`no accent "${ref.accent}" #${n}`);
    t = a.time;
  }
  if (ref.offsetBeats) t = timeOfBeatIndex(idx.map, beatIndexAt(idx.map, t) + ref.offsetBeats);
  return t;
}

/** Accent matched to a grid slot: the nearest slot within 40 ms. */
function matchAccents(ctx: Ctx, slots: Slot[]): void {
  for (const a of ctx.song.accents) {
    let best: Slot | undefined;
    let bestD = 0.04;
    for (const sl of slots) {
      const d = Math.abs(sl.s - a.time);
      if (d <= bestD) {
        best = sl;
        bestD = d;
      }
    }
    if (best && a.strength > best.accent) {
      best.accent = a.strength;
      best.accentLabel = a.label;
    }
  }
}

export function stage1(ctx: Ctx): void {
  const { idx, song } = ctx;
  ctx.events = ctx.level.events
    .map((ev, index) => ({ index, s: resolveRef(idx, ev.at), line: ctx.level.eventLines[index], ev }))
    .sort((a, b) => a.s - b.s || a.index - b.index);

  ctx.slots = ctx.sections.map((sec) => {
    const slots: Slot[] = [];
    if (sec.song.feel === "pulsed") {
      const grid = sec.moveset.grid ?? "beat";
      for (let bar = sec.song.bars[0]; bar <= sec.song.bars[1]; bar++) {
        const bpb = idx.beatsPerBar[bar] ?? 2;
        const step = subdivisionBeats(grid, bpb);
        const n = Math.max(1, Math.round(bpb / step));
        const b0 = beatIndexOf(idx, { bar, beat: 1 });
        for (let k = 0; k < n; k++) {
          const bi = b0 + k * step;
          if (bi > song.beats.length - 1) continue;
          const s = timeOfBeatIndex(song, bi);
          slots.push({ s, bi, bar, slotInBar: k, section: sec.index, energy: energyAt(song, s), accent: 0, kind: "grid" });
        }
      }
      matchAccents(ctx, slots);
    } else {
      // Free: continuous-move times plus anchors.
      const every = sec.moveset.continuous?.everyBeats ?? 2;
      for (let bi = sec.b0 + every; bi < sec.b1 - 0.5; bi += every) {
        const s = timeOfBeatIndex(song, bi);
        slots.push({ s, bi, bar: 0, slotInBar: 0, section: sec.index, energy: energyAt(song, s), accent: 0, kind: "continuous" });
      }
      for (const a of sec.song.anchors ?? []) {
        if (a.time < sec.s0 || a.time >= sec.s1) continue;
        const bi = beatIndexAt(song, a.time);
        slots.push({ s: a.time, bi, bar: 0, slotInBar: 0, section: sec.index, energy: energyAt(song, a.time), accent: 0, kind: "anchor", anchorStrength: a.strength });
      }
      slots.sort((a, b) => a.s - b.s);
    }
    return slots;
  });
}
