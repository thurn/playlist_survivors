/**
 * The movement model: the only thing that moves the hero. Every function is a
 * pure function of the input so far, so the same code can read a real keyboard.
 *
 * - Step movement: each press moves one quick, fixed-length step, then the hero
 *   stands completely still.
 * - Free movement: held keys walk the hero smoothly, with almost no inertia.
 *
 * Times are world seconds and distances world pixels.
 */
import type { HeroDef } from "../content/schema.ts";
import { clamp, smoothstep } from "../core/shaping.ts";

export type Key = "up" | "down" | "left" | "right";
/** A gameplay key: a direction, or Space (play mode's stomp). */
export type InputKey = Key | "space";

/** Two keys on opposite sides (Left and Right, or Up and Down)? */
export const opposite = (a: Key, b: Key): boolean => KEY_DIR[a].x === -KEY_DIR[b].x && KEY_DIR[a].y === -KEY_DIR[b].y;
export const KEYS: readonly Key[] = ["up", "left", "down", "right"];

/** Unit vector of each key, in world coordinates (y grows downward). */
export const KEY_DIR: Record<Key, { x: number; y: number }> = {
  up: { x: 0, y: -1 },
  down: { x: 0, y: 1 },
  left: { x: -1, y: 0 },
  right: { x: 1, y: 0 },
};

/**
 * Wall rule: the longest distance (0 to `len`) a straight move from (x, y)
 * along the unit vector (dx, dy) can cover while staying in the walkable band.
 */
export type Clamp = (x: number, y: number, dx: number, dy: number, len: number) => number;

export const NO_WALLS: Clamp = (_x, _y, _dx, _dy, len) => len;

/** One step, from its press to its landing. */
export interface Step {
  /** World time of the press that started it. */
  w0: number;
  x0: number;
  y0: number;
  /** Unit direction of the first key. */
  dx: number;
  dy: number;
  /** Distance the first key's leg covers (shortened at walls). */
  len1: number;
  /** Final displacement from (x0, y0). */
  ex: number;
  ey: number;
  /** Ease progress at which a perpendicular key bent the step toward the diagonal. */
  bendE?: number;
  keys: Key[];
  /** Strides: the stride's length before walls (a bend keeps it), and whether a held key started it rather than a press. */
  len?: number;
  held?: boolean;
  /** Strides: the song time it started (the stomp window counts song time, as the player hears it). */
  s0?: number;
  /**
   * Strides: the press a held stride took over from within the diagonal window, its world and song
   * time and where its stride began: the bend and stomp windows count from it, and a rock step
   * returns there.
   */
  press?: { w: number; s: number; x: number; y: number };
  /**
   * Strides: a stomp within the stomp window of the stride's start turned it into a rock step at
   * world time w, where the hero was at (x, y): over the rest of the stride it comes back to where
   * the press's stride began, (tx, ty).
   */
  rock?: { w: number; x: number; y: number; tx: number; ty: number };
}

export interface StepPose {
  x: number;
  y: number;
  /** Hop height. */
  z: number;
  /** Time progress 0–1. */
  u: number;
}

export const stepSeconds = (hero: HeroDef): number => hero.step.durationMs / 1000;

/** Cubic ease-out: the hero leaves fast and lands firmly. */
export const easeOut = (u: number): number => 1 - (1 - u) ** 3;

/** Start a step on a key press, from the hero's current position. */
export function startStep(hero: HeroDef, w: number, x: number, y: number, key: Key, walls: Clamp = NO_WALLS): Step {
  const d = KEY_DIR[key];
  const len = walls(x, y, d.x, d.y, hero.step.length);
  return { w0: w, x0: x, y0: y, dx: d.x, dy: d.y, len1: len, ex: d.x * len, ey: d.y * len, keys: [key] };
}

/**
 * A perpendicular key soon after the first bends the rest of the step toward
 * the diagonal. The step still lands one step length from where it started.
 * Returns undefined when the key does not bend this step (it starts a new one).
 */
