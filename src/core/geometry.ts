/** Hit shapes in world pixels, and the small vector helpers the planner and runtime share. */

export interface Vec {
  x: number;
  y: number;
}

export type Shape =
  /** A circular sector: everything within `radius` and `halfAngle` of `dir`. */
  | { kind: "arc"; x: number; y: number; radius: number; dir: number; halfAngle: number }
  | { kind: "circle"; x: number; y: number; radius: number }
  /** A capsule: the segment (x0,y0)–(x1,y1) thickened by `radius`. Lines and dashes use it. */
  | { kind: "capsule"; x0: number; y0: number; x1: number; y1: number; radius: number }
  /** An annulus. */
  | { kind: "ring"; x: number; y: number; inner: number; outer: number };

export const TAU = Math.PI * 2;

export function wrapAngle(a: number): number {
  let r = (a + Math.PI) % TAU;
  if (r < 0) r += TAU;
  return r - Math.PI;
}

export function dist(ax: number, ay: number, bx: number, by: number): number {
  return Math.hypot(ax - bx, ay - by);
}

function segDist(px: number, py: number, x0: number, y0: number, x1: number, y1: number): number {
  const dx = x1 - x0;
  const dy = y1 - y0;
  const l2 = dx * dx + dy * dy;
  const t = l2 === 0 ? 0 : Math.max(0, Math.min(1, ((px - x0) * dx + (py - y0) * dy) / l2));
  return Math.hypot(px - (x0 + t * dx), py - (y0 + t * dy));
}

/** True if point p is inside the shape grown by `margin` pixels. */
export function shapeContains(s: Shape, px: number, py: number, margin = 0): boolean {
  switch (s.kind) {
    case "circle":
      return dist(px, py, s.x, s.y) <= s.radius + margin;
    case "ring": {
      const d = dist(px, py, s.x, s.y);
      return d >= s.inner - margin && d <= s.outer + margin;
    }
    case "capsule":
      return segDist(px, py, s.x0, s.y0, s.x1, s.y1) <= s.radius + margin;
    case "arc": {
      const d = dist(px, py, s.x, s.y);
      if (d > s.radius + margin) return false;
      if (d <= margin) return true;
      const da = Math.abs(wrapAngle(Math.atan2(py - s.y, px - s.x) - s.dir));
      if (da <= s.halfAngle) return true;
      // Distance from the point to the nearer edge ray of the sector.
      const edge = s.dir + Math.sign(wrapAngle(Math.atan2(py - s.y, px - s.x) - s.dir)) * s.halfAngle;
      return segDist(px, py, s.x, s.y, s.x + Math.cos(edge) * s.radius, s.y + Math.sin(edge) * s.radius) <= margin;
    }
  }
}

export function shapeArea(s: Shape): number {
  switch (s.kind) {
    case "circle":
      return Math.PI * s.radius * s.radius;
    case "ring":
      return Math.PI * (s.outer * s.outer - s.inner * s.inner);
    case "capsule":
      return Math.PI * s.radius * s.radius + 2 * s.radius * dist(s.x0, s.y0, s.x1, s.y1);
    case "arc":
      return s.halfAngle * s.radius * s.radius;
  }
}

/** A rough centre of the shape, used for pushing enemies out of it. */
export function shapeCentre(s: Shape): Vec {
  switch (s.kind) {
    case "circle":
    case "ring":
      return { x: s.x, y: s.y };
    case "capsule":
      return { x: (s.x0 + s.x1) / 2, y: (s.y0 + s.y1) / 2 };
    case "arc":
      return { x: s.x, y: s.y };
  }
}

/** Axis-aligned bounds [minX, minY, maxX, maxY]. */
export function shapeBounds(s: Shape): [number, number, number, number] {
  switch (s.kind) {
    case "circle":
      return [s.x - s.radius, s.y - s.radius, s.x + s.radius, s.y + s.radius];
    case "ring":
    case "arc": {
      const r = s.kind === "ring" ? s.outer : s.radius;
      return [s.x - r, s.y - r, s.x + r, s.y + r];
    }
    case "capsule":
      return [
        Math.min(s.x0, s.x1) - s.radius,
        Math.min(s.y0, s.y1) - s.radius,
        Math.max(s.x0, s.x1) + s.radius,
        Math.max(s.y0, s.y1) + s.radius,
      ];
  }
}

/**
 * Direction to push a point out of the shape (unit vector), for the crowd's
 * hit keep-out force.
 */
