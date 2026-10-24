// The TS sim modules' outputs at ts-freeze (timing, movement, strides, aim, tiers, upgrades, gait),
// for the Rust ports in sim. From the repository root (after npm ci), with the Node that made F2's
// fixtures:
//   npx tsx native/crates/sim/tests/data/gen-sim-vectors.mts | gzip -n > native/crates/sim/tests/data/sim-vectors.json.gz
// Inputs and outputs are written together; every number as its f64 bits (16 hex digits), so -0,
// NaN and Infinity survive.
import { loadContent } from "../../../../../src/content/loader.ts";
import { readContentFiles } from "../../../../../scripts/lib/files.ts";
import { Rng } from "../../../../../src/core/rng.ts";
import * as timing from "../../../../../src/sim/timing.ts";
import * as mv from "../../../../../src/sim/movement.ts";
import * as st from "../../../../../src/sim/strides.ts";
import * as aim from "../../../../../src/sim/aim.ts";
import * as tiers from "../../../../../src/sim/tiers.ts";
import * as up from "../../../../../src/sim/upgrades.ts";
import * as gait from "../../../../../src/sim/gait.ts";

if (process.versions.v8 !== "13.6.233.17-node.49") throw new Error(`V8 ${process.versions.v8}, not F2's`);
const b = new Float64Array(1), u = new BigUint64Array(b.buffer);
const h = (x: number) => { b[0] = x; return u[0].toString(16).padStart(16, "0"); };
const H = (xs: ArrayLike<number>) => Array.from(xs, h);
const opt = (x: number | undefined) => (x === undefined ? null : h(x));

const lib = loadContent(readContentFiles());
const hero = lib.hero;
const r = new Rng(1858, "sim-vectors");
const range = (lo: number, hi: number) => lo + (hi - lo) * r.next();

// ------------------------------------------------------------------ timing
const grid = (n: number, start: number, beat: number, jitter: number) => Array.from({ length: n }, (_, i) => start + i * beat + range(-jitter, jitter));
const grids = [[], [1.5], [0, 0.5], grid(40, 0.3, 0.5, 0.02), grid(30, 2, 0.41, 0), grid(25, 0, 0.75, 0.05)];
const probes = (g: number[]) => [-1, 0, ...g.slice(0, 6), ...g.slice(0, 6).map((x) => x + 0.25), ...(g.length ? [g[g.length - 1] + 3] : []), ...Array.from({ length: 12 }, () => range(-0.5, (g[g.length - 1] ?? 1) + 0.5))];
const rate = [-0.2, -0.031, -0.03, -0.0301, 0, 0.012, 0.03, 0.06, 0.0600001, 0.09, 0.12, 0.1201, 1, -0, NaN].map((d) => {
  const pr = timing.ratePress(hero, d);
  return [h(d), pr.rating ?? null];
});
const nearest = grids.map((g) => ({ grid: H(g), probes: probes(g).map((s) => [h(s), timing.nearestIndex(g, s), timing.firstAtOrAfter(g, s), g.length ? (timing.ratePressOnGrid(hero, s, g).rating ?? null) : null]) }));
const KEYS: mv.InputKey[] = ["up", "down", "left", "right", "space"];
const classify = [0, 1, 2, 3].map((c) => {
  const g = grid(48, 0.5, [0.5, 0.42, 0.6, 0.5][c], c === 2 ? 0.01 : 0);
  const late = [0.035, 0, 0.06, -0.05][c];
  const keyOffset = c === 3 ? 1000 : 0;
  const memo = timing.freshPressMemo();
  const presses: { s: number; partner: boolean; group: timing.KeyGroup }[] = [];
  for (let i = 0; i < g.length - 1; i++) {
    const n = r.next();
    const base = g[i] + late + range(-0.05, 0.05);
    presses.push({ s: base, partner: false, group: n < 0.15 ? "stomp" : "direction" });
    if (n > 0.6) presses.push({ s: base + range(0.01, 0.2), partner: n > 0.9, group: "direction" });
    if (n > 0.8) presses.push({ s: (g[i] + g[i + 1]) / 2 + range(-0.04, 0.04), partner: false, group: "direction" });
  }
  const out = presses.map((p) => {
    const j = timing.classifyPress(hero, g, p.s, p.partner, memo, p.group, (gi) => gi + keyOffset);
    return [j.class, j.rating ?? null, h(j.delta), j.gridIndex, h(j.change), h(j.drift)];
  });
  return { grid: H(g), keyOffset, presses: presses.map((p) => [h(p.s), p.partner, p.group]), out, recent: { direction: H(memo.recent.direction), stomp: H(memo.recent.stomp) } };
});
const tierFor = [];
for (const t of [0, 1, 2, 3, 4]) for (const g of [0, 0.1, 0.29, 0.3, 0.45, 0.5, 0.55, 0.7, 0.75, 0.8, 0.95, 1]) tierFor.push([t, h(g), timing.tierFor(hero, t, g)]);
const drain = [0, 1, 2, 3, 4, 7].map((bpb) => [h(bpb), h(timing.drainPerBeat(hero, bpb))]);
const groove = [[0.5, 0.08], [0.95, 0.08], [0.05, -0.15], [0, 0], [1, -0.04]].map(([g, c]) => H([g, c, timing.grooveAfterChange(g, c)]));

