/**
 * Loads and validates content files and level scripts. Every error names the
 * file and line it came from. Works in the browser and in Node: callers pass
 * file names and text.
 */
import { LineCounter, parseDocument, type Document } from "yaml";
import type { z } from "zod";
import {
  contentFile,
  heroDef,
  levelScript,
  type EffectRecipe,
  type EnemyDef,
  type FormationDef,
  type HeroDef,
  type LevelScriptInput,
  type MusicalRef,
  type SkillProfileName,
  type SoundPalette,
  type WeaponDef,
  type ZoneTheme,
} from "./schema.ts";
import { indexSong, type SongMap } from "../core/songmap.ts";

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
  return validateData(schema, p.data, issues, (path) => ({ file: p.file, line: lineOf(p, path) }));
}

/** Validate data against a schema; `locate` gives the file and line that set a path. */
function validateData<S extends z.ZodType>(schema: S, data: unknown, issues: ContentIssue[], locate: (path: (string | number)[]) => Origin, prefix: (string | number)[] = []): z.infer<S> | undefined {
  const r = schema.safeParse(data);
  if (r.success) return r.data;
  for (const iss of r.error.issues) {
    const path = [...prefix, ...iss.path.filter((k): k is string | number => typeof k !== "symbol")];
    if (iss.code === "unrecognized_keys") {
      for (const key of iss.keys) {
        const kp = [...path, key];
        issues.push({ ...locate(kp), path: pathString(path), message: `unknown field "${key}"` });
      }
      continue;
    }
    const message = iss.code === "invalid_type" && /received undefined/.test(iss.message) ? "required field is missing" : iss.message;
    issues.push({ ...locate(path), path: pathString(path), message });
  }
  return undefined;
}

/** Where a definition came from, for cross-reference errors. */
interface Origin {
  file: string;
  line?: number;
}

export interface ContentLibrary {
  /** The hero's movement model, timing, groove and swings (exactly one content file defines it). */
  hero: HeroDef;
  enemies: Map<string, EnemyDef>;
  weapons: Map<string, WeaponDef>;
  formations: Map<string, FormationDef>;
  effects: Map<string, EffectRecipe>;
  soundPalettes: Map<string, SoundPalette>;
  zones: Map<string, ZoneTheme>;
}

type Kind = Exclude<keyof ContentLibrary, "hero">;
const KINDS: Kind[] = ["enemies", "weapons", "formations", "effects", "soundPalettes", "zones"];

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

