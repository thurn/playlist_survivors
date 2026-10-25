/**
 * World atlas: 32 px terrain tiles and props (trees, plants, rocks, ruins,
 * animated fires and soft-edged ground patches) from assets-src/world.json.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { AtlasRect, PropDef, WorldAtlas } from "./types.ts";
import { gradientMap, Img, loadImage } from "./image.ts";
import { AtlasBuilder } from "./pack.ts";
import { ASSETS_SRC, resolveSource } from "./paths.ts";

type Rect = [number, number, number, number];

interface Piece { src: string; rect: Rect; at: [number, number]; scale?: number }

interface VariantDef {
  src?: string;
  rect?: Rect;
  /** Animation frames (rects of equal size) instead of `rect`. */
  frames?: Rect[];
  /** Several pieces drawn into one image. */
  compose?: Piece[];
  /** A soft-edged ground patch cut from an autotile "hole" block. */
  patch?: { src: string; hole: Rect; fill: [number, number]; edgeRecolor?: string; fillRecolor?: string; fringe?: number };
  recolor?: string;
  flip?: boolean;
  /** Fade this many pixels of the outer edge to soft alpha. */
  feather?: number;
  /** Scripted ruin edit: knock the top off at a jagged line between these fractions of the height. */
  broken?: { seed: number; min: number; max: number };
  anchor?: [number, number];
}

interface WorldDef {
  regions: Record<string, Rect>;
  recolors: Record<string, { stops: string[] }>;
  tiles: Record<string, { src: string; cells: Array<[number, number]>; recolor?: string }>;
  props: Record<string, { tall: boolean; radius: number; fps?: number; flat?: boolean; variants: VariantDef[] }>;
}

