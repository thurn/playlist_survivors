/** Shared logic for `npm run check:content` and its tests. */
import { readdirSync } from "node:fs";
import { join } from "node:path";
import { ContentError, loadContent, loadLevel, type ContentLibrary } from "../../src/content/loader.ts";
import type { SongMap } from "../../src/core/songmap.ts";
import { ROOT, readContentFiles, readSource } from "./files.ts";

export interface FixtureResult {
  name: string;
  expected: string;
  ok: boolean;
  message: string;
}

/** Each broken fixture must fail to load with a message containing its `# expect:` text. */
export function checkBrokenFixtures(song: SongMap, dir = "tests/fixtures/broken"): FixtureResult[] {
  const out: FixtureResult[] = [];
  const goodContent = readContentFiles();
  for (const name of readdirSync(join(ROOT, dir)).filter((f) => f.endsWith(".yaml")).sort()) {
    const src = readSource(join(dir, name));
    const expected = /^# expect: (.*)$/m.exec(src.text)?.[1]?.trim() ?? "";
    let message: string;
    try {
      if (name.endsWith(".content.yaml")) {
        loadContent([...goodContent, src]);
      } else {
        loadLevel(src, loadContent(goodContent), song);
      }
      message = "(loaded without error)";
    } catch (e) {
      message = e instanceof ContentError ? e.message : String(e);
    }
    out.push({ name, expected, ok: expected !== "" && message.includes(expected), message });
  }
  return out;
}

export function loadRealContent(): ContentLibrary {
  return loadContent(readContentFiles());
}
