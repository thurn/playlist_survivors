/**
 * The parity report (docs/design/reactive-audio.md, "The Parity Report"): the authored run and the
 * reactive run of a track with reactive-moderate's presses on the track's reference beats, seed
 * 1858, damage off, row by row. Only the Galop has an authored level (levels/orpheus-galop.play.yaml);
 * other tracks with a reference print the reactive column alone.
 *
 *   npm run reactive:parity -- --track orpheus-galop
 */
import { parseArgs } from "node:util";
import { formatParity, parityRows } from "../src/planner/parity.ts";
import { prepare } from "../src/planner/plan.ts";
import { liveRun } from "../src/planner/reactive-checks.ts";
import { reactivePlayerFor } from "../src/reactive/players.ts";
import { plannerInput, propsFromAtlas } from "./lib/plan-node.ts";
import { reactiveInput, readStreamFile } from "./lib/reactive-node.ts";

const { values } = parseArgs({ options: { track: { type: "string", default: "orpheus-galop" }, seed: { type: "string", default: "1858" } } });
const track = values.track!;
const seed = Number(values.seed);
const AUTHORED: Record<string, string> = { "orpheus-galop": "levels/orpheus-galop.play.yaml" };
const checks = { noHeroDamage: true };
const props = propsFromAtlas();

const run = reactiveInput(readStreamFile(track), { seed, checks });
const ref = run.reference;
if (!ref) throw new Error(`${track} has no reference to compare against`);
const player = (input: Parameters<typeof prepare>[0]) => reactivePlayerFor(prepare(input).ctx, ref, "reactive-moderate");
const reactive = liveRun(run.input, props, { player: player(run.input) }).recording.plan;
let authored;
if (AUTHORED[track]) {
  const input = { ...plannerInput({ level: AUTHORED[track], seed }), checks };
  authored = liveRun(input, props, { player: player(input) }).recording.plan;
}
console.log(`Parity report: ${track}, seed ${seed}, reactive-moderate's presses on the ${ref.source} reference, damage off\n`);
console.log(formatParity(parityRows(authored, reactive, ref, run.input.content, run.input.reactive, run.input.level.director?.sectionRule)));
