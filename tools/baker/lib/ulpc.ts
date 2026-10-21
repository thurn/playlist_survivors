/**
 * The parts of the Universal LPC generator the baker reimplements:
 * sheet definitions, layer paths per body type and animation, zPos ordering,
 * palette recolors, and custom (oversized 128/192 px) weapon animations with
 * the 64 px body centred inside the larger frame.
 */
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { Img, loadImage, recolor } from "./image.ts";
import { ULPC } from "./paths.ts";

export const DIRS = ["up", "left", "down", "right"] as const;
const DIR_LETTER: Record<string, number> = { n: 0, w: 1, s: 2, e: 3 };

// ------------------------------------------------------------ palettes

interface MaterialMeta {
  default: string;
  base: string;
  palettes: Record<string, Record<string, string[]>>;
}

let materials: Record<string, MaterialMeta> | null = null;
export function paletteMaterials(): Record<string, MaterialMeta> {
  if (materials) return materials;
  const root = join(ULPC, "palette_definitions");
  const out: Record<string, MaterialMeta> = {};
  const walk = (dir: string) => {
    for (const name of readdirSync(dir).sort()) {
      const p = join(dir, name);
      if (statSync(p).isDirectory()) {
        walk(p);
        continue;
      }
      if (!name.endsWith(".json")) continue;
      const json = JSON.parse(readFileSync(p, "utf8"));
      if (name.startsWith("meta_")) {
        if (json.type !== "material") continue;
        const mat = name.slice(5, -5);
        out[mat] = { ...(out[mat] ?? { palettes: {} }), default: json.default, base: json.base };
      } else {
        const [mat, version] = name.slice(0, -5).split("_");
        out[mat] = out[mat] ?? ({ palettes: {} } as unknown as MaterialMeta);
        out[mat].palettes = out[mat].palettes ?? {};
        out[mat].palettes[version] = json;
      }
    }
  };
  walk(root);
  materials = out;
  return out;
}

/** Resolve a recolor key (`color`, `version.color`, `material.color` or `material.version.color`) to colours. */
export function paletteColors(defaultMaterial: string, key: string): string[] {
  const mats = paletteMaterials();
  const parts = key.split(".");
  let material = defaultMaterial;
  let version: string | undefined;
  let color: string;
  if (parts.length === 3) [material, version, color] = parts;
  else if (parts.length === 2) {
    if (mats[parts[0]]) [material, color] = parts;
    else [version, color] = parts;
  } else color = parts[0];
  const meta = mats[material];
  if (!meta) throw new Error(`unknown palette material ${material} in ${key}`);
  version = version ?? meta.default;
  const cols = meta.palettes[version]?.[color];
  if (!cols) throw new Error(`unknown palette colour ${material}.${version}.${color}`);
  return cols;
}

// ------------------------------------------------------------ definitions

export interface LayerDef {
  zPos: number;
  custom_animation?: string;
  [bodyType: string]: unknown;
}

export interface RecolorDef {
  material: string;
  base: string;
  type_name?: string;
}

export interface SheetDef {
  id: string;
  name?: string;
  layers: LayerDef[];
  variants: string[];
  animations: string[];
  recolors: RecolorDef[];
  matchBodyColor: boolean;
  typeName?: string;
}

const DEFAULT_ANIMS = ["spellcast", "thrust", "walk", "slash", "shoot", "hurt", "watering"];
const defCache = new Map<string, SheetDef>();

export function loadSheetDef(id: string): SheetDef {
  const hit = defCache.get(id);
  if (hit) return hit;
  const path = join(ULPC, "sheet_definitions", `${id}.json`);
  if (!existsSync(path)) throw new Error(`unknown generator item ${id} (no ${path})`);
  const raw = JSON.parse(readFileSync(path, "utf8")) as Record<string, unknown>;
  const layers: LayerDef[] = [];
  for (let i = 1; i < 10; i++) {
    const l = raw[`layer_${i}`] as LayerDef | undefined;
    if (!l) break;
    layers.push({ ...l, zPos: (l.zPos as number | undefined) ?? 100 });
  }
  const recolors: RecolorDef[] = [];
  const rc = raw.recolors as Record<string, unknown> | undefined;
  if (rc) {
    const entries: Array<Record<string, unknown>> = [];
    for (let n = 1; n < 10; n++) {
      const c = rc[`color_${n}`] as Record<string, unknown> | undefined;
      if (!c) break;
      entries.push(c);
    }
    if (entries.length === 0) entries.push(rc);
    const mats = paletteMaterials();
    for (const e of entries) {
      const material = e.material as string;
      const meta = mats[material];
      if (!meta) throw new Error(`item ${id}: unknown material ${material}`);
      let base = (e.base as string | undefined) ?? `${meta.default}.${meta.base}`;
      if (!base.includes(".")) base = `${meta.default}.${base}`;
      recolors.push({ material, base, type_name: e.type_name as string | undefined });
    }
  }
  const def: SheetDef = {
    id,
    name: raw.name as string | undefined,
    layers,
    variants: (raw.variants as string[] | undefined) ?? [],
    animations: (raw.animations as string[] | undefined) ?? DEFAULT_ANIMS,
    recolors,
    matchBodyColor: Boolean(raw.match_body_color),
    typeName: raw.type_name as string | undefined,
  };
  defCache.set(id, def);
  return def;
}

