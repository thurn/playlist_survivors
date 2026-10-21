/**
 * Auto-aim: the sword picks its own direction, like a Vampire Survivors
 * weapon. It reads only the game state at the start of the wind-up.
 */
import { TAU, shapeContains, wrapAngle, type Shape } from "../core/geometry.ts";

export interface AimTarget {
  x: number;
  y: number;
  /** Body radius: the swing catches a target whose body overlaps the arc. */
  r: number;
  /** Boss 5, elites 2, regular enemies 1. */
  weight: number;
}

export interface ArcReach {
  radius: number;
  /** Half the arc's opening angle, in radians. */
  halfAngle: number;
}

export const AIM_DIRECTIONS = 16;

/** The arc a swing from (x, y) toward dir covers. */
export function swingArc(x: number, y: number, dir: number, reach: ArcReach): Extract<Shape, { kind: "arc" }> {
  return { kind: "arc", x, y, radius: reach.radius, dir, halfAngle: reach.halfAngle };
}

/** Does a hit shape catch this body? Its centre lies within the shape grown by half its radius. */
export function catches(shape: Shape, x: number, y: number, r: number): boolean {
  return shapeContains(shape, x, y, 0.5 * r);
}

/**
 * The direction that hits the most valuable targets. Ties go to the direction
 * nearest the nearest enemy. With nobody in reach, aim at the nearest enemy
 * within twice the reach; with nobody near, keep the current facing.
 */
export function aim(hx: number, hy: number, facing: number, targets: readonly AimTarget[], reach: ArcReach): number {
  // Nearest enemy (for ties and the fallback).
  let nearest: AimTarget | undefined;
  let nearestD = Infinity;
  for (const t of targets) {
    const d = Math.hypot(t.x - hx, t.y - hy);
    if (d < nearestD) {
      nearestD = d;
      nearest = t;
    }
  }
  const toNearest = nearest ? Math.atan2(nearest.y - hy, nearest.x - hx) : facing;
  let bestScore = 0;
  let bestDir = facing;
  let bestOff = Infinity;
  for (let k = 0; k < AIM_DIRECTIONS; k++) {
    const dir = wrapAngle((k / AIM_DIRECTIONS) * TAU);
    const arc = swingArc(hx, hy, dir, reach);
    let score = 0;
    for (const t of targets) if (catches(arc, t.x, t.y, t.r)) score += t.weight;
    if (score <= 0) continue;
    const off = Math.abs(wrapAngle(dir - toNearest));
    if (score > bestScore + 1e-9 || (Math.abs(score - bestScore) <= 1e-9 && off < bestOff - 1e-9)) {
      bestScore = score;
      bestDir = dir;
      bestOff = off;
    }
  }
  if (bestScore > 0) return bestDir;
  if (nearest && nearestD <= 2 * reach.radius) return toNearest;
  return facing;
}
