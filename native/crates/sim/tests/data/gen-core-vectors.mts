// The TS core's outputs at ts-freeze, for the Rust ports in sim::core. From the repository root
// (after npm ci), with the Node that made F2's fixtures:
//   npx tsx native/crates/sim/tests/data/gen-core-vectors.mts > native/crates/sim/tests/data/core-vectors.json
// Every number is written as its f64 bits (16 hex digits), so -0 and NaN survive.
import { readFileSync } from "node:fs";
import { hash32, Rng, rand01, noise1, noise2 } from "../../../../../src/core/rng.ts";
import { Warp } from "../../../../../src/core/warp.ts";
import * as g from "../../../../../src/core/geometry.ts";
import * as sm from "../../../../../src/core/songmap.ts";
import * as sh from "../../../../../src/core/shaping.ts";

if (process.versions.v8 !== "13.6.233.17-node.49") throw new Error(`V8 ${process.versions.v8}, not F2's`);
const b = new Float64Array(1), u = new BigUint64Array(b.buffer);
const h = (x: number) => { b[0] = x; return u[0].toString(16).padStart(16, "0"); };
const H = (xs: number[]) => xs.map(h);

const parts: (number | string)[][] = [
  [], [0], [1], [-1], [42.9], [-42.9], [2 ** 31], [2 ** 32 + 5], [-(2 ** 33)], [NaN], [Infinity],
  ["level"], ["é"], ["😀"], ["a", 1, "b", 2], [7, "reactive", 3.5], ["", ""], ["mash4", 1858],
];
const seeds: (number | string)[][] = [[42], ["level", 7], ["😀", -3.5], [1858, "reactive-moderate"], []];
const rngs = seeds.map((seed) => {
  const r = new Rng(...seed);
  const next = Array.from({ length: 40 }, () => r.next());
  const ops = [r.range(-5, 7), r.int(3, 9), r.weighted([0, 1, 2], [0.2, 0.5, 0.3]), r.roundStochastic(2.4), r.signed(), r.pick([10, 20, 30, 40])];
  return { seed, next: H(next), ops: H(ops) };
});
const noise = [];
for (const seed of [0, 7, 1858]) for (const x of [-3.7, -0.5, 0, 0.25, 1, 2.5, 17.3]) {
  noise.push({ seed, x, rand01: h(rand01(seed, Math.floor(x * 10), 3)), noise1: h(noise1(seed, x)), noise2: h(noise2(seed, x, x * 0.7 - 1)) });
}
const shaping = [-0.2, 0, 0.1, 0.25, 0.5, 0.99, 1.7].map((p) => ({ p, out: H([sh.beatHit(p), sh.beatSwell(p), sh.amount(2, p), sh.smoothstep(p), sh.frac(p * 3.3), sh.lerp(1, 5, p), sh.clamp(p, 0, 1)]) }));
const stepSets = [
  [{ s: 0, rate: 1 }],
  [{ s: 0, rate: 1 }, { s: 2, rate: 0 }, { s: 2.1, rate: 1 }, { s: 5, rate: 0.5 }, { s: 5, rate: 0.6 }, { s: 9, rate: 0.6 }, { s: 12, rate: 1 }],
  [{ s: 3, rate: 0 }, { s: 1, rate: 0.75 }, { s: 3.2, rate: 1 }],
];
const warps = stepSets.map((steps) => {
  const w = Warp.fromRates(steps);
  const ss = [-2, 0, 0.5, 1, 2, 2.05, 2.1, 3, 3.1, 4.99, 5, 7, 11, 12, 20];
  return {
    steps: steps.map((st) => [h(st.s), h(st.rate)]),
    breaks: w.breaks.map((br) => H([br.s, br.w, br.rate])),
    s: H(ss),
    toWorld: H(ss.map((s) => w.toWorld(s))),
    rateAt: H(ss.map((s) => w.rateAt(s))),
    toSong: H(ss.map((s) => w.toSong(s))),
    hitStops: w.hitStops().map(H),
  };
});
const shapes: g.Shape[] = [
  { kind: "circle", x: 10, y: -5, radius: 30 },
  { kind: "ring", x: 0, y: 0, inner: 20, outer: 60 },
  { kind: "capsule", x0: -40, y0: 10, x1: 50, y1: -20, radius: 12 },
  { kind: "capsule", x0: 5, y0: 5, x1: 5, y1: 5, radius: 9 },
  { kind: "arc", x: 0, y: 0, radius: 80, dir: 0.7, halfAngle: 0.9 },
  { kind: "arc", x: 3, y: -2, radius: 120, dir: -2.8, halfAngle: Math.PI, inner: 40 },
];
const points: [number, number][] = [];
for (let i = 0; i < 40; i++) points.push([Math.cos(i * 1.7) * (i * 3.1), Math.sin(i * 2.3) * (i * 2.9)]);
points.push([0, 0], [10, -5], [80, 0]);
const geometry = shapes.map((s) => {
  const r = new Rng("shape", s.kind);
  const draw = () => r.next();
  return {
    shape: s,
    area: h(g.shapeArea(s)),
    centre: H([g.shapeCentre(s).x, g.shapeCentre(s).y]),
    bounds: H(g.shapeBounds(s)),
    contains: points.map(([x, y]) => [g.shapeContains(s, x, y), g.shapeContains(s, x, y, 5)]),
    pushOut: points.map(([x, y]) => { const v = g.pushOutDir(s, x, y); return H([v.x, v.y]); }),
    nearest: points.map(([x, y]) => { const v = g.nearestInside(s, x, y); return H([v.x, v.y]); }),
    samples: Array.from({ length: 12 }, (_, i) => { const v = g.sampleInShape(s, draw, i % 3 === 0 ? 0 : 4); return H([v.x, v.y]); }),
    moved: g.moveShape(s, 3.5, -1.25),
  };
});
const wrap = [-10, -Math.PI, -1, 0, -0, 1, Math.PI, 4, 100].map((a) => [h(a), h(g.wrapAngle(a))]);
const map: sm.SongMap = JSON.parse(readFileSync("songs/orpheus-galop/songmap.json", "utf8"));
const idx = sm.indexSong(map);
const times = [-1, 0, 0.1, 3, 10.5, 43.21, 60, 100.65, 144.386, 150, 200];
const songmap = {
  barStart: H(idx.barStart), beatsPerBar: H(idx.beatsPerBar), barCount: idx.barCount,
  times: H(times),
  beatIndexAt: H(times.map((s) => sm.beatIndexAt(map, s))),
  energyAt: H(times.map((s) => sm.energyAt(map, s))),
  sectionAtTime: times.map((s) => sm.sectionAtTime(idx, s).label),
  beatIndices: H([-3, -0.5, 0, 0.25, 7, 100.5, 300, 431.2, 500]),
  timeOfBeatIndex: H([-3, -0.5, 0, 0.25, 7, 100.5, 300, 431.2, 500].map((i) => sm.timeOfBeatIndex(map, i))),
  barOfBeatIndex: H([-3, 0, 1, 2.999999, 100.5, 300, 600].map((i) => sm.barOfBeatIndex(idx, i))),
  timeOf: [[-1, 1], [0, 2], [1, 1], [1, 2.5], [30, 1], [177, 2], [178, 1], [200, 1.5]].map(([bar, beat]) => h(sm.timeOf(idx, { bar, beat }))),
  sectionTimes: map.sections.map((sec) => H(sm.sectionTimes(idx, sec))),
  sectionAtBar: [0, 1, 14, 15, 108, 177, 300].map((bar) => sm.sectionAtBar(map, bar)?.label ?? null),
};
const hashes = parts.map((p) => ({ parts: p.map((x) => (typeof x === "string" ? { s: x } : { n: h(x) })), out: h(hash32(...p)) }));
process.stdout.write(JSON.stringify({ hashes, rngs, noise, shaping, warps, geometry, wrap, songmap }) + "\n");
