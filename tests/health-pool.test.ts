/**
 * The small health pool: "Health and Damage" and "Chicken" in
 * docs/design/gameplay-redesign.md, on the real song: the moderate player's run, and scripted
 * stretches where the hero stands still.
 */
import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas, runPlanner } from "../scripts/lib/plan-node.ts";
import { beatIndexAt, timeOfBeatIndex } from "../src/core/songmap.ts";
import { Rng } from "../src/core/rng.ts";
import { recordingContext } from "../src/live/log.ts";
import { LiveRecording } from "../src/live/recording.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import { heroSample } from "../src/plan/sample.ts";
import { prepare, virtualPlayer } from "../src/planner/plan.ts";
import { KEY_DIR, type Key } from "../src/sim/movement.ts";
import type { KeyEvent, Observation, VirtualPlayer } from "../src/sim/player.ts";
import { BatchRecorder } from "../src/sim/recorder.ts";
import { CHICKEN_BOSS, CHICKEN_CHANCE, CHICKEN_HEAL, CHICKEN_SPREAD, CHICKEN_TOUCH, Sim, worldFor } from "../src/sim/simulate.ts";
import { NEVER_LATE } from "../src/sim/timing.ts";

const PLAY = "levels/orpheus-galop.play.yaml";
const input = plannerInput({ level: PLAY });
const props = propsFromAtlas();
const { ctx, sch, map } = prepare(input);
const lib = ctx.lib;
const song = ctx.song;
const sixteenthAtOrAfter = (t: number) => timeOfBeatIndex(song, Math.ceil(beatIndexAt(song, t) * 4 - 1e-6) / 4);
const moderate = runPlanner({ level: PLAY, profile: "moderate" });
const keys: KeyEvent[] = moderate.inputs.map((i) => ({ s: i.s, kind: i.kind, key: i.key }));
const INVULNERABLE = lib.hero.health.invulnerableMs / 1000;

/** A batch run of the moderate player's keys, standing still from `from` to `to` (letting go of what it held), up to `until`. */
function stand(from: number, to: number, until: number, c = ctx) {
  const rec = new BatchRecorder();
  const sim = new Sim(c, sch, worldFor(), rec);
  const held = new Set<string>();
  for (const k of keys) {
    if (k.s >= from && k.s < to) continue;
    if (k.s < from && k.kind === "press") held.add(k.key);
    else if (k.s < from) held.delete(k.key);
    sim.key(k);
  }
  for (const key of held) sim.key({ s: from, kind: "release", key: key as Key });
  sim.advanceTo(until);
  return rec;
}

/** Without blades and lightning, so a crowd reaches the hero. */
const bare = { ...ctx, level: { ...ctx.level, upgrades: { ...ctx.level.upgrades, loadout: [] } } };

