/**
 * The shipped reactive tracks for the scripts that write them: the regression tracks (classical and
 * synthetic, docs/design/reactive-audio.md) and the reactive feel corpus from corpus/manifest.yaml
 * (docs/design/reactive-feel.md, "The Corpus"), public/reactive/index.json built from both, and the
 * analysis tool run in parallel.
 */
import { execFile } from "node:child_process";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { availableParallelism } from "node:os";
import { join } from "node:path";
import { promisify } from "node:util";
import { parse } from "yaml";
import { z } from "zod";
import { TRACK_SPLITS, type ReactiveIndex, type ReactiveTrack } from "../../src/reactive/tracks.ts";
import { ROOT } from "./files.ts";

export const REACTIVE_DIR = join(ROOT, "public/reactive");
export const MANIFEST = join(ROOT, "corpus/manifest.yaml");

export interface RegressionTrack {
  id: string;
  title: string;
  artist: string;
  group: "classical" | "synthetic";
  /** Listed in the index (false for check-only tracks). */
  listed: boolean;
  /** Stream parameter overrides (ps-analysis stream --params), by the tool's field names. */
  params?: Record<string, number>;
}

export const REGRESSION_TRACKS: RegressionTrack[] = [
  { id: "orpheus-galop", title: "Orpheus in the Underworld: Galop", artist: "Offenbach", group: "classical", listed: true },
  { id: "william-tell-finale", title: "William Tell Overture: Finale", artist: "Rossini", group: "classical", listed: true },
  { id: "mountain-king", title: "In the Hall of the Mountain King", artist: "Grieg", group: "classical", listed: true },
  { id: "bumblebee", title: "Flight of the Bumblebee", artist: "Rimsky-Korsakov", group: "classical", listed: true },
  { id: "beethoven-fifth", title: "Symphony No. 5: Allegro con brio", artist: "Beethoven", group: "classical", listed: true },
  { id: "house-128", title: "House at 128", artist: "Synthetic", group: "synthetic", listed: true },
  { id: "dnb-174", title: "Drum and Bass at 174", artist: "Synthetic", group: "synthetic", listed: true },
  { id: "hiphop-92-swing", title: "Swung Hip-Hop at 92", artist: "Synthetic", group: "synthetic", listed: true },
  { id: "accelerando-100-140", title: "Accelerando, 100 to 140", artist: "Synthetic", group: "synthetic", listed: true },
  { id: "pop-arrangement", title: "Pop Arrangement", artist: "Synthetic", group: "synthetic", listed: true },
  // The check's short track: 20 s statistics windows (docs/design/reactive-audio.md, "Cuts"), and
  // strong onsets from the 95th percentile, since its accents (every 4 s) outnumber the top 2%.
  { id: "causality-mix", title: "Causality Mix", artist: "Synthetic", group: "synthetic", listed: false, params: { change_stats_s: 20, change_threshold_s: 20, end_window_s: 20, strong_percentile: 95 } },
];

/** A regression track's audio, relative to public/reactive/. */
export function regressionAudio(t: RegressionTrack): string {
  return t.group === "synthetic" ? `${t.id}/${t.id}.ogg` : `../audio/${t.id}.flac`;
}

const CorpusTrackSchema = z.object({
  id: z.string().regex(/^(notjam|cc)-[a-z0-9][a-z0-9-]{0,56}$/),
  /** The source file under the ingest's --source directory; "archive.zip:path" names a file inside a zip. */
  source: z.string().min(1),
  title: z.string().min(1),
  artist: z.string().min(1),
  licence: z.enum(["CC0-1.0", "CC-BY-3.0", "CC-BY-4.0"]),
  credit: z.string().min(1).optional(),
  split: z.enum(TRACK_SPLITS),
  /** Near-duplicates share a family, and a family has a single split (absent: its own family). */
  family: z.string().min(1).optional(),
});
const ManifestSchema = z.object({ tracks: z.array(CorpusTrackSchema) });

export type CorpusTrack = z.infer<typeof CorpusTrackSchema>;

export function readManifest(path = MANIFEST): CorpusTrack[] {
  const tracks = ManifestSchema.parse(parse(readFileSync(path, "utf8"))).tracks;
  for (const t of tracks) if (t.licence !== "CC0-1.0" && !t.credit) throw new Error(`${t.id}: a ${t.licence} track needs a credit`);
  return tracks;
}

export const corpusGroup = (t: CorpusTrack): "notjam" | "cc" => (t.id.startsWith("notjam-") ? "notjam" : "cc");
/** A corpus track's audio, relative to public/reactive/. */
export const corpusAudio = (t: CorpusTrack) => `${t.id}/${t.id}.ogg`;
export const familyOf = (t: CorpusTrack) => t.family ?? t.id;
/** A corpus track's fitted reference (npm run reactive:fit-grid). */
export const corpusReference = (t: CorpusTrack) => join(REACTIVE_DIR, t.id, "reference.json");
/** Where the fits' click overlays go (gitignored), to check a fit by ear. */
export const CLICK_DIR = join(ROOT, "captures/corpus");

