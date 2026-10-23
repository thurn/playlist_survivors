/**
 * A reactive level's director (docs/design/reactive-audio.md, "The reactive level"): what drives
 * the horde in place of the song map's sections.
 */
import type { Director } from "../content/schema.ts";
import { clamp, lerp } from "../core/shaping.ts";

/** The target population at song time s: min(max, (start + perMinute * s / 60) * scale), the energy at s mapped into energyScale. */
export function directorPopulation(d: Director, s: number, energy: number): number {
  const p = d.population;
  return Math.min(p.max, (p.start + (p.perMinute * s) / 60) * lerp(p.energyScale[0], p.energyScale[1], clamp(energy, 0, 1)));
}

/** The roster at song time s: the entry with the latest `from` at or before it. */
export function directorRoster(d: Director, s: number): Record<string, number> {
  return (d.roster.findLast((r) => r.from <= s + 1e-9) ?? d.roster[0]).enemies;
}
