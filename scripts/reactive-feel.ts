/**
 * The reactive feel rounds (docs/design/reactive-feel.md, "Raters", "Rounds", "Held-out Protocol"):
 *
 *   npm run reactive:feel -- batches --round <n> [--size 5]       the round's rater batches (the anchor in the first)
 *   npm run reactive:feel -- brief --round <n> --batch <k>        a rater's prompt for batch k
 *   npm run reactive:feel -- batches --milestone <m>              a milestone's held-out batches (no anchor)
 *   npm run reactive:feel -- brief --milestone <m> --batch <k>    a milestone rater's prompt
 *   npm run reactive:feel -- brief --round <n> --second <name> --tracks <id>,...   a second rater's prompt
 *   npm run reactive:feel -- aggregate --round <n>                 docs/feel/round-<n>.json from the batches' scores
 *   npm run reactive:feel -- milestone --milestone <m> --round <n> docs/feel/milestone-<m>.json: numbers only
 *   npm run reactive:feel -- overfit --milestone <m>               the overfitting rule against milestone 0
 *
 * Raters write captures/feel/round-<n>/batch-<k>/scores.json (a second rater
 * second-<name>/scores.json); milestone raters write into
 * captures/heldout/milestone-<m>/. The milestone and overfit commands print
 * only numbers, and nothing of a held-out track by name.
 */
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, relative } from "node:path";
import { parseArgs } from "node:util";
import type { TrackMetrics } from "./lib/feel.ts";
import { aggregate, ANCHOR, batches, calibrationLow, DIMENSIONS, milestoneNumbers, overfitting, parseScores, type MilestoneNumbers, type RatedBatch, type RoundSummary } from "./lib/feel-rounds.ts";
import { ROOT } from "./lib/files.ts";
import { corpusTrackIds } from "./lib/reactive-node.ts";

const [command, ...rest] = process.argv.slice(2);
const { values } = parseArgs({
  args: rest,
  options: { round: { type: "string" }, batch: { type: "string" }, second: { type: "string" }, tracks: { type: "string" }, size: { type: "string", default: "5" }, milestone: { type: "string" } },
});
const num = (name: string, v: string | undefined) => {
  const n = Number(v);
  if (v === undefined || !Number.isInteger(n) || n < 0) {
    console.error(`${command} needs --${name} <whole number>`);
    process.exit(2);
  }
  return n;
};
const roundDir = (n: number) => join(ROOT, `captures/feel/round-${n}`);
const milestoneDir = (m: number) => join(ROOT, `captures/heldout/milestone-${m}`);
/** Where batches and briefs work: a round's folder, or a milestone's (held-out tracks, no anchor). */
const workDir = () => (values.milestone !== undefined ? milestoneDir(num("milestone", values.milestone)) : roundDir(num("round", values.round)));
const feelDocs = join(ROOT, "docs/feel");
const readJson = (f: string) => JSON.parse(readFileSync(f, "utf8")) as unknown;

/** The rated batches under a directory: each batch-<k> folder's scores.json a first rating, each second-<name> one a second. */
function ratedIn(dir: string, heldOut = false): RatedBatch[] {
  if (!existsSync(dir)) return [];
  return readdirSync(dir)
    .filter((d) => (d.startsWith("batch-") || d.startsWith("second-")) && existsSync(join(dir, d, "scores.json")))
    .sort()
    .map((d) => {
      try {
        return { name: d, second: d.startsWith("second-"), scores: parseScores(readJson(join(dir, d, "scores.json"))) };
      } catch (e) {
        // (A held-out batch's error would quote its tracks or prose: name the folder only.)
        throw new Error(heldOut ? `${d}'s scores.json is malformed: re-run that batch's rater` : `${d}/scores.json: ${e instanceof Error ? e.message : String(e)}`, { cause: e });
      }
    });
}

const fmt = (x: number | null) => (x === null ? "n/a" : x.toFixed(2));

try {
  run();
} catch (e) {
  console.error(e instanceof Error ? e.message : String(e));
  process.exit(1);
}

