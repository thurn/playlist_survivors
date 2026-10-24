/**
 * Regenerates every shipped reactive track's belief stream and
 * public/reactive/index.json:
 *
 *   npm run reactive:streams [-- <id> ...]
 *
 * Run it whenever the stream code changes (and bump its TOOL_VERSION). The
 * synthetic tracks' Ogg audio and exact reference (reference.json beside it)
 * are rendered together, only when either is missing or with --resynth, since
 * Vorbis files differ byte for byte between encodes; their streams always come
 * from the Ogg files, so encoder delay cannot shift them, and from that saved
 * reference, so it always matches the audio. causality-mix is check-only and
 * stays out of the index.
 */
import { execFileSync } from "node:child_process";
import { existsSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parseArgs } from "node:util";
import type { ReactiveIndex, ReactiveTrack } from "../src/reactive/tracks.ts";
import { ROOT } from "./lib/files.ts";

interface Track {
  id: string;
  title: string;
  artist: string;
  group: "classical" | "synthetic";
  /** Listed in the index (false for check-only tracks). */
  listed: boolean;
  /** Stream parameter overrides (ps-analysis stream --params), by the tool's field names. */
  params?: Record<string, number>;
}

const TRACKS: Track[] = [
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

const { values, positionals } = parseArgs({ allowPositionals: true, options: { resynth: { type: "boolean" } } });
const unknown = positionals.filter((id) => !TRACKS.some((t) => t.id === id));
if (unknown.length) {
  console.error(`unknown tracks: ${unknown.join(", ")}`);
  process.exit(2);
}
const reactive = join(ROOT, "public/reactive");

function analysis(...args: string[]): void {
  execFileSync("uv", ["run", "--project", join(ROOT, "tools/analysis"), "ps-analysis", ...args], { stdio: "inherit", cwd: ROOT });
}

/** The audio path relative to public/reactive/. */
function audioOf(t: Track): string {
  return t.group === "synthetic" ? `${t.id}/${t.id}.ogg` : `../audio/${t.id}.flac`;
}

for (const t of TRACKS) {
  if (positionals.length && !positionals.includes(t.id)) continue;
  const audio = join(reactive, audioOf(t));
  const out = join(reactive, t.id, "stream.json");
  const params = t.params ? ["--params", JSON.stringify(t.params)] : [];
  console.log(`== ${t.id}`);
  if (t.group === "synthetic") {
    const reference = join(reactive, t.id, "reference.json");
    if (values.resynth || !existsSync(audio) || !existsSync(reference)) analysis("synth", t.id, "--out", audio, "--reference", reference);
    analysis("stream", audio, "--out", out, "--reference", reference, ...params);
  } else if (existsSync(join(ROOT, "songs", t.id, "songmap.json"))) {
    analysis("stream", audio, "--out", out, "--songmap", join(ROOT, "songs", t.id, "songmap.json"), ...params);
  } else {
    analysis("stream", audio, "--out", out, "--offline-reference", ...params);
  }
}

const index: ReactiveIndex = {
  version: 1,
  tracks: TRACKS.filter((t) => t.listed).map(
    (t): ReactiveTrack => ({ id: t.id, title: t.title, artist: t.artist, group: t.group, stream: `${t.id}/stream.json`, audio: audioOf(t) }),
  ),
};
writeFileSync(join(reactive, "index.json"), `${JSON.stringify(index, null, 1)}\n`);
console.log(`wrote public/reactive/index.json (${index.tracks.length} tracks)`);
