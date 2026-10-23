/**
 * Reactive mode's checks (docs/design/reactive-audio.md, "Causality" and "Automated Checks").
 *
 * The simulation receives the whole belief stream, so causality is proved, not built in: for a cut
 * at song time C, every stream entry known after C is changed (deterministically, seeded by the
 * track and C), the song map, Ctx and Schedule are rebuilt from the changed stream, the same key
 * events up to C are replayed, and the live recording (simulation, sounds) and the evaluator's
 * frames must match the true run's before C.
 *
 * Pure functions of their inputs: the command (scripts/check-reactive.ts) reads the files.
 */
import type { PropInfo } from "../mapgen/mapgen.ts";
import { Rng } from "../core/rng.ts";
import { Warp } from "../core/warp.ts";
import { LiveRecording } from "../live/recording.ts";
import { LiveRunner, SPECULATE_S } from "../live/runner.ts";
import { handOver, liveSim, type BotFor } from "../live/session.ts";
import { Evaluator } from "../plan/evaluate.ts";
import type { Plan } from "../plan/types.ts";
import type { StreamBeat, StreamOnset, ReactiveStream, Reference } from "../reactive/stream.ts";
import type { KeyEvent, Player } from "../sim/player.ts";
import { Sim, worldFor } from "../sim/simulate.ts";
import type { CheckResult } from "./checks.ts";
import { soundRows } from "./checks.ts";
import type { PlannerInput } from "./context.ts";
import { GATE_EXEMPT_SOUNDS } from "./emit.ts";
import { planHash, planWithSim, prepare } from "./plan.ts";

// ------------------------------------------------------------------ the perturbation

/**
 * The stream with every entry known after C changed (seeded by `seed` and C), as the perturbation
 * check prescribes: committed beats lose every fifth, gain one in every seventh gap and move by up
 * to 35% of their period, with every field changed and each staying strictly increasing and later
 * than both max(knownAt, C) + 0.1 s and the last unchanged beat plus a quarter period; ticks get
 * seeded evidence and energy; onsets move by up to 50 ms, get seeded strengths and strong flags,
 * and lose every third; and the song lasts C plus 10 to 60 s.
 */
export function perturbStream(stream: ReactiveStream, C: number, seed: string): ReactiveStream {
  const r = new Rng(0x5eed, "perturb", seed, Math.round(C * 1e6));
  const keep = stream.beats.filter((b) => b.knownAt <= C);
  const later = stream.beats.filter((b) => b.knownAt > C);
  const beats: StreamBeat[] = [...keep];
  const lastKept = keep[keep.length - 1];
  let prevT = lastKept ? lastKept.t : -Infinity;
  let prevPeriod = lastKept ? lastKept.period : (later[0]?.period ?? 0.5);
  let prevKnown = C;
  const push = (t0: number, known0: number, period0: number) => {
    const knownAt = Math.max(prevKnown, C + 1e-4, known0);
    const t = Math.max(t0, Math.max(knownAt, C) + 0.1 + 1e-4, prevT + 0.25 * prevPeriod + 1e-4);
    const period = period0 * r.range(0.8, 1.25);
    beats.push({ t, knownAt, period, evidence: r.next() });
    prevT = t;
    prevPeriod = period;
    prevKnown = knownAt;
  };
  later.forEach((b, i) => {
    // An extra beat in every seventh gap.
    if (i % 7 === 6) push((prevT + b.t) / 2, b.knownAt + r.range(-0.3, 0), b.period);
    if (i % 5 === 4) return;
    push(b.t + r.range(-0.35, 0.35) * b.period, b.knownAt + r.range(-0.3, 0.3), b.period);
  });
  const duration = C + r.range(10, 60);
  const ticks = stream.ticks.filter((k) => k.t <= duration + 1e-9).map((k) => (k.t <= C ? k : { t: k.t, evidence: r.next(), energy: r.next() }));
  const maxStrength = Math.max(1, ...stream.onsets.map((o) => o.strength));
  const onsets: StreamOnset[] = [];
  let n = 0;
  for (const o of stream.onsets) {
    if (o.knownAt <= C) {
      onsets.push(o);
      continue;
    }
    if (n++ % 3 === 2) continue;
    onsets.push({ t: o.t + r.range(-0.05, 0.05), knownAt: o.knownAt + r.range(-0.02, 0.02), strength: r.range(0, maxStrength), strong: r.next() < 0.3 });
  }
  // (Every field changes, knownAt too, staying after C and in order.)
  let lastKnown = C + 1e-4;
  for (const o of onsets) {
    if (o.knownAt <= C && stream.onsets.includes(o)) continue;
    o.knownAt = Math.max(lastKnown, o.knownAt);
    lastKnown = o.knownAt;
  }
  return { ...stream, audio: { ...stream.audio, duration }, beats, ticks, onsets };
}

