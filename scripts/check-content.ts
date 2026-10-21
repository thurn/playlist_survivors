/**
 * Content check: validates every content file and level script against its
 * schema and the song map, then confirms each deliberately broken fixture
 * fails with a useful message.
 */
import { readdirSync } from "node:fs";
import { join } from "node:path";
import { ContentError, loadLevel } from "../src/content/loader.ts";
import { ROOT, hasSongMap, readSongMap, readSource } from "./lib/files.ts";
import { checkBrokenFixtures, loadRealContent } from "./lib/content-check.ts";

let failed = false;
try {
  const lib = loadRealContent();
  const counts = Object.entries(lib)
    .filter(([, m]) => m instanceof Map)
    .map(([k, m]) => `${(m as Map<string, unknown>).size} ${k}`);
  console.log(`content ok: ${counts.join(", ")}`);
  for (const name of readdirSync(join(ROOT, "levels")).filter((f) => f.endsWith(".yaml"))) {
    const src = readSource(join("levels", name));
    const song = /^song: (\S+)/m.exec(src.text)?.[1] ?? "";
    if (!hasSongMap(song)) {
      console.error(`${src.name}: no song map at songs/${song}/songmap.json`);
      failed = true;
      continue;
    }
    const level = loadLevel(src, lib, readSongMap(song));
    console.log(`level ok: ${src.name} (${level.sections.length} sections, ${level.events.length} events)`);
  }
  const song = readSongMap("orpheus-galop");
  for (const r of checkBrokenFixtures(song)) {
    if (r.ok) console.log(`broken fixture fails as expected: ${r.name}\n    ${r.message.split("\n")[0]}`);
    else {
      failed = true;
      console.error(`broken fixture ${r.name}: expected an error containing "${r.expected}", got:\n    ${r.message}`);
    }
  }
} catch (e) {
  failed = true;
  console.error(e instanceof ContentError ? e.message : e);
}
process.exit(failed ? 1 : 0);
