/**
 * Reactive mode's settled judgment (docs/design/reactive-audio.md, "Stage 2: Fair Judging"): each
 * press is judged again 2 s after it, against a reading of the latest hindsight grid chosen from
 * the player's own presses. A pure step, so its state (the beat registry, the settled memory and
 * the incumbent reading) lives in the simulation's state and snapshots carry it.
 */
import type { HeroDef } from "../content/schema.ts";
import { classifyPress, freshPressMemo, type KeyGroup, type PressJudgement, type PressMemo } from "../sim/timing.ts";

/** How long after its press a press settles. */
export const SETTLE_DELAY = 2;

/** The reading's knobs (design: the window, the margin, the significance factor and the hit window may be tuned). */
export const READING = {
  /** Presses from this long before the entry's time choose. */
  windowS: 8,
  margin: 0.1,
  significance: 3,
  hitS: 0.07,
  /** With fewer presses than this, the incumbent stays. */
  minPresses: 8,
  /** Beat identity: a grid time this close to a registered beat is that beat (or a quarter of the grid's shortest spacing if less). */
  matchS: 0.07,
};

export type ReadingName = "as tracked" | "off-beat" | "double" | "half A" | "half B";
export type GridSource = "offline" | "online";

export interface HindsightEntry {
  knownAt: number;
  offline: number[];
  online: number[];
}

export interface Candidate {
  source: GridSource;
  reading: ReadingName;
  beats: number[];
}

/** An entry's ten candidate grids: five readings of each of its two beat lists. */
export function candidates(entry: HindsightEntry): Candidate[] {
  const out: Candidate[] = [];
  for (const source of ["offline", "online"] as const) {
    const d = entry[source];
    const mids = d.slice(1).map((b, i) => (b + d[i]) / 2);
    out.push(
      { source, reading: "as tracked", beats: d },
      { source, reading: "off-beat", beats: mids },
      { source, reading: "double", beats: [...d, ...mids].sort((a, b) => a - b) },
      { source, reading: "half A", beats: d.filter((_, i) => i % 2 === 0) },
      { source, reading: "half B", beats: d.filter((_, i) => i % 2 === 1) },
    );
  }
  return out;
}

function nearestDist(times: readonly number[], t: number): number {
  if (times.length === 0) return Infinity;
  let lo = 0;
  let hi = times.length - 1;
  while (hi - lo > 1) {
    const m = (lo + hi) >> 1;
    if (times[m] <= t) lo = m;
    else hi = m;
  }
  return Math.min(Math.abs(times[lo] - t), Math.abs(times[hi] - t));
}

/** A candidate's score for presses: its hit share less the share random presses would hit. */
export function score(c: Candidate, presses: readonly number[], windowS = READING.windowS): { hits: number; chance: number; score: number } {
  const hits = presses.length ? presses.filter((p) => nearestDist(c.beats, p) <= READING.hitS + 1e-9).length / presses.length : 0;
  const chance = Math.min(1, 2 * READING.hitS * (c.beats.length / windowS));
  return { hits, chance, score: hits - chance };
}

/** The tolerance for matching beats of a grid: 70 ms, or a quarter of its shortest spacing if less. */
export function matchTolerance(beats: readonly number[]): number {
  let shortest = Infinity;
  for (let i = 1; i < beats.length; i++) shortest = Math.min(shortest, beats[i] - beats[i - 1]);
  return Math.min(READING.matchS, shortest / 4);
}

/**
 * How well grid a agrees with grid b where they overlap: a's beats within the tolerance of one of
 * b's, less those that are not (a grid holding b's beats and as many more agrees no better than
 * one holding them alone).
 */
function agreement(a: readonly number[], b: readonly number[], tol: number): number {
  if (a.length === 0 || b.length === 0) return 0;
  const lo = Math.max(a[0], b[0]) - tol;
  const hi = Math.min(a[a.length - 1], b[b.length - 1]) + tol;
  let n = 0;
  for (const x of a) if (x >= lo && x <= hi) n += nearestDist(b, x) <= tol ? 1 : -1;
  return n;
}

/** A candidate's source and reading, as the settle records name it. */
export const label = (c: Candidate) => `${c.source} ${c.reading}`;

export interface Reading {
  chosen: Candidate;
  /** Did it replace the incumbent? */
  switched: boolean;
}

/**
 * The reading a press settles on: the incumbent (the candidate agreeing best with the previous
 * settle's grid; offline "as tracked" at first or with no overlap), replaced by the best-scoring candidate
 * only when it beats it by more than the margin and the significance test, with enough presses.
 */
