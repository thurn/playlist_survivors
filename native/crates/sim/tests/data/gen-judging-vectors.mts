// The TS judging without hindsight (reactive mode before stage 2: presses change the groove when
// pressed, and the drain runs on each beat), which no shipped stream exercises: each run's stream
// with its hindsight removed, played by the oracle fixture's keys to the end. From the repository
// root (after npm ci), with the Node that made F2's fixtures:
//   npx tsx native/crates/sim/tests/data/gen-judging-vectors.mts | gzip -n > native/crates/sim/tests/data/judging-vectors.json.gz
import { readFileSync } from "node:fs";
import { gunzipSync } from "node:zlib";
import { reactiveInput, readStreamFile } from "../../../../../scripts/lib/reactive-node.ts";
import { propsFromAtlas } from "../../../../../scripts/lib/plan-node.ts";
import { prepare } from "../../../../../src/planner/plan.ts";
import { LiveRecording } from "../../../../../src/live/recording.ts";
import { Sim, worldFor } from "../../../../../src/sim/simulate.ts";

if (process.versions.v8 !== "13.6.233.17-node.49") throw new Error(`V8 ${process.versions.v8}, not F2's`);
const RUNS: [string, string][] = [["house-128", "reactive-moderate"], ["cc-kimiko-ishizaka-aria", "reactive-moderate"], ["orpheus-galop", "mash4"]];
const pick = (v: Record<string, unknown>) => Object.fromEntries(["s", "kind", "key", "rating", "delta", "class", "extra", "offbeat", "unjudged", "stomp", "rock", "bend", "steps"].filter((k) => k in v).map((k) => [k, v[k]]));
const runs = RUNS.map(([track, profile]) => {
  const { input } = reactiveInput(readStreamFile(track));
  delete input.reactive!.hindsight;
  const { ctx, sch, map } = prepare(input);
  const recording = new LiveRecording(ctx, sch, map, ctx.lib, "player", propsFromAtlas());
  const sim = new Sim(ctx, sch, worldFor(), recording, {});
  const keys = gunzipSync(readFileSync(`native/oracle/fixtures/${track}/${profile}/keys.jsonl.gz`)).toString().trim().split("\n").map((l) => JSON.parse(l));
  for (const k of keys) sim.key(k);
  sim.runToEnd();
  recording.refresh();
  const p = recording.plan;
  return {
    track,
    profile,
    groove: Array.from(p.groove.s, (s, i) => [s, p.groove.v[i]]),
    tier: Array.from(p.tier.s, (s, i) => [s, p.tier.v[i]]),
    inputs: p.inputs.map((x) => pick(x as unknown as Record<string, unknown>)),
    settles: (p.settles ?? []).length,
  };
});
process.stdout.write(JSON.stringify({ tag: "ts-freeze", runs }) + "\n");
