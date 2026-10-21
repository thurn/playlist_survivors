/** Content, level scripts, song maps and landmarks, bundled as raw text by Vite. */
import type { SourceFile } from "../content/loader.ts";
import type { SongMap } from "../core/songmap.ts";

const contentRaw = import.meta.glob("/content/*.yaml", { query: "?raw", import: "default", eager: true }) as Record<string, string>;
const levelRaw = import.meta.glob("/levels/*.yaml", { query: "?raw", import: "default", eager: true }) as Record<string, string>;
const songMaps = import.meta.glob("/songs/*/songmap.json", { import: "default", eager: true }) as Record<string, SongMap>;
const landmarkRaw = import.meta.glob("/content/landmarks/*.tmj", { query: "?raw", import: "default", eager: true }) as Record<string, string>;

export function contentFiles(): SourceFile[] {
  return Object.entries(contentRaw)
    .map(([name, text]) => ({ name: name.slice(1), text }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

export function levelFile(name = "orpheus-galop"): SourceFile {
  const key = `/levels/${name}.yaml`;
  const text = levelRaw[key];
  if (text === undefined) throw new Error(`no level script ${key}`);
  return { name: key.slice(1), text };
}

export function songMap(song: string): SongMap {
  const m = songMaps[`/songs/${song}/songmap.json`];
  if (!m) throw new Error(`no song map for ${song}`);
  return m;
}

export function landmarks(): Record<string, unknown> {
  return Object.fromEntries(Object.entries(landmarkRaw).map(([k, v]) => [k.split("/").pop()!.replace(/\.tmj$/, ""), JSON.parse(v)]));
}
