/**
 * The corpus metrics report (docs/design/reactive-feel.md, "Metrics" and "Mechanics Targets"):
 *
 *   npm run reactive:corpus -- --split iteration [--rate 120]
 *   npm run reactive:corpus -- --track notjam-breakbeat-chips,cc-kevin-macleod-long-stroll
 *   npm run reactive:corpus -- --split held-out --milestone 0
 *
 * One row per track and the split's means, every metric or n/a, plus the mechanics targets
 * (printed, not gated); everything goes to captures/feel/corpus.json. The held-out split runs only
 * with --milestone <n>: its per-track metrics go to captures/heldout/milestone-<n>/corpus.json, and
 * only the split's means are printed (the "Held-out Protocol": executors never study held-out
 * tracks). `--split regression` measures the classical and synthetic tracks.
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parseArgs } from "node:util";
import { pct } from "./lib/feel-metrics.ts";
import { sampleFrames, trackMetrics, trackRuns, type TrackMetrics } from "./lib/feel.ts";
import { ROOT } from "./lib/files.ts";
import { propsFromAtlas } from "./lib/plan-node.ts";
import { corpusTrackIds, gatedTrackIds } from "./lib/reactive-node.ts";

const { values } = parseArgs({ options: { split: { type: "string" }, track: { type: "string" }, milestone: { type: "string" }, rate: { type: "string", default: "240" } } });
const split = values.split;
// (A milestone scores the whole held-out split, as numbers: never a chosen track.)
if (values.milestone !== undefined && (split !== "held-out" || values.track !== undefined)) {
  console.error("--milestone runs the whole held-out split: --split held-out, without --track");
  process.exit(2);
}
if (split === "held-out" && values.milestone === undefined) {
  console.error('the held-out split runs only at a milestone: --milestone <n> (docs/design/reactive-feel.md, "Held-out Protocol")');
  process.exit(2);
}
if (split !== undefined && !["iteration", "held-out", "regression"].includes(split)) {
  console.error(`--split takes iteration, held-out or regression, not ${split}`);
  process.exit(2);
}
const ids = values.track ? values.track.split(",") : split === "regression" ? gatedTrackIds() : corpusTrackIds((split ?? "iteration") as "iteration" | "held-out");
const heldOut = new Set(corpusTrackIds("held-out"));
if (values.milestone === undefined && ids.some((id) => heldOut.has(id))) {
  console.error("held-out tracks run only with --milestone <n>");
  process.exit(2);
}
const quiet = values.milestone !== undefined;
if (quiet && ids.length < 5) {
  console.error(`a milestone needs the held-out split (${ids.length} tracks found)`);
  process.exit(2);
}
const rate = Number(values.rate);
if (!(rate >= 60)) {
  console.error("--rate takes a frame rate of at least 60 Hz");
  process.exit(2);
}
const props = propsFromAtlas();

const rows: TrackMetrics[] = [];
const t0 = performance.now();
for (const id of ids) {
  const r = trackRuns(id, props);
  rows.push(trackMetrics(r, sampleFrames(r.plans["reactive-moderate"], r.lib, rate)));
  if (!quiet) process.stderr.write(`${id} `);
}
if (!quiet) process.stderr.write("\n");

const v = (x: number | null | undefined, f: (n: number) => string = (n) => n.toFixed(2)) => (x === null || x === undefined ? "n/a" : f(x));
const cols: [string, number, (m: TrackMetrics) => string][] = [
  ["track", 44, (m) => m.id],
  ["fit", 10, (m) => m.fit],
  ["beatF", 6, (m) => v(m.beat.strictF)],
  ["anyF", 6, (m) => v(m.beat.anyLevelF)],
  ["lockin", 7, (m) => v(m.beat.lockInS, (n) => `${n.toFixed(1)}s`)],
  ["sound", 6, (m) => v(m.soundSync)],
  ["bars", 5, () => "n/a"],
  ["hon T2", 7, (m) => pct(m.reward.honestTier2 ?? undefined)],
  ["m4 T0", 7, (m) => pct(m.reward.mash4Tier0 ?? undefined)],
  ["dmg×", 5, (m) => v(m.reward.damageRatio, (n) => n.toFixed(1))],
  ["judged", 7, (m) => pct(m.fairness.judgedBeatDriven ?? undefined)],
  ["P/G", 6, (m) => pct(m.fairness.perfectOrGreat ?? undefined)],
  ["pieces/ups/waves", 17, (m) => `${m.pacing.setPieces}/${m.pacing.levelUps}/${m.pacing.zoneWaves}`],
  ["cover", 6, (m) => `${m.coverage.n ?? "n/a"}/${m.coverage.of}`],
  ["vsync", 6, (m) => v(m.visualSync.beatTier)],
  ["struct", 7, (m) => (m.structure.followed === null ? "n/a" : `${pct(m.structure.followed)}`)],
  ["hier", 6, (m) => v(m.hierarchy.spearman)],
  ["brk snd", 7, (m) => String(m.breakdowns.percussiveSounds)],
];
const line = (cells: string[]) => cells.map((c, i) => c.padEnd(cols[i][1])).join(" ");
const meanOf = (f: (m: TrackMetrics) => number | null) => {
  const xs = rows.map(f).filter((x): x is number => x !== null && !Number.isNaN(x));
  return xs.length ? xs.reduce((a, x) => a + x, 0) / xs.length : null;
};
const means = {
  strictF: meanOf((m) => m.beat.strictF),
  anyLevelF: meanOf((m) => m.beat.anyLevelF),
  lockInS: meanOf((m) => m.beat.lockInS),
  soundSync: meanOf((m) => m.soundSync),
  honestTier2: meanOf((m) => m.reward.honestTier2),
  mash4Tier0: meanOf((m) => m.reward.mash4Tier0),
  damageRatio: meanOf((m) => m.reward.damageRatio),
  judgedBeatDriven: meanOf((m) => m.fairness.judgedBeatDriven),
  perfectOrGreat: meanOf((m) => m.fairness.perfectOrGreat),
  coverage: meanOf((m) => m.coverage.n),
  visualSync: meanOf((m) => m.visualSync.beatTier),
  structure: meanOf((m) => m.structure.followed),
  hierarchy: meanOf((m) => m.hierarchy.spearman),
  breakdownSounds: meanOf((m) => m.breakdowns.percussiveSounds),
};
const label = split ?? "tracks";
// The rest of the metrics, a second row per track (n/a: bars E2, identity family G2, bar-tier sync E2).
const more: [string, number, (m: TrackMetrics) => string][] = [
  ["track", 44, (m) => m.id],
  ["level", 6, (m) => v(m.beat.level)],
  ["run", 5, (m) => v(m.beat.longestRun)],
  ["m8 T0", 7, (m) => pct(m.reward.mash8Tier0 ?? undefined)],
  ["strike size by tier", 22, (m) => m.reward.strikeSizeByTier.map((x) => (x === null ? "n/a" : x.toFixed(0))).join("/")],
  ["first piece/up/wave", 20, (m) => [m.pacing.firstSetPiece, m.pacing.firstLevelUp, m.pacing.firstZoneWave].map((x) => v(x)).join("/")],
  ["false fin", 9, (m) => String(m.pacing.falseFinales)],
  ["survive", 8, (m) => (Object.values(m.pacing.survives).every(Boolean) ? "yes" : "no")],
  ["family", 7, () => "n/a"],
  ["grade", 6, (m) => m.identity.meanGrade.toFixed(3)],
  ["zones", 12, (m) => m.identity.zones.slice(0, 6).join(",") + (m.identity.zones.length > 6 ? "…" : "")],
  ["bar sync", 8, () => "n/a"],
  ["cap", 5, (m) => v(m.hierarchy.capShare)],
  ["follow", 6, (m) => v(m.breakdowns.pressFollowing)],
  ["drop", 5, (m) => v(m.breakdowns.intensityDrop)],
  ["drains", 6, (m) => String(m.breakdowns.grooveDrains)],
];
if (!quiet) {
  console.log(line(cols.map((c) => c[0])));
  for (const m of rows) console.log(line(cols.map((c) => c[2](m))));
  console.log("");
  const line2 = (cells: string[]) => cells.map((c, i) => c.padEnd(more[i][1])).join(" ");
  console.log(line2(more.map((c) => c[0])));
  for (const m of rows) console.log(line2(more.map((c) => c[2](m))));
}
console.log(
  `${label} means (${rows.length} tracks): beatF ${v(means.strictF)}, anyF ${v(means.anyLevelF)}, lock-in ${v(means.lockInS, (n) => `${n.toFixed(1)} s`)}, sound sync ${v(means.soundSync)}, honest tier 2+ ${pct(means.honestTier2 ?? undefined)}, mash4 tier 0 ${pct(means.mash4Tier0 ?? undefined)}, damage ratio ${v(means.damageRatio)}, judged ${pct(means.judgedBeatDriven ?? undefined)}, Perfect/Great ${pct(means.perfectOrGreat ?? undefined)}, coverage ${v(means.coverage, (n) => n.toFixed(1))}/12, visual sync ${v(means.visualSync)}, structure ${pct(means.structure ?? undefined)}, hierarchy ${v(means.hierarchy)}, breakdown sounds ${v(means.breakdownSounds, (n) => n.toFixed(1))}`,
);
if (!quiet) {
  // Mechanics targets (printed, not gated), in priority order.
  console.log("mechanics targets (damage off, judged time):");
  const miss = (name: string, bad: TrackMetrics[]) => console.log(`  ${name}: ${bad.length ? `missed on ${bad.map((m) => m.id).join(", ")}` : "met on every track"}`);
  miss(
    "mashing never beats playing",
    rows.filter((m) => m.mashing.beatsPlaying.length),
  );
  miss(
    "reactive-moderate at tier 2+ for 60%",
    rows.filter((m) => (m.reward.honestTier2 ?? 0) < 0.6),
  );
  miss(
    "honest damage at least 2× masher damage",
    rows.filter((m) => (m.reward.damageRatio ?? 0) < 2),
  );
  miss(
    "mash4 and mash8 at tier 0 for 80% (lowest priority)",
    rows.filter((m) => (m.reward.mash4Tier0 ?? 0) < 0.8 || (m.reward.mash8Tier0 ?? 0) < 0.8),
  );
  // (On tracks with at least 30 s of clear beat.)
  miss(
    "honest players survive with damage on (30 s or more of clear beat)",
    rows.filter((m) => (1 - m.breakdowns.unjudgedShare) * m.duration >= 30 && Object.values(m.pacing.survives).some((x) => !x)),
  );
}
const outDir = quiet ? join(ROOT, `captures/heldout/milestone-${values.milestone}`) : join(ROOT, "captures/feel");
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, "corpus.json"), `${JSON.stringify({ split: label, rate, means, tracks: rows }, null, 1)}\n`);
console.log(`${rows.length} tracks in ${((performance.now() - t0) / 1000).toFixed(0)} s; wrote ${quiet ? `captures/heldout/milestone-${values.milestone}/corpus.json` : "captures/feel/corpus.json"}`);
