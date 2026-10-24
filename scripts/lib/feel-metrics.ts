/**
 * The reactive feel metrics' arithmetic (docs/design/reactive-feel.md, "Metrics"): pure functions of
 * times and sampled series, so tests can feed them synthetic inputs. scripts/lib/feel.ts gathers the
 * inputs from a track's runs.
 */

/** Committed beats within `tol` of a grid point, matched one to one in time order. */
export function beatMatches(beats: readonly number[], grid: readonly number[], tol = 0.045): boolean[] {
  const hit: boolean[] = [];
  let j = 0;
  let used = -1;
  for (const b of beats) {
    while (j + 1 < grid.length && grid[j + 1] <= b) j++;
    let best = -1;
    for (const k of [j, j + 1]) if (k >= 0 && k < grid.length && k !== used && Math.abs(grid[k] - b) <= tol + 1e-9 && (best < 0 || Math.abs(grid[k] - b) < Math.abs(grid[best] - b))) best = k;
    hit.push(best >= 0);
    if (best >= 0) used = best;
  }
  return hit;
}

/** The F-measure of committed beats against a grid (within `tol`), over the grid's span. */
export function beatF(beats: readonly number[], grid: readonly number[], tol = 0.045): number {
  if (grid.length < 2) return 0;
  const step = (grid[grid.length - 1] - grid[0]) / (grid.length - 1);
  const inSpan = beats.filter((b) => b >= grid[0] - step / 2 && b <= grid[grid.length - 1] + step / 2);
  const hits = beatMatches(inSpan, grid, tol).filter(Boolean).length;
  const p = inSpan.length ? hits / inSpan.length : 0;
  const r = hits / grid.length;
  return p + r > 0 ? (2 * p * r) / (p + r) : 0;
}

/** The grid at other metrical levels: double time, both half-time phases, two-thirds time (a fraction interpolates). */
export function levelGrids(grid: readonly number[]): number[][] {
  const at = (pos: number) => {
    const i = Math.min(Math.floor(pos), grid.length - 2);
    return grid[i] + (pos - i) * (grid[i + 1] - grid[i]);
  };
  const steps = (step: number, off: number) => {
    const out: number[] = [];
    for (let p = off; p <= grid.length - 1 + 1e-9; p += step) out.push(at(p));
    return out;
  };
  return [[...grid], steps(0.5, 0), steps(2, 0), steps(2, 1), steps(1.5, 0)];
}

/** The best F-measure at any metrical level. */
export function anyLevelF(beats: readonly number[], grid: readonly number[], tol = 0.045): number {
  if (grid.length < 2) return 0;
  return Math.max(...levelGrids(grid).map((g) => beatF(beats, g, tol)));
}

/** The longest run of consecutive correct beats, as a share of the track's length (s). */
export function longestRunShare(beats: readonly number[], hits: readonly boolean[], duration: number): number {
  let best = 0;
  let start = -1;
  for (let i = 0; i < beats.length; i++) {
    if (hits[i]) {
      if (start < 0) start = i;
      best = Math.max(best, beats[i] - beats[start]);
    } else start = -1;
  }
  return duration > 0 ? best / duration : 0;
}

/** Lock-in: the time of the first beat completing a run of `n` consecutive correct beats, or undefined. */
export function lockIn(beats: readonly number[], hits: readonly boolean[], n = 8): number | undefined {
  let run = 0;
  for (let i = 0; i < beats.length; i++) {
    run = hits[i] ? run + 1 : 0;
    if (run >= n) return beats[i];
  }
  return undefined;
}

/** The share of times within `tol` of a reference slot: the reference beats subdivided `per` ways. */
export function onSlots(times: readonly number[], beats: readonly number[], per = 4, tol = 0.02): number {
  if (!times.length || beats.length < 2) return 0;
  const slots: number[] = [];
  for (let i = 0; i + 1 < beats.length; i++) for (let k = 0; k < per; k++) slots.push(beats[i] + ((beats[i + 1] - beats[i]) * k) / per);
  slots.push(beats[beats.length - 1]);
  return times.filter((t) => nearest(slots, t) <= tol + 1e-9).length / times.length;
}

/** The distance from t to the nearest of sorted times. */
export function nearest(sorted: readonly number[], t: number): number {
  let lo = 0;
  let hi = sorted.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (sorted[mid] < t) lo = mid + 1;
    else hi = mid;
  }
  let d = Infinity;
  if (lo < sorted.length) d = sorted[lo] - t;
  if (lo > 0) d = Math.min(d, t - sorted[lo - 1]);
  return d;
}

