/**
 * Downloaded source packs: fetch into the cache, verify sha256, and extract
 * each archive into its own directory. Archives are treated as untrusted data:
 * nothing inside them is ever executed, and entries with absolute or parent
 * paths are rejected before extraction.
 */
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { DOWNLOADS, EXTRACTED, REPO } from "./paths.ts";

export interface SourcePack {
  /** Archive download URL. */
  url: string;
  /** The page describing the pack and its licence. */
  page: string;
  sha256: string;
  /** Cache file name under DOWNLOADS. */
  file: string;
}

export function loadSourcePacks(): Record<string, SourcePack> {
  const raw = JSON.parse(readFileSync(join(REPO, "tools/baker/sources.json"), "utf8")) as { sources: Record<string, SourcePack> };
  return raw.sources;
}

function sha256(path: string): string {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

async function download(url: string, dest: string): Promise<void> {
  const res = await fetch(url, { redirect: "follow" });
  if (!res.ok) throw new Error(`download failed ${res.status} ${url}`);
  const buf = Buffer.from(await res.arrayBuffer());
  writeFileSync(dest, buf);
}

/** Make sure every pack is downloaded, verified and extracted. */
export async function ensureSources(log: (s: string) => void = () => {}): Promise<void> {
  const packs = loadSourcePacks();
  mkdirSync(DOWNLOADS, { recursive: true });
  mkdirSync(EXTRACTED, { recursive: true });
  for (const [id, pack] of Object.entries(packs)) {
    const archive = join(DOWNLOADS, pack.file);
    if (!existsSync(archive)) {
      log(`downloading ${id} from ${pack.url}`);
      await download(pack.url, archive);
    }
    const got = sha256(archive);
    if (got !== pack.sha256) {
      throw new Error(`sha256 mismatch for ${id} (${archive}): expected ${pack.sha256}, got ${got}. Delete the file to re-download, or update tools/baker/sources.json after reviewing the new archive.`);
    }
    const dir = join(EXTRACTED, id);
    const marker = join(dir, `.extracted-${pack.sha256.slice(0, 16)}`);
    if (existsSync(marker)) continue;
    const listing = execFileSync("unzip", ["-Z1", archive], { encoding: "utf8", maxBuffer: 1 << 26 }).split("\n").filter(Boolean);
    for (const entry of listing) {
      if (entry.startsWith("/") || entry.split(/[\\/]/).includes("..")) throw new Error(`unsafe archive entry in ${id}: ${entry}`);
    }
    rmSync(dir, { recursive: true, force: true });
    mkdirSync(dir, { recursive: true });
    log(`extracting ${id}`);
    execFileSync("unzip", ["-q", "-o", archive, "-d", dir], { stdio: "inherit" });
    writeFileSync(marker, `${pack.url}\n`);
  }
}
