/**
 * Licence resolution and credits.
 *
 * - Generator art (`ulpc:` refs) is licensed by the generator's CREDITS.csv.
 * - Everything else (`lpc:`, `dl:`, `gen:` refs) must match an entry in the
 *   hand-maintained assets-src/licences.yaml.
 *
 * Only CC0, CC-BY, OGA-BY and CC-BY-SA are allowed. When a file offers several
 * licences, the least restrictive allowed one is recorded. A file whose only
 * licences are GPL/LGPL (or anything else not allowed), or a used file with no
 * licence record at all, is an error.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { parse as parseYaml } from "yaml";
import type { CreditEntry } from "../../../src/assets/types.ts";
import { ASSETS_SRC, ULPC } from "./paths.ts";

/** Allowed licences, least restrictive first. */
const LICENCE_RANK: Record<string, number> = {
  "CC0": 0,
  "OGA-BY 3.0": 1,
  "OGA-BY 4.0": 1,
  "CC-BY 4.0": 2,
  "CC-BY 3.0": 3,
  "CC-BY-SA 4.0": 4,
  "CC-BY-SA 3.0": 5,
};

export function normalizeLicence(raw: string): string {
  let s = raw.trim().replace(/\s+/g, " ");
  s = s.replace(/^CC[ _-]?BY[ _-]?SA/i, "CC-BY-SA").replace(/^CC[ _-]?BY(?![-_ ]?SA)/i, "CC-BY").replace(/^CC ?0$/i, "CC0");
  s = s.replace(/\+$/, "");
  if (/^CC-BY(-SA)?$/.test(s)) s += " 3.0";
  if (/^OGA-BY$/.test(s)) s += " 3.0";
  return s;
}

export function isAllowedLicence(l: string): boolean {
  return normalizeLicence(l) in LICENCE_RANK;
}

/** The least restrictive allowed licence, or null when none is allowed. */
export function leastRestrictive(licences: string[]): string | null {
  const allowed = licences.map(normalizeLicence).filter((l) => l in LICENCE_RANK);
  if (allowed.length === 0) return null;
  return allowed.sort((a, b) => LICENCE_RANK[a] - LICENCE_RANK[b])[0];
}

export function isShareAlike(licence: string): boolean {
  return licence.startsWith("CC-BY-SA");
}

export interface LicenceRecord {
  /** Pattern or file this record came from (used to group credits). */
  key: string;
  title?: string;
  authors: string[];
  licences: string[];
  urls: string[];
  notes?: string;
}

interface ManifestEntry {
  files: string[];
  title?: string;
  licences: string[];
  authors: string[];
  urls: string[];
  notes?: string;
}

let manifestCache: ManifestEntry[] | null = null;
export function loadLicenceManifest(): ManifestEntry[] {
  if (manifestCache) return manifestCache;
  const doc = parseYaml(readFileSync(join(ASSETS_SRC, "licences.yaml"), "utf8")) as { entries: ManifestEntry[] };
  for (const e of doc.entries) {
    if (!Array.isArray(e.files) || !Array.isArray(e.licences) || !Array.isArray(e.authors) || !Array.isArray(e.urls)) {
      throw new Error(`malformed licences.yaml entry: ${JSON.stringify(e).slice(0, 200)}`);
    }
  }
  manifestCache = doc.entries;
  return manifestCache;
}

export function globToRegExp(glob: string): RegExp {
  let re = "";
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === "*") {
      if (glob[i + 1] === "*") {
        re += ".*";
        i++;
      } else re += "[^/]*";
    } else if (c === "?") re += "[^/]";
    else re += c.replace(/[.+^${}()|[\]\\]/g, "\\$&");
  }
  return new RegExp(`^${re}$`);
}

// ---------------------------------------------------------------- CREDITS.csv

interface CsvRow {
  file: string;
  notes: string;
  authors: string[];
  licences: string[];
  urls: string[];
}

function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = "";
  let inQuotes = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inQuotes) {
      if (c === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else inQuotes = false;
      } else field += c;
    } else if (c === '"') inQuotes = true;
    else if (c === ",") {
      row.push(field.trim());
      field = "";
    } else if (c === "\n" || c === "\r") {
      if (c === "\r" && text[i + 1] === "\n") i++;
      row.push(field.trim());
      field = "";
      if (row.length > 1 || row[0] !== "") rows.push(row);
      row = [];
    } else field += c;
  }
  if (field !== "" || row.length) {
    row.push(field.trim());
    rows.push(row);
  }
  return rows;
}

