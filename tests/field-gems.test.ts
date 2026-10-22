/**
 * Gems on the field (play mode's fieldGems; "Gems", "Gem chimes" and "Vacuum" in
 * docs/design/gameplay-redesign.md), on the real song: gems wait where they drop, the pickup radius
 * grows with the tier, collection is on sixteenths, chimes climb the scale, and vacuums follow set
 * pieces.
 */
import { describe, expect, it } from "vitest";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { beatIndexAt, timeOfBeatIndex } from "../src/core/songmap.ts";
import { Warp } from "../src/core/warp.ts";
import { heroSample } from "../src/plan/sample.ts";
import type { CameraTrack, Plan, SoundEvent } from "../src/plan/types.ts";
import { chimeDegree, Emitter, type EmitSink } from "../src/planner/emit.ts";
import { prepare } from "../src/planner/plan.ts";
import { GEM_CAP, VACUUM_AHEAD } from "../src/sim/simulate.ts";

const PLAY = "levels/orpheus-galop.play.yaml";
const input = plannerInput({ level: PLAY });
const { ctx } = prepare(input);
const song = input.songMap;
const sixteenthAtOrAfter = (t: number) => timeOfBeatIndex(song, Math.ceil(beatIndexAt(song, t) * 4 - 1e-6) / 4);
const tierAt = (p: Plan, s: number) => p.tier!.v[Math.max(0, Array.from(p.tier!.s).findLastIndex((t) => t <= s))];
const moderate = runPlanner({ level: PLAY, profile: "moderate" });

describe("gems on the field", () => {
  const warp = new Warp(moderate.warp);
  const vacuumTouches = new Set((moderate.vacuums ?? []).map((v) => v.wTouch));
  const picked = moderate.events.gems.filter((g) => g.wT !== undefined && !vacuumTouches.has(g.wT));

  it("wait where they drop until the pickup radius, 40 to 120 px by tier, reaches them", () => {
    expect(input.content.hero.pickupRadius).toEqual([40, 65, 95, 120]);
    expect(picked.length).toBeGreaterThan(500);
    let far = 0;
    for (const g of picked) {
      const hp = heroSample(moderate, g.wT!);
      const d = Math.hypot(hp.x - g.x0, hp.y - g.y0);
      const r = moderate.pickupRadius![tierAt(moderate, warp.toSong(g.wT!))];
      // (Taken off on the fixed step it came within the radius: the hero moves up to a step's length past it.)
      expect(d, `gem dropped at w=${g.w0.toFixed(2)}`).toBeLessThanOrEqual(r + 6);
      if (d > 45) far++;
    }
    // At higher tiers, gems are pulled in from further than tier 0's 40 px.
    expect(far).toBeGreaterThan(50);
    // Some still lie on the field at the end.
    expect(moderate.events.gems.some((g) => !Number.isFinite(g.w1))).toBe(true);
  });

  it("are collected on the first sixteenth at least 60 ms after they take off", () => {
    let n = 0;
    for (const g of picked) {
      const sT = warp.toSong(g.wT!);
      // (Not a takeoff in a hit-stop: world time stands still there, so its song time is unknown.)
      if (warp.toWorld(sT + 0.005) - g.wT! < 1e-6) continue;
      n++;
      expect(g.s1, `gem taken at s=${sT.toFixed(3)}`).toBeCloseTo(sixteenthAtOrAfter(sT + 0.06), 6);
    }
    expect(n).toBeGreaterThan(500);
  });

  it("number at most 300 on the field: past that a death adds its value to the nearest gem", () => {
    // The holder walks away from much of the horde: gems pile up.
    const holder = runPlanner({ level: PLAY, profile: "holder", seed: 1859 });
    // (The finale blast's gems fly straight in, as today: not on the field.)
    const field = holder.events.gems.filter((g) => g.wT !== undefined || !Number.isFinite(g.w1));
    const marks = field.flatMap((g) => [[g.w0, 1], [g.wT ?? Infinity, -1]] as const).filter(([w]) => Number.isFinite(w)).sort((a, b) => a[0] - b[0] || a[1] - b[1]);
    let n = 0;
    let most = 0;
    for (const [, d] of marks) most = Math.max(most, (n += d));
    expect(most).toBeLessThanOrEqual(GEM_CAP);
    expect(most).toBeGreaterThan(GEM_CAP - 10);
    // Grown gems: worth more than a big enemy's 5, or a regular's 1 grown past it.
    expect(holder.events.gems.some((g) => g.value > 5 || (g.value > 1 && g.value < 5))).toBe(true);
  });
});

