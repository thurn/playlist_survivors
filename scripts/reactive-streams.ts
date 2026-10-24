/**
 * Regenerates every shipped reactive track's belief stream and
 * public/reactive/index.json:
 *
 *   npm run reactive:streams [-- <id> ...] [--jobs <n>] [--resynth]
 *
 * Run it whenever the stream code changes (and bump its TOOL_VERSION). The
 * synthetic tracks' Ogg audio and exact reference (reference.json beside it)
 * are rendered together, only when either is missing or with --resynth, since
 * Vorbis files differ byte for byte between encodes; their streams always come
 * from the Ogg files, so encoder delay cannot shift them, and from that saved
 * reference, so it always matches the audio. causality-mix is check-only and
 * stays out of the index. The corpus's tracks (corpus/manifest.yaml, ingested
 * with npm run reactive:corpus-add) are restreamed from their shipped Ogg files
 * too, with their fitted references (npm run reactive:fit-grid). `--jobs` runs that many at once (default: two thirds of the cores), the
 * longest first.
 */
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { parseArgs } from "node:util";
import { ROOT } from "./lib/files.ts";
import { analysis, corpusAudio, corpusReferenceArgs, jobsOption, readManifest, REACTIVE_DIR, REGRESSION_TRACKS, regressionAudio, runJobs, writeIndex } from "./lib/corpus.ts";

const { values, positionals } = parseArgs({ allowPositionals: true, options: { resynth: { type: "boolean" }, jobs: { type: "string" } } });
const corpus = readManifest();
const known = [...REGRESSION_TRACKS.map((t) => t.id), ...corpus.map((t) => t.id)];
const unknown = positionals.filter((id) => !known.includes(id));
if (unknown.length) {
  console.error(`unknown tracks: ${unknown.join(", ")}`);
  process.exit(2);
}
const jobs = jobsOption(values.jobs);
const wanted = (id: string) => positionals.length === 0 || positionals.includes(id);

interface Job {
  id: string;
  /** Held-out tracks print nothing of their analysis (docs/design/reactive-feel.md, "Held-out Protocol"). */
  quiet: boolean;
  run(): Promise<string>;
}

const todo: Job[] = [];
for (const t of REGRESSION_TRACKS) {
  if (!wanted(t.id)) continue;
  const audio = join(REACTIVE_DIR, regressionAudio(t));
  const out = join(REACTIVE_DIR, t.id, "stream.json");
  const params = t.params ? ["--params", JSON.stringify(t.params)] : [];
  todo.push({
    id: t.id,
    quiet: false,
    run: async () => {
      if (t.group === "synthetic") {
        const reference = join(REACTIVE_DIR, t.id, "reference.json");
        if (values.resynth || !existsSync(audio) || !existsSync(reference)) await analysis("synth", t.id, "--out", audio, "--reference", reference);
        return analysis("stream", audio, "--out", out, "--reference", reference, ...params);
      }
      const songmap = join(ROOT, "songs", t.id, "songmap.json");
      if (existsSync(songmap)) return analysis("stream", audio, "--out", out, "--songmap", songmap, ...params);
      return analysis("stream", audio, "--out", out, "--offline-reference", ...params);
    },
  });
}
for (const t of corpus) {
  if (!wanted(t.id)) continue;
  const audio = join(REACTIVE_DIR, corpusAudio(t));
  if (!existsSync(audio)) {
    if (positionals.includes(t.id)) console.error(`${t.id} is not ingested yet (npm run reactive:corpus-add)`);
    continue;
  }
  todo.push({ id: t.id, quiet: t.split === "held-out", run: () => analysis("stream", audio, "--out", join(REACTIVE_DIR, t.id, "stream.json"), ...corpusReferenceArgs(t)) });
}

// The longest tracks first (by their current streams' lengths, a track without one first), so the
// last to finish are short.
const lengthOf = (id: string) => {
  try {
    return (JSON.parse(readFileSync(join(REACTIVE_DIR, id, "stream.json"), "utf8")) as { audio: { duration: number } }).audio.duration;
  } catch {
    return Number.MAX_VALUE;
  }
};
const lengths = new Map(todo.map((j) => [j.id, lengthOf(j.id)]));
todo.sort((a, b) => lengths.get(b.id)! - lengths.get(a.id)!);

const t0 = performance.now();
let failed = false;
await runJobs(todo, jobs, async (j) => {
  const s0 = performance.now();
  const summary = await j.run();
  console.log(`== ${j.id} (${((performance.now() - s0) / 1000).toFixed(1)} s)${j.quiet ? "" : `\n${summary.trim()}`}`);
}).catch((e: Error) => {
  failed = true;
  console.error(e.message);
});
const index = writeIndex(corpus);
console.log(`restreamed ${todo.length} tracks in ${((performance.now() - t0) / 1000).toFixed(0)} s with ${jobs} jobs; wrote public/reactive/index.json (${index.tracks.length} tracks)`);
if (failed) process.exit(1);