// ------------------------------------------------------------------ runs

export interface LiveRun {
  recording: LiveRecording;
  /** The evaluator's frames (JSON) by frame number (song time k / 30), as live play drew them. */
  frames: Map<number, string>;
}

/**
 * A live simulation and its recording (as play mode records it), driven by a player or by key
 * events. With `frames`, the evaluator's frame at each 1/30 s in [from, to) is taken as live play
 * draws it: with the simulation SPECULATE_S ahead, and the recording as it stood then.
 */
export function liveRun(input: PlannerInput, props: Record<string, PropInfo> | undefined, opts: { player?: Player; keys?: readonly KeyEvent[]; cutS?: number; untilS?: number; frames?: { from: number; to: number } }): LiveRun {
  const { ctx, sch, map } = prepare(input);
  const recording = new LiveRecording(ctx, sch, map, ctx.lib, "player", props);
  const sim = new Sim(ctx, sch, worldFor(), recording, { ...(opts.player ? { player: opts.player } : {}), cutS: opts.cutS, untilS: opts.untilS });
  for (const k of opts.keys ?? []) sim.key(k);
  const frames = new Map<number, string>();
  if (opts.frames) {
    const ev = new Evaluator(recording.plan, ctx.lib, recording);
    for (let k = Math.max(0, Math.ceil(opts.frames.from * FRAME_RATE - 1e-9)); k / FRAME_RATE < opts.frames.to - 1e-9; k++) {
      const s = k / FRAME_RATE;
      sim.advanceTo(Math.min(sim.endS, s + SPECULATE_S));
      recording.refresh();
      const f = ev.evaluate(s);
      // (The frame's section record carries the section's bounds, the reactive section's end being
      // the file's length, which no frame reads: its other fields are compared.)
      frames.set(k, JSON.stringify({ ...f, section: { ...f.section, s0: 0, s1: 0 } }));
    }
  }
  sim.runToEnd();
  recording.refresh();
  return { recording, frames };
}

/** The key events a recording's inputs replay. */
export function keysOf(plan: Plan): KeyEvent[] {
  return plan.inputs.map((i) => ({ s: i.s, kind: i.kind, key: i.key }));
}

/** A sixteenth-note slot's sounds are final half a slot plus 60 ms after its centre (the live recording closes it then). */
const SOUND_SETTLE = 0.25;
const FRAME_RATE = 30;

