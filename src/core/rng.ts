/** Deterministic seeded random numbers. Planner code never uses Math.random. */

/** 32-bit integer hash of any number of integers (or strings, hashed first). */
export function hash32(...parts: (number | string)[]): number {
  let h = 0x811c9dc5 | 0;
  for (const p of parts) {
    if (typeof p === "string") {
      for (let i = 0; i < p.length; i++) {
        h ^= p.charCodeAt(i);
        h = Math.imul(h, 0x01000193);
      }
      h ^= 0xff;
      h = Math.imul(h, 0x01000193);
    } else {
      let v = Math.floor(p) | 0;
      for (let i = 0; i < 4; i++) {
        h ^= v & 0xff;
        h = Math.imul(h, 0x01000193);
        v >>>= 8;
      }
    }
  }
  // Final avalanche (murmur3 fmix32).
  h ^= h >>> 16;
  h = Math.imul(h, 0x85ebca6b);
  h ^= h >>> 13;
  h = Math.imul(h, 0xc2b2ae35);
  h ^= h >>> 16;
  return h >>> 0;
}

/** A small, fast seeded generator (sfc32). */
export class Rng {
  private a: number;
  private b: number;
  private c: number;
  private d: number;

  constructor(...seed: (number | string)[]) {
    this.a = hash32(1, ...seed);
    this.b = hash32(2, ...seed);
    this.c = hash32(3, ...seed);
    this.d = 1;
    for (let i = 0; i < 12; i++) this.next();
  }

  /** Uniform in [0, 1). */
  next(): number {
    const t = (((this.a + this.b) | 0) + this.d) | 0;
    this.d = (this.d + 1) | 0;
    this.a = this.b ^ (this.b >>> 9);
    this.b = (this.c + (this.c << 3)) | 0;
    this.c = (this.c << 21) | (this.c >>> 11);
    this.c = (this.c + t) | 0;
    return (t >>> 0) / 4294967296;
  }

  range(lo: number, hi: number): number {
    return lo + (hi - lo) * this.next();
  }

  int(lo: number, hiInclusive: number): number {
    return lo + Math.floor(this.next() * (hiInclusive - lo + 1));
  }

  pick<T>(items: readonly T[]): T {
    return items[Math.floor(this.next() * items.length)];
  }

  /** Pick by relative weights. */
  weighted<T>(items: readonly T[], weights: readonly number[]): T {
    let total = 0;
    for (const w of weights) total += w;
    let r = this.next() * total;
    for (let i = 0; i < items.length; i++) {
      r -= weights[i];
      if (r < 0) return items[i];
    }
    return items[items.length - 1];
  }

  /** Round x up or down at random so the expected value is x. */
  roundStochastic(x: number): number {
    const f = Math.floor(x);
    return f + (this.next() < x - f ? 1 : 0);
  }

  /** Signed value in [-1, 1). */
  signed(): number {
    return this.next() * 2 - 1;
  }
}

/** Stateless uniform [0,1) from a seed and an index; for closed-form effects. */
export function rand01(seed: number, i: number, k = 0): number {
  return hash32(seed, i, k) / 4294967296;
}

/** Smooth 1D value noise in [-1, 1]. */
export function noise1(seed: number, x: number): number {
  const i = Math.floor(x);
  const f = x - i;
  const a = rand01(seed, i) * 2 - 1;
  const b = rand01(seed, i + 1) * 2 - 1;
  const u = f * f * (3 - 2 * f);
  return a + (b - a) * u;
}

/** Smooth 2D value noise in [0, 1]. */
export function noise2(seed: number, x: number, y: number): number {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = x - ix;
  const fy = y - iy;
  const ux = fx * fx * (3 - 2 * fx);
  const uy = fy * fy * (3 - 2 * fy);
  const a = rand01(seed, ix, iy);
  const b = rand01(seed, ix + 1, iy);
  const c = rand01(seed, ix, iy + 1);
  const d = rand01(seed, ix + 1, iy + 1);
  return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy;
}