describe("bites", () => {
  it("come at most once per 0.5 s of world time, however many enemies are at contact", () => {
    const rec = stand(60, 90, 90, bare);
    const bites = rec.bites.filter((b) => b.source === "bite");
    expect(bites.length).toBeGreaterThan(8);
    for (const b of rec.bites) expect(b.until).toBeCloseTo(b.w + INVULNERABLE, 9);
    for (let i = 1; i < rec.bites.length; i++) expect(rec.bites[i].w - rec.bites[i - 1].w, `hurt ${i} at ${rec.bites[i].s.toFixed(2)}`).toBeGreaterThanOrEqual(INVULNERABLE - 1e-9);
    // Each takes its enemy's contact damage.
    for (const b of bites) expect(b.damage).toBe(lib.enemies.get(b.enemy)!.contactDamage);
    // (The hero has 100 health.)
    expect(lib.hero.health.max).toBe(100);
  });

  it("leave the hero invulnerable: arrows and slams that land then don't hurt it", () => {
    // Invulnerable for the rest of the run after the first hurt, the hero standing still through the
    // archers' volleys and the boss's slams.
    const immune = { ...bare, lib: { ...bare.lib, hero: { ...lib.hero, health: { ...lib.hero.health, invulnerableMs: 1e7 } } } };
    const rec = stand(75, Infinity, ctx.duration, immune);
    expect(rec.bites.length).toBe(1);
    const touched = rec.threats.filter((t) => t.outcome === "touch" && t.w > rec.bites[0].w);
    expect(touched.some((t) => t.kind === "arrow")).toBe(true);
    expect(touched.some((t) => t.kind === "slam")).toBe(true);
    expect(rec.healthS.filter((s) => s > rec.bites[0].s).length).toBe(rec.heals.filter((h) => h.s > rec.bites[0].s).length);
  });

  it("from an arrow take 15 and from the boss's slam 25", () => {
    // (No bites here, and 1000 health, so the hero standing still lives to meet both.)
    const tough = { ...bare, lib: { ...bare.lib, hero: { ...lib.hero, health: { ...lib.hero.health, max: 1000 } }, enemies: new Map([...lib.enemies].map(([k, e]) => [k, { ...e, contactDamage: 0 }])) } };
    const rec = stand(75, Infinity, ctx.duration, tough);
    const arrows = rec.bites.filter((b) => b.source === "arrow");
    const slams = rec.bites.filter((b) => b.source === "slam");
    expect(arrows.length).toBeGreaterThan(0);
    expect(slams.length).toBeGreaterThan(0);
    for (const b of arrows) expect(b.damage).toBe(15);
    for (const b of slams) expect(b.damage).toBe(25);
  });

  it("never change the groove", () => {
    // The same keys with and without blades and lightning (far more bites): the groove is the same throughout.
    const a = new BatchRecorder();
    const b = new BatchRecorder();
    for (const [rec, c] of [
      [a, ctx],
      [b, bare],
    ] as const) {
      const sim = new Sim(c, sch, worldFor(), rec);
      for (const k of keys) sim.key(k);
      sim.advanceTo(120);
    }
    expect(b.bites.length).toBeGreaterThan(a.bites.length + 3);
    expect(Array.from(a.grooveS)).toEqual(Array.from(b.grooveS));
    expect(Array.from(a.grooveV)).toEqual(Array.from(b.grooveV));
  });

  it("sound on the first sixteenth at least 60 ms later, and make the hero blink while invulnerable", () => {
    const bites = moderate.bites;
    expect(bites.length).toBeGreaterThan(3);
    const hurts = moderate.events.sounds.filter((x) => x.key === "hurt");
    for (const b of bites) expect(hurts.some((x) => Math.abs(x.s - sixteenthAtOrAfter(b.s + NEVER_LATE)) < 1e-6 && x.movedFrom === undefined), `a hurt sound for ${b.s.toFixed(3)}`).toBe(true);
    const ev = new Evaluator(moderate, lib);
    const b = bites[0];
    const at = (w: number) => ev.evaluate(moderate.warp.length ? songAt(w) : w).hero.blink;
    expect(at(b.w + 0.09)).toBe(true);
    expect(at(b.w + 0.03)).toBe(false);
    expect(at(b.until + 0.1)).toBe(false);
  });
});

/** Song time at world time w in the moderate run. */
function songAt(w: number): number {
  const br = moderate.warp;
  let i = br.length - 1;
  while (i > 0 && br[i].w > w) i--;
  return br[i].s + (w - br[i].w) * (br[i].rate || 0);
}

