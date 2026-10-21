import type { EffectRecipe } from "../content/schema.ts";

/** How long an effect recipe stays visible, per clock. */
export function recipeLife(r: EffectRecipe): { world: number; song: number } {
  let world = 0;
  let song = 0;
  for (const l of r.layers) {
    const life = "life" in l && typeof l.life === "number" ? l.life : l.kind === "shake" ? 1.2 : 0.5;
    const clock = "clock" in l ? l.clock : "song";
    if (clock === "world") world = Math.max(world, life);
    else song = Math.max(song, life);
  }
  return { world, song };
}
