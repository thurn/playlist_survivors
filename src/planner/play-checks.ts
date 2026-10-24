/**
 * Play mode's own checks, on a recording of the open field. The shared
 * checks (checks.ts) run too; these add play mode's rules and the signature
 * moments a moderately competent player must meet. Every failure names the
 * time and entity involved.
 */
import type { ContentLibrary } from "../content/loader.ts";
import { waveRadius } from "../mapgen/openfield.ts";
import { gemTotals } from "../plan/evaluate.ts";
import { enemyPos, heroSample } from "../plan/sample.ts";
import { partnerPress, type Plan } from "../plan/types.ts";
import { viewRect } from "./camera.ts";
import { heldAt, heldSpans, type CheckResult } from "./checks.ts";
import { Warp } from "../core/warp.ts";

const fmt = (s: number) => `s=${s.toFixed(3)}`;

function beatIndex(beats: ArrayLike<number>, s: number): number {
  const n = beats.length;
  if (s <= beats[0]) return (s - beats[0]) / (beats[1] - beats[0]);
  if (s >= beats[n - 1]) return n - 1 + (s - beats[n - 1]) / (beats[n - 1] - beats[n - 2]);
  let lo = 0;
  let hi = n - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (beats[mid] <= s) lo = mid;
    else hi = mid;
  }
  return lo + (s - beats[lo]) / (beats[hi] - beats[lo]);
}

function nearest(times: ArrayLike<number>, s: number): number {
  let lo = 0;
  let hi = times.length - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (times[mid] <= s) lo = mid;
    else hi = mid;
  }
  return Math.abs(times[hi] - s) < Math.abs(times[lo] - s) ? hi : lo;
}

/** A kick-line skeleton standing a beat after the release has closed at least this far on the hero (px): it charges. (Charging, they close 36 px or more; held still, the crowd still shoves them up to about 15.) */
const KICK_CHARGE = 24;
/** At least this share of the kick-line skeletons standing at the release are dead within KICK_PAYOFF_BEATS of it. */
const KICK_PAYOFF = 1 / 2;
const KICK_PAYOFF_BEATS = 6;

/** A bend comes at most this long after the press whose stride it bends (the diagonal window, with room for a held stride taking over). */
const BEND_WINDOW = 0.05;

/**
 * Fixed-speed strides, for every player: no dead presses, the speed cap, and holding keeps full
 * speed. See "Automated Checks" in docs/design/gameplay-redesign.md.
 */
