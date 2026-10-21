/**
 * A tiny straight-alpha RGBA image type plus the pixel operations the baker
 * needs. All compositing happens in plain JS so results are deterministic;
 * sharp is only used to decode and encode PNGs.
 */
import { createHash } from "node:crypto";
import { mkdirSync } from "node:fs";
import { dirname } from "node:path";
import sharp from "sharp";

export class Img {
  readonly w: number;
  readonly h: number;
  readonly data: Uint8Array;

  constructor(w: number, h: number, data?: Uint8Array) {
    this.w = w;
    this.h = h;
    this.data = data ?? new Uint8Array(w * h * 4);
    if (this.data.length !== w * h * 4) throw new Error(`bad image buffer ${w}x${h}`);
  }

  clone(): Img {
    return new Img(this.w, this.h, new Uint8Array(this.data));
  }

  idx(x: number, y: number): number {
    return (y * this.w + x) * 4;
  }

  alpha(x: number, y: number): number {
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return 0;
    return this.data[(y * this.w + x) * 4 + 3];
  }

  get(x: number, y: number): [number, number, number, number] {
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return [0, 0, 0, 0];
    const i = this.idx(x, y);
    return [this.data[i], this.data[i + 1], this.data[i + 2], this.data[i + 3]];
  }

  set(x: number, y: number, p: readonly number[]): void {
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return;
    const i = this.idx(x, y);
    this.data[i] = p[0];
    this.data[i + 1] = p[1];
    this.data[i + 2] = p[2];
    this.data[i + 3] = p[3];
  }

  /** Copy a region into a new image (out-of-bounds pixels are transparent). */
  crop(x: number, y: number, w: number, h: number): Img {
    const out = new Img(w, h);
    for (let yy = 0; yy < h; yy++) {
      const sy = y + yy;
      if (sy < 0 || sy >= this.h) continue;
      for (let xx = 0; xx < w; xx++) {
        const sx = x + xx;
        if (sx < 0 || sx >= this.w) continue;
        const si = (sy * this.w + sx) * 4;
        const di = (yy * w + xx) * 4;
        out.data[di] = this.data[si];
        out.data[di + 1] = this.data[si + 1];
        out.data[di + 2] = this.data[si + 2];
        out.data[di + 3] = this.data[si + 3];
      }
    }
    return out;
  }

  /** Draw `src` over this image at (dx, dy) with source-over blending. */
  draw(src: Img, dx: number, dy: number, opacity = 1): void {
    for (let y = 0; y < src.h; y++) {
      const ty = dy + y;
      if (ty < 0 || ty >= this.h) continue;
      for (let x = 0; x < src.w; x++) {
        const tx = dx + x;
        if (tx < 0 || tx >= this.w) continue;
        const si = (y * src.w + x) * 4;
        let sa = src.data[si + 3];
        if (opacity !== 1) sa = Math.round(sa * opacity);
        if (sa === 0) continue;
        const di = (ty * this.w + tx) * 4;
        if (sa === 255) {
          this.data[di] = src.data[si];
          this.data[di + 1] = src.data[si + 1];
          this.data[di + 2] = src.data[si + 2];
          this.data[di + 3] = 255;
          continue;
        }
        const da = this.data[di + 3];
        const sA = sa / 255;
        const dA = da / 255;
        const oA = sA + dA * (1 - sA);
        for (let c = 0; c < 3; c++) {
          const v = (src.data[si + c] * sA + this.data[di + c] * dA * (1 - sA)) / oA;
          this.data[di + c] = Math.round(v);
        }
        this.data[di + 3] = Math.round(oA * 255);
      }
    }
  }

