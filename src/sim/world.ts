/**
 * The world: everything the map decides for the simulation. The demo plays in
 * a forest corridor along a route; play mode adds an open field.
 */
import type { MapLayout } from "../plan/types.ts";
import { RouteLookup, routeFrame, type RouteFrame } from "../mapgen/mapgen.ts";
import type { Clamp } from "./movement.ts";
import { paceAt, type Schedule } from "./schedule.ts";

/** The walkable band: the corridor less this margin. */
const CORRIDOR_MARGIN = 48;

export interface World {
  /** Wall rule for steps: the longest straight move that stays walkable. */
  readonly clamp: Clamp;
  /** Distance from (x, y) to the edge of the walkable area (negative outside). */
  edge(x: number, y: number): number;
  /** The way forward at song time s: a point and a unit tangent (the route's pace point in the corridor). */
  pacePoint(s: number): RouteFrame;
}

/** The walkable band around the route. */
export class Corridor {
  private lookup: RouteLookup;
  readonly half: number;
  constructor(readonly map: MapLayout) {
    this.lookup = new RouteLookup({ step: map.routeStep, x: map.routeX, y: map.routeY });
    this.half = map.corridorHalfWidth - CORRIDOR_MARGIN;
  }
  /** Distance from (x, y) to the band's edge (negative outside). */
  edge(x: number, y: number): number {
    return this.half - Math.abs(this.lookup.nearest(x, y).lat);
  }
  /** Route distance and lateral offset of the nearest route point. */
  nearest(x: number, y: number): { d: number; lat: number } {
    return this.lookup.nearest(x, y);
  }
  /** Wall rule for steps: the longest straight move that stays in the band. */
  readonly clamp: Clamp = (x, y, dx, dy, len) => {
    if (this.edge(x + dx * len, y + dy * len) >= 0) return len;
    if (this.edge(x, y) < 0) return 0;
    let lo = 0;
    let hi = len;
    for (let i = 0; i < 12; i++) {
      const mid = (lo + hi) / 2;
      if (this.edge(x + dx * mid, y + dy * mid) >= 0) lo = mid;
      else hi = mid;
    }
    return lo;
  };
}

/** The demo's world: a corridor through the forest, and the route's pace point. */
export class CorridorWorld implements World {
  private corridor: Corridor;
  private route: { step: number; x: Float32Array; y: Float32Array };
  readonly clamp: Clamp;

  constructor(
    map: MapLayout,
    private readonly sch: Schedule,
  ) {
    this.corridor = new Corridor(map);
    this.route = { step: map.routeStep, x: map.routeX, y: map.routeY };
    this.clamp = this.corridor.clamp;
  }

  edge(x: number, y: number): number {
    return this.corridor.edge(x, y);
  }

  pacePoint(s: number): RouteFrame {
    return routeFrame(this.route, paceAt(this.sch, s));
  }
}
