/**
 * Render contact sheets of every baked asset for the human review gate.
 * Run with `npm run bake:contact-sheet` after `npm run bake`.
 *
 * Writes captures/contact-sheet.png (everything, 1x) plus 2x close-ups:
 * captures/contact-sheet-<sprite>.png per character, contact-sheet-world.png
 * and contact-sheet-effects.png. Each animation shows one row per facing
 * (up, left, down, right) and a row of hit-flash frames; a red dot marks the
 * anchor and a yellow frame marks the impact frame.
 */
import { mkdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import sharp from "sharp";
import type { AtlasRect, CharacterAtlas, EffectsAtlas, WorldAtlas } from "./lib/types.ts";
import { FACINGS } from "./lib/types.ts";
import { Img, loadImage, savePng } from "./lib/image.ts";
import { CAPTURES, OUT } from "./lib/paths.ts";

interface Label { x: number; y: number; text: string; size: number; color: string }

class Sheet {
  items: Array<{ img: Img; x: number; y: number; scale: number; bg?: boolean; mark?: { ax: number; ay: number }; border?: string }> = [];
  labels: Label[] = [];
  y = 8;
  width = 0;
  constructor(readonly maxW: number, readonly scale: number) {}

  label(text: string, size = 14, color = "#ffe9a8"): void {
    this.labels.push({ x: 8, y: this.y + size, text, size, color });
    this.y += size + 6;
  }

  /** Lay frames out in rows that wrap at maxW. */
  row(frames: Img[], opts: { prefix?: string; anchor?: { ax: number; ay: number }; anchors?: Array<{ ax: number; ay: number }>; impact?: number } = {}): void {
    const s = this.scale;
    let x = 8;
    if (opts.prefix) {
      this.labels.push({ x, y: this.y + 12, text: opts.prefix, size: 11, color: "#b8c4d8" });
      x += 48;
    }
    let rowH = 0;
    frames.forEach((f, i) => {
      const w = f.w * s, h = f.h * s;
      if (x + w > this.maxW - 8 && x > 56) {
        this.y += rowH + 4;
        x = 56;
        rowH = 0;
      }
      this.items.push({ img: f, x, y: this.y, scale: s, bg: true, mark: opts.anchors?.[i] ?? opts.anchor, border: i === opts.impact ? "#ffd000" : undefined });
      this.labels.push({ x: x + 1, y: this.y + 9, text: String(i), size: 8, color: "#ffffff" });
      x += w + 4;
      rowH = Math.max(rowH, h);
      this.width = Math.max(this.width, x);
    });
    this.y += rowH + 6;
  }

  gap(n = 10): void {
    this.y += n;
  }

  async save(path: string): Promise<void> {
    const img = await this.render();
    mkdirSync(CAPTURES, { recursive: true });
    await savePng(img, path);
    console.log(`wrote ${path} (${img.w}x${img.h})`);
  }

  /** Draw frames and labels into one image. */
  async render(): Promise<Img> {
    const W = Math.max(400, Math.min(this.maxW, this.width + 8));
    const H = this.y + 8;
    const base = new Img(W, H);
    for (let i = 0; i < base.data.length; i += 4) {
      base.data[i] = 38; base.data[i + 1] = 42; base.data[i + 2] = 54; base.data[i + 3] = 255;
    }
    for (const it of this.items) {
      const img = it.scale === 1 ? it.img : it.img.scaleNearest(it.scale);
      if (it.bg) {
        // Checkerboard behind each frame so transparency and bleeding are visible.
        for (let yy = 0; yy < img.h; yy++) for (let xx = 0; xx < img.w; xx++) {
          const c = ((xx >> 3) + (yy >> 3)) & 1 ? 74 : 62;
          base.set(it.x + xx, it.y + yy, [c, c + 4, c + 14, 255]);
        }
      }
      base.draw(img, it.x, it.y);
      if (it.border) {
        for (let xx = -1; xx <= img.w; xx++) { base.set(it.x + xx, it.y - 1, [255, 208, 0, 255]); base.set(it.x + xx, it.y + img.h, [255, 208, 0, 255]); }
        for (let yy = -1; yy <= img.h; yy++) { base.set(it.x - 1, it.y + yy, [255, 208, 0, 255]); base.set(it.x + img.w, it.y + yy, [255, 208, 0, 255]); }
      }
      if (it.mark) {
        const mx = it.x + Math.round(it.mark.ax * it.scale), my = it.y + Math.round(it.mark.ay * it.scale);
        for (let d = -2; d <= 2; d++) { base.set(mx + d, my, [255, 40, 40, 255]); base.set(mx, my + d, [255, 40, 40, 255]); }
      }
    }
    const esc = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;");
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}">${this.labels
      .map((l) => `<text x="${l.x}" y="${l.y}" font-family="Menlo, monospace" font-size="${l.size}" fill="${l.color}" stroke="#000" stroke-width="0.6" paint-order="stroke">${esc(l.text)}</text>`)
      .join("")}</svg>`;
    const { data } = await sharp(Buffer.from(base.data.buffer, base.data.byteOffset, base.data.length), { raw: { width: W, height: H, channels: 4 } })
      .composite([{ input: Buffer.from(svg), top: 0, left: 0 }])
      .raw()
      .toBuffer({ resolveWithObject: true });
    return new Img(W, H, new Uint8Array(data.buffer, data.byteOffset, data.length).slice());
  }
}

/** Tile section images into columns (tallest first into the shortest column). */
async function saveOverview(sections: Img[], path: string): Promise<void> {
  const colH = Math.max(6000, ...sections.map((s) => s.h));
  const cols: Array<{ items: Img[]; h: number; w: number }> = [];
  for (const s of sections) {
    let col = cols.find((c) => c.h + s.h <= colH);
    if (!col) cols.push((col = { items: [], h: 0, w: 0 }));
    col.items.push(s);
    col.h += s.h;
    col.w = Math.max(col.w, s.w);
  }
  const W = cols.reduce((a, c) => a + c.w, 0), H = Math.max(...cols.map((c) => c.h));
  const out = new Img(W, H);
  for (let i = 0; i < out.data.length; i += 4) { out.data[i] = 38; out.data[i + 1] = 42; out.data[i + 2] = 54; out.data[i + 3] = 255; }
  let x = 0;
  for (const c of cols) {
    let y = 0;
    for (const s of c.items) {
      out.blit(s, x, y);
      y += s.h;
    }
    x += c.w;
  }
  mkdirSync(CAPTURES, { recursive: true });
  await savePng(out, path);
  console.log(`wrote ${path} (${W}x${H})`);
}

async function pagesOf(names: string[]): Promise<Img[]> {
  return Promise.all(names.map((n) => loadImage(join(OUT, n))));
}

function cut(pages: Img[], r: AtlasRect): Img {
  return pages[r.page].crop(r.x, r.y, r.w, r.h);
}

async function main(): Promise<void> {
  const chars = JSON.parse(readFileSync(join(OUT, "characters.json"), "utf8")) as CharacterAtlas;
  const world = JSON.parse(readFileSync(join(OUT, "world.json"), "utf8")) as WorldAtlas;
  const fx = JSON.parse(readFileSync(join(OUT, "effects.json"), "utf8")) as EffectsAtlas;
  const cp = await pagesOf(chars.pages);
  const wp = await pagesOf(world.pages);
  const ep = await pagesOf(fx.pages);
  const sections: Img[] = [];

  const charSection = (sheet: Sheet, id: string) => {
    const sprite = chars.sprites[id];
    sheet.label(`sprite ${id}`, 18, "#9fe0ff");
    for (const [name, a] of Object.entries(sprite.animations)) {
      sheet.label(`${id} / ${name}  frame ${a.frameW}x${a.frameH}  ${a.frames.down.length} frames  anchor (${a.anchorX},${a.anchorY})${a.impactFrame !== undefined ? `  impact ${a.impactFrame}` : ""}${a.fps ? `  ${a.fps} fps` : ""}`, 12);
      for (const facing of FACINGS) {
        sheet.row(a.frames[facing].map((f) => cut(cp, f)), { prefix: facing, anchor: { ax: a.anchorX, ay: a.anchorY }, impact: a.impactFrame });
      }
      sheet.row(a.frames.down.map((f) => cut(cp, f.flash)), { prefix: "flash" });
    }
    sheet.gap();
  };
  for (const id of Object.keys(chars.sprites)) {
    const sec = new Sheet(1400, 1);
    charSection(sec, id);
    sections.push(await sec.render());
    const one = new Sheet(2400, 2);
    charSection(one, id);
    await one.save(join(CAPTURES, `contact-sheet-${id}.png`));
  }

  const worldSection = (sheet: Sheet) => {
    sheet.label("world tiles", 18, "#9fe0ff");
    for (const [id, rects] of Object.entries(world.tiles)) {
      sheet.label(`${id} (${rects.length} variants) and a 6x3 random-ish tiling`, 12);
      const tiles = rects.map((r) => cut(wp, r));
      const tiled = new Img(32 * 6, 32 * 3);
      for (let y = 0; y < 3; y++) for (let x = 0; x < 6; x++) tiled.blit(tiles[(x * 7 + y * 3 + x * y) % tiles.length], x * 32, y * 32);
      sheet.row([...tiles, tiled]);
    }
    sheet.label("world props", 18, "#9fe0ff");
    for (const [id, p] of Object.entries(world.props)) {
      sheet.label(`${id}  tall=${p.tall} radius=${p.radius}${p.frames ? `  animated ${p.fps} fps` : ""}`, 12);
      sheet.row(p.variants.map((v) => cut(wp, v.rect)), { anchors: p.variants.map((v) => ({ ax: v.anchorX, ay: v.anchorY })) });
      p.frames?.forEach((fr, i) => sheet.row(fr.map((r) => cut(wp, r)), { prefix: `v${i}`, anchor: { ax: p.variants[i].anchorX, ay: p.variants[i].anchorY } }));
    }
  };
  const effectsSection = (sheet: Sheet) => {
    sheet.label("effects", 18, "#9fe0ff");
    for (const [id, a] of Object.entries(fx.anims)) {
      sheet.label(`${id}  ${a.frames.length} frames @ ${a.fps} fps  anchor (${a.anchorX},${a.anchorY})`, 12);
      sheet.row(a.frames.map((r) => cut(ep, r)), { anchor: { ax: a.anchorX, ay: a.anchorY } });
    }
  };
  const ws = new Sheet(1400, 1);
  worldSection(ws);
  sections.push(await ws.render());
  const es = new Sheet(1400, 1);
  effectsSection(es);
  sections.push(await es.render());
  await saveOverview(sections, join(CAPTURES, "contact-sheet.png"));
  const w2 = new Sheet(2400, 2);
  worldSection(w2);
  await w2.save(join(CAPTURES, "contact-sheet-world.png"));
  const e2 = new Sheet(2400, 1);
  effectsSection(e2);
  await e2.save(join(CAPTURES, "contact-sheet-effects.png"));
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