// ------------------------------------------------------------------ movement and strides
const encodeStep = (s: mv.Step) => ({
  w0: h(s.w0), x0: h(s.x0), y0: h(s.y0), dx: h(s.dx), dy: h(s.dy), len1: h(s.len1), ex: h(s.ex), ey: h(s.ey),
  bendE: opt(s.bendE), keys: s.keys, len: opt(s.len), held: s.held ?? null, s0: opt(s.s0),
  press: s.press ? H([s.press.w, s.press.s, s.press.x, s.press.y]) : null,
  rock: s.rock ? H([s.rock.w, s.rock.x, s.rock.y, s.rock.tx, s.rock.ty]) : null,
});
const rule = st.strideRule(hero);
const wall = (lim: number): mv.Clamp => (_x, _y, _dx, _dy, len) => Math.min(len, lim);
const steps: mv.Step[] = [];
const bends = [];
const starts = [];
for (let i = 0; i < 24; i++) {
  const state = { ...st.freshStrides(), budget: range(0, 40), w: range(0, 5), walkFrom: i % 6 === 2 ? range(0, 5) : Infinity };
  const before = [state.budget, state.w, state.walkFrom];
  const key = mv.KEYS[i % 4];
  const dir = mv.KEY_DIR[key];
  const w = state.w + range(0, 1);
  const x = range(-100, 100), y = range(-100, 100);
  const one = i % 5 === 3 ? 30 : 48;
  const s = st.startStride(rule, state, w, x, y, dir, one, { key: i % 7 === 3 ? undefined : key, held: i % 5 === 0 }, i % 3 === 0 ? wall(20) : mv.NO_WALLS);
  starts.push({ before: H(before), w: h(w), x: h(x), y: h(y), dir: H([dir.x, dir.y]), one: h(one), key: i % 7 === 3 ? null : key, held: i % 5 === 0, wall: i % 3 === 0 ? h(20) : null, after: H([state.budget, state.w]), out: encodeStep(s) });
  if (i % 4 === 1) s.press = { w: w - 0.01, s: w + 3, x: s.x0 - 1, y: s.y0 + 2 };
  if (i % 2 === 0) s.s0 = w + 1.5;
  steps.push(s);
  const si = steps.length - 1;
  for (const [ki, k] of mv.KEYS.entries()) {
    const bw = w + range(0, 0.15);
    const dur = i % 2 ? rule.durationS : mv.stepSeconds(hero);
    const walled = ki >= 2;
    const bent = mv.bendStep(hero, s, bw, k, walled ? wall(30) : mv.NO_WALLS, dur);
    bends.push({ step: si, w: h(bw), key: k, wall: walled ? h(30) : null, duration: h(dur), out: bent ? encodeStep(bent) : null });
    if (bent && i % 2 === 0) steps.push(bent);
  }
}
const rocked = { ...steps[2], rock: { w: steps[2].w0 + 0.05, x: steps[2].x0 + 3, y: steps[2].y0 - 1, tx: steps[2].x0 - 2, ty: steps[2].y0 } };
steps.push(rocked);
const poses = [];
for (let i = 0; i < steps.length; i++) for (const dt of [-0.1, 0, 0.02, 0.05, 0.07, 0.11, 0.2, 1]) {
  const p = mv.movePose(steps[i], steps[i].w0 + dt, rule.durationS, rule.hop);
  poses.push([i, h(steps[i].w0 + dt), ...H([p.x, p.y, p.z, p.u])]);
}
const ease = [-0.5, 0, 0.1, 0.5, 0.9, 1, 1.5].map((x) => H([x, mv.easeOut(x)]));
const walk = [0, 1, 31.9, 64, 100.5, 1e5, -10].flatMap((d) => [8, 9].map((f) => H([d, f, mv.walkFrame(hero, d, f)])));
const opposite = mv.KEYS.flatMap((a) => mv.KEYS.map((b2) => [a, b2, mv.opposite(a, b2)]));

