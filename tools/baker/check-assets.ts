/**
 * Validate the baked assets. Run with `npm run check:assets`.
 *
 * Checks: every used source has an allowed licence and complete credits; all
 * manifests parse and reference existing page files with in-bounds rects;
 * every required sprite, animation and facing exists; page count and size
 * limits; sound files; total sizes; and, when the content files exist,
 * that enemies, moves, zones, effect recipes and sound palettes only
 * reference baked assets. Exits non-zero listing every problem.
 */
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import sharp from "sharp";
import { parse as parseYaml } from "yaml";
import type { AtlasRect, CharacterAtlas, CreditsFile, EffectsAtlas, SoundManifest, WorldAtlas } from "../../src/assets/types.ts";
import { FACINGS } from "../../src/assets/types.ts";
import { isAllowedLicence, leastRestrictive, resolveLicence } from "./lib/licences.ts";
import { ASSETS_SRC, OUT, REPO, SFX_OUT } from "./lib/paths.ts";

const problems: string[] = [];
const fail = (msg: string) => problems.push(msg);

/** Sprites and animations the demo needs (see docs/design/tech-demo.md, milestone 5). */
const REQUIRED: Record<string, string[]> = {
  hero: ["idle", "walk", "run", "jump", "slash", "backslash", "halfslash", "thrust", "spellcast", "slash-oversize", "hurt"],
  goblin: ["idle", "walk", "slash", "hurt"],
  orc: ["idle", "walk", "slash", "hurt"],
  "orc-elite": ["idle", "walk", "slash", "hurt"],
  "orc-ember": ["idle", "walk", "slash", "hurt"],
  skeleton: ["idle", "walk", "slash", "hurt"],
  "skeleton-ember": ["idle", "walk", "slash", "hurt", "kick"],
  "skeleton-archer": ["idle", "walk", "slash", "hurt", "shoot"],
  minotaur: ["idle", "walk", "slash", "hurt", "slash-oversize"],
  slime: ["walk", "attack", "hurt"],
  bat: ["walk", "attack", "hurt"],
  bee: ["walk", "attack", "hurt"],
  eyeball: ["walk", "attack", "hurt"],
};
const IMPACT_REQUIRED = ["slash", "backslash", "halfslash", "thrust", "slash-oversize"];
const REQUIRED_TILES = ["grass-summer", "dirt-summer", "grass-autumn", "dirt-autumn", "grass-ember", "dirt-ember", "lava-rock", "lava"];
const REQUIRED_PROPS = [
  "tree-summer", "tree-autumn", "tree-dead", "bush-summer", "bush-autumn", "mushroom", "mushroom-big", "flower-summer", "flower-autumn",
  "grass-tuft", "rock-small", "rock-big", "brazier", "campfire", "standing-stone", "ruin-pillar", "ruin-wall", "lava-pool",
  "patch-dirt-summer", "patch-dirt-autumn", "patch-dirt-ember", "patch-lava",
];
const ANIMATED_PROPS = ["brazier", "campfire"];
const REQUIRED_EFFECTS = ["explosion-small", "explosion-big", "lightning", "fire", "sparkle", "soul-wisp"];
const REQUIRED_EFFECT_PREFIXES = ["slash-", "spell-", "gib-", "bone-"];

function readJson<T>(path: string): T | null {
  if (!existsSync(path)) {
    fail(`missing ${path}`);
    return null;
  }
  try {
    return JSON.parse(readFileSync(path, "utf8")) as T;
  } catch (e) {
    fail(`cannot parse ${path}: ${(e as Error).message}`);
    return null;
  }
}

