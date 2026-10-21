/**
 * The virtual player: a deterministic program that plays like a highly skilled
 * human. It knows the song (the press pattern, the timing grid, the route
 * pace), as a practiced player would, and sees only the current game state
 * when it acts. It never sees the future of the simulation.
 */
import type { HeroDef } from "../content/schema.ts";
import { Rng } from "../core/rng.ts";
import { clamp } from "../core/shaping.ts";
import { easeOut, KEY_DIR, type Key } from "./movement.ts";
import type { GridSlot } from "./schedule.ts";

export interface Observation {
  /** Song time now. */
  s: number;
  hero: { x: number; y: number; stepping: boolean };
  enemies: { x: number; y: number; type: string; health: number }[];
  /**
   * Visible threats: an arrow's line runs from (x, y) through its aim point (ax, ay); a slam marker
   * is a circle of `radius` (already grown by the hurtbox) around (x, y).
   */
  threats: { kind: "arrow" | "slam"; x: number; y: number; ax?: number; ay?: number; landsAt: number; radius?: number }[];
  /** The boss, when it is in the fight. */
  boss?: { x: number; y: number };
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
  s0: number;
  s1: number;
}

/** A near miss: within this many pixels of the hurtbox's edge. */
const NEAR = 24;
/** Free movement: the weave across the corridor (px) and its period (beats). */
const WEAVE = 70;
const WEAVE_BEATS = 16;
/** Free movement: aim this far ahead of the pace point, and stand still when this close. */
const STEER_LEAD = 16;
const STEER_STOP = 14;

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
  /**
   * Planned decisions (song time): presses in step movement, a look for threats on every other
   * grid slot (a dodge adds a press there), and steering on each beat in free movement.
   */
  private plan: { s: number; kind: "press" | "watch" | "steer"; beat: number; release?: boolean }[] = [];
  private next = 0;
  private lastDir: { x: number; y: number } | undefined;
  private count = 0;
  private held = new Set<Key>();

  constructor(
    private readonly hero: HeroDef,
    private readonly seed: number,
    slots: readonly GridSlot[],
    sections: readonly PlayerSection[],
    beats: readonly number[],
    profile: SkillProfile = "skilled",
    /** Is a grid slot an accent the section turns into a cleave? */
    heavyAccent: (slot: GridSlot) => boolean = () => false,
    /** The boss's death chord (song time), which the player knows from the song. */
    private readonly bossChord?: number,
  ) {
    const spec = PROFILES[profile];
    const grid = slots.filter((sl) => sl.kind === "grid" && sections[sl.section].steps);
    grid.forEach((sl, i) => {
      const sec = sections[sl.section];
      let pressed = spec.downbeatsOnly ? sl.slot === 0 : sec.presses[sl.slot % sec.presses.length] === "x";
      // Press one grid step before an accent strong enough for a cleave, even on a rest slot.
      const next = grid[i + 1];
      if (next && heavyAccent(next)) pressed = true;
      const r = new Rng(seed, "press-time", Math.round(sl.s * 1000));
      const err = spec.sd > 0 ? clamp(normal(r) * spec.sd, -spec.clampS, spec.clampS) : 0;
      this.plan.push({ s: sl.s + (pressed ? err : 0), kind: pressed ? "press" : "watch", beat: sl.bi });
    });
    // Free movement: change the held keys at most once per beat; let go before step movement.
    const sectionAt = (t: number) => sections.findLast((x) => t >= x.s0 - 1e-9) ?? sections[0];
    for (let i = 0; i < beats.length; i++) {
      const sec = sectionAt(beats[i]);
      if (sec.steps) continue;
      const next = beats[i + 1];
      const release = next === undefined || sectionAt(next).steps;
      this.plan.push({ s: beats[i] + 0.01, kind: "steer", beat: i, release });
    }
    this.plan.sort((a, b) => a.s - b.s);
  }

  nextDecision(after: number): number {
    while (this.next < this.plan.length && this.plan[this.next].s < after - 1e-9) this.next++;
    return this.next < this.plan.length ? this.plan[this.next].s : Infinity;
  }

  decide(obs: Observation): KeyEvent[] {
    // The decision planned for now.
    while (this.next < this.plan.length && this.plan[this.next].s < obs.s - 1e-9) this.next++;
    if (this.next >= this.plan.length) return [];
    const d = this.plan[this.next++];
    if (d.kind === "steer") return this.steer(obs, d.beat, d.release ?? false);
    // On a rest slot, press only to get out of a threat's way.
    if (d.kind === "watch" && !this.inDanger(obs)) return [];
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

  /**
   * Free movement: hold the direction key toward the pace point plus a slow
   * weave across the corridor.
   */
  private steer(obs: Observation, beat: number, release: boolean): KeyEvent[] {
    let want: Key[] = [];
    if (!release) {
      const p = obs.pacePoint;
      const weave = WEAVE * Math.sin((2 * Math.PI * beat) / WEAVE_BEATS);
      const tx = p.x + p.tx * STEER_LEAD - p.ty * weave;
      const ty = p.y + p.ty * STEER_LEAD + p.tx * weave;
      const dx = tx - obs.hero.x;
      const dy = ty - obs.hero.y;
      if (Math.hypot(dx, dy) > STEER_STOP) {
        const k = ((Math.round((Math.atan2(dy, dx) / (2 * Math.PI)) * 8) % 8) + 8) % 8;
        want = DIRECTIONS[k];
      }
    }
    const out: KeyEvent[] = [];
    for (const k of [...this.held]) {
      if (want.includes(k)) continue;
      this.held.delete(k);
      out.push({ s: obs.s, kind: "release", key: k });
    }
    for (const k of want) {
      if (this.held.has(k)) continue;
      this.held.add(k);
      out.push({ s: obs.s, kind: "press", key: k });
    }
    return out;
  }

  /** Would standing here be hit: an arrow about to fly (or flying) at the hero, or a slam marker underfoot? */
  private inDanger(obs: Observation): boolean {
    return obs.threats.some((t) => this.threatCost(t, obs.hero.x, obs.hero.y) >= 50);
  }

  /** 50 inside a threat, a little when the hero would be clear of it but not close enough for a near miss. */
  private threatCost(t: Observation["threats"][number], x: number, y: number): number {
    if (t.kind === "slam") {
      const edge = Math.hypot(x - t.x, y - t.y) - (t.radius ?? 30);
      return edge <= 3 ? 50 : edge > NEAR - 4 ? 12 : 0;
    }
    // The arrow's line, from the archer through its aim point, against the hurtbox.
    const dx = (t.ax ?? x) - t.x;
    const dy = (t.ay ?? y) - t.y;
    const dl = Math.hypot(dx, dy) || 1;
    const hx = x - t.x;
    const hy = y - 20 - t.y;
    if (hx * dx + hy * dy < 0) return 0;
    const edge = Math.abs(hx * (dy / dl) - hy * (dx / dl)) - 10;
    return edge <= 3 ? 50 : edge > NEAR - 4 ? 12 : 0;
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
      // Leave slam markers and arrow lines, by a close shave: judged where the step has taken the
      // hero when the threat lands.
      for (const t of obs.threats) {
        const u = clamp((t.landsAt - obs.s) / (this.hero.step.durationMs / 1000), 0, 1);
        const e = easeOut(u);
        cost += this.threatCost(t, obs.hero.x + d.x * L * e, obs.hero.y + d.y * L * e);
      }
      // Be beside the boss for its death chord.
      if (obs.boss && this.bossChord !== undefined && obs.s > this.bossChord - 1.5 && obs.s < this.bossChord) cost += Math.max(0, Math.hypot(ax - obs.boss.x, ay - obs.boss.y) - 70) / 15;
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