const strideGrids = [grid(20, 0.5, 0.5, 0), grid(20, 0, 0.9, 0.05), grid(12, 1, 1.7, 0), [2], []];
const strideSets = strideGrids.map((g) => {
  const ss = st.strideSteps(g, rule.maxPeriodS);
  const ps = [-1, ...Array.from(ss.s.slice(0, 10)), ...Array.from(ss.s.slice(0, 10), (x) => x + 0.05), 100, ...Array.from({ length: 10 }, () => range(-1, (g[g.length - 1] ?? 1) + 1))];
  return { grid: H(g), s: H(ss.s), period: H(ss.period), per: Array.from(ss.per), probes: ps.map((x) => H([x, st.periodAt(ss, x), st.stompCancelsUntil(ss, x)])) };
});
const heldSets: mv.Key[][] = [[], ["up"], ["up", "down"], ["down", "up"], ["left", "up"], ["up", "left", "right"], ["right", "down", "left", "up"], ["left", "left"]];
const held = heldSets.map((ks) => {
  const d = st.heldDirection(ks);
  return [ks, d ? H([d.x, d.y]) : null];
});
const budgets = [];
for (let i = 0; i < 30; i++) {
  const state = { budget: range(0, 50), w: range(0, 3), walkFrom: i % 3 === 0 ? range(0, 4) : Infinity, stompUntil: -Infinity };
  const before = { ...state };
  const w = state.w + range(-0.5, 1);
  const one = [48, 30][i % 2];
  const at = st.budgetAt(rule, state, w, one);
  const stride = steps[i % steps.length];
  const iw = w + 0.03, ix = stride.x0 + range(-5, 5), iy = stride.y0 + range(-5, 5), walkBack = i % 4 === 0 ? 2 : 0;
  const back = st.interrupt(rule, state, stride, iw, ix, iy, one, walkBack);
  budgets.push({ before: H([before.budget, before.w, before.walkFrom]), w: h(w), one: h(one), step: i % steps.length, at: h(at), interrupt: H([iw, ix, iy, walkBack]), back: h(back), after: H([state.budget, state.w]), striding: [st.striding(rule, stride, w), st.striding(rule, undefined, w)] });
}

// ------------------------------------------------------------------ aim
const aims = [];
for (let i = 0; i < 40; i++) {
  const n = i % 7 === 0 ? 0 : Math.floor(range(1, 9));
  const targets = Array.from({ length: n }, () => ({ x: range(-150, 150), y: range(-150, 150), r: range(5, 20), weight: [1, 1, 2, 5][Math.floor(range(0, 4))] }));
  if (i % 9 === 4 && targets.length > 1) targets[1] = { ...targets[0] };
  const reach = { radius: range(40, 120), halfAngle: range(0.3, Math.PI) };
  const hx = range(-20, 20), hy = range(-20, 20), facing = range(-Math.PI, Math.PI);
  const out = aim.aim(hx, hy, facing, targets, reach);
  const arc = aim.swingArc(hx, hy, out, reach);
  aims.push({ hx: h(hx), hy: h(hy), facing: h(facing), targets: targets.map((t) => H([t.x, t.y, t.r, t.weight])), reach: H([reach.radius, reach.halfAngle]), out: h(out), catches: targets.map((t) => aim.catches(arc, t.x, t.y, t.r)) });
}

// ------------------------------------------------------------------ tiers
const tierRows = [-1, 0, 1, 1.5, 2, 3, 4, 9].map((t) => ({
  tier: h(t),
  swing: H(Object.values(tiers.swingArcAt(hero, t))),
  heavy: H(Object.values(tiers.heavyArcAt(hero, t))),
  wave: h(tiers.waveReachAt(hero, t)),
  damage: h(tiers.tierDamage(hero, t)),
  weapons: [...lib.weapons.values()].map((wd) => ({
    id: wd.id,
    orbit: wd.orbit ? H(Object.values(tiers.orbitAt(wd, t))) : null,
    chain: wd.chain ? (tiers.chainAt(wd, t) ? H(Object.values(tiers.chainAt(wd, t)!)) : "none") : null,
    trail: wd.trail || wd.hitShape ? h(tiers.trailRadiusAt(wd, t)) : null,
    evolved: wd.orbit ? H(Object.values(up.evolvedOrbit(wd, tiers.orbitAt(wd, t), t > 1))) : null,
  })),
}));

