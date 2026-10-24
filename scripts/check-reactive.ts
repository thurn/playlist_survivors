/**
 * Reactive mode's check (docs/design/reactive-audio.md, "Automated Checks"). Gates fail it;
 * targets are printed beside their values, for each stage's bead to record.
 *
 * Gates (stage 1):
 * - playable: the Galop through the live runner for 10 s with synthetic key presses (after a
 *   hand-over at 50 s, the prelude being unjudged), with no errors and with strikes, sounds and
 *   judged presses;
 * - fresh streams: every shipped stream's toolVersion is the stream tool's, and each next committed
 *   beat is known by the time the beat before it arrives;
 * - stream quality: on house-128 and dnb-174, after the first 4 s, 95% of committed beats within
 *   45 ms of a reference beat;
 * - the gate on the Galop: 80% of the prelude's committed beats below the gate;
 * - causality: the perturbation check on causality-mix (compressed director, damage off) with
 *   reactive-moderate's and mash4's presses passes, and every stage 1 feature class (level-ups, zone
 *   waves) fired before a cut; the negative control (the spawn count reading the evidence a
 *   second ahead) fails it;
 * - determinism: the Galop simulated twice with reactive-moderate gives the same plan hash;
 * - sounds: every percussive gameplay sound of causality-mix's and the Galop's live recordings lies
 *   on a committed-beat slot of its subdivision within 1 ms, none on a beat committed below the gate
 *   (the exempt sounds apart);
 * - every shipped track simulates end to end with reactive-moderate and reactive-human at the
 *   level's own difficulty, their runs pass play mode's plan checks that apply to a reactive plan,
 *   and reactive-moderate lives to the end.
 * Gates (stage 2):
 * - timing: no groove change less than 2 s after its press (house-128's and the Galop's
 *   reactive-moderate, house-128's mash8);
 * - on house-128, damage off, over judged time: reactive-moderate at tier 2 or higher 60% of the
 *   time, mash4 and mash8 at tier 0 80% of it. (The readings' gates are unit tests:
 *   tests/reactive-settle.test.ts.)
 * Targets (stage 1): committed-beat accuracy on every track, and the Galop's pulsed sections by section.
 * Targets (stage 2): the players' tiers on dnb-174, pop-arrangement and the Galop; random presses
 * keeping offline "as tracked"; pop-arrangement's pads-only breakdown settling unjudged.
 * Targets (stage 4): the Galop's detected section starts against its song map's section starts.
 *
 *   npm run check:reactive [-- --full]
 *
 * `--full` adds the perturbation check on the Galop and every shipped track, a cut every 10 s.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { CheckResult } from "../src/planner/checks.ts";
import type { PlannerInput } from "../src/planner/context.ts";
import { planWithSim, prepare } from "../src/planner/plan.ts";
import { belowGateShare, beatsWithin, checkDeterminism, checkReactiveCausality, checkReactiveSounds, checkSettleTiming, judgedTierShare, liveRun, smokeTest, type CausalityCase } from "../src/planner/reactive-checks.ts";
import { SETTLE_DELAY } from "../src/reactive/settle.ts";
import { sectionDetection } from "../src/planner/parity.ts";
import { detectedSections } from "../src/reactive/sections.ts";
import { grooveAt } from "../src/plan/evaluate.ts";
import type { Plan } from "../src/plan/types.ts";
import { reactivePlayerFor } from "../src/reactive/players.ts";
import { checkReactivePlan } from "../src/reactive/plan-checks.ts";
import type { ReactiveStream, Reference, StreamFile } from "../src/reactive/stream.ts";
import type { SkillProfileName } from "../src/content/schema.ts";
import { ROOT } from "./lib/files.ts";
import { propsFromAtlas } from "./lib/plan-node.ts";
import { inputForStream, reactiveInput, readStreamFile, shippedTrackIds } from "./lib/reactive-node.ts";

const full = process.argv.includes("--full");
const t0 = performance.now();
const props = propsFromAtlas();
const results: CheckResult[] = [];
const lines: string[] = [];
const pct = (x: number) => `${(100 * x).toFixed(1)}%`;
const CHECKS = { noHeroDamage: true } as const;
const CHECK_LEVEL = "levels/reactive-check.yaml";
const PLAYERS: SkillProfileName[] = ["reactive-moderate", "mash4"];
/** The smoke test hands over to its synthetic keys here: the Galop's opening, where the beat is clear. */
const SMOKE_FROM_S = 50;

