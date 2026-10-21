/**
 * Loads and validates content files and level scripts. Every error names the
 * file and line it came from. Works in the browser and in Node: callers pass
 * file names and text.
 */
import { LineCounter, parseDocument, type Document } from "yaml";
import type { z } from "zod";
import {
  contentFile,
  levelScript,
  type EffectRecipe,
  type EnemyDef,
  type FormationDef,
  type LevelScriptInput,
  type MoveDef,
  type MovesetDef,
  type MusicalRef,
  type SoundPalette,
  type WeaponDef,
  type ZoneTheme,
} from "./schema.ts";
import type { SongMap } from "../core/songmap.ts";

export interface SourceFile {
  name: string;
  text: string;
}

export interface ContentIssue {
  file: string;
  line?: number;
  path: string;
  message: string;
}

export class ContentError extends Error {
  readonly issues: ContentIssue[];
  constructor(issues: ContentIssue[]) {
    super(issues.map(formatIssue).join("\n"));
    this.name = "ContentError";
    this.issues = issues;
  }
}

export function formatIssue(i: ContentIssue): string {
  const where = i.line !== undefined ? `${i.file}:${i.line}` : i.file;
  return `${where}: ${i.path ? i.path + ": " : ""}${i.message}`;
}

interface ParsedYaml {
  data: unknown;
  doc: Document;
  lines: LineCounter;
  file: string;
}

function parseYaml(f: SourceFile, issues: ContentIssue[]): ParsedYaml | undefined {
  const lines = new LineCounter();
  const doc = parseDocument(f.text, { lineCounter: lines, prettyErrors: false });
  if (doc.errors.length > 0) {
    for (const e of doc.errors) {
      issues.push({ file: f.name, line: lines.linePos(e.pos[0]).line, path: "", message: e.message.split("\n")[0] });
    }
    return undefined;
  }
  return { data: doc.toJS(), doc, lines, file: f.name };
}

/** Line of the YAML node at `path`, or of its nearest existing ancestor. */
function lineOf(p: ParsedYaml, path: (string | number)[]): number | undefined {
  for (let n = path.length; n >= 0; n--) {
    const node = p.doc.getIn(path.slice(0, n), true) as { range?: [number, number, number] } | undefined;
    if (node && node.range) return p.lines.linePos(node.range[0]).line;
    if (n === 0) {
      const contents = p.doc.contents as { range?: [number, number, number] } | null;
      if (contents?.range) return p.lines.linePos(contents.range[0]).line;
    }
  }
  return undefined;
}

function pathString(path: PropertyKey[]): string {
  return path.map((k) => (typeof k === "number" ? `[${k}]` : `.${String(k)}`)).join("").replace(/^\./, "");
}

function validate<S extends z.ZodType>(schema: S, p: ParsedYaml, issues: ContentIssue[]): z.infer<S> | undefined {
  const r = schema.safeParse(p.data);
  if (r.success) return r.data;
  for (const iss of r.error.issues) {
    const path = iss.path.filter((k): k is string | number => typeof k !== "symbol");
    if (iss.code === "unrecognized_keys") {
      for (const key of iss.keys) {
        const kp = [...path, key];
        issues.push({ file: p.file, line: lineOf(p, kp), path: pathString(path), message: `unknown field "${key}"` });
      }
      continue;
    }
    const message = iss.code === "invalid_type" && /received undefined/.test(iss.message) ? "required field is missing" : iss.message;
    issues.push({ file: p.file, line: lineOf(p, path), path: pathString(path), message });
  }
  return undefined;
}

/** Where a definition came from, for cross-reference errors. */
interface Origin {
  file: string;
  line?: number;
}

export interface ContentLibrary {
  moves: Map<string, MoveDef>;
  movesets: Map<string, MovesetDef>;
  enemies: Map<string, EnemyDef>;
  weapons: Map<string, WeaponDef>;
  formations: Map<string, FormationDef>;
  effects: Map<string, EffectRecipe>;
  soundPalettes: Map<string, SoundPalette>;
  zones: Map<string, ZoneTheme>;
}

type Kind = keyof ContentLibrary;
const KINDS: Kind[] = ["moves", "movesets", "enemies", "weapons", "formations", "effects", "soundPalettes", "zones"];