async function pageSizes(kind: string, pages: string[], maxPages: number, maxSize: number): Promise<Array<{ w: number; h: number }>> {
  if (!Array.isArray(pages) || pages.length === 0) fail(`${kind}: no pages`);
  if (pages.length > maxPages) fail(`${kind}: ${pages.length} pages exceeds the limit of ${maxPages}`);
  const sizes: Array<{ w: number; h: number }> = [];
  for (const p of pages) {
    const path = join(OUT, p);
    if (!existsSync(path)) {
      fail(`${kind}: page file ${p} is missing`);
      sizes.push({ w: 0, h: 0 });
      continue;
    }
    const meta = await sharp(path).metadata();
    const w = meta.width ?? 0, h = meta.height ?? 0;
    if (w > maxSize || h > maxSize) fail(`${kind}: page ${p} is ${w}x${h}, over ${maxSize}x${maxSize}`);
    sizes.push({ w, h });
  }
  return sizes;
}

function checkRect(where: string, r: AtlasRect | undefined, sizes: Array<{ w: number; h: number }>): void {
  if (!r || typeof r.x !== "number") {
    fail(`${where}: missing rect`);
    return;
  }
  const s = sizes[r.page];
  if (!s) fail(`${where}: page ${r.page} does not exist`);
  else if (r.x < 0 || r.y < 0 || r.w <= 0 || r.h <= 0 || r.x + r.w > s.w || r.y + r.h > s.h) fail(`${where}: rect ${JSON.stringify(r)} is outside page ${r.page} (${s.w}x${s.h})`);
}

async function checkCharacters(): Promise<CharacterAtlas | null> {
  const atlas = readJson<CharacterAtlas>(join(OUT, "characters.json"));
  if (!atlas) return null;
  const sizes = await pageSizes("characters", atlas.pages, 4, 4096);
  for (const [id, anims] of Object.entries(REQUIRED)) {
    const sprite = atlas.sprites[id];
    if (!sprite) {
      fail(`characters: missing sprite ${id}`);
      continue;
    }
    for (const a of anims) if (!sprite.animations[a]) fail(`characters: ${id} is missing animation ${a}`);
    for (const a of IMPACT_REQUIRED) if (sprite.animations[a] && sprite.animations[a].impactFrame === undefined && id === "hero") fail(`characters: hero/${a} has no impactFrame`);
  }
  for (const [id, sprite] of Object.entries(atlas.sprites)) {
    if (sprite.id !== id) fail(`characters: sprite key ${id} has id ${sprite.id}`);
    for (const [name, a] of Object.entries(sprite.animations)) {
      const where = `characters: ${id}/${name}`;
      if (!(a.frameW > 0 && a.frameH > 0)) fail(`${where}: bad frame size`);
      if (a.anchorX < 0 || a.anchorY < 0 || a.anchorX > a.frameW || a.anchorY > a.frameH) fail(`${where}: anchor (${a.anchorX},${a.anchorY}) outside the ${a.frameW}x${a.frameH} frame`);
      const lengths = new Set<number>();
      for (const f of FACINGS) {
        const frames = a.frames?.[f];
        if (!frames || frames.length === 0) {
          fail(`${where}: no frames facing ${f}`);
          continue;
        }
        lengths.add(frames.length);
        frames.forEach((fr, i) => {
          checkRect(`${where}/${f}[${i}]`, fr, sizes);
          checkRect(`${where}/${f}[${i}].flash`, fr.flash, sizes);
          if (fr.w !== a.frameW || fr.h !== a.frameH) fail(`${where}/${f}[${i}]: rect ${fr.w}x${fr.h} differs from frame size ${a.frameW}x${a.frameH}`);
          if (fr.flash && (fr.flash.w !== fr.w || fr.flash.h !== fr.h)) fail(`${where}/${f}[${i}]: flash rect size differs`);
        });
      }
      if (lengths.size > 1) fail(`${where}: facings have different frame counts (${[...lengths].join(", ")})`);
      if (a.impactFrame !== undefined && (a.impactFrame < 0 || a.impactFrame >= Math.min(...lengths))) fail(`${where}: impactFrame ${a.impactFrame} out of range`);
    }
  }
  if (atlas.sprites["skeleton-ember"]?.animations.kick?.frames.down.length !== 3) fail("characters: skeleton-ember/kick must have 3 frames");
  return atlas;
}

