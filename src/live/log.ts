/**
 * Play mode's timing log: every gameplay key from the DOM event to what the
 * simulation did with it, with the audio clock, the frames and the sounds
 * around it, so a press that "did nothing" can be explained after the fact.
 *
 * - keys: each keydown/keyup, whether the keyboard queued it (or why not), its
 *   song time and the audio clock it was read from.
 * - applies: when the runner applied each queued key (late keys, rollbacks).
 * - traces: what the simulation finally did with each key (rollbacks re-run a
 *   press; the latest run is kept, with a count of runs).
 * - frames: every animation frame's timestamp, song time and phase.
 * - sounds: the live sound scheduler's starts, late starts, skips and cancels.
 * - events: phase changes (count-ins, pauses, restarts, the end), focus, flushes.
 * - the context (play.ts): settings, the song's grid, the meter's values, the settled presses and
 *   swings, the groove and tier series, the sounds moved to a later slot (soundMoves: known less
 *   than 60 ms before their own), and the hero's path (heroPath) for its speed.
 *
 * The dev server saves it to logs/play/<id>.json (see vite.config.ts), and
 * `npm run log:play` summarizes it. Elsewhere, F8 downloads it.
 */
import type { Warp } from "../core/warp.ts";
import type { HeroTrack, Plan } from "../plan/types.ts";
import type { PressTrace } from "../sim/recorder.ts";

/** The hero's path sampled every `dt` of song time from s0: world time and position (px). */
export interface HeroPath {
  dt: number;
  s0: number;
  w: number[];
  x: number[];
  y: number[];
}

const r6s = (v: number) => Math.round(v * 1e6) / 1e6;

/** A sound moved to a later slot of its subdivision (rules.gridSounds): known too late for its own. */
export interface SoundMove {
  key: string;
  sample: string;
  from: number;
  s: number;
}

/** Gems on the field (play mode): pickups per sixteenth (song time, gems, experience), the vacuums, and the experience dropped and collected. */
export interface GemLog {
  pickups: { s: number; n: number; xp: number }[];
  vacuums: { s: number; sTouch?: number; gems?: number }[];
  dropped: number;
  collected: number;
}

/** Gems on the field: what the log carries of them (in live play, up to world time `w`: gems still in flight haven't landed). */
export function gemLog(plan: Plan, w = Infinity): GemLog {
  const by = new Map<number, { n: number; xp: number }>();
  let dropped = 0;
  let collected = 0;
  for (const g of plan.events.gems) {
    dropped += g.value;
    if (!Number.isFinite(g.w1) || g.w1 <= 0 || g.w1 > w) continue;
    collected += g.value;
    const p = by.get(g.s1) ?? { n: 0, xp: 0 };
    p.n++;
    p.xp += g.value;
    by.set(g.s1, p);
  }
  const pickups = [...by].sort((a, b) => a[0] - b[0]).map(([t, p]) => ({ s: r6s(t), n: p.n, xp: p.xp }));
  const vacuums = (plan.vacuums ?? []).map((v) => ({ s: r6s(v.s), ...(v.sTouch !== undefined ? { sTouch: r6s(v.sTouch), gems: v.gems ?? 0 } : {}) }));
  return { pickups, vacuums, dropped, collected };
}

/** The upgrade pool (play mode): each level-up's song time, its offers (kind:id) and the one taken; with orbs, when it was taken (walked into, or the one ahead after two bars) and the grid time it applied. */
export interface UpgradeLog {
  s: number;
  offers: string[];
  taken: number;
  sTaken?: number;
  sApply?: number;
  walked?: boolean;
}

/** What the log carries of a recording up to song time s: the groove and tier series, the sounds moved to a later slot, gems on the field, the level-ups' offers, and the hero's path. */
export function recordingContext(plan: Plan, warp: Warp, s: number): { groove: { s: number[]; v: number[] }; tier?: { s: number[]; v: number[] }; soundMoves: SoundMove[]; gems?: GemLog; upgrades?: UpgradeLog[]; heroPath: HeroPath } {
  return {
    ...(plan.rules?.fieldGems ? { gems: gemLog(plan, warp.toWorld(s)) } : {}),
    ...(plan.upgrades
      ? {
          upgrades: plan.upgrades
            .filter((u) => u.s <= s)
            .map((u) => ({ s: r6s(u.s), offers: u.offers.map((o) => `${o.kind}:${o.id}`), taken: u.taken, ...(u.sTaken !== undefined && u.sTaken <= s ? { sTaken: r6s(u.sTaken), sApply: r6s(u.sApply!), walked: !!u.walked } : {}) })),
        }
      : {}),
    soundMoves: plan.events.sounds.filter((x) => x.movedFrom !== undefined).map((x) => ({ key: x.key ?? x.sample, sample: x.sample, from: r6s(x.movedFrom!), s: r6s(x.s) })),
    groove: { s: Array.from(plan.groove.s, r6s), v: Array.from(plan.groove.v, (v) => Math.round(v * 1e4) / 1e4) },
    ...(plan.tier ? { tier: { s: Array.from(plan.tier.s, r6s), v: Array.from(plan.tier.v) } } : {}),
    heroPath: heroPath(plan.hero, warp, 0, s),
  };
}

