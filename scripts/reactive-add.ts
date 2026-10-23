/**
 * Adds one of your own audio files as a local reactive track:
 *
 *   npm run reactive:add -- <audio file> [--id <id>] [--title <title>]
 *
 * Analyses the audio into reactive-local/<id>/ (gitignored): its belief stream
 * with an offline reference, and the analysed samples as audio.flac, which the
 * game plays (a compressed file's encoder delay may decode differently in the
 * browser than in the analysis). Lists it in reactive-local/index.json. The dev
 * server serves that folder at /reactive-local/; builds never include it.
 */
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { basename, extname, join, relative, resolve } from "node:path";
import { parseArgs } from "node:util";
import { ROOT } from "./lib/files.ts";
import { parseReactiveIndex } from "../src/reactive/tracks.ts";
import { LOCAL_DIR, readLocalIndex, slugify, upsertTrack } from "./lib/reactive-local.ts";

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: { id: { type: "string" }, title: { type: "string" } },
});
if (positionals.length !== 1) {
  console.error("usage: npm run reactive:add -- <audio file> [--id <id>] [--title <title>]");
  process.exit(2);
}
const source = positionals[0];
if (!existsSync(source)) {
  console.error(`no such file: ${source}`);
  process.exit(2);
}
const stem = basename(source, extname(source));
const id = slugify(values.id ?? stem);
const shipped = parseReactiveIndex(JSON.parse(readFileSync(join(ROOT, "public/reactive/index.json"), "utf8")));
if (shipped.tracks.some((t) => t.id === id)) {
  console.error(`${id} is a shipped track's id; choose another with --id`);
  process.exit(2);
}
const dir = join(ROOT, LOCAL_DIR);
const trackDir = join(dir, id);
mkdirSync(trackDir, { recursive: true });
const audio = join(trackDir, "audio.flac");
const stream = join(trackDir, "stream.json");
console.log(`analysing ${source} as ${id} …`);
execFileSync(
  "uv",
  [
    "run",
    "--project",
    join(ROOT, "tools/analysis"),
    "ps-analysis",
    "stream",
    resolve(source),
    "--out",
    stream,
    "--offline-reference",
    "--write-audio",
    audio,
  ],
  { stdio: "inherit", cwd: ROOT },
);
const index = upsertTrack(readLocalIndex(dir), {
  id,
  title: values.title ?? stem,
  artist: "",
  group: "local",
  stream: `${id}/stream.json`,
  audio: `${id}/audio.flac`,
});
writeFileSync(join(dir, "index.json"), `${JSON.stringify(index, null, 1)}\n`);
console.log(`added ${id}: ${relative(ROOT, trackDir)}/ (${index.tracks.length} local tracks)`);