describe("chickens", () => {
  it("drop from half the orc elites (seeded by the enemy) and two from the boss, where they die", () => {
    const chickens = moderate.chickens;
    const elites = moderate.enemies.filter((e) => e.type === "orc-elite" && e.fates.at(-1)?.lethal);
    // (Not those the finale blast kills: its ring's hits.)
    const blastS = moderate.hits.find((h) => h.source === "ring")?.s ?? Infinity;
    let dropped = 0;
    let considered = 0;
    for (const e of elites) {
      const f = e.fates.at(-1)!;
      if (moderate.hits[f.hit].s >= blastS - 1e-9) continue;
      considered++;
      const drops = new Rng(ctx.seed, "chicken", e.id).next() < CHICKEN_CHANCE;
      const here = chickens.filter((c) => Math.abs(c.w - f.w) < 1e-9 && Math.hypot(c.x - f.x, c.y - f.y) < 1e-6);
      expect(here.length, `orc elite #${e.id}`).toBe(drops ? 1 : 0);
      if (drops) dropped++;
    }
    expect(considered).toBeGreaterThan(10);
    expect(dropped / considered).toBeGreaterThan(0.35);
    expect(dropped / considered).toBeLessThan(0.65);
    const boss = moderate.enemies.find((e) => e.type === "minotaur");
    const bf = boss?.fates.at(-1);
    // (Two, side by side where it fell; the boss's death freezes world time, so others may share its moment.)
    expect(bf?.lethal && moderate.hits[bf.hit].s < blastS).toBe(true);
    const bossChickens = chickens.filter((c) => Math.abs(c.w - bf!.w) < 1e-9 && Math.abs(c.y - bf!.y) < 1e-6 && Math.abs(Math.abs(c.x - bf!.x) - CHICKEN_SPREAD / 2) < 1e-6);
    expect(bossChickens.length).toBe(CHICKEN_BOSS);
  });

  it("heal 30 on the first sixteenth at least 60 ms after the hero touches one, with its sound there", () => {
    // (The moderate player without blades and lightning: hurt often, it walks to the chickens.)
    const rec = new LiveRecording(bare, sch, map, lib, "player", props);
    new Sim(bare, sch, worldFor(), rec, { player: virtualPlayer(bare, sch, "moderate") }).runToEnd();
    rec.refresh();
    const run = rec.plan;
    const eaten = run.chickens.filter((c) => c.sTaken !== undefined);
    expect(eaten.length).toBeGreaterThan(1);
    const sounds = run.events.sounds.filter((x) => x.key === "chicken");
    const max = lib.hero.health.max;
    let full = 0;
    for (const c of eaten) {
      // Touched: the hero within 24 px (a step's slack) when it was eaten.
      const h = heroSample(run, c.wTaken!);
      expect(Math.hypot(h.x - c.x, h.y - c.y)).toBeLessThanOrEqual(CHICKEN_TOUCH + 6);
      expect(c.sHeal).toBeCloseTo(sixteenthAtOrAfter(c.sTaken! + NEVER_LATE), 9);
      expect(sounds.some((x) => Math.abs(x.s - c.sHeal!) < 1e-6 && x.movedFrom === undefined)).toBe(true);
      const heal = run.heals.find((x) => x.source === "chicken" && Math.abs(x.s - c.sHeal!) < 1e-9);
      if (!heal) continue;
      // (Never past the maximum.)
      const before = run.health.v[Array.from(run.health.s).findIndex((s) => s === c.sHeal) - 1] * max;
      // (Health is kept as a 32-bit fraction of the maximum.)
      expect(heal.amount).toBeCloseTo(Math.min(CHICKEN_HEAL, max - before), 3);
      if (heal.amount === CHICKEN_HEAL) full++;
    }
    expect(full).toBeGreaterThan(0);
  });

  it("draw a virtual player below 60% health, and not above", () => {
    // The same moment, a chicken in sight behind where the player is heading: it turns to it only when hurt.
    // (A fresh player each time: its roaming and its noise move on with each choice.)
    const keysFor = (o: Observation) => (virtualPlayer(bare, sch, "moderate") as VirtualPlayer).steerKeys(o);
    const obs = (health: number, chickens?: { x: number; y: number }[]): Observation => ({
      s: 72,
      w: 72,
      hero: { x: 0, y: 0, stepping: false, stride: 44, tier: 2, pickup: 95, health },
      enemies: [],
      threats: [],
      ...(chickens ? { chickens } : {}),
      pacePoint: { x: 0, y: 0, tx: 1, ty: 0 },
    });
    const dir = (keys: Key[]) => ({ x: keys.reduce((a, k) => a + KEY_DIR[k].x, 0), y: keys.reduce((a, k) => a + KEY_DIR[k].y, 0) });
    const heading = dir(keysFor(obs(0.9)));
    const behind = [{ x: -heading.x * 180, y: -heading.y * 180 }];
    const hurt = dir(keysFor(obs(0.5, behind)));
    const healthy = dir(keysFor(obs(0.9, behind)));
    expect(hurt.x * -heading.x + hurt.y * -heading.y).toBeGreaterThan(0);
    expect(healthy.x * -heading.x + healthy.y * -heading.y).toBeLessThanOrEqual(0);
  });

  it("are eaten in a run, and the play log carries every hurt and heal", () => {
    const rec = new LiveRecording(ctx, sch, map, lib, "player", props);
    const sim = new Sim(ctx, sch, worldFor(), rec, { player: virtualPlayer(ctx, sch, "moderate") });
    sim.runToEnd();
    rec.refresh();
    const plan = rec.plan;
    expect(plan.chickens.some((c) => c.sTaken !== undefined)).toBe(true);
    // Each hurt with its 0.5 s of invulnerability, and each heal with its source.
    const log = recordingContext(plan, rec.timeWarp, rec.s).health!;
    expect(log.bites.length).toBe(plan.bites.length);
    expect(log.bites.length).toBeGreaterThan(3);
    expect(log.bites.every((b) => Math.abs(b.invulnerable - INVULNERABLE) < 1e-6 && b.damage > 0 && b.enemy)).toBe(true);
    expect(log.heals.length).toBe(plan.heals.length);
    expect(log.heals.every((h) => ["chicken", "level-up", "max-health"].includes(h.source) && h.amount > 0)).toBe(true);
  });
});