/** The hero's path from song time s0 to s1, every dt (for the log's speed per second). */
export function heroPath(hero: HeroTrack, warp: Warp, s0: number, s1: number, dt = 0.05): HeroPath {
  const out: HeroPath = { dt, s0, w: [], x: [], y: [] };
  const n = hero.x.length;
  if (n === 0) return out;
  for (let k = 0; s0 + k * dt <= s1 + 1e-9; k++) {
    const w = warp.toWorld(s0 + k * dt);
    const i = Math.max(0, Math.min(n - 1, Math.round((w - hero.w0) * hero.rate)));
    out.w.push(Math.round(w * 1e4) / 1e4);
    out.x.push(Math.round(hero.x[i] * 10) / 10);
    out.y.push(Math.round(hero.y[i] * 10) / 10);
  }
  return out;
}

/** The audio clock's raw readings at one moment. */
export interface ClockProbe {
  /** performance.now() when read. */
  perfNow: number;
  currentTime: number;
  outputLatency: number;
  baseLatency: number;
  /** getOutputTimestamp(): the context time being heard at performanceTime. */
  contextTime?: number;
  performanceTime?: number;
  /** Context time of song time 0. */
  t0: number;
  state: string;
}

export interface KeyLog {
  id: number;
  type: "down" | "up";
  key: string;
  code: string;
  /** The event's timestamp and when the handler ran (performance ms). */
  timeStamp: number;
  handledAt: number;
  repeat: boolean;
  /** queued: it went to the game. Otherwise why not: a key-repeat, input disabled (paused, a menu), already held, or not held (a key-up). */
  fate: "queued" | "repeat" | "disabled" | "held" | "not-held";
  /** Its song time (after the calibration offset), the offset, and the clock it came from. */
  s?: number;
  offsetMs?: number;
  clock?: ClockProbe;
}

export interface ApplyLog {
  id: number;
  /** The frame that applied it: its song time and timestamp. */
  frameS: number;
  frameTs: number;
  /** Song time it was applied at (later than its own when it arrived after the settled time). */
  appliedS: number;
  late: boolean;
  /** The simulation had run past it: it rolled back from this song time to a snapshot at this one. */
  rollbackFrom?: number;
  rollbackTo?: number;
}

/** A key the count-in or a phase without play threw away. */
export interface DropLog {
  id?: number;
  s: number;
  phase: string;
}

export interface SoundLog {
  kind: "scheduled" | "startedLate" | "skipped" | "cancelled";
  sample: string;
  /** Song time of the sound's onset, and the song time when the scheduler acted. */
  s: number;
  songNow: number;
  /** For starts: the context time it starts at, and how far into the sample. */
  at?: number;
  offset?: number;
}

export interface EventLog {
  perf: number;
  s?: number;
  kind: string;
  detail?: unknown;
}

const PHASES = ["countIn", "playing", "paused", "replay"];

/** Frame columns: timestamps (ms), song times (s), and per-frame counts. */
class Frames {
  ts: number[] = [];
  /** performance.now() at the frame's start: how late the callback ran. */
  perf: number[] = [];
  s: number[] = [];
  /** The unsmoothed song time the clock read for this frame. */
  raw: number[] = [];
  phase: number[] = [];
  /** The display period used for presentation. */
  dur: number[] = [];
  /** Keys handed to the runner, and how long the frame's work took. */
  keys: number[] = [];
  ms: number[] = [];
  simS: number[] = [];

  get length(): number {
    return this.ts.length;
  }
  trim(n: number): void {
    for (const k of Object.keys(this) as (keyof Frames)[]) (this[k] as number[]).splice(0, n);
  }
}