export function bendStep(hero: HeroDef, step: Step, w: number, key: Key, walls: Clamp = NO_WALLS, durationS = stepSeconds(hero)): Step | undefined {
  if (step.bendE !== undefined || step.held || step.rock || step.keys.length === 0 || w - (step.press?.w ?? step.w0) > hero.step.diagonalWindowMs / 1000 + 1e-9) return undefined;
  const d = KEY_DIR[key];
  if (Math.abs(d.x * step.dx + d.y * step.dy) > 1e-9) return undefined;
  const u = clamp((w - step.w0) / durationS, 0, 1);
  const gx = (step.dx + d.x) / Math.SQRT2;
  const gy = (step.dy + d.y) / Math.SQRT2;
  const len = walls(step.x0, step.y0, gx, gy, step.len ?? hero.step.length);
  return { ...step, ex: gx * len, ey: gy * len, bendE: easeOut(u), keys: [...step.keys, key] };
}

/** Where the step has taken the hero at world time w. */
export function stepPose(hero: HeroDef, step: Step, w: number): StepPose {
  return movePose(step, w, stepSeconds(hero), hero.step.hop);
}

/** Where a step or stride lasting durationS, with a hop of `hop` px, has taken the hero at world time w. */
export function movePose(step: Step, w: number, durationS: number, hop: number): StepPose {
  const u = clamp((w - step.w0) / durationS, 0, 1);
  const rock = step.rock;
  if (rock && w >= rock.w) {
    // A rock step: from where the stomp found the hero back to the start, eased like a stride.
    const g = easeOut(clamp((w - rock.w) / Math.max(1e-9, step.w0 + durationS - rock.w), 0, 1));
    return { x: rock.x + (rock.tx - rock.x) * g, y: rock.y + (rock.ty - rock.y) * g, z: hop * Math.sin(Math.PI * u), u };
  }
  const e = easeOut(u);
  let x = step.x0 + step.dx * step.len1 * e;
  let y = step.y0 + step.dy * step.len1 * e;
  if (step.bendE !== undefined && e > step.bendE) {
    // Curve smoothly from the first key's line onto the diagonal landing spot:
    // the blend starts with zero slope, so the path has no kink.
    const g = smoothstep((e - step.bendE) / Math.max(1e-6, 1 - step.bendE));
    x += (step.ex - step.dx * step.len1) * g;
    y += (step.ey - step.dy * step.len1) * g;
  }
  return { x, y, z: hop * Math.sin(Math.PI * u), u };
}

/** Landing spot of a step. */
export function stepEnd(step: Step): { x: number; y: number } {
  return { x: step.x0 + step.ex, y: step.y0 + step.ey };
}

// ------------------------------------------------------------------ walking

/** Target walking velocity for a set of held keys: eight directions, opposite keys cancel. */
export function walkTarget(hero: HeroDef, held: Iterable<Key>): { x: number; y: number } {
  let x = 0;
  let y = 0;
  for (const k of held) {
    x += KEY_DIR[k].x;
    y += KEY_DIR[k].y;
  }
  const l = Math.hypot(x, y);
  if (l < 1e-9) return { x: 0, y: 0 };
  return { x: (x / l) * hero.walk.speed, y: (y / l) * hero.walk.speed };
}

/**
 * Move a walking velocity toward its target over dt world seconds. Speed
 * changes at a constant rate: full speed (or a stop) within accelMs.
 */
export function walkVelocity(hero: HeroDef, vx: number, vy: number, tx: number, ty: number, dt: number): { x: number; y: number } {
  const maxDv = (hero.walk.speed / (hero.walk.accelMs / 1000)) * dt;
  const ddx = tx - vx;
  const ddy = ty - vy;
  const d = Math.hypot(ddx, ddy);
  if (d <= maxDv) return { x: tx, y: ty };
  return { x: vx + (ddx / d) * maxDv, y: vy + (ddy / d) * maxDv };
}

/** Walk-cycle frame for the distance walked: one full cycle per strideCycle pixels, so the feet never slide. */
export function walkFrame(hero: HeroDef, distance: number, frames: number): number {
  const c = distance / hero.walk.strideCycle;
  return Math.floor((c - Math.floor(c)) * frames) % frames;
}