/** The stream command's reference for a corpus track: its fitted reference, else madmom's offline decode. */
export function corpusReferenceArgs(t: CorpusTrack): string[] {
  return existsSync(corpusReference(t)) ? ["--reference", corpusReference(t)] : ["--offline-reference"];
}

/**
 * Fits a corpus track's reference and writes its click overlay; the summary is returned, a
 * held-out track's being only whether it fitted (docs/design/reactive-feel.md, "Held-out Protocol").
 */
export async function fitCorpusTrack(t: CorpusTrack): Promise<Record<string, unknown>> {
  const quiet = t.split === "held-out" ? ["--quiet"] : [];
  return JSON.parse(await analysis("fit-grid", join(REACTIVE_DIR, corpusAudio(t)), "--out", corpusReference(t), "--click", join(CLICK_DIR, `${t.id}-click.ogg`), ...quiet)) as Record<string, unknown>;
}

/** The index: the listed regression tracks, then the corpus tracks ingested so far (audio and stream present), in manifest order. */
export function buildIndex(corpus: readonly CorpusTrack[]): ReactiveIndex {
  const regression = REGRESSION_TRACKS.filter((t) => t.listed).map((t): ReactiveTrack => ({ id: t.id, title: t.title, artist: t.artist, group: t.group, stream: `${t.id}/stream.json`, audio: regressionAudio(t) }));
  const ingested = corpus
    .filter((t) => existsSync(join(REACTIVE_DIR, corpusAudio(t))) && existsSync(join(REACTIVE_DIR, t.id, "stream.json")))
    .map((t): ReactiveTrack => ({ id: t.id, title: t.title, artist: t.artist, group: corpusGroup(t), stream: `${t.id}/stream.json`, audio: corpusAudio(t), split: t.split, licence: t.licence, ...(t.credit ? { credit: t.credit } : {}) }));
  return { version: 1, tracks: [...regression, ...ingested] };
}

export function writeIndex(corpus: readonly CorpusTrack[]): ReactiveIndex {
  const index = buildIndex(corpus);
  writeFileSync(join(REACTIVE_DIR, "index.json"), `${JSON.stringify(index, null, 1)}\n`);
  return index;
}

/**
 * Parallel jobs by default: two thirds of the cores, leaving room for the desktop. (On an 18-core
 * M-series Mac a full regeneration took 601 s with its 6 performance cores and 358 s with 12.)
 */
export function defaultJobs(): number {
  return Math.max(1, Math.round((availableParallelism() * 2) / 3));
}

/** The --jobs option: a positive integer, or the default when absent (anything else exits). */
export function jobsOption(value: string | undefined): number {
  if (value === undefined) return defaultJobs();
  const n = Number(value);
  if (!Number.isInteger(n) || n < 1) {
    console.error(`--jobs takes a positive whole number, not "${value}"`);
    process.exit(2);
  }
  return n;
}

const run = promisify(execFile);

/**
 * Runs the analysis tool (`ps-analysis <args>`), its output kept: thrown with its tail on failure,
 * and otherwise returned, so callers choose what to print (held-out tracks print nothing of theirs).
 */
export async function analysis(...args: string[]): Promise<string> {
  try {
    const { stdout } = await run("uv", ["run", "--project", join(ROOT, "tools/analysis"), "ps-analysis", ...args], { cwd: ROOT, maxBuffer: 64 << 20 });
    return stdout;
  } catch (e) {
    const err = e as { stdout?: string; stderr?: string; message: string };
    const tail = `${err.stderr ?? ""}${err.stdout ?? ""}`.trim().split("\n").slice(-15).join("\n");
    throw new Error(`ps-analysis ${args[0]} failed: ${err.message.split("\n")[0]}\n${tail}`, { cause: e });
  }
}

/** Runs `job` over the items, at most `jobs` at once; resolves once all are done, rejecting with every failure. */
export async function runJobs<T>(items: readonly T[], jobs: number, job: (item: T) => Promise<void>): Promise<void> {
  const failures: string[] = [];
  let next = 0;
  const worker = async () => {
    while (next < items.length) {
      const item = items[next++];
      try {
        await job(item);
      } catch (e) {
        failures.push(e instanceof Error ? e.message : String(e));
      }
    }
  };
  await Promise.all(Array.from({ length: Math.max(1, Math.min(jobs, items.length)) }, worker));
  if (failures.length) throw new Error(failures.join("\n\n"));
}
