import type { WorldAtlas } from "../../../src/assets/types.ts";
import type { Img } from "./image.ts";
export async function bakeWorld(_use: (ref: string, usedFor: string) => void, _log: (s: string) => void): Promise<{ atlas: WorldAtlas; pages: Img[] }> {
  throw new Error("todo");
}