export function checkStrides(plan: Plan): CheckResult[] {
  const st = plan.strides;
  if (!st) return [];
  const results: CheckResult[] = [];
  const add = (name: string, failures: string[], checked: number) => results.push({ name, failures, checked });
  const t = plan.hero;
  const warp = new Warp(plan.warp);
  const presses = plan.inputs.filter((i) => i.kind === "press");

  // No dead presses: every press strides at once, bends the stride in progress, or stomps. A stride
  // is recorded, and the first hero samples after it show the hop of a stride that started at the
  // press (unless another stride started in between). A bend, or an opposite key's rock step, shows
  // the hop of the press stride just before it. A stomp stamps its ring of dust at the press, and
  // a stomp that rocked no stride stamps in place with a hop of its own.
  {
    const f: string[] = [];
    let n = 0;
    const held = heldSpans(plan.inputs);
    const stamps = plan.events.effects.filter((e) => e.recipe === "stomp").map((e) => e.s);
    const startW = [...presses.filter((p) => !partnerPress(p)).map((p) => p.w), ...Array.from(st.steps).filter((s) => heldAt(held, s)).map((s) => warp.toWorld(s))];
    for (const p of presses) {
      const k = Math.floor(p.w * t.rate + 1e-9) + 1;
      if (k >= t.z.length) continue;
      n++;
      const wk = k / t.rate;
      if (p.stomp && !stamps.some((s) => Math.abs(s - p.s) < 1e-6)) {
        f.push(`the stomp at ${fmt(p.s)} stamps no ring of dust`);
        continue;
      }
      if (partnerPress(p)) {
        // The stride it bends (or rocks) started at a press just before it (within the window), and is moving.
        const from = presses.some((q) => !partnerPress(q) && q.w <= p.w + 1e-9 && q.w >= p.w - BEND_WINDOW);
        if (!from || !(t.z[k] > 1e-6)) f.push(`the ${p.key} press at ${fmt(p.s)} ${p.bend ? "bends" : "rocks"} no stride in progress (no press stride just before it, or no hop at w=${wk.toFixed(3)})`);
        continue;
      }
      // (A stride rocked back near its end hardly moves: its ring of dust is the stomp's evidence.)
      if (p.stomp && p.rock) continue;
      if (p.stride === undefined && !p.stomp) {
        f.push(`the ${p.key} press at ${fmt(p.s)} records no stride or stomp`);
        continue;
      }
      // The two samples after the press, while no other stride has started: a stride ending can
      // pass through the same height on its way down as a new one on its way up, but not twice.
      const hop = (j: number) => st.hop * Math.sin(Math.PI * Math.min(1, (j / t.rate - p.w) / st.durationS));
      const free = (j: number) => !startW.some((w) => w > p.w + 1e-9 && w <= j / t.rate + 1e-9);
      if (!free(k)) {
        if (!(t.z[k] > 0)) f.push(`the ${p.key} press at ${fmt(p.s)} does not stride at once (no hop at w=${wk.toFixed(3)})`);
        continue;
      }
      for (const j of [k, k + 1]) {
        if (j >= t.z.length || (j > k && !free(j))) continue;
        if (Math.abs(t.z[j] - hop(j)) > 0.01 + 1e-4 * st.hop) {
          f.push(`the ${p.key} press at ${fmt(p.s)} does not stride at once: the hop at w=${(j / t.rate).toFixed(3)} is ${t.z[j].toFixed(3)} px, not ${hop(j).toFixed(3)}`);
          break;
        }
      }
    }
    add("no dead presses", f, n);
  }

  // The speed cap: over every window of time, the hero covers at most the walk speed times the
  // window, plus one stride and a pixel. (A window can only break the cap if the path walked in it,
  // which bounds its displacement, does: the path's lead over the walk speed, g, prunes the pairs.)
  {
    const f: string[] = [];
    const one = st.speed * Math.max(st.freePeriodS, ...st.periods);
    const m = t.x.length;
    const g = new Float64Array(m);
    for (let k = 1; k < m; k++) g[k] = g[k - 1] + Math.hypot(t.x[k] - t.x[k - 1], t.y[k] - t.y[k - 1]) - st.speed / t.rate;
    const ahead = new Float64Array(m + 1).fill(-Infinity);
    for (let k = m - 1; k >= 0; k--) ahead[k] = Math.max(g[k], ahead[k + 1]);
    let worst = { excess: 0, i: 0, j: 0, d: 0 };
    for (let i = 0; i < m; i++) {
      const bar = g[i] + one + 1;
      if (ahead[i + 1] <= bar) continue;
      for (let j = i + 1; j < m; j++) {
        if (g[j] <= bar) continue;
        const d = Math.hypot(t.x[j] - t.x[i], t.y[j] - t.y[i]);
        const excess = d - (st.speed * ((j - i) / t.rate) + one + 1);
        if (excess > worst.excess) worst = { excess, i, j, d };
      }
    }
    const n = m;
    if (worst.excess > 0) {
      const { i, j, d } = worst;
      f.push(`the hero covers ${d.toFixed(1)} px in ${((j - i) / t.rate).toFixed(3)} s from w=${(i / t.rate).toFixed(3)} (s=${warp.toSong(i / t.rate).toFixed(3)}), over the cap of ${(st.speed * ((j - i) / t.rate) + one + 1).toFixed(1)}`);
    }
    add("the speed cap", f, n);
  }

  // Holding keeps full speed: in step movement, a bar in which a key is held throughout (from the
  // step before it), with no press in it or since that step, covers the walk speed within 5%. Each held stride spends the ground built up since the stride
  // before, so the bar's strides cover the time from the step before the bar to its last step (in
  // world time: a hit-stop near the bar's edge moves ground between neighbouring bars).
  {
    const f: string[] = [];
    let n = 0;
    const spans = heldSpans(plan.inputs);
    const pressS = presses.map((p) => p.s);
    const b = plan.songBeats;
    for (let bar = 1; bar + 1 < plan.barStart.length; bar++) {
      const i0 = plan.barStart[bar];
      const i1 = plan.barStart[bar + 1];
      if (!Number.isFinite(i0) || !Number.isFinite(i1) || i1 >= b.length) continue;
      const s0 = b[i0];
      const s1 = b[i1];
      const sec = plan.sections.findLast((x) => s0 >= x.s0 - 1e-9);
      if (!sec || sec.movement !== "steps" || s1 > sec.s1 + 1e-6) continue;
      const j0 = st.steps.findIndex((x) => x > s0 - 1e-9);
      const j1 = st.steps.findIndex((x) => x > s1 - 1e-9);
      if (j0 < 1 || j1 <= j0) continue;
      const prevStep = st.steps[j0 - 1];
      const lastStep = st.steps[j1 - 1];
      if (!spans.some(([a, e]) => a <= prevStep + 1e-9 && e >= s1 - 1e-9)) continue;
      if (pressS.some((s) => s > prevStep - 1e-6 && s < s1)) continue;
      const w0 = warp.toWorld(s0);
      const w1 = warp.toWorld(s1);
      if (w1 - w0 < 1e-3) continue;
      // (A stride still moving when the bar starts, frozen by a hit-stop or slowed by slow motion
      // just before it, is taken over by the bar's first held stride, which adds the rest of its
      // ground: the bar covers more than the ground built since the step before.)
      if (w0 - warp.toWorld(prevStep) < st.durationS - 1e-9) continue;
      // The hero stands still just before each held stride: from the sample at or before the bar's
      // first stride to the one at or before the next bar's.
      const k0 = Math.floor(w0 * t.rate + 1e-9);
      const k1 = Math.min(t.x.length - 1, Math.floor(w1 * t.rate + 1e-9));
      n++;
      let covered = 0;
      for (let k = k0; k < k1; k++) covered += Math.hypot(t.x[k + 1] - t.x[k], t.y[k + 1] - t.y[k]);
      const want = st.speed * (warp.toWorld(lastStep) - warp.toWorld(prevStep));
      if (Math.abs(covered / want - 1) > 0.05) f.push(`holding through the bar at ${fmt(s0)} covers ${covered.toFixed(1)} px, not the walk speed's ${want.toFixed(1)} (within 5%)`);
    }
    add("holding keeps full speed", f, n);
  }
  return results;
}