const files = new Map<string, StreamFile>();
const file = (id: string) => {
  if (!files.has(id)) files.set(id, readStreamFile(id));
  return files.get(id)!;
};
const tracks = shippedTrackIds();

/** A player for an input, on a reference. */
const playerOn = (ref: Reference, name: SkillProfileName) => (input: PlannerInput) => reactivePlayerFor(prepare(input).ctx, ref, name);

// ---------------------------------------------------------------- fresh streams
{
  const tool = Number(/^TOOL_VERSION = (\d+)/m.exec(readFileSync(join(ROOT, "tools/analysis/src/ps_analysis/stream.py"), "utf8"))?.[1]);
  const failures = [...tracks, "causality-mix"].filter((id) => file(id).params.toolVersion !== tool).map((id) => `${id}: toolVersion ${file(id).params.toolVersion}, the tool's is ${tool} (npm run reactive:streams)`);
  results.push({ name: "fresh streams", failures, checked: tracks.length + 1 });
  // The simulation reads the length of the beat it is in (the next committed beat) wherever it
  // interpolates beats: each next beat must be committed by the time the beat before it arrives.
  const late: string[] = [];
  for (const id of [...tracks, "causality-mix"]) {
    const b = file(id).beats;
    for (let i = 0; i + 1 < b.length; i++) if (b[i + 1].knownAt > b[i].t + 1e-9) late.push(`${id}: the beat at ${b[i + 1].t.toFixed(3)} s is committed at ${b[i + 1].knownAt.toFixed(2)} s, after the beat before it (${b[i].t.toFixed(3)} s)`);
  }
  results.push({ name: "each next beat committed by the beat before it", failures: late, checked: tracks.length + 1 });
}

// ---------------------------------------------------------------- stream quality and the Galop's gate
const streamOf = (id: string): ReactiveStream => reactiveInput(file(id)).stream;
{
  const failures: string[] = [];
  for (const id of ["house-128", "dnb-174"]) {
    const q = beatsWithin(streamOf(id), file(id).reference!, 0.045, 4);
    lines.push(`stream quality ${id}: ${pct(q.share)} of ${q.n} committed beats within 45 ms after 4 s (gate 95%)`);
    if (q.share < 0.95) failures.push(`${id}: ${pct(q.share)} of committed beats within 45 ms of a reference beat, under 95%`);
  }
  results.push({ name: "stream quality", failures, checked: 2 });
  const galop = file("orpheus-galop");
  const prelude = galop.reference!.sections!.find((s) => s.label === "prelude")!;
  const g = belowGateShare(streamOf("orpheus-galop"), prelude.s0, prelude.s1);
  lines.push(`the Galop's prelude: ${pct(g.share)} of ${g.n} committed beats below the gate (gate 80%)`);
  results.push({ name: "the gate on the Galop's prelude", failures: g.share >= 0.8 ? [] : [`${pct(g.share)} of the prelude's committed beats below the gate, under 80%`], checked: 1 });
}

// ---------------------------------------------------------------- targets: committed-beat accuracy
{
  lines.push("target: committed-beat accuracy after 4 s (within 45 ms of a reference beat; the experiment's Galop: 84% of pulsed beats at a 0.25 s lead)");
  for (const id of [...tracks, "causality-mix"]) {
    const ref = file(id).reference!;
    const q = beatsWithin(streamOf(id), ref, 0.045, 4);
    lines.push(`  ${id.padEnd(22)} ${pct(q.share).padStart(6)} of ${q.n} (${ref.source} reference)`);
  }
  const galop = file("orpheus-galop");
  const secs = galop.reference!.sections!;
  const st = streamOf("orpheus-galop");
  let n = 0;
  let hit = 0;
  const per: string[] = [];
  for (const s of secs) {
    const q = beatsWithin(st, galop.reference!, 0.045, 0, [s.s0, s.s1]);
    per.push(`${s.label} ${pct(q.share)}`);
    if (s.feel !== "free") {
      n += q.n;
      hit += q.share * q.n;
    }
  }
  lines.push(`  the Galop's pulsed sections: ${pct(hit / Math.max(1, n))} of ${n} (the horizon fallback's trigger is 79%)`);
  lines.push(`  by section: ${per.join(", ")}`);
}

