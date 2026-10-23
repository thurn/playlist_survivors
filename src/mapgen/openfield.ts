/**
 * The open field: ground generated in 512 px chunks around the
 * camera, each a deterministic function of the seed, its coordinates and its
 * zone. Zones change on the music: a transform wave sweeps out from the hero
 * in sixteenth-note bands and re-themes everything it passes. Landmarks rise
 * from the ground centred on the hero.
 */
import type { ZoneTheme } from "../content/schema.ts";
import { Rng, hash32 } from "../core/rng.ts";
import type { MapLayout, MapProp } from "../plan/types.ts";
import { DEFAULT_PROPS, TILE, type PropInfo, type TiledMap } from "./mapgen.ts";

/** Chunk size in world pixels (16 tiles). */
export const OPEN_CHUNK = 512;
/** A transform wave's ring grows this far each sixteenth note... */
export const WAVE_BAND = 175;
/** ...for this many sixteenths, when it has left any screen (1400 px from where it started). */
export const WAVE_BANDS = 8;
/** Tall props fade to this opacity when the hero comes this close (fully faded at FADE_NEAR). */
export const PROP_FADE = 0.3;
export const FADE_NEAR = 80;
export const FADE_FAR = 120;

/** A map layout with no ground of its own: the open field generates its ground in chunks. */
export function openFieldLayout(zones: string[]): MapLayout {
  return {
    zones: zones.map((id) => ({ id, d0: 0, d1: 0 })),
    hash: (hash32(0x0ae7, ...zones) >>> 0).toString(16).padStart(8, "0"),
  };
}

/** A transform wave as the plan records it. */
export interface ZoneWave {
  /** Song and world time of its downbeat, and where it starts (the hero). */
  s: number;
  w: number;
  x: number;
  y: number;
  /** Song beat index of its downbeat. */
  sbi: number;
  /** Zone indices (into the plan's zones) before and after. */
  prev: number;
  zone: number;
  /** Song time it has left the screen: from then on, everywhere is in the new zone. */
  endS: number;
}

/** The ring's radius at song beat index sbi: one band per sixteenth note, from the downbeat's own. */
export function waveRadius(wv: Pick<ZoneWave, "sbi">, sbi: number): number {
  if (sbi < wv.sbi - 1e-9) return 0;
  return WAVE_BAND * Math.min(WAVE_BANDS, Math.floor((sbi - wv.sbi) * 4 + 1e-6) + 1);
}

/** The newest wave that has started by song time s (waves in time order). */
export function latestWave(waves: readonly ZoneWave[], s: number): ZoneWave | undefined {
  for (let k = waves.length - 1; k >= 0; k--) if (s >= waves[k].s - 1e-9) return waves[k];
  return undefined;
}

/** A point's zone at song time s: the zone of the newest wave that has reached it. */
export function zoneAt(waves: readonly ZoneWave[], firstZone: number, x: number, y: number, s: number, sbi: number): number {
  for (let k = waves.length - 1; k >= 0; k--) {
    const wv = waves[k];
    if (s < wv.s - 1e-9) continue;
    if (s >= wv.endS || Math.hypot(x - wv.x, y - wv.y) <= waveRadius(wv, sbi)) return wv.zone;
  }
  return firstZone;
}

/** A landmark stamped in the open field. */
export interface LandmarkStamp {
  id: string;
  s: number;
  w: number;
  x: number;
  y: number;
  /** Its props, at their places in the world. */
  props: MapProp[];
  /** Generated tall props this close to its centre sink as it rises. */
  clearRadius: number;
  /** Song seconds it takes to rise (one bar). */
  rise: number;
}

/** A landmark's props centred on (x, y). */
export function stampLandmark(map: TiledMap, x: number, y: number, info: Record<string, PropInfo> = {}): { props: MapProp[]; clearRadius: number } {
  const getProp = (name: string) => map.properties?.find((p) => p.name === name)?.value;
  const cx = (map.width * map.tilewidth) / 2;
  const cy = (map.height * map.tileheight) / 2;
  const props: MapProp[] = [];
  for (const layer of map.layers) {
    for (const o of layer.objects ?? []) {
      if ((o.type ?? o.class) !== "prop") continue;
      const pi = info[o.name] ?? DEFAULT_PROPS[o.name] ?? { tall: false, radius: 10 };
      const light = o.properties?.find((p) => p.name === "lightColor");
      const lightR = o.properties?.find((p) => p.name === "lightRadius");
      props.push({
        sprite: o.name,
        variant: Number(o.properties?.find((p) => p.name === "variant")?.value ?? 0),
        x: Math.round(x + o.x - cx),
        y: Math.round(y + o.y - cy),
        tall: pi.tall,
        flip: false,
        light: light ? { color: String(light.value), radius: Number(lightR?.value ?? 96) } : undefined,
      });
    }
  }
  return { props, clearRadius: Number(getProp("clearRadius") ?? 200) };
}

