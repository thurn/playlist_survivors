import { describe, expect, it } from "vitest";
import { ContentError, loadLevel } from "../src/content/loader.ts";
import { checkBrokenFixtures, loadRealContent } from "../scripts/lib/content-check.ts";
import { readSource } from "../scripts/lib/files.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

describe("content", () => {
  it("loads every content file", () => {
    const lib = loadRealContent();
    expect(lib.moves.get("cleave")?.hitStopMs).toBe(110);
    // galop-dashes inherits locomotion and rules from galop-steps.
    const dashes = lib.movesets.get("galop-dashes")!;
    expect(dashes.locomotion?.mode).toBe("step");
    expect(dashes.attacks?.some((a) => "pattern" in a && a.moves.includes("dash-strike"))).toBe(true);
    expect(dashes.attacks?.some((a) => "pattern" in a && a.moves.includes("slash"))).toBe(true);
  });

  it("loads the level script with inherited section fields", () => {
    const lib = loadRealContent();
    const level = loadLevel(readSource("levels/orpheus-galop.yaml"), lib, syntheticSongMap());
    const opening = level.sections[1];
    expect(opening.zone).toBe("twilight-glade"); // inherited
    expect(opening.timeScale).toEqual({ from: 1, to: 1 }); // default, not inherited
    expect(level.sections[2].sfxPalette).toBe("crisp-steel");
  });

  it("rejects every broken fixture with a useful message", () => {
    const results = checkBrokenFixtures(syntheticSongMap());
    expect(results.length).toBeGreaterThan(5);
    for (const r of results) expect(r.ok, `${r.name}: expected "${r.expected}", got ${r.message}`).toBe(true);
  });

  it("reports line numbers", () => {
    const lib = loadRealContent();
    try {
      loadLevel({ name: "x.yaml", text: "song: orpheus-galop\nseed: 1\ntitle: {piece: a, composer: b, performer: c}\nzones: [nowhere]\nsections: []\n" }, lib, syntheticSongMap());
      throw new Error("should fail");
    } catch (e) {
      expect(e).toBeInstanceOf(ContentError);
      expect((e as ContentError).message).toMatch(/x\.yaml:4: zones\[0\]: unknown zone "nowhere"|x\.yaml:5/);
    }
  });
});
