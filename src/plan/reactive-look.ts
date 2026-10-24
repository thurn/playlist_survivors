/**
 * Reactive mode's fast lane (docs/design/reactive-feel.md, "The Fast Lane"): the light grading's
 * colour from the detected key, and a stand-in identity per song. Pure functions of stream data
 * known by a given time; the evaluator decides when each applies.
 */
import { hash32 } from "../core/rng.ts";
import { clamp, lerp } from "../core/shaping.ts";

/**
 * A section's key is the commonest key of the ticks over this long before its start (song seconds):
 * short, since a start is detected a few seconds into the new section. The identity's key takes a
 * longer view of the song so far.
 */
export const KEY_WINDOW_S = 4;
export const IDENTITY_KEY_WINDOW_S = 12;
/** The identity is chosen at the first detected section start, or at this song time if none comes sooner. */
export const IDENTITY_BY_S = 30;

const PITCH: Record<string, number> = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };

/** A tick's key ("F# minor") as a code: its pitch class (0–11) for a major key, plus 12 for a minor one; -1 for none. */
export function keyCode(name: string | undefined): number {
  const m = name?.match(/^([A-G])([#b]?) (major|minor)$/);
  if (!m) return -1;
  const pc = (PITCH[m[1]] + (m[2] === "#" ? 1 : m[2] === "b" ? 11 : 0)) % 12;
  return pc + (m[3] === "minor" ? 12 : 0);
}

/** The commonest key code of the ticks in (s - windowS, s], a tie going to the key heard last; -1 for none. */
export function dominantKey(tickT: ArrayLike<number>, tickKey: ArrayLike<number>, s: number, windowS = KEY_WINDOW_S): number {
  // Each key's votes, in the order first met going back from s (so the first is the one heard last).
  const votes = new Map<number, number>();
  let hi = tickT.length - 1;
  while (hi >= 0 && tickT[hi] > s + 1e-9) hi--;
  for (let k = hi; k >= 0 && tickT[k] > s - windowS; k--) if (tickKey[k] >= 0) votes.set(tickKey[k], (votes.get(tickKey[k]) ?? 0) + 1);
  let best = -1;
  let bestN = 0;
  for (const [c, n] of votes)
    if (n > bestN) {
      best = c;
      bestN = n;
    }
  return best;
}

/** A grading tweak: multipliers and lift per channel, and a saturation factor. */
export interface Look {
  mul: [number, number, number];
  lift: [number, number, number];
  sat: number;
}

export const NEUTRAL_LOOK: Look = { mul: [1, 1, 1], lift: [0, 0, 0], sat: 1 };

/** A fully saturated colour (0–1 channels) at hue h (turns). */
export function hueRgb(h: number): [number, number, number] {
  const f = (n: number) => {
    const k = (n + h * 6) % 6;
    return 1 - Math.max(0, Math.min(k, 4 - k, 1));
  };
  return [f(5), f(3), f(1)];
}

/** How strongly the key tints the grading, and how far a minor key cools towards blue and darkens. */
const KEY_TINT = 0.1;
const MINOR_COOL = 0.4;
const MINOR_DARK = 0.88;
const COOL: [number, number, number] = [0.35, 0.55, 1];

/**
 * A key's look: the hue walks the circle of fifths (C red, G orange, D yellow, … a fifth a twelfth
 * of a turn), so related keys look related; minor keys grade cooler, darker and a little greyer.
 */
export function keyLook(code: number): Look {
  if (code < 0) return NEUTRAL_LOOK;
  const minor = code >= 12;
  const fifths = ((code % 12) * 7) % 12;
  let c = hueRgb(fifths / 12);
  if (minor) c = c.map((v, i) => lerp(v, COOL[i], MINOR_COOL)) as [number, number, number];
  const dark = minor ? MINOR_DARK : 1;
  return {
    mul: c.map((v) => lerp(1, v, KEY_TINT) * dark) as [number, number, number],
    lift: c.map((v) => 0.018 * v) as [number, number, number],
    sat: minor ? 0.9 : 1,
  };
}

/** The tint colour of a key, for effects that take the song's colour (lightened towards white). */
export function keyTint(code: number): number {
  if (code < 0) return 0xfff0c0;
  const minor = code >= 12;
  const [r, g, b] = hueRgb((((code % 12) * 7) % 12) / 12).map((v, i) => Math.round(255 * lerp(minor ? lerp(v, COOL[i], MINOR_COOL) : v, 1, 0.45)));
  return (r << 16) | (g << 8) | b;
}

/** A tempo band for the identity: under 90, 90 to 120, 120 to 150, and 150 BPM or more. */
export function tempoBand(beatS: number): number {
  if (!(beatS > 0)) return 0;
  const bpm = 60 / beatS;
  return bpm < 90 ? 0 : bpm < 120 ? 1 : bpm < 150 ? 2 : 3;
}

/** The identity's palette tint strength. */
const IDENTITY_TINT = 0.06;

/**
 * The stand-in identity (G2 replaces it): from a hash of the key, its mode and the tempo band, a
 * rotation of the zones' order and a palette tint. The rotation is 1 to n - 2 places, so the order
 * always changes and the first wave after the choice never returns to the zone it leaves.
 */
export function identity(key: number, band: number, zones: number): { rotation: number; tint: Look } {
  const h = hash32(0x1de7, "identity", key, band) >>> 0;
  const rotation = zones > 2 ? 1 + (h % (zones - 2)) : 0;
  const hue = ((h >>> 8) % 360) / 360;
  const c = hueRgb(hue);
  return { rotation, tint: { mul: c.map((v) => lerp(1, v, IDENTITY_TINT)) as [number, number, number], lift: [0, 0, 0], sat: 1 } };
}

/** Two looks blended (t 0 is a, 1 is b). */
export function blendLook(a: Look, b: Look, t: number): Look {
  const u = clamp(t, 0, 1);
  return {
    mul: a.mul.map((v, i) => lerp(v, b.mul[i], u)) as [number, number, number],
    lift: a.lift.map((v, i) => lerp(v, b.lift[i], u)) as [number, number, number],
    sat: lerp(a.sat, b.sat, u),
  };
}

/** Two looks applied one after the other. */
export function combineLook(a: Look, b: Look): Look {
  return {
    mul: a.mul.map((v, i) => v * b.mul[i]) as [number, number, number],
    lift: a.lift.map((v, i) => v + b.lift[i]) as [number, number, number],
    sat: a.sat * b.sat,
  };
}
