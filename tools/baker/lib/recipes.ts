/** Load character recipes from assets-src/characters, resolving `extends`. */
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { ASSETS_SRC } from "./paths.ts";
import type { Recipe, RecipeLayer } from "./ulpc.ts";

interface RawRecipe extends Partial<Recipe> {
  id: string;
  extends?: string;
  /** Per-item layer overrides applied on top of the parent's layers. */
  override?: Record<string, Partial<RecipeLayer>>;
}

export function loadRecipes(): Recipe[] {
  const dir = join(ASSETS_SRC, "characters");
  const raw = new Map<string, RawRecipe>();
  for (const f of readdirSync(dir).filter((f) => f.endsWith(".json")).sort()) {
    const r = JSON.parse(readFileSync(join(dir, f), "utf8")) as RawRecipe;
    if (r.id !== f.replace(/\.json$/, "")) throw new Error(`recipe ${f} must have id ${f.replace(/\.json$/, "")}`);
    raw.set(r.id, r);
  }
  const resolved = new Map<string, Recipe>();
  const resolve = (id: string, seen: string[] = []): Recipe => {
    const hit = resolved.get(id);
    if (hit) return hit;
    if (seen.includes(id)) throw new Error(`recipe extends cycle: ${[...seen, id].join(" -> ")}`);
    const r = raw.get(id);
    if (!r) throw new Error(`unknown recipe ${id}`);
    let out: Recipe;
    if (r.extends) {
      const parent = resolve(r.extends, [...seen, id]);
      const layers = (r.layers ?? parent.layers).map((l) => ({ ...l, ...(r.override?.[l.item] ?? {}) }));
      for (const item of Object.keys(r.override ?? {})) {
        if (!layers.some((l) => l.item === item)) throw new Error(`recipe ${id}: override for ${item}, which the parent does not use`);
      }
      out = { id, bodyType: r.bodyType ?? parent.bodyType, layers, animations: r.animations ?? parent.animations };
    } else {
      if (!r.bodyType || !r.layers || !r.animations) throw new Error(`recipe ${id} needs bodyType, layers and animations`);
      out = { id, bodyType: r.bodyType, layers: r.layers, animations: r.animations };
    }
    resolved.set(id, out);
    return out;
  };
  return [...raw.keys()].map((id) => resolve(id));
}
