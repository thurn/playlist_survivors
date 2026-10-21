/**
 * The map generator: a long strip of forest along a smooth route. Deterministic
 * for a given seed, route length, zone extents, themes and landmarks.
 */
import type { ZoneTheme } from "../content/schema.ts";
import { Rng, hash32, noise1 } from "../core/rng.ts";
import { smoothstep } from "../core/shaping.ts";
import type { MapLayout, MapProp, MapZone } from "../plan/types.ts";

export const TILE = 32;
export const CORRIDOR_HALF_WIDTH = 352;
const ROUTE_STEP = 8;
const MARGIN = 1200;

export interface PropInfo {
  tall: boolean;
  radius: number;
}

export interface LandmarkPlacement {
  id: string;
  /** Route distance at which the landmark's origin is stamped. */
  d: number;
  map: TiledMap;
}

export interface TiledObject {
  name: string;
  type?: string;
  class?: string;
  x: number;
  y: number;
  properties?: { name: string; value: unknown }[];
}

export interface TiledMap {
  width: number;
  height: number;
  tilewidth: number;
  tileheight: number;
  layers: { type: string; name: string; objects?: TiledObject[] }[];
  properties?: { name: string; value: unknown }[];
}

export interface MapInput {
  seed: number;
  routeLength: number;
  zones: { id: string; d0: number; d1: number }[];
  themes: Map<string, ZoneTheme>;
  landmarks: LandmarkPlacement[];
  props?: Record<string, PropInfo>;
}

/** Fallback prop facts when no world atlas is supplied. */
export const DEFAULT_PROPS: Record<string, PropInfo> = {
  "tree-summer": { tall: true, radius: 30 },
  "tree-autumn": { tall: true, radius: 30 },
  "tree-dead": { tall: true, radius: 24 },
  "bush-summer": { tall: true, radius: 16 },
  "bush-autumn": { tall: true, radius: 16 },
  mushroom: { tall: false, radius: 6 },
  "mushroom-big": { tall: true, radius: 18 },
  "flower-summer": { tall: false, radius: 6 },
  "flower-autumn": { tall: false, radius: 6 },
  "grass-tuft": { tall: false, radius: 6 },
  "rock-small": { tall: false, radius: 8 },
  "rock-big": { tall: true, radius: 20 },
  brazier: { tall: true, radius: 10 },
  campfire: { tall: false, radius: 14 },
  "standing-stone": { tall: true, radius: 16 },
  "ruin-pillar": { tall: true, radius: 16 },
  "ruin-wall": { tall: true, radius: 30 },
  "lava-pool": { tall: false, radius: 40 },
  "patch-dirt-summer": { tall: false, radius: 40 },
  "patch-dirt-autumn": { tall: false, radius: 40 },
  "patch-dirt-ember": { tall: false, radius: 40 },
  "patch-lava": { tall: false, radius: 40 },
};

export interface Route {
  step: number;
  x: Float32Array;
  y: Float32Array;
  length: number;
}

/** A smooth curve heading generally right and slightly up, made of gentle bends. */
export function buildRoute(seed: number, length: number): Route {
  const n = Math.max(2, Math.ceil(length / ROUTE_STEP) + 1);
  const x = new Float32Array(n);
  const y = new Float32Array(n);
  let px = 0;
  let py = 0;
  const s = hash32(seed, "route");
  for (let i = 0; i < n; i++) {
    x[i] = px;
    y[i] = py;
    const d = i * ROUTE_STEP;
    const heading = -0.16 + 0.42 * noise1(s, d / 1100) + 0.12 * noise1(s + 7, d / 420);
    px += Math.cos(heading) * ROUTE_STEP;
    py += Math.sin(heading) * ROUTE_STEP;
  }
  return { step: ROUTE_STEP, x, y, length };
}

export interface RouteFrame {
  x: number;
  y: number;
  /** Unit tangent. */
  tx: number;
  ty: number;
}