export interface PlayCheckOptions {
  /** Require the signature moments (the moderate player's run). */
  signature: boolean;
}

export function checkPlay(plan: Plan, lib: ContentLibrary, opts: PlayCheckOptions): CheckResult[] {
  const results: CheckResult[] = [];
  const add = (name: string, failures: string[], checked: number) => results.push({ name, failures, checked });
  const beatLen = (s: number) => {
    const b = plan.songBeats;
    const k = Math.max(0, Math.min(b.length - 2, Math.floor(beatIndex(b, s))));
    return b[k + 1] - b[k];
  };

  results.push(...checkStrides(plan));

  // The sword swings on every grid time of the sections with a steady beat (until the finale blast
  // ends its routine).
  {
    const f: string[] = [];
    const struck = new Set(plan.swings.map((sw) => Math.round(sw.s * 1e4)));
    const blast = plan.hits.find((h) => h.source === "ring")?.s ?? Infinity;
    let n = 0;
    for (const sec of plan.sections) {
      if (sec.movement !== "steps") continue;
      for (const g of plan.grid) {
        if (g < sec.s0 - 1e-9 || g >= sec.s1 - 1e-9 || g >= blast - 0.04) continue;
        n++;
        if (!struck.has(Math.round(g * 1e4))) f.push(`no swing on the grid time ${fmt(g)} (${sec.label})`);
      }
    }
    add("the sword swings on every grid time", f, n);
  }

  // Zone waves: each starts on a downbeat and has left the screen within one bar.
  {
    const f: string[] = [];
    const downbeats = plan.barStart.filter((b) => Number.isFinite(b)).map((b) => plan.songBeats[b]);
    const warpAt = (s: number) => {
      const br = plan.warp;
      let k = 0;
      while (k + 1 < br.length && br[k + 1].s <= s) k++;
      return br[k].w + (s - br[k].s) * br[k].rate;
    };
    for (const wv of plan.open.waves) {
      if (Math.abs(downbeats[nearest(downbeats, wv.s)] - wv.s) > 0.001) f.push(`the transform wave at ${fmt(wv.s)} does not start on a downbeat`);
      // One bar: the bar's length in beats at the wave.
      let bar = 1;
      while (bar + 1 < plan.barStart.length && plan.barStart[bar + 1] <= Math.round(wv.sbi)) bar++;
      const beats = (plan.barStart[bar + 1] ?? plan.barStart[bar] + 2) - plan.barStart[bar];
      const sEnd = plan.songBeats[Math.min(plan.songBeats.length - 1, Math.round(wv.sbi) + beats)] - 1e-6;
      const view = viewRect(plan.camera, warpAt(sEnd));
      const r = waveRadius(wv, beatIndex(plan.songBeats, sEnd));
      const far = Math.max(...[view[0], view[2]].flatMap((x) => [view[1], view[3]].map((y) => Math.hypot(x - wv.x, y - wv.y))));
      if (far > r && wv.endS > sEnd + 1e-6) f.push(`the transform wave at ${fmt(wv.s)} has not left the screen a bar later (radius ${r.toFixed(0)}, screen corner ${far.toFixed(0)} px away)`);
    }
    add("zone waves", f, plan.open.waves.length);
  }

  // The reward spectrum: every player reaches the finale, and the blast clears the screen.
  {
    const f: string[] = [];
    const ring = plan.hits.filter((h) => h.source === "ring");
    if (ring.length === 0) f.push(`the ${plan.profile} player's run has no finale blast`);
    else {
      const end = ring[ring.length - 1].w;
      const left = plan.enemies.filter((e) => !(e.deathW <= end + 1e-6));
      if (left.length > 0) f.push(`${left.length} enemies survive the finale blast (first #${left[0].id} ${left[0].type})`);
    }
    add("the finale", f, 1);
  }

  if (!opts.signature) return results;

  // Signature moments for a moderately competent player.
  {
    const f: string[] = [];
    let n = 0;
    // At least 80% of the cleave-eligible accents produce a cleave: with press cleaves, a shockwave
    // its press launched (every eligible accent's swing is heavy); before them, a heavy swing.
    const accents = plan.cleaveAccents;
    if (accents.length > 0) {
      n++;
      const heavy = new Set(plan.cleaves.map((x) => Math.round(x.s * 1000)));
      const missed = accents.filter((a) => !heavy.has(Math.round(a.s * 1000)));
      const got = accents.length - missed.length;
      if (got < 0.8 * accents.length) f.push(`only ${got} of ${accents.length} cleave accents produce a cleave (80% needed); missed ${missed.slice(0, 6).map((a) => fmt(a.s)).join(", ")}`);
    }
    // The kick line's release pays off: the line charges at the hero, and most of it falls at once.
    // Each skeleton still standing a beat after the release has closed on the hero (toward where
    // the hero stood at the release), and at least half of those standing at the release are dead
    // within six beats of it. (Over 64 seeds the moderate player cuts down 3 to 9 of the 12 while
    // the line dances; of the rest, all are dead within six beats on all but one seed, 2 of 3 on that.)
    const byId = new Map(plan.enemies.map((e) => [e.id, e]));
    const warp = new Warp(plan.warp);
    for (const fm of plan.formations) {
      if (lib.formations.get(fm.def)?.shape !== "kick-line") continue;
      const beat = beatLen(fm.sRelease);
      const w0 = warp.toWorld(fm.sRelease);
      const w1 = warp.toWorld(fm.sRelease + beat);
      const h = heroSample(plan, w0);
      let standing = 0;
      let dead = 0;
      for (const id of fm.members) {
        const e = byId.get(id)!;
        if (e.deathW <= w0 || e.spawnW > w0) continue;
        standing++;
        const last = e.fates[e.fates.length - 1];
        const ds = last?.lethal ? plan.hits[last.hit].s : Infinity;
        if (ds <= fm.sRelease + KICK_PAYOFF_BEATS * beat + 0.001) dead++;
        if (e.deathW <= w1) continue;
        n++;
        const p0 = enemyPos(plan, e, w0, { x: 0, y: 0 });
        const p1 = enemyPos(plan, e, w1, { x: 0, y: 0 });
        const d = Math.hypot(h.x - p0.x, h.y - p0.y);
        const closed = d > 0 ? ((p1.x - p0.x) * (h.x - p0.x) + (p1.y - p0.y) * (h.y - p0.y)) / d : Infinity;
        if (closed < KICK_CHARGE) f.push(`kick-line skeleton #${id} closes ${closed.toFixed(0)} px on the hero in the beat after the release at ${fmt(fm.sRelease)} (at least ${KICK_CHARGE} px)`);
      }
      if (standing === 0) continue;
      n++;
      if (dead < KICK_PAYOFF * standing - 1e-9) f.push(`only ${dead} of the ${standing} kick-line skeletons standing at the release at ${fmt(fm.sRelease)} are dead ${KICK_PAYOFF_BEATS} beats later (half needed)`);
    }
    // At least 75% of threats end as near misses.
    if (plan.threats.length > 0) {
      n++;
      const near = plan.threats.filter((t) => t.outcome === "near-miss").length;
      if (near < 0.75 * plan.threats.length) f.push(`only ${near} of ${plan.threats.length} threats end as near misses (75% needed): ${plan.threats.filter((t) => t.outcome !== "near-miss").map((t) => `${t.kind} at ${fmt(t.s)} ${t.outcome}`).join(", ")}`);
    }
    // Every scheduled level-up happens on its scheduled moment (the i-th level-up is the i-th upgrade, whatever it takes).
    plan.levelUpsDue.forEach((due, i) => {
      n++;
      const name = `level-up #${i + 1}`;
      const got = plan.upgrades[i];
      if (!got) f.push(`${name} never happens (scheduled at ${fmt(due.s)})`);
      else if (Math.abs(got.s - due.s) > 0.001) f.push(`${name} happens at ${fmt(got.s)}, not on its scheduled moment at ${fmt(due.s)}`);
      // Upgrade orbs: one is walked into at each.
      if (got?.orbs && !got.walked) f.push(`${name} at ${fmt(got.s)}: no orb is walked into${got.sTaken !== undefined ? ` (the one ahead is taken at ${fmt(got.sTaken)})` : ""}`);
    });
    // Gems on the field: at least 60% of the experience dropped is collected.
    {
      n++;
      const g = gemTotals(plan);
      if (g.collected < 0.6 * g.dropped) f.push(`only ${g.collected} of ${g.dropped} experience dropped is collected (60% needed)`);
    }
    add("signature moments", f, n);
  }
  return results;
}

