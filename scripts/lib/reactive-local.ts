/**
 * The user's local reactive tracks: `reactive-local/` at the repository root
 * (gitignored), which `npm run reactive:add` fills and the dev server serves at
 * /reactive-local/. Builds never include it.
 */
import { existsSync, readFileSync } from "node:fs";
import { extname, isAbsolute, join, relative, resolve } from "node:path";
import { parseReactiveIndex, type ReactiveIndex, type ReactiveTrack } from "../../src/reactive/tracks.ts";

export const LOCAL_DIR = "reactive-local";

/** A track id from a file name: lowercase letters, digits and dashes. */
export function slugify(name: string): string {
  const slug = name
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 64)
    .replace(/-+$/, "");
  return slug || "track";
}

/** The index with `track` added, replacing any track with the same id. */
export function upsertTrack(index: ReactiveIndex, track: ReactiveTrack): ReactiveIndex {
  return { version: 1, tracks: [...index.tracks.filter((t) => t.id !== track.id), track] };
}

export function readLocalIndex(dir: string): ReactiveIndex {
  const path = join(dir, "index.json");
  return existsSync(path) ? parseReactiveIndex(JSON.parse(readFileSync(path, "utf8"))) : { version: 1, tracks: [] };
}

/** The file under `dir` that a request path names, or undefined if it would escape it. */
export function resolveLocalPath(dir: string, urlPath: string): string | undefined {
  let decoded: string;
  try {
    decoded = decodeURIComponent(urlPath.split(/[?#]/)[0]);
  } catch {
    return undefined;
  }
  if (decoded.includes("\0")) return undefined;
  const root = resolve(dir);
  const path = resolve(root, `.${decoded.startsWith("/") ? "" : "/"}${decoded}`);
  const rel = relative(root, path);
  if (rel === "" || rel.startsWith("..") || isAbsolute(rel)) return undefined;
  return path;
}

const TYPES: Record<string, string> = {
  ".json": "application/json",
  ".flac": "audio/flac",
  ".ogg": "audio/ogg",
  ".oga": "audio/ogg",
  ".opus": "audio/ogg",
  ".mp3": "audio/mpeg",
  ".wav": "audio/wav",
  ".m4a": "audio/mp4",
  ".aac": "audio/aac",
  ".webm": "audio/webm",
};

export function contentType(path: string): string {
  return TYPES[extname(path).toLowerCase()] ?? "application/octet-stream";
}
