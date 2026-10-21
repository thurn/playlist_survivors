/**
 * Character baking: ULPC recipes and source-of-tales monsters into
 * public/assets/characters.json plus atlas pages, with a white-silhouette
 * hit-flash copy of every frame.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { CharacterAtlas, CharacterSprite, Facing, SpriteAnimation } from "../../../src/assets/types.ts";
import { Img } from "./image.ts";
import { buildKick } from "./kick.ts";
import { bakeMonster, type MonsterDef } from "./monsters.ts";
import { AtlasBuilder } from "./pack.ts";
import { ASSETS_SRC } from "./paths.ts";
import { loadRecipes } from "./recipes.ts";
import { ANIM_DEFAULTS, composeAnimation, type ComposedAnim, type Recipe } from "./ulpc.ts";

const FACINGS: Facing[] = ["up", "left", "down", "right"];

interface PendingAnim {
  frameW: number;
  frameH: number;
  anchorX: number;
  anchorY: number;
  frames: Img[][];
  impactFrame?: number;
  fps?: number;
}

/** Crop every frame of an animation to the union of their opaque bounds (keeps the anchor meaningful). */
function cropToUnion(a: PendingAnim, margin = 1): PendingAnim {
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (const dir of a.frames) for (const f of dir) {
    const bb = f.bbox();
    if (!bb) continue;
    x0 = Math.min(x0, bb.x); y0 = Math.min(y0, bb.y);
    x1 = Math.max(x1, bb.x + bb.w); y1 = Math.max(y1, bb.y + bb.h);
  }
  if (!isFinite(x0)) return a;
  x0 = Math.max(0, x0 - margin); y0 = Math.max(0, y0 - margin);
  x1 = Math.min(a.frameW, x1 + margin); y1 = Math.min(a.frameH, y1 + margin);
  return {
    ...a,
    frameW: x1 - x0,
    frameH: y1 - y0,
    anchorX: a.anchorX - x0,
    anchorY: a.anchorY - y0,
    frames: a.frames.map((d) => d.map((f) => f.crop(x0, y0, x1 - x0, y1 - y0))),
  };
}

/** Feet row of a 64 px LPC frame: just below the lowest opaque pixel of the body. */
function footRow(frame: Img): number {
  const bb = frame.bbox();
  return bb ? bb.y + bb.h : 62;
}

async function bakeRecipe(recipe: Recipe, use: (ref: string) => void): Promise<Record<string, PendingAnim>> {
  const cache = new Map<string, Img>();
  const ctx = { use: (p: string) => use(`ulpc:${p}`) };
  // Measure the feet on the plain standing frame (walk column 0, facing down).
  const stand = await composeAnimation(recipe, "__stand", { base: "walk", cols: [0] }, { use: () => {} }, cache);
  const standing = stand.frames.map((d) => d[0]);
  const foot = footRow(stand.frames[2][0].crop((stand.frameSize - 64) / 2, (stand.frameSize - 64) / 2, 64, 64));
  const out: Record<string, PendingAnim> = {};
  for (const [name, a] of Object.entries(recipe.animations)) {
    const spec = { ...(ANIM_DEFAULTS[name] ?? {}), ...a };
    let composed: ComposedAnim;
    if (spec.script === "kick" || spec.script === "kick-alt") {
      const s64 = standing.map((f) => f.crop((f.w - 64) / 2, (f.h - 64) / 2, 64, 64));
      const k = buildKick(s64);
      composed = { frameSize: 64, frames: spec.script === "kick" ? k.kick : k.kickAlt, impactFrame: spec.impactFrame, fps: spec.fps };
      // The kick is derived from the walk sheets already recorded for this recipe.
      await composeAnimation(recipe, name, { base: "walk", cols: [0] }, ctx, cache);
    } else if (spec.script) {
      throw new Error(`${recipe.id}: unknown animation script ${spec.script}`);
    } else {
      if (!spec.base) throw new Error(`${recipe.id}: animation ${name} needs a base`);
      composed = await composeAnimation(recipe, name, spec, ctx, cache);
    }
    const off = (composed.frameSize - 64) / 2;
    let pending: PendingAnim = {
      frameW: composed.frameSize,
      frameH: composed.frameSize,
      anchorX: off + 32,
      anchorY: off + foot,
      frames: composed.frames,
      impactFrame: composed.impactFrame,
      fps: composed.fps,
    };
    if (composed.frameSize > 64) pending = cropToUnion(pending);
    for (const d of pending.frames) if (d.every((f) => f.isEmpty())) throw new Error(`${recipe.id}/${name}: empty facing`);
    if (pending.impactFrame !== undefined && pending.impactFrame >= pending.frames[0].length) {
      throw new Error(`${recipe.id}/${name}: impactFrame ${pending.impactFrame} out of range`);
    }
    out[name] = pending;
  }
  return out;
}

export interface CharacterBakeResult {
  atlas: CharacterAtlas;
  pages: Img[];
}

export async function bakeCharacters(use: (ref: string, usedFor: string) => void, log: (s: string) => void): Promise<CharacterBakeResult> {
  const builder = new AtlasBuilder({ pageW: 4096, pageH: 4096, maxPages: 4, padding: 2, extrude: 1 });
  const sprites: Record<string, Record<string, PendingAnim>> = {};
  for (const recipe of loadRecipes()) {
    log(`  character ${recipe.id}`);
    sprites[recipe.id] = await bakeRecipe(recipe, (ref) => use(ref, recipe.id));
  }
  const monsters = JSON.parse(readFileSync(join(ASSETS_SRC, "monsters.json"), "utf8")) as { monsters: Record<string, MonsterDef & { impactFrame?: number }> };
  for (const [id, def] of Object.entries(monsters.monsters)) {
    log(`  monster ${id}`);
    const anims = await bakeMonster(id, def, (ref) => use(ref, id));
    sprites[id] = {};
    for (const [name, a] of Object.entries(anims)) {
      sprites[id][name] = { ...a, ...(name === "attack" ? { impactFrame: 2 } : {}) };
    }
  }

  // Register every frame and its flash copy with the packer, in a stable order.
  const handles: Record<string, Record<string, Array<Array<{ f: number; fl: number }>>>> = {};
  for (const id of Object.keys(sprites).sort()) {
    handles[id] = {};
    for (const name of Object.keys(sprites[id]).sort()) {
      handles[id][name] = sprites[id][name].frames.map((d) => d.map((f) => ({ f: builder.add(f), fl: builder.add(f.whiteSilhouette()) })));
    }
  }
  log(`  packing ${builder.uniqueCount} unique character images`);
  const packed = builder.pack();
  const atlas: CharacterAtlas = { pages: packed.pages.map((_, i) => `characters-${i}.png`), sprites: {} };
  for (const id of Object.keys(sprites).sort()) {
    const sprite: CharacterSprite = { id, animations: {} };
    for (const name of Object.keys(sprites[id]).sort()) {
      const a = sprites[id][name];
      const frames = {} as SpriteAnimation["frames"];
      FACINGS.forEach((facing, d) => {
        frames[facing] = handles[id][name][d].map((h) => ({ ...packed.rectOf(h.f), flash: packed.rectOf(h.fl) }));
      });
      sprite.animations[name] = {
        frameW: a.frameW,
        frameH: a.frameH,
        anchorX: a.anchorX,
        anchorY: a.anchorY,
        frames,
        ...(a.impactFrame !== undefined ? { impactFrame: a.impactFrame } : {}),
        ...(a.fps !== undefined ? { fps: a.fps } : {}),
      };
    }
    atlas.sprites[id] = sprite;
  }
  return { atlas, pages: packed.pages };
}
