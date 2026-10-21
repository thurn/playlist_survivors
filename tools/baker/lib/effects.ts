import type { EffectsAtlas } from "../../../src/assets/types.ts";
import type { Img } from "./image.ts";
export async function bakeEffects(_use: (ref: string, usedFor: string) => void, _log: (s: string) => void): Promise<{ atlas: EffectsAtlas; pages: Img[] }> {
  throw new Error("todo");
}
