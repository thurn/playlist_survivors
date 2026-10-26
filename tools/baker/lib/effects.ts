/**
 * Effects atlas: slash arcs, explosions, spells, lightning, fire, gore and
 * sparkles from assets-src/effects.json.
 */
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { EffectAnim, EffectsAtlas } from "./types.ts";
import { Img, loadImage } from "./image.ts";
import { AtlasBuilder } from "./pack.ts";
import { ASSETS_SRC, resolveSource } from "./paths.ts";

interface AnimDef {
  strip?: { src: string; frameW: number; frameH: number };
  grid?: { src: string; frameW: number; frameH: number; count?: number; first?: number };
  files?: { list?: string[]; dir?: string };
  script?: string;
  scale?: number;
  anchor?: "center" | "bottom";
  fps: number;
  /** One-line description for the generated catalogue. */
  desc: string;
}

function naturalCompare(a: string, b: string): number {
  return a.localeCompare(b, "en", { numeric: true });
}

/** Drop empty frames at the end of a sequence (sheets are often padded). */
function dropTrailingEmpty(frames: Img[]): Img[] {
  let n = frames.length;
  while (n > 1 && frames[n - 1].isEmpty()) n--;
  return frames.slice(0, n);
}

/**
 * The soul wisp: the LPC fire flame recoloured to a pale spectral blue,
 * flickering through its three frames while it rises, narrows and fades.
 */
function soulWisp(fireSheet: Img): Img[] {
  const W = 24, H = 52;
  const flames = [1, 2, 3].map((i) => {
    const f = fireSheet.crop(i * 32, 0, 32, 32);
    const bb = f.bbox()!;
    return f.crop(bb.x, bb.y, bb.w, bb.h);
  });
  const frames: Img[] = [];
  for (let i = 0; i < 8; i++) {
    const t = i / 7;
    const blob = flames[i % 3].scaleNearest(0.8 - 0.35 * t, 0.8 + 0.1 * t);
    for (let k = 0; k < blob.data.length; k += 4) {
      const l = (0.299 * blob.data[k] + 0.587 * blob.data[k + 1] + 0.114 * blob.data[k + 2]) / 255;
      blob.data[k] = Math.round(110 + 140 * l);
      blob.data[k + 1] = Math.round(170 + 85 * l);
      blob.data[k + 2] = Math.round(230 + 25 * l);
    }
    const f = new Img(W, H);
    f.draw(blob.fade(1 - 0.8 * t), Math.round((W - blob.w) / 2), Math.round(H - blob.h - t * 26));
    frames.push(f);
  }
  return frames;
}