function resolveExtends<T extends { id: string; extends?: string }>(
  kind: string,
  defs: Map<string, T>,
  origins: Map<string, Origin>,
  issues: ContentIssue[],
  merge: (parent: T, child: T) => T,
): void {
  const done = new Set<string>();
  const visit = (key: string, stack: string[]): T | undefined => {
    const d = defs.get(key);
    if (!d) return undefined;
    if (done.has(key) || !d.extends) {
      done.add(key);
      return d;
    }
    const o = origins.get(key)!;
    if (stack.includes(key)) {
      issues.push({ ...o, path: `${kind}.${key}.extends`, message: `circular extends: ${[...stack, key].join(" → ")}` });
      return d;
    }
    const parent = visit(d.extends, [...stack, key]);
    if (!parent) {
      issues.push({ ...o, path: `${kind}.${key}.extends`, message: `unknown ${kind} "${d.extends}"` });
      done.add(key);
      return d;
    }
    const merged = merge(parent, d);
    defs.set(key, merged);
    done.add(key);
    return merged;
  };
  for (const key of [...defs.keys()]) visit(key, []);
}

export function mergeMoveset(parent: MovesetDef, child: MovesetDef): MovesetDef {
  const out: MovesetDef = { ...parent };
  for (const [k, v] of Object.entries(child)) {
    if (v === undefined || k === "attacks") continue;
    (out as Record<string, unknown>)[k] = v;
  }
  out.attacks = [...(parent.attacks ?? []), ...(child.attacks ?? [])];
  out.extends = undefined;
  out.id = child.id;
  return out;
}

export function loadContent(files: SourceFile[]): ContentLibrary {
  const issues: ContentIssue[] = [];
  const lib = Object.fromEntries(KINDS.map((k) => [k, new Map()])) as unknown as ContentLibrary;
  const origins = Object.fromEntries(KINDS.map((k) => [k, new Map<string, Origin>()])) as Record<Kind, Map<string, Origin>>;

  for (const f of [...files].sort((a, b) => a.name.localeCompare(b.name))) {
    const p = parseYaml(f, issues);
    if (!p) continue;
    const data = validate(contentFile, p, issues);
    if (!data) continue;
    for (const kind of KINDS) {
      const list = (data as Record<string, { id: string }[] | undefined>)[kind];
      if (!list) continue;
      list.forEach((def, i) => {
        const origin = { file: f.name, line: lineOf(p, [kind, i]) };
        const map = lib[kind] as Map<string, unknown>;
        if (map.has(def.id)) {
          const prev = origins[kind].get(def.id)!;
          issues.push({ ...origin, path: `${kind}[${i}].id`, message: `duplicate id "${def.id}" (first defined at ${prev.file}:${prev.line})` });
          return;
        }
        map.set(def.id, def);
        origins[kind].set(def.id, origin);
      });
    }
  }

  resolveExtends("movesets", lib.movesets, origins.movesets, issues, mergeMoveset);
  resolveExtends("soundPalettes", lib.soundPalettes, origins.soundPalettes, issues, (p, c) => ({
    ...p,
    ...c,
    sounds: { ...p.sounds, ...c.sounds },
    extends: undefined,
  }));

  // Cross references.
  const ref = (kind: Kind, defId: string, field: string, target: Kind, value: string | undefined) => {
    if (value === undefined) return;
    if (!lib[target].has(value)) {
      const o = origins[kind].get(defId)!;
      issues.push({ ...o, path: `${kind}.${defId}.${field}`, message: `unknown ${target.replace(/s$/, "")} "${value}"` });
    }
  };
  for (const [k, m] of lib.moves) {
    ref("moves", k, "effect", "effects", m.effect);
    ref("moves", k, "extraEffect", "effects", m.extraEffect);
    if (!m.dodge && !m.hitShape) issues.push({ ...origins.moves.get(k)!, path: `moves.${k}.hitShape`, message: "attacking moves need a hitShape" });
    if (m.weight === "heavy" && m.hitStopMs === undefined) issues.push({ ...origins.moves.get(k)!, path: `moves.${k}.hitStopMs`, message: "heavy moves need hitStopMs" });
  }
  for (const [k, ms] of lib.movesets) {
    const o = origins.movesets.get(k)!;
    if (!ms.locomotion) issues.push({ ...o, path: `movesets.${k}.locomotion`, message: "locomotion is required (directly or through extends)" });
    if (ms.travelSpeed === undefined) issues.push({ ...o, path: `movesets.${k}.travelSpeed`, message: "travelSpeed is required" });
    for (const a of ms.attacks ?? []) {
      if ("pattern" in a) {
        a.moves.forEach((mv) => ref("movesets", k, "attacks.moves", "moves", mv));
        if (!ms.grid) issues.push({ ...o, path: `movesets.${k}.grid`, message: "pattern attacks need a grid" });
      } else if ("onAccent" in a) ref("movesets", k, "attacks.onAccent.move", "moves", a.onAccent.move);
      else ref("movesets", k, "attacks.onSectionEnd", "moves", a.onSectionEnd);
    }
    ref("movesets", k, "continuous.move", "moves", ms.continuous?.move);
    ref("movesets", k, "onAnchor.move", "moves", ms.onAnchor?.move);
  }
  for (const [k, e] of lib.enemies) {
    ref("enemies", k, "death.recipe", "effects", e.death.recipe);
    if (e.size !== "regular" && e.hits === undefined && e.size === "elite") {
      issues.push({ ...origins.enemies.get(k)!, path: `enemies.${k}.hits`, message: "elite enemies need hits" });
    }
  }
  for (const [k, w] of lib.weapons) {
    ref("weapons", k, "effect", "effects", w.effect);
    const o = origins.weapons.get(k)!;
    const need = { orbit: w.orbit, chain: w.chain, trail: w.trail, blast: w.blast }[w.kind];
    if (!need) issues.push({ ...o, path: `weapons.${k}.${w.kind}`, message: `${w.kind} weapons need a "${w.kind}" block` });
    if (w.kind !== "blast" && !w.hitShape) issues.push({ ...o, path: `weapons.${k}.hitShape`, message: "weapons need a hitShape" });
  }
  for (const [k, f] of lib.formations) {
    ref("formations", k, "enemy", "enemies", f.enemy);
    if ("move" in f.release) ref("formations", k, "release.move", "moves", f.release.move);
  }

  if (issues.length > 0) throw new ContentError(issues);
  return lib;
}