export function pushOutDir(s: Shape, px: number, py: number): Vec {
  let cx: number;
  let cy: number;
  if (s.kind === "capsule") {
    const dx = s.x1 - s.x0;
    const dy = s.y1 - s.y0;
    const l2 = dx * dx + dy * dy;
    const t = l2 === 0 ? 0 : Math.max(0, Math.min(1, ((px - s.x0) * dx + (py - s.y0) * dy) / l2));
    cx = s.x0 + t * dx;
    cy = s.y0 + t * dy;
  } else {
    cx = s.x;
    cy = s.y;
  }
  let vx = px - cx;
  let vy = py - cy;
  if (s.kind === "ring") {
    const d = Math.hypot(vx, vy);
    const mid = (s.inner + s.outer) / 2;
    if (d < mid) {
      vx = -vx;
      vy = -vy;
    }
  }
  const l = Math.hypot(vx, vy);
  if (l < 1e-6) return { x: 1, y: 0 };
  return { x: vx / l, y: vy / l };
}

/** Sample a uniformly distributed point inside the shape, shrunk by `inset`. */
export function sampleInShape(s: Shape, r: () => number, inset = 0): Vec {
  switch (s.kind) {
    case "circle": {
      const rad = Math.max(0, s.radius - inset) * Math.sqrt(r());
      const a = r() * TAU;
      return { x: s.x + Math.cos(a) * rad, y: s.y + Math.sin(a) * rad };
    }
    case "ring": {
      const lo = s.inner + inset;
      const hi = Math.max(lo, s.outer - inset);
      const rad = Math.sqrt(lo * lo + r() * (hi * hi - lo * lo));
      const a = r() * TAU;
      return { x: s.x + Math.cos(a) * rad, y: s.y + Math.sin(a) * rad };
    }
    case "arc": {
      const minR = Math.min(s.radius * 0.35, 20);
      const maxR = Math.max(minR, s.radius - inset);
      const rad = Math.sqrt(minR * minR + r() * (maxR * maxR - minR * minR));
      const angInset = Math.min(s.halfAngle * 0.9, inset / Math.max(rad, 1));
      const a = s.dir + (r() * 2 - 1) * (s.halfAngle - angInset);
      return { x: s.x + Math.cos(a) * rad, y: s.y + Math.sin(a) * rad };
    }
    case "capsule": {
      const t = r();
      const rad = Math.max(0, s.radius - inset) * (r() * 2 - 1);
      const dx = s.x1 - s.x0;
      const dy = s.y1 - s.y0;
      const l = Math.hypot(dx, dy) || 1;
      return { x: s.x0 + dx * t - (dy / l) * rad, y: s.y0 + dy * t + (dx / l) * rad };
    }
  }
}

/** Translate a shape. */
export function moveShape(s: Shape, dx: number, dy: number): Shape {
  switch (s.kind) {
    case "capsule":
      return { ...s, x0: s.x0 + dx, y0: s.y0 + dy, x1: s.x1 + dx, y1: s.y1 + dy };
    default:
      return { ...s, x: s.x + dx, y: s.y + dy };
  }
}

/** The nearest point inside the shape (shrunk by `inset`) to p. */
export function nearestInside(s: Shape, px: number, py: number, inset = 0.5): Vec {
  switch (s.kind) {
    case "circle": {
      const d = dist(px, py, s.x, s.y);
      const r = Math.max(0, s.radius - inset);
      if (d <= r) return { x: px, y: py };
      return { x: s.x + ((px - s.x) / d) * r, y: s.y + ((py - s.y) / d) * r };
    }
    case "ring": {
      const d = dist(px, py, s.x, s.y) || 1e-6;
      const r = Math.min(s.outer - inset, Math.max(s.inner + inset, d));
      return { x: s.x + ((px - s.x) / d) * r, y: s.y + ((py - s.y) / d) * r };
    }
    case "capsule": {
      const dx = s.x1 - s.x0;
      const dy = s.y1 - s.y0;
      const l2 = dx * dx + dy * dy;
      const t = l2 === 0 ? 0 : Math.max(0, Math.min(1, ((px - s.x0) * dx + (py - s.y0) * dy) / l2));
      const cx = s.x0 + t * dx;
      const cy = s.y0 + t * dy;
      const d = dist(px, py, cx, cy);
      const r = Math.max(0, s.radius - inset);
      if (d <= r) return { x: px, y: py };
      return { x: cx + ((px - cx) / d) * r, y: cy + ((py - cy) / d) * r };
    }
    case "arc": {
      const d = dist(px, py, s.x, s.y);
      const a = wrapAngle(Math.atan2(py - s.y, px - s.x) - s.dir);
      const r = Math.min(Math.max(d, 2), s.radius - inset);
      const lim = Math.max(0, s.halfAngle - inset / Math.max(r, 1));
      const aa = Math.max(-lim, Math.min(lim, a)) + s.dir;
      if (d <= s.radius - inset && Math.abs(a) <= lim) return { x: px, y: py };
      return { x: s.x + Math.cos(aa) * r, y: s.y + Math.sin(aa) * r };
    }
  }
}
