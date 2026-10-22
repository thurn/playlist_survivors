import { describe, expect, it } from "vitest";
import { ContentError, loadLevel, type LevelResolver } from "../src/content/loader.ts";
import { loadRealContent } from "../scripts/lib/content-check.ts";
import { levelResolver, readSource } from "../scripts/lib/files.ts";
import { plannerInput, propsFromAtlas } from "../scripts/lib/plan-node.ts";
import { prepare, runSim, virtualPlayer } from "../src/planner/plan.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { grooveAt } from "../src/plan/evaluate.ts";
import { BatchRecorder } from "../src/sim/recorder.ts";
import { Sim, HERO_ANIMS, HERO_RATE } from "../src/sim/simulate.ts";
import { ratePress } from "../src/sim/timing.ts";
import { CorridorWorld } from "../src/sim/world.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const lib = loadRealContent();
const song = syntheticSongMap();
const demo = loadLevel(readSource("levels/orpheus-galop.yaml"), lib, song, levelResolver);
const play = loadLevel(readSource("levels/orpheus-galop.play.yaml"), lib, song, levelResolver);

describe("the play level file", () => {
  it("extends the demo's level script, restating only what differs", () => {
    expect(play.extends).toBe("orpheus-galop");
    expect(play.world).toBe("open");
    expect(demo.world).toBe("corridor");
    expect(play.rules).toEqual({ oneStepPerSlot: true, accentTelegraphs: true, health: true, strides: true });
    expect(demo.rules).toEqual({ oneStepPerSlot: false, accentTelegraphs: false, health: false, strides: false });
    expect(play.player).toBe("moderate");
    expect(play.seed).toBe(demo.seed);
    expect(play.title).toEqual(demo.title);
    // Sections merge field by field: the climax keeps its zoom and loses its camera lead.
    const climax = play.sections.find((s) => s.label === "climax")!;
    expect(climax.camera).toEqual({ ...demo.sections.find((s) => s.label === "climax")!.camera, lead: 0 });
    // The galop starts in the woods (the vamp sets it; the opening inherits it).
    expect(play.sections.find((s) => s.label === "galop-vamp")!.zone).toBe("galop-woods");
    expect(play.sections.find((s) => s.label === "galop-opening")!.zone).toBe("galop-woods");
    // The prelude plays at full speed, with calm enemies instead.
    const prelude = play.sections.find((s) => s.label === "prelude")!;
    expect(prelude.timeScale).toEqual({ from: 1, to: 1 });
    expect(prelude.calm).toBe(0.5);
    expect(prelude.autoSwingBeats).toBe(1);
    expect(demo.sections[0].autoSwingBeats).toBe(2);
    expect(demo.sections[0].calm).toBe(0);
    expect(play.sections.find((s) => s.label === "galop-vamp")!.calm).toBe(0); // not inherited
    const changed = ["prelude", "climax", "galop-vamp", "galop-opening"];
    for (const [i, s] of play.sections.entries()) if (!changed.includes(s.label)) expect(s).toEqual(demo.sections[i]);
    // Events merge by `at` and kind: the level-ups need their own experience; nothing is added.
    expect(play.events.length).toBe(demo.events.length);
    expect(play.events.filter((e) => e.levelUp).map((e) => [e.levelUp, e.xp])).toEqual([
      ["orbiting-blades", 300],
      ["chain-lightning", 850],
      ["fire-trail", 1700],
    ]);
    expect(play.events.filter((e) => !e.levelUp)).toEqual(demo.events.filter((e) => !e.levelUp));
  });

  it("merges its hero overrides over content/hero.yaml for this level only", () => {
    expect(play.hero.timing).toEqual({ perfectMs: 45, greatMs: 90, goodMs: 140, floor: 0.1 });
    expect(play.hero.step).toEqual(lib.hero.step);
    expect(lib.hero.timing.perfectMs).toBe(35);
    expect(demo.hero).toBe(lib.hero);
    // Looser timing windows: 80 ms off is Great in play mode, Good in the demo.
    expect(ratePress(play.hero, 0.08, 0.3).rating).toBe("great");
    expect(ratePress(lib.hero, 0.08, 0.3).rating).toBe("good");
  });

  it("points each error at the file that set the value", () => {
    const base = readSource("levels/orpheus-galop.yaml");
    const files: Record<string, string> = {
      // A parent with a bad section (its first section's pace is negative), and a child that is fine.
      parent: base.text.replace("    pace: 30 ", "    pace: -30 "),
      child: "extends: parent\nworld: open\n",
      // A child that breaks a value the parent set correctly.
      typo: "extends: orpheus-galop\nsections:\n  - section: theme-a\n    presses: \"xo\"\n",
    };
    const resolve: LevelResolver = (name) => (files[name] !== undefined ? { name: `levels/${name}.yaml`, text: files[name] } : levelResolver(name));
    const fail = (name: string) => {
      try {
        loadLevel({ name: `levels/${name}.yaml`, text: files[name] }, lib, song, resolve);
      } catch (e) {
        expect(e).toBeInstanceOf(ContentError);
        return (e as ContentError).message;
      }
      throw new Error(`${name} loaded`);
    };
    expect(fail("child")).toMatch(/^levels\/parent\.yaml:\d+: sections\[0\]\.pace: /);
    expect(fail("typo")).toMatch(/^levels\/typo\.yaml:4: sections\[3\]\.presses: presses use x and \./);
  });
});

