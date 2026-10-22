/**
 * The virtual player: a deterministic program that plays like a highly skilled
 * human. It knows the song (the press pattern, the timing grid, the route
 * pace), as a practiced player would, and sees only the current game state
 * when it acts. It never sees the future of the simulation.
 */
import type { HeroDef } from "../content/schema.ts";
import { Rng } from "../core/rng.ts";
import { wrapAngle } from "../core/geometry.ts";
import { clamp } from "../core/shaping.ts";
import { easeOut, KEY_DIR, type Key } from "./movement.ts";
import type { GridSlot } from "./schedule.ts";

export interface Observation {
  /** Song and world time now. */
  s: number;
  w: number;
  hero: { x: number; y: number; stepping: boolean };
  enemies: { x: number; y: number; type: string; health: number }[];
  /**
   * Visible threats: an arrow's line runs from (x, y) through its aim point (ax, ay); a slam marker
   * is a circle of `radius` (already grown by the hurtbox) around (x, y).
   */
  threats: { kind: "arrow" | "slam"; x: number; y: number; ax?: number; ay?: number; landsAt: number; radius?: number; seenAt: number }[];
  /** The boss, when it is in the fight. */
  boss?: { x: number; y: number };
  /** The middle of a formation still dancing in its slots, when one is. */
  formation?: { x: number; y: number };
  /** Where the route schedule says the hero should be now, and the route's direction there. */
  pacePoint: { x: number; y: number; tx: number; ty: number };
  /** Distance from a point to the edge of the walkable band (negative outside). */
  edgeDistance: (x: number, y: number) => number;
}

export interface KeyEvent {
  s: number;
  kind: "press" | "release";
  key: Key;
  /** Play mode's log: which keyboard event this is (the simulation only passes it on to its traces). */
  id?: number;
}

export interface Player {
  /** Song time of the player's next decision after `after` (Infinity when it is done). */
  nextDecision(after: number): number;
  /** Decide at the observation's song time: the key events it makes before its next decision. */
  decide(obs: Observation): KeyEvent[];
}

export type SkillProfile = "perfect" | "skilled" | "casual" | "moderate";

export interface ProfileSpec {
  /** Standard deviation and clamp of the timing error, in seconds. */
  sd: number;
  clampS: number;
  /** Casual players press downbeats only. */
  downbeatsOnly: boolean;
  /** Share of the section's pattern slots pressed, and of the presses that set up a cleave (seeded draws). */
  pressRate: number;
  accentRate: number;
  /** Seconds a threat must have been in view before the player reacts to it. */
  reaction: number;
  /** Chance per press of taking the second-best direction instead of the best. */
  sloppy: number;
}

/** The simulation shows a threat to the player this long after it appears. */
const SIM_REACTION = 0.15;

export const PROFILES: Record<SkillProfile, ProfileSpec> = {
  perfect: { sd: 0, clampS: 0, downbeatsOnly: false, pressRate: 1, accentRate: 1, reaction: SIM_REACTION, sloppy: 0 },
  skilled: { sd: 0.012, clampS: 0.04, downbeatsOnly: false, pressRate: 1, accentRate: 1, reaction: SIM_REACTION, sloppy: 0 },
  casual: { sd: 0.12, clampS: 0.25, downbeatsOnly: true, pressRate: 1, accentRate: 1, reaction: SIM_REACTION, sloppy: 0 },
  // The automated stand-in for a moderately competent human (play mode's checks require it to
  // hit the signature moments).
  moderate: { sd: 0.03, clampS: 0.1, downbeatsOnly: false, pressRate: 0.85, accentRate: 0.8, reaction: 0.25, sloppy: 0.15 },
};

/** Sections as the player knows them: the press pattern and step movement. */
export interface PlayerSection {
  steps: boolean;
  presses: string;
  s0: number;
  s1: number;
  /** The section's pace (world px per world-second): the open field's roam speed. */
  pace?: number;
}