/** The first difference between two runs before song time C, or undefined. Frames are compared wherever both runs took one. */
export function reactivePrefixDifference(ra: LiveRun, rb: LiveRun, C: number): string | undefined {
  const pa = ra.recording.plan;
  const pb = rb.recording.plan;
  const warp = new Warp(pa.warp);
  const wC = warp.toWorld(C);
  const same = (x: unknown, y: unknown) => JSON.stringify(x) === JSON.stringify(y);
  for (let i = 0; i < pa.warp.length && pa.warp[i].s < C; i++) {
    const x = pa.warp[i];
    const y = pb.warp[i];
    if (!y || x.s !== y.s || x.w !== y.w || x.rate !== y.rate) return `warp break ${i} at s=${x.s.toFixed(4)} differs`;
  }
  const kC = Math.floor(wC * pa.hero.rate - 1e-6);
  for (let k = 0; k <= kC && k < pa.hero.x.length; k++) {
    if (pa.hero.x[k] !== pb.hero.x[k] || pa.hero.y[k] !== pb.hero.y[k] || pa.hero.anim[k] !== pb.hero.anim[k] || pa.hero.frame[k] !== pb.hero.frame[k] || pa.hero.facing[k] !== pb.hero.facing[k]) return `hero sample ${k} (w=${(k / pa.hero.rate).toFixed(3)}) differs`;
  }
  for (let k = 0; k <= kC && k < pa.camera.x.length; k++) {
    if (pa.camera.x[k] !== pb.camera.x[k] || pa.camera.y[k] !== pb.camera.y[k] || pa.camera.zoom[k] !== pb.camera.zoom[k]) return `camera sample ${k} differs`;
  }
  const before = <T extends { s: number }>(l: readonly T[], t = C) => l.filter((x) => x.s < t);
  // Records completed later (an orb taken, a chicken eaten) as they stood at C.
  const upgradesAtC = (p: Plan) => p.upgrades.map((u) => (u.sTaken !== undefined && u.sTaken < C ? u : { s: u.s, offers: u.offers, taken: -1, w: u.w, orbs: u.orbs }));
  const chickensAtC = (p: Plan) => p.chickens.map((c) => (c.sTaken !== undefined && c.sTaken < C ? c : { s: c.s, w: c.w, x: c.x, y: c.y }));
  const vacuumsAtC = (p: Plan) => p.vacuums.map((v) => (v.sTouch !== undefined && v.sTouch < C ? v : { s: v.s, w: v.w, x: v.x, y: v.y }));
  const lists: [string, { s: number }[], { s: number }[]][] = [
    ["inputs", pa.inputs, pb.inputs],
    ["hits", pa.hits, pb.hits],
    ["swings", pa.swings, pb.swings],
    ["upgrades", upgradesAtC(pa), upgradesAtC(pb)],
    ["zone waves", pa.open.waves, pb.open.waves],
    ["weapons", pa.weapons, pb.weapons],
    ["bites", pa.bites, pb.bites],
    ["heals", pa.heals, pb.heals],
    ["chickens", chickensAtC(pa), chickensAtC(pb)],
    ["ui events", pa.events.ui, pb.events.ui],
    ["effects", pa.events.effects, pb.events.effects],
    ["vacuums", vacuumsAtC(pa), vacuumsAtC(pb)],
    ["near misses", pa.nearMisses, pb.nearMisses],
    ["threats", pa.threats, pb.threats],
    ["landmarks", pa.open.landmarks, pb.open.landmarks],
  ];
  for (const [name, x, y] of lists) if (!same(before(x), before(y))) return `${name} before s=${C.toFixed(3)} differ`;
  const series: [string, { s: ArrayLike<number>; v: ArrayLike<number> } | undefined, { s: ArrayLike<number>; v: ArrayLike<number> } | undefined][] = [
    ["groove", pa.groove, pb.groove],
    ["tier", pa.tier, pb.tier],
    ["health", pa.health, pb.health],
  ];
  for (const [name, x, y] of series) {
    if (!x || !y) continue;
    for (let i = 0; i < x.s.length && x.s[i] < C; i++) if (x.s[i] !== y.s[i] || x.v[i] !== y.v[i]) return `${name} at s=${x.s[i].toFixed(3)} differs`;
    for (let i = 0; i < y.s.length && y.s[i] < C; i++) if (x.s[i] !== y.s[i]) return `${name} at s=${y.s[i].toFixed(3)} differs`;
  }
  // The sounds: every closed slot's sound events (sample, slot and gain) before C.
  const sounds = (p: Plan) => p.events.sounds.filter((x) => x.s < C - SOUND_SETTLE).map((x) => [x.sample, x.s, x.gain, x.key]);
  if (!same(sounds(pa), sounds(pb))) return `sounds before s=${(C - SOUND_SETTLE).toFixed(3)} differ`;
  // (A gem's value and size grow as later gems join it on the field: the frames show it as it stood.)
  const gems = (p: Plan) => p.events.gems.filter((g) => g.w0 < wC).map((g) => [g.w0, g.x0, g.y0, g.seed]);
  if (!same(gems(pa), gems(pb))) return `gems dropped before w=${wC.toFixed(3)} differ`;
  // Arrows and slam markers as they started; lightning chains' points so far.
  const projectiles = (p: Plan) => p.projectiles.filter((x) => x.w0 < wC).map((x) => [x.kind, x.w0, x.x0, x.y0]);
  if (!same(projectiles(pa), projectiles(pb))) return `projectiles before w=${wC.toFixed(3)} differ`;
  const slams = (p: Plan) => p.slams.filter((x) => x.w0 < wC);
  if (!same(slams(pa), slams(pb))) return `slam markers before w=${wC.toFixed(3)} differ`;
  const chains = (p: Plan) => p.chains.map((c) => c.points.filter((q) => q.w < wC)).filter((pts) => pts.length > 0);
  if (!same(chains(pa), chains(pb))) return `lightning chains before w=${wC.toFixed(3)} differ`;
  // The HUD's series: kills, Perfect presses, experience, level-ups and weapon fires.
  const upTo = (l: ArrayLike<number>) => Array.from(l).filter((t) => t < C);
  const hud = (p: Plan) => ({ kills: upTo(p.hud.kills), perfect: upTo(p.hud.perfect), xp: Array.from(p.hud.xpFill).slice(0, upTo(p.hud.xpTimes).length), xpTimes: upTo(p.hud.xpTimes), levelUps: upTo(p.hud.levelUps), fires: Object.fromEntries(Object.entries(p.hud.weaponFires).map(([k, v]) => [k, upTo(v)]).filter(([, v]) => v.length > 0)) });
  {
    const ha = hud(pa);
    const hb = hud(pb);
    const d = firstDifference(JSON.parse(JSON.stringify(ha)), JSON.parse(JSON.stringify(hb)), "hud");
    if (d !== undefined) return `the HUD's series before s=${C.toFixed(3)}: ${d} differs`;
  }
  const rate = pa.enemySampleRate;
  const jC = Math.floor(wC * rate - 1e-6);
  const samplesOf = (p: Plan, e: Plan["enemies"][number]) => e.samples ?? p.enemySamples.subarray(e.sampleOffset * 2, (e.sampleOffset + e.sampleCount) * 2);
  for (const ea of pa.enemies) {
    if (ea.spawnW >= wC) continue;
    const eb = pb.enemies[ea.id];
    if (!eb || eb.type !== ea.type || eb.spawnW !== ea.spawnW) return `enemy #${ea.id} spawns differently`;
    if (!same(ea.fates.filter((f) => f.w < wC), eb.fates.filter((f) => f.w < wC))) return `enemy #${ea.id}'s hits before w=${wC.toFixed(2)} differ`;
    if ((ea.deathW < wC || eb.deathW < wC) && ea.deathW !== eb.deathW) return `enemy #${ea.id} dies at a different time`;
    const k0 = Math.round(ea.sampleW0 * rate);
    if (Math.round(eb.sampleW0 * rate) !== k0) return `enemy #${ea.id}'s samples start differently`;
    const sa = samplesOf(pa, ea);
    const sb = samplesOf(pb, eb);
    for (let j = k0; j <= jC && j - k0 < ea.sampleCount; j++) {
      const i = (j - k0) * 2;
      if (j - k0 >= eb.sampleCount) return `enemy #${ea.id} has fewer samples before w=${wC.toFixed(2)}`;
      if (sa[i] !== sb[i] || sa[i + 1] !== sb[i + 1]) return `enemy #${ea.id} sample at w=${(j / rate).toFixed(3)} differs`;
    }
  }
  for (const eb of pb.enemies) if (eb.spawnW < wC && !pa.enemies[eb.id]) return `enemy #${eb.id} spawns only in the changed run`;
  // The frames: the evaluator's frame state (HUD values, beat phase, pulses and effects) every 1/30 s.
  for (const [k, fb] of [...rb.frames].sort((x, y) => x[0] - y[0])) {
    const fa = ra.frames.get(k);
    if (fa === undefined || fa === fb) continue;
    return `frame at s=${(k / FRAME_RATE).toFixed(3)}: ${firstDifference(JSON.parse(fa), JSON.parse(fb), "")} differs`;
  }
  return undefined;
}