// ---------------------------------------------------------------- level script

export interface ResolvedSection {
  /** Song-map section label. */
  label: string;
  bars: [number, number];
  moveset: string;
  zone: string;
  timeScale: { from: number; to: number };
  population: [number, number];
  roster: Record<string, number>;
  camera: { zoom: number; lead: number; punchOnAccents: boolean };
  sfxPalette: string;
  intensity: number;
  banner?: string;
}

export type LevelEvent = LevelScriptInput["events"][number];

export interface LevelScript {
  song: string;
  seed: number;
  title: LevelScriptInput["title"];
  zones: string[];
  sections: ResolvedSection[];
  events: LevelEvent[];
  /** Source line of each event, for planner error messages. */
  eventLines: (number | undefined)[];
  file: string;
}

/**
 * Validate a level script against the schema, the content library and the
 * song map, and apply field inheritance between sections.
 */
export function loadLevel(f: SourceFile, lib: ContentLibrary, song: SongMap): LevelScript {
  const issues: ContentIssue[] = [];
  const p = parseYaml(f, issues);
  if (!p) throw new ContentError(issues);
  const data = validate(levelScript, p, issues);
  if (!data) throw new ContentError(issues);

  const at = (path: (string | number)[]) => ({ file: f.name, line: lineOf(p, path), path: pathString(path) });
  const need = (path: (string | number)[], kind: keyof ContentLibrary, value: string | undefined) => {
    if (value !== undefined && !lib[kind].has(value)) {
      issues.push({ ...at(path), message: `unknown ${kind.replace(/s$/, "")} "${value}"` });
    }
  };

  if (data.song !== song.song) issues.push({ ...at(["song"]), message: `level is for song "${data.song}" but the song map is "${song.song}"` });
  data.zones.forEach((z, i) => need(["zones", i], "zones", z));

  const lastBar = song.sections[song.sections.length - 1].bars[1];
  const sections: ResolvedSection[] = [];
  let prev: ResolvedSection | undefined;
  data.sections.forEach((s, i) => {
    const path = ["sections", i];
    let label: string | undefined;
    let bars: [number, number] | undefined;
    if (s.bars) {
      const [a, b] = s.bars.split("-").map(Number);
      if (a < 1 || b > lastBar || a > b) {
        issues.push({ ...at([...path, "bars"]), message: `bar range ${s.bars} is outside the song (bars 1-${lastBar})` });
        return;
      }
      const sec = song.sections.find((x) => x.bars[0] === a && x.bars[1] === b);
      if (!sec) {
        issues.push({ ...at([...path, "bars"]), message: `bar range ${s.bars} does not match a song-map section (${song.sections.map((x) => `${x.label} ${x.bars.join("-")}`).join(", ")})` });
        return;
      }
      if (s.section && s.section !== sec.label) issues.push({ ...at([...path, "section"]), message: `bars ${s.bars} are section "${sec.label}", not "${s.section}"` });
      label = sec.label;
      bars = [a, b];
    } else if (s.section) {
      const sec = song.sections.find((x) => x.label === s.section);
      if (!sec) {
        issues.push({ ...at([...path, "section"]), message: `unknown song-map section "${s.section}"` });
        return;
      }
      label = sec.label;
      bars = [...sec.bars];
    } else {
      issues.push({ ...at(path), message: "a section needs bars or section" });
      return;
    }
    need([...path, "moveset"], "movesets", s.moveset);
    need([...path, "zone"], "zones", s.zone);
    need([...path, "sfxPalette"], "soundPalettes", s.sfxPalette);
    for (const t of Object.keys(s.roster ?? {})) need([...path, "roster", t], "enemies", t);
    if (s.zone && !data.zones.includes(s.zone)) issues.push({ ...at([...path, "zone"]), message: `zone "${s.zone}" is not in the level's zones list` });

    const zone = s.zone ?? prev?.zone;
    const roster = s.roster ?? prev?.roster;
    const sfx = s.sfxPalette ?? prev?.sfxPalette;
    if (!zone) issues.push({ ...at(path), message: "the first section must set zone" });
    if (!roster) issues.push({ ...at(path), message: "the first section must set roster" });
    if (!sfx) issues.push({ ...at(path), message: "the first section must set sfxPalette" });
    const ts = s.timeScale ?? 1;
    const cam = { zoom: 1, lead: 60, punchOnAccents: false, ...(prev?.camera ?? {}), ...(s.camera ?? {}) };
    const r: ResolvedSection = {
      label,
      bars,
      moveset: s.moveset,
      zone: zone ?? "",
      timeScale: typeof ts === "number" ? { from: ts, to: ts } : ts,
      population: s.population,
      roster: roster ?? {},
      camera: cam,
      sfxPalette: sfx ?? "",
      intensity: s.intensity ?? 1,
      banner: s.banner,
    };
    sections.push(r);
    prev = r;
  });

  // Sections must cover the song in order.
  const expected = song.sections.map((x) => x.label);
  const got = sections.map((x) => x.label);
  if (issues.length === 0 && got.join(",") !== expected.join(",")) {
    issues.push({ ...at(["sections"]), message: `sections must cover every song-map section in order: expected ${expected.join(", ")}; got ${got.join(", ")}` });
  }

  data.events.forEach((e, i) => {
    const path = ["events", i];
    checkRef(song, e.at, [...path, "at"]);
    need([...path, "levelUp"], "weapons", e.levelUp);
    need([...path, "formation"], "formations", e.formation);
    need([...path, "blast"], "weapons", e.blast);
    if (e.boss) need([...path, "boss", "type"], "enemies", e.boss.type);
    if (e.threats) checkRef(song, e.threats.until, [...path, "threats", "until"]);
    if (e.titleCard) checkRef(song, e.titleCard.until, [...path, "titleCard", "until"]);
    const kinds = ["levelUp", "formation", "threats", "boss", "banner", "titleCard", "landmark", "timeScale", "blast"].filter((k) => (e as Record<string, unknown>)[k] !== undefined);
    if (kinds.length === 0) issues.push({ ...at(path), message: "event does nothing (add levelUp, formation, threats, boss, banner, titleCard, landmark, timeScale or blast)" });
  });

  function checkRef(map: SongMap, r: MusicalRef, path: (string | number)[]) {
    const err = refError(map, r);
    if (err) issues.push({ ...at(path), message: err });
  }

  if (issues.length > 0) throw new ContentError(issues);
  return {
    song: data.song,
    seed: data.seed,
    title: data.title,
    zones: data.zones,
    sections,
    events: data.events,
    eventLines: data.events.map((_, i) => lineOf(p, ["events", i])),
    file: f.name,
  };
}

/** Why a musical reference cannot resolve against this song map, or undefined. */
export function refError(map: SongMap, r: MusicalRef): string | undefined {
  const lastBar = map.sections[map.sections.length - 1].bars[1];
  if ("bar" in r) {
    if (r.bar < 1 || r.bar > lastBar + 1) return `bar ${r.bar} is outside the song (bars 1-${lastBar})`;
    return undefined;
  }
  if ("section" in r) {
    const sec = map.sections.find((s) => s.label === r.section);
    if (!sec) return `unknown song-map section "${r.section}"`;
    if (r.phrase !== undefined) {
      const ph = map.phrases.filter((p) => p[0] >= sec.bars[0] && p[1] <= sec.bars[1]);
      if (r.phrase > ph.length) return `section "${r.section}" has only ${ph.length} phrases`;
    } else if (r.point === "finalAccent") return "finalAccent needs a phrase";
    return undefined;
  }
  const matching = map.accents.filter((a) => a.label === r.accent);
  if (matching.length === 0) return `no accents labelled "${r.accent}"`;
  const n = r.fromEnd ?? r.index ?? 1;
  if (n > matching.length) return `only ${matching.length} accents labelled "${r.accent}"`;
  return undefined;
}
