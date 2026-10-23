/** Reactive mode in Node: shipped belief streams, and planner inputs for the reactive level. */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { loadContent, loadLevel } from "../../src/content/loader.ts";
import type { CheckOptions, PlannerInput } from "../../src/planner/context.ts";
import { reactiveSongMap } from "../../src/reactive/songmap.ts";
import { parseStream, splitStream, type ReactiveStream, type Reference, type StreamFile } from "../../src/reactive/stream.ts";
import { parseReactiveIndex } from "../../src/reactive/tracks.ts";
import { ROOT, levelResolver, readContentFiles, readSource } from "./files.ts";
import { heroAnimsFromAtlas, readLandmarks, soundOnsets } from "./plan-node.ts";

export const REACTIVE_DIR = join(ROOT, "public/reactive");
export const REACTIVE_LEVEL = "levels/reactive.yaml";

/** The playable shipped tracks' ids, from public/reactive/index.json. */
export function shippedTrackIds(): string[] {
  return parseReactiveIndex(JSON.parse(readFileSync(join(REACTIVE_DIR, "index.json"), "utf8"))).tracks.map((t) => t.id);
}

/** A shipped track's stream file (the check-only causality-mix too). */
export function readStreamFile(id: string): StreamFile {
  return parseStream(JSON.parse(readFileSync(join(REACTIVE_DIR, id, "stream.json"), "utf8")));
}

export interface ReactiveRun {
  input: PlannerInput;
  stream: ReactiveStream;
  reference?: Reference;
}

let shared: { content: PlannerInput["content"]; heroAnims: PlannerInput["heroAnims"]; landmarks: PlannerInput["landmarks"]; soundOnsets: PlannerInput["soundOnsets"] } | undefined;

/**
 * The planner input for the reactive level on a stream: its reactive song map, the level loaded
 * against it, and the stream. The check calls it for every changed stream, so the content is read once.
 */
export function inputForStream(stream: ReactiveStream, opts: { seed?: number; checks?: CheckOptions; level?: string; knownLength?: number } = {}): PlannerInput {
  shared ??= { content: loadContent(readContentFiles()), heroAnims: heroAnimsFromAtlas(), landmarks: readLandmarks(), soundOnsets: soundOnsets() };
  const songMap = reactiveSongMap(stream);
  const level = loadLevel(readSource(opts.level ?? REACTIVE_LEVEL), shared.content, songMap, levelResolver);
  return {
    songMap,
    level,
    content: shared.content,
    seed: opts.seed ?? level.seed,
    heroAnims: shared.heroAnims,
    landmarks: shared.landmarks,
    soundOnsets: shared.soundOnsets,
    version: "node",
    reactive: stream,
    knownLength: opts.knownLength ?? stream.audio.duration,
    ...(opts.checks ? { checks: opts.checks } : {}),
  };
}

/** The planner input for the reactive level on a stream file (its reference kept apart, for the players and checks). */
export function reactiveInput(file: StreamFile, opts: { seed?: number; checks?: CheckOptions; level?: string } = {}): ReactiveRun {
  const { stream, reference } = splitStream(file);
  return { input: inputForStream(stream, opts), stream, reference };
}