  /** Copy pixels (no blending) into this image. */
  blit(src: Img, dx: number, dy: number): void {
    for (let y = 0; y < src.h; y++) {
      const ty = dy + y;
      if (ty < 0 || ty >= this.h) continue;
      for (let x = 0; x < src.w; x++) {
        const tx = dx + x;
        if (tx < 0 || tx >= this.w) continue;
        const si = (y * src.w + x) * 4;
        const di = (ty * this.w + tx) * 4;
        this.data[di] = src.data[si];
        this.data[di + 1] = src.data[si + 1];
        this.data[di + 2] = src.data[si + 2];
        this.data[di + 3] = src.data[si + 3];
      }
    }
  }

  isEmpty(): boolean {
    for (let i = 3; i < this.data.length; i += 4) if (this.data[i] !== 0) return false;
    return true;
  }

  /** Bounding box of non-transparent pixels, or null when empty. */
  bbox(): { x: number; y: number; w: number; h: number } | null {
    let x0 = this.w, y0 = this.h, x1 = -1, y1 = -1;
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        if (this.data[(y * this.w + x) * 4 + 3] !== 0) {
          if (x < x0) x0 = x;
          if (x > x1) x1 = x;
          if (y < y0) y0 = y;
          if (y > y1) y1 = y;
        }
      }
    }
    if (x1 < 0) return null;
    return { x: x0, y: y0, w: x1 - x0 + 1, h: y1 - y0 + 1 };
  }

  flipH(): Img {
    const out = new Img(this.w, this.h);
    for (let y = 0; y < this.h; y++)
      for (let x = 0; x < this.w; x++) out.set(this.w - 1 - x, y, this.get(x, y));
    return out;
  }

  /** Nearest-neighbour scale by an integer or fractional factor. */
  scaleNearest(sx: number, sy = sx): Img {
    const w = Math.max(1, Math.round(this.w * sx));
    const h = Math.max(1, Math.round(this.h * sy));
    const out = new Img(w, h);
    for (let y = 0; y < h; y++) {
      const srcY = Math.min(this.h - 1, Math.floor((y + 0.5) / sy));
      for (let x = 0; x < w; x++) {
        const srcX = Math.min(this.w - 1, Math.floor((x + 0.5) / sx));
        out.set(x, y, this.get(srcX, srcY));
      }
    }
    return out;
  }

  /** Every opaque pixel becomes white, keeping alpha: the hit-flash frame. */
  whiteSilhouette(): Img {
    const out = this.clone();
    for (let i = 0; i < out.data.length; i += 4) {
      if (out.data[i + 3] !== 0) {
        out.data[i] = 255;
        out.data[i + 1] = 255;
        out.data[i + 2] = 255;
      } else {
        out.data[i] = out.data[i + 1] = out.data[i + 2] = 0;
      }
    }
    return out;
  }

  /** Multiply alpha by a factor. */
  fade(f: number): Img {
    const out = this.clone();
    for (let i = 3; i < out.data.length; i += 4) out.data[i] = Math.round(out.data[i] * f);
    return out;
  }

  /** Zero the colour of fully transparent pixels so identical-looking frames hash identically. */
  normalize(): this {
    for (let i = 0; i < this.data.length; i += 4) {
      if (this.data[i + 3] === 0) this.data[i] = this.data[i + 1] = this.data[i + 2] = 0;
    }
    return this;
  }

  hash(): string {
    return createHash("sha1").update(`${this.w}x${this.h}:`).update(this.data).digest("hex");
  }
}

const imageCache = new Map<string, Img>();

/** Decode a PNG (or any sharp-readable image) to straight RGBA. Cached by path. */
export async function loadImage(path: string): Promise<Img> {
  const hit = imageCache.get(path);
  if (hit) return hit;
  const { data, info } = await sharp(path).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  if (info.channels !== 4) throw new Error(`unexpected channel count in ${path}`);
  const img = new Img(info.width, info.height, new Uint8Array(data.buffer, data.byteOffset, data.length).slice());
  img.normalize();
  imageCache.set(path, img);
  return img;
}

