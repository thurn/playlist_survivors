/**
 * The time warp w = W(s): maps song time to world time. It is the running
 * total of a piecewise-constant playback rate. Ramps are approximated by short
 * constant steps; hit-stops are rate-0 segments.
 */

export interface WarpBreak {
  /** Song time at which this rate starts. */
  s: number;
  /** World time at s. */
  w: number;
  /** World seconds per song second until the next break. */
  rate: number;
}

export class Warp {
  readonly breaks: WarpBreak[];

  constructor(breaks: WarpBreak[]) {
    if (breaks.length === 0) throw new Error("warp needs at least one breakpoint");
    this.breaks = breaks;
  }

  /** Build from (s, rate) steps starting at song time 0, world time 0. */
  static fromRates(steps: { s: number; rate: number }[]): Warp {
    const sorted = [...steps].sort((a, b) => a.s - b.s);
    const breaks: WarpBreak[] = [];
    let w = 0;
    for (let i = 0; i < sorted.length; i++) {
      const st = sorted[i];
      if (breaks.length > 0) {
        const prev = breaks[breaks.length - 1];
        w = prev.w + (st.s - prev.s) * prev.rate;
        if (prev.rate === st.rate) continue;
        if (st.s === prev.s) {
          breaks[breaks.length - 1] = { s: st.s, w: prev.w, rate: st.rate };
          continue;
        }
      }
      breaks.push({ s: st.s, w, rate: st.rate });
    }
    return new Warp(breaks);
  }

  private segmentAtSong(s: number): number {
    const b = this.breaks;
    let lo = 0;
    let hi = b.length - 1;
    if (s < b[0].s) return 0;
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (b[mid].s <= s) lo = mid;
      else hi = mid - 1;
    }
    return lo;
  }

  toWorld(s: number): number {
    const b = this.breaks[this.segmentAtSong(s)];
    if (s < this.breaks[0].s) {
      const r = this.firstNonZeroRate();
      return b.w + (s - b.s) * r;
    }
    return b.w + (s - b.s) * b.rate;
  }

  rateAt(s: number): number {
    return this.breaks[this.segmentAtSong(s)].rate;
  }

  private firstNonZeroRate(): number {
    for (const b of this.breaks) if (b.rate > 0) return b.rate;
    return 1;
  }

  /**
   * Inverse warp. Inside a hit-stop many song times share one world time; by
   * convention this returns the song time at which the hit-stop began.
   */
  toSong(w: number): number {
    const b = this.breaks;
    if (w <= b[0].w) {
      const r = b[0].rate > 0 ? b[0].rate : this.firstNonZeroRate();
      return b[0].s + (w - b[0].w) / r;
    }
    // Largest k with b[k].w < w.
    let lo = 0;
    let hi = b.length - 1;
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (b[mid].w < w) lo = mid;
      else hi = mid - 1;
    }
    const seg = b[lo];
    const rate = seg.rate > 0 ? seg.rate : this.firstNonZeroRate();
    return seg.s + (w - seg.w) / rate;
  }

  /** Song-time intervals where the rate is zero. */
  hitStops(): [number, number][] {
    const out: [number, number][] = [];
    for (let i = 0; i < this.breaks.length; i++) {
      if (this.breaks[i].rate === 0) {
        const end = i + 1 < this.breaks.length ? this.breaks[i + 1].s : Infinity;
        out.push([this.breaks[i].s, end]);
      }
    }
    return out;
  }

  /** True if song time s is inside a hit-stop. */
  inHitStop(s: number): boolean {
    return this.rateAt(s) === 0;
  }
}