/** The path of the first difference between two JSON-like values, or undefined. */
function firstDifference(a: unknown, b: unknown, path: string): string | undefined {
  if (a === b) return undefined;
  if (typeof a !== "object" || typeof b !== "object" || a === null || b === null) return `${path || "value"} (${JSON.stringify(a)?.slice(0, 60)} vs ${JSON.stringify(b)?.slice(0, 60)})`;
  if (Array.isArray(a) !== Array.isArray(b)) return path || "value";
  const keys = new Set([...Object.keys(a), ...Object.keys(b)]);
  for (const k of keys) {
    const d = firstDifference((a as Record<string, unknown>)[k], (b as Record<string, unknown>)[k], path ? `${path}.${k}` : k);
    if (d !== undefined) return d;
  }
  return undefined;
}

// ------------------------------------------------------------------ the causality check

export interface CausalityCase {
  /** A name for the track (seeds the perturbation). */
  track: string;
  stream: ReactiveStream;
  /** The planner input for a stream (the check rebuilds Ctx and Schedule from each changed one). */
  inputFor: (stream: ReactiveStream) => PlannerInput;
  props?: Record<string, PropInfo>;
  /** The players' runs to replay: the key events of each player's own run. */
  players: { name: string; player: (input: PlannerInput) => Player }[];
  /** Cuts (default: just before every committed beat's and strong onset's knownAt, and just after every level-up and zone wave). */
  cuts?: number[];
  /** Stop at the first failing cut (the negative control only needs one). */
  stopAtFirst?: boolean;
}

