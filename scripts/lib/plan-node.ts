/** Run the planner in Node from the files on disk. */
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { loadContent, loadLevel } from "../../src/content/loader.ts";
import type { SongMap } from "../../src/core/songmap.ts";
import type { CharacterAtlas, SoundManifest, WorldAtlas } from "../../src/assets/types.ts";
import { plan, planWithSim } from "../../src/planner/plan.ts";
import type { PlannerInput, AnimInfo } from "../../src/planner/context.ts";
import type { PropInfo } from "../../src/mapgen/mapgen.ts";
import type { Plan } from "../../src/plan/types.ts";
import type { SkillProfile } from "../../src/sim/player.ts";
import type { SimResult } from "../../src/sim/simulate.ts";
import { ROOT, hasSongMap, readContentFiles, readSongMap, readSource } from "./files.ts";

export function readLandmarks(): Record<string, unknown> {
  const dir = join(ROOT, "content/landmarks");
  const out: Record<string, unknown> = {};
  for (const f of readdirSync(dir).filter((x) => x.endsWith(".tmj"))) out[f.replace(/\.tmj$/, "")] = JSON.parse(readFileSync(join(dir, f), "utf8"));
  return out;
}

export function heroAnimsFromAtlas(): Record<string, AnimInfo> | undefined {
  const p = join(ROOT, "public/assets/characters.json");
  if (!existsSync(p)) return undefined;
  const atlas = JSON.parse(readFileSync(p, "utf8")) as CharacterAtlas;
  const hero = atlas.sprites.hero;
  if (!hero) return undefined;
  return Object.fromEntries(Object.entries(hero.animations).map(([k, a]) => [k, { frames: a.frames.down.length, impactFrame: a.impactFrame }]));
}

export function propsFromAtlas(): Record<string, PropInfo> | undefined {
  const p = join(ROOT, "public/assets/world.json");
  if (!existsSync(p)) return undefined;
  const atlas = JSON.parse(readFileSync(p, "utf8")) as WorldAtlas;
  return Object.fromEntries(Object.entries(atlas.props).map(([k, v]) => [k, { tall: v.tall, radius: v.radius }]));
}

export function soundOnsets(): Record<string, number> | undefined {
  const p = join(ROOT, "public/assets/sfx/sounds.json");
  if (!existsSync(p)) return undefined;
  const m = JSON.parse(readFileSync(p, "utf8")) as SoundManifest;
  return Object.fromEntries(Object.entries(m.sounds).map(([k, v]) => [k, v.onsetS ?? 0]));
}

export function plannerInput(opts: { songMap?: SongMap; seed?: number; level?: string } = {}): PlannerInput {
  const content = loadContent(readContentFiles());
  const levelSrc = readSource(opts.level ?? "levels/orpheus-galop.yaml");
  const songName = /^song: (\S+)/m.exec(levelSrc.text)?.[1] ?? "";
  const songMap = opts.songMap ?? (hasSongMap(songName) ? readSongMap(songName) : undefined);
  if (!songMap) throw new Error(`no song map for ${songName}`);
  const level = loadLevel(levelSrc, content, songMap);
  return { songMap, level, content, seed: opts.seed ?? level.seed, heroAnims: heroAnimsFromAtlas(), landmarks: readLandmarks(), soundOnsets: soundOnsets(), version: "node" };
}

export function runPlanner(opts: { songMap?: SongMap; seed?: number; level?: string; profile?: SkillProfile } = {}): Plan {
  return plan(plannerInput(opts), { props: propsFromAtlas(), now: () => performance.now(), profile: opts.profile });
}

/** The plan and its simulation, from one run. */
export function runPlannerWithSim(opts: { songMap?: SongMap; seed?: number; level?: string; profile?: SkillProfile } = {}): { plan: Plan; sim: SimResult } {
  return planWithSim(plannerInput(opts), { props: propsFromAtlas(), now: () => performance.now(), profile: opts.profile });
}