/** Pearson correlation (0 when either series is constant). */
export function correlation(a: readonly number[], b: readonly number[]): number {
  const n = Math.min(a.length, b.length);
  if (n < 3) return 0;
  let ma = 0;
  let mb = 0;
  for (let i = 0; i < n; i++) {
    ma += a[i];
    mb += b[i];
  }
  ma /= n;
  mb /= n;
  let sab = 0;
  let saa = 0;
  let sbb = 0;
  for (let i = 0; i < n; i++) {
    sab += (a[i] - ma) * (b[i] - mb);
    saa += (a[i] - ma) ** 2;
    sbb += (b[i] - mb) ** 2;
  }
  return saa > 0 && sbb > 0 ? sab / Math.sqrt(saa * sbb) : 0;
}

/** Spearman rank correlation. */
export function spearman(a: readonly number[], b: readonly number[]): number {
  const rank = (x: readonly number[]) => {
    const idx = x.map((v, i) => [v, i] as const).sort((p, q) => p[0] - q[0]);
    const r = new Array<number>(x.length);
    for (let i = 0; i < idx.length;) {
      let j = i;
      while (j + 1 < idx.length && idx[j + 1][0] === idx[i][0]) j++;
      for (let k = i; k <= j; k++) r[idx[k][1]] = (i + j) / 2;
      i = j + 1;
    }
    return r;
  };
  return correlation(rank(a), rank(b));
}

/**
 * The beat pulse a series should follow: 1 on each reference beat, decaying over the beat
 * (exp(-6 × phase)), at each sample time; NaN outside the beats' span.
 */
export function beatPulse(times: readonly number[], beats: readonly number[]): number[] {
  return times.map((t) => {
    let lo = 0;
    let hi = beats.length - 1;
    if (hi < 1 || t < beats[0] || t >= beats[hi]) return NaN;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (beats[mid] <= t) lo = mid;
      else hi = mid;
    }
    return Math.exp(-6 * ((t - beats[lo]) / (beats[hi] - beats[lo])));
  });
}

/**
 * Does a series react to the beat? |correlation| with the beat pulse at least 0.3, or a periodicity
 * peak at the beat: its mean over beat phase (16 bins) varying by at least its standard deviation
 * (noise alone varies by about half of it), over the samples kept.
 */
export function reactsToBeat(series: readonly number[], times: readonly number[], beats: readonly number[], keep: (i: number) => boolean): { corr: number; fold: number; reacts: boolean } {
  const pulse = beatPulse(times, beats);
  const xs: number[] = [];
  const ps: number[] = [];
  const bins = Array.from({ length: 16 }, () => ({ n: 0, sum: 0 }));
  for (let i = 0; i < series.length; i++) {
    if (!keep(i) || Number.isNaN(pulse[i])) continue;
    xs.push(series[i]);
    ps.push(pulse[i]);
    const phase = -Math.log(Math.max(pulse[i], 1e-9)) / 6;
    const b = bins[Math.min(15, Math.floor(phase * 16))];
    b.n++;
    b.sum += series[i];
  }
  const corr = correlation(xs, ps);
  const mean = xs.reduce((a, x) => a + x, 0) / (xs.length || 1);
  const sd = Math.sqrt(xs.reduce((a, x) => a + (x - mean) ** 2, 0) / (xs.length || 1));
  const means = bins.filter((b) => b.n > 0).map((b) => b.sum / b.n);
  const fold = sd > 0 && means.length ? (Math.max(...means) - Math.min(...means)) / sd : 0;
  return { corr, fold, reacts: Math.abs(corr) >= 0.3 || fold >= 1 };
}

/** Local maxima above the series' mean plus one standard deviation (their sample indices). */
export function peaks(series: readonly number[]): number[] {
  const n = series.length;
  if (n < 3) return [];
  const mean = series.reduce((a, x) => a + x, 0) / n;
  const sd = Math.sqrt(series.reduce((a, x) => a + (x - mean) ** 2, 0) / n);
  if (sd === 0) return [];
  const out: number[] = [];
  for (let i = 1; i + 1 < n; i++) if (series[i] > mean + sd && series[i] >= series[i - 1] && series[i] > series[i + 1]) out.push(i);
  return out;
}

/** The share of a series' peaks within `tol` of a target time. */
export function peaksOnTargets(series: readonly number[], times: readonly number[], targets: readonly number[], tol = 1 / 60): { share: number; n: number } {
  const p = peaks(series);
  if (!p.length || !targets.length) return { share: 0, n: p.length };
  return { share: p.filter((i) => nearest(targets, times[i]) <= tol + 1e-9).length / p.length, n: p.length };
}

/** Of the reference section starts, the share followed by a visible change within `within` s; and the changes far from every start. */
export function structureMatch(starts: readonly number[], changes: readonly number[], within = 8): { followed: number; of: number; stray: number } {
  const sorted = [...changes].sort((a, b) => a - b);
  const followed = starts.filter((s) => sorted.some((c) => c >= s - 1e-9 && c <= s + within + 1e-9)).length;
  const stray = sorted.filter((c) => !starts.some((s) => c >= s - 1e-9 && c <= s + within + 1e-9)).length;
  return { followed, of: starts.length, stray };
}