function run() {
  switch (command) {
    case "batches": {
      const dir = workDir();
      const milestone = values.milestone !== undefined;
      // (A milestone's raters are calibrated on the anchor too: it is not held out.)
      const out = batches(corpusTrackIds(milestone ? "held-out" : "iteration"), Math.max(1, num("size", values.size)));
      mkdirSync(dir, { recursive: true });
      writeFileSync(join(dir, "batches.json"), `${JSON.stringify(out, null, 1)}\n`);
      // (A milestone's batches are listed only by size.)
      out.forEach((b, k) => console.log(`batch-${k + 1}: ${milestone ? `${b.length} tracks` : b.join(", ")}`));
      console.log(`${out.length} batches; wrote ${relative(ROOT, join(dir, "batches.json"))}`);
      break;
    }
    case "brief": {
      const dir = workDir();
      const milestone = values.milestone !== undefined;
      let name: string;
      let ids: string[];
      if (values.second !== undefined) {
        name = `second-${values.second}`;
        ids = (values.tracks ?? "").split(",").filter(Boolean);
      } else {
        const k = num("batch", values.batch);
        const all = JSON.parse(readFileSync(join(dir, "batches.json"), "utf8")) as string[][];
        if (!all[k - 1]) throw new Error(`no batch ${k} (${all.length} batches)`);
        name = `batch-${k}`;
        ids = all[k - 1];
      }
      const held = new Set(corpusTrackIds("held-out"));
      if (!milestone && ids.some((id) => held.has(id))) throw new Error("a round's batches hold iteration tracks only (milestones rate held-out tracks)");
      if (milestone && ids.some((id) => !held.has(id) && id !== ANCHOR)) throw new Error("a milestone's batches hold held-out tracks (and the anchor) only");
      const missing = ids.filter((id) => !existsSync(join(dir, id, "metrics.md")));
      if (missing.length)
        throw new Error(
          `no dossier for ${missing.length} of the batch's tracks (npm run reactive:dossier${milestone ? ` -- --split held-out --milestone ${values.milestone}` : " -- --track … --round …"})`,
        );
      // (A held-out rater's reply reaches the executor, so it names no track and no score.)
      const brief = readFileSync(join(feelDocs, "rater-brief.md"), "utf8").replace(
        "Then reply with one line per track: its id and its ten scores.",
        milestone
          ? "Then reply with the single word `written`. You are a held-out rater: never name a track, a score or an issue in your reply."
          : "Then reply with one line per track: its id and its ten scores.",
      );
      const what = milestone ? `milestone ${values.milestone} (a milestone batch: its folders are in captures/heldout/)` : `round ${values.round}`;
      console.log(
        `${brief}\n## Your batch\n\nYou are rater \`${name}\` for ${what}. The repository is ${ROOT}. Score these tracks (dossier folders):\n\n${ids.map((id) => `- \`${relative(ROOT, join(dir, id))}/\``).join("\n")}\n\nWrite your scores, with \`"rater": "${name}"\`, to \`${relative(ROOT, join(dir, name, "scores.json"))}\` (create the folder).\n`,
      );
      break;
    }
    case "aggregate": {
      const round = num("round", values.round);
      const rated = ratedIn(roundDir(round));
      if (!rated.length) throw new Error(`no scores in ${relative(ROOT, roundDir(round))}`);
      const metrics: Record<string, TrackMetrics> = {};
      for (const b of rated)
        for (const id of Object.keys(b.scores.tracks)) {
          const f = join(roundDir(round), id, "metrics.json");
          if (existsSync(f)) metrics[id] = readJson(f) as TrackMetrics;
        }
      const held = new Set(corpusTrackIds("held-out"));
      if (rated.some((b) => Object.keys(b.scores.tracks).some((id) => held.has(id)))) throw new Error("a round's scores name a held-out track: held-out tracks are a milestone's");
      // (The iteration set's metric means, when npm run reactive:corpus measured it.)
      const corpusFile = join(ROOT, "captures/feel/corpus.json");
      const corpus = existsSync(corpusFile) ? (readJson(corpusFile) as { split: string; means: Record<string, unknown> }) : undefined;
      const summary: RoundSummary = aggregate(round, rated, metrics, ANCHOR, corpus?.split === "iteration" ? corpus.means : undefined);
      if (!summary.metricMeans) console.log("note: no iteration metric means (npm run reactive:corpus -- --split iteration first)");
      mkdirSync(feelDocs, { recursive: true });
      writeFileSync(join(feelDocs, `round-${round}.json`), `${JSON.stringify(summary, null, 1)}\n`);
      console.log(`round ${round}: ${Object.keys(summary.tracks).length} tracks rated in ${rated.length} batches`);
      console.log(`means: ${DIMENSIONS.map((d) => `${d} ${fmt(summary.means[d])}`).join(", ")}`);
      if (summary.anchor)
        console.log(
          `anchor: ${DIMENSIONS.map((d) => `${d} ${summary.anchor!.scores[d]}`).join(", ")}${summary.anchor.calibrated ? " (calibrated)" : ` — NOT calibrated on ${summary.anchor.low.join(", ")}: re-rate ${summary.anchor.rater}'s batch before triage`}`,
        );
      else console.log(`anchor: not rated (${ANCHOR} belongs in the first batch)`);
      const needSecond = [...new Set(summary.contradictions.filter((c) => !summary.tracks[c.id].second).map((c) => c.id))];
      console.log(
        `contradictions: ${summary.contradictions.length ? summary.contradictions.map((c) => `${c.id} ${c.dim} ${c.score} with the metric in the ${c.quartile} quartile`).join("; ") : "none"}`,
      );
      if (needSecond.length) console.log(`second rater needed for: ${needSecond.join(", ")}`);
      // (The anchor's batch is always rated twice.)
      const anchorBatch = rated.find((b) => !b.second && b.scores.tracks[ANCHOR]);
      if (anchorBatch && !summary.tracks[ANCHOR]?.second) console.log(`second rater needed for the anchor's batch: ${Object.keys(anchorBatch.scores.tracks).join(", ")}`);
      console.log(`rater noise (${summary.noise.n} tracks rated twice): ${DIMENSIONS.map((d) => `${d} ${fmt(summary.noise.dims[d])}`).join(", ")}`);
      console.log(`${summary.issues.length} issues; wrote docs/feel/round-${round}.json`);
      break;
    }
    case "milestone": {
      const m = num("milestone", values.milestone);
      const round = num("round", values.round);
      const dir = join(ROOT, `captures/heldout/milestone-${m}`);
      const corpus = join(dir, "corpus.json");
      if (!existsSync(corpus)) throw new Error(`no held-out metrics: npm run reactive:corpus -- --split held-out --milestone ${m}`);
      const out = join(feelDocs, `milestone-${m}.json`);
      // (A milestone's numbers, milestone 0's above all as the baseline, are never silently replaced.)
      if (existsSync(out)) throw new Error(`docs/feel/milestone-${m}.json exists: a milestone is scored once (delete it by hand to redo it)`);
      const numbers: MilestoneNumbers = milestoneNumbers(m, round, ratedIn(dir, true), (readJson(corpus) as { means: Record<string, unknown> }).means);
      writeFileSync(out, `${JSON.stringify(numbers, null, 1)}\n`);
      if (numbers.anchor) {
        const low = calibrationLow(numbers.anchor);
        console.log(
          `anchor: ${DIMENSIONS.map((d) => `${d} ${numbers.anchor![d]}`).join(", ")}${low.length ? ` — NOT calibrated on ${low.join(", ")}: re-rate the anchor's batch and redo the milestone` : " (calibrated)"}`,
        );
      } else console.log("anchor: not rated (it belongs in the milestone's first batch)");
      console.log(
        `milestone ${m} (round ${round}): ${numbers.tracks} held-out tracks rated; means ${DIMENSIONS.map((d) => `${d} ${fmt(numbers.dimensions[d])}`).join(", ")}; wrote docs/feel/milestone-${m}.json`,
      );
      break;
    }
    case "overfit": {
      const m = num("milestone", values.milestone);
      const now = readJson(join(feelDocs, `milestone-${m}.json`)) as MilestoneNumbers;
      const base = readJson(join(feelDocs, "milestone-0.json")) as MilestoneNumbers;
      const iterNow = (readJson(join(feelDocs, `round-${now.round}.json`)) as RoundSummary).means;
      const iterBase = (readJson(join(feelDocs, `round-${base.round}.json`)) as RoundSummary).means;
      const o = overfitting(iterNow, now.dimensions, iterBase, base.dimensions);
      console.log(`gap growth since milestone 0: ${DIMENSIONS.map((d) => `${d} ${fmt(o.growth[d])}`).join(", ")}; mean ${fmt(o.meanGrowth)}`);
      console.log(o.trips ? `the overfitting rule trips (${o.reasons.join("; ")}): the next round is a generalisation round` : "the overfitting rule holds");
      break;
    }
    default:
      console.error("usage: npm run reactive:feel -- (batches | brief | aggregate | milestone | overfit) …");
      process.exit(2);
  }
}