export async function bakeWorld(use: (ref: string, usedFor: string) => void, log: (s: string) => void): Promise<{ atlas: WorldAtlas; pages: Img[] }> {
  const def = JSON.parse(readFileSync(join(ASSETS_SRC, "world.json"), "utf8")) as WorldDef;
  const recolorWith = (img: Img, name: string | undefined): Img => {
    if (!name) return img;
    const r = def.recolors[name];
    if (!r) throw new Error(`world: unknown recolor ${name}`);
    return gradientMap(img, r.stops);
  };
  const load = async (ref: string, rect: Rect | null, usedFor: string): Promise<Img> => {
    const region = ref.includes("#") ? def.regions[ref] : undefined;
    if (ref.includes("#") && !region) throw new Error(`world: ${ref} names an unknown region`);
    if (region && rect) {
      const [rx, ry, rw, rh] = region;
      if (rect[0] < rx || rect[1] < ry || rect[0] + rect[2] > rx + rw || rect[1] + rect[3] > ry + rh) {
        throw new Error(`world: rect ${rect.join(",")} for ${usedFor} is outside the reviewed region ${ref}`);
      }
    }
    use(ref, `world:${usedFor}`);
    const img = await loadImage(resolveSource(ref));
    return rect ? img.crop(...rect) : img;
  };

  const builder = new AtlasBuilder({ pageW: 2048, pageH: 2048, maxPages: 2, padding: 2, extrude: 1 });

  // Tiles: exact 32x32 cells; the extrusion keeps neighbouring tiles from bleeding.
  const tileHandles: Record<string, number[]> = {};
  for (const [id, t] of Object.entries(def.tiles)) {
    const handles: number[] = [];
    for (const [c, r] of t.cells) {
      const img = recolorWith(await load(t.src, [c * 32, r * 32, 32, 32], id), t.recolor);
      for (let i = 3; i < img.data.length; i += 4) if (img.data[i] !== 255) throw new Error(`world: tile ${id} cell ${c},${r} is not opaque`);
      handles.push(builder.add(img));
    }
    tileHandles[id] = handles;
  }

  // Props.
  type PendingVariant = { frames: number[]; anchorX: number; anchorY: number };
  const propVariants: Record<string, PendingVariant[]> = {};
  for (const [id, p] of Object.entries(def.props)) {
    const out: PendingVariant[] = [];
    for (const v of p.variants) {
      let frames: Img[];
      if (v.frames) {
        if (!v.src) throw new Error(`world: ${id} frames need src`);
        frames = await Promise.all(v.frames.map((r) => load(v.src!, r, id)));
      } else if (v.compose) {
        const w = Math.max(...v.compose.map((c) => c.at[0] + Math.round(c.rect[2] * (c.scale ?? 1))));
        const h = Math.max(...v.compose.map((c) => c.at[1] + Math.round(c.rect[3] * (c.scale ?? 1))));
        const img = new Img(w, h);
        for (const c of v.compose) {
          let piece = await load(c.src, c.rect, id);
          if (c.scale && c.scale !== 1) piece = piece.scaleNearest(c.scale);
          img.draw(piece, c.at[0], c.at[1]);
        }
        frames = [img];
      } else if (v.patch) {
        frames = [await buildPatch(v.patch, id, load, recolorWith, def)];
      } else {
        if (!v.src || !v.rect) throw new Error(`world: ${id} variant needs src and rect`);
        frames = [await load(v.src, v.rect, id)];
      }
      frames = frames.map((f) => recolorWith(f, v.recolor));
      if (v.broken) frames = frames.map((f) => breakTop(f, v.broken!));
      if (v.feather) frames = frames.map((f) => feather(f, v.feather!));
      // Trim all frames to their union bounds.
      let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
      for (const f of frames) {
        const bb = f.bbox();
        if (!bb) continue;
        x0 = Math.min(x0, bb.x); y0 = Math.min(y0, bb.y); x1 = Math.max(x1, bb.x + bb.w); y1 = Math.max(y1, bb.y + bb.h);
      }
      if (!isFinite(x0)) throw new Error(`world: ${id} variant is empty`);
      const trimmed = frames.map((f) => f.crop(x0, y0, x1 - x0, y1 - y0));
      const ax = v.anchor ? v.anchor[0] - x0 : Math.round((x1 - x0) / 2);
      // Ground contact: the bottom of the art (a little up for tall props with roots or
      // bases); flat ground art such as patches and pools is anchored at its centre.
      const ay = v.anchor ? v.anchor[1] - y0 : p.flat ? Math.round((y1 - y0) / 2) : y1 - y0 - (p.tall ? 2 : 1);
      const variants = [{ imgs: trimmed, ax }];
      if (v.flip) variants.push({ imgs: trimmed.map((f) => f.flipH()), ax: trimmed[0].w - ax });
      for (const vv of variants) out.push({ frames: vv.imgs.map((f) => builder.add(f)), anchorX: vv.ax, anchorY: ay });
    }
    propVariants[id] = out;
  }

  log(`  packing ${builder.uniqueCount} unique world images`);
  const packed = builder.pack();
  const tiles: Record<string, AtlasRect[]> = {};
  for (const [id, hs] of Object.entries(tileHandles)) tiles[id] = hs.map((h) => packed.rectOf(h));
  const props: Record<string, PropDef> = {};
  for (const [id, vs] of Object.entries(propVariants)) {
    const p = def.props[id];
    const animated = vs.some((v) => v.frames.length > 1);
    props[id] = {
      variants: vs.map((v) => ({ rect: packed.rectOf(v.frames[0]), anchorX: v.anchorX, anchorY: v.anchorY })),
      tall: p.tall,
      radius: p.radius,
      ...(animated ? { frames: vs.map((v) => v.frames.map((h) => packed.rectOf(h))), fps: p.fps ?? 8 } : {}),
    };
  }
  return { atlas: { pages: packed.pages.map((_, i) => `world-${i}.png`), tiles, props }, pages: packed.pages };
}

/** Soften the outer `n` pixels of the art's alpha by distance to the transparent outside. */
function feather(img: Img, n: number): Img {
  const out = img.clone();
  const dist = distanceToTransparent(img, n + 1);
  for (let y = 0; y < img.h; y++) for (let x = 0; x < img.w; x++) {
    const d = dist[y * img.w + x];
    if (d <= n && img.alpha(x, y)) {
      const i = out.idx(x, y);
      out.data[i + 3] = Math.round(out.data[i + 3] * (d / (n + 1)));
    }
  }
  return out;
}

/** Chebyshev-ish distance (in pixels, capped) from each pixel to the nearest transparent pixel or edge. */
function distanceToTransparent(img: Img, cap: number): Uint8Array {
  const d = new Uint8Array(img.w * img.h).fill(cap);
  for (let y = 0; y < img.h; y++) for (let x = 0; x < img.w; x++) {
    if (!img.alpha(x, y)) { d[y * img.w + x] = 0; continue; }
    let best = cap;
    for (let r = 1; r < cap && best === cap; r++) {
      for (let dy = -r; dy <= r && best === cap; dy++) for (let dx = -r; dx <= r; dx++) {
        if (Math.max(Math.abs(dx), Math.abs(dy)) !== r) continue;
        if (!img.alpha(x + dx, y + dy)) { best = r; break; }
      }
    }
    d[y * img.w + x] = best;
  }
  return d;
}

/**
 * Build a ground patch from an LPC autotile block whose grass has a
 * transparent hole: the hole is filled with the ground tile, and the grass
 * blades around it are kept as a fringe that fades out, so the patch blends
 * into grass ground with a soft, organic edge.
 */