/** The groove checks start after this section: the galop vamp, where the song's pulse begins. */
const GROOVE_FROM = "galop-vamp";

/**
 * The share of pulsed time after the galop vamp (step sections, while the hero lives) during
 * which the tier passes `test`. Without press classes the tier is 0 throughout.
 */
export function tierShare(plan: Plan, test: (tier: number) => boolean): number {
  const from = plan.sections.findIndex((sec) => sec.label === GROOVE_FROM);
  const end = plan.stats.deathS ?? Infinity;
  const ts = plan.tier.s;
  const tv = plan.tier.v;
  let total = 0;
  let passed = 0;
  for (const sec of plan.sections.slice(from + 1)) {
    if (sec.movement !== "steps") continue;
    const s1 = Math.min(sec.s1, end);
    if (s1 <= sec.s0) continue;
    total += s1 - sec.s0;
    // The tier series is piecewise constant: walk its changes across the section.
    let i = 0;
    while (i < ts.length && ts[i] <= sec.s0) i++;
    let s = sec.s0;
    let tier = i > 0 ? tv[i - 1] : 0;
    for (; s < s1; i++) {
      const next = i < ts.length ? Math.min(ts[i], s1) : s1;
      if (test(tier)) passed += next - s;
      s = next;
      if (i < ts.length) tier = tv[i];
    }
  }
  return total > 0 ? passed / total : 0;
}