async function checkWorld(): Promise<WorldAtlas | null> {
  const atlas = readJson<WorldAtlas>(join(OUT, "world.json"));
  if (!atlas) return null;
  const sizes = await pageSizes("world", atlas.pages, 2, 4096);
  for (const t of REQUIRED_TILES) if (!atlas.tiles[t]?.length) fail(`world: missing tile ${t}`);
  for (const [id, rects] of Object.entries(atlas.tiles)) {
    rects.forEach((r, i) => {
      checkRect(`world: tile ${id}[${i}]`, r, sizes);
      if (r.w !== 32 || r.h !== 32) fail(`world: tile ${id}[${i}] is ${r.w}x${r.h}, not 32x32`);
    });
  }
  for (const p of REQUIRED_PROPS) if (!atlas.props[p]?.variants.length) fail(`world: missing prop ${p}`);
  for (const p of ANIMATED_PROPS) if (atlas.props[p] && !(atlas.props[p].frames && atlas.props[p].frames!.every((f) => f.length > 1))) fail(`world: prop ${p} should be animated`);
  for (const [id, p] of Object.entries(atlas.props)) {
    if (typeof p.tall !== "boolean" || !(p.radius > 0)) fail(`world: prop ${id} needs tall and a positive radius`);
    p.variants.forEach((v, i) => {
      checkRect(`world: prop ${id}[${i}]`, v.rect, sizes);
      if (v.anchorX < 0 || v.anchorY < 0 || v.anchorX > v.rect.w || v.anchorY > v.rect.h) fail(`world: prop ${id}[${i}] anchor outside its rect`);
    });
    if (p.frames) {
      if (p.frames.length !== p.variants.length) fail(`world: prop ${id} has ${p.frames.length} frame lists for ${p.variants.length} variants`);
      p.frames.forEach((fl, i) => fl.forEach((r, k) => checkRect(`world: prop ${id}[${i}] frame ${k}`, r, sizes)));
      if (!(p.fps && p.fps > 0)) fail(`world: animated prop ${id} needs fps`);
    }
  }
  return atlas;
}

async function checkEffects(): Promise<EffectsAtlas | null> {
  const atlas = readJson<EffectsAtlas>(join(OUT, "effects.json"));
  if (!atlas) return null;
  const sizes = await pageSizes("effects", atlas.pages, 3, 4096);
  for (const e of REQUIRED_EFFECTS) if (!atlas.anims[e]) fail(`effects: missing ${e}`);
  for (const p of REQUIRED_EFFECT_PREFIXES) if (!Object.keys(atlas.anims).some((k) => k.startsWith(p))) fail(`effects: no ${p}* animations`);
  for (const [id, a] of Object.entries(atlas.anims)) {
    if (!a.frames.length) fail(`effects: ${id} has no frames`);
    if (!(a.fps > 0)) fail(`effects: ${id} has no fps`);
    a.frames.forEach((r, i) => {
      checkRect(`effects: ${id}[${i}]`, r, sizes);
      if (r.w !== a.frames[0].w || r.h !== a.frames[0].h) fail(`effects: ${id}[${i}] differs in size from frame 0`);
    });
  }
  return atlas;
}

function dirSize(dir: string): number {
  let total = 0;
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    total += e.isDirectory() ? dirSize(p) : statSync(p).size;
  }
  return total;
}