// ------------------------------------------------------------------ upgrades
const weaponIds = [...lib.weapons.keys()].filter((id) => lib.weapons.get(id)!.kind !== "blast");
const passiveIds = [...lib.passives.keys()];
const loadouts: up.Loadout[] = [
  up.startingLoadout([]),
  up.startingLoadout(["orbiting-blades", "chain-lightning"]),
  { weapons: ["orbiting-blades", "chain-lightning", "fire-trail"], evolved: ["orbiting-blades"], passives: { might: 1 } },
  { weapons: [...weaponIds], evolved: [...weaponIds], passives: { armour: 2, magnet: 1 } },
];
const offerLists = [[], ["fire-trail"], weaponIds];
const offers = [];
for (const seed of [2026, 7, -3.5]) for (const index of [0, 1, 2, 5]) for (let li = 0; li < loadouts.length; li++) for (let oi = 0; oi < offerLists.length; oi++) {
  offers.push({ seed: h(seed), index: h(index), loadout: li, offer: oi, out: up.offersFor(seed, index, lib, offerLists[oi], loadouts[li]).map((x) => [x.kind, x.id]) });
}
const records: up.UpgradeRecord[] = [
  { s: 10, offers: [{ kind: "passive", id: "might" }, { kind: "weapon", id: "fire-trail" }], taken: 1 },
  { s: 20, offers: [{ kind: "passive", id: "might" }], taken: 0, sApply: 25 },
  { s: 30, offers: [{ kind: "evolve", id: "orbiting-blades" }], taken: -1 },
  { s: 40, offers: [{ kind: "passive", id: "armour" }, { kind: "passive", id: "might" }], taken: 1 },
];
const loadoutAt = [0, 10, 15, 24.9, 25, 41].map((s) => {
  const l = up.loadoutAt(["orbiting-blades"], records, s);
  return { s: h(s), weapons: l.weapons, evolved: l.evolved, passives: Object.entries(l.passives).map(([k, v]) => [k, h(v)]), bonus: H(Object.values(up.passiveBonus(lib, l.passives))) };
});
const items: up.UpgradeItem[] = [
  ...weaponIds.map((id) => ({ kind: "weapon" as const, id })),
  ...weaponIds.map((id) => ({ kind: "evolve" as const, id })),
  ...passiveIds.map((id) => ({ kind: "passive" as const, id })),
  { kind: "passive", id: "unknown-passive" },
  { kind: "weapon", id: "unknown-weapon" },
];
const preferred = [];
for (let i = 0; i < 30; i++) {
  const n = 1 + Math.floor(range(0, 3));
  const list = Array.from({ length: n }, () => items[Math.floor(range(0, items.length))]);
  for (const health of [1, 0.6, 0.5, 0.49]) preferred.push({ offers: list.map((x) => [x.kind, x.id]), health: h(health), out: up.preferredOffer(list, health) });
}
const bites = [[0.5, 0], [0.5, 2], [3, 1], [3, 5], [10, 2.5], [1, 1]].map(([a, ar]) => H([a, ar, up.armouredBite(a, ar)]));
const names = items.map((x) => [x.kind, x.id, up.itemName(lib, x)]);

// ------------------------------------------------------------------ gait
const gaits: { every: "eighth" | "beat"; pattern: string; share: number }[] = [
  ...[...lib.enemies.values()].filter((e) => e.gait).map((e) => e.gait!),
  { every: "beat", pattern: "..x.x", share: 0.5 },
];
const gaitRows = gaits.map((g) => ({
  every: g.every, pattern: g.pattern,
  slots: [-1.5, 0, 0.49, 0.5, 1, 2.999999999, 7.25].map((x) => H([x, gait.gaitSlot(g, x)])),
  ks: [-5, -1, 0, 1, 2, 3, 4, 9].map((k) => [k, gait.gaitSteps(g, k), gait.gaitStepAtOrBefore(g, k), gait.gaitStepAfter(g, k), h(gait.gaitBeat(g, k))]),
  ground: h(gait.gaitGround(g)),
  perBeat: h(gait.gaitPerBeat(g)),
}));

process.stdout.write(JSON.stringify({
  tag: "ts-freeze",
  timing: { rate, nearest, classify, tierFor, drain, groove, neverLate: h(timing.NEVER_LATE), keyGroups: KEYS.map((k) => [k, timing.keyGroup(k)]) },
  movement: { steps: steps.map(encodeStep), bends, poses, ease, walk, opposite, stepSeconds: h(mv.stepSeconds(hero)) },
  strides: { rule: H([rule.speed, rule.durationS, rule.hop, rule.maxPeriodS, rule.freePeriodS, rule.stompS]), sets: strideSets, held, budgets, starts },
  aim: aims,
  tiers: tierRows,
  upgrades: { loadouts: loadouts.map((l) => ({ weapons: l.weapons, evolved: l.evolved, passives: Object.entries(l.passives).map(([k, v]) => [k, h(v)]) })), offerLists, offers, loadoutAt, preferred, bites, names },
  gait: gaitRows,
}) + "\n");
