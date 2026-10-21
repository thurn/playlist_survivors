/**
 * The virtual player: a deterministic program that plays like a highly skilled
 * human. It knows the song (the press pattern, the timing grid, the route
 * pace), as a practiced player would, and sees only the current game state
 * when it acts. It never sees the future of the simulation.
 */
import type { HeroDef } from "../content/schema.ts";
import { Rng } from "../core/rng.ts";
import { clamp } from "../core/shaping.ts";
import { KEY_DIR, type Key } from "./movement.ts";
import type { GridSlot } from "./schedule.ts";

export interface Observation {
  /** Song time now. */
  s: number;
  hero: { x: number; y: number; stepping: boolean };
  enemies: { x: number; y: number; type: string; health: number }[];
  threats: { kind: "arrow" | "slam"; x: number; y: number; dir?: number; landsAt: number; radius?: number }[];
  /** Where the route schedule says the hero should be now, and the route's direction there. */
  pacePoint: { x: number; y: number; tx: number; ty: number };
  /** Distance from a point to the edge of the walkable band (negative outside). */
  edgeDistance: (x: number, y: number) => number;
}

export interface KeyEvent {
  s: number;
  kind: "press" | "release";
  key: Key;
}

export interface Player {
  /** Song time of the player's next decision after `after` (Infinity when it is done). */
  nextDecision(after: number): number;
  /** Decide at the observation's song time: the key events it makes before its next decision. */
  decide(obs: Observation): KeyEvent[];
}

export type SkillProfile = "perfect" | "skilled" | "casual";

export interface ProfileSpec {
  /** Standard deviation and clamp of the timing error, in seconds. */
  sd: number;
  clampS: number;
  /** Casual players press downbeats only. */
  downbeatsOnly: boolean;
}

export const PROFILES: Record<SkillProfile, ProfileSpec> = {
  perfect: { sd: 0, clampS: 0, downbeatsOnly: false },
  skilled: { sd: 0.012, clampS: 0.04, downbeatsOnly: false },
  casual: { sd: 0.045, clampS: 0.15, downbeatsOnly: true },
};

/** Sections as the player knows them: the press pattern and step movement. */
export interface PlayerSection {
  steps: boolean;
  presses: string;
}

/** The eight step directions: one key, or two perpendicular keys for a diagonal. */
const DIRECTIONS: Key[][] = [["right"], ["right", "down"], ["down"], ["down", "left"], ["left"], ["left", "up"], ["up"], ["up", "right"]];

function dirVector(keys: Key[]): { x: number; y: number } {
  let x = 0;
  let y = 0;
  for (const k of keys) {
    x += KEY_DIR[k].x;
    y += KEY_DIR[k].y;
  }
  const l = Math.hypot(x, y) || 1;
  return { x: x / l, y: y / l };
}

/** Normal deviate from two uniforms (Box–Muller). */
function normal(r: Rng): number {
  const u = Math.max(1e-12, r.next());
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * r.next());
}

export class VirtualPlayer implements Player {
  /** Planned press times (song time) and the grid slot each one aims at. */
  private presses: { s: number; slot: GridSlot }[] = [];
  private next = 0;
  private lastDir: { x: number; y: number } | undefined;
  private count = 0;

  constructor(
    private readonly hero: HeroDef,
    private readonly seed: number,
    slots: readonly GridSlot[],
    sections: readonly PlayerSection[],
    profile: SkillProfile = "skilled",
  ) {
    const spec = PROFILES[profile];
    for (const sl of slots) {
      const sec = sections[sl.section];
      if (!sec.steps) continue;
      const pressed = spec.downbeatsOnly ? sl.slot === 0 : sec.presses[sl.slot % sec.presses.length] === "x";
      if (!pressed) continue;
      const r = new Rng(seed, "press-time", Math.round(sl.s * 1000));
      const err = spec.sd > 0 ? clamp(normal(r) * spec.sd, -spec.clampS, spec.clampS) : 0;
      this.presses.push({ s: sl.s + err, slot: sl });
    }
    this.presses.sort((a, b) => a.s - b.s);
  }

  nextDecision(after: number): number {
    while (this.next < this.presses.length && this.presses[this.next].s < after - 1e-9) this.next++;
    return this.next < this.presses.length ? this.presses[this.next].s : Infinity;
  }

  decide(obs: Observation): KeyEvent[] {
    // The press planned for now.
    while (this.next < this.presses.length && this.presses[this.next].s < obs.s - 1e-9) this.next++;
    if (this.next >= this.presses.length) return [];
    this.next++;
    const keys = this.chooseKeys(obs);
    this.count++;
    const r = new Rng(this.seed, "press-keys", this.count);
    const out: KeyEvent[] = [];
    let s = obs.s;
    for (let i = 0; i < keys.length; i++) {
      // A diagonal's second key follows within a few milliseconds.
      if (i > 0) s += r.range(0.002, 0.015);
      out.push({ s, kind: "press", key: keys[i] });
    }
    for (const k of keys) out.push({ s: s + r.range(0.06, 0.1), kind: "release", key: k });
    return out;
  }

  /** Score the eight directions by where the step would leave the hero, and pick the lowest cost. */
  private chooseKeys(obs: Observation): Key[] {
    const L = this.hero.step.length;
    const p = obs.pacePoint;
    const nx = -p.ty;
    const ny = p.tx;
    let best: Key[] = DIRECTIONS[0];
    let bestCost = Infinity;
    const r = new Rng(this.seed, "press-noise", this.count);
    for (const keys of DIRECTIONS) {
      const d = dirVector(keys);
      const ax = obs.hero.x + d.x * L;
      const ay = obs.hero.y + d.y * L;
      // Keep up with the route: along the route (stay within the pace window) and across it.
      const along = (ax - p.x) * p.tx + (ay - p.y) * p.ty;
      const lat = (ax - p.x) * nx + (ay - p.y) * ny;
      let cost = along > 0 ? (along / 30) ** 2 : (along / 70) ** 2;
      cost += (lat / 160) ** 2;
      // Leave slam markers and arrow lines.
      for (const t of obs.threats) {
        const rad = (t.radius ?? 30) + 14;
        if (Math.hypot(ax - t.x, ay - t.y) < rad) cost += 50;
      }
      // Stay off the walls.
      const edge = obs.edgeDistance(ax, ay);
      cost += 2.0 * clamp(1 - edge / 60, 0, 2);
      // Don't get buried, but stay in the fight.
      let near = 0;
      let close = 0;
      for (const e of obs.enemies) {
        const dd = Math.hypot(e.x - ax, e.y - ay);
        if (dd < 30) close++;
        if (dd < 100) near++;
      }
      cost += 0.5 * Math.min(close, 6) * 0.5;
      cost -= 0.8 * Math.min(near, 15) / 5;
      // Avoid back-and-forth jitter.
      if (this.lastDir && d.x * this.lastDir.x + d.y * this.lastDir.y < -0.7) cost += 0.6;
      // A little variety.
      cost += r.next() * 0.2;
      if (cost < bestCost) {
        bestCost = cost;
        best = keys;
      }
    }
    this.lastDir = dirVector(best);
    // Which of a diagonal's two keys comes first is a matter of habit.
    if (best.length === 2 && r.next() < 0.5) return [best[1], best[0]];
    return best;
  }
}
