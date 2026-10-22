/**
 * The horde's set pieces (play mode's setPieces; "The Horde" in docs/design/gameplay-redesign.md), on
 * the real song: a closing ring around where the hero stood, closing to 100 px on its accent, and a
 * stampede running across the view; and the steady stream held to 60% of the population curve.
 */
import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas, runPlanner } from "../scripts/lib/plan-node.ts";
import { plan as planOf } from "../src/planner/plan.ts";
import { Warp } from "../src/core/warp.ts";
import { enemyPos, heroSample } from "../src/plan/sample.ts";
import { POP_CAP, RING_CLOSE, STAMPEDE_SPEED } from "../src/sim/simulate.ts";

const PLAY = "levels/orpheus-galop.play.yaml";
const lib = plannerInput({ level: PLAY }).content;
// (The holder kills little, so the set pieces keep most of their members.)
const plan = runPlanner({ level: PLAY, profile: "holder" });
const warp = new Warp(plan.warp);
const byId = new Map(plan.enemies.map((e) => [e.id, e]));
const tmp = { x: 0, y: 0 };
const at = (id: number, w: number) => {
  enemyPos(plan, byId.get(id)!, w, tmp);
  return { x: tmp.x, y: tmp.y };
};

describe("a closing ring", () => {
  const ring = plan.formations.find((f) => f.def === "ring" && f.members.length === 30)!;
  const w0 = byId.get(ring.members[0])!.spawnW;
  const centre = heroSample(plan, w0);

  it("spawns its 30 evenly spaced on a circle just outside the view, around the hero, with three gaps of three places", () => {
    expect(ring.members.length).toBe(30);
    const pos = ring.members.map((id) => at(id, w0));
    const radii = pos.map((p) => Math.hypot(p.x - centre.x, p.y - centre.y));
    for (const r of radii) expect(r).toBeCloseTo(radii[0], 0);
    // Just outside the view: further than its corners from the hero (a 16:9 view of at least 600 px).
    expect(radii[0]).toBeGreaterThan(Math.hypot(300, 170));
    // 39 places (30 members and 3 gaps of 3): 27 neighbours one place apart, and 3 four places apart.
    const step = (Math.PI * 2) / 39;
    const ang = pos.map((p) => Math.atan2(p.y - centre.y, p.x - centre.x)).sort((a, b) => a - b);
    const gaps = ang.map((a, k) => (k + 1 < ang.length ? ang[k + 1] - a : ang[0] + Math.PI * 2 - a));
    expect(gaps.filter((g) => Math.abs(g - step) < 1e-3).length).toBe(27);
    expect(gaps.filter((g) => Math.abs(g - 4 * step) < 1e-3).length).toBe(3);
  });

  it("closes at a steady speed to 100 px from its centre on its accent, then chases", () => {
    const w1 = warp.toWorld(ring.sRelease);
    const alive = ring.members.filter((id) => byId.get(id)!.deathW > w1 + 0.01);
    expect(alive.length).toBeGreaterThan(5);
    for (const id of alive) {
      const p = at(id, w1);
      expect(Math.hypot(p.x - centre.x, p.y - centre.y)).toBeCloseTo(RING_CLOSE, -1);
    }
    // Half way (in song time) it is half way.
    const sMid = (warp.toSong(w0) + ring.sRelease) / 2;
    const r0 = Math.hypot(at(alive[0], w0).x - centre.x, at(alive[0], w0).y - centre.y);
    const pm = at(alive[0], warp.toWorld(sMid));
    expect(Math.hypot(pm.x - centre.x, pm.y - centre.y)).toBeCloseTo((r0 + RING_CLOSE) / 2, -1);
  });

  it("forms two rows when it has more than 40", () => {
    const big = plan.formations.find((f) => f.def === "ring" && f.members.length === 60)!;
    const w = byId.get(big.members[0])!.spawnW;
    const c = heroSample(plan, w);
    // (Two rows 32 px apart; the hero's sample at the spawn is a few px from the ring's centre.)
    const radii = big.members.map((id) => Math.hypot(at(id, w).x - c.x, at(id, w).y - c.y)).sort((a, b) => a - b);
    const jumps = radii.slice(1).map((r, k) => r - radii[k]).filter((d) => d > 15);
    expect(jumps.length).toBe(1);
    expect(jumps[0]).toBeGreaterThan(20);
  });
});

