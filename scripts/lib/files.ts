/** Node helpers for reading content, levels and song maps from disk. */
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, relative, resolve } from "node:path";
import type { SourceFile } from "../../src/content/loader.ts";
import type { SongMap } from "../../src/core/songmap.ts";

export const ROOT = resolve(import.meta.dirname, "../..");

export function readSource(path: string): SourceFile {
  const abs = resolve(ROOT, path);
  return { name: relative(ROOT, abs), text: readFileSync(abs, "utf8") };
}

export function readContentFiles(dir = "content"): SourceFile[] {
  const abs = join(ROOT, dir);
  return readdirSync(abs)
    .filter((f) => f.endsWith(".yaml"))
    .sort()
    .map((f) => readSource(join(dir, f)));
}

export function songMapPath(song: string): string {
  return join(ROOT, "songs", song, "songmap.json");
}

export function hasSongMap(song: string): boolean {
  return existsSync(songMapPath(song));
}

export function readSongMap(song: string): SongMap {
  return JSON.parse(readFileSync(songMapPath(song), "utf8")) as SongMap;
}