describe("play-mode rules", () => {
  const input = plannerInput({ songMap: song, level: "levels/orpheus-galop.play.yaml" });
  const { ctx, sch, map } = prepare(input, propsFromAtlas());

  /** The hero's travel and the recorded presses for a few key events in theme A. */
  const mash = (rules: typeof play.rules) => {
    const rec = new BatchRecorder();
    const sim = new Sim(ctx, sch, new CorridorWorld(map, sch), rules, rec);
    const sec = ctx.sections.find((s) => s.label === "theme-a")!;
    const grid = sch.gridSlots.filter((g) => g.s > sec.s0 + 2).slice(0, 3).map((g) => g.s);
    const keys: KeyEvent[] = [
      // A press on the beat...
      { s: grid[0], kind: "press", key: "right" },
      { s: grid[0] + 0.03, kind: "release", key: "right" },
      // ...a mashed second press for the same beat...
      { s: grid[0] + 0.07, kind: "press", key: "right" },
      { s: grid[0] + 0.09, kind: "release", key: "right" },
      // ...and a diagonal on the next beat: the second key bends the step.
      { s: grid[1] - 0.01, kind: "press", key: "right" },
      { s: grid[1] + 0.01, kind: "press", key: "down" },
      { s: grid[1] + 0.05, kind: "release", key: "right" },
      { s: grid[1] + 0.05, kind: "release", key: "down" },
    ];
    sim.advanceTo(grid[0] - 0.5);
    const start = sim.heroPosition();
    for (const k of keys) sim.key(k);
    sim.advanceTo(grid[2]);
    const end = sim.heroPosition();
    sim.runToEnd();
    const r = rec.result({ endS: sim.s, animNames: HERO_ANIMS, heroRate: HERO_RATE });
    const presses = r.inputs.filter((i) => i.kind === "press" && i.s > grid[0] - 0.01 && i.s < grid[2]);
    const groove = r.groove.s.filter((s) => s > grid[0] - 0.01 && s < grid[2]).length;
    return { travel: Math.hypot(end.x - start.x, end.y - start.y), presses, groove, grid };
  };

  it("rates one press per grid slot: a mashed press still strides, a diagonal still bends", () => {
    const m = mash(play.rules);
    expect(m.presses.map((p) => [p.key, !!p.extra, !!p.bend, p.rating !== undefined])).toEqual([
      ["right", false, false, true],
      ["right", true, false, false],
      ["right", false, false, true],
      ["down", false, true, false],
    ]);
    // The extra press is not rated and does not touch the groove; it arms nothing.
    expect(m.groove).toBe(2);
    expect(m.presses[1].armed).toBeUndefined();
    // Every press strides: the mashed one splits the ground with the next, so the hero is no faster
    // than walking (140 px/s), unlike the demo's steps, where it adds a whole 44 px step.
    const [g0, g1] = m.grid;
    const one = 140 * (g1 - g0);
    expect(m.presses[0].stride).toBeCloseTo(one, 6);
    expect(m.presses[1].stride).toBeGreaterThan(140 * 0.07);
    expect(m.presses[1].stride).toBeLessThan(one / 2);
    expect(m.presses[2].stride).toBeCloseTo(140 * (g1 - 0.01 - (g0 + 0.07)), 6);
    expect(m.presses[3].stride).toBeUndefined();
    expect(m.travel).toBeLessThan(one + 140 * (g1 - g0));
    expect(mash(demo.rules).travel).toBeGreaterThan(m.travel + 20);
  });

  it("lifts the groove past full for eighth notes on the off-beat, and ignores sloppy extras", () => {
    const sec = ctx.sections.find((s) => s.label === "theme-a")!;
    const grid = Array.from(sch.gridTimes).filter((g) => g > sec.s0 + 2).slice(0, 17);
    const run = (offset: number) => {
      const rec = new BatchRecorder();
      const sim = new Sim(ctx, sch, new CorridorWorld(map, sch), play.rules, rec);
      sim.advanceTo(grid[0] - 0.5);
      for (let k = 0; k + 1 < grid.length; k++) {
        const half = (grid[k] + grid[k + 1]) / 2;
        sim.key({ s: grid[k], kind: "press", key: k % 2 ? "left" : "right" });
        sim.key({ s: grid[k] + 0.02, kind: "release", key: k % 2 ? "left" : "right" });
        sim.key({ s: half + offset, kind: "press", key: k % 2 ? "right" : "left" });
        sim.key({ s: half + offset + 0.02, kind: "release", key: k % 2 ? "right" : "left" });
      }
      sim.advanceTo(grid[grid.length - 1] - 0.01);
      sim.runToEnd();
      const r = rec.result({ endS: sim.s, animNames: HERO_ANIMS, heroRate: HERO_RATE });
      const extras = r.inputs.filter((i) => i.kind === "press" && i.extra && i.s > grid[0] && i.s < grid[grid.length - 1]);
      return { groove: grooveAt(r.groove, grid[grid.length - 1] - 0.01), extras };
    };
    const onBeat = run(0);
    expect(onBeat.extras.length).toBe(16);
    expect(onBeat.extras.every((i) => i.offbeat === "perfect")).toBe(true);
    expect(onBeat.groove).toBeGreaterThan(1.05);
    // Extras outside the Great window around the half-beat are unrated: the groove stays at what the beat presses give.
    const sloppy = run(-0.13);
    expect(sloppy.extras.length).toBe(16);
    expect(sloppy.extras.every((i) => i.offbeat === undefined)).toBe(true);
    expect(sloppy.groove).toBeLessThanOrEqual(1);
  });

  it("walks at full speed in the calm prelude", () => {
    const sim = new Sim(ctx, sch, new CorridorWorld(map, sch), play.rules, new BatchRecorder());
    sim.advanceTo(2);
    sim.key({ s: 2, kind: "press", key: "right" });
    sim.advanceTo(2.5);
    const a = sim.heroPosition();
    sim.advanceTo(3.5);
    const b = sim.heroPosition();
    expect(Math.hypot(b.x - a.x, b.y - a.y)).toBeCloseTo(play.hero.walk.speed, -1);
  });

  it("swings by itself on every beat in the prelude, twice the demo's rate", () => {
    const autos = (s: typeof sch) => s.slots.filter((x) => x.kind === "auto" && x.section === 0);
    const demoSch = prepare(plannerInput({ songMap: song }), propsFromAtlas()).sch;
    const gaps = autos(sch).slice(1).map((x, i) => x.bi - autos(sch)[i].bi);
    expect(Math.min(...gaps)).toBe(1);
    expect(autos(sch).length).toBeGreaterThan(1.8 * autos(demoSch).length);
  });

  it("leaves the demo's rules alone: there, every press steps", () => {
    const m = mash(demo.rules);
    expect(m.presses.filter((p) => p.extra).length).toBe(0);
    expect(m.groove).toBe(3);
  });
});

