import { describe, expect, it } from "vitest";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { chunkContent, waveRadius, zoneAt, WAVE_BAND, WAVE_BANDS, type ZoneWave } from "../src/mapgen/openfield.ts";
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { checkPlay } from "../src/planner/play-checks.ts";
import { heroSample } from "../src/plan/sample.ts";
import { Warp } from "../src/core/warp.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const LEVEL = "levels/orpheus-galop.play.yaml";

describe("the open field's geometry", () => {
  const wave: ZoneWave = { s: 10, w: 10, x: 100, y: 0, sbi: 20, prev: 0, zone: 1, endS: 11 };

  it("grows a transform wave's ring one band per sixteenth note, from its downbeat", () => {
    expect(waveRadius(wave, 19.9)).toBe(0);
    expect(waveRadius(wave, 20)).toBe(WAVE_BAND);
    expect(waveRadius(wave, 20.26)).toBe(2 * WAVE_BAND);
    expect(waveRadius(wave, 30)).toBe(WAVE_BAND * WAVE_BANDS);
  });

  it("gives each point the zone of the newest wave that has reached it", () => {
    // Before the wave, and outside its ring, the first zone; inside the ring and after it has passed, the new one.
    expect(zoneAt([wave], 0, 100, 0, 9, 18)).toBe(0);
    expect(zoneAt([wave], 0, 150, 0, 10.01, 20.01)).toBe(1);
    expect(zoneAt([wave], 0, 100 + 2 * WAVE_BAND, 0, 10.01, 20.01)).toBe(0);
    expect(zoneAt([wave], 0, 5000, 5000, 11, 22)).toBe(1);
  });

  it("generates each chunk identically from its seed, place and zone", () => {
    const lib = plannerInput({ songMap: syntheticSongMap(), level: LEVEL }).content;
    const theme = lib.zones.get("galop-woods")!;
    const a = chunkContent(1858, 3, -2, theme);
    expect(chunkContent(1858, 3, -2, theme)).toEqual(a);
    expect(chunkContent(1858, 4, -2, theme)).not.toEqual(a);
    expect(a.ground.length).toBeGreaterThan(4);
    for (const p of [...a.ground, ...a.tall]) {
      expect(p.x).toBeGreaterThanOrEqual(3 * 512);
      expect(p.x).toBeLessThanOrEqual(4 * 512);
    }
  });
});

describe("a play-mode run in the open field", () => {
  const songMap = syntheticSongMap();
  const input = plannerInput({ songMap, level: LEVEL });
  const plan = runPlanner({ songMap, level: LEVEL });

  it("is played by the moderate player in the open field", () => {
    expect(plan.profile).toBe("moderate");
    expect(plan.open).toBeDefined();
    expect(plan.rules).toEqual({ oneStepPerSlot: true, accentTelegraphs: true, health: true });
  });

  it("hurts the hero, heals it on clean beats, and it survives", () => {
    expect(plan.stats.minHealth).toBeLessThan(1);
    expect(plan.stats.minHealth).toBeGreaterThan(0);
    expect(plan.stats.deathS).toBeUndefined();
    const h = plan.health;
    let heals = 0;
    for (let i = 1; i < h.v.length; i++) if (h.v[i] > h.v[i - 1]) heals++;
    expect(heals).toBeGreaterThan(0);
  });

  it("passes the checks it shares with the demo, and play mode's own rules", () => {
    const shared = summarizeChecks(checkPlan(plan, input.content));
    expect(shared.ok, shared.text).toBe(true);
    const own = summarizeChecks(checkPlay(plan, input.content, { signature: false }));
    expect(own.ok, own.text).toBe(true);
  });

  it("sweeps a transform wave out from the hero for each zone change, and raises the landmarks around the hero", () => {
    const warp = new Warp(plan.warp);
    const zones = plan.sections.map((s) => s.zone).filter((z, i, a) => i > 0 && z !== a[i - 1]);
    expect(plan.open!.waves.length).toBe(zones.length);
    for (const wv of plan.open!.waves) {
      const h = heroSample(plan, warp.toWorld(wv.s));
      expect(Math.hypot(h.x - wv.x, h.y - wv.y)).toBeLessThan(8);
    }
    expect(plan.open!.landmarks.map((l) => l.id)).toEqual(["standing-stones", "ruined-shrine"]);
    for (const l of plan.open!.landmarks) {
      const h = heroSample(plan, warp.toWorld(l.s));
      expect(Math.hypot(h.x - l.x, h.y - l.y)).toBeLessThan(8);
    }
  });

  it("wanders the field: no route, and the hero goes where the bot roams", () => {
    let far = 0;
    for (let k = 0; k < plan.hero.x.length; k++) far = Math.max(far, Math.hypot(plan.hero.x[k], plan.hero.y[k]));
    expect(far).toBeGreaterThan(300);
  });
});
