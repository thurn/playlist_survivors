/**
 * A reactive level's director (docs/design/reactive-audio.md, "The reactive level" and "Stage 4:
 * Structure"): what drives the horde in place of the song map's sections. Everything here reads
 * the stream only as known by the time it is asked about (a tick from its time, a detected section
 * from its downbeat).
 */
import type { Director } from "../content/schema.ts";
import type { DetectedSection } from "./sections.ts";
import { effectiveDownbeat, firstBeatAtOrAfter, tickIndexAt, type ReactiveStream, type TickLevel } from "./stream.ts";

/** The target population at song time s: min(max, (start + perMinute * s / 60) * scale). */
export function directorPopulation(d: Director, s: number, scale: number): number {
  const p = d.population;
  return Math.min(p.max, (p.start + (p.perMinute * s) / 60) * scale);
}

/** The roster of the k-th detected section (0 before the first), holding the last entry. */
export function sectionRoster(d: Director, k: number): Record<string, number> {
  return d.sections[Math.min(k, d.sections.length - 1)];
}

/**
 * The mean of `value` over the loudness levels of the ticks in the last `windowS` before s (each
 * tick's level holding until the next), so a level's effect changes smoothly over that window.
 */
export function levelBlend(stream: ReactiveStream, s: number, windowS: number, value: (l: TickLevel) => number): number {
  const ticks = stream.ticks;
  let i = tickIndexAt(stream, s);
  if (i < 0) return value("medium");
  const t0 = s - windowS;
  let sum = 0;
  let hi = s;
  for (; i >= 0 && hi > t0 + 1e-12; i--) {
    const lo = Math.max(ticks[i].t, t0);
    sum += (hi - lo) * value(ticks[i].level ?? "medium");
    hi = lo;
  }
  // (Before the first tick: medium.)
  if (hi > t0 + 1e-12) sum += (hi - t0) * value("medium");
  return sum / windowS;
}

/** The key of the latest tick at or before s that has one (none before 10 s, or without keys). */
export function keyAt(stream: ReactiveStream, s: number): string | undefined {
  for (let i = tickIndexAt(stream, s); i >= 0; i--) if (stream.ticks[i].key) return stream.ticks[i].key;
  return undefined;
}

export interface PaletteSwitch {
  /** The committed downbeat it takes effect on, and the level whose palette it brings. */
  s: number;
  level: TickLevel;
}

const paletteMemo = new WeakMap<ReactiveStream, Map<number, PaletteSwitch[]>>();

/**
 * The sound palette's switches: once the level has differed from the palette's for `holdS`, the
 * palette switches to it on the effective downbeat after that moment (the palette starts medium).
 */
export function paletteSwitches(stream: ReactiveStream, holdS: number): PaletteSwitch[] {
  let byHold = paletteMemo.get(stream);
  if (!byHold) paletteMemo.set(stream, (byHold = new Map()));
  const memo = byHold.get(holdS);
  if (memo) return memo;
  const out: PaletteSwitch[] = [];
  let palette: TickLevel = "medium";
  let run: TickLevel | undefined;
  let runStart = 0;
  for (const k of stream.ticks) {
    const lv = k.level ?? "medium";
    if (lv !== run) {
      run = lv;
      runStart = k.t;
    }
    if (lv !== palette && k.t - runStart >= holdS - 1e-9) {
      palette = lv;
      const b = effectiveDownbeat(stream, k.t);
      if (b >= 0) out.push({ s: stream.beats[b].t, level: lv });
    }
  }
  byHold.set(holdS, out);
  return out;
}

/** The level whose sound palette plays at song time s (medium before the first switch). */
export function paletteLevelAt(stream: ReactiveStream, holdS: number, s: number): TickLevel {
  return paletteSwitches(stream, holdS).findLast((p) => p.s <= s + 1e-9)?.level ?? "medium";
}

export interface ZoneWavePlan {
  s: number;
  /** The detected section (1 for the first start) a wave starts, absent for a fallback wave. */
  section?: number;
}

/**
 * The zone waves: one on each detected section start, and one on the first committed beat at or
 * after `fallbackS` without a wave, unless a start is known by then. Whether a fallback wave runs
 * at its time depends only on the starts known by then.
 */
export function zoneWavePlan(stream: ReactiveStream, starts: readonly DetectedSection[], fallbackS: number, endS: number): ZoneWavePlan[] {
  const out: ZoneWavePlan[] = [];
  let last = 0;
  let i = 0;
  for (;;) {
    const next = starts[i];
    // The fallback's beat: a section start on it or before it, or already known by then, is the wave instead.
    const fallback = firstBeatAtOrAfter(stream, last + fallbackS)?.t ?? Infinity;
    if (next && (next.s <= fallback + 1e-9 || next.knownAt <= fallback + 1e-9)) {
      if (next.s >= endS) break;
      out.push({ s: next.s, section: i + 1 });
      last = next.s;
      i++;
    } else if (fallback < endS) {
      out.push({ s: fallback });
      last = fallback;
    } else break;
  }
  return out;
}
