/**
 * The native app's oracle fixtures (docs/design/native-app.md, "Oracle fixtures";
 * native/oracle/FORMAT.md). Instrumentation only, on top of `ts-freeze`: it changes no game code.
 *
 *   npm run oracle:export                    # everything below, into native/oracle/
 *   npm run oracle:export -- runs [--only <track>/<profile>,...] [--jobs N]
 *   npm run oracle:export -- math | content | truncation | stills | sizes
 *
 * A run: the virtual player plays the track (its `decide` events inside the sim, as the checks
 * run it); its key events are recorded; the sim is run again fed only those keys, and the two
 * recordings must be identical; the keys-only run is exported: its keys, its canonical event log
 * and the hash of the evaluator's frame at every 1/60 s, taken as FORMAT.md defines a frame.
 * Core runs keep the whole log and every frame hash; the rest keep per-second hash chains.
 *
 * `stills` takes the reference stills through `npm run capture` (a dev server on this checkout's own
 * port and headless Chrome); `truncation` runs the truncation check once.
 */
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { availableParallelism } from "node:os";
import { join, relative } from "node:path";
import { pathToFileURL } from "node:url";
import { gzipSync } from "node:zlib";
import { loadContent } from "../src/content/loader.ts";
import type { SkillProfileName } from "../src/content/schema.ts";
import { Warp } from "../src/core/warp.ts";
import { LiveRecording } from "../src/live/recording.ts";
import { SPECULATE_S } from "../src/live/runner.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import type { Plan } from "../src/plan/types.ts";
import { defaultCuts, keysOf, liveRun, reactivePrefixDifference } from "../src/planner/reactive-checks.ts";
import type { PlannerInput } from "../src/planner/context.ts";
import { planHash, prepare } from "../src/planner/plan.ts";
import { reactivePlayerFor } from "../src/reactive/players.ts";
import type { ReactiveStream, Reference } from "../src/reactive/stream.ts";
import type { KeyEvent, Player } from "../src/sim/player.ts";
import { Sim, worldFor } from "../src/sim/simulate.ts";
import { ROOT, readContentFiles } from "./lib/files.ts";
import { propsFromAtlas } from "./lib/plan-node.ts";
import { REACTIVE_DIR, REACTIVE_LEVEL, corpusTrackIds, gatedTrackIds, inputForStream, reactiveInput, readStreamFile } from "./lib/reactive-node.ts";

const OUT = join(ROOT, "native/oracle");
const FIXTURES = join(OUT, "fixtures");
export const FRAME_RATE = 60;
const TAG = "ts-freeze";

// ------------------------------------------------------------------ the runs

/** Every virtual player profile the TS reactive checks use. */
const ALL_PROFILES: SkillProfileName[] = ["reactive-moderate", "reactive-human", "reactive-half", "reactive-offbeat", "reactive-skilled", "mash2", "mash4", "mash8", "holder"];
/** Every other track: an honest player and a masher. */
const PAIR: SkillProfileName[] = ["reactive-moderate", "mash4"];
/** The core set's corpus tracks: chiptune breakbeat, waltz, chill, solo piano (rubato), a live band, dark electronic. */
const CORE_CORPUS = ["notjam-breakbeat-chips", "notjam-waltz-of-the-lich-king", "notjam-chill-menu", "cc-kimiko-ishizaka-aria", "cc-hairy-larry-and-the-flying-hungarians-ride-me-to-heaven", "cc-kevin-macleod-black-vortex"];
/**
 * Full logs (FORMAT.md, "Runs"): the whole event log, every frame hash and dense frames, for every
 * core track with the honest player, and for the Galop's and causality-mix's other telling
 * profiles. Every other core run, and every other track, keeps per-second hash chains. (Trimmed
 * from full logs for every core profile, about 75 MB compressed, and then from four Galop profiles,
 * 35 MB.)
 */
const FULL: Record<string, SkillProfileName[]> = {
  "orpheus-galop": ["reactive-moderate", "mash4"],
  "causality-mix": ["reactive-moderate", "mash4"],
};
const isFull = (track: string, profile: SkillProfileName) => (FULL[track] ?? ["reactive-moderate"]).includes(profile);

interface RunSpec {
  track: string;
  profile: SkillProfileName;
  core: boolean;
  full: boolean;
}

function synthetic(): string[] {
  return gatedTrackIds().filter((id) => !["orpheus-galop", "william-tell-finale", "mountain-king", "bumblebee", "beethoven-fifth"].includes(id));
}

export function runSpecs(): RunSpec[] {
  const core = ["orpheus-galop", ...synthetic(), "causality-mix", ...CORE_CORPUS];
  const specs: RunSpec[] = [];
  for (const track of core) for (const profile of ALL_PROFILES) specs.push({ track, profile, core: true, full: isFull(track, profile) });
  const rest = [...gatedTrackIds(), ...corpusTrackIds()].filter((id) => !core.includes(id));
  for (const track of rest) for (const profile of PAIR) specs.push({ track, profile, core: false, full: false });
  return specs;
}

let propsCache: ReturnType<typeof propsFromAtlas> | null = null;
const props = () => (propsCache ??= propsFromAtlas());

function liveSimRun(input: PlannerInput, opts: { player?: Player; keys?: readonly KeyEvent[] }) {
  const { ctx, sch, map } = prepare(input);
  const recording = new LiveRecording(ctx, sch, map, ctx.lib, "player", props());
  const sim = new Sim(ctx, sch, worldFor(), recording, opts.player ? { player: opts.player } : {});
  for (const k of opts.keys ?? []) sim.key(k);
  return { ctx, recording, sim };
}

