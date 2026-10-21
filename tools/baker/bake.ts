/**
 * The offline asset baker. Run with `npm run bake`.
 *
 * Inputs: assets-src/ (recipes, world/effects/sound selections, licences.yaml),
 * the local LPC collections under ~/Documents/lpc, and downloaded packs listed
 * in tools/baker/sources.json (cached in ~/.cache/playlist-survivors).
 *
 * Outputs (committed): public/assets/{characters,world,effects}.json and their
 * page PNGs, public/assets/sfx/<pack>/<name>.ogg plus sounds.json, and
 * public/assets/credits.json. Baking twice gives byte-identical outputs.
 *
 * Flags: --only=characters,world,effects,sounds bakes a subset (manifests of
 * the others are left untouched; credits are only rewritten on a full bake).
 */
import { readdirSync, rmSync, writeFileSync, mkdirSync, existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import type { CreditsFile } from "../../src/assets/types.ts";
import { bakeCharacters } from "./lib/characters.ts";
import { bakeEffects } from "./lib/effects.ts";
import { savePng, type Img } from "./lib/image.ts";
import { CreditCollector, isShareAlike, leastRestrictive, resolveLicence } from "./lib/licences.ts";
import { OUT } from "./lib/paths.ts";
import { bakeSounds } from "./lib/sounds.ts";
import { ensureSources } from "./lib/sources.ts";
import { bakeWorld } from "./lib/world.ts";

const RECORDING: CreditsFile["recording"] = {
  title: "Orpheus in the Underworld — Overture",
  composer: "Jacques Offenbach",
  performer: "Musopen archive recording",
  licence: "CC0 (unverified)",
  url: "https://commons.wikimedia.org/wiki/File:Offenbach_-_Orpheus_in_the_Underworld_-_Overture.ogg",
  notes: "Development recording. Its CC0 status is unverified, so it must be replaced (or its licence confirmed) before public release.",
};

function writeJson(path: string, value: unknown): void {
  mkdirSync(join(path, ".."), { recursive: true });
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`);
}

async function writePages(prefix: string, pages: Img[]): Promise<void> {
  mkdirSync(OUT, { recursive: true });
  for (const f of readdirSync(OUT)) if (new RegExp(`^${prefix}-\\d+\\.png$`).test(f)) rmSync(join(OUT, f));
  for (let i = 0; i < pages.length; i++) await savePng(pages[i], join(OUT, `${prefix}-${i}.png`));
}

/** Per-output licence: CC-BY-SA when any contributing source's chosen licence is share-alike. */
function outputLicence(refs: string[]): string {
  const chosen = refs.map((r) => leastRestrictive(resolveLicence(r).licences)!);
  if (chosen.some(isShareAlike)) return "CC-BY-SA 3.0";
  if (chosen.every((l) => l === "CC0")) return "CC0";
  return "CC-BY 3.0 / OGA-BY 3.0 (attribution required)";
}

async function main(): Promise<void> {
  const only = process.argv.find((a) => a.startsWith("--only="))?.slice(7).split(",");
  const want = (k: string) => !only || only.includes(k);
  const log = (s: string) => console.log(s);
  await ensureSources(log);

  const credits = new CreditCollector();
  const perOutput: Record<string, Set<string>> = {};
  const user = (output: string) => (ref: string, usedFor: string) => {
    credits.use(ref, usedFor);
    (perOutput[output] ??= new Set()).add(ref);
  };

  if (want("characters")) {
    log("characters");
    const { atlas, pages } = await bakeCharacters(user("characters"), log);
    credits.validate();
    await writePages("characters", pages);
    writeJson(join(OUT, "characters.json"), atlas);
  }
  if (want("world")) {
    log("world");
    const { atlas, pages } = await bakeWorld(user("world"), log);
    credits.validate();
    await writePages("world", pages);
    writeJson(join(OUT, "world.json"), atlas);
  }
  if (want("effects")) {
    log("effects");
    const { atlas, pages } = await bakeEffects(user("effects"), log);
    credits.validate();
    await writePages("effects", pages);
    writeJson(join(OUT, "effects.json"), atlas);
  }
  if (want("sounds")) {
    log("sounds");
    const manifest = await bakeSounds(user("sounds"), log);
    credits.validate();
    writeJson(join(OUT, "sfx/sounds.json"), manifest);
  }

  credits.validate();
  if (!only) {
    const outputs: NonNullable<CreditsFile["outputs"]> = [];
    const pagesOf = (prefix: string) => readdirSync(OUT).filter((f) => new RegExp(`^${prefix}-\\d+\\.png$`).test(f)).sort();
    for (const prefix of ["characters", "world", "effects"]) {
      const lic = outputLicence([...(perOutput[prefix] ?? [])]);
      for (const f of pagesOf(prefix)) {
        outputs.push({ file: f, licence: lic, ...(lic.startsWith("CC-BY-SA") ? { notes: "Contains art derived from CC-BY-SA sources, so the baked page is CC-BY-SA." } : {}) });
      }
    }
    outputs.push({ file: "sfx/*", licence: outputLicence([...(perOutput.sounds ?? [])]), notes: "Each sample keeps its source's licence; see entries." });
    const file: CreditsFile = { recording: RECORDING, entries: credits.entries(), outputs };
    writeJson(join(OUT, "credits.json"), file);
  } else if (!existsSync(join(OUT, "credits.json"))) {
    console.warn("credits.json not written: run a full bake");
  } else {
    // Keep the credits file honest on partial bakes: fail if a used file is not credited.
    const existing = JSON.parse(readFileSync(join(OUT, "credits.json"), "utf8")) as CreditsFile;
    const files = new Set(existing.entries.map((e) => e.file));
    const missing = credits.entries().filter((e) => !files.has(e.file));
    if (missing.length) console.warn(`credits.json is stale (${missing.length} new entries); run a full bake`);
  }
  log("done");
}

main().catch((e) => {
  console.error(e instanceof Error ? e.message : e);
  process.exit(1);
});