export function routeFrame(r: { step: number; x: ArrayLike<number>; y: ArrayLike<number> }, d: number): RouteFrame {
  const n = r.x.length;
  const f = Math.max(0, d / r.step);
  let k = Math.floor(f);
  if (k >= n - 1) {
    // Extrapolate along the last segment.
    k = n - 2;
  }
  const u = f - k;
  const x0 = r.x[k];
  const y0 = r.y[k];
  const x1 = r.x[k + 1];
  const y1 = r.y[k + 1];
  const dx = x1 - x0;
  const dy = y1 - y0;
  // Smooth the tangent by averaging neighbouring segments.
  const k2 = Math.min(n - 2, k + 4);
  const k0 = Math.max(0, k - 4);
  const sx = r.x[k2 + 1] - r.x[k0];
  const sy = r.y[k2 + 1] - r.y[k0];
  const sl = Math.hypot(sx, sy) || 1;
  return { x: x0 + dx * u, y: y0 + dy * u, tx: sx / sl, ty: sy / sl };
}

/** Nearest route distance and signed lateral offset for points, via a coarse grid. */
class RouteLookup {
  private cells = new Map<number, number[]>();
  private readonly cell = 128;
  constructor(private r: Route) {
    for (let i = 0; i < r.x.length; i += 2) {
      const key = this.key(Math.floor(r.x[i] / this.cell), Math.floor(r.y[i] / this.cell));
      let list = this.cells.get(key);
      if (!list) this.cells.set(key, (list = []));
      list.push(i);
    }
  }
  private key(cx: number, cy: number): number {
    return (cx + 4096) * 8192 + (cy + 4096);
  }
  nearest(px: number, py: number): { d: number; lat: number } {
    const cx = Math.floor(px / this.cell);
    const cy = Math.floor(py / this.cell);
    let best = -1;
    let bestD2 = Infinity;
    for (let rad = 1; rad <= 12 && best < 0; rad += 3) {
      for (let gx = cx - rad; gx <= cx + rad; gx++) {
        for (let gy = cy - rad; gy <= cy + rad; gy++) {
          const list = this.cells.get(this.key(gx, gy));
          if (!list) continue;
          for (const i of list) {
            const d2 = (this.r.x[i] - px) ** 2 + (this.r.y[i] - py) ** 2;
            if (d2 < bestD2) {
              bestD2 = d2;
              best = i;
            }
          }
        }
      }
    }
    if (best < 0) {
      // Far from the route: fall back to projecting on x.
      return { d: Math.max(0, px), lat: 9999 };
    }
    // Refine among neighbours.
    for (let i = Math.max(0, best - 3); i <= Math.min(this.r.x.length - 1, best + 3); i++) {
      const d2 = (this.r.x[i] - px) ** 2 + (this.r.y[i] - py) ** 2;
      if (d2 < bestD2) {
        bestD2 = d2;
        best = i;
      }
    }
    const f = routeFrame(this.r, best * this.r.step);
    const lat = (px - f.x) * -f.ty + (py - f.y) * f.tx;
    return { d: best * this.r.step, lat };
  }
}

function zoneAt(zones: MapZone[], d: number): number {
  for (let i = 0; i < zones.length; i++) if (d < zones[i].d1) return i;
  return zones.length - 1;
}

/** The zone used for a point, with blending over 8 tiles around zone boundaries. */
function blendedZone(zones: MapZone[], d: number, r01: number): number {
  const i = zoneAt(zones, d);
  const blend = 8 * TILE;
  if (i + 1 < zones.length && d > zones[i].d1 - blend / 2) {
    const u = smoothstep((d - (zones[i].d1 - blend / 2)) / blend);
    return r01 < u ? i + 1 : i;
  }
  if (i > 0 && d < zones[i].d0 + blend / 2) {
    const u = smoothstep((d - (zones[i].d0 - blend / 2)) / blend);
    return r01 < u ? i : i - 1;
  }
  return i;
}

function prop(name: string, info: Record<string, PropInfo>): PropInfo {
  return info[name] ?? DEFAULT_PROPS[name] ?? { tall: false, radius: 10 };
}