let csvCache: Map<string, CsvRow> | null = null;
function loadGeneratorCredits(): Map<string, CsvRow> {
  if (csvCache) return csvCache;
  const rows = parseCsv(readFileSync(join(ULPC, "CREDITS.csv"), "utf8"));
  const map = new Map<string, CsvRow>();
  for (const r of rows.slice(1)) {
    const split = (s: string) => s.split(",").map((x) => x.trim()).filter(Boolean);
    map.set(r[0], { file: r[0], notes: r[1] ?? "", authors: split(r[2] ?? ""), licences: split(r[3] ?? ""), urls: split(r[4] ?? "") });
  }
  csvCache = map;
  return map;
}

/** Find the CREDITS.csv row for a spritesheet path (relative to spritesheets/). */
export function generatorCredit(path: string): CsvRow | null {
  const map = loadGeneratorCredits();
  const exact = map.get(path);
  if (exact) return exact;
  // Variant files: "a/b/walk/steel.png" is credited as "a/b/walk.png".
  const parts = path.split("/");
  if (parts.length > 1) {
    const parent = parts.slice(0, -1).join("/") + ".png";
    const hit = map.get(parent);
    if (hit) return hit;
  }
  // Custom-animation sheets: "a/b/attack_slash/fg/steel.png" is credited per animation in "a/b/attack_slash/fg/".
  const dir = parts.slice(0, -1).join("/") + "/";
  for (const [k, v] of map) if (k.startsWith(dir) && !k.slice(dir.length).includes("/")) return v;
  return null;
}

// ---------------------------------------------------------------- resolution

export class LicenceError extends Error {}

/** Resolve the licence record of a source reference, or throw. */
export function resolveLicence(ref: string): LicenceRecord {
  if (ref.startsWith("ulpc:")) {
    const p = ref.slice(5);
    const row = generatorCredit(p);
    if (!row) throw new LicenceError(`no CREDITS.csv entry for generator file ${p}`);
    const dir = p.split("/").slice(0, -1).join("/");
    return { key: `ulpc:${dir}|${row.authors.join(",")}|${row.licences.join(",")}`, authors: row.authors, licences: row.licences, urls: row.urls, notes: row.notes || undefined };
  }
  for (const e of loadLicenceManifest()) {
    for (const f of e.files) {
      if (globToRegExp(f).test(ref)) {
        return { key: f, title: e.title, authors: e.authors, licences: e.licences, urls: e.urls, notes: e.notes };
      }
    }
  }
  throw new LicenceError(`no licence record for ${ref} (add it to assets-src/licences.yaml)`);
}

/** Collects every source file the bake uses and turns them into credit entries. */
export class CreditCollector {
  private uses = new Map<string, Set<string>>();

  use(ref: string, usedFor: string): void {
    let s = this.uses.get(ref);
    if (!s) this.uses.set(ref, (s = new Set()));
    s.add(usedFor);
  }

  refs(): string[] {
    return [...this.uses.keys()].sort();
  }

  /** Throws a LicenceError listing every problem. */
  validate(): void {
    const problems: string[] = [];
    for (const ref of this.refs()) {
      try {
        const rec = resolveLicence(ref);
        if (!leastRestrictive(rec.licences)) problems.push(`${ref}: disallowed licence(s) ${rec.licences.join(", ")} (GPL-only/LGPL-only art is not allowed)`);
        if (rec.authors.length === 0) problems.push(`${ref}: no authors`);
        if (rec.urls.length === 0) problems.push(`${ref}: no URLs`);
      } catch (e) {
        problems.push((e as Error).message);
      }
    }
    if (problems.length) throw new LicenceError(`licence check failed:\n  ${problems.join("\n  ")}`);
  }

  entries(): CreditEntry[] {
    const groups = new Map<string, { rec: LicenceRecord; files: string[]; usedFor: Set<string> }>();
    for (const ref of this.refs()) {
      const rec = resolveLicence(ref);
      let g = groups.get(rec.key);
      if (!g) groups.set(rec.key, (g = { rec, files: [], usedFor: new Set() }));
      g.files.push(ref);
      for (const u of this.uses.get(ref)!) g.usedFor.add(u);
    }
    const out: CreditEntry[] = [];
    for (const g of groups.values()) {
      const licence = leastRestrictive(g.rec.licences)!;
      let file: string;
      if (g.files.length === 1) file = g.files[0];
      else if (g.rec.key.startsWith("ulpc:")) file = `${g.rec.key.split("|")[0]}/*`;
      else file = g.rec.key;
      const others = g.rec.licences.map(normalizeLicence).filter((l) => l !== licence);
      const notes = [g.rec.notes, others.length ? `Also offered under: ${others.join(", ")}.` : ""].filter(Boolean).join(" ");
      out.push({
        file,
        ...(g.rec.title ? { title: g.rec.title } : {}),
        authors: [...g.rec.authors],
        licence,
        urls: [...g.rec.urls],
        usedFor: [...g.usedFor].sort(),
        ...(notes ? { notes } : {}),
      });
    }
    return out.sort((a, b) => a.file.localeCompare(b.file));
  }
}
