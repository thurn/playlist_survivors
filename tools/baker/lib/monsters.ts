/**
 * Small monsters from source-of-tales (slime, bat, bee, eyeball), read via
 * their Mana-style sprite XML. Baked shadows are removed (the runtime draws
 * blob shadows) and the anchor is put where the shadow was, or under the body
 * for ground monsters.
 */
import { existsSync, readFileSync } from "node:fs";
import { Img, components, loadImage } from "./image.ts";
import { resolveSource } from "./paths.ts";

export interface MonsterDef {
  /** Sprite XML (lpc: ref), or null to synthesize from `image`. */
  xml?: string;
  /** Shared frame-layout XMLs, resolved explicitly and credited. */
  includes?: string[];
  /** Use a real walk cycle instead of a small monster's looping stand. */
  walkAction?: string;
  /** For monsters without XML: the base image and frame size. */
  image?: string;
  frameW?: number;
  frameH?: number;
  /** Remove the baked shadow and anchor on it. */
  flying?: boolean;
}

interface XmlFrame { index: number; offsetX: number; offsetY: number; delay: number }
interface XmlAction { imageset: string; dirs: Record<string, XmlFrame[]> }
interface XmlSprite { imagesets: Record<string, { src: string; width: number; height: number }>; actions: Record<string, XmlAction> }

function attr(tag: string, name: string): string | undefined {
  const m = new RegExp(`\\b${name}="([^"]*)"`).exec(tag);
  return m ? m[1] : undefined;
}

export function parseSpriteXml(text: string): XmlSprite {
  const imagesets: XmlSprite["imagesets"] = {};
  for (const m of text.matchAll(/<imageset\b[^>]*>/g)) {
    imagesets[attr(m[0], "name")!] = { src: attr(m[0], "src")!, width: Number(attr(m[0], "width")), height: Number(attr(m[0], "height")) };
  }
  const actions: XmlSprite["actions"] = {};
  for (const m of text.matchAll(/<action\b([^>]*)>([\s\S]*?)<\/action>/g)) {
    const name = attr(m[1], "name")!;
    const action: XmlAction = { imageset: attr(m[1], "imageset")!, dirs: {} };
    for (const a of m[2].matchAll(/<animation\b([^>]*)>([\s\S]*?)<\/animation>/g)) {
      const dir = attr(a[1], "direction")!;
      const frames: XmlFrame[] = [];
      for (const f of a[2].matchAll(/<(frame|sequence)\b[^>]*>/g)) {
        const t = f[0];
        const ox = Number(attr(t, "offsetX") ?? 0), oy = Number(attr(t, "offsetY") ?? 0), delay = Number(attr(t, "delay") ?? 100);
        if (f[1] === "frame") frames.push({ index: Number(attr(t, "index")), offsetX: ox, offsetY: oy, delay });
        else for (let i = Number(attr(t, "start")); i <= Number(attr(t, "end")); i++) frames.push({ index: i, offsetX: ox, offsetY: oy, delay });
      }
      action.dirs[dir] = frames;
    }
    actions[name] = action;
  }
  return { imagesets, actions };
}

export interface MonsterAnim {
  frameW: number;
  frameH: number;
  anchorX: number;
  anchorY: number;
  fps: number;
  frames: Img[][];
}

const DIRS = ["up", "left", "down", "right"] as const;
const ATTACK_MARGIN = 8;

function cell(img: Img, index: number, w: number, h: number): Img {
  const cols = Math.floor(img.w / w);
  return img.crop((index % cols) * w, Math.floor(index / cols) * h, w, h);
}

/** Find the baked shadow in a frame: the lowest flat dark component, separate from the body. */
function findShadow(frame: Img): { top: number; cx: number; cy: number } | null {
  const comps = components(frame, 4);
  if (comps.length < 2) return null;
  const lowest = [...comps].sort((a, b) => b.y - a.y)[0];
  const others = comps.filter((c) => c !== lowest);
  const bodyBottom = Math.max(...others.map((c) => c.y + c.h));
  if (lowest.y < bodyBottom || lowest.h > 8) return null;
  return { top: lowest.y, cx: lowest.x + lowest.w / 2, cy: lowest.y + lowest.h / 2 };
}

function eraseRows(img: Img, fromY: number): Img {
  const out = img.clone();
  for (let y = Math.max(0, fromY); y < out.h; y++) for (let x = 0; x < out.w; x++) out.set(x, y, [0, 0, 0, 0]);
  return out;
}

