/**
 * Reactive mode's fast lane (docs/design/reactive-feel.md, "The Fast Lane"): the key's colour, the
 * stand-in identity, onset strengths, and the frames that use them.
 */
import { describe, expect, it } from "vitest";
import { Evaluator, type Frame } from "../src/plan/evaluate.ts";
import { dominantKey, identity, IDENTITY_BY_S, keyCode, keyLook, keyTint, KEY_WINDOW_S, tempoBand } from "../src/plan/reactive-look.ts";
import { onsetShares, planWithSim, prepare } from "../src/planner/plan.ts";
import { reactivePlayerFor } from "../src/reactive/players.ts";
import { reactiveInput, readStreamFile } from "../scripts/lib/reactive-node.ts";

describe("the key's colour and the identity", () => {
  it("reads keys as pitch classes, minor ones 12 on", () => {
    expect(keyCode("C major")).toBe(0);
    expect(keyCode("F# minor")).toBe(18);
    expect(keyCode("Bb major")).toBe(10);
    expect(keyCode("Cb major")).toBe(11);
    expect([keyCode(undefined), keyCode("H major"), keyCode("C dorian")]).toEqual([-1, -1, -1]);
  });

  it("takes a section's key as the commonest over the window before it, ties to the latest", () => {
    const t = [1, 2, 3, 4, 5, 6, 20];
    const k = [0, 0, 7, 7, -1, 9, 4];
    expect(dominantKey(t, k, 6)).toBe(7);
    expect(dominantKey(t, k, 2.5)).toBe(0);
    // (Nothing from the tick at 20 before it, and nothing older than the window.)
    expect(dominantKey(t, k, 19)).toBe(-1);
    expect(dominantKey(t, k, 6 + KEY_WINDOW_S - 0.5)).toBe(9);
    expect(dominantKey(t, k, 0.5)).toBe(-1);
    // A tie goes to the key heard last.
    expect(dominantKey([1, 2, 3, 4], [11, 9, 9, 11], 4)).toBe(11);
  });

  it("gives a section detected a few seconds after a key change the new key", () => {
    // C major to 20 s, then A minor; the section start is detected at 23 s.
    const t = Array.from({ length: 600 }, (_, i) => i * 0.05);
    const k = t.map((x) => (x < 20 ? keyCode("C major") : keyCode("A minor")));
    expect(dominantKey(t, k, 23)).toBe(keyCode("A minor"));
  });

  it("walks the circle of fifths and grades minor keys darker and cooler", () => {
    const sum = (v: number[]) => v.reduce((a, b) => a + b, 0);
    const c = keyLook(keyCode("C major"));
    const cm = keyLook(keyCode("C minor"));
    expect(sum(cm.mul)).toBeLessThan(sum(c.mul));
    expect(cm.mul[2] / cm.mul[0]).toBeGreaterThan(c.mul[2] / c.mul[0]);
    expect(cm.sat).toBeLessThan(c.sat);
    // A fifth apart is a twelfth of the hue circle; a semitone apart is seven.
    const hueOf = (code: number) => keyTint(code);
    expect(hueOf(keyCode("G major"))).not.toBe(hueOf(keyCode("C major")));
    expect(keyLook(-1)).toEqual({ mul: [1, 1, 1], lift: [0, 0, 0], sat: 1 });
  });

  it("chooses a rotation of 1 to n - 2 zones, the same each time", () => {
    for (let key = -1; key < 24; key++)
      for (let band = 0; band < 4; band++) {
        const id = identity(key, band, 5);
        expect(id.rotation).toBeGreaterThanOrEqual(1);
        expect(id.rotation).toBeLessThanOrEqual(3);
        expect(identity(key, band, 5)).toEqual(id);
      }
    expect(new Set(Array.from({ length: 24 }, (_, k) => identity(k, 1, 5).rotation)).size).toBe(3);
    expect([tempoBand(0.75), tempoBand(0.6), tempoBand(0.45), tempoBand(0.35), tempoBand(0)]).toEqual([0, 1, 2, 3, 0]);
  });

  it("measures each onset against the loudest known before it, never after", () => {
    const onsets = [100, 50, 400, 25, 400, 10].map((strength, i) => ({ knownAt: i, strength }));
    const n = onsetShares(onsets);
    expect(n[0]).toBe(1);
    expect(n[1]).toBeCloseTo(Math.sqrt(50 / (100 * 0.5 ** (1 / 8))), 6);
    expect(n[2]).toBe(1);
    for (const v of n) expect(v).toBeGreaterThanOrEqual(0);
    for (const v of n) expect(v).toBeLessThanOrEqual(1);
    // A prefix's shares don't depend on what follows.
    expect(Array.from(onsetShares(onsets.slice(0, 3)))).toEqual(Array.from(n.slice(0, 3)));
  });
});