export function loadContent(files: SourceFile[]): ContentLibrary {
  const issues: ContentIssue[] = [];
  const lib = Object.fromEntries(KINDS.map((k) => [k, new Map()])) as unknown as ContentLibrary;
  const origins = Object.fromEntries(KINDS.map((k) => [k, new Map<string, Origin>()])) as Record<Kind, Map<string, Origin>>;
  let heroOrigin: Origin | undefined;

  for (const f of [...files].sort((a, b) => a.name.localeCompare(b.name))) {
    const p = parseYaml(f, issues);
    if (!p) continue;
    const data = validate(contentFile, p, issues);
    if (!data) continue;
    if (data.hero) {
      const origin = { file: f.name, line: lineOf(p, ["hero"]) };
      if (heroOrigin) issues.push({ ...origin, path: "hero", message: `the hero is already defined at ${heroOrigin.file}:${heroOrigin.line}` });
      else {
        lib.hero = data.hero;
        heroOrigin = origin;
      }
    }
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

  resolveExtends("soundPalettes", lib.soundPalettes, origins.soundPalettes, issues, (p, c) => ({
    ...p,
    ...c,
    sounds: { ...p.sounds, ...c.sounds },
    extends: undefined,
  }));

  if (!heroOrigin) issues.push({ file: "content", path: "hero", message: "no content file defines the hero" });

  // Cross references.
  const ref = (kind: Kind, defId: string, field: string, target: Kind, value: string | undefined) => {
    if (value === undefined) return;
    if (!lib[target].has(value)) {
      const o = origins[kind].get(defId)!;
      issues.push({ ...o, path: `${kind}.${defId}.${field}`, message: `unknown ${target.replace(/s$/, "")} "${value}"` });
    }
  };
  for (const [k, e] of lib.enemies) {
    ref("enemies", k, "death.recipe", "effects", e.death.recipe);
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
  }

  if (issues.length > 0) throw new ContentError(issues);
  return lib;
}

// ---------------------------------------------------------------- level script

/** Free movement: the sword swings by itself this often (beats) unless a section sets autoSwingBeats. */
export const AUTO_SWING_BEATS = 2;

export interface ResolvedSection {
  /** Song-map section label. */
  label: string;
  bars: [number, number];
  /** Step movement (one step per press) or free movement (held keys walk). */
  movement: "steps" | "free";
  /** Route speed of the pace point, world pixels per world-second. */
  pace: number;
  /** The skilled player's presses per timing-grid slot of each bar: x presses, . rests. */
  presses: string;
  timingGrid: "eighth" | "beat" | "bar";
  heavyOnAccents: { minStrength: number; label?: string };
  zone: string;
  timeScale: { from: number; to: number };
  /** Enemy calm, 0 to 1: speed scales by 1 - calm and the approach curls by up to CALM_CURL. */
  calm: number;
  /** Free movement: the sword swings by itself every this many beats. */
  autoSwingBeats: number;
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
  player: SkillProfileName;
  title: LevelScriptInput["title"];
  zones: string[];
  sections: ResolvedSection[];
  events: LevelEvent[];
  /** Source file and line of each event, for planner error messages. */
  eventFiles: string[];
  eventLines: (number | undefined)[];
  file: string;
  /** The level this one extends, if any. */
  extends?: string;
  /** The demo's forest corridor, or play mode's open field. */
  world: "corridor" | "open";
  rules: { oneStepPerSlot: boolean; accentTelegraphs: boolean; health: boolean; strides: boolean };
  /** The hero for this level: content/hero.yaml with the level's overrides (the same object when it has none). */
  hero: HeroDef;
}

/** The song a level script is for (following `extends`), read before the song map is loaded. */
export function levelSongName(f: SourceFile, resolve?: LevelResolver, depth = 0): string {
  const own = /^song: (\S+)/m.exec(f.text)?.[1];
  if (own) return own;
  const parent = /^extends: (\S+)/m.exec(f.text)?.[1];
  const pf = parent && depth < 8 ? resolve?.(parent) : undefined;
  return pf ? levelSongName(pf, resolve, depth + 1) : "";
}

/** Finds another level script by name (`extends: orpheus-galop` names levels/orpheus-galop.yaml). */
export type LevelResolver = (name: string) => SourceFile | undefined;

/** A level script's data merged over its parent's, and where each merged value was set. */
interface LevelSource {
  data: unknown;
  locate(path: (string | number)[]): Origin;
}

const EVENT_KINDS = ["levelUp", "formation", "threats", "boss", "banner", "titleCard", "landmark", "timeScale", "blast"];

const isRecord = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === "object" && !Array.isArray(v);

/** Plain objects merge key by key; anything else in `over` replaces. */
function deepMerge(base: unknown, over: unknown): unknown {
  if (!isRecord(base) || !isRecord(over)) return over;
  const out: Record<string, unknown> = { ...base };
  for (const [k, v] of Object.entries(over)) out[k] = k in base ? deepMerge(base[k], v) : v;
  return out;
}

/** Field by field: `over`'s fields replace `base`'s, except that the `nested` objects merge key by key. */
function mergeFields(base: unknown, over: Record<string, unknown>, nested: string[]): unknown {
  if (!isRecord(base)) return over;
  const out: Record<string, unknown> = { ...base, ...over };
  for (const k of nested) if (isRecord(base[k]) && isRecord(over[k])) out[k] = { ...base[k], ...over[k] };
  return out;
}

/** Does `v` have a value at `path`? */
function hasPath(v: unknown, path: (string | number)[]): boolean {
  let cur = v;
  for (const k of path) {
    if (cur === null || typeof cur !== "object" || !(k in (cur as object))) return false;
    cur = (cur as Record<string | number, unknown>)[k];
  }
  return true;
}

/** A canonical string for comparing musical references. */
function canonical(v: unknown): string {
  if (!isRecord(v)) return JSON.stringify(v);
  return `{${Object.keys(v)
    .sort()
    .map((k) => `${k}:${canonical(v[k])}`)
    .join(",")}}`;
}

const levelName = (file: string) => file.split("/").pop()!.replace(/\.ya?ml$/, "");

/**
 * Read a level script, merged over the level it extends: top-level fields
 * replace the parent's (the hero overrides merge), sections merge by their
 * `section` label field by field, and events merge by their `at` and kind.
 */
function readLevelSource(f: SourceFile, resolve: LevelResolver | undefined, issues: ContentIssue[], stack: string[]): LevelSource | undefined {
  const p = parseYaml(f, issues);
  if (!p) return undefined;
  const own: LevelSource = { data: p.data, locate: (path) => ({ file: f.name, line: lineOf(p, path) }) };
  if (!isRecord(p.data) || typeof p.data.extends !== "string") return own;
  const parentName = p.data.extends;
  const here = { ...own.locate(["extends"]), path: "extends" };
  if (stack.includes(parentName)) {
    issues.push({ ...here, message: `circular extends: ${[...stack, parentName].join(" → ")}` });
    return undefined;
  }
  const pf = resolve?.(parentName);
  if (!pf) {
    issues.push({ ...here, message: `unknown level "${parentName}"` });
    return undefined;
  }
  const parent = readLevelSource(pf, resolve, issues, [...stack, parentName]);
  if (!parent) return undefined;
  if (!isRecord(parent.data)) return parent;
  return mergeLevel(parent, parent.data, p, p.data, issues);
}

function mergeLevel(parent: LevelSource, pd: Record<string, unknown>, p: ParsedYaml, child: Record<string, unknown>, issues: ContentIssue[]): LevelSource {
  const childLoc = (path: (string | number)[]): Origin => ({ file: p.file, line: lineOf(p, path) });
  const out: Record<string, unknown> = { ...pd };
  for (const [k, v] of Object.entries(child)) {
    if (k === "sections" || k === "events") continue;
    out[k] = k === "hero" ? deepMerge(pd[k], v) : v;
  }
  // Sections: by label, field by field.
  const secs = Array.isArray(pd.sections) ? [...pd.sections] : [];
  const secFrom: { parent?: number; child?: number }[] = secs.map((_, i) => ({ parent: i }));
  const childSecs = Array.isArray(child.sections) ? child.sections : [];
  childSecs.forEach((cs, ci) => {
    const label = isRecord(cs) ? cs.section : undefined;
    const idx = secs.findIndex((ps) => isRecord(ps) && ps.section === label);
    if (typeof label !== "string" || idx < 0) {
      const known = secs.map((ps) => (isRecord(ps) ? ps.section : undefined)).filter((x) => typeof x === "string");
      issues.push({ ...childLoc(["sections", ci, "section"]), path: `sections[${ci}].section`, message: `unknown section "${String(label)}" in the level this extends (${known.join(", ")})` });
      return;
    }
    secs[idx] = mergeFields(secs[idx], cs, ["camera", "heavyOnAccents"]);
    secFrom[idx].child = ci;
  });
  out.sections = secs;
  // Events: by `at` and kind; the rest are new.
  const evs = Array.isArray(pd.events) ? [...pd.events] : [];
  const evFrom: { parent?: number; child?: number }[] = evs.map((_, i) => ({ parent: i }));
  const childEvs = Array.isArray(child.events) ? child.events : [];
  childEvs.forEach((ce, ci) => {
    if (!isRecord(ce)) return;
    const kind = EVENT_KINDS.find((k) => k in ce);
    const idx = evs.findIndex((pe, i) => evFrom[i].child === undefined && isRecord(pe) && canonical(pe.at) === canonical(ce.at) && (kind === undefined || kind in pe));
    if (idx >= 0) {
      evs[idx] = mergeFields(evs[idx], ce, []);
      evFrom[idx].child = ci;
    } else {
      evs.push(ce);
      evFrom.push({ child: ci });
    }
  });
  out.events = evs;
  const listLocate = (key: "sections" | "events", from: { parent?: number; child?: number }[], childList: unknown[], i: number, rest: (string | number)[]): Origin => {
    const fr = from[i];
    if (!fr) return hasPath(child, [key]) ? childLoc([key]) : parent.locate([key]);
    if (fr.child !== undefined && (fr.parent === undefined || hasPath(childList[fr.child], rest))) return childLoc([key, fr.child, ...rest]);
    return parent.locate([key, fr.parent!, ...rest]);
  };
  return {
    data: out,
    locate(path) {
      const [k, i, ...rest] = path;
      if (k === "sections" && typeof i === "number") return listLocate("sections", secFrom, childSecs, i, rest);
      if (k === "events" && typeof i === "number") return listLocate("events", evFrom, childEvs, i, rest);
      if (path.length > 0 && hasPath(child, path)) return childLoc(path);
      return parent.locate(path);
    },
  };
}

/**
 * Validate a level script against the schema, the content library and the
 * song map, and apply field inheritance between sections.
 */
export function loadLevel(f: SourceFile, lib: ContentLibrary, song: SongMap, resolve?: LevelResolver): LevelScript {
  const issues: ContentIssue[] = [];
  const src = readLevelSource(f, resolve, issues, [levelName(f.name)]);
  if (!src || issues.length > 0) throw new ContentError(issues);
  const data = validateData(levelScript, src.data, issues, src.locate);
  if (!data) throw new ContentError(issues);

  const at = (path: (string | number)[]) => ({ ...src.locate(path), path: pathString(path) });
  // The hero: content/hero.yaml with this level's overrides, checked against the hero schema.
  let levelHero = lib.hero;
  if (data.hero && lib.hero) {
    const merged = validateData(heroDef, deepMerge(lib.hero, data.hero), issues, src.locate, ["hero"]);
    if (merged) levelHero = merged;
  }
  const need = (path: (string | number)[], kind: Kind, value: string | undefined) => {
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
    const timeScale = typeof ts === "number" ? { from: ts, to: ts } : ts;
    const cam = { zoom: 1, lead: 60, punchOnAccents: false, ...(prev?.camera ?? {}), ...(s.camera ?? {}) };
    const feel = song.sections.find((x) => x.label === label)!.feel;
    const movement = s.movement ?? (feel === "free" ? "free" : "steps");
    const pace = s.pace ?? prev?.pace;
    const presses = s.presses ?? prev?.presses;
    if (pace === undefined) issues.push({ ...at(path), message: "the first section must set pace" });
    if (presses === undefined && movement === "steps") issues.push({ ...at(path), message: "a step section needs presses (set here or on an earlier section)" });
    const timingGrid = s.timingGrid ?? "beat";
    if (pace !== undefined && presses !== undefined && movement === "steps") {
      const err = paceError(song, bars, timingGrid, presses, pace, (timeScale.from + timeScale.to) / 2, lib.hero?.step.length ?? 44);
      if (err) issues.push({ ...at(s.pace !== undefined ? [...path, "pace"] : path), path: "", message: `section ${label}: ${err}` });
    }
    if (pace !== undefined && movement === "free" && lib.hero && pace > 0.9 * lib.hero.walk.speed) {
      issues.push({ ...at(s.pace !== undefined ? [...path, "pace"] : path), path: "", message: `section ${label}: pace ${pace} is more than 90% of the walking speed (${lib.hero.walk.speed})` });
    }
    const r: ResolvedSection = {
      label,
      bars,
      movement,
      pace: pace ?? 0,
      presses: presses ?? "x",
      timingGrid,
      heavyOnAccents: { minStrength: s.heavyOnAccents?.minStrength ?? 0.7, label: s.heavyOnAccents?.label },
      zone: zone ?? "",
      timeScale,
      calm: s.calm ?? 0,
      autoSwingBeats: s.autoSwingBeats ?? AUTO_SWING_BEATS,
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
    if (e.boss) {
      need([...path, "boss", "type"], "enemies", e.boss.type);
      checkRef(song, e.boss.slamsUntil, [...path, "boss", "slamsUntil"]);
    }
    if (e.threats) checkRef(song, e.threats.until, [...path, "threats", "until"]);
    if (e.levelUp && e.xp === undefined) issues.push({ ...at(path), message: "a levelUp needs xp (the experience it takes)" });
    if (e.xp !== undefined && !e.levelUp) issues.push({ ...at([...path, "xp"]), message: "xp only applies to a levelUp" });
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
    player: data.player,
    title: data.title,
    zones: data.zones,
    sections,
    events: data.events,
    eventFiles: data.events.map((_, i) => src.locate(["events", i]).file),
    eventLines: data.events.map((_, i) => src.locate(["events", i]).line),
    file: f.name,
    extends: data.extends,
    world: data.world,
    rules: { oneStepPerSlot: data.rules?.oneStepPerSlot ?? false, accentTelegraphs: data.rules?.accentTelegraphs ?? false, health: data.rules?.health ?? false, strides: data.rules?.strides ?? false },
    hero: levelHero,
  };
}

/** At most this fraction of a skilled player's steps go forward along the route. */
export const FORWARD_SHARE = 0.8;

/**
 * Why a step section's pace is out of reach, or undefined: forward progress
 * can be at most 80% of the step length times the press rate.
 */
export function paceError(song: SongMap, bars: [number, number], grid: "eighth" | "beat" | "bar", presses: string, pace: number, timeScale: number, stepLength: number): string | undefined {
  const idx = indexSong(song);
  const b0 = idx.barStart[bars[0]];
  const b1 = bars[1] + 1 <= idx.barCount ? idx.barStart[bars[1] + 1] : song.beats.length - 1;
  if (!(b1 > b0) || b1 > song.beats.length - 1) return undefined;
  const beatS = (song.beats[b1] - song.beats[b0]) / (b1 - b0);
  const bpb = idx.beatsPerBar[bars[0]] ?? 2;
  const slotsPerBeat = grid === "eighth" ? 2 : grid === "beat" ? 1 : 1 / bpb;
  const share = [...presses].filter((c) => c === "x").length / presses.length;
  // Presses per world-second: slow motion stretches the music over more world time.
  const rate = (share * slotsPerBeat) / beatS / Math.max(0.05, timeScale);
  const needed = pace / (FORWARD_SHARE * stepLength);
  if (needed <= rate + 1e-9) return undefined;
  return `pace ${pace} needs ${needed.toFixed(2)} presses per second (at most ${Math.round(FORWARD_SHARE * 100)}% of ${stepLength} px steps go forward), but "${presses}" presses only ${rate.toFixed(2)} times per second`;
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