/**
 * Each player the groove check knows: the moderate player climbs, and so do the human and late
 * players (timed like real play logs: a 50 ms spread, and 35 or 100 ms late, which the drift
 * mostly takes off); the holder and mashers stay at tier 0. The moderate player's rule holds on
 * every seed. The others' shares move by tens of points from seed to seed (the late player's
 * between about 20% and 85%), so theirs are judged on the average over the seeds each runs on.
 */
const TIER_RULES: { match: (player: string) => boolean; what: string; test: (tier: number) => boolean; min: number; averaged?: true }[] = [
  { match: (p) => p === "moderate", what: "at tier 2 or higher", test: (t) => t >= 2, min: 0.7 },
  { match: (p) => p === "human", what: "at tier 2 or higher", test: (t) => t >= 2, min: 0.5, averaged: true },
  { match: (p) => p === "late", what: "at tier 2 or higher", test: (t) => t >= 2, min: 0.4, averaged: true },
  { match: (p) => p === "holder" || /^mash\d+$/.test(p), what: "at tier 0", test: (t) => t === 0, min: 0.8, averaged: true },
];

/** A player's share of pulsed time after the galop vamp passing its groove rule, if it has an averaged one. */
export function averagedGrooveShare(player: string, plan: Plan): number | undefined {
  const rule = TIER_RULES.find((r) => r.match(player));
  return rule?.averaged ? tierShare(plan, rule.test) : undefined;
}

