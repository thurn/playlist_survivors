/**
 * The tier is visible ("What a tier changes" in docs/design/gameplay-redesign.md): each
 * tier has its own weapon colours, and from tier 1 the hero has a faint aura. Index by tier, 0 to 3;
 * tier 0 is today's look.
 */

/** The sword's arcs and the hero's afterimages through a swing. */
export const SWORD_TINTS = [0xffffff, 0x9fe4ff, 0x9fffd8, 0xffe070];
/** The orbiting blades: the blade and the glow around it. */
export const BLADE_TINTS = [0xffffff, 0xd8f4ff, 0xd8fff0, 0xfff4c0];
export const BLADE_GLOWS = [0x80c8ff, 0x50d8ff, 0x60f0c0, 0xffd040];
/** Chain lightning's outer glow (its core stays white). */
export const BOLT_COLORS = [0x8fb8ff, 0x70d8ff, 0x80ffd0, 0xffe070];
/** The fire trail: its flames, and a glow under them from tier 1. */
export const FLAME_TINTS = [0xffffff, 0xffe8d0, 0xe8fff0, 0xfff4b0];
export const FLAME_GLOWS = [0, 0x60c0ff, 0x70f0c0, 0xffd040];
/** The hero's aura: its colour and strength (none at tier 0). */
export const AURA_TINTS = [0, 0x60c0ff, 0x70f0c0, 0xffd040];
export const AURA_ALPHA = [0, 0.24, 0.34, 0.48];

/** A tier's entry in one of the tables above. */
export const tierLook = (table: readonly number[], tier: number | undefined): number => table[Math.max(0, Math.min(table.length - 1, tier ?? 0))];