export interface CausalityResult extends CheckResult {
  /** Feature classes that fired before at least one cut, and those that never did. */
  fired: string[];
  missing: string[];
  ms: number;
}

/** The feature classes stage 1 has: each must fire before a cut, or the check proves nothing about it. */
export const STAGE1_FEATURES = ["level-ups", "zone waves"] as const;

/** Default cuts: just before each committed beat's and strong onset's knownAt, and just after each level-up and zone wave. */
export function defaultCuts(stream: ReactiveStream, truePlan: Plan, until: number): number[] {
  const raw = [
    ...stream.beats.map((b) => b.knownAt - 0.001),
    ...stream.onsets.filter((o) => o.strong).map((o) => o.knownAt - 0.001),
    ...truePlan.upgrades.map((u) => u.s + 0.001),
    ...truePlan.open.waves.map((w) => w.s + 0.001),
  ].filter((c) => c > 0.05 && c < until);
  raw.sort((a, b) => a - b);
  const out: number[] = [];
  for (const c of raw) if (out.length === 0 || c - out[out.length - 1] > 0.002) out.push(c);
  return out;
}

export function checkReactiveCausality(c: CausalityCase): CausalityResult {
  const t0 = performance.now();
  const input = c.inputFor(c.stream);
  const failures: string[] = [];
  const firedBefore = new Set<string>();
  let checked = 0;
  for (const pl of c.players) {
    // The player's own run gives the key events; the true run replays them, as each changed run does.
    const own = liveRun(input, c.props, { player: pl.player(input) });
    const keys = keysOf(own.recording.plan);
    const truth = liveRun(input, c.props, { keys, frames: { from: 0, to: c.stream.audio.duration - 0.5 } });
    const truthPlan = truth.recording.plan;
    const cuts = c.cuts ?? defaultCuts(c.stream, truthPlan, c.stream.audio.duration - 0.5);
    const last = cuts[cuts.length - 1] ?? 0;
    if (truthPlan.upgrades.some((u) => u.s < last)) firedBefore.add("level-ups");
    if (truthPlan.open.waves.some((w) => w.s < last)) firedBefore.add("zone waves");
    let prev = 0;
    for (const C of cuts) {
      checked++;
      const changed = perturbStream(c.stream, C, c.track);
      // Frames from just before the previous cut's window ended, up to where the simulation, run ahead, still has C behind it.
      const run = liveRun(c.inputFor(changed), c.props, { keys, cutS: C, untilS: C, frames: { from: Math.max(0, prev - SPECULATE_S - 0.05), to: C - SPECULATE_S } });
      const diff = reactivePrefixDifference(truth, run, C);
      prev = C;
      if (diff) {
        failures.push(`${pl.name}, cut at s=${C.toFixed(3)}: ${diff}`);
        if (c.stopAtFirst) break;
      }
    }
    if (c.stopAtFirst && failures.length) break;
  }
  const missing = STAGE1_FEATURES.filter((f) => !firedBefore.has(f));
  for (const f of missing) failures.push(`no ${f} fired before a cut: the check proves nothing about them`);
  return { name: `causality (${c.track})`, failures, checked, fired: [...firedBefore], missing, ms: performance.now() - t0 };
}