const r3 = (v: number) => Math.round(v * 1000) / 1000;
const r6 = (v: number) => Math.round(v * 1e6) / 1e6;

export class PlayLog {
  readonly id: string;
  readonly keys: KeyLog[] = [];
  readonly applies: ApplyLog[] = [];
  readonly drops: DropLog[] = [];
  readonly traces = new Map<number, PressTrace & { runs: number }>();
  readonly frames = new Frames();
  readonly sounds: SoundLog[] = [];
  readonly events: EventLog[] = [];
  private nextId = 1;
  private lastFlush = 0;
  /** Everything else the export carries (the song's grid, settings, the recording's settled inputs). */
  context: () => Record<string, unknown> = () => ({});

  /** At most this many frames (about 11 minutes at 60 Hz) and sounds are kept: the oldest go. */
  static readonly MAX_FRAMES = 40_000;
  static readonly MAX_SOUNDS = 20_000;

  constructor(now = new Date()) {
    this.id = now.toISOString().replace(/[:.]/g, "-");
  }

  newKeyId(): number {
    return this.nextId++;
  }

  key(k: KeyLog): void {
    this.keys.push(k);
  }

  apply(a: ApplyLog): void {
    this.applies.push(a);
  }

  drop(d: DropLog): void {
    this.drops.push(d);
  }

  press(t: PressTrace): void {
    if (t.id === undefined) return;
    const prev = this.traces.get(t.id);
    this.traces.set(t.id, { ...t, runs: (prev?.runs ?? 0) + 1 });
  }

  frame(f: { ts: number; perf: number; s: number; raw: number; phase: string; dur: number; keys: number; ms: number; simS: number }): void {
    const fr = this.frames;
    fr.ts.push(r3(f.ts));
    fr.perf.push(r3(f.perf));
    fr.s.push(r6(f.s));
    fr.raw.push(r6(f.raw));
    fr.phase.push(PHASES.indexOf(f.phase));
    fr.dur.push(r3(f.dur));
    fr.keys.push(f.keys);
    fr.ms.push(r3(f.ms));
    fr.simS.push(r6(f.simS));
    if (fr.length > PlayLog.MAX_FRAMES) fr.trim(fr.length - PlayLog.MAX_FRAMES);
  }

  sound(x: SoundLog): void {
    this.sounds.push(x);
    if (this.sounds.length > PlayLog.MAX_SOUNDS) this.sounds.splice(0, this.sounds.length - PlayLog.MAX_SOUNDS);
  }

  event(kind: string, s?: number, detail?: unknown): void {
    this.events.push({ perf: r3(performance.now()), s, kind, detail });
  }

  toJSON(): Record<string, unknown> {
    return {
      version: 1,
      id: this.id,
      savedAt: new Date().toISOString(),
      userAgent: typeof navigator !== "undefined" ? navigator.userAgent : undefined,
      ...this.context(),
      keys: this.keys,
      applies: this.applies,
      drops: this.drops,
      traces: [...this.traces.values()],
      events: this.events,
      sounds: this.sounds,
      frames: { phases: PHASES, ...this.frames },
    };
  }

  /** Save it: to the dev server's logs/play/ folder (development builds only). */
  async flush(reason: string): Promise<boolean> {
    this.lastFlush = performance.now();
    if (!import.meta.env.DEV) return false;
    this.event("flush", undefined, reason);
    try {
      const r = await fetch(`${import.meta.env.BASE_URL}__playlog?id=${encodeURIComponent(this.id)}`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(this) });
      return r.ok;
    } catch {
      return false;
    }
  }

  /** Flush every `everyMs` while playing (so a crash or a closed tab loses little). */
  maybeFlush(everyMs = 15_000): void {
    if (performance.now() - this.lastFlush >= everyMs) void this.flush("periodic");
  }

  /** On leaving the page: a beacon survives the unload where fetch may not. */
  beacon(): void {
    if (!import.meta.env.DEV || typeof navigator.sendBeacon !== "function") return;
    this.event("flush", undefined, "pagehide");
    navigator.sendBeacon(`${import.meta.env.BASE_URL}__playlog?id=${encodeURIComponent(this.id)}`, new Blob([JSON.stringify(this)], { type: "application/json" }));
  }

  /** Download it as a file (any build). */
  download(): void {
    const url = URL.createObjectURL(new Blob([JSON.stringify(this)], { type: "application/json" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = `play-${this.id}.json`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