export async function bakeEffects(use: (ref: string, usedFor: string) => void, log: (s: string) => void): Promise<{ atlas: EffectsAtlas; pages: Img[] }> {
  const def = JSON.parse(readFileSync(join(ASSETS_SRC, "effects.json"), "utf8")) as { anims: Record<string, AnimDef> };
  const load = async (ref: string, id: string) => {
    use(ref, `effect:${id}`);
    return loadImage(resolveSource(ref));
  };
  const builder = new AtlasBuilder({ pageW: 2048, pageH: 2048, maxPages: 3, padding: 2, extrude: 1 });
  const pending: Record<string, { handles: number[]; anchorX: number; anchorY: number; fps: number }> = {};
  for (const [id, a] of Object.entries(def.anims)) {
    let frames: Img[];
    if (a.strip) {
      const img = await load(a.strip.src, id);
      const n = Math.floor(img.w / a.strip.frameW);
      frames = dropTrailingEmpty(Array.from({ length: n }, (_, i) => img.crop(i * a.strip!.frameW, 0, a.strip!.frameW, a.strip!.frameH)));
    } else if (a.grid) {
      const g = a.grid;
      const img = await load(g.src, id);
      const cols = Math.floor(img.w / g.frameW), rows = Math.floor(img.h / g.frameH);
      const all = Array.from({ length: cols * rows }, (_, i) => img.crop((i % cols) * g.frameW, Math.floor(i / cols) * g.frameH, g.frameW, g.frameH));
      const from = g.first ?? 0;
      frames = dropTrailingEmpty(all.slice(from, g.count !== undefined ? from + g.count : undefined));
      if (frames.some((f) => f.isEmpty())) frames = frames.filter((f) => !f.isEmpty());
    } else if (a.files) {
      let refs = a.files.list ?? [];
      if (a.files.dir) {
        const dir = a.files.dir;
        refs = readdirSync(resolveSource(dir)).filter((f) => f.toLowerCase().endsWith(".png")).sort(naturalCompare).map((f) => `${dir}/${f}`);
      }
      if (refs.length === 0) throw new Error(`effects: ${id} has no frames`);
      const imgs = await Promise.all(refs.map((r) => load(r, id)));
      // Frames cut from one strip can differ by a pixel in width: pad to a common size, top-left aligned.
      const W = Math.max(...imgs.map((i) => i.w)), H = Math.max(...imgs.map((i) => i.h));
      frames = imgs.map((i) => {
        const f = new Img(W, H);
        f.blit(i, 0, 0);
        return f;
      });
    } else if (a.script === "soul-wisp") {
      frames = soulWisp(await load("lpc:source-of-tales/tiles/fire.png", id));
    } else {
      throw new Error(`effects: ${id} has no frame source`);
    }
    if (a.scale && a.scale !== 1) frames = frames.map((f) => f.scaleNearest(a.scale!));
    const fw = frames[0].w, fh = frames[0].h;
    let ax = fw / 2, ay = a.anchor === "bottom" ? fh : fh / 2;
    // Trim to the union of opaque bounds.
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const f of frames) {
      const bb = f.bbox();
      if (!bb) continue;
      x0 = Math.min(x0, bb.x); y0 = Math.min(y0, bb.y); x1 = Math.max(x1, bb.x + bb.w); y1 = Math.max(y1, bb.y + bb.h);
    }
    if (!isFinite(x0)) throw new Error(`effects: ${id} is empty`);
    if (a.anchor === "bottom") ay = y1;
    frames = frames.map((f) => f.crop(x0, y0, x1 - x0, y1 - y0));
    ax -= x0;
    ay -= y0;
    pending[id] = { handles: frames.map((f) => builder.add(f)), anchorX: Math.round(ax), anchorY: Math.round(ay), fps: a.fps };
  }
  writeCatalogue(def.anims, pending);
  log(`  packing ${builder.uniqueCount} unique effect images`);
  const packed = builder.pack();
  const anims: Record<string, EffectAnim> = {};
  for (const [id, p] of Object.entries(pending)) {
    anims[id] = { frames: p.handles.map((h) => packed.rectOf(h)), anchorX: p.anchorX, anchorY: p.anchorY, fps: p.fps };
  }
  return { atlas: { pages: packed.pages.map((_, i) => `effects-${i}.png`), anims }, pages: packed.pages };
}

/** assets-src/effects-catalogue.md, generated so it never drifts from effects.json. */
function writeCatalogue(defs: Record<string, AnimDef>, pending: Record<string, { handles: number[]; anchorX: number; anchorY: number; fps: number }>): void {
  const lines = [
    "# Effect catalogue",
    "",
    "Generated by `npm run bake` from `assets-src/effects.json`; edit that file, not this one.",
    "",
    "Animation ids in `public/assets/effects.json` (reference them as `fx:<id>` in content/effects.yaml).",
    "Anchors are the centre of the effect unless noted as bottom (ground-based effects, whose anchor is where they touch the ground).",
    "Slash arcs and particle-* textures are HD glowing sprites: draw them additively. The other effects are pixel art.",
    "",
    "| id | frames | fps | anchor | looks like |",
    "|---|---|---|---|---|",
  ];
  for (const [id, a] of Object.entries(defs)) {
    lines.push(`| \`${id}\` | ${pending[id].handles.length} | ${a.fps} | ${a.anchor === "bottom" ? "bottom" : "centre"} | ${a.desc} |`);
  }
  writeFileSync(join(ASSETS_SRC, "effects-catalogue.md"), `${lines.join("\n")}\n`);
}