// ------------------------------------------------------------------ the other gates

/** Determinism: the same stream and player simulated twice give the same plan hash. */
export function checkDeterminism(input: PlannerInput, player: () => Player, props?: Record<string, PropInfo>): CheckResult {
  const a = planHash(planWithSim(input, { props, player: player() }).plan);
  const b = planHash(planWithSim(input, { props, player: player() }).plan);
  return { name: "determinism", failures: a === b ? [] : [`plan hashes differ: ${a} vs ${b}`], checked: 1 };
}

/** Share of committed beats (at or after `after`, within the reference's span) within `tol` of a reference beat. */
export function beatsWithin(stream: ReactiveStream, ref: Reference, tol = 0.045, after = 0, range?: [number, number]): { share: number; n: number } {
  const r = ref.beats;
  if (r.length < 2) return { share: 0, n: 0 };
  const lo = Math.max(after, r[0] - 0.5 * (r[1] - r[0]), range?.[0] ?? -Infinity);
  const hi = Math.min(r[r.length - 1] + 0.5 * (r[r.length - 1] - r[r.length - 2]), range?.[1] ?? Infinity);
  let n = 0;
  let hit = 0;
  for (const b of stream.beats) {
    if (b.t < lo || b.t > hi) continue;
    n++;
    let a = 0;
    let z = r.length - 1;
    while (z - a > 1) {
      const m = (a + z) >> 1;
      if (r[m] <= b.t) a = m;
      else z = m;
    }
    if (Math.min(Math.abs(r[a] - b.t), Math.abs(r[z] - b.t)) <= tol + 1e-9) hit++;
  }
  return { share: n ? hit / n : 0, n };
}

/** Share of the committed beats within a song-time range whose evidence is below the gate. */
export function belowGateShare(stream: ReactiveStream, from: number, to: number): { share: number; n: number } {
  const bs = stream.beats.filter((b) => b.t >= from && b.t < to);
  return { share: bs.length ? bs.filter((b) => b.evidence < stream.params.gate).length / bs.length : 0, n: bs.length };
}

/**
 * Sounds: every percussive gameplay sound lies on a committed-beat slot of its subdivision within
 * 1 ms, and none belongs to a beat committed below the gate, apart from the exempt sounds.
 */
