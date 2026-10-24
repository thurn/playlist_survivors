/**
 * Ingests the reactive feel corpus (docs/design/reactive-feel.md, "Where the corpus lives"):
 *
 *   npm run reactive:corpus-add [-- <id> ...] [--source ~/Downloads] [--jobs <n>]
 *
 * For each track of corpus/manifest.yaml missing its audio, encodes the source
 * as Ogg Vorbis (quality 4) to public/reactive/<id>/<id>.ogg; for each just
 * encoded or missing its stream, writes the belief stream from that Ogg file
 * with an offline reference; then rewrites public/reactive/index.json. A second run changes nothing. A source
 * inside a zip ("archive.zip:path") is extracted into a fresh temporary
 * directory first. Held-out tracks are ingested and never studied, so nothing
 * of their analysis is printed. npm run reactive:streams restreams them all.
 */
import { execFile } from "node:child_process";
import { existsSync, mkdtempSync, rmSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { basename, join, resolve } from "node:path";
import { parseArgs, promisify } from "node:util";
import { analysis, corpusAudio, jobsOption, readManifest, REACTIVE_DIR, runJobs, writeIndex, type CorpusTrack } from "./lib/corpus.ts";

const { values, positionals } = parseArgs({ allowPositionals: true, options: { source: { type: "string" }, jobs: { type: "string" } } });
const corpus = readManifest();
const unknown = positionals.filter((id) => !corpus.some((t) => t.id === id));
if (unknown.length) {
  console.error(`not in the manifest: ${unknown.join(", ")}`);
  process.exit(2);
}
const source = resolve((values.source ?? "~/Downloads").replace(/^~(?=$|\/)/, homedir()));
const jobs = jobsOption(values.jobs);
const unzip = promisify(execFile);

/** The source audio for a track, extracted into `scratch` when it is inside a zip. */
async function sourceFile(t: CorpusTrack, scratch: () => string): Promise<string> {
  const at = t.source.indexOf(".zip:");
  if (at < 0) return join(source, t.source);
  const zip = join(source, t.source.slice(0, at + 4));
  const member = t.source.slice(at + 5);
  const dir = scratch();
  await unzip("unzip", ["-q", "-j", zip, member, "-d", dir]);
  return join(dir, basename(member));
}

const todo = corpus.filter((t) => positionals.length === 0 || positionals.includes(t.id));
let encoded = 0;
let streamed = 0;
const t0 = performance.now();
let failed = false;
await runJobs(todo, jobs, async (t) => {
  const audio = join(REACTIVE_DIR, corpusAudio(t));
  const stream = join(REACTIVE_DIR, t.id, "stream.json");
  const did: string[] = [];
  if (!existsSync(audio)) {
    let dir: string | undefined;
    try {
      const file = await sourceFile(t, () => (dir = mkdtempSync(join(tmpdir(), "corpus-"))));
      if (!existsSync(file)) throw new Error(`${t.id}: no source at ${file}`);
      await analysis("encode", file, "--out", audio);
    } finally {
      if (dir) rmSync(dir, { recursive: true, force: true });
    }
    encoded++;
    did.push("encoded");
  }
  // (A fresh encode always gets a fresh stream: the stream must come from the shipped Ogg file.)
  if (did.length || !existsSync(stream)) {
    await analysis("stream", audio, "--out", stream, "--offline-reference");
    streamed++;
    did.push("streamed");
  }
  console.log(`${t.id}: ${did.length ? did.join(", ") : "up to date"}`);
}).catch((e: Error) => {
  failed = true;
  console.error(e.message);
});
const index = writeIndex(corpus);
console.log(`${encoded} encoded, ${streamed} streamed in ${((performance.now() - t0) / 1000).toFixed(0)} s with ${jobs} jobs; wrote public/reactive/index.json (${index.tracks.length} tracks)`);
if (failed) process.exit(1);