/**
 * The keys-only run with its frame hashes (FORMAT.md, "Frames"): every key queued up front, then
 * for each frame k (s = k / 60, s < the sim's end) the sim advanced to min(end, s + SPECULATE_S),
 * the recording refreshed, and evaluate(s) hashed.
 */
function framedRun(input: PlannerInput, keys: readonly KeyEvent[], denseAt: number[]) {
  const { ctx, recording, sim } = liveSimRun(input, { keys });
  const ev = new Evaluator(recording.plan, ctx.lib, recording);
  const hashes: bigint[] = [];
  const dense: Record<string, unknown> = {};
  const wanted = new Set(denseAt.map((t) => Math.round(t * FRAME_RATE)));
  for (let k = 0; k / FRAME_RATE < sim.endS; k++) {
    const s = k / FRAME_RATE;
    sim.advanceTo(Math.min(sim.endS, s + SPECULATE_S));
    recording.refresh();
    const f = ev.evaluate(s);
    hashes.push(frameHash(f));
    if (wanted.has(k)) dense[String(k)] = canonicalJson(f);
  }
  sim.runToEnd();
  recording.refresh();
  return { recording, hashes, dense };
}

/** The path of the first difference between two recordings, with both values (typed arrays as arrays). */
function firstDifference(a: Plan, b: Plan): string {
  const walk = (x: unknown, y: unknown, path: string): string | undefined => {
    if (JSON.stringify(x, plain) === JSON.stringify(y, plain)) return undefined;
    if (x && y && typeof x === "object" && typeof y === "object") {
      const xs = ArrayBuffer.isView(x) ? Array.from(x as unknown as ArrayLike<number>) : x;
      const ys = ArrayBuffer.isView(y) ? Array.from(y as unknown as ArrayLike<number>) : y;
      for (const k of new Set([...Object.keys(xs), ...Object.keys(ys)])) {
        const d = walk((xs as Record<string, unknown>)[k], (ys as Record<string, unknown>)[k], `${path}.${k}`);
        if (d) return d;
      }
    }
    return `${path}: ${JSON.stringify(x, plain)?.slice(0, 200)} vs ${JSON.stringify(y, plain)?.slice(0, 200)}`;
  };
  const { planMs: _a, simMs: _b, ...sa } = a.stats;
  const { planMs: _c, simMs: _d, ...sb } = b.stats;
  return walk({ ...a, stats: sa }, { ...b, stats: sb }, "") ?? "";
}

/** Stable JSON of the recording, for the keys-only assertion (typed arrays as arrays). */
function recordingJson(p: Plan): string {
  const { stats, ...rest } = p;
  const { planMs: _p, simMs: _s, ...timeless } = stats;
  return JSON.stringify({ ...rest, stats: timeless }, (_k, v) => (ArrayBuffer.isView(v) ? Array.from(v as unknown as ArrayLike<number>) : v));
}

function exportRun(spec: RunSpec): Record<string, unknown> {
  const t0 = performance.now();
  const file = readStreamFile(spec.track);
  const { input, reference } = reactiveInput(file);
  if (!reference) throw new Error(`${spec.track} has no reference: no virtual player can play it`);
  const player = reactivePlayerFor(prepare(input).ctx, reference, spec.profile);
  const own = liveSimRun(input, { player });
  own.sim.runToEnd();
  own.recording.refresh();
  const keys = keysOf(own.recording.plan);
  const duration = input.reactive!.audio.duration;
  const denseAt = [15, 60, 120, duration - 5].filter((t) => t > 0 && t < duration);
  const run = framedRun(input, keys, spec.full ? denseAt : []);
  const plan = run.recording.plan;
  // The assertions (FORMAT.md, "How a run was made"): the keys reproduce the player's keys, and
  // taking frames changes nothing (the same keys run without frames give the same recording).
  if (JSON.stringify(keysOf(plan)) !== JSON.stringify(keys)) throw new Error(`${spec.track}/${spec.profile}: the keys-only run's keys differ from the player's`);
  const bare = liveSimRun(input, { keys });
  bare.sim.runToEnd();
  bare.recording.refresh();
  const keysJson = recordingJson(plan);
  if (recordingJson(bare.recording.plan) !== keysJson) throw new Error(`${spec.track}/${spec.profile}: taking frames changed the keys-only run: ${firstDifference(bare.recording.plan, plan)}`);
  // The player's own run usually matches too; where its decide events' extra sim stops change a
  // stop-dependent read, the first difference is recorded.
  const playerDifference = recordingJson(own.recording.plan) === keysJson ? null : firstDifference(own.recording.plan, plan);
  const events = eventLog(plan);
  const dir = join(FIXTURES, spec.track, spec.profile);
  rmSync(dir, { recursive: true, force: true });
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, "keys.jsonl.gz"), gzipSync(keys.map((k) => JSON.stringify({ s: k.s, kind: k.kind, key: k.key })).join("\n") + "\n", { level: 9 }));
  const lines = events.map((e) => e.line);
  if (spec.full) {
    writeFileSync(join(dir, "events.jsonl.gz"), gzipSync(lines.join("\n") + "\n", { level: 9 }));
    const buf = Buffer.alloc(run.hashes.length * 8);
    run.hashes.forEach((h, i) => buf.writeBigUInt64LE(h, i * 8));
    writeFileSync(join(dir, "frames.bin.gz"), gzipSync(buf, { level: 9 }));
    writeFileSync(join(dir, "dense.json.gz"), gzipSync(JSON.stringify(run.dense), { level: 9 }));
  } else {
    writeFileSync(join(dir, "chains.json"), JSON.stringify({ events: eventChain(events, duration), frames: frameChain(run.hashes) }) + "\n");
  }
  const { planMs: _p, simMs: _s, ...stats } = plan.stats;
  const meta = {
    format: 1,
    tag: TAG,
    track: spec.track,
    profile: spec.profile,
    core: spec.core,
    full: spec.full,
    seed: input.seed,
    frameRate: FRAME_RATE,
    speculateS: SPECULATE_S,
    frames: run.hashes.length,
    events: events.length,
    keys: keys.length,
    planHash: planHash(plan),
    playerRunIdentical: playerDifference === null,
    ...(playerDifference ? { playerRunDifference: playerDifference } : {}),
    lastFrameHash: hex64(run.hashes.reduce((h, x) => fnv64Bytes(h, u64Bytes(x)), FNV_OFFSET)),
    stats,
    inputs: inputHashes(spec.track),
    levelHash: hex64(canonicalHash(input.level)),
  };
  writeFileSync(join(dir, "meta.json"), JSON.stringify(meta, null, 2) + "\n");
  return { run: `${spec.track}/${spec.profile}`, ms: Math.round(performance.now() - t0), frames: run.hashes.length, events: events.length };
}