/** A near miss: within this many pixels of the hurtbox's edge. */
const NEAR = 24;
/** Free movement: the weave across the corridor (px) and its period (beats). */
const WEAVE = 70;
const WEAVE_BEATS = 16;
/** Free movement: aim this far ahead of the pace point, and stand still when this close. */
const STEER_LEAD = 16;
const STEER_STOP = 14;
/** The open field's roam point: it turns at most this much per bar, heads for a crowd farther than this, and stays this close to the hero. */
const ROAM_TURN = Math.PI / 4;
const ROAM_CROWD = 200;
const ROAM_LEASH = 120;
/** The roam walks up this close to a dancing formation's middle, so the cleave on its release reaches the whole line. */
const ROAM_FORMATION = 100;

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
  private readonly spec: ProfileSpec;
  private readonly sections: readonly PlayerSection[];
  private readonly beats: readonly number[];
  private readonly open: boolean;
  /** Fixed-speed strides (play mode): hold the direction between presses, and let go to stand. */
  private readonly strides: boolean;
  /** The cost of the latest choice of keys. */
  private bestCost = Infinity;
  /** The open field's roam point: where it is, its heading (radians), and when it last moved. */
  private roam?: { x: number; y: number; dir: number; w: number; bi: number };

  constructor(
    private readonly hero: HeroDef,
    private readonly seed: number,
    slots: readonly GridSlot[],
    sections: readonly PlayerSection[],
    beats: readonly number[],
    profile: SkillProfile = "skilled",
    /** Is a grid slot an accent the section turns into a cleave? */
    heavyAccent: (slot: GridSlot) => boolean = () => false,
    opts: { open?: boolean; strides?: boolean } = {},
  ) {
    const spec = PROFILES[profile];
    this.spec = spec;
    this.sections = sections;
    this.beats = beats;
    this.open = opts.open ?? false;
    this.strides = opts.strides ?? false;
    const grid = slots.filter((sl) => sl.kind === "grid" && sections[sl.section].steps);
    grid.forEach((sl, i) => {
      const sec = sections[sl.section];
      let pressed = spec.downbeatsOnly ? sl.slot === 0 : sec.presses[sl.slot % sec.presses.length] === "x";
      if (pressed && spec.pressRate < 1 && new Rng(seed, "press-skip", Math.round(sl.s * 1000)).next() >= spec.pressRate) pressed = false;
      // Press one grid step before an accent strong enough for a cleave, even on a rest slot.
      const next = grid[i + 1];
      if (next && heavyAccent(next) && (spec.accentRate >= 1 || new Rng(seed, "accent-setup", Math.round(sl.s * 1000)).next() < spec.accentRate)) pressed = true;
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

  decide(seen: Observation): KeyEvent[] {
    // A slower player only reacts to a threat once it has been in view a while.
    const obs = this.spec.reaction > SIM_REACTION + 1e-9 ? { ...seen, threats: seen.threats.filter((t) => seen.s >= t.seenAt + this.spec.reaction - 1e-9) } : seen;
    this.moveRoam(obs);
    // The decision planned for now.
    while (this.next < this.plan.length && this.plan[this.next].s < obs.s - 1e-9) this.next++;
    if (this.next >= this.plan.length) return [];
    const d = this.plan[this.next++];
    if (d.kind === "steer") return this.steer(obs, d.beat, d.release ?? false);
    // On a rest slot, press only to get out of a threat's way.
    if (d.kind === "watch" && !this.inDanger(obs)) return this.strides ? this.keepHolding(obs) : [];
    const keys = this.chooseKeys(obs);
    this.count++;
    const r = new Rng(this.seed, "press-keys", this.count);
    if (this.strides) return this.strideKeys(obs, keys, r);
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
   * Strides: let go of the keys held, press the new direction (a key already down is pressed
   * again), and hold it, so the hero strides on every grid step until the next decision. When
   * standing still would be better than the best direction, or in a careful moment (a threat in
   * view, a formation dancing), tap instead: one stride, then stand.
   */
  private strideKeys(obs: Observation, keys: Key[], r: Rng): KeyEvent[] {
    const out: KeyEvent[] = [];
    for (const k of this.held) out.push({ s: obs.s, kind: "release", key: k });
    this.held.clear();
    let s = obs.s;
    for (let i = 0; i < keys.length; i++) {
      if (i > 0) s += r.range(0.002, 0.015);
      out.push({ s, kind: "press", key: keys[i] });
    }
    // Careful moments (a threat in view, a formation dancing) and a better spot standing still: tap.
    const careful = obs.threats.length > 0 || obs.formation !== undefined;
    if (careful || this.standCost(obs) < this.bestCost) for (const k of keys) out.push({ s: s + r.range(0.06, 0.1), kind: "release", key: k });
    else for (const k of keys) this.held.add(k);
    return out;
  }

  /** Strides, on a rest slot: keep holding while going on beats standing still, else let go. */
  private keepHolding(obs: Observation): KeyEvent[] {
    if (this.held.size === 0) return [];
    const keys = [...this.held];
    const p = this.paceFrame(obs);
    const d = dirVector(keys);
    const L = this.hero.step.length;
    if (this.placeCost(obs, p, d, obs.hero.x + d.x * L, obs.hero.y + d.y * L, L) <= this.standCost(obs)) return [];
    this.held.clear();
    return keys.map((key) => ({ s: obs.s, kind: "release" as const, key }));
  }

  /** The cost of standing where the hero is. */
  private standCost(obs: Observation): number {
    return this.placeCost(obs, this.paceFrame(obs), { x: 0, y: 0 }, obs.hero.x, obs.hero.y, 0);
  }

  /**
   * Free movement: hold the direction key toward the pace point plus a slow
   * weave across the corridor.
   */
  private steer(obs: Observation, beat: number, release: boolean): KeyEvent[] {
    let want: Key[] = [];
    if (!release) {
      const p = this.paceFrame(obs);
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

  /** What the player keeps up with: the route's pace point, or in the open field its roam point. */
  private paceFrame(obs: Observation): Observation["pacePoint"] {
    if (!this.open || !this.roam) return obs.pacePoint;
    return { x: this.roam.x, y: this.roam.y, tx: Math.cos(this.roam.dir), ty: Math.sin(this.roam.dir) };
  }

  private beatIndex(s: number): number {
    const b = this.beats;
    let lo = 0;
    let hi = b.length - 1;
    if (s <= b[0]) return 0;
    if (s >= b[hi]) return hi;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (b[mid] <= s) lo = mid;
      else hi = mid;
    }
    return lo + (s - b[lo]) / (b[hi] - b[lo]);
  }

  /**
   * The open field has no pace point: the player follows a roam point instead. It starts at the
   * hero and moves at the section's pace along a heading that turns slowly (seeded, at most 45
   * degrees per bar), pulled toward a dancing formation, or else the crowd, when it is more than 200 px away.
   */
  private moveRoam(obs: Observation): void {
    if (!this.open) return;
    const bi = this.beatIndex(obs.s);
    if (!this.roam) {
      this.roam = { x: obs.hero.x, y: obs.hero.y, dir: 0, w: obs.w, bi };
      return;
    }
    const r = this.roam;
    const dw = Math.max(0, obs.w - r.w);
    // A turn rate per bar of two beats.
    for (let b = r.bi; b < bi - 1e-9; ) {
      const next = Math.min(bi, Math.floor(b / 2 + 1e-9) * 2 + 2);
      r.dir += new Rng(this.seed, "roam", Math.floor(b / 2 + 1e-9)).range(-1, 1) * ROAM_TURN * ((next - b) / 2);
      b = next;
    }
    // A dancing formation draws the roam to it (a player walks up to the line to cleave it); otherwise the crowd does.
    if (obs.formation) {
      const f = obs.formation;
      if (Math.hypot(f.x - r.x, f.y - r.y) > ROAM_FORMATION) r.dir += wrapAngle(Math.atan2(f.y - r.y, f.x - r.x) - r.dir) * Math.min(1, dw * 1.5);
    } else if (obs.enemies.length > 0) {
      let cx = 0;
      let cy = 0;
      for (const e of obs.enemies) {
        cx += e.x;
        cy += e.y;
      }
      cx /= obs.enemies.length;
      cy /= obs.enemies.length;
      if (Math.hypot(cx - r.x, cy - r.y) > ROAM_CROWD) r.dir += wrapAngle(Math.atan2(cy - r.y, cx - r.x) - r.dir) * Math.min(1, dw * 1.5);
    }
    const sec = this.sections.findLast((x) => obs.s >= x.s0 - 1e-9) ?? this.sections[0];
    const pace = sec.pace ?? 40;
    r.x += Math.cos(r.dir) * pace * dw;
    r.y += Math.sin(r.dir) * pace * dw;
    // On a leash: it never runs far ahead of the hero.
    const lx = r.x - obs.hero.x;
    const ly = r.y - obs.hero.y;
    const ld = Math.hypot(lx, ly);
    if (ld > ROAM_LEASH) {
      r.x = obs.hero.x + (lx / ld) * ROAM_LEASH;
      r.y = obs.hero.y + (ly / ld) * ROAM_LEASH;
    }
    r.w = obs.w;
    r.bi = bi;
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

  /** The keys the player would press now, ignoring timing (the masher borrows its sense of direction). */
  steerKeys(obs: Observation): Key[] {
    this.moveRoam(obs);
    return this.chooseKeys(obs);
  }

  /**
   * What a step toward d (a unit vector, or zero to stand) costs where it leaves the hero, (ax, ay),
   * L px away: keeping up with the pace frame p, threats, walls and the crowd.
   */
  private placeCost(obs: Observation, p: Observation["pacePoint"], d: { x: number; y: number }, ax: number, ay: number, L: number): number {
    const nx = -p.ty;
    const ny = p.tx;
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
    return cost;
  }

  /** Score the eight directions by where the step would leave the hero, and pick the lowest cost. */
  private chooseKeys(obs: Observation): Key[] {
    const L = this.hero.step.length;
    const p = this.paceFrame(obs);
    let best: Key[] = DIRECTIONS[0];
    let bestCost = Infinity;
    let second: Key[] = DIRECTIONS[0];
    let secondCost = Infinity;
    const r = new Rng(this.seed, "press-noise", this.count);
    for (const keys of DIRECTIONS) {
      const d = dirVector(keys);
      const ax = obs.hero.x + d.x * L;
      const ay = obs.hero.y + d.y * L;
      let cost = this.placeCost(obs, p, d, ax, ay, L);
      // Avoid back-and-forth jitter.
      if (this.lastDir && d.x * this.lastDir.x + d.y * this.lastDir.y < -0.7) cost += 0.6;
      // A little variety.
      cost += r.next() * 0.2;
      if (cost < bestCost) {
        second = best;
        secondCost = bestCost;
        bestCost = cost;
        best = keys;
      } else if (cost < secondCost) {
        secondCost = cost;
        second = keys;
      }
    }
    // A sloppy press goes the second-best way.
    this.bestCost = bestCost;
    if (this.spec.sloppy > 0 && Number.isFinite(secondCost) && new Rng(this.seed, "sloppy", this.count).next() < this.spec.sloppy) {
      best = second;
      this.bestCost = secondCost;
    }
    this.lastDir = dirVector(best);
    // Which of a diagonal's two keys comes first is a matter of habit.
    if (best.length === 2 && r.next() < 0.5) return [best[1], best[0]];
    return best;
  }
}

/**
 * A player who ignores the beat: in step movement it presses a key every
 * 1/rate seconds (jittered ±30%), in the direction the virtual player would
 * choose, and in free movement it steers like the virtual player. Play mode's
 * checks use it to keep mashing well behind playing on the beat.
 */
export class MasherPlayer implements Player {
  private t = 0;
  private n = 0;

  constructor(
    private readonly steerer: VirtualPlayer,
    private readonly sections: readonly PlayerSection[],
    private readonly seed: number,
    /** Presses per second. */
    private readonly rate: number,
  ) {}

  private steps(s: number): boolean {
    return (this.sections.findLast((x) => s >= x.s0 - 1e-9) ?? this.sections[0]).steps;
  }

  /** The next mash time at or after `after`, in step movement. */
  private mashAt(after: number): number {
    while (this.t < after - 1e-9) this.t += (1 / this.rate) * new Rng(this.seed, "mash", this.n++).range(0.7, 1.3);
    if (this.steps(this.t)) return this.t;
    const next = this.sections.find((x) => x.steps && x.s0 > this.t);
    if (!next) return Infinity;
    this.t = next.s0;
    return this.t;
  }

  nextDecision(after: number): number {
    // The virtual player's own decisions in step movement are skipped: only its steering counts.
    let v = this.steerer.nextDecision(after);
    while (Number.isFinite(v) && this.steps(v)) v = this.steerer.nextDecision(v + 1e-6);
    return Math.min(this.mashAt(after), v);
  }

  decide(obs: Observation): KeyEvent[] {
    if (!this.steps(obs.s)) return this.steerer.decide(obs);
    if (Math.abs(obs.s - this.t) > 1e-6) return [];
    const key = this.steerer.steerKeys(obs)[0];
    this.t = obs.s + (1 / this.rate) * new Rng(this.seed, "mash", this.n++).range(0.7, 1.3);
    return [
      { s: obs.s, kind: "press", key },
      { s: obs.s + 0.03, kind: "release", key },
    ];
  }
}