export function chooseReading(entry: HindsightEntry, presses: readonly number[], previous: readonly number[] | undefined, previousLabel?: string): Reading {
  const cands = candidates(entry);
  let incumbent = cands[0];
  if (previous && previous.length > 0) {
    const agree = cands.map((c) => agreement(c.beats, previous, matchTolerance(c.beats.length > 1 ? c.beats : previous)));
    const best = Math.max(0, ...agree);
    if (best > 0) incumbent = cands[agree.indexOf(best)];
    // (Of candidates agreeing about as well, the previous one's source and reading: a re-decode moves
    // a beat or two, and another list's reading holding the same beats is no change of reading.)
    const same = cands.findIndex((c) => label(c) === previousLabel);
    if (best > 0 && same >= 0 && agree[same] >= best - Math.max(1, 0.1 * best)) incumbent = cands[same];
  }
  const n = presses.length;
  if (n < READING.minPresses) return { chosen: incumbent, switched: false };
  // (Early in the track the window is shorter than windowS: densities are per second of it.)
  const span = Math.max(1, Math.min(READING.windowS, entry.knownAt));
  const si = score(incumbent, presses, span);
  let top = incumbent;
  let st = si;
  for (const c of cands) {
    const sc = score(c, presses, span);
    if (sc.score > st.score + 1e-12) {
      top = c;
      st = sc;
    }
  }
  if (top === incumbent) return { chosen: incumbent, switched: false };
  const d = st.score - si.score;
  const sig = READING.significance * Math.sqrt((st.hits * (1 - st.hits) + si.hits * (1 - si.hits)) / n);
  return d > READING.margin && d > sig ? { chosen: top, switched: true } : { chosen: incumbent, switched: false };
}

/** A press as the settle step sees it. */
export interface SettlePress {
  s: number;
  partner: boolean;
  group: KeyGroup;
  /** Unjudged at the press (the evidence below the gate): it settles unjudged too. */
  unclear: boolean;
}

export interface Settled {
  /** Undefined: unjudged. */
  judgement?: PressJudgement;
  reading: Reading;
  /** The entry's time. */
  knownAt: number;
}

/**
 * The settle step's state: the beat registry (times settled judging has seen, by identity), the
 * settled memory (counted presses and taken off-beats by beat identity), and the latest reading's grid.
 */
export class Settler {
  registry: number[] = [];
  memo: PressMemo = freshPressMemo();
  incumbent?: number[];
  incumbentLabel?: string;

  /** The identity of a grid time: a registered beat within the tolerance, or a new one. */
  identity(t: number, tol: number): number {
    for (let i = this.registry.length - 1; i >= 0; i--) if (Math.abs(this.registry[i] - t) <= tol) return i;
    this.registry.push(t);
    return this.registry.length - 1;
  }

  /**
   * Settle a press against an entry: the reading from the window's presses (not this one), then
   * play mode's classes against the chosen grid, remembered by beat identity. Unjudged when the
   * beat was unclear at the press, the entry has no beats, or the grid has no beat within one of its
   * own periods of the press.
   */
  settle(hero: HeroDef, press: SettlePress, entry: HindsightEntry, windowPresses: readonly number[]): Settled {
    const reading = chooseReading(entry, windowPresses, this.incumbent, this.incumbentLabel);
    this.incumbent = reading.chosen.beats;
    this.incumbentLabel = label(reading.chosen);
    const grid = reading.chosen.beats;
    const base = { reading, knownAt: entry.knownAt };
    if (press.unclear || grid.length < 2) return base;
    let lo = 0;
    while (lo + 1 < grid.length && grid[lo + 1] <= press.s) lo++;
    const hi = Math.min(grid.length - 1, lo + 1);
    const period = grid[hi] - grid[lo] || grid[1] - grid[0];
    if (nearestDist(grid, press.s) > period + 1e-9) return base;
    const tol = matchTolerance(grid);
    const ids = new Map<number, number>();
    const keyOf = (gi: number) => {
      let id = ids.get(gi);
      if (id === undefined) {
        id = this.identity(grid[gi], tol);
        ids.set(gi, id);
      }
      return id;
    };
    return { ...base, judgement: classifyPress(hero, grid, press.s, press.partner, this.memo, press.group, keyOf) };
  }
}

/** A judgment as the key display names it (unjudged without one). */
export function judgementLabel(j: PressJudgement | undefined): string {
  if (!j) return "unjudged";
  if (j.class === "counted") return j.rating ?? "loose";
  if (j.class === "offbeat") return `offbeat-${j.rating}`;
  return j.class;
}