/**
 * The averaged groove rules, on each player's shares (from averagedGrooveShare) over the seeds it
 * ran on: the human player is at tier 2 or higher for at least 50% of pulsed time after the galop
 * vamp on average, the late player for 40%, and the holder and mashers at tier 0 for 80%.
 */
export function checkGrooveAverages(shares: ReadonlyMap<string, readonly number[]>): CheckResult[] {
  const out: CheckResult[] = [];
  for (const [player, xs] of shares) {
    const rule = TIER_RULES.find((r) => r.match(player));
    if (!rule?.averaged || xs.length === 0) continue;
    const avg = xs.reduce((a, x) => a + x, 0) / xs.length;
    const failures = avg >= rule.min ? [] : [`the ${player} player was ${rule.what} for ${(avg * 100).toFixed(0)}% of pulsed time after the galop vamp on average over ${xs.length} seeds (at least ${rule.min * 100}%; by seed ${xs.map((x) => `${(x * 100).toFixed(0)}%`).join(", ")})`];
    out.push({ name: `groove: the ${player} player is ${rule.what} for at least ${rule.min * 100}% of pulsed time, on average`, failures, checked: xs.length });
  }
  return out;
}

/** Song time as m:ss (rounded down). */
export const mmss = (s: number) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, "0")}`;

/** A holder's or masher's run, for the curve: when the hero fell, or the song's end if it survived (song seconds). */
export interface CurveRun {
  player: string;
  seed: number;
  alive: number;
  fell: boolean;
}

/** Those who hold keys or mash, and the bands their time alive averages within: each one's, and the four's together. */
export const FALLERS = ["holder", "mash2", "mash4", "mash8"];
export const FALL_EACH: [number, number] = [95, 140];
export const FALL_ALL: [number, number] = [105, 125];

/**
 * The curve ("The Difficulty Curve" in docs/design/gameplay-redesign.md): the holder and mashers
 * fall in the climax, judged on their time alive averaged over the seeds they ran on (a run they
 * survive counts as the song's end, past FALL_EACH): each one's average within FALL_EACH, which
 * catches one of them becoming immortal or helpless, and the four together within the design's
 * 1:45-2:05, which catches a shift of the whole curve. (Measured on master over 64 seeds: the
 * four average 1:56 together, from the 2-a-second masher's 1:51 to the 8-a-second masher's 2:03,
 * which survives about one seed in five; a single run's fall spreads over about 40 s. Over 8
 * seeds, one player's average moves about 4.5 s either way and the four's about 3 s, so each band
 * sits about three standard deviations or more from master's average.) No runs at all fail: the
 * curve is never passed unchecked.
 */
export function checkCurve(runs: readonly CurveRun[]): CheckResult[] {
  const out: CheckResult[] = [];
  const all: number[] = [];
  const mean = (xs: readonly number[]) => xs.reduce((a, x) => a + x, 0) / xs.length;
  const within = (x: number, [lo, hi]: [number, number]) => x >= lo - 1e-9 && x <= hi + 1e-9;
  const avgNote = (x: number) => `${mmss(x)} (${x.toFixed(1)} s)`;
  for (const player of FALLERS) {
    const mine = runs.filter((r) => r.player === player);
    if (mine.length === 0) continue;
    const alive = mine.map((r) => r.alive);
    all.push(...alive);
    const avg = mean(alive);
    const by = mine.map((r) => `${r.seed} ${r.fell ? mmss(r.alive) : "survived"}`).join(", ");
    out.push({
      name: `the curve: the ${player} player falls between ${mmss(FALL_EACH[0])} and ${mmss(FALL_EACH[1])} on average`,
      failures: within(avg, FALL_EACH) ? [] : [`the ${player} player's time alive averages ${avgNote(avg)} over ${mine.length} seeds (${by})`],
      checked: mine.length,
    });
  }
  out.push({
    name: `the curve: the holder and mashers fall between ${mmss(FALL_ALL[0])} and ${mmss(FALL_ALL[1])} on average`,
    failures: all.length === 0 ? ["no holder or masher ran (no average seeds)"] : within(mean(all), FALL_ALL) ? [] : [`their time alive averages ${avgNote(mean(all))} over ${all.length} runs`],
    checked: all.length,
  });
  return out;
}