// ---------------------------------------------------------------- causality
const causalityCase = (id: string, level: string | undefined, cuts?: number[]): CausalityCase => {
  const f = file(id);
  const opts = { level, checks: CHECKS };
  const { stream, reference } = reactiveInput(f, opts);
  return {
    track: id,
    stream,
    inputFor: (s) => inputForStream(s, { ...opts, knownLength: stream.audio.duration }),
    props,
    players: PLAYERS.map((name) => ({ name, player: playerOn(reference!, name) })),
    ...(cuts ? { cuts } : {}),
  };
};
{
  const r = checkReactiveCausality(causalityCase("causality-mix", CHECK_LEVEL));
  results.push(r);
  lines.push(`causality on causality-mix: ${r.checked} cuts, ${r.failures.length} failing, features fired: ${r.fired.join(", ")} (${(r.ms / 1000).toFixed(1)} s)`);
  // The negative control: a simulation that reads the evidence a second ahead must fail.
  const leak = checkReactiveCausality({ ...causalityCase("causality-mix", CHECK_LEVEL), inputFor: (s) => inputForStream(s, { level: CHECK_LEVEL, checks: { ...CHECKS, leakFutureEvidence: true }, knownLength: file("causality-mix").audio.duration }), stopAtFirst: true });
  const caught = leak.failures.find((f) => f.includes("cut at"));
  lines.push(`negative control: ${caught ? `caught (${caught})` : "NOT caught"}`);
  results.push({ name: "the negative control fails", failures: caught ? [] : ["the causality check passed a simulation that reads the evidence a second ahead"], checked: 1 });
  if (full) {
    for (const id of tracks) {
      const d = file(id).audio.duration;
      const cuts = Array.from({ length: Math.floor((d - 1) / 10) }, (_, k) => 10 * (k + 1));
      const rf = checkReactiveCausality(causalityCase(id, undefined, cuts));
      // (The default director: level-ups and zone waves need not fire before a 10 s cut.)
      const failures = rf.failures.filter((f) => !f.startsWith("no "));
      results.push({ ...rf, name: `causality --full (${id})`, failures });
      lines.push(`causality --full on ${id}: ${rf.checked} cuts, ${failures.length} failing (${(rf.ms / 1000).toFixed(1)} s)`);
    }
  }
}