export function checkReactiveSounds(plan: Plan, stream: ReactiveStream, lib: PlannerInput["content"]): CheckResult {
  const rows = soundRows(lib);
  const beats = stream.beats.map((b) => b.t);
  const failures: string[] = [];
  let checked = 0;
  const owner = (s: number) => {
    let lo = -1;
    let hi = beats.length;
    while (hi - lo > 1) {
      const m = (lo + hi) >> 1;
      if (beats[m] <= s + 1e-6) lo = m;
      else hi = m;
    }
    return lo;
  };
  const timeAt = (bi: number) => {
    const k = Math.max(0, Math.min(beats.length - 2, Math.floor(bi)));
    return beats[k] + (bi - k) * (beats[k + 1] - beats[k]);
  };
  for (const snd of plan.events.sounds) {
    if (snd.key === undefined) continue;
    const row = rows.get(snd.key);
    const i = owner(snd.s);
    if (row) {
      checked++;
      const every = row === "kill" ? "sixteenth" : row === "grid" ? "beat" : row;
      let ok: boolean;
      if (every === "bar") ok = i >= 0 && i % 2 === 0 && Math.abs(beats[i] - snd.s) <= 0.001;
      else {
        const per = every === "sixteenth" ? 4 : every === "eighth" ? 2 : 1;
        const bi = i + (i >= 0 && i + 1 < beats.length ? (snd.s - beats[i]) / (beats[i + 1] - beats[i]) : 0);
        ok = i >= 0 && Math.abs(timeAt(Math.round(bi * per) / per) - snd.s) <= 0.001;
      }
      if (!ok) failures.push(`${snd.key} sound at s=${snd.s.toFixed(3)} is off its ${every} slot of the committed beats`);
    }
    // A swing's whoosh belongs to its strike's beat (it leads the strike by 40 ms).
    const ownerS = snd.key === "swing" ? snd.s + 0.04 : snd.s;
    const j = owner(ownerS);
    if (!GATE_EXEMPT_SOUNDS.has(snd.key) && (j < 0 || stream.beats[j].evidence < stream.params.gate)) failures.push(`${snd.key} sound at s=${snd.s.toFixed(3)} plays on a beat committed below the gate`);
  }
  return { name: "sounds on committed beats", failures: failures.slice(0, 20).concat(failures.length > 20 ? [`… and ${failures.length - 20} more`] : []), checked };
}

/**
 * The headless smoke test: a reactive session through the live runner, as the browser plays it.
 * The level's virtual player fast-forwards to `fromS` (as `?start=` does) and hands over; then 10 s
 * of song time run a frame at a time with synthetic key presses on the committed beats. No errors,
 * and strikes, sounds and judged presses come out.
 */
export function smokeTest(input: PlannerInput, props: Record<string, PropInfo> | undefined, botFor: BotFor, fromS: number, seconds = 10): CheckResult {
  const failures: string[] = [];
  try {
    const { recording, sim } = liveSim(input, props, fromS > 0, botFor);
    sim.advanceTo(fromS);
    handOver(sim, recording);
    const runner = new LiveRunner(sim, recording);
    const beats = (input.reactive?.beats ?? []).map((b) => b.t).filter((t) => t > fromS + 0.5 && t < fromS + seconds - 0.5);
    const keys: KeyEvent[] = beats.flatMap((t, i) => {
      const key = (["right", "down", "left", "up"] as const)[i % 4];
      return [
        { s: t + 0.01, kind: "press" as const, key, id: 2 * i },
        { s: t + 0.09, kind: "release" as const, key, id: 2 * i + 1 },
      ];
    });
    for (let k = 1; k * (1 / 60) <= seconds; k++) {
      const now = fromS + k / 60;
      // Keys arrive at the frame after their time, as the keyboard delivers them.
      const due = keys.filter((e) => e.s <= now - 1 / 60 && e.s > now - 2 / 60);
      runner.frame(now, due);
      recording.refresh();
    }
    const plan = recording.plan;
    const inWindow = <T extends { s: number }>(l: readonly T[]) => l.filter((x) => x.s > fromS && x.s < fromS + seconds);
    const strikes = inWindow(plan.swings).length;
    const sounds = inWindow(plan.events.sounds).length;
    const judged = inWindow(plan.inputs).filter((i) => i.kind === "press" && !i.unjudged && i.class !== undefined).length;
    if (strikes === 0) failures.push("no strikes");
    if (sounds === 0) failures.push("no sounds");
    if (judged === 0) failures.push(`no judged presses (of ${inWindow(plan.inputs).filter((i) => i.kind === "press").length})`);
    if (failures.length === 0 && runner.applied.length < keys.length) failures.push(`${runner.applied.length} of ${keys.length} key events applied`);
  } catch (e) {
    failures.push(`error: ${e instanceof Error ? (e.stack ?? e.message) : String(e)}`);
  }
  return { name: "smoke test (live runner)", failures, checked: 1 };
}