function checkSounds(): SoundManifest | null {
  const m = readJson<SoundManifest>(join(SFX_OUT, "sounds.json"));
  if (!m) return null;
  for (const [id, s] of Object.entries(m.sounds)) {
    if (!/^(kenney|rpg80|rpgpack|fantasy|sot)\//.test(id)) fail(`sounds: unexpected pack in ${id}`);
    if (s.file !== `${id}.ogg`) fail(`sounds: ${id} file should be ${id}.ogg`);
    if (!existsSync(join(SFX_OUT, s.file))) fail(`sounds: missing file ${s.file}`);
    if (!(s.durationS > 0)) fail(`sounds: ${id} has no duration`);
    if (!Number.isFinite(s.lufs) || s.lufs < -40 || s.lufs > -10) fail(`sounds: ${id} loudness ${s.lufs} LUFS looks wrong`);
  }
  for (const id of ["kenney/impactMetal_light_000", "kenney/impactMetal_heavy_000", "kenney/impactPunch_heavy_000", "kenney/impactPlate_light_000", "rpgpack/swing2", "kenney/pluck_001", "sot/bomb", "kenney/impactBell_heavy_000", "fantasy/Jingle_Achievement_00"]) {
    if (!m.sounds[id]) fail(`sounds: missing required sample ${id}`);
  }
  const sfxBytes = dirSize(SFX_OUT);
  if (sfxBytes > 15 * 1024 * 1024) fail(`sounds: ${(sfxBytes / 1048576).toFixed(1)} MB exceeds 15 MB`);
  return m;
}

function checkCredits(): void {
  const c = readJson<CreditsFile>(join(OUT, "credits.json"));
  if (!c) return;
  if (!c.recording?.title || !c.recording.url || !c.recording.licence) fail("credits: recording credit is incomplete");
  if (!c.entries?.length) fail("credits: no entries");
  for (const e of c.entries ?? []) {
    if (!e.authors?.length) fail(`credits: ${e.file} has no authors`);
    if (!e.urls?.length) fail(`credits: ${e.file} has no URLs`);
    if (!isAllowedLicence(e.licence)) fail(`credits: ${e.file} has disallowed licence ${e.licence}`);
    if (!e.usedFor?.length) fail(`credits: ${e.file} has no usedFor`);
  }
  // Re-resolve every non-generator source referenced by the bake inputs.
  const refs = new Set<string>();
  const collect = (v: unknown) => {
    if (typeof v === "string" && /^(lpc|dl):/.test(v)) refs.add(v);
    else if (Array.isArray(v)) v.forEach(collect);
    else if (v && typeof v === "object") Object.values(v).forEach(collect);
  };
  for (const f of ["world.json", "effects.json", "sounds.json", "monsters.json"]) {
    const p = join(ASSETS_SRC, f);
    if (existsSync(p)) collect(JSON.parse(readFileSync(p, "utf8")));
  }
  for (const ref of refs) {
    if (ref.endsWith("/") || /\/[^/.]+$/.test(ref)) continue; // directories (e.g. effect frame folders)
    try {
      const rec = resolveLicence(ref);
      if (!leastRestrictive(rec.licences)) fail(`licence: ${ref} has only disallowed licences (${rec.licences.join(", ")})`);
      if (!rec.authors.length || !rec.urls.length) fail(`licence: ${ref} lacks authors or URLs`);
    } catch (e) {
      fail(`licence: ${(e as Error).message}`);
    }
  }
}

function loadYaml(name: string): Record<string, unknown> | null {
  const p = join(REPO, "content", name);
  if (!existsSync(p)) return null;
  try {
    return parseYaml(readFileSync(p, "utf8")) as Record<string, unknown>;
  } catch (e) {
    fail(`content/${name}: cannot parse (${(e as Error).message})`);
    return null;
  }
}

function checkContent(chars: CharacterAtlas | null, world: WorldAtlas | null, fx: EffectsAtlas | null, sfx: SoundManifest | null): void {
  const enemies = loadYaml("enemies.yaml");
  if (enemies && chars) {
    for (const e of (enemies.enemies as Array<{ id: string; atlas?: string }>) ?? []) {
      if (e.atlas && !chars.sprites[e.atlas]) fail(`content/enemies.yaml: ${e.id} uses unknown sprite ${e.atlas}`);
    }
  }
  const moves = loadYaml("moves.yaml");
  if (moves && chars) {
    for (const m of (moves.moves as Array<{ id: string; animation?: { name?: string } }>) ?? []) {
      const n = m.animation?.name;
      if (n && !chars.sprites.hero?.animations[n]) fail(`content/moves.yaml: ${m.id} uses unknown hero animation ${n}`);
    }
  }
  const formations = loadYaml("formations.yaml");
  if (formations && chars) {
    const enemyAtlas = new Map(((enemies?.enemies as Array<{ id: string; atlas?: string }>) ?? []).map((e) => [e.id, e.atlas ?? e.id]));
    for (const f of (formations.formations as Array<{ id: string; enemy?: string; dance?: { animation?: string } }>) ?? []) {
      const sprite = f.enemy ? enemyAtlas.get(f.enemy) ?? f.enemy : undefined;
      if (sprite && f.dance?.animation && !chars.sprites[sprite]?.animations[f.dance.animation]) fail(`content/formations.yaml: ${f.id} dance ${f.dance.animation} is not baked for ${sprite}`);
    }
  }
  const zones = loadYaml("zones.yaml");
  if (zones && world) {
    for (const z of (zones.zones as Array<{ id: string; terrain?: Record<string, string>; props?: Array<{ sprite: string }> }>) ?? []) {
      for (const p of z.props ?? []) if (!world.props[p.sprite]) fail(`content/zones.yaml: ${z.id} uses unknown prop ${p.sprite}`);
      for (const [k, v] of Object.entries(z.terrain ?? {})) {
        if (typeof v === "string" && !world.tiles[v] && !world.props[v]) fail(`content/zones.yaml: ${z.id} terrain.${k} names unknown tile/prop ${v}`);
      }
    }
  }
  const effects = loadYaml("effects.yaml");
  if (effects && fx) {
    const walk = (v: unknown, where: string) => {
      if (typeof v === "string" && v.startsWith("fx:") && !fx.anims[v.slice(3)]) fail(`content/effects.yaml: ${where} uses unknown effect ${v}`);
      else if (Array.isArray(v)) v.forEach((x) => walk(x, where));
      else if (v && typeof v === "object") Object.values(v).forEach((x) => walk(x, where));
    };
    for (const e of (effects.effects as Array<{ id: string }>) ?? []) walk(e, e.id);
  }
  const sounds = loadYaml("sounds.yaml");
  if (sounds && sfx) {
    for (const p of (sounds.soundPalettes as Array<{ id: string; sounds?: Record<string, unknown> }>) ?? []) {
      for (const [event, list] of Object.entries(p.sounds ?? {})) {
        for (const s of Array.isArray(list) ? list : [list]) {
          const id = typeof s === "string" ? s : (s as { id?: string })?.id;
          if (id && !sfx.sounds[id.replace(/\.ogg$/, "")]) fail(`content/sounds.yaml: palette ${p.id} ${event} uses unknown sample ${id}`);
        }
      }
    }
  }
}

async function main(): Promise<void> {
  const chars = await checkCharacters();
  const world = await checkWorld();
  const fx = await checkEffects();
  const sfx = checkSounds();
  checkCredits();
  checkContent(chars, world, fx, sfx);
  const total = dirSize(OUT);
  if (total > 40 * 1024 * 1024) fail(`public/assets is ${(total / 1048576).toFixed(1)} MB, over 40 MB`);
  if (problems.length) {
    console.error(`asset check failed with ${problems.length} problem(s):`);
    for (const p of problems) console.error(`  - ${p}`);
    process.exit(1);
  }
  console.log(`asset check passed: ${Object.keys(chars!.sprites).length} sprites, ${Object.keys(world!.props).length} props, ${Object.keys(fx!.anims).length} effects, ${Object.keys(sfx!.sounds).length} sounds, ${(total / 1048576).toFixed(1)} MB`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
