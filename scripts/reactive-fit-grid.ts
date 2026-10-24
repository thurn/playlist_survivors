/**
 * Fits the corpus's ground-truth beat grids (docs/design/reactive-feel.md, "Ground Truth"):
 *
 *   npm run reactive:fit-grid [-- <id> ...] [--jobs <n>]
 *
 * Writes each corpus track's public/reactive/<id>/reference.json and a click
 * overlay (downbeats and section starts accented) to the gitignored
 * captures/corpus/<id>-click.ogg; restream them with npm run reactive:streams.
 * The regression tracks are fitted too, for comparison only: their fits go to
 * captures/corpus/fits/, and their shipped references stay as they are. Prints
 * the corpus table (fit, tempo, residuals, meter) for the iteration and
 * regression tracks; held-out tracks are reported as counts only, and nothing
 * of their fits is printed (the "Held-out Protocol").
 */
import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { parseArgs } from "node:util";
import { analysis, CLICK_DIR, fitCorpusTrack, jobsOption, readManifest, REACTIVE_DIR, REGRESSION_TRACKS, regressionAudio, runJobs } from "./lib/corpus.ts";

const { values, positionals } = parseArgs({ allowPositionals: true, options: { jobs: { type: "string" } } });
const corpus = readManifest();
const regression = REGRESSION_TRACKS.filter((t) => t.listed);
const known = [...corpus.map((t) => t.id), ...regression.map((t) => t.id)];
const unknown = positionals.filter((id) => !known.includes(id));
if (unknown.length) {
  console.error(`unknown tracks: ${unknown.join(", ")}`);
  process.exit(2);
}
const jobs = jobsOption(values.jobs);
const wanted = (id: string) => positionals.length === 0 || positionals.includes(id);
const fitsDir = join(CLICK_DIR, "fits");
mkdirSync(fitsDir, { recursive: true });

interface Row {
  id: string;
  split: "iteration" | "held-out" | "regression";
  summary: Record<string, unknown>;
}
const rows: Row[] = [];
const todo: (() => Promise<void>)[] = [];
for (const t of corpus) {
  if (!wanted(t.id)) continue;
  todo.push(async () => {
    rows.push({ id: t.id, split: t.split, summary: await fitCorpusTrack(t) });
  });
}
for (const t of regression) {
  if (!wanted(t.id)) continue;
  todo.push(async () => {
    const out = await analysis("fit-grid", join(REACTIVE_DIR, regressionAudio(t)), "--out", join(fitsDir, `${t.id}.json`), "--click", join(CLICK_DIR, `${t.id}-click.ogg`));
    rows.push({ id: t.id, split: "regression", summary: JSON.parse(out) as Record<string, unknown> });
  });
}

const t0 = performance.now();
let failed = false;
await runJobs(todo, jobs, (job) => job()).catch((e: Error) => {
  failed = true;
  console.error(e.message);
});

const ms = (r: unknown) => {
  const x = r as { median: number; p90: number } | undefined;
  return x ? `${x.median.toFixed(1)}/${x.p90.toFixed(1)}` : "";
};
const order = (s: Row["split"]) => (s === "iteration" ? 0 : s === "regression" ? 1 : 2);
rows.sort((a, b) => order(a.split) - order(b.split) || a.id.localeCompare(b.id));
console.log("track                                      split       fit        bpm     level  residual ms (median/p90)  within 45 ms  meter       sections");
for (const r of rows.filter((x) => x.split !== "held-out")) {
  const s = r.summary;
  const meter = s.meter === undefined ? "" : `${String(s.meter)} (${String(s.meterConfidence)})`;
  console.log(
    `${r.id.padEnd(42)} ${r.split.padEnd(11)} ${String(s.fit).padEnd(10)} ${s.bpm === undefined ? "".padEnd(7) : String(s.bpm).padEnd(7)} ${String(s.level ?? "").padEnd(6)} ${ms(s.residualMs).padEnd(25)} ${s.within45 === undefined ? "".padEnd(12) : `${(100 * Number(s.within45)).toFixed(0)}%`.padEnd(12)} ${meter.padEnd(11)} ${String(s.sections ?? "")}${s.decode ? `  (${String(s.decode)})` : ""}`,
  );
}
const held = rows.filter((x) => x.split === "held-out");
if (held.length) {
  const counts = new Map<string, number>();
  for (const r of held) counts.set(String(r.summary.fit), (counts.get(String(r.summary.fit)) ?? 0) + 1);
  console.log(`held-out: ${held.length} fitted, ${[...counts].map(([k, n]) => `${n} ${k}`).join(", ")} (counts only)`);
}
console.log(`fitted ${rows.length} tracks in ${((performance.now() - t0) / 1000).toFixed(0)} s with ${jobs} jobs; click overlays in captures/corpus/. Restream with npm run reactive:streams.`);
if (failed) process.exit(1);