// ------------------------------------------------------------------ the event log

interface LogEvent {
  t: number;
  order: number;
  i: number;
  k: string;
  v: unknown;
  line: string;
}

/**
 * The canonical event log (FORMAT.md, "Events"): one line per event, `{"t", "k", "i", "v"}`, where
 * `k` is its category, `i` its index in that category, `t` its song time by the category's rule,
 * and `v` the record (typed arrays as arrays). Sorted by t, then category (CATEGORIES' order), then i.
 */
export function eventLog(p: Plan): LogEvent[] {
  const warp = new Warp(p.warp);
  const song = (w: number) => warp.toSong(w);
  const out: LogEvent[] = [];
  const add = (k: string, list: readonly unknown[], time: (x: never, i: number) => number) => {
    const order = CATEGORIES.indexOf(k);
    if (order < 0) throw new Error(`unknown category ${k}`);
    list.forEach((x, i) => {
      const t = time(x as never, i);
      out.push({ t, order, i, k, v: x, line: JSON.stringify({ t, k, i, v: x }, plain) });
    });
  };
  const s = (x: { s: number }) => x.s;
  const series = (k: string, ser: { s: ArrayLike<number>; v: ArrayLike<number> } | undefined) => {
    if (!ser) return;
    add(k, Array.from(ser.s, (t, i) => [t, ser.v[i]]), (x: [number, number]) => x[0]);
  };
  const times = (k: string, l: ArrayLike<number>) => add(k, Array.from(l), (t: number) => t);
  // The beats: each song beat with its world time and when it became known.
  add("beat", Array.from(p.songBeats, (b, i) => [b, p.worldBeats[i] ?? null, p.beatsKnownAt?.[i] ?? null]), (x: [number, number, number]) => x[0]);
  add("input", p.inputs, s);
  add("settle", p.settles ?? [], s);
  add("swing", p.swings, s);
  add("cleave", p.cleaves, (c: Plan["cleaves"][number]) => c.launch ?? Math.max(c.s, c.press));
  series("groove", p.groove);
  series("tier", p.tier);
  series("health", p.health);
  add("enemy", p.enemies.map(({ samples: _s, sampleOffset: _o, ...e }) => e), (e: Plan["enemies"][number]) => song(e.spawnW));
  add("hit", p.hits, s);
  add("bite", p.bites, s);
  add("heal", p.heals, s);
  add("upgrade", p.upgrades, s);
  add("levelUpDue", p.levelUpsDue, s);
  add("chicken", p.chickens, s);
  add("vacuum", p.vacuums, s);
  add("weapon", p.weapons, s);
  add("formation", p.formations, (f: Plan["formations"][number]) => f.sRelease);
  add("wave", p.open.waves, s);
  add("landmark", p.open.landmarks, (l: { s?: number; w?: number }) => l.s ?? song(l.w ?? 0));
  add("ui", p.events.ui, s);
  add("effect", p.events.effects, s);
  add("sound", p.events.sounds, s);
  add("gem", p.events.gems, (g: Plan["events"]["gems"][number]) => song(g.w0));
  add("projectile", p.projectiles, (x: Plan["projectiles"][number]) => song(x.w0));
  add("slam", p.slams, (x: Plan["slams"][number]) => song(x.w0));
  add("chain", p.chains, (c: Plan["chains"][number]) => song(c.points[0]?.w ?? 0));
  add("nearMiss", p.nearMisses, s);
  add("threat", p.threats, s);
  add("accent", p.accents, s);
  add("cleaveAccent", p.cleaveAccents, s);
  times("hud.kill", p.hud.kills);
  times("hud.perfect", p.hud.perfect);
  add("hud.xp", Array.from(p.hud.xpTimes, (t, i) => [t, p.hud.xpFill[i]]), (x: [number, number]) => x[0]);
  times("hud.levelUp", p.hud.levelUps);
  // (Numbered across the category: every weapon's fires, weapons in ascending order.)
  add("hud.fire", Object.entries(p.hud.weaponFires).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)).flatMap(([w, l]) => Array.from(l, (t) => [w, t])), (x: [string, number]) => x[1]);
  const { planMs: _p, simMs: _s, ...stats } = p.stats;
  let end = p.duration;
  for (const e of out) if (Number.isFinite(e.t) && e.t > end) end = e.t;
  add("summary", [{
    stats,
    boss: p.boss ?? null,
    warp: p.warp,
    sections: p.sections,
    pickupRadius: p.pickupRadius,
    grid: hex64(fnv64Numbers(p.grid)),
    strides: { ...p.strides, steps: hex64(fnv64Numbers(p.strides.steps)), periods: hex64(fnv64Numbers(p.strides.periods)), per: p.strides.per ? hex64(fnv64Numbers(p.strides.per)) : undefined },
    hero: Object.fromEntries((["x", "y", "z", "facing", "anim", "frame", "trail"] as const).map((k) => [k, hex64(fnv64Numbers(p.hero[k]))])),
    heroAnims: p.hero.animNames,
    camera: Object.fromEntries((["x", "y", "zoom"] as const).map((k) => [k, hex64(fnv64Numbers(p.camera[k]))])),
    enemySamples: hex64(fnv64Numbers(p.enemySamples)),
    energy: hex64(fnv64Numbers(p.energy)),
    barStart: p.barStart,
    map: p.map,
    progressS: p.progressS ?? null,
  }], () => end);
  out.sort((a, b) => a.t - b.t || a.order - b.order || a.i - b.i);
  return out;
}

