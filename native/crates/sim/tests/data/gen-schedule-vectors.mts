// The TS batch schedule at ts-freeze on the oracle's core tracks, for sim::schedule's growable port.
// From the repository root (after npm ci), with the Node that made F2's fixtures:
//   npx tsx native/crates/sim/tests/data/gen-schedule-vectors.mts | gzip -n > native/crates/sim/tests/data/schedule-vectors.json.gz
// Every number is written as its f64 bits (16 hex digits), so -0, NaN and Infinity survive.
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { reactiveInput, readStreamFile } from "../../../../../scripts/lib/reactive-node.ts";
import { makeCtx } from "../../../../../src/planner/context.ts";
import { buildSchedule } from "../../../../../src/sim/schedule.ts";

if (process.versions.v8 !== "13.6.233.17-node.49") throw new Error(`V8 ${process.versions.v8}, not F2's`);
const b = new Float64Array(1), u = new BigUint64Array(b.buffer);
const h = (x: number) => { b[0] = x; return u[0].toString(16).padStart(16, "0"); };
const H = (xs: ArrayLike<number>) => Array.from(xs, h);

const FIXTURES = "native/oracle/fixtures";
const core = readdirSync(FIXTURES).filter((t) => readdirSync(join(FIXTURES, t)).some((p) => JSON.parse(readFileSync(join(FIXTURES, t, p, "meta.json"), "utf8")).core));
const tracks = core.map((track) => {
  const { input } = reactiveInput(readStreamFile(track));
  const ctx = makeCtx(input);
  const sc = buildSchedule(ctx);
  const sameSlots = sc.slots.length === sc.gridSlots.length && sc.slots.every((x, i) => x === sc.gridSlots[i]) && sc.grids.length === 1 && sc.grids[0].every((x, i) => x === sc.gridSlots[i]);
  if (!sameSlots) throw new Error(`${track}: the reactive schedule's slots are not its one grid`);
  return {
    track,
    beats: H(input.songMap.beats),
    energy: H(input.songMap.energy),
    sections: ctx.sections.map((x) => H([x.s0, x.s1, x.b0, x.b1])),
    gridSlots: sc.gridSlots.map((x) => [h(x.s), h(x.bi), h(x.bar), h(x.slot), h(x.section), h(x.accent), x.kind, x.accentLabel ?? null]),
    gridTimes: H(sc.gridTimes),
    strikeTimes: H(sc.strikeTimes),
    strideSteps: { s: H(sc.strideSteps.s), period: H(sc.strideSteps.period), per: Array.from(sc.strideSteps.per) },
    baseSteps: sc.baseSteps.map((x) => H([x.s, x.rate])),
    levelUps: sc.levelUps.map((x) => [x.id, h(x.s), h(x.xp)]),
  };
});
process.stdout.write(JSON.stringify({ tag: "ts-freeze", tracks }) + "\n");
