/** Musical time: resolve level-script references (bars, phrases, named accents) to song time. */
import type { MusicalRef } from "../content/schema.ts";
import { beatIndexAt, timeOf, timeOfBeatIndex, type SongIndex } from "../core/songmap.ts";
import { PlanError, type Ctx } from "./context.ts";

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

/** Resolve every level-script event to song time, in time order. */
export function resolveEvents(ctx: Ctx): void {
  ctx.events = ctx.level.events
    .map((ev, index) => ({ index, s: resolveRef(ctx.idx, ev.at), line: ctx.level.eventLines[index], ev }))
    .sort((a, b) => a.s - b.s || a.index - b.index);
}
