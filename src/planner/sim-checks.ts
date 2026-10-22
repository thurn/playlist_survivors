/**
 * Checks that re-run the simulation. Causality: for a cut time T (or several),
 * re-simulate with every press after T removed; the recording must match the
 * full run exactly up to T. A cut stops the virtual player; the game keeps
 * running.
 */
import type { PropInfo } from "../mapgen/mapgen.ts";
import { Warp } from "../core/warp.ts";
import type { SkillProfile } from "../sim/player.ts";
import type { SimResult } from "../sim/simulate.ts";
import type { PlannerInput } from "./context.ts";
import type { CheckResult } from "./checks.ts";
import { runSim } from "./plan.ts";

/** The first difference between two recordings before song time T, or undefined. */
export function prefixDifference(a: SimResult, b: SimResult, T: number): string | undefined {
  const warp = new Warp(a.warp);
  const wT = warp.toWorld(T);
  for (let i = 0; i < a.warp.length && a.warp[i].s < T; i++) {
    const x = a.warp[i];
    const y = b.warp[i];
    if (!y || x.s !== y.s || x.w !== y.w || x.rate !== y.rate) return `warp break ${i} at s=${x.s.toFixed(4)} differs`;
  }
  const kT = Math.floor(wT * a.hero.rate - 1e-6);
  for (let k = 0; k <= kT && k < a.hero.x.length; k++) {
    if (a.hero.x[k] !== b.hero.x[k] || a.hero.y[k] !== b.hero.y[k] || a.hero.anim[k] !== b.hero.anim[k] || a.hero.frame[k] !== b.hero.frame[k] || a.hero.facing[k] !== b.hero.facing[k]) {
      return `hero sample ${k} (w=${(k / a.hero.rate).toFixed(3)}) differs`;
    }
    if (a.camera.xs[k] !== b.camera.xs[k] || a.camera.ys[k] !== b.camera.ys[k] || a.camera.zooms[k] !== b.camera.zooms[k]) return `camera sample ${k} (w=${(k / a.hero.rate).toFixed(3)}) differs`;
  }
  const inA = a.inputs.filter((i) => i.s < T);
  const inB = b.inputs.filter((i) => i.s < T);
  if (JSON.stringify(inA) !== JSON.stringify(inB)) return `inputs before s=${T.toFixed(2)} differ`;
  const hitsA = a.hits.filter((h) => h.s < T);
  const hitsB = b.hits.filter((h) => h.s < T);
  if (JSON.stringify(hitsA) !== JSON.stringify(hitsB)) return `hits before s=${T.toFixed(2)} differ`;
  const swA = a.swings.filter((x) => x.s < T);
  const swB = b.swings.filter((x) => x.s < T);
  if (JSON.stringify(swA) !== JSON.stringify(swB)) return `swings before s=${T.toFixed(2)} differ`;
  const gA = a.groove.s.filter((s) => s < T).length;
  for (let i = 0; i < gA; i++) if (a.groove.s[i] !== b.groove.s[i] || a.groove.v[i] !== b.groove.v[i]) return `groove at s=${a.groove.s[i].toFixed(3)} differs`;
  const rate = 30;
  const jT = Math.floor(wT * rate - 1e-6);
  for (const ea of a.enemies) {
    if (ea.spawnW >= wT) continue;
    const eb = b.enemies[ea.id];
    if (!eb || eb.type !== ea.type || eb.spawnW !== ea.spawnW) return `enemy #${ea.id} spawns differently`;
    const fa = ea.fates.filter((f) => f.w < wT);
    const fb = eb.fates.filter((f) => f.w < wT);
    if (JSON.stringify(fa) !== JSON.stringify(fb)) return `enemy #${ea.id}'s hits before w=${wT.toFixed(2)} differ`;
    if ((ea.deathW < wT || eb.deathW < wT) && ea.deathW !== eb.deathW) return `enemy #${ea.id} dies at a different time`;
    const k0 = Math.round(ea.sampleW0 * rate);
    if (Math.round(eb.sampleW0 * rate) !== k0) return `enemy #${ea.id}'s samples start differently`;
    for (let j = k0; j <= jT && j - k0 < ea.sampleCount; j++) {
      const ia = (ea.sampleOffset + j - k0) * 2;
      const ib = (eb.sampleOffset + j - k0) * 2;
      if (j - k0 >= eb.sampleCount) return `enemy #${ea.id} has fewer samples before w=${wT.toFixed(2)}`;
      if (a.enemySamples[ia] !== b.enemySamples[ib] || a.enemySamples[ia + 1] !== b.enemySamples[ib + 1]) return `enemy #${ea.id} sample at w=${(j / rate).toFixed(3)} differs`;
    }
  }
  return undefined;
}

/** Re-simulate with presses after each cut removed and compare up to the cut. */
export function checkCausality(input: PlannerInput, opts: { props?: Record<string, PropInfo>; profile?: SkillProfile; cuts?: number[]; full?: SimResult } = {}): CheckResult {
  const full = opts.full ?? runSim(input, { props: opts.props, profile: opts.profile }).sim;
  const duration = input.songMap.audio.duration;
  // One cut in the thick of the galop catches a future press leaking into the past; the cut run
  // only has to simulate up to it.
  const cuts = opts.cuts ?? [0.6 * duration];
  const failures: string[] = [];
  for (const T of cuts) {
    const cut = runSim(input, { props: opts.props, profile: opts.profile, cutS: T, untilS: T }).sim;
    const diff = prefixDifference(full, cut, T);
    if (diff) failures.push(`cut at s=${T.toFixed(2)}: ${diff}`);
  }
  return { name: "causality", failures, checked: cuts.length };
}
