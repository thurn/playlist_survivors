/** The shared rhythm shaping functions. Each takes a phase in [0, 1) and peaks on the beat. */

export const beatHit = (phase: number): number => Math.exp(-6 * phase);
export const beatSwell = (phase: number): number => 0.5 + 0.5 * Math.cos(2 * Math.PI * phase);
export const amount = (base: number, energy: number): number => base * (0.4 + 0.6 * energy);

export const clamp = (x: number, lo: number, hi: number): number => (x < lo ? lo : x > hi ? hi : x);
export const lerp = (a: number, b: number, t: number): number => a + (b - a) * t;
export const smoothstep = (t: number): number => {
  const u = clamp(t, 0, 1);
  return u * u * (3 - 2 * u);
};
export const frac = (x: number): number => x - Math.floor(x);
