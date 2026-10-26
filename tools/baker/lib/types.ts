/**
 * Formats of the baked asset manifests written by the asset baker
 * (tools/baker) into public/assets/ and read by the runtime and checks.
 */

export type Facing = "up" | "left" | "down" | "right";
export const FACINGS: readonly Facing[] = ["up", "left", "down", "right"];

/** A rectangle on one atlas page. */
export interface AtlasRect {
  page: number;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface SpriteFrame extends AtlasRect {
  /** The white-silhouette copy of this frame, used for the hit flash. */
  flash: AtlasRect;
}

export interface SpriteAnimation {
  /** Frame size in world pixels (64, 128 or 192 for LPC; 32 for small monsters). */
  frameW: number;
  frameH: number;
  /** The character's feet position inside the frame, in pixels from its top-left. */
  anchorX: number;
  anchorY: number;
  /** Frames for each facing. Monsters without directional art repeat one list. */
  frames: Record<Facing, SpriteFrame[]>;
  /** Frame on which a hit lands, for attack animations. */
  impactFrame?: number;
  /** Suggested playback rate in frames per second (moves may override it). */
  fps?: number;
}

export interface CharacterSprite {
  id: string;
  animations: Record<string, SpriteAnimation>;
}

/** public/assets/characters.json */
export interface CharacterAtlas {
  /** Page image file names, relative to public/assets/. */
  pages: string[];
  sprites: Record<string, CharacterSprite>;
}

export interface PropVariant {
  rect: AtlasRect;
  /** Base (ground contact) point inside the rect. */
  anchorX: number;
  anchorY: number;
}

export interface PropDef {
  variants: PropVariant[];
  /** Tall props (trees, pillars) are depth-sorted with characters; short ones lie on the ground. */
  tall: boolean;
  /** Footprint radius in world pixels, for spacing during placement. */
  radius: number;
  /** Optional animation frames (e.g. a brazier's flame); each entry is one variant's frame list. */
  frames?: AtlasRect[][];
  fps?: number;
}

/** public/assets/world.json */
export interface WorldAtlas {
  pages: string[];
  /** 32×32 tile variants by tile id. */
  tiles: Record<string, AtlasRect[]>;
  props: Record<string, PropDef>;
}

export interface EffectAnim {
  frames: AtlasRect[];
  /** Centre point inside a frame. */
  anchorX: number;
  anchorY: number;
  fps: number;
}

/** public/assets/effects.json */
export interface EffectsAtlas {
  pages: string[];
  anims: Record<string, EffectAnim>;
}

export interface SoundEntry {
  /** Path relative to public/assets/sfx/. */
  file: string;
  durationS: number;
  /** Integrated loudness after normalization. */
  lufs: number;
  /** Seconds from the start of the sample to its perceived onset (first reaching half its peak). */
  onsetS: number;
}

/** public/assets/sfx/sounds.json, keyed by sample id such as "kenney/impactMetal_light_000". */
export interface SoundManifest {
  sounds: Record<string, SoundEntry>;
}

export interface CreditEntry {
  /** Source file or file pattern. */
  file: string;
  title?: string;
  authors: string[];
  /** The least restrictive allowed licence. */
  licence: "CC0" | "CC-BY 3.0" | "CC-BY 4.0" | "CC-BY-SA 3.0" | "CC-BY-SA 4.0" | "OGA-BY 3.0" | string;
  urls: string[];
  /** What it is used for in the demo (e.g. "hero", "goblin", "terrain"). */
  usedFor: string[];
  notes?: string;
}

/** public/assets/credits.json */
export interface CreditsFile {
  recording: { title: string; composer: string; performer: string; licence: string; url: string; notes?: string };
  entries: CreditEntry[];
  /**
   * Licences of the baked output files. Art derived from CC-BY-SA sources is
   * itself CC-BY-SA, so a page holding any such art is listed as CC-BY-SA.
   */
  outputs?: Array<{ file: string; licence: string; notes?: string }>;
}

/** UI glyphs use a baseline origin; bearings locate the trimmed raster. */
export interface FontGlyph {
  rect: AtlasRect;
  advance: number;
  bearingX: number;
  bearingY: number;
}
export interface FontAtlas {
  pages: string[];
  fonts: Record<string, { size: number; lineHeight: number; ascent: number; glyphs: Record<string, FontGlyph> }>;
}
export interface IconAtlas {
  pages: string[];
  icons: Record<string, AtlasRect>;
}
