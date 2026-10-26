/** Font outlines and icon SVGs become white alpha glyphs for runtime tinting. */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import opentype from "opentype.js";
import sharp from "sharp";
import { Img } from "./image.ts";
import { AtlasBuilder } from "./pack.ts";
import { ASSETS_SRC, resolveSource } from "./paths.ts";
import type { FontAtlas, FontGlyph, IconAtlas } from "./types.ts";

interface UiDef {
  fonts: Record<string, { src: string; size: number }>;
  icons: Record<string, string>;
}
const load = (): UiDef => JSON.parse(readFileSync(join(ASSETS_SRC, "ui.json"), "utf8")) as UiDef;
async function raster(svg: string, size?: number): Promise<Img> {
  let image = sharp(Buffer.from(svg));
  if (size) image = image.resize(size, size);
  const { data, info } = await image.ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  return new Img(info.width, info.height, data);
}
export async function bakeFonts(use: (ref: string, usedFor: string) => void): Promise<{ atlas: FontAtlas; pages: Img[] }> {
  const builder = new AtlasBuilder({ pageW: 1024, pageH: 1024, maxPages: 1, padding: 2, extrude: 1 });
  const fonts: FontAtlas["fonts"] = {};
  const handles: Array<{ glyph: FontGlyph; handle: number }> = [];
  for (const [id, def] of Object.entries(load().fonts)) {
    use(def.src, `font:${id}`);
    const bytes = readFileSync(resolveSource(def.src));
    const font = opentype.parse(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength));
    const scale = def.size / font.unitsPerEm;
    const glyphs: Record<string, FontGlyph> = {};
    for (let code = 32; code <= 126; code++) {
      const ch = String.fromCharCode(code);
      if (!font.hasChar(ch)) throw new Error(`font ${id} is missing ASCII ${code}`);
      const glyph = font.charToGlyph(ch);
      const path = glyph.getPath(0, 0, def.size);
      const bounds = path.getBoundingBox();
      const x = Math.floor(bounds.x1), y = Math.floor(bounds.y1);
      const w = Math.max(1, Math.ceil(bounds.x2) - x), h = Math.max(1, Math.ceil(bounds.y2) - y);
      // Outline paths avoid machine-installed fonts and Fontconfig fallbacks.
      const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="${x} ${y} ${w} ${h}"><path fill="white" d="${path.toPathData(3)}"/></svg>`;
      const entry: FontGlyph = { rect: { page: 0, x: 0, y: 0, w, h }, advance: (glyph.advanceWidth ?? 0) * scale, bearingX: x, bearingY: y };
      glyphs[ch] = entry;
      handles.push({ glyph: entry, handle: builder.add(await raster(svg)) });
    }
    fonts[id] = { size: def.size, ascent: Math.ceil(font.ascender * scale), lineHeight: Math.ceil((font.ascender - font.descender) * scale), glyphs };
  }
  const packed = builder.pack();
  for (const { glyph, handle } of handles) glyph.rect = packed.rectOf(handle);
  return { atlas: { pages: ["fonts-0.png"], fonts }, pages: packed.pages };
}
export async function bakeIcons(use: (ref: string, usedFor: string) => void): Promise<{ atlas: IconAtlas; pages: Img[] }> {
  const builder = new AtlasBuilder({ pageW: 1024, pageH: 1024, maxPages: 1, padding: 2, extrude: 1 });
  const handles: Record<string, number> = {};
  for (const [id, ref] of Object.entries(load().icons)) {
    use(ref, `icon:${id}`);
    const svg = readFileSync(resolveSource(ref), "utf8");
    const background = '<path d="M0 0h512v512H0z"/>';
    if (!svg.includes(background)) throw new Error(`icon ${id}: unreviewed SVG background`);
    handles[id] = builder.add(await raster(svg.replace(background, ""), 64));
  }
  const packed = builder.pack();
  return { atlas: { pages: ["icons-0.png"], icons: Object.fromEntries(Object.entries(handles).map(([id, h]) => [id, packed.rectOf(h)])) }, pages: packed.pages };
}