// ---------------------------------------------------------------- stage 2: the settled groove
{
  type Run = { plan: Plan; stream: ReactiveStream };
  const runs = new Map<string, Run>();
  const run = (id: string, name: SkillProfileName): Run => {
    const key = `${id} ${name}`;
    if (!runs.has(key)) {
      const r = reactiveInput(file(id), { checks: CHECKS });
      runs.set(key, { plan: planWithSim(r.input, { props, player: playerOn(r.reference!, name)(r.input) }).plan, stream: r.stream });
    }
    return runs.get(key)!;
  };
  const tier2 = (id: string, name: SkillProfileName) => judgedTierShare(run(id, name).plan, run(id, name).stream, (t) => t >= 2);
  const tier0 = (id: string, name: SkillProfileName) => judgedTierShare(run(id, name).plan, run(id, name).stream, (t) => t === 0);
  // Gates on house-128.
  const failures: string[] = [];
  const m = tier2("house-128", "reactive-moderate");
  if (m < 0.6) failures.push(`house-128: reactive-moderate at tier 2 or higher for ${pct(m)} of judged time, under 60%`);
  for (const name of ["mash4", "mash8"] as const) {
    const z = tier0("house-128", name);
    if (z < 0.8) failures.push(`house-128: ${name} at tier 0 for ${pct(z)} of judged time, under 80%`);
  }
  lines.push(`the settled groove on house-128 (damage off, judged time): reactive-moderate at tier 2+ ${pct(m)} (gate 60%), mash4 at tier 0 ${pct(tier0("house-128", "mash4"))}, mash8 ${pct(tier0("house-128", "mash8"))} (gate 80%)`);
  results.push({ name: "the settled groove on house-128", failures, checked: 3 });
  for (const [id, name] of [["house-128", "reactive-moderate"], ["orpheus-galop", "reactive-moderate"], ["house-128", "mash8"]] as const) {
    const r = checkSettleTiming(run(id, name).plan, run(id, name).stream);
    results.push({ ...r, name: `${r.name} (${id}, ${name})` });
  }
  // Targets.
  lines.push("targets (stage 2, damage off, shares of judged time):");
  const want: [SkillProfileName, "2+" | "0", number][] = [
    ["reactive-moderate", "2+", 0.6],
    ["reactive-human", "2+", 0.5],
    ["reactive-offbeat", "2+", 0.5],
    ["reactive-half", "2+", 0.4],
    ["mash4", "0", 0.8],
    ["mash8", "0", 0.8],
  ];
  for (const id of ["dnb-174", "pop-arrangement"]) {
    const cells = want.map(([name, at, target]) => {
      const v = at === "2+" ? tier2(id, name) : tier0(id, name);
      return `${name} tier ${at} ${pct(v)} (${pct(target)}${v >= target ? "" : ", missed"})`;
    });
    lines.push(`  ${id}: ${cells.join(", ")}`);
  }
  const gm = tier2("orpheus-galop", "reactive-moderate");
  lines.push(`  orpheus-galop: reactive-moderate tier 2+ ${pct(gm)} (40%${gm >= 0.4 ? "" : ", missed"}), mash4 tier 0 ${pct(tier0("orpheus-galop", "mash4"))}, mash8 tier 0 ${pct(tier0("orpheus-galop", "mash8"))} (80%)`);
  const kept: string[] = [];
  for (const name of ["mash4", "mash8"] as const) {
    let n = 0;
    let k = 0;
    let unswitched = 0;
    for (const id of ["house-128", "dnb-174", "pop-arrangement", "orpheus-galop"]) {
      for (const x of run(id, name).plan.settles ?? []) {
        n++;
        if (x.reading === "offline as tracked") k++;
        if (!x.switched) unswitched++;
      }
    }
    kept.push(`${name} ${pct(k / Math.max(1, n))} (presses switched the reading in ${n - unswitched} of ${n})`);
  }
  // (The incumbent follows its beats, not its label: when an offline re-decode moves, the same beats
  // may hold under the online list's reading, which no press chose.)
  lines.push(`  random presses keep offline "as tracked" in: ${kept.join(", ")} of settles (90%)`);
  const pop = run("pop-arrangement", "reactive-moderate");
  const brk = file("pop-arrangement").reference!.sections!.find((x) => x.label === "breakdown-pads-only")!;
  const inBrk = (pop.plan.settles ?? []).filter((x) => x.p >= brk.s0 && x.p < brk.s1);
  const unj = inBrk.filter((x) => x.settled === "unjudged").length / Math.max(1, inBrk.length);
  const g0 = grooveAt(pop.plan.groove, brk.s0 + SETTLE_DELAY);
  const g1 = grooveAt(pop.plan.groove, brk.s1 + SETTLE_DELAY);
  lines.push(`  pop-arrangement's pads-only breakdown: ${pct(unj)} of reactive-moderate's ${inBrk.length} presses settle unjudged (80%); the settled groove ${g0.toFixed(2)} at its start, ${g1.toFixed(2)} at its end (at least ${(g0 - 0.1).toFixed(2)})`);
}

// ---------------------------------------------------------------- targets: structure (stage 4)
{
  const galop = file("orpheus-galop");
  const d = sectionDetection(streamOf("orpheus-galop"), galop.reference!);
  lines.push("targets (stage 4, the Galop's detected section starts):");
  lines.push(`  within 8 s after ${d.found} of the ${d.of} song-map section starts from the vamp to the coda, matched one to one (at least 4${d.found >= 4 ? "" : ", missed"})`);
  lines.push(`  ${d.falseStarts} in the galop more than 8 s from every song-map section start (at most 3${d.falseStarts <= 3 ? "" : ", missed"}), ${d.free} in the prelude (at most 1${d.free <= 1 ? "" : ", missed"})`);
  lines.push(`  at ${d.times.map((t) => t.toFixed(1)).join(", ")} s; song-map starts at ${galop.reference!.sections!.slice(1).map((x) => x.s0.toFixed(1)).join(", ")} s`);
  lines.push("structure per track (changes, jumps, candidate endings, detected section starts):");
  for (const id of [...tracks, "causality-mix"]) {
    const st = streamOf(id);
    const x = st.structure!;
    lines.push(`  ${id.padEnd(22)} ${x.changes.length} changes, ${x.jumps.length} jumps, ${x.ends.length ? `ends at ${x.ends.map((e) => e.t.toFixed(1)).join(", ")} s` : "no ending"}, ${detectedSections(st).length} section starts`);
  }
}

