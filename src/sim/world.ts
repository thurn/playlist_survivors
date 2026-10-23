/**
 * The world: the open field, generated in chunks around the hero. The hero can walk anywhere, and
 * props never block. There is no route: spawns, the camera lead and formations follow the hero's
 * heading instead.
 */
import { NO_WALLS, type Clamp } from "./movement.ts";

/** A point and a unit heading. */
export interface Frame {
  x: number;
  y: number;
  tx: number;
  ty: number;
}

export interface World {
  /** Wall rule for steps: the longest straight move that stays walkable (none block in the open field). */
  readonly clamp: Clamp;
  /** Where the hero starts: a point and a unit tangent. */
  pacePoint(s: number): Frame;
}

export class OpenWorld implements World {
  readonly clamp: Clamp = NO_WALLS;

  /** The field's origin, heading right (where the hero starts). */
  pacePoint(): Frame {
    return { x: 0, y: 0, tx: 1, ty: 0 };
  }
}
