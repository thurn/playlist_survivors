/**
 * The reactive feel corpus (docs/design/reactive-feel.md, "The Corpus"): the manifest, the shipped
 * index built from it, the credits, and the split between gated and corpus tracks.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { buildIndex, corpusAudio, corpusGroup, familyOf, readManifest, REGRESSION_TRACKS, runJobs } from "../scripts/lib/corpus.ts";
import { ROOT } from "../scripts/lib/files.ts";
import { corpusTrackIds, gatedTrackIds } from "../scripts/lib/reactive-node.ts";
import { parseReactiveIndex } from "../src/reactive/tracks.ts";

const manifest = readManifest();
const index = parseReactiveIndex(JSON.parse(readFileSync(join(ROOT, "public/reactive/index.json"), "utf8")));

describe("the corpus", () => {
  it("lists the design's 39 NotJam tracks, 23 to iterate on and 16 held out, each once", () => {
    const notjam = manifest.filter((t) => corpusGroup(t) === "notjam");
    expect(notjam.length).toBe(39);
    expect(notjam.filter((t) => t.split === "iteration").length).toBe(23);
    expect(notjam.filter((t) => t.split === "held-out").length).toBe(16);
    expect(new Set(manifest.map((t) => t.id)).size).toBe(manifest.length);
    expect(new Set(manifest.map((t) => t.source)).size).toBe(manifest.length);
    // (Never a loopable copy or a jingle.)
    expect(manifest.filter((t) => /_Loopable|let_us_begin|you_died/.test(t.source))).toEqual([]);
  });

  it("keeps each family of near-duplicates on one side of the split", () => {
    const splits = new Map<string, Set<string>>();
    for (const t of manifest) (splits.get(familyOf(t)) ?? splits.set(familyOf(t), new Set()).get(familyOf(t))!).add(t.split);
    for (const [family, s] of splits) expect([...s], family).toHaveLength(1);
    expect(manifest.filter((t) => t.family !== undefined).length).toBe(8);
  });

  it("ships every manifest track in the index with its group, split, licence and credit", () => {
    for (const t of manifest) {
      const e = index.tracks.find((x) => x.id === t.id);
      expect(e, t.id).toBeDefined();
      expect(e).toEqual({ id: t.id, title: t.title, artist: t.artist, group: corpusGroup(t), stream: `${t.id}/stream.json`, audio: corpusAudio(t), split: t.split, licence: t.licence, ...(t.credit ? { credit: t.credit } : {}) });
    }
    // The index is exactly what the scripts build: the listed regression tracks, then the corpus.
    expect(index).toEqual(buildIndex(manifest));
    expect(index.tracks.slice(0, REGRESSION_TRACKS.filter((t) => t.listed).length).every((t) => t.split === undefined)).toBe(true);
  });

  it("streams every corpus track with the fitted reference beside it", () => {
    // (Structure only: whether a held-out track fitted is not for a gate to read.)
    for (const t of manifest) {
      const fitted = JSON.parse(readFileSync(join(ROOT, "public/reactive", t.id, "reference.json"), "utf8")) as { source: string; fit: string };
      const stream = JSON.parse(readFileSync(join(ROOT, "public/reactive", t.id, "stream.json"), "utf8")) as { reference?: { source: string; fit?: string; beats: number[] } };
      expect(fitted.source, t.id).toBe("fitted");
      expect(["constant", "piecewise", "failed"], t.id).toContain(fitted.fit);
      expect(stream.reference, t.id).toEqual(fitted);
    }
  });

  it("credits every track", () => {
    const credits = readFileSync(join(ROOT, "corpus/CREDITS.md"), "utf8");
    for (const t of manifest) expect(credits, t.id).toContain(`\`${t.id}\``);
    for (const t of manifest) if (t.credit) expect(credits, t.id).toContain(t.credit);
  });

  it("keeps the corpus, and every held-out track, out of the gated tracks", () => {
    const gated = gatedTrackIds();
    expect(gated).toEqual(REGRESSION_TRACKS.filter((t) => t.listed).map((t) => t.id));
    expect(corpusTrackIds().sort()).toEqual(manifest.map((t) => t.id).sort());
    for (const id of corpusTrackIds("held-out")) expect(gated).not.toContain(id);
    expect(corpusTrackIds("iteration")).toContain("notjam-breakbeat-chips");
  });
});

describe("the parallel jobs", () => {
  it("runs every job at most n at once, a failure stopping none of the others, and rejects with every failure", async () => {
    let running = 0;
    let peak = 0;
    const done: number[] = [];
    const run = runJobs([1, 2, 3, 4, 5, 6], 2, async (n) => {
      running++;
      peak = Math.max(peak, running);
      await new Promise((r) => setTimeout(r, 5));
      running--;
      if (n % 3 === 0) throw new Error(`job ${n} failed`);
      done.push(n);
    });
    await expect(run).rejects.toThrow(/job 3 failed\n\njob 6 failed/);
    expect(done.sort()).toEqual([1, 2, 4, 5]);
    expect(peak).toBe(2);
  });
});