/** Bake a monster's walk (looping stand/move cycle), attack and hurt (death) animations. */
export async function bakeMonster(id: string, def: MonsterDef, use: (ref: string) => void): Promise<Record<string, MonsterAnim>> {
  let sprite: XmlSprite;
  if (def.xml) {
    const xmlPath = resolveSource(def.xml);
    use(def.xml);
    sprite = parseSpriteXml(readFileSync(xmlPath, "utf8"));
    for (const ref of def.includes ?? []) {
      use(ref);
      const shared = parseSpriteXml(readFileSync(resolveSource(ref), "utf8"));
      sprite.actions = { ...shared.actions, ...sprite.actions };
    }
  } else {
    // Synthesize the same structure as the other monsters' XML.
    const w = def.frameW!, h = def.frameH!;
    const stand: Record<string, XmlFrame[]> = {}, attack: Record<string, XmlFrame[]> = {};
    const lunge: Record<string, [number, number]> = { up: [0, -1], left: [-1, 0], down: [0, 1], right: [1, 0] };
    DIRS.forEach((d, row) => {
      stand[d] = [0, 1, 2, 1].map((c) => ({ index: row * 3 + c, offsetX: 0, offsetY: 0, delay: 150 }));
      attack[d] = [2, 4, 8, 4, 2].map((k) => ({ index: row * 3, offsetX: lunge[d][0] * k, offsetY: lunge[d][1] * k, delay: 100 }));
    });
    sprite = {
      imagesets: { base: { src: def.image!.replace(/^lpc:source-of-tales\//, ""), width: w, height: h } },
      actions: { stand: { imageset: "base", dirs: stand }, attack: { imageset: "base", dirs: attack } },
    };
  }
  const images: Record<string, Img> = {};
  const needed = new Set([sprite.actions.stand.imageset, sprite.actions[def.walkAction ?? "stand"].imageset, sprite.actions.attack.imageset, sprite.actions.dead?.imageset]);
  for (const [name, set] of Object.entries(sprite.imagesets)) {
    if (!needed.has(name)) continue;
    const ref = `lpc:source-of-tales/${set.src}`;
    if (!existsSync(resolveSource(ref))) throw new Error(`${id}: missing ${ref}`);
    use(ref);
    images[name] = await loadImage(resolveSource(ref));
  }
  const base = sprite.imagesets.base ?? Object.values(sprite.imagesets)[0];
  const fw = base.width, fh = base.height;

  // Shadow and anchor from the down-facing stand frame.
  const probe = cell(images[sprite.actions.stand.imageset], sprite.actions.stand.dirs.down[0].index, fw, fh);
  let anchorX = fw / 2, anchorY: number;
  let shadowTop = fh;
  const shadow = def.flying ? findShadow(probe) : null;
  if (def.flying) {
    if (!shadow) throw new Error(`${id}: expected a baked shadow`);
    shadowTop = shadow.top;
    anchorX = Math.round(shadow.cx);
    anchorY = Math.round(shadow.cy);
  } else {
    const bb = probe.bbox()!;
    anchorY = bb.y + bb.h;
  }
  const getFrame = (set: string, index: number) => eraseRows(cell(images[set], index, fw, fh), shadowTop);

  const out: Record<string, MonsterAnim> = {};
  const fpsOf = (frames: XmlFrame[]) => Math.round(1000 / (frames.filter((f) => f.delay > 0)[0]?.delay ?? 100));
  {
    const a = sprite.actions[def.walkAction ?? "stand"];
    out.walk = { frameW: fw, frameH: fh, anchorX, anchorY, fps: fpsOf(a.dirs.down), frames: DIRS.map((d) => a.dirs[d].map((f) => getFrame(a.imageset, f.index))) };
  }
  {
    const a = sprite.actions.attack;
    const W = fw + 2 * ATTACK_MARGIN, H = fh + 2 * ATTACK_MARGIN;
    out.attack = {
      frameW: W, frameH: H, anchorX: anchorX + ATTACK_MARGIN, anchorY: anchorY + ATTACK_MARGIN, fps: fpsOf(a.dirs.down),
      frames: DIRS.map((d) => a.dirs[d].map((f) => {
        const img = new Img(W, H);
        img.draw(getFrame(a.imageset, f.index), ATTACK_MARGIN + f.offsetX, ATTACK_MARGIN + f.offsetY);
        return img;
      })),
    };
  }
  if (sprite.actions.dead) {
    const a = sprite.actions.dead;
    out.hurt = { frameW: fw, frameH: fh, anchorX, anchorY, fps: fpsOf(a.dirs.down ?? a.dirs.up), frames: DIRS.map((d) => (a.dirs[d] ?? a.dirs.up).map((f) => getFrame(a.imageset, f.index))) };
  } else {
    // No death art: fade out while sinking toward the ground.
    const a = sprite.actions.stand;
    out.hurt = {
      frameW: fw, frameH: fh, anchorX, anchorY, fps: 10,
      frames: DIRS.map((d) => [1, 0.8, 0.6, 0.4, 0.2].map((alpha, i) => {
        const img = new Img(fw, fh);
        img.draw(getFrame(a.imageset, a.dirs[d][0].index).fade(alpha), 0, i);
        return img;
      })),
    };
  }
  return out;
}
