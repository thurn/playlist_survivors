/**
 * Play mode's own checks, on a recording of the open field. The shared
 * checks (checks.ts) run too; these add play mode's rules and the signature
 * moments a moderately competent player must meet. Every failure names the
 * time and entity involved.
 */
import type { ContentLibrary } from "../content/loader.ts";
import { waveRadius } from "../mapgen/openfield.ts";
import type { Plan } from "../plan/types.ts";
import { viewRect } from "./camera.ts";
import type { CheckResult } from "./checks.ts";

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

  // One step per slot: no two rated presses share a grid time.
  {
    const f: string[] = [];
    const seen = new Map<number, number>();
    let n = 0;
    for (const i of plan.inputs) {
      if (i.kind !== "press" || !i.steps || i.bend || i.ignored || i.rating === undefined) continue;
      n++;
      const g = nearest(plan.grid, i.s);
      const prev = seen.get(g);
      if (prev !== undefined) f.push(`presses at ${fmt(prev)} and ${fmt(i.s)} are both rated for the grid time ${fmt(plan.grid[g])}`);
      seen.set(g, i.s);
    }
    add("one step per slot", f, n);
  }

  // Zone waves: each starts on a downbeat and has left the screen within one bar.
  if (plan.open) {
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
    // At least 80% of the cleave-eligible accents produce a cleave.
    const accents = plan.cleaveAccents ?? [];
    if (accents.length > 0) {
      n++;
      const heavy = new Set(plan.swings.filter((sw) => sw.kind === "heavy").map((sw) => Math.round(sw.s * 1000)));
      const missed = accents.filter((a) => !heavy.has(Math.round(a.s * 1000)));
      const got = accents.length - missed.length;
      if (got < 0.8 * accents.length) f.push(`only ${got} of ${accents.length} cleave accents produce a cleave (80% needed); missed ${missed.slice(0, 6).map((a) => fmt(a.s)).join(", ")}`);
    }
    // Every kick-line skeleton dies within two beats of the release.
    const byId = new Map(plan.enemies.map((e) => [e.id, e]));
    for (const fm of plan.formations) {
      if (lib.formations.get(fm.def)?.shape !== "kick-line") continue;
      const limit = fm.sRelease + 2 * beatLen(fm.sRelease) + 0.001;
      for (const id of fm.members) {
        n++;
        const e = byId.get(id)!;
        const last = e.fates[e.fates.length - 1];
        const ds = last?.lethal ? plan.hits[last.hit].s : Infinity;
        if (ds > limit) f.push(`kick-line skeleton #${id} dies at ${Number.isFinite(ds) ? fmt(ds) : "never"}, more than two beats after the release at ${fmt(fm.sRelease)}`);
      }
    }
    // At least 75% of threats end as near misses.
    if (plan.threats.length > 0) {
      n++;
      const near = plan.threats.filter((t) => t.outcome === "near-miss").length;
      if (near < 0.75 * plan.threats.length) f.push(`only ${near} of ${plan.threats.length} threats end as near misses (75% needed): ${plan.threats.filter((t) => t.outcome !== "near-miss").map((t) => `${t.kind} at ${fmt(t.s)} ${t.outcome}`).join(", ")}`);
    }
    // The boss dies within one beat after its death chord.
    if (plan.boss) {
      n++;
      const { chordS, deathS } = plan.boss;
      if (deathS === undefined) f.push(`the boss never dies (death chord at ${fmt(chordS)})`);
      else if (deathS < chordS - 0.001 || deathS > chordS + beatLen(chordS) + 0.001) f.push(`the boss dies at ${fmt(deathS)}, not within a beat after its death chord at ${fmt(chordS)}`);
    }
    // Every scheduled level-up happens on its scheduled moment.
    for (const due of plan.levelUpsDue ?? []) {
      n++;
      const got = plan.weapons.find((g) => g.id === due.id);
      if (!got) f.push(`the ${due.id} level-up never happens (scheduled at ${fmt(due.s)})`);
      else if (Math.abs(got.s - due.s) > 0.001) f.push(`the ${due.id} level-up happens at ${fmt(got.s)}, not on its scheduled moment at ${fmt(due.s)}`);
    }
    add("signature moments", f, n);
  }
  return results;
}