/** Encode deterministically to PNG. */
export async function savePng(img: Img, path: string): Promise<void> {
  mkdirSync(dirname(path), { recursive: true });
  await sharp(Buffer.from(img.data.buffer, img.data.byteOffset, img.data.length), {
    raw: { width: img.w, height: img.h, channels: 4 },
  })
    .png({ compressionLevel: 9, adaptiveFiltering: true, palette: false })
    .toFile(path);
}

export function hexToRgb(hex: string): [number, number, number] {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) throw new Error(`bad colour ${hex}`);
  const n = parseInt(m[1], 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

/**
 * Replace colours using parallel source/target palettes, matching within ±1
 * per channel like the generator's recolor shader.
 */
export function recolor(img: Img, pairs: Array<{ from: string[]; to: string[] }>): Img {
  const list: Array<[number[], number[]]> = [];
  for (const { from, to } of pairs) {
    if (from.length !== to.length) throw new Error(`palette length mismatch ${from.length} vs ${to.length}`);
    for (let i = 0; i < from.length; i++) list.push([hexToRgb(from[i]), hexToRgb(to[i])]);
  }
  const out = img.clone();
  const d = out.data;
  for (let i = 0; i < d.length; i += 4) {
    if (d[i + 3] === 0) continue;
    for (const [s, t] of list) {
      if (Math.abs(d[i] - s[0]) <= 1 && Math.abs(d[i + 1] - s[1]) <= 1 && Math.abs(d[i + 2] - s[2]) <= 1) {
        d[i] = t[0];
        d[i + 1] = t[1];
        d[i + 2] = t[2];
        break;
      }
    }
  }
  return out;
}

/** Map each pixel's luminance through a colour gradient (used for the ember recolor). */
export function gradientMap(img: Img, stops: string[], gamma = 1, mix = 1): Img {
  const cols = stops.map(hexToRgb);
  const out = img.clone();
  const d = out.data;
  for (let i = 0; i < d.length; i += 4) {
    if (d[i + 3] === 0) continue;
    const l = Math.pow((0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2]) / 255, gamma);
    const f = Math.min(0.99999, Math.max(0, l)) * (cols.length - 1);
    const k = Math.floor(f);
    const t = f - k;
    for (let c = 0; c < 3; c++) {
      const v = cols[k][c] * (1 - t) + cols[k + 1][c] * t;
      d[i + c] = Math.round(d[i + c] * (1 - mix) + v * mix);
    }
  }
  return out;
}

/** Connected components of non-transparent pixels (8-connected). */
export function components(img: Img, minPixels = 1): Array<{ x: number; y: number; w: number; h: number; n: number; pixels: number[] }> {
  const seen = new Uint8Array(img.w * img.h);
  const out: Array<{ x: number; y: number; w: number; h: number; n: number; pixels: number[] }> = [];
  for (let y = 0; y < img.h; y++) {
    for (let x = 0; x < img.w; x++) {
      const p = y * img.w + x;
      if (seen[p] || img.data[p * 4 + 3] === 0) continue;
      const stack = [p];
      seen[p] = 1;
      const pixels: number[] = [];
      let x0 = x, x1 = x, y0 = y, y1 = y;
      while (stack.length) {
        const q = stack.pop()!;
        pixels.push(q);
        const qx = q % img.w, qy = (q - qx) / img.w;
        if (qx < x0) x0 = qx;
        if (qx > x1) x1 = qx;
        if (qy < y0) y0 = qy;
        if (qy > y1) y1 = qy;
        for (let dy = -1; dy <= 1; dy++) {
          for (let dx = -1; dx <= 1; dx++) {
            const nx = qx + dx, ny = qy + dy;
            if (nx < 0 || ny < 0 || nx >= img.w || ny >= img.h) continue;
            const r = ny * img.w + nx;
            if (!seen[r] && img.data[r * 4 + 3] !== 0) {
              seen[r] = 1;
              stack.push(r);
            }
          }
        }
      }
      if (pixels.length >= minPixels) out.push({ x: x0, y: y0, w: x1 - x0 + 1, h: y1 - y0 + 1, n: pixels.length, pixels });
    }
  }
  return out;
}