describe("the fast lane's frames", () => {
  const run = reactiveInput(readStreamFile("house-128"), { checks: { noHeroDamage: true } });
  const ctx = prepare(run.input).ctx;
  const plan = planWithSim(run.input, { player: reactivePlayerFor(ctx, run.reference!, "reactive-moderate") }).plan;
  const ev = new Evaluator(plan, run.input.content);
  const frames: Frame[] = [];
  for (let s = 0.5; s < plan.duration - 1; s += 0.05) frames.push(ev.evaluate(s));
  const sectionStarts = plan.open.waves.filter((w) => w.section !== undefined).map((w) => w.s);
  const idS = sectionStarts[0] !== undefined && sectionStarts[0] < IDENTITY_BY_S ? sectionStarts[0] : IDENTITY_BY_S;

  it("changes the grading only as a section starts or the identity is chosen", () => {
    expect(sectionStarts.length).toBeGreaterThan(1);
    let changes = 0;
    for (let k = 1; k < frames.length; k++) {
      if (JSON.stringify(frames[k].look) === JSON.stringify(frames[k - 1].look)) continue;
      changes++;
      const s = frames[k].s;
      const near = [...sectionStarts, idS].some((t) => s >= t - 1e-9 && s <= t + 1.5 + 0.05);
      expect(near, `the look changed at ${s.toFixed(2)}`).toBe(true);
    }
    expect(changes).toBeGreaterThan(0);
  });

  it("rotates the zones of the waves from the identity's choice on, and names them so", () => {
    const n = plan.map.zones.length;
    const turns = new Set<number>();
    for (const wv of plan.open.waves) {
      const shown = ev.evaluate(Math.max(wv.s, wv.endS) + 0.01).zone.a;
      if (wv.s < idS - 1e-9) expect(shown, `wave at ${wv.s.toFixed(2)}`).toBe(wv.zone);
      else turns.add((shown - wv.zone + n) % n);
    }
    // One rotation for every wave from the choice on, of 1 to n - 2 zones.
    expect(turns.size).toBe(1);
    expect([...turns][0]).toBeGreaterThanOrEqual(1);
    expect([...turns][0]).toBeLessThanOrEqual(n - 2);
    const names = plan.map.zones.map((z) => run.input.content.zones.get(z.id)!.name);
    const banner = sectionStarts.find((s) => s >= idS);
    if (banner !== undefined) {
      const f = ev.evaluate(banner + 0.2);
      const zoneShown = plan.map.zones[f.zone.b].id;
      expect(f.hud.banners.map((b) => b.text)).toContain(run.input.content.zones.get(zoneShown)!.name);
      expect(names).toContain(f.hud.banners[0].text);
    }
  });

  it("makes the horde squash on committed beats and hop between them while the beat is clear", () => {
    const clear = frames.filter((f) => f.reactive && f.reactive.mix * f.reactive.amp > 0.9 && f.enemies.length > 10);
    expect(clear.length).toBeGreaterThan(20);
    // Across two beats, each walking enemy squashes (its width over height swinging) and hops (a
    // stepping one steps, which some do every other beat).
    const k = Math.floor(clear[clear.length >> 1].sbi);
    const b = plan.songBeats;
    const span = Array.from({ length: 20 }, (_, i) => ev.evaluate(b[k + (i >= 10 ? 1 : 0)] + ((i % 10) / 10) * (b[k + (i >= 10 ? 2 : 1)] - b[k + (i >= 10 ? 1 : 0)])));
    let squashed = 0;
    let hopped = 0;
    let walking = 0;
    for (const e of span[0].enemies) {
      const seen = span.map((f) => f.enemies.find((x) => x.id === e.id));
      if (seen.some((x) => !x || x.anim !== "walk")) continue;
      walking++;
      const shape = seen.map((x) => x!.scaleX / x!.scaleY);
      const z = seen.map((x) => x!.z);
      if (Math.max(...shape) - Math.min(...shape) > 0.2) squashed++;
      if (Math.max(...z) - Math.min(...z) > 2.5) hopped++;
    }
    expect(walking).toBeGreaterThan(20);
    expect(squashed).toBeGreaterThan(0.9 * walking);
    expect(hopped).toBeGreaterThan(0.9 * walking);
  });

  it("bursts on each onset from when it is known, and not before", () => {
    const fx = plan.reactiveFx!;
    const k = fx.onsetT.findIndex((t) => t > 10);
    const t = fx.onsetT[k];
    const glows = (s: number) => ev.evaluate(s).particles.filter((p) => p.tex === "glow" && p.additive).length;
    // (Bursts last 0.4 s, so only those of onsets within it count.)
    const live = (s: number) => Array.from(fx.onsetT).filter((o) => o <= s + 1e-9 && s - o < 0.4).length;
    expect(glows(t + 0.01)).toBe(live(t + 0.01));
    expect(glows(t - 0.001)).toBe(live(t - 0.001));
    expect(glows(t + 0.01)).toBeGreaterThan(0);
  });
});

describe("the reward at each tier", () => {
  it("shows a swing's arcs more faintly at lower tiers", () => {
    const run = reactiveInput(readStreamFile("house-128"), { checks: { noHeroDamage: true } });
    const ctx = prepare(run.input).ctx;
    const plan = planWithSim(run.input, { player: reactivePlayerFor(ctx, run.reference!, "mash4") }).plan;
    const reactive = new Evaluator(plan, run.input.content);
    const { reactiveFx, ...rest } = plan;
    void reactiveFx;
    const plain = new Evaluator(rest, run.input.content);
    // (Without the stream's effects there is no look: play mode's frames keep their grading.)
    expect(plain.evaluate(40).look).toBeUndefined();
    expect(reactive.evaluate(40).look).toBeDefined();
    const ratios = new Set<number>();
    for (const sw of plan.swings.slice(0, 60)) {
      const s = sw.s + 0.02;
      const a = reactive.evaluate(s).fx.filter((f) => f.art.startsWith("proc:arc"));
      const b = plain.evaluate(s).fx.filter((f) => f.art.startsWith("proc:arc"));
      expect(a.length).toBe(b.length);
      a.forEach((f, i) => ratios.add(Math.round((f.alpha / b[i].alpha) * 100) / 100));
    }
    expect([...ratios].every((r) => [0.5, 0.7, 0.88, 1].includes(r))).toBe(true);
    expect(ratios.has(0.5)).toBe(true);
  });
});
