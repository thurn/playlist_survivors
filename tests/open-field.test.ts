import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas, runPlanner } from "../scripts/lib/plan-node.ts";
import { plan as makePlan } from "../src/planner/plan.ts";
import type { KeyEvent, Player } from "../src/sim/player.ts";
import { chunkContent, waveRadius, zoneAt, WAVE_BAND, WAVE_BANDS, type ZoneWave } from "../src/mapgen/openfield.ts";
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { checkPlay, checkStrides } from "../src/planner/play-checks.ts";
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
    expect(plan.rules).toEqual({ oneStepPerSlot: false, accentTelegraphs: true, health: true, strides: true, pressClasses: true, selfFiring: true, gridSounds: true });
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

  it("changes the groove only on presses and beats, never on a bite", () => {
    const presses = new Set(plan.inputs.filter((i) => i.kind === "press").map((i) => i.s));
    const beats = new Set(Array.from(plan.songBeats));
    const g = plan.groove;
    let n = 0;
    for (let k = 1; k < g.s.length; k++) {
      n++;
      expect(presses.has(g.s[k]) || beats.has(g.s[k]), `groove change at s=${g.s[k]}`).toBe(true);
    }
    expect(n).toBeGreaterThan(100);
    expect(plan.stats.minHealth).toBeLessThan(1);
  });

  // A run with a few taps of Right, each with a quiet second around it (for the checks' negative cases).
  const quiet = (() => {
    const taps = [62, 64, 66];
    let next = 0;
    const player: Player = {
      nextDecision: (after) => (next < taps.length ? Math.max(after, taps[next]) : Infinity),
      decide: (obs): KeyEvent[] => {
        const s = taps[next++];
        return s === undefined || obs.s < s - 1e-6 ? [] : [{ s, kind: "press", key: "right" }, { s: s + 0.05, kind: "release", key: "right" }];
      },
    };
    return makePlan(input, { props: propsFromAtlas(), player });
  })();

  it("catches a press that does not stride at once", () => {
    const dead = (fake: (typeof plan.inputs)[number]) => {
      const inputs = [...quiet.inputs, fake].sort((a, b) => a.w - b.w);
      return checkStrides({ ...quiet, inputs }).find((c) => c.name === "no dead presses")!.failures;
    };
    expect(checkStrides(quiet).find((c) => c.name === "no dead presses")!.failures).toEqual([]);
    // A tap with no other press for a second around it: the hero stands still 300 ms on.
    const p = quiet.inputs.find((i) => i.kind === "press")!;
    const at = (dt: number) => ({ ...p, s: p.s + dt, w: p.w + dt });
    // An unapplied press after the stride has landed, with or without a stride recorded for it.
    expect(dead({ ...at(0.3), stride: 10 }).length).toBe(1);
    expect(dead({ ...at(0.3), stride: undefined }).length).toBe(1);
    // An ignored press during the stride in progress: the hop is the earlier stride's, not its own
    // (late in the stride, its height on the way down can match a new stride's on the way up once).
    expect(dead({ ...at(0.03), stride: 10 }).length).toBe(1);
    expect(dead({ ...at(0.09), stride: 10 }).length).toBe(1);
    // A bend claimed long after the stride landed.
    expect(dead({ ...at(0.3), bend: true, stride: undefined }).length).toBe(1);
  });

  it("lets no stomp with Space authorize movement", () => {
    // A quiet moment, 300 ms after a tap: a Space stomp there, and the hero nudged 10 px.
    const p = quiet.inputs.find((i) => i.kind === "press")!;
    const at = { ...p, s: p.s + 0.3, w: p.w + 0.3, key: "space" as const, stride: undefined, stomp: true, rating: undefined };
    const inputs = [...quiet.inputs, at, { ...at, kind: "release" as const, s: at.s + 0.05, w: at.w + 0.05 }].sort((a, b) => a.w - b.w);
    const x = Float32Array.from(quiet.hero.x);
    const k = Math.floor(at.w * quiet.hero.rate) + 1;
    for (let j = k; j < k + 3; j++) x[j] += 10;
    const moved = checkPlan({ ...quiet, inputs, hero: { ...quiet.hero, x } }, input.content).find((c) => c.name === "every hero movement has an input")!;
    expect(moved.failures.length).toBeGreaterThan(0);
  });

  it("holds the hero to the walk speed over windows of any length", () => {
    // A hero track gliding steadily for 60 s: a hair over the walk speed breaks the cap in the end.
    const glide = (speed: number) => {
      const n = 60 * plan.hero.rate;
      const x = Float32Array.from({ length: n }, (_, k) => (k * speed) / plan.hero.rate);
      const hero = { ...plan.hero, x, y: new Float32Array(n), z: new Float32Array(n) };
      return checkStrides({ ...plan, hero, inputs: [] }).find((c) => c.name === "the speed cap")!.failures;
    };
    expect(glide(139.9)).toEqual([]);
    expect(glide(141).length).toBe(1);
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