describe("gem chimes", () => {
  it("climb the scale through consecutive sixteenths and start again after a beat without pickups", () => {
    // The design's theme A example: slots k, k+1, k+2, then nothing for a beat (k+3 to k+6).
    let prev: { slot: number; degree: number } | undefined;
    const degrees = [100, 101, 102, 107, 108].map((slot) => (prev = { slot, degree: chimeDegree(prev, slot) }).degree);
    expect(degrees).toEqual([0, 1, 2, 0, 1]);
    // A shorter gap keeps climbing.
    expect(chimeDegree({ slot: 100, degree: 3 }, 103)).toBe(4);
  });

  it("are pitched in the section's key: in G major, G, A, B, then G again", () => {
    const sounds: SoundEvent[] = [];
    const sink: EmitSink = { effect() {}, sound: (snd) => sounds.push(snd), ui() {}, effectCount: 0, soundCount: 0 };
    const cam: CameraTrack = { rate: 60, w0: 0, x: new Float32Array([0]), y: new Float32Array([0]), zoom: new Float32Array([1]) };
    const em = new Emitter(ctx, new Warp([{ s: 0, w: 0, rate: 1 }]), () => cam, sink, true, { grid: [], lastKeyAt: () => -Infinity });
    const theme = ctx.sections.find((x) => x.label === "theme-a")!;
    expect(theme.song.key).toBe("G major");
    const s0 = sixteenthAtOrAfter(theme.s0 + 10);
    const slot = (k: number) => timeOfBeatIndex(song, beatIndexAt(song, s0) + k / 4);
    [0, 1, 2, 7].forEach((k, i) => em.gemArrival(slot(k), { x: 0, y: 0 }, [0, 1, 2, 0][i]));
    const chimes = sounds.filter((x) => x.key === "chime");
    expect(chimes.map((x) => Math.round(12 * Math.log2(x.rate)))).toEqual([0, 2, 4, 0]);
    expect(chimes.map((x) => x.s)).toEqual([0, 1, 2, 7].map(slot));
  });
});

describe("a vacuum", () => {
  it("drops on the first downbeat after the first ring's accent, ahead of the hero, and its gems land over one bar of sixteenths", () => {
    // The first ring closes on the chord at 62.30; the next downbeat is 62.98.
    const v = moderate.vacuums![0];
    const bar = ctx.idx.barStart.findIndex((b, k) => k > 0 && timeOfBeatIndex(song, b) > 62.3 + 1e-6);
    expect(v.s).toBeCloseTo(timeOfBeatIndex(song, ctx.idx.barStart[bar]), 6);
    expect(v.s).toBeCloseTo(62.98, 1);
    const hp = heroSample(moderate, v.w);
    // (The hero's sample there is interpolated: a few px from where it was as the vacuum dropped.)
    expect(Math.abs(Math.hypot(v.x - hp.x, v.y - hp.y) - VACUUM_AHEAD)).toBeLessThan(6);
    // The moderate player goes and touches it, soon after, and it sends the gems in view (a long run of chimes).
    expect(v.sTouch).toBeDefined();
    expect(v.sTouch! - v.s).toBeLessThan(4);
    expect(v.gems).toBeGreaterThan(40);
    expect(moderate.events.gems.filter((g) => g.wT === v.wTouch).length).toBeGreaterThanOrEqual(v.gems!);
    // The design's example: touched at 63.40, its gems land on eight sixteenths from 63.49 to 64.09
    // (in the song map, from 63.51 to 64.11).
    const first = sixteenthAtOrAfter(63.4 + 0.06);
    expect(Math.abs(first - 63.49)).toBeLessThan(0.03);
    expect(Math.abs(timeOfBeatIndex(song, beatIndexAt(song, first) + 7 / 4) - 64.09)).toBeLessThan(0.03);
    // Every touched vacuum: the gems in view fly at the touch and land from the first sixteenth at
    // least 60 ms away, on at most one bar of sixteenths (eight in a 2/4 bar).
    const touched = moderate.vacuums!.filter((x) => x.wTouch !== undefined);
    expect(touched.length).toBeGreaterThan(2);
    for (const t of touched) {
      const gems = moderate.events.gems.filter((g) => g.wT === t.wTouch);
      if (gems.length === 0) continue;
      const slots = [...new Set(gems.map((g) => g.s1))].sort((a, b) => a - b);
      expect(slots[0]).toBeCloseTo(sixteenthAtOrAfter(t.sTouch! + 0.06), 6);
      expect(slots.length).toBeLessThanOrEqual(8);
      if (gems.length >= 8) expect(slots.length).toBe(8);
    }
  });
});