/** Whether an item's definition lists the given animation folder. */
function itemListsFolder(def: SheetDef, folder: string): boolean {
  const a = def.animations;
  switch (folder) {
    case "backslash":
      return a.includes("1h_backslash") || a.includes("1h_slash");
    case "halfslash":
      return a.includes("1h_halfslash");
    case "combat_idle":
      return a.includes("combat");
    case "thrust":
      return a.includes("thrust") || a.includes("watering");
    default:
      return a.includes(folder);
  }
}

// ------------------------------------------------------------ custom animations

export interface CustomAnim {
  frameSize: number;
  /** frames[dirRow][col] = [baseAnim, dirRow, baseCol] */
  frames: Array<Array<[string, number, number]>>;
}

let customCache: Record<string, CustomAnim> | null = null;
/** Parse the generator's custom animation table from its source (the source of truth). */
export function customAnimations(): Record<string, CustomAnim> {
  if (customCache) return customCache;
  const text = readFileSync(join(ULPC, "sources/custom-animations.ts"), "utf8");
  const start = text.indexOf("const customAnimations");
  const body = text.slice(start);
  const out: Record<string, CustomAnim> = {};
  const re = /\n {2}(\w+): \{\n([\s\S]*?)\n {2}\},/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(body))) {
    const fs = /frameSize: (\d+)/.exec(m[2]);
    if (!fs) continue;
    const rows = [...m[2].matchAll(/\[\s*((?:"[^"]+",?\s*)+)\]/g)].map((r) =>
      [...r[1].matchAll(/"([a-z_]+)-([nwse]),(\d+)"/g)].map((f) => [f[1], DIR_LETTER[f[2]], Number(f[3])] as [string, number, number]),
    );
    out[m[1]] = { frameSize: Number(fs[1]), frames: rows };
  }
  if (!out.slash_oversize || !out.slash_128) throw new Error("failed to parse generator custom animations");
  customCache = out;
  return out;
}

// ------------------------------------------------------------ recipes

export type ColorSpec = string | { custom: string[] };

export interface RecipeLayer {
  item: string;
  /** Recolor target for the item's first recolor channel. */
  color?: ColorSpec;
  /** Recolor targets for every channel, in order (overrides `color`). */
  colors?: ColorSpec[];
  /** Variant file for items without recolors. */
  variant?: string;
  /** Only draw this layer in these output animations / never in these. */
  only?: string[];
  except?: string[];
  /** Output animation -> "folder:col" to borrow when the item lacks that animation. */
  fallback?: Record<string, string>;
}

export interface RecipeAnim {
  /** Body animation folder the frames come from. */
  base?: string;
  /** Custom (oversized) animations whose layers may be drawn. */
  custom?: string[];
  /** Columns of the base sheet to use, in order (default: all). */
  cols?: number[];
  impactFrame?: number;
  fps?: number;
  /** Generated by a script instead of composited. */
  script?: string;
}

export interface Recipe {
  id: string;
  bodyType: string;
  layers: RecipeLayer[];
  animations: Record<string, RecipeAnim>;
  /** Optional per-recipe notes, ignored by the baker. */
  $comment?: string;
}

/** Defaults for well-known output animation names. */
export const ANIM_DEFAULTS: Record<string, RecipeAnim> = {
  idle: { base: "idle", fps: 3 },
  walk: { base: "walk", cols: [1, 2, 3, 4, 5, 6, 7, 8], fps: 10 },
  run: { base: "run", fps: 12 },
  jump: { base: "jump", fps: 10 },
  slash: { base: "slash", custom: ["slash_128"], fps: 14 },
  backslash: { base: "backslash", custom: ["backslash_128"], fps: 16 },
  halfslash: { base: "halfslash", custom: ["halfslash_128"], fps: 14 },
  thrust: { base: "thrust", custom: ["thrust_oversize", "thrust_128"], fps: 14 },
  spellcast: { base: "spellcast", fps: 12 },
  shoot: { base: "shoot", fps: 14 },
  hurt: { base: "hurt", fps: 10 },
  "slash-oversize": { base: "slash", custom: ["slash_oversize"], fps: 12 },
};

export interface ComposedAnim {
  frameSize: number;
  /** frames[dir] list of frames (all same size). */
  frames: Img[][];
  impactFrame?: number;
  fps?: number;
}

export interface ComposeContext {
  /** Record a used generator file (path relative to spritesheets/). */
  use(path: string): void;
}

function frameCount(img: Img, size: number): number {
  return Math.floor(img.w / size);
}

interface ResolvedLayer {
  def: SheetDef;
  layer: LayerDef;
  layerIndex: number;
  order: number;
  recipe: RecipeLayer;
  pairs: Array<{ from: string[]; to: string[] }>;
}

function resolveColors(recipe: Recipe, rl: RecipeLayer, def: SheetDef): Array<{ from: string[]; to: string[] }> {
  if (def.recolors.length === 0) return [];
  let specs: Array<ColorSpec | undefined> = rl.colors ?? [rl.color];
  if (specs[0] === undefined && def.matchBodyColor) {
    const body = recipe.layers.find((l) => loadSheetDef(l.item).matchBodyColor && l !== rl && (l.color || l.colors));
    if (body) specs = body.colors ?? [body.color];
  }
  const pairs: Array<{ from: string[]; to: string[] }> = [];
  def.recolors.forEach((rc, i) => {
    const spec = specs[i];
    if (spec === undefined) return; // keep the source colours
    const from = paletteColors(rc.material, rc.base);
    const to = typeof spec === "string" ? paletteColors(rc.material, spec) : spec.custom;
    if (to.length !== from.length) throw new Error(`${recipe.id}/${def.id}: custom palette needs ${from.length} colours`);
    pairs.push({ from, to });
  });
  return pairs;
}

async function loadLayerImage(ctx: ComposeContext, rel: string, pairs: Array<{ from: string[]; to: string[] }>, cache: Map<string, Img>): Promise<Img | null> {
  const key = `${rel}|${JSON.stringify(pairs)}`;
  const hit = cache.get(key);
  if (hit) return hit;
  const abs = join(ULPC, "spritesheets", rel);
  if (!existsSync(abs)) return null;
  ctx.use(rel);
  let img = await loadImage(abs);
  if (pairs.length) img = recolor(img, pairs);
  cache.set(key, img);
  return img;
}

function variantFile(rl: RecipeLayer, def: SheetDef): string {
  const v = rl.variant ?? def.variants[0];
  if (!v) throw new Error(`item ${def.id} needs a variant`);
  if (def.variants.length && !def.variants.includes(v)) throw new Error(`item ${def.id} has no variant ${v} (have ${def.variants.join(", ")})`);
  return v.replaceAll(" ", "_");
}

/** Standard per-animation sheet path for a layer, or null when the item lacks it. */
function standardPath(rl: RecipeLayer, def: SheetDef, layer: LayerDef, bodyType: string, folder: string): string | null {
  if (layer.custom_animation) return null;
  const base = layer[bodyType] as string | undefined;
  if (!base) return null;
  if (!itemListsFolder(def, folder)) return null;
  return def.recolors.length ? `${base}${folder}.png` : `${base}${folder}/${variantFile(rl, def)}.png`;
}

/**
 * Composite one output animation of a recipe. Returns null when the body
 * lacks the base animation.
 */
export async function composeAnimation(recipe: Recipe, name: string, spec: RecipeAnim, ctx: ComposeContext, cache: Map<string, Img>): Promise<ComposedAnim> {
  const base = spec.base!;
  const customs = customAnimations();
  const allowedCustom = (spec.custom ?? []).filter((c) => customs[c]);
  const layers: ResolvedLayer[] = [];
  let order = 0;
  for (const rl of recipe.layers) {
    if (rl.only && !rl.only.includes(name)) continue;
    if (rl.except && rl.except.includes(name)) continue;
    const def = loadSheetDef(rl.item);
    const pairs = resolveColors(recipe, rl, def);
    def.layers.forEach((layer, li) => layers.push({ def, layer, layerIndex: li, order: order++, recipe: rl, pairs }));
  }
  layers.sort((a, b) => a.layer.zPos - b.layer.zPos || a.order - b.order);

  // Load every sheet first so the output frame size is known.
  type Source = { img: Img; size: number; custom?: CustomAnim; col?: number; rows: number };
  const sources: Array<Source | null> = [];
  let frameSize = 64;
  let baseCols = 0;
  for (const L of layers) {
    let src: Source | null = null;
    const bodyPath = L.layer[recipe.bodyType] as string | undefined;
    if (!bodyPath) {
      sources.push(null);
      continue;
    }
    if (L.layer.custom_animation) {
      const ca = L.layer.custom_animation;
      if (allowedCustom.includes(ca) && customs[ca].frames[0][0][0] === base) {
        const rel = L.def.recolors.length ? `${bodyPath.replace(/\/$/, "")}.png` : `${bodyPath}${variantFile(L.recipe, L.def)}.png`;
        const img = await loadLayerImage(ctx, rel, L.pairs, cache);
        if (!img) throw new Error(`${recipe.id}: missing custom sheet ${rel}`);
        src = { img, size: customs[ca].frameSize, custom: customs[ca], rows: 4 };
        frameSize = Math.max(frameSize, customs[ca].frameSize);
      }
    } else {
      const rel = standardPath(L.recipe, L.def, L.layer, recipe.bodyType, base);
      const img = rel ? await loadLayerImage(ctx, rel, L.pairs, cache) : null;
      if (img) {
        src = { img, size: 64, rows: Math.max(1, Math.floor(img.h / 64)) };
        if (L.def.typeName === "body" || L.recipe === recipe.layers[0]) baseCols = Math.max(baseCols, frameCount(img, 64));
      }
    }
    const fb = L.recipe.fallback?.[name];
    if (!src && fb && true) {
      const [folder, colStr] = fb.split(":");
      const fbCustom = customs[folder];
      if (fbCustom && L.layer.custom_animation === folder) {
        // Borrow one column of a custom (oversized) sheet, e.g. "walk_128:0".
        const frel = `${bodyPath}${variantFile(L.recipe, L.def)}.png`;
        const fimg = await loadLayerImage(ctx, frel, L.pairs, cache);
        if (!fimg) throw new Error(`${recipe.id}: fallback ${fb} missing for ${L.def.id}`);
        src = { img: fimg, size: fbCustom.frameSize, col: Number(colStr), rows: 4 };
        frameSize = Math.max(frameSize, fbCustom.frameSize);
      } else if (!fbCustom && !L.layer.custom_animation) {
        const frel = standardPath(L.recipe, L.def, L.layer, recipe.bodyType, folder);
        const fimg = frel ? await loadLayerImage(ctx, frel, L.pairs, cache) : null;
        if (fimg) src = { img: fimg, size: 64, col: Number(colStr), rows: Math.max(1, Math.floor(fimg.h / 64)) };
      }
    }
    sources.push(src);
  }
  if (baseCols === 0) throw new Error(`${recipe.id}: body has no ${base} animation`);
  const cols = spec.cols ?? Array.from({ length: baseCols }, (_, i) => i);
  const frames: Img[][] = [];
  for (let d = 0; d < 4; d++) {
    const list: Img[] = [];
    for (const col of cols) {
      const out = new Img(frameSize, frameSize);
      layers.forEach((_, li) => {
        const s = sources[li];
        if (!s) return;
        const row = s.rows === 1 ? 0 : d;
        let sx: number;
        if (s.custom) {
          const idx = s.custom.frames[d].findIndex((f) => f[2] === col);
          if (idx < 0) return;
          sx = idx * s.size;
        } else {
          sx = (s.col ?? col) * s.size;
          if (sx + s.size > s.img.w) return;
        }
        const piece = s.img.crop(sx, row * s.size, s.size, s.size);
        const off = (frameSize - s.size) / 2;
        out.draw(piece, off, off);
      });
      list.push(out);
    }
    frames.push(list);
  }
  return { frameSize, frames, impactFrame: spec.impactFrame, fps: spec.fps };
}
