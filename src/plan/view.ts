/**
 * What the evaluator reads from a recording besides its plain fields: the
 * lookups that need an index. A finished plan builds them once (StaticIndex);
 * play mode's live recording keeps them up to date as it grows and rewinds.
 */
import type { ContentLibrary } from "../content/loader.ts";
import { clamp } from "../core/shaping.ts";
import type { EnemyRecord, GemEvent, Plan } from "./types.ts";

export interface PlanIndex {
  enemy(id: number): EnemyRecord;
  /** Ids of enemies that may be drawable at world time w (alive or in their death fall). */
  enemiesAt(w: number): ArrayLike<number>;
  /** Indices of effect events that may be alive at world time w. */
  effectsAt(w: number): ArrayLike<number>;
  /** Indices of effect events with a decal layer, in world-time order. */
  decals(): ArrayLike<number>;
  /** Gems in flight at world time w. */
  gemsAt(w: number): GemEvent[];
  /** The world beat index at world time w. */
  worldBeatIndex(w: number): number;
  /** Song time of the finale blast's first band, once it has happened (it ends the weapons). */
  blastS(): number | undefined;
  /** Changes whenever recorded data may have been replaced (a rewind): caches keyed by it are rebuilt. */
  readonly version: number;
}

/** Fractional index of t in sorted beat times, extrapolated past either end. */
export function beatIndexIn(beats: ArrayLike<number>, t: number): number {
  const n = beats.length;
  if (t <= beats[0]) return (t - beats[0]) / (beats[1] - beats[0]);
  if (t >= beats[n - 1]) return n - 1 + (t - beats[n - 1]) / (beats[n - 1] - beats[n - 2]);
  let lo = 0;
  let hi = n - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (beats[mid] <= t) lo = mid;
    else hi = mid;
  }
  const span = beats[hi] - beats[lo];
  return span <= 0 ? lo : lo + (t - beats[lo]) / span;
}

/** The index of a finished plan: active-entity buckets and caches built once. */
export class StaticIndex implements PlanIndex {
  readonly version = 0;
  private byId: Map<number, EnemyRecord>;
  private gemCache?: number[][];
  private decalIdx?: number[];
  private ringS?: number | null;

  constructor(
    private readonly plan: Plan,
    private readonly lib: ContentLibrary,
  ) {
    this.byId = new Map(plan.enemies.map((e) => [e.id, e]));
  }

  enemy(id: number): EnemyRecord {
    return this.byId.get(id)!;
  }

  private bucket(w: number, n: number): number {
    return clamp(Math.floor(w / this.plan.activeBucket), 0, n - 1);
  }

  enemiesAt(w: number): ArrayLike<number> {
    return this.plan.activeEnemies[this.bucket(w, this.plan.activeEnemies.length)];
  }

  effectsAt(w: number): ArrayLike<number> {
    return this.plan.activeEffects[this.bucket(w, this.plan.activeEffects.length)];
  }

  decals(): ArrayLike<number> {
    if (!this.decalIdx) {
      this.decalIdx = [];
      this.plan.events.effects.forEach((ev, i) => {
        const r = this.lib.effects.get(ev.recipe);
        if (r?.layers.some((l) => l.kind === "decal")) this.decalIdx!.push(i);
      });
    }
    return this.decalIdx;
  }

  gemsAt(w: number): GemEvent[] {
    const plan = this.plan;
    const nb = plan.activeEnemies.length;
    if (!this.gemCache) {
      this.gemCache = Array.from({ length: nb }, () => [] as number[]);
      plan.events.gems.forEach((g, i) => {
        const b0 = this.bucket(g.w0, nb);
        const b1 = this.bucket(g.w1, nb);
        for (let b = b0; b <= b1; b++) this.gemCache![b].push(i);
      });
    }
    const out: GemEvent[] = [];
    for (const i of this.gemCache[this.bucket(w, nb)]) {
      const g = plan.events.gems[i];
      if (w >= g.w0 && w <= g.w1) out.push(g);
    }
    return out;
  }

  worldBeatIndex(w: number): number {
    return beatIndexIn(this.plan.worldBeats, w);
  }

  blastS(): number | undefined {
    if (this.ringS === undefined) this.ringS = this.plan.hits.find((x) => x.source === "ring")?.s ?? null;
    return this.ringS ?? undefined;
  }
}