export function generateMap(input: MapInput): MapLayout {
  const info = { ...DEFAULT_PROPS, ...(input.props ?? {}) };
  const route = buildRoute(input.seed, input.routeLength + 1600);
  const lookup = new RouteLookup(route);
  const zones: MapZone[] = input.zones.map((z) => ({ ...z }));
  if (zones.length > 0) zones[zones.length - 1].d1 = Math.max(zones[zones.length - 1].d1, route.length + 2000);

  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (let i = 0; i < route.x.length; i++) {
    minX = Math.min(minX, route.x[i]);
    maxX = Math.max(maxX, route.x[i]);
    minY = Math.min(minY, route.y[i]);
    maxY = Math.max(maxY, route.y[i]);
  }
  const originX = Math.floor((minX - MARGIN) / TILE) * TILE;
  const originY = Math.floor((minY - MARGIN) / TILE) * TILE;
  const cols = Math.ceil((maxX + MARGIN - originX) / TILE);
  const rows = Math.ceil((maxY + MARGIN - originY) / TILE);

  const tileIds: string[] = [];
  const tileIndex = (id: string) => {
    let i = tileIds.indexOf(id);
    if (i < 0) i = tileIds.push(id) - 1;
    return i;
  };
  const tiles = new Uint16Array(cols * rows);
  const tileVariants = new Uint8Array(cols * rows);
  const tileZone = new Uint8Array(cols * rows);
  const tileSeed = hash32(input.seed, "tiles");
  for (let ty = 0; ty < rows; ty++) {
    for (let tx = 0; tx < cols; tx++) {
      const px = originX + tx * TILE + TILE / 2;
      const py = originY + ty * TILE + TILE / 2;
      const { d } = lookup.nearest(px, py);
      const k = ty * cols + tx;
      const z = blendedZone(zones, d, hash32(tileSeed, tx, ty) / 4294967296);
      const theme = input.themes.get(zones[z].id)!;
      tiles[k] = tileIndex(theme.terrain.ground);
      tileVariants[k] = hash32(tileSeed, tx, ty, 1) & 0xff;
      tileZone[k] = z;
    }
  }

  // Props.
  const props: MapProp[] = [];
  const occupied: { x: number; y: number; r: number }[] = [];
  const grid = new Map<number, number[]>();
  const gcell = 64;
  const gkey = (x: number, y: number) => (Math.floor(x / gcell) + 4096) * 8192 + (Math.floor(y / gcell) + 4096);
  const free = (x: number, y: number, r: number) => {
    const cx = Math.floor(x / gcell);
    const cy = Math.floor(y / gcell);
    for (let gx = cx - 2; gx <= cx + 2; gx++) {
      for (let gy = cy - 2; gy <= cy + 2; gy++) {
        const list = grid.get((gx + 4096) * 8192 + (gy + 4096));
        if (!list) continue;
        for (const i of list) {
          const o = occupied[i];
          if ((o.x - x) ** 2 + (o.y - y) ** 2 < (o.r + r) ** 2) return false;
        }
      }
    }
    return true;
  };
  const occupy = (x: number, y: number, r: number) => {
    const i = occupied.push({ x, y, r }) - 1;
    const key = gkey(x, y);
    let list = grid.get(key);
    if (!list) grid.set(key, (list = []));
    list.push(i);
  };

  // Landmarks first, so generated props avoid them.
  const clearings: { x: number; y: number; r: number }[] = [];
  for (const lm of input.landmarks) {
    const f = routeFrame(route, lm.d);
    const getProp = (name: string) => lm.map.properties?.find((p) => p.name === name)?.value;
    const along = Number(getProp("offsetAlong") ?? 0);
    const ox = f.x + f.tx * along;
    const oy = f.y + f.ty * along;
    const cxMap = (lm.map.width * lm.map.tilewidth) / 2;
    const cyMap = (lm.map.height * lm.map.tileheight) / 2;
    clearings.push({ x: ox, y: oy, r: Number(getProp("clearRadius") ?? 200) });
    for (const layer of lm.map.layers) {
      for (const o of layer.objects ?? []) {
        const kind = o.type ?? o.class;
        if (kind !== "prop") continue;
        const x = ox + (o.x - cxMap);
        const y = oy + (o.y - cyMap);
        const pi = prop(o.name, info);
        const light = o.properties?.find((p) => p.name === "lightColor");
        const lightR = o.properties?.find((p) => p.name === "lightRadius");
        props.push({
          sprite: o.name,
          variant: Number(o.properties?.find((p) => p.name === "variant")?.value ?? 0),
          x: Math.round(x),
          y: Math.round(y),
          tall: pi.tall,
          flip: false,
          light: light ? { color: String(light.value), radius: Number(lightR?.value ?? 96) } : undefined,
        });
        occupy(x, y, pi.radius);
      }
    }
  }
  const inClearing = (x: number, y: number) => clearings.some((c) => (c.x - x) ** 2 + (c.y - y) ** 2 < c.r * c.r);

  // Worn ground patches along the route.
  const pr = new Rng(input.seed, "patches");
  for (let d = 0; d < route.length; d += 52 + pr.range(0, 40)) {
    const f = routeFrame(route, d);
    const lat = pr.range(-90, 90);
    const x = f.x - f.ty * lat;
    const y = f.y + f.tx * lat;
    const z = blendedZone(zones, d, pr.next());
    const theme = input.themes.get(zones[z].id)!;
    props.push({ sprite: theme.terrain.patch, variant: pr.int(0, 255), x: Math.round(x), y: Math.round(y), tall: false, flip: pr.next() < 0.5 });
  }

  // Dart throwing over the map area, sorted by a seeded order for determinism.
  const rng = new Rng(input.seed, "props");
  const area = cols * rows;
  const attempts = Math.floor(area * 0.9);
  for (let a = 0; a < attempts; a++) {
    const x = originX + rng.next() * cols * TILE;
    const y = originY + rng.next() * rows * TILE;
    const roll = rng.next();
    const pick = rng.next();
    const variant = rng.int(0, 255);
    const flip = rng.next() < 0.5;
    const { d, lat } = lookup.nearest(x, y);
    const alat = Math.abs(lat);
    const z = blendedZone(zones, d, pick);
    const theme = input.themes.get(zones[z].id)!;
    const inCorridor = alat < CORRIDOR_HALF_WIDTH;
    const candidates = theme.props.filter((p) => (inCorridor ? p.corridor : !p.corridor || alat < CORRIDOR_HALF_WIDTH + 120));
    if (candidates.length === 0) continue;
    // Total density per tile; forests get denser further out.
    const outFactor = inCorridor ? 0.45 : Math.min(2.2, 0.7 + (alat - CORRIDOR_HALF_WIDTH) / 260);
    let acc = 0;
    let chosen: (typeof candidates)[number] | undefined;
    for (const c of candidates) {
      acc += c.density * outFactor;
      if (roll < acc) {
        chosen = c;
        break;
      }
    }
    if (!chosen) continue;
    const pi = prop(chosen.sprite, info);
    // Tall props stay outside the corridor so they never hide the action.
    if (pi.tall && alat < CORRIDOR_HALF_WIDTH + pi.radius) continue;
    if (inClearing(x, y)) continue;
    if (!free(x, y, pi.radius)) continue;
    occupy(x, y, pi.radius);
    props.push({ sprite: chosen.sprite, variant, x: Math.round(x), y: Math.round(y), tall: pi.tall, flip, light: chosen.light });
  }

  // Stable ordering: ground props first, then by y for depth.
  props.sort((a, b) => Number(a.tall) - Number(b.tall) || a.y - b.y || a.x - b.x || a.sprite.localeCompare(b.sprite));

  let h = hash32(input.seed, cols, rows, originX, originY);
  for (let i = 0; i < tiles.length; i += 7) h = hash32(h, tiles[i], tileVariants[i]);
  for (const p of props) h = hash32(h, p.sprite, p.x, p.y, p.variant);
  return {
    originX,
    originY,
    cols,
    rows,
    tiles,
    tileVariants,
    tileIds,
    tileZone,
    routeStep: route.step,
    routeX: route.x,
    routeY: route.y,
    routeLength: input.routeLength,
    corridorHalfWidth: CORRIDOR_HALF_WIDTH,
    zones,
    props,
    hash: (h >>> 0).toString(16).padStart(8, "0"),
  };
}