/** What one chunk holds for one zone: its ground props (baked into the chunk) and its tall props. */
export interface ChunkContent {
  ground: MapProp[];
  tall: MapProp[];
}

/** Tile variant at tile (tx, ty): the same in every zone, so re-theming keeps the pattern. */
export function tileVariant(seed: number, tx: number, ty: number): number {
  return hash32(seed, "open-tile", tx, ty) & 0xff;
}

/**
 * The props of chunk (cx, cy) in a zone. Underfoot decorations are the
 * densest; tall props are sparse, so they never hide the action for
 * long (and they fade when the hero comes close).
 */
export function chunkContent(seed: number, cx: number, cy: number, theme: ZoneTheme, info: Record<string, PropInfo> = {}): ChunkContent {
  const r = new Rng(seed, "open-chunk", cx, cy, theme.id);
  const x0 = cx * OPEN_CHUNK;
  const y0 = cy * OPEN_CHUNK;
  const ground: MapProp[] = [];
  const tall: MapProp[] = [];
  const taken: { x: number; y: number; r: number }[] = [];
  const free = (x: number, y: number, rad: number) => taken.every((o) => (o.x - x) ** 2 + (o.y - y) ** 2 >= (o.r + rad) ** 2);
  // Worn ground patches.
  const patches = 4 + r.int(0, 3);
  for (let k = 0; k < patches; k++) {
    ground.push({ sprite: theme.terrain.patch, variant: r.int(0, 255), x: Math.round(x0 + r.next() * OPEN_CHUNK), y: Math.round(y0 + r.next() * OPEN_CHUNK), tall: false, flip: r.next() < 0.5 });
  }
  const tiles = (OPEN_CHUNK / TILE) ** 2;
  const attempts = Math.floor(tiles * 0.9);
  for (let a = 0; a < attempts; a++) {
    const x = x0 + r.next() * OPEN_CHUNK;
    const y = y0 + r.next() * OPEN_CHUNK;
    const roll = r.next();
    const variant = r.int(0, 255);
    const flip = r.next() < 0.5;
    let acc = 0;
    let chosen: ZoneTheme["props"][number] | undefined;
    for (const c of theme.props) {
      const pi = info[c.sprite] ?? DEFAULT_PROPS[c.sprite] ?? { tall: false, radius: 10 };
      // Underfoot decorations thickest; trees, bushes and ruins much sparser.
      acc += c.density * (c.underfoot ? 0.45 : pi.tall ? 0.1 : 0.3);
      if (roll < acc) {
        chosen = c;
        break;
      }
    }
    if (!chosen) continue;
    const pi = info[chosen.sprite] ?? DEFAULT_PROPS[chosen.sprite] ?? { tall: false, radius: 10 };
    const rad = pi.tall ? pi.radius * 3 : pi.radius;
    if (!free(x, y, rad)) continue;
    taken.push({ x, y, r: rad });
    const p: MapProp = { sprite: chosen.sprite, variant, x: Math.round(x), y: Math.round(y), tall: pi.tall, flip, light: chosen.light };
    (pi.tall || chosen.light ? tall : ground).push(p);
  }
  tall.sort((a, b) => a.y - b.y || a.x - b.x);
  return { ground, tall };
}

/** A tall prop's opacity with the hero at (hx, hy): it fades when the hero comes close. */
export function propFade(p: { x: number; y: number }, hx: number, hy: number): number {
  const d = Math.hypot(p.x - hx, p.y - 16 - (hy - 16));
  if (d >= FADE_FAR) return 1;
  if (d <= FADE_NEAR) return PROP_FADE;
  return PROP_FADE + ((1 - PROP_FADE) * (d - FADE_NEAR)) / (FADE_FAR - FADE_NEAR);
}