export const CATEGORIES = [
  "beat", "input", "settle", "swing", "cleave", "groove", "tier", "health", "enemy", "hit", "bite", "heal", "upgrade", "levelUpDue",
  "chicken", "vacuum", "weapon", "formation", "wave", "landmark", "ui", "effect", "sound", "gem", "projectile", "slam", "chain",
  "nearMiss", "threat", "accent", "cleaveAccent", "hud.kill", "hud.perfect", "hud.xp", "hud.levelUp", "hud.fire", "summary",
];

/** JSON.stringify's replacer: typed arrays as arrays. */
function plain(_k: string, v: unknown): unknown {
  return ArrayBuffer.isView(v) ? Array.from(v as unknown as ArrayLike<number>) : v;
}

/**
 * Per second of song: chain_n = FNV-1a 64 over (chain_{n-1} as 8 LE bytes, then the canonical
 * serialization of each event {t, k, i, v} with n <= t < n + 1, in log order; the last second
 * takes every later event too).
 */
function eventChain(events: LogEvent[], duration: number): string[] {
  let seconds = Math.ceil(duration);
  for (const e of events) if (Number.isFinite(e.t)) seconds = Math.max(seconds, Math.floor(e.t) + 1);
  const chain: string[] = [];
  let h = FNV_OFFSET;
  let j = 0;
  for (let n = 0; n < seconds; n++) {
    const st = new Hasher();
    st.bytes(u64Bytes(h));
    while (j < events.length && (n === seconds - 1 || events[j].t < n + 1)) {
      const e = events[j++];
      canonicalFeed(st, { t: e.t, k: e.k, i: e.i, v: e.v });
    }
    h = st.value();
    chain.push(hex64(h));
  }
  return chain;
}

/** Per second: chain_n = FNV-1a 64 over (chain_{n-1}, then the frame hashes of frames 60n to 60n + 59, each 8 LE bytes). */
function frameChain(hashes: bigint[]): string[] {
  const chain: string[] = [];
  let h = FNV_OFFSET;
  for (let n = 0; n * FRAME_RATE < hashes.length; n++) {
    h = fnv64Bytes(FNV_OFFSET, u64Bytes(h));
    for (const x of hashes.slice(n * FRAME_RATE, (n + 1) * FRAME_RATE)) h = fnv64Bytes(h, u64Bytes(x));
    chain.push(hex64(h));
  }
  return chain;
}

// ------------------------------------------------------------------ hashing

export const FNV_OFFSET = 0xcbf29ce484222325n;

/** FNV-1a 64 over bytes, from h, in two 32-bit halves (a BigInt per byte is far too slow). */
export function fnv64Bytes(h: bigint, bytes: Uint8Array): bigint {
  const st = new Hasher(h);
  st.bytes(bytes);
  return st.value();
}

const u64Bytes = (x: bigint) => {
  const b = Buffer.alloc(8);
  b.writeBigUInt64LE(x);
  return b;
};
export const hex64 = (h: bigint) => h.toString(16).padStart(16, "0");

/** FNV-1a 64 of numbers as f64 bits (LE), length-prefixed: for the summary's sample tracks. */
function fnv64Numbers(a: ArrayLike<number>): bigint {
  const st = new Hasher(FNV_OFFSET);
  st.u32(a.length);
  for (let i = 0; i < a.length; i++) st.f64(a[i]);
  return st.value();
}

/** A streaming FNV-1a 64 hasher: hi and lo 32-bit halves, multiplying by the prime 0x100000001b3. */
export class Hasher {
  private hi: number;
  private lo: number;
  private readonly f = new Float64Array(1);
  private readonly fb = new Uint8Array(this.f.buffer);
  private readonly enc = new TextEncoder();

  constructor(h: bigint = FNV_OFFSET) {
    this.hi = Number(h >> 32n);
    this.lo = Number(h & 0xffffffffn);
  }