/** The eighths player's average groove is within this of the skilled player's. */
const EIGHTHS_GROOVE = 0.05;

/**
 * The groove tells the players apart, on one seed's runs (by player name): after the galop vamp
 * the moderate player is at tier 2 or higher for at least 70% of pulsed time, and the eighths
 * player's average groove is within 0.05 of the skilled player's. (The other players' rules are
 * averaged over the seeds: checkGrooveAverages.)
 */
export function checkGroove(runs: ReadonlyMap<string, Plan>): CheckResult[] {
  const out: CheckResult[] = [];
  for (const [player, plan] of runs) {
    const rule = TIER_RULES.find((r) => r.match(player));
    if (!rule || rule.averaged) continue;
    const share = tierShare(plan, rule.test);
    const failures = share >= rule.min ? [] : [`seed ${plan.seed}: the ${player} player was ${rule.what} for ${(share * 100).toFixed(0)}% of pulsed time after the galop vamp (at least ${rule.min * 100}%)`];
    out.push({ name: `groove: the ${player} player is ${rule.what} for at least ${rule.min * 100}% of pulsed time`, failures, checked: 1 });
  }
  const eighths = runs.get("eighths");
  const skilled = runs.get("skilled");
  if (eighths && skilled) {
    const d = eighths.stats.averageGroove - skilled.stats.averageGroove;
    const failures = Math.abs(d) <= EIGHTHS_GROOVE ? [] : [`seed ${eighths.seed}: the eighths player's average groove ${eighths.stats.averageGroove.toFixed(3)}, the skilled player's ${skilled.stats.averageGroove.toFixed(3)}`];
    out.push({ name: `groove: the eighths player's average is within ${EIGHTHS_GROOVE} of the skilled player's`, failures, checked: 1 });
  }
  return out;
}