/** Times where a sampled value changes (beyond `eps`) from the previous sample. */
export function changeTimes(times: readonly number[], values: readonly number[], eps = 1e-6, minGap = 1): number[] {
  const out: number[] = [];
  for (let i = 1; i < values.length; i++) if (Math.abs(values[i] - values[i - 1]) > eps && (!out.length || times[i] - out[out.length - 1] >= minGap)) out.push(times[i]);
  return out;
}

/** The mean of a series over each span [edges[k], edges[k+1]). */
export function spanMeans(times: readonly number[], series: readonly number[], edges: readonly number[]): number[] {
  const out: number[] = [];
  let i = 0;
  for (let k = 0; k + 1 < edges.length; k++) {
    let sum = 0;
    let n = 0;
    while (i < times.length && times[i] < edges[k]) i++;
    let j = i;
    while (j < times.length && times[j] < edges[k + 1]) {
      sum += series[j];
      n++;
      j++;
    }
    out.push(n ? sum / n : NaN);
  }
  return out;
}

/** A share as a percentage string, or n/a. */
export const pct = (x: number | undefined) => (x === undefined || Number.isNaN(x) ? "n/a" : `${(100 * x).toFixed(1)}%`);

const median = (xs: readonly number[]) => {
  const s = [...xs].sort((a, b) => a - b);
  return s.length ? s[s.length >> 1] : NaN;
};

/**
 * The metrical level committed beats run at, as an index into levelGrids: by the ratio of their
 * median period to the grid's step (1, a half, 2 or 1.5), a half-time level's phase by F-measure. An
 * off-beat lock at the grid's period stays at the grid's own level, so it scores as wrong.
 */
export function committedLevel(beats: readonly number[], grid: readonly number[]): number {
  if (beats.length < 3 || grid.length < 3) return 0;
  const ratio = median(beats.slice(1).map((b, i) => b - beats[i])) / median(grid.slice(1).map((g, i) => g - grid[i]));
  const options: [number, number[]][] = [
    [1, [0]],
    [0.5, [1]],
    [2, [2, 3]],
    [1.5, [4]],
  ];
  const [, idx] = options.reduce((best, o) => (Math.abs(Math.log(ratio / o[0])) < Math.abs(Math.log(ratio / best[0])) ? o : best));
  if (idx.length === 1) return idx[0];
  const levels = levelGrids(grid);
  return idx.reduce((a, b) => (beatF(beats, levels[b]) > beatF(beats, levels[a]) ? b : a));
}

/**
 * Per grid interval, whether the music around it is clearly beat-driven: in the window of `w` grid
 * beats around it, at least `min` onsets, and at least `share` of them within `tol` of a 16th-note
 * slot of the grid.
 */
export function beatDriven(grid: readonly number[], onsets: readonly number[], w = 8, min = 4, share = 0.6, tol = 0.03): boolean[] {
  const slots: number[] = [];
  for (let i = 0; i + 1 < grid.length; i++) for (let k = 0; k < 4; k++) slots.push(grid[i] + ((grid[i + 1] - grid[i]) * k) / 4);
  const onSlot = onsets.map((t) => nearest(slots, t) <= tol + 1e-9);
  const out: boolean[] = [];
  for (let i = 0; i + 1 < grid.length; i++) {
    const a = grid[Math.max(0, i - w / 2)];
    const b = grid[Math.min(grid.length - 1, i + 1 + w / 2)];
    let n = 0;
    let on = 0;
    onsets.forEach((t, k) => {
      if (t < a || t >= b) return;
      n++;
      if (onSlot[k]) on++;
    });
    out.push(n >= min && on / n >= share);
  }
  return out;
}

/** Strike sources that are the player's own (a cleave's shockwave included; the finale's rings are not). */
const STRIKES = new Set(["move", "weapon", "wave"]);

/**
 * Damage dealt by the player's own strikes the predicate keeps (by hit index), each capped at what
 * the enemy had left: an enemy's fates in order, each taking at most its remaining health.
 */
export function strikeDamage(
  enemies: readonly { type: string; fates: readonly { hit: number; damage: number }[] }[],
  hits: readonly { source: string }[],
  health: (type: string) => number,
  keep: (hit: number) => boolean,
): number {
  let total = 0;
  for (const e of enemies) {
    let left = health(e.type);
    for (const f of e.fates) {
      const dealt = Math.min(f.damage, Math.max(0, left));
      left -= dealt;
      if (STRIKES.has(hits[f.hit]?.source ?? "") && keep(f.hit)) total += dealt;
    }
  }
  return total;
}