  byte(b: number): void {
    const lo = (this.lo ^ b) >>> 0;
    // (hi, lo) * (0x100, 0x1b3) mod 2^64: lo * 0x1b3 is under 2^41, exact in a double.
    const p = lo * 0x1b3;
    const carry = Math.floor(p / 4294967296);
    this.lo = p >>> 0;
    this.hi = (Math.imul(this.hi, 0x1b3) + (lo << 8) + carry) >>> 0;
  }

  bytes(b: Uint8Array): void {
    for (let i = 0; i < b.length; i++) this.byte(b[i]);
  }

  u32(n: number): void {
    this.byte(n & 0xff);
    this.byte((n >>> 8) & 0xff);
    this.byte((n >>> 16) & 0xff);
    this.byte((n >>> 24) & 0xff);
  }

  f64(x: number): void {
    // (One NaN: the canonical quiet NaN's bits.)
    this.f[0] = Number.isNaN(x) ? NaN : x;
    if (Number.isNaN(x)) {
      for (const b of [0, 0, 0, 0, 0, 0, 0xf8, 0x7f]) this.byte(b);
      return;
    }
    for (let i = 0; i < 8; i++) this.byte(this.fb[i]);
  }

  str(s: string): void {
    const b = this.enc.encode(s);
    this.u32(b.length);
    this.bytes(b);
  }

  value(): bigint {
    return (BigInt(this.hi) << 32n) | BigInt(this.lo);
  }
}

/**
 * The canonical serialization (FORMAT.md, "Canonical serialization"), fed to a hasher: null 0x00,
 * false 0x01, true 0x02, a number 0x03 and its f64 bits, a string 0x04 and its UTF-8 length-prefixed,
 * an array (typed arrays too) 0x05, its length and its items, an object 0x06, the count of its
 * defined fields and each (key, value) with keys in ascending UTF-16 order. Undefined fields are
 * skipped, as JSON.stringify skips them.
 */
export function canonicalFeed(st: Hasher, v: unknown): void {
  if (v === null) return st.byte(0);
  switch (typeof v) {
    case "boolean":
      return st.byte(v ? 2 : 1);
    case "number":
      st.byte(3);
      return st.f64(v);
    case "string":
      st.byte(4);
      return st.str(v);
    case "object": {
      if (Array.isArray(v) || ArrayBuffer.isView(v)) {
        const a = v as ArrayLike<unknown>;
        st.byte(5);
        st.u32(a.length);
        for (let i = 0; i < a.length; i++) canonicalFeed(st, a[i] === undefined ? null : a[i]);
        return;
      }
      if (v instanceof Map || v instanceof Set) throw new Error("canonical serialization: a Map or Set (convert it first)");
      const keys = Object.keys(v as object)
        .filter((k) => (v as Record<string, unknown>)[k] !== undefined && typeof (v as Record<string, unknown>)[k] !== "function")
        .sort();
      st.byte(6);
      st.u32(keys.length);
      for (const k of keys) {
        st.str(k);
        canonicalFeed(st, (v as Record<string, unknown>)[k]);
      }
      return;
    }
    default:
      throw new Error(`canonical serialization: a ${typeof v}`);
  }
}

export function canonicalHash(v: unknown): bigint {
  const st = new Hasher();
  canonicalFeed(st, v);
  return st.value();
}

const frameHash = (f: unknown) => canonicalHash(f);

/** A frame as JSON with keys sorted, for the dense debug frames. */
function canonicalJson(v: unknown): unknown {
  if (ArrayBuffer.isView(v)) return Array.from(v as unknown as ArrayLike<number>);
  if (Array.isArray(v)) return v.map(canonicalJson);
  if (v && typeof v === "object") return Object.fromEntries(Object.keys(v).sort().filter((k) => (v as Record<string, unknown>)[k] !== undefined).map((k) => [k, canonicalJson((v as Record<string, unknown>)[k])]));
  return v;
}

// ------------------------------------------------------------------ inputs

const sha256 = (b: Buffer | string) => createHash("sha256").update(b).digest("hex");

function filesUnder(dir: string): string[] {
  const out: string[] = [];
  const walk = (d: string) => {
    for (const name of readdirSync(d).sort()) {
      const p = join(d, name);
      if (statSync(p).isDirectory()) walk(p);
      else out.push(p);
    }
  };
  walk(dir);
  return out;
}

/** A tree's hash: SHA-256 over "<path relative to the repository>\0<file's SHA-256>\n" for every file, sorted by path. */
function treeHash(dir: string): string {
  const lines = filesUnder(join(ROOT, dir)).map((p) => relative(ROOT, p).split("\\").join("/")).sort().map((rel) => `${rel}\0${sha256(readFileSync(join(ROOT, rel)))}\n`);
  return sha256(lines.join(""));
}

let treeHashes: { content: string; assets: string; level: string } | undefined;

function inputHashes(track: string) {
  treeHashes ??= { content: treeHash("content"), assets: treeHash("public/assets"), level: sha256(readFileSync(join(ROOT, REACTIVE_LEVEL))) };
  return {
    stream: { path: `public/reactive/${track}/stream.json`, sha256: sha256(readFileSync(join(REACTIVE_DIR, track, "stream.json"))) },
    content: { path: "content", tree: treeHashes.content },
    level: { path: REACTIVE_LEVEL, sha256: treeHashes.level },
    assets: { path: "public/assets", tree: treeHashes.assets },
  };
}

// ------------------------------------------------------------------ math vectors

