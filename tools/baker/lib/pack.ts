/**
 * Deterministic atlas packing: a skyline bottom-left packer with padding and
 * edge extrusion, plus de-duplication of identical images.
 */
import type { AtlasRect } from "./types.ts";
import { Img } from "./image.ts";

export interface PackOptions {
  /** Page width and maximum page height in pixels. */
  pageW: number;
  pageH: number;
  maxPages: number;
  /** Transparent gap around each image (the extrusion lives inside it). */
  padding: number;
  /** How many pixels of edge extrusion to write into the padding. */
  extrude: number;
}

interface Entry {
  id: number;
  img: Img;
  hash: string;
  order: number;
}

export interface PackedAtlas {
  pages: Img[];
  rectOf(handle: number): AtlasRect;
}

export class AtlasBuilder {
  private entries: Entry[] = [];
  private byHash = new Map<string, number>();
  private rects = new Map<number, AtlasRect>();

  constructor(private readonly opts: PackOptions) {}

  /** Add an image; identical images share one rect. Returns a handle. */
  add(img: Img): number {
    if (img.w > this.opts.pageW - 2 * this.opts.padding || img.h > this.opts.pageH - 2 * this.opts.padding) {
      throw new Error(`image ${img.w}x${img.h} does not fit an atlas page`);
    }
    const hash = img.hash();
    const hit = this.byHash.get(hash);
    if (hit !== undefined) return hit;
    const id = this.entries.length;
    this.entries.push({ id, img, hash, order: id });
    this.byHash.set(hash, id);
    return id;
  }

  get uniqueCount(): number {
    return this.entries.length;
  }

  pack(): PackedAtlas {
    const { pageW, pageH, maxPages, padding } = this.opts;
    // Tallest first, then widest, then insertion order: fully deterministic.
    const sorted = [...this.entries].sort((a, b) => b.img.h - a.img.h || b.img.w - a.img.w || a.order - b.order);
    const pages: Array<{ skyline: Array<{ x: number; y: number; w: number }>; used: number; items: Array<{ e: Entry; x: number; y: number }> }> = [];
    const newPage = () => {
      if (pages.length >= maxPages) throw new Error(`atlas needs more than ${maxPages} pages of ${pageW}x${pageH}`);
      pages.push({ skyline: [{ x: 0, y: 0, w: pageW }], used: 0, items: [] });
    };
    newPage();
    for (const e of sorted) {
      const w = e.img.w + 2 * padding;
      const h = e.img.h + 2 * padding;
      let placed = false;
      for (const page of pages) {
        const spot = findSkylineSpot(page.skyline, w, h, pageW, pageH);
        if (!spot) continue;
        addSkyline(page.skyline, spot.i, spot.x, spot.y + h, w);
        page.items.push({ e, x: spot.x + padding, y: spot.y + padding });
        page.used = Math.max(page.used, spot.y + h);
        placed = true;
        break;
      }
      if (!placed) {
        newPage();
        const page = pages[pages.length - 1];
        const spot = findSkylineSpot(page.skyline, w, h, pageW, pageH);
        if (!spot) throw new Error("image does not fit an empty page");
        addSkyline(page.skyline, spot.i, spot.x, spot.y + h, w);
        page.items.push({ e, x: spot.x + padding, y: spot.y + padding });
        page.used = Math.max(page.used, spot.y + h);
      }
    }
    const out: Img[] = [];
    pages.forEach((page, pi) => {
      const height = Math.min(pageH, Math.ceil(page.used / 64) * 64);
      const width = pages.length === 1 ? Math.min(pageW, roundUp(Math.max(...page.items.map((it) => it.x + it.e.img.w + padding)), 64)) : pageW;
      const img = new Img(width, height);
      for (const it of page.items) {
        img.blit(it.e.img, it.x, it.y);
        extrudeEdges(img, it.x, it.y, it.e.img.w, it.e.img.h, this.opts.extrude);
        this.rects.set(it.e.id, { page: pi, x: it.x, y: it.y, w: it.e.img.w, h: it.e.img.h });
      }
      out.push(img);
    });
    return {
      pages: out,
      rectOf: (handle: number) => {
        const r = this.rects.get(handle);
        if (!r) throw new Error(`unknown atlas handle ${handle}`);
        return { ...r };
      },
    };
  }
}

function roundUp(v: number, m: number): number {
  return Math.ceil(v / m) * m;
}

function findSkylineSpot(sky: Array<{ x: number; y: number; w: number }>, w: number, h: number, pageW: number, pageH: number): { i: number; x: number; y: number } | null {
  let best: { i: number; x: number; y: number } | null = null;
  for (let i = 0; i < sky.length; i++) {
    const x = sky[i].x;
    if (x + w > pageW) break;
    let y = 0;
    let remaining = w;
    let j = i;
    while (remaining > 0) {
      if (j >= sky.length) { y = Infinity; break; }
      y = Math.max(y, sky[j].y);
      remaining -= sky[j].w;
      j++;
    }
    if (y + h > pageH) continue;
    if (!best || y < best.y || (y === best.y && x < best.x)) best = { i, x, y };
  }
  return best;
}

function addSkyline(sky: Array<{ x: number; y: number; w: number }>, i: number, x: number, top: number, w: number): void {
  const seg = { x, y: top, w };
  sky.splice(i, 0, seg);
  // Shrink or remove the segments now covered by the new one.
  const k = i + 1;
  while (k < sky.length) {
    const s = sky[k];
    const end = seg.x + seg.w;
    if (s.x >= end) break;
    const overlap = end - s.x;
    if (overlap >= s.w) {
      sky.splice(k, 1);
    } else {
      s.x += overlap;
      s.w -= overlap;
      break;
    }
  }
  // Merge neighbours at equal height.
  for (let m = 0; m < sky.length - 1; ) {
    if (sky[m].y === sky[m + 1].y) {
      sky[m].w += sky[m + 1].w;
      sky.splice(m + 1, 1);
    } else m++;
  }
}

/** Copy the outermost pixel rows/columns outward so filtering never samples a neighbour. */
function extrudeEdges(img: Img, x: number, y: number, w: number, h: number, n: number): void {
  for (let k = 1; k <= n; k++) {
    for (let xx = 0; xx < w; xx++) {
      img.set(x + xx, y - k, img.get(x + xx, y));
      img.set(x + xx, y + h - 1 + k, img.get(x + xx, y + h - 1));
    }
    for (let yy = -k; yy < h + k; yy++) {
      const cy = Math.min(h - 1, Math.max(0, yy));
      img.set(x - k, y + yy, img.get(x, y + cy));
      img.set(x + w - 1 + k, y + yy, img.get(x + w - 1, y + cy));
    }
  }
}
