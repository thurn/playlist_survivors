/**
 * Shared map facts: the tile size, prop facts and the Tiled landmark format. The open field
 * (openfield.ts) generates the ground from these.
 */

export const TILE = 32;

export interface PropInfo {
  tall: boolean;
  radius: number;
}

export interface TiledObject {
  name: string;
  type?: string;
  class?: string;
  x: number;
  y: number;
  properties?: { name: string; value: unknown }[];
}

export interface TiledMap {
  width: number;
  height: number;
  tilewidth: number;
  tileheight: number;
  layers: { type: string; name: string; objects?: TiledObject[] }[];
  properties?: { name: string; value: unknown }[];
}

/** Fallback prop facts when no world atlas is supplied. */
export const DEFAULT_PROPS: Record<string, PropInfo> = {
  "tree-summer": { tall: true, radius: 30 },
  "tree-autumn": { tall: true, radius: 30 },
  "tree-dead": { tall: true, radius: 24 },
  "bush-summer": { tall: true, radius: 16 },
  "bush-autumn": { tall: true, radius: 16 },
  mushroom: { tall: false, radius: 6 },
  "mushroom-big": { tall: true, radius: 18 },
  "flower-summer": { tall: false, radius: 6 },
  "flower-autumn": { tall: false, radius: 6 },
  "grass-tuft": { tall: false, radius: 6 },
  "rock-small": { tall: false, radius: 8 },
  "rock-big": { tall: true, radius: 20 },
  brazier: { tall: true, radius: 10 },
  campfire: { tall: false, radius: 14 },
  "standing-stone": { tall: true, radius: 16 },
  "ruin-pillar": { tall: true, radius: 16 },
  "ruin-wall": { tall: true, radius: 30 },
  "lava-pool": { tall: false, radius: 40 },
  "patch-dirt-summer": { tall: false, radius: 40 },
  "patch-dirt-autumn": { tall: false, radius: 40 },
  "patch-dirt-ember": { tall: false, radius: 40 },
  "patch-lava": { tall: false, radius: 40 },
};