/** Every Math function the ported TS code calls (src/core, sim, planner, reactive, plan, live, mapgen, content). */
function mathFunctionsUsed(): string[] {
  const used = new Set<string>();
  for (const dir of ["core", "sim", "planner", "reactive", "plan", "live", "mapgen", "content"]) {
    for (const p of filesUnder(join(ROOT, "src", dir)).filter((f) => f.endsWith(".ts"))) {
      for (const m of readFileSync(p, "utf8").matchAll(/\bMath\.([a-z0-9]+)\s*\(/gi)) used.add(m[1]);
    }
  }
  used.delete("random");
  return [...used].sort();
}

/** A small seeded generator for the vectors' inputs (xorshift128+ on BigInt-free halves is overkill: mulberry32). */
function mulberry32(seed: number) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const SPECIAL = [0, -0, 1, -1, 0.5, -0.5, 2, Math.PI, -Math.PI, Math.PI / 2, Math.PI / 4, Math.E, 1e-300, -1e-300, 5e-324, 1e300, -1e300, Infinity, -Infinity, NaN, 0.1, 1 / 3, 709.78, -745.13, 1e-8, 1e8];

/** Extra inputs where JS and C libraries are known to differ, or the game leans on: by function. */
const EDGES: Record<string, number[][]> = {
  pow: [[1, NaN], [NaN, 0], [-1, Infinity], [-1, -Infinity], [0.5, Infinity], [2, -Infinity], [-0, -1], [-0, -3], [0, -1], [-Infinity, 3], [-Infinity, -3], [-8, 1 / 3]],
  atan2: [[Infinity, Infinity], [-Infinity, Infinity], [Infinity, -Infinity], [-Infinity, -Infinity], [0, -0], [-0, -0], [0, 0], [-0, 0]],
  hypot: [[Infinity, NaN], [NaN, Infinity], [-Infinity, NaN], [3, 4], [1e308, 1e308], [5e-324, 5e-324]],
  round: [[0.5], [-0.5], [1.5], [-1.5], [2.5], [-2.5], [0.49999999999999994], [-0.49999999999999994], [4503599627370495.5], [-4503599627370495.5], [9007199254740992], [4503599627370497], [-0.4]],
};

function vectorsFor(name: string, n: number): { args: number[]; out: number }[] {
  const f = (Math as unknown as Record<string, (...a: number[]) => number>)[name];
  const arity = ["atan2", "pow", "min", "max", "hypot", "imul"].includes(name) ? 2 : 1;
  // (Seeded by the whole name, so no two functions share a sequence.)
  let seed = 0x5eed;
  for (const ch of name) seed = Math.imul(seed ^ ch.charCodeAt(0), 0x01000193);
  const r = mulberry32(seed);
  // Inputs: the specials (every pair of them for two arguments), the edge cases, then values spread
  // over the ranges the game uses (angles, times, small and large magnitudes), and random doubles'
  // bits for the whole range. pow also gets the exponents the code uses, its own and through `**`.
  const draw = () => {
    const u = r();
    if (u < 0.35) return (r() * 2 - 1) * 8 * Math.PI;
    if (u < 0.6) return (r() * 2 - 1) * 1000;
    if (u < 0.75) return r() * 4;
    if (u < 0.85) return (r() * 2 - 1) * 1e-6;
    const b = new Uint32Array([Math.floor(r() * 4294967296), Math.floor(r() * 4294967296)]);
    return new Float64Array(b.buffer)[0];
  };
  const args: number[][] = [];
  if (arity === 1) for (const x of SPECIAL) args.push([x]);
  else for (const x of SPECIAL) for (const y of SPECIAL) args.push([x, y]);
  args.push(...(EDGES[name] ?? []));
  if (name === "pow") {
    for (const e of [2, 3, 1.5, 0.5, 4, 1 / 3, 2.2, 0.75]) for (let i = 0; i < 40; i++) args.push([r() * 4, e]);
    for (const b of [0.5, 2, 10, Math.E]) for (let i = 0; i < 40; i++) args.push([b, (r() * 2 - 1) * 20]);
  }
  while (args.length < n) args.push(arity === 1 ? [draw()] : [draw(), draw()]);
  return args.map((a) => ({ args: a, out: f(...a) }));
}

const bits = (x: number) => {
  const b = Buffer.alloc(8);
  b.writeDoubleLE(x);
  return b.readBigUInt64LE().toString(16).padStart(16, "0");
};

function exportMath(): void {
  const fns = mathFunctionsUsed();
  const lines: string[] = [];
  for (const name of fns) for (const v of vectorsFor(name, 1000)) lines.push(JSON.stringify({ fn: name, args: v.args.map(bits), out: bits(v.out) }));
  writeFileSync(join(OUT, "math-vectors.jsonl.gz"), gzipSync(lines.join("\n") + "\n", { level: 9 }));
  writeFileSync(join(OUT, "math-functions.json"), JSON.stringify({ engine: `node ${process.version} (V8 ${process.versions.v8})`, functions: fns, vectorsPerFunction: "at least 1000 (every pair of specials for two-argument functions)", note: "the ** operator has Math.pow's semantics: jsmath::pow covers it" }, null, 2) + "\n");
  console.log(`math: ${fns.length} functions (${fns.join(", ")}), ${lines.length} vectors`);
}

// ------------------------------------------------------------------ the content hash

function exportContent(): void {
  const lib = loadContent(readContentFiles());
  const value = toCanonicalValue(lib);
  const hash = hex64(canonicalHash(value));
  writeFileSync(join(OUT, "content.json"), JSON.stringify({ hash, inputs: { content: treeHash("content") }, note: "FNV-1a 64 of the canonical serialization of loadContent(content/*.yaml) (Maps as objects keyed by their keys as strings, Sets as arrays in insertion order)" }, null, 2) + "\n");
  writeFileSync(join(OUT, "content.json.gz"), gzipSync(JSON.stringify(canonicalJson(value)), { level: 9 }));
  console.log(`content: ${hash}`);
}

/** Maps become objects (keys as strings), Sets arrays in insertion order: what the content library holds, as plain data. */
function toCanonicalValue(v: unknown): unknown {
  if (v instanceof Map) return Object.fromEntries([...v].map(([k, x]) => [String(k), toCanonicalValue(x)]));
  if (v instanceof Set) return [...v].map(toCanonicalValue);
  if (ArrayBuffer.isView(v)) return Array.from(v as unknown as ArrayLike<number>);
  if (Array.isArray(v)) return v.map(toCanonicalValue);
  if (v && typeof v === "object") return Object.fromEntries(Object.entries(v).map(([k, x]) => [k, toCanonicalValue(x)]));
  return v;
}

// ------------------------------------------------------------------ the truncation check

/**
 * The truncation check, run once on TS (docs/design/native-app.md, "Causality checks"): for a cut
 * C, every stream entry known after C is deleted (the song's length and knownLength held, as TS
 * can't run without them); the keys-only run up to C must match the full stream's. Each failure is
 * a read of the stream's end the perturbation check never exposed.
 */
export function truncateStream(stream: ReactiveStream, C: number): ReactiveStream {
  const known = <T extends { knownAt: number }>(l: readonly T[]) => l.filter((e) => e.knownAt <= C);
  return {
    ...stream,
    beats: known(stream.beats),
    ticks: stream.ticks.filter((k) => k.t <= C),
    onsets: known(stream.onsets),
    ...(stream.hindsight ? { hindsight: known(stream.hindsight) } : {}),
    ...(stream.timbre ? { timbre: known(stream.timbre) } : {}),
    ...(stream.structure ? { structure: { changes: known(stream.structure.changes), jumps: known(stream.structure.jumps), ends: known(stream.structure.ends) } } : {}),
    ...(stream.accents ? { accents: { forecast: known(stream.accents.forecast) } } : {}),
  };
}

const CHECK_LEVEL = "levels/reactive-check.yaml";
const CHECKS = { noHeroDamage: true } as const;
const CHECK_PLAYERS: SkillProfileName[] = ["reactive-moderate", "mash4"];

/**
 * As checkReactiveCausality runs the perturbation check (the same levels, checks, players and
 * default cuts, frames at 30 fps over the cut's window), but deleting the entries known after each
 * cut: on causality-mix with the check level, and on the Galop with the reactive level. A negative
 * control (the simulation reading the evidence a second ahead) must fail it.
 */
function truncationCheck(): void {
  const results: Record<string, unknown>[] = [];
  const cases = [
    { track: "causality-mix", level: CHECK_LEVEL, leak: false },
    { track: "orpheus-galop", level: undefined, leak: false },
    { track: "causality-mix", level: CHECK_LEVEL, leak: true },
  ];
  for (const c of cases) {
    const { stream, reference } = reactiveInput(readStreamFile(c.track), { level: c.level, checks: CHECKS });
    const inputFor = (st: ReactiveStream, leak = false) => inputForStream(st, { level: c.level, checks: leak ? { ...CHECKS, leakFutureEvidence: true } : CHECKS, knownLength: stream.audio.duration });
    const input = inputFor(stream);
    const failures: { player: string; C: number; diff: string }[] = [];
    let checked = 0;
    for (const name of CHECK_PLAYERS) {
      const own = liveRun(input, props(), { player: reactivePlayerFor(prepare(input).ctx, reference as Reference, name) });
      const keys = keysOf(own.recording.plan);
      const truth = liveRun(input, props(), { keys, frames: { from: 0, to: stream.audio.duration - 0.5 } });
      const cuts = defaultCuts(stream, truth.recording.plan, stream.audio.duration - 0.5);
      let prev = 0;
      for (const C of cuts) {
        checked++;
        const run = liveRun(inputFor(truncateStream(stream, C), c.leak), props(), { keys, cutS: C, untilS: C, frames: { from: Math.max(0, prev - SPECULATE_S - 0.05), to: C - SPECULATE_S } });
        prev = C;
        const diff = reactivePrefixDifference(truth, run, C);
        if (diff) {
          failures.push({ player: name, C, diff });
          if (c.leak) break;
        }
      }
      if (c.leak && failures.length) break;
    }
    const label = `${c.track}${c.leak ? " (negative control)" : ""}`;
    results.push({ track: c.track, level: c.level ?? REACTIVE_LEVEL, negativeControl: c.leak, players: CHECK_PLAYERS, cuts: checked, failures });
    console.log(`truncation ${label}: ${checked} cuts, ${failures.length} failing`);
    for (const f of failures.slice(0, 40)) console.log(`  ${f.player} C=${f.C.toFixed(3)}: ${f.diff}`);
  }
  const control = results[2] as { failures: unknown[] };
  if (control.failures.length === 0) throw new Error("truncation: the negative control passed, so the check proves nothing");
  writeFileSync(join(OUT, "truncation.json"), JSON.stringify(results, null, 2) + "\n");
}

// ------------------------------------------------------------------ reference stills

/** About 20 moments on the core set, as the TS renderer draws the virtual player's run (`npm run capture`). */
const STILLS: { track: string; profile: SkillProfileName; times: number[] }[] = [
  // The Galop: the free prelude, the vamp, the opening, theme A, the kick line, the bridge, the climax, the finale.
  { track: "orpheus-galop", profile: "reactive-moderate", times: [20, 47, 58, 72, 92, 104, 118, 132, 144.8] },
  { track: "orpheus-galop", profile: "mash4", times: [80] },
  { track: "house-128", profile: "reactive-moderate", times: [20, 50] },
  { track: "dnb-174", profile: "reactive-moderate", times: [35] },
  { track: "pop-arrangement", profile: "reactive-moderate", times: [70, 150] },
  { track: "notjam-breakbeat-chips", profile: "reactive-moderate", times: [40] },
  { track: "notjam-waltz-of-the-lich-king", profile: "reactive-moderate", times: [60] },
  { track: "cc-kimiko-ishizaka-aria", profile: "reactive-moderate", times: [60] },
  { track: "cc-kevin-macleod-black-vortex", profile: "reactive-moderate", times: [90] },
];

async function exportStills(): Promise<void> {
  const dir = join(OUT, "stills");
  rmSync(dir, { recursive: true, force: true });
  mkdirSync(dir, { recursive: true });
  const tmp = join(dir, ".capture");
  const index: { file: string; track: string; profile: string; t: number; query: string }[] = [];
  for (const st of STILLS) {
    const query = `mode=reactive&track=${st.track}&player=${st.profile}`;
    rmSync(tmp, { recursive: true, force: true });
    const code = await new Promise<number>((done) => {
      const child = spawn("npm", ["run", "capture", "--", "--query", query, "--times", st.times.join(","), "--out", tmp], { cwd: ROOT, stdio: ["ignore", "inherit", "inherit"] });
      child.on("exit", (c) => done(c ?? 1));
    });
    if (code !== 0) throw new Error(`capture failed for ${query}`);
    const shots = readdirSync(tmp).filter((f) => f.endsWith(".png")).sort();
    if (shots.length !== st.times.length) throw new Error(`capture wrote ${shots.length} stills for ${st.times.length} times (${query})`);
    // (capture names them by time, so sorted names follow the sorted times.)
    [...st.times].sort((a, b) => a - b).forEach((t, i) => {
      const file = `${st.track}-${st.profile}-${t.toFixed(2)}.png`;
      writeFileSync(join(dir, file), readFileSync(join(tmp, shots[i])));
      index.push({ file, track: st.track, profile: st.profile, t, query });
    });
  }
  rmSync(tmp, { recursive: true, force: true });
  writeFileSync(join(dir, "index.json"), JSON.stringify({ tag: TAG, width: 1920, height: 1080, stills: index }, null, 2) + "\n");
  console.log(`stills: ${index.length}`);
}

// ------------------------------------------------------------------ sizes

function sizes(): void {
  let total = 0;
  const by: Record<string, number> = {};
  for (const p of filesUnder(OUT)) {
    const n = statSync(p).size;
    total += n;
    const key = relative(OUT, p).split("/")[0];
    by[key] = (by[key] ?? 0) + n;
  }
  for (const [k, n] of Object.entries(by)) console.log(`${k.padEnd(28)} ${(n / 1e6).toFixed(2)} MB`);
  console.log(`total                        ${(total / 1e6).toFixed(2)} MB`);
}

// ------------------------------------------------------------------ the command

async function runAll(only: string[] | undefined, jobs: number): Promise<void> {
  const specs = runSpecs().filter((s) => !only || only.includes(`${s.track}/${s.profile}`));
  if (jobs <= 1 || specs.length <= 1) {
    for (const s of specs) console.log(JSON.stringify(exportRun(s)));
    return;
  }
  // Fan out: each child exports a slice of the runs.
  const queue = [...specs];
  const failures: string[] = [];
  await Promise.all(
    Array.from({ length: Math.min(jobs, specs.length) }, async () => {
      for (let s = queue.shift(); s; s = queue.shift()) {
        const id = `${s.track}/${s.profile}`;
        const code = await new Promise<number>((done) => {
          const child = spawn(process.execPath, [...process.execArgv, process.argv[1], "runs", "--only", id, "--jobs", "1"], { stdio: ["ignore", "inherit", "inherit"] });
          child.on("exit", (c) => done(c ?? 1));
        });
        if (code !== 0) failures.push(id);
      }
    }),
  );
  if (failures.length) throw new Error(`failed: ${failures.join(", ")}`);
}

async function main(): Promise<void> {
  const args = process.argv.slice(2);
  const command = args[0] && !args[0].startsWith("--") ? args[0] : "all";
  const opt = (k: string) => {
    const i = args.indexOf(k);
    return i >= 0 ? args[i + 1] : undefined;
  };
  const only = opt("--only")?.split(",");
  const jobs = Number(opt("--jobs") ?? Math.max(1, Math.floor(availableParallelism() / 2)));
  mkdirSync(OUT, { recursive: true });
  if (command === "runs" || command === "all") {
    if (command === "all" && existsSync(FIXTURES)) rmSync(FIXTURES, { recursive: true });
    await runAll(only, jobs);
  }
  if (command === "math" || command === "all") exportMath();
  if (command === "content" || command === "all") exportContent();
  if (command === "truncation") truncationCheck();
  if (command === "stills") await exportStills();
  if (command === "sizes" || command === "all") sizes();
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await main();