describe("the moderate virtual player", () => {
  const input = plannerInput({ songMap: song });
  const skilled = runSim(input, { props: propsFromAtlas(), profile: "skilled" }).sim;
  const moderate = runSim(input, { props: propsFromAtlas(), profile: "moderate" }).sim;
  const rated = (inputs: typeof skilled.inputs) => inputs.filter((i) => i.kind === "press" && i.steps && !i.bend);

  it("presses about 85% of the pattern, with a looser hand", () => {
    const m = rated(moderate.inputs);
    const s = rated(skilled.inputs);
    const share = m.length / s.length;
    expect(share).toBeGreaterThan(0.75);
    expect(share).toBeLessThan(0.95);
    const deltas = m.map((i) => i.delta ?? 0);
    expect(Math.max(...deltas.map(Math.abs))).toBeLessThanOrEqual(0.1 + 1e-6);
    const sd = Math.sqrt(deltas.reduce((a, d) => a + d * d, 0) / deltas.length);
    expect(sd).toBeGreaterThan(0.02);
    expect(sd).toBeLessThan(0.045);
  });

  it("is a deterministic program of the seed", () => {
    const again = runSim(input, { props: propsFromAtlas(), profile: "moderate" }).sim;
    expect(again.inputs).toEqual(moderate.inputs);
  });

  it("reacts to threats later than the skilled player", () => {
    const { ctx, sch } = prepare(input, propsFromAtlas());
    const v = sch.volleys[0];
    const obs = { s: v.sDraw + 0.2, w: v.sDraw + 0.2, hero: { x: 0, y: 0, stepping: false }, enemies: [], pacePoint: { x: 0, y: 0, tx: 1, ty: 0 }, edgeDistance: () => 100, threats: [{ kind: "slam" as const, x: 0, y: 0, radius: 38, landsAt: v.sArrive, seenAt: v.sDraw }] };
    // Both players stand on a slam marker on a rest slot: the skilled one steps away, the moderate one has not reacted yet.
    const watch = (p: ReturnType<typeof virtualPlayer>) => {
      p.nextDecision(obs.s);
      return p.decide(obs).length;
    };
    void ctx;
    expect(watch(virtualPlayer(ctx, sch, "skilled"))).toBeGreaterThan(0);
    expect(watch(virtualPlayer(ctx, sch, "moderate"))).toBe(0);
  });
});
