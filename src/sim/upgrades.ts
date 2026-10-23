/**
 * The upgrade pool: what a level-up offers, the loadout the hero builds
 * from what it takes, and what that loadout changes. Pure, so the simulation, the evaluator and the
 * HUD read the same answers from the same records.
 */
import type { ContentLibrary } from "../content/loader.ts";
import type { WeaponDef } from "../content/schema.ts";
import { Rng } from "../core/rng.ts";
import type { UpgradeItem, UpgradeRecord } from "../plan/types.ts";

export type { UpgradeItem, UpgradeRecord };

/** What the hero has: its weapons beside the sword (in the order it got them), the evolved ones, and passive counts. */
export interface Loadout {
  weapons: string[];
  evolved: string[];
  passives: Record<string, number>;
}

/** How many items a level-up offers. */
export const OFFERS = 3;

/** The weapon upgrades still to take: the level's new weapons not owned, then the owned weapons' evolutions. */
export function weaponUpgrades(lib: Pick<ContentLibrary, "weapons">, offer: readonly string[], owned: Loadout): UpgradeItem[] {
  const out: UpgradeItem[] = offer.filter((id) => !owned.weapons.includes(id)).map((id) => ({ kind: "weapon", id }));
  for (const id of owned.weapons) if (lib.weapons.get(id)?.evolve && !owned.evolved.includes(id)) out.push({ kind: "evolve", id });
  return out;
}

/**
 * A level-up's offers: three different items, at least one a weapon upgrade while any remain, drawn
 * from the run's seed and the level-up's index (so a replay offers the same), in a shuffled order.
 */
export function offersFor(seed: number, index: number, lib: Pick<ContentLibrary, "weapons" | "passives">, offer: readonly string[], owned: Loadout): UpgradeItem[] {
  const r = new Rng(seed, "offers", index);
  const pick = (list: UpgradeItem[]) => list.splice(Math.floor(r.next() * list.length), 1)[0];
  const weapons = weaponUpgrades(lib, offer, owned);
  const picks: UpgradeItem[] = [];
  if (weapons.length > 0) picks.push(pick(weapons));
  const rest: UpgradeItem[] = [...weapons, ...[...lib.passives.keys()].map((id): UpgradeItem => ({ kind: "passive", id }))];
  while (picks.length < OFFERS && rest.length > 0) picks.push(pick(rest));
  for (let i = picks.length - 1; i > 0; i--) {
    const j = Math.floor(r.next() * (i + 1));
    [picks[i], picks[j]] = [picks[j], picks[i]];
  }
  return picks;
}

/** A fresh loadout: the level's starting weapons, nothing evolved, no passives. */
export function startingLoadout(loadout: readonly string[]): Loadout {
  return { weapons: [...loadout], evolved: [], passives: {} };
}

/** Take an item into a loadout. */
export function takeItem(l: Loadout, item: UpgradeItem): void {
  if (item.kind === "weapon") l.weapons.push(item.id);
  else if (item.kind === "evolve") l.evolved.push(item.id);
  else l.passives[item.id] = (l.passives[item.id] ?? 0) + 1;
}

/** The loadout at song time s: the starting weapons with every upgrade applied by then (an orb's on its grid time). */
export function loadoutAt(loadout: readonly string[], upgrades: readonly UpgradeRecord[], s: number): Loadout {
  const l = startingLoadout(loadout);
  for (const u of upgrades) if ((u.sApply ?? u.s) <= s && u.taken >= 0) takeItem(l, u.offers[u.taken]);
  return l;
}

/** Upgrade orbs: the virtual players' fixed priority (below LOW_HEALTH, max health comes first). */
export const ORB_PRIORITY = ["evolve:orbiting-blades", "evolve:chain-lightning", "passive:might", "weapon:fire-trail", "passive:magnet", "passive:max-health", "passive:armour"];
export const LOW_HEALTH = 0.5;

/** The offer a virtual player chooses: the first by ORB_PRIORITY (max health first below LOW_HEALTH), else the first offered. */
export function preferredOffer(offers: readonly UpgradeItem[], health = 1): number {
  const order = health < LOW_HEALTH ? ["passive:max-health", ...ORB_PRIORITY.filter((x) => x !== "passive:max-health")] : ORB_PRIORITY;
  const rank = (o: UpgradeItem) => {
    const r = order.indexOf(`${o.kind}:${o.id}`);
    return r < 0 ? order.length : r;
  };
  let best = 0;
  offers.forEach((o, i) => {
    if (rank(o) < rank(offers[best])) best = i;
  });
  return best;
}

/** The passives' bonuses added up: max health, pickup radius and damage fractions, and armour. */
export function passiveBonus(lib: Pick<ContentLibrary, "passives">, passives: Readonly<Record<string, number>>): { maxHealth: number; pickup: number; damage: number; armour: number } {
  const b = { maxHealth: 0, pickup: 0, damage: 0, armour: 0 };
  for (const [id, n] of Object.entries(passives)) {
    const p = lib.passives.get(id);
    if (!p) continue;
    b.maxHealth += p.maxHealth * n;
    b.pickup += p.pickup * n;
    b.damage += p.damage * n;
    b.armour += p.armour * n;
  }
  return b;
}

/** A bite after armour: armour less, but never below 1 (or below the bite itself, if that is less). */
export function armouredBite(amount: number, armour: number): number {
  return Math.max(Math.min(1, amount), amount - armour);
}

/** Evolved blades: more blades and a wider orbit than the tier's. */
export function evolvedOrbit(wd: WeaponDef, orbit: { count: number; radius: number }, evolved: boolean): { count: number; radius: number } {
  const e = wd.evolve;
  return evolved && e ? { count: orbit.count + e.count, radius: orbit.radius * e.radiusScale } : orbit;
}

/** An item's name, for labels: the weapon's, its evolution's or the passive's. */
export function itemName(lib: Pick<ContentLibrary, "weapons" | "passives">, item: UpgradeItem): string {
  if (item.kind === "passive") return lib.passives.get(item.id)?.name ?? item.id;
  const wd = lib.weapons.get(item.id);
  return (item.kind === "evolve" ? wd?.evolve?.name : wd?.name) ?? item.id;
}