async function buildPatch(
  p: NonNullable<VariantDef["patch"]>,
  id: string,
  load: (ref: string, rect: Rect | null, usedFor: string) => Promise<Img>,
  recolorWith: (img: Img, name: string | undefined) => Img,
  _def: WorldDef,
): Promise<Img> {
  const block = recolorWith(await load(p.src, p.hole, id), p.edgeRecolor);
  const fillTile = recolorWith(await load(p.src, [p.fill[0] * 32, p.fill[1] * 32, 32, 32], id), p.fillRecolor);
  const W = block.w, H = block.h;
  // Flood-fill the transparent hole from the centre.
  const hole = new Uint8Array(W * H);
  const stack = [Math.floor(H / 2) * W + Math.floor(W / 2)];
  if (block.alpha(Math.floor(W / 2), Math.floor(H / 2))) throw new Error(`world: ${id} patch block centre is not a hole`);
  while (stack.length) {
    const q = stack.pop()!;
    if (hole[q]) continue;
    const x = q % W, y = (q - x) / W;
    if (block.alpha(x, y)) continue;
    hole[q] = 1;
    if (x > 0) stack.push(q - 1);
    if (x < W - 1) stack.push(q + 1);
    if (y > 0) stack.push(q - W);
    if (y < H - 1) stack.push(q + W);
  }
  for (let x = 0; x < W; x++) if (hole[x] || hole[(H - 1) * W + x]) throw new Error(`world: ${id} patch hole touches the block edge`);
  // Distance (in pixels) from the hole, for the fringe fade.
  const fringe = p.fringe ?? 4;
  const out = new Img(W, H);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    if (hole[y * W + x]) {
      out.set(x, y, fillTile.get(x % 32, y % 32));
      continue;
    }
    let d = Infinity;
    for (let dy = -fringe; dy <= fringe; dy++) for (let dx = -fringe; dx <= fringe; dx++) {
      const nx = x + dx, ny = y + dy;
      if (nx < 0 || ny < 0 || nx >= W || ny >= H || !hole[ny * W + nx]) continue;
      d = Math.min(d, Math.hypot(dx, dy));
    }
    if (d > fringe) continue;
    const px = block.get(x, y);
    const a = d <= 1.5 ? 255 : Math.round(255 * Math.max(0, 1 - (d - 1.5) / (fringe - 0.5)));
    out.set(x, y, [px[0], px[1], px[2], Math.min(px[3], a)]);
  }
  return out;
}

/** Deterministic hash noise in [0, 1). */
function noise(seed: number, i: number): number {
  let h = (seed * 374761393 + i * 668265263) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/**
 * Ruin edit: remove everything above a jagged line (stepped every 5-9
 * columns, heights between `min` and `max` of the art's height, measured from
 * the bottom) and outline the new broken edge with the art's darkest colour.
 */
function breakTop(img: Img, b: { seed: number; min: number; max: number }): Img {
  const bb = img.bbox();
  if (!bb) return img;
  const out = img.clone();
  let dark: [number, number, number, number] = [0, 0, 0, 255];
  let best = Infinity;
  for (let i = 0; i < img.data.length; i += 4) {
    if (img.data[i + 3] !== 255) continue;
    const l = img.data[i] + img.data[i + 1] + img.data[i + 2];
    if (l < best) { best = l; dark = [img.data[i], img.data[i + 1], img.data[i + 2], 255]; }
  }
  const bottom = bb.y + bb.h;
  let x = bb.x;
  let step = 0;
  let prevTop = -1;
  let frac = (b.min + b.max) / 2;
  while (x < bb.x + bb.w) {
    const width = 5 + Math.floor(noise(b.seed, step * 2) * 5);
    // A clamped random walk keeps neighbouring steps close, like crumbled masonry.
    frac = Math.min(b.max, Math.max(b.min, frac + (noise(b.seed, step * 2 + 1) - 0.5) * 0.3));
    const top = Math.round(bottom - bb.h * frac);
    for (let cx = x; cx < Math.min(x + width, bb.x + bb.w); cx++) {
      for (let y = 0; y < top; y++) out.set(cx, y, [0, 0, 0, 0]);
      if (out.alpha(cx, top)) out.set(cx, top, dark);
    }
    // Outline the vertical step between neighbouring column groups.
    if (prevTop >= 0 && prevTop !== top) {
      const edgeX = prevTop < top ? x - 1 : x;
      for (let y = Math.min(prevTop, top); y <= Math.max(prevTop, top); y++) if (out.alpha(edgeX, y)) out.set(edgeX, y, dark);
    }
    prevTop = top;
    x += width;
    step++;
  }
  return out;
}