describe("a stampede", () => {
  const sp = plan.formations.find((f) => f.def === "stampede" && f.members.length === 40)!;
  const w0 = byId.get(sp.members[0])!.spawnW;

  it("runs straight across the view at 2.5 times its enemies' speed, ignoring the hero", () => {
    const speed = lib.enemies.get("goblin")!.speed * STAMPEDE_SPEED;
    const moving = sp.members.filter((id) => byId.get(id)!.deathW > w0 + 0.5);
    expect(moving.length).toBeGreaterThan(20);
    for (const id of moving) {
      const a = at(id, w0 + 0.1);
      const b = at(id, w0 + 0.5);
      expect(Math.abs(b.x - a.x)).toBeCloseTo(speed * 0.4, -1);
      expect(b.y - a.y).toBeCloseTo(0, 0);
    }
    // Two rows with a gap at the same height in each.
    const xs = new Set(sp.members.map((id) => Math.round(at(id, w0).x)));
    expect(xs.size).toBe(2);
  });

  it("recycles the runners that leave the far side", () => {
    const recycled = sp.members.filter((id) => byId.get(id)!.recycled);
    expect(recycled.length).toBeGreaterThan(10);
    for (const id of recycled) expect(byId.get(id)!.deathW).toBeLessThan(w0 + 8);
  });
});

describe("the steady stream", () => {
  it("is thinner: about 60% of the population curve between set pieces", () => {
    // (The population check holds it to 60% within 25% from the fourth bar of each section.)
    const theme = plan.sections.find((x) => x.label === "theme-a")!;
    const w = warp.toWorld(theme.s0 + 8);
    const stream = plan.enemies.filter((e) => e.formation === undefined && e.spawnW <= w && w < e.deathW).length;
    const target = theme.population[0] + (theme.population[1] - theme.population[0]) * (8 / (theme.s1 - theme.s0));
    expect(stream / target).toBeGreaterThan(0.45);
    expect(stream / target).toBeLessThan(0.75);
  });
});

describe("the population cap", () => {
  it("holds when a set piece is bigger than the room left: the stream makes way, the ring takes what fits, and so does all that follows", () => {
    const input = plannerInput({ level: PLAY });
    const ev = input.level.events.find((e) => e.ring)!;
    ev.ring!.count = POP_CAP;
    const p = planOf(input, { props: propsFromAtlas(), profile: "moderate" });
    // (Through the whole song: the formations and the boss that come after a full ring make room too.)
    const ends = p.enemies.flatMap((e) => [[e.spawnW, 1], [e.deathW, -1]] as const).filter(([w]) => Number.isFinite(w)).sort((a, b) => a[0] - b[0] || a[1] - b[1]);
    let n = 0;
    let most = 0;
    for (const [, d] of ends) most = Math.max(most, (n += d));
    expect(most).toBeLessThanOrEqual(POP_CAP);
    expect(p.formations.find((f) => f.def === "ring")!.members.length).toBeGreaterThan(200);
  });

  it("holds when a formation arrives while a full ring still closes", () => {
    // A ring of 520 spawning two beats before the first bee wedge and closing twelve beats after it.
    const input = plannerInput({ level: PLAY });
    const ev = input.level.events.find((e) => e.ring)!;
    ev.at = { section: "theme-a-repeat", point: "start", offsetBeats: 2 };
    ev.ring!.count = POP_CAP;
    ev.ring!.closesOn = { section: "theme-a-repeat", point: "start", offsetBeats: 16 };
    const p = planOf(input, { props: propsFromAtlas(), profile: "holder" });
    const ends = p.enemies.flatMap((e) => [[e.spawnW, 1], [e.deathW, -1]] as const).filter(([w]) => Number.isFinite(w)).sort((a, b) => a[0] - b[0] || a[1] - b[1]);
    let n = 0;
    let most = 0;
    for (const [, d] of ends) most = Math.max(most, (n += d));
    expect(most).toBeLessThanOrEqual(POP_CAP);
    expect(p.formations.some((f) => f.def === "bee-wedge")).toBe(true);
  });
});
