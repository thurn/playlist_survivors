/**
 * Reactive mode in the app (docs/design/reactive-audio.md, "Mode and Flow"): the track list from
 * both indexes (the shipped public/reactive/index.json and the dev server's /reactive-local/, which
 * may be missing), a track's belief stream and audio, and the planner input for the reactive level
 * on it. The stream's reference stays out of the input: only the virtual players read it.
 */
import { ContentError, loadLevel, type ContentLibrary } from "../content/loader.ts";
import type { SkillProfileName } from "../content/schema.ts";
import type { PlannerInput } from "../planner/context.ts";
import { reactiveSongMap } from "../reactive/songmap.ts";
import { parseStream, splitStream, type Reference } from "../reactive/stream.ts";
import { parseReactiveIndex, TRACK_GROUPS, type ReactiveTrack } from "../reactive/tracks.ts";
import type { Overlay } from "./app.ts";
import { landmarks, levelFile, levelResolver } from "./sources.ts";
import { loadBestGrade, overlayMenu } from "./title.ts";

export const REACTIVE_LEVEL = "reactive";

/** A track as the app loads it: its stream's and audio's URLs. */
export interface TrackEntry extends ReactiveTrack {
  streamUrl: string;
}

/** The best grade's key for a track (each track keeps its own). */
export const reactiveGradeKey = (id: string) => `${REACTIVE_LEVEL}.${id}`;

const base = () => new URL(import.meta.env.BASE_URL, location.href);

async function readIndex(url: URL, required: boolean): Promise<TrackEntry[]> {
  try {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`${url.pathname}: HTTP ${res.status}`);
    return parseReactiveIndex(await res.json()).tracks.map((t) => ({ ...t, streamUrl: new URL(t.stream, url).href }));
  } catch (e) {
    if (required) throw e instanceof Error ? e : new Error(String(e));
    // (A missing local index is normal; a broken one is worth a word.)
    if (!(e instanceof Error && e.message.includes("HTTP 404"))) console.warn(`ignoring ${url.pathname}:`, e);
    return [];
  }
}

/** The shipped tracks, then the local ones (a missing local index is not an error). */
export async function loadTracks(): Promise<TrackEntry[]> {
  const shipped = await readIndex(new URL("reactive/index.json", base()), true);
  const local = await readIndex(new URL("reactive-local/index.json", base()), false);
  const clash = local.filter((t) => shipped.some((s) => s.id === t.id));
  if (clash.length) console.warn(`local tracks hidden by shipped tracks with the same id: ${clash.map((t) => t.id).join(", ")}`);
  return [...shipped, ...local.filter((t) => !clash.includes(t))];
}

export interface ReactiveTrackRun {
  track: TrackEntry;
  input: PlannerInput;
  reference?: Reference;
  /** The audio the stream was analysed from. */
  audioUrl: string;
}

/** Fetch a track's stream and build the reactive level's planner input on it. */
export async function loadReactiveTrack(track: TrackEntry, lib: ContentLibrary, opts: { seed?: number; player?: SkillProfileName }): Promise<ReactiveTrackRun> {
  const res = await fetch(track.streamUrl);
  if (!res.ok) throw new Error(`${track.streamUrl}: HTTP ${res.status}`);
  const { stream, reference } = splitStream(parseStream(await res.json()));
  const songMap = reactiveSongMap(stream);
  let level;
  try {
    level = loadLevel(levelFile(REACTIVE_LEVEL), lib, songMap, levelResolver);
  } catch (e) {
    throw e instanceof ContentError ? new Error(`Level script or content error:\n${e.message}`) : e;
  }
  level = { ...level, title: { piece: track.title, composer: track.artist, performer: "" } };
  const input: PlannerInput = {
    songMap,
    level,
    content: lib,
    seed: opts.seed ?? level.seed,
    profile: opts.player ?? level.player,
    landmarks: landmarks(),
    version: __BUILD_VERSION__,
    reactive: stream,
    knownLength: stream.audio.duration,
  };
  return { track, input, reference, audioUrl: new URL(stream.audio.file, track.streamUrl).href };
}

/** The track groups' names on the title screen. */
const GROUP_NAMES: Record<ReactiveTrack["group"], string> = { classical: "Classical", synthetic: "Synthetic", notjam: "NotJam (the corpus)", cc: "Creative Commons (the corpus)", local: "Your tracks" };

/**
 * The track list: with tracks in more than one group, the groups first (Back to the title), then a
 * group's tracks, each with its best grade (Back to the groups). Resolves with the chosen track, or
 * undefined for Back.
 */
export async function trackMenu(overlay: Overlay, tracks: readonly TrackEntry[]): Promise<TrackEntry | undefined> {
  overlay.status.textContent = "Reactive mode (experimental): the game hears the music only as it plays";
  const groups = TRACK_GROUPS.filter((g) => tracks.some((t) => t.group === g));
  let g = 0;
  for (;;) {
    let shown = tracks;
    if (groups.length > 1) {
      g = await overlayMenu(overlay, [...groups.map((x) => `${GROUP_NAMES[x]}   (${tracks.filter((t) => t.group === x).length})`), "Back"], g);
      if (g === groups.length) return undefined;
      shown = tracks.filter((t) => t.group === groups[g]);
    }
    const items = shown.map((t) => {
      const best = loadBestGrade(reactiveGradeKey(t.id));
      return `${t.title}${t.artist ? ` — ${t.artist}` : ""}${t.split === "held-out" ? "   (held out)" : ""}${best ? `   best ${best}` : ""}`;
    });
    const i = await overlayMenu(overlay, [...items, "Back"]);
    if (i < shown.length) return shown[i];
    if (groups.length <= 1) return undefined;
  }
}
