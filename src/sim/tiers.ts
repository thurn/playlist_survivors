/**
 * What a tier changes (play mode's rules.selfFiring; see "What a tier changes" in
 * docs/design/gameplay-redesign.md): attack area, and a little damage. The
 * content gives each value at tiers 0 to 3; a tier past the last uses the last.
 */
import type { HeroDef, WeaponDef } from "../content/schema.ts";

const at = <T>(values: readonly T[], tier: number): T => values[Math.max(0, Math.min(values.length - 1, Math.floor(tier)))];

/** The light swing's arc at a tier (radius in px, degrees; 360 is a full spin). */
export function swingArcAt(hero: HeroDef, tier: number): { radius: number; degrees: number } {
  const arc = hero.swings.light.tiers ? at(hero.swings.light.tiers, tier) : hero.swings.light.hitShape;
  return { radius: arc.radius, degrees: arc.degrees };
}

/** Every hero attack's damage multiplier at a tier. */
export const tierDamage = (hero: HeroDef, tier: number): number => at(hero.meter.damage, tier);

/** Orbiting blades at a tier: how many, and the orbit's radius. */
export function orbitAt(wd: WeaponDef, tier: number): { count: number; radius: number } {
  const o = wd.orbit!;
  return o.tiers ? at(o.tiers, tier) : { count: o.count, radius: o.radius };
}

/** Chain lightning at a tier: its jumps, and how far each reaches. */
export function chainAt(wd: WeaponDef, tier: number): { jumps: number; range: number } | undefined {
  const c = wd.chain!;
  return c.tiers ? at(c.tiers, tier) : undefined;
}

/** The fire trail's hit radius at a tier. */
export function trailRadiusAt(wd: WeaponDef, tier: number): number {
  const t = wd.trail?.tiers;
  return t ? at(t, tier).radius : wd.hitShape!.radius;
}