// ---------------------------------------------------------------- determinism, sounds and every track end to end
{
  const galop = reactiveInput(file("orpheus-galop"), { checks: CHECKS });
  results.push(checkDeterminism(galop.input, () => playerOn(galop.reference!, "reactive-moderate")(galop.input), props));
  // Playable: the Galop through the live runner, handed over at the galop's opening (the prelude is unjudged).
  const play = reactiveInput(file("orpheus-galop"));
  results.push(smokeTest(play.input, props, (ctx) => reactivePlayerFor(ctx, play.reference!, "reactive-moderate"), SMOKE_FROM_S));
  const soundFailures: CheckResult[] = [];
  for (const [id, level] of [["causality-mix", CHECK_LEVEL], ["orpheus-galop", undefined]] as const) {
    const run = reactiveInput(file(id), { level, checks: CHECKS });
    const rec = liveRun(run.input, props, { player: playerOn(run.reference!, "reactive-moderate")(run.input) }).recording;
    const r = checkReactiveSounds(rec.plan, run.stream, run.input.content);
    soundFailures.push({ ...r, name: `${r.name} (${id})` });
  }
  results.push(...soundFailures);
  // Every shipped track end to end as it plays: the level's own difficulty (damage on), the honest
  // players' runs (as ?player= plans them) passing play mode's plan checks that apply, and
  // reactive-moderate living to the end.
  const failures: string[] = [];
  const planFailures: string[] = [];
  const survival: string[] = [];
  lines.push("every shipped track end to end (damage on):");
  for (const id of tracks) {
    const cells: string[] = [];
    for (const name of ["reactive-moderate", "reactive-human"] as const) {
      const run = reactiveInput(file(id));
      try {
        const { plan } = planWithSim(run.input, { props, player: playerOn(run.reference!, name)(run.input) });
        const presses = plan.inputs.filter((i) => i.kind === "press");
        const unjudged = presses.filter((i) => i.unjudged).length;
        const health = plan.stats.deathS === undefined ? `${Math.round(plan.stats.minHealth * 100)}% lowest health` : `fell at ${plan.stats.deathS.toFixed(0)} s`;
        cells.push(`${name}: ${plan.hud.kills.length} kills, ${presses.length} presses (${unjudged} unjudged), groove ${plan.stats.averageGroove.toFixed(2)}, ${health}`);
        if (plan.inputs.length === 0 || plan.events.sounds.length === 0) failures.push(`${id}, ${name}: no presses or no sounds`);
        for (const c of checkReactivePlan(plan, run.input.content)) for (const f of c.failures.slice(0, 2)) planFailures.push(`${id}, ${name}: ${c.name}: ${f}`);
        if (name === "reactive-moderate" && plan.stats.deathS !== undefined) survival.push(`${id}: reactive-moderate fell at ${plan.stats.deathS.toFixed(1)} s`);
      } catch (e) {
        failures.push(`${id}, ${name}: ${e instanceof Error ? e.message : String(e)}`);
      }
    }
    lines.push(`  ${id.padEnd(22)} ${cells.join(" | ")}`);
  }
  results.push({ name: "every shipped track simulates end to end", failures, checked: 2 * tracks.length });
  results.push({ name: "the honest players' runs pass the plan checks", failures: planFailures, checked: 2 * tracks.length });
  results.push({ name: "reactive-moderate lives to the end of every shipped track", failures: survival, checked: tracks.length });
}

for (const l of lines) console.log(l);
let failed = false;
for (const r of results) {
  const ok = r.failures.length === 0;
  if (!ok) failed = true;
  console.log(`${ok ? "ok  " : "FAIL"} ${r.name} (${r.checked} checked)`);
  for (const f of r.failures.slice(0, 10)) console.log(`       ${f}`);
  if (r.failures.length > 10) console.log(`       … and ${r.failures.length - 10} more`);
}
console.log(`check:reactive ${failed ? "FAILED" : "passed"} in ${((performance.now() - t0) / 1000).toFixed(1)} s`);
process.exit(failed ? 1 : 0);
