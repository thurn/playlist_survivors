import { describe, expect, it } from "vitest";
import { ContentError, loadLevel } from "../src/content/loader.ts";
import { checkBrokenFixtures, loadRealContent } from "../scripts/lib/content-check.ts";
import { readSource } from "../scripts/lib/files.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

describe("content", () => {
  it("loads every content file", () => {
    const lib = loadRealContent();
    expect(lib.hero.swings.heavy.hitStopMs).toBe(110);
    expect(lib.enemies.get("orc")?.health).toBe(1.15);
    expect(lib.enemies.get("minotaur")?.behavior).toBe("boss");
    expect(lib.weapons.get("orbiting-blades")?.damage).toBe(0.6);
  });

  it("loads the level script with inherited section fields", () => {
    const lib = loadRealContent();
    const level = loadLevel(readSource("levels/orpheus-galop.yaml"), lib, syntheticSongMap());
    const opening = level.sections[2];
    expect(opening.zone).toBe("galop-woods"); // inherited from the galop vamp
    expect(opening.timeScale).toEqual({ from: 1, to: 1 }); // default, not inherited
    expect(level.sections[3].sfxPalette).toBe("crisp-steel");
    // Movement settings: presses and pace inherit; the timing grid and heavy swings do not.
    const repeat = level.sections[4];
    expect(repeat.movement).toBe("steps");
    expect(repeat.presses).toBe("xx");
    expect(repeat.pace).toBe(55);
    expect(opening.heavyOnAccents).toEqual({ minStrength: 0.7, label: "tutti-chord" });
    expect(level.sections[3].heavyOnAccents).toEqual({ minStrength: 0.7, label: undefined });
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
