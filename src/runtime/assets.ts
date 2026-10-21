/**
 * Runtime assets: baked atlases and procedural textures. Everything is loaded
 * once; lookups are cheap and allocation-free.
 */
import { Assets, Rectangle, Texture, type TextureSource } from "pixi.js";
import type { CharacterAtlas, EffectsAtlas, Facing, SoundManifest, WorldAtlas, AtlasRect, CreditsFile } from "../assets/types.ts";
import { FACINGS } from "../assets/types.ts";

export interface CharAnim {
  frames: Texture[][]; // [facing][frame]
  flash: Texture[][];
  anchorX: number;
  anchorY: number;
  frameW: number;
  frameH: number;
  count: number;
}

export interface GameAssets {
  chars: Map<string, Map<string, CharAnim>>;
  tiles: Map<string, Texture[]>;
  props: Map<string, { variants: { tex: Texture; ax: number; ay: number; frames?: Texture[] }[]; tall: boolean; fps: number }>;
  fx: Map<string, { frames: Texture[]; ax: number; ay: number; fps: number }>;
  proc: Map<string, Texture>;
  /** Source of the procedural atlas (particles must share one source). */
  procSource: TextureSource;
  sounds: SoundManifest;
  credits: CreditsFile;
  white: Texture;
}

const BASE = `${import.meta.env.BASE_URL}assets/`;

function sub(sources: TextureSource[], r: AtlasRect): Texture {
  return new Texture({ source: sources[r.page], frame: new Rectangle(r.x, r.y, r.w, r.h) });
}

async function loadPages(pages: string[]): Promise<TextureSource[]> {
  const out: TextureSource[] = [];
  for (const p of pages) {
    const tex = (await Assets.load({ src: BASE + p, data: { scaleMode: "nearest" } })) as Texture;
    tex.source.scaleMode = "nearest";
    out.push(tex.source);
  }
  return out;
}

export async function loadGameAssets(onProgress: (f: number) => void): Promise<GameAssets> {
  const getJson = async <T>(p: string): Promise<T> => {
    const r = await fetch(BASE + p);
    if (!r.ok) throw new Error(`failed to load ${p}: ${r.status}`);
    return (await r.json()) as T;
  };
  const [chars, world, effects, sounds, credits] = await Promise.all([
    getJson<CharacterAtlas>("characters.json"),
    getJson<WorldAtlas>("world.json"),
    getJson<EffectsAtlas>("effects.json"),
    getJson<SoundManifest>("sfx/sounds.json"),
    getJson<CreditsFile>("credits.json"),
  ]);
  onProgress(0.2);
  const charPages = await loadPages(chars.pages);
  onProgress(0.45);
  const worldPages = await loadPages(world.pages);
  const fxPages = await loadPages(effects.pages);
  onProgress(0.6);

  const charMap = new Map<string, Map<string, CharAnim>>();
  for (const [id, spr] of Object.entries(chars.sprites)) {
    const m = new Map<string, CharAnim>();
    for (const [name, a] of Object.entries(spr.animations)) {
      m.set(name, {
        frames: FACINGS.map((f: Facing) => a.frames[f].map((fr) => sub(charPages, fr))),
        flash: FACINGS.map((f: Facing) => a.frames[f].map((fr) => sub(charPages, fr.flash))),
        anchorX: a.anchorX,
        anchorY: a.anchorY,
        frameW: a.frameW,
        frameH: a.frameH,
        count: a.frames.down.length,
      });
    }
    charMap.set(id, m);
  }
  const tiles = new Map<string, Texture[]>();
  for (const [id, rects] of Object.entries(world.tiles)) tiles.set(id, rects.map((r) => sub(worldPages, r)));
  const props: GameAssets["props"] = new Map();
  for (const [id, p] of Object.entries(world.props)) {
    props.set(id, {
      tall: p.tall,
      fps: p.fps ?? 8,
      variants: p.variants.map((v, i) => ({ tex: sub(worldPages, v.rect), ax: v.anchorX, ay: v.anchorY, frames: p.frames?.[i]?.map((r) => sub(worldPages, r)) })),
    });
  }
  const fx: GameAssets["fx"] = new Map();
  for (const [id, a] of Object.entries(effects.anims)) fx.set(id, { frames: a.frames.map((r) => sub(fxPages, r)), ax: a.anchorX, ay: a.anchorY, fps: a.fps });

  const { proc, source } = buildProcAtlas(fxPages, effects);
  onProgress(0.7);
  return { chars: charMap, tiles, props, fx, proc, procSource: source, sounds, credits, white: Texture.WHITE };
}

// ------------------------------------------------------------------ procedural

type Draw = (g: CanvasRenderingContext2D, w: number, h: number) => void;

function radial(g: CanvasRenderingContext2D, w: number, h: number, stops: [number, string][]) {
  const grd = g.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, w / 2);
  for (const [o, c] of stops) grd.addColorStop(o, c);
  g.fillStyle = grd;
  g.fillRect(0, 0, w, h);
}

function pixelBlob(g: CanvasRenderingContext2D, w: number, h: number, seed: number, fill: string, density = 0.9) {
  let s = seed;
  const rnd = () => ((s = (s * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff);
  g.fillStyle = fill;
  const cx = w / 2;
  const cy = h / 2;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const d = Math.hypot((x + 0.5 - cx) / (w / 2), (y + 0.5 - cy) / (h / 2));
      const edge = 0.55 + 0.4 * rnd();
      if (d < edge * density) g.fillRect(x, y, 1, 1);
    }
  }
}

function arc(g: CanvasRenderingContext2D, w: number, h: number, degrees: number) {
  // A crescent: bright leading edge fading toward the trailing edge.
  const cx = w / 2;
  const cy = h / 2;
  const R = w / 2 - 2;
  const half = (degrees * Math.PI) / 360;
  const steps = 48;
  for (let i = 0; i < steps; i++) {
    const t = i / (steps - 1);
    const a0 = -half + (2 * half * i) / steps;
    const a1 = -half + (2 * half * (i + 1)) / steps;
    const thick = R * (0.08 + 0.22 * Math.sin(Math.PI * t));
    g.beginPath();
    g.arc(cx, cy, R, a0, a1 + 0.02);
    g.arc(cx, cy, R - thick, a1 + 0.02, a0, true);
    g.closePath();
    g.fillStyle = `rgba(255,255,255,${(0.25 + 0.75 * t).toFixed(3)})`;
    g.fill();
  }
}

const PROC: Record<string, [number, number, Draw]> = {
  spark: [6, 6, (g) => {
    g.fillStyle = "#fff";
    g.fillRect(2, 0, 2, 6);
    g.fillRect(0, 2, 6, 2);
  }],
  glow: [32, 32, (g, w, h) => radial(g, w, h, [[0, "rgba(255,255,255,1)"], [0.35, "rgba(255,255,255,0.55)"], [1, "rgba(255,255,255,0)"]])],
  light: [64, 64, (g, w, h) => radial(g, w, h, [[0, "rgba(255,255,255,1)"], [0.5, "rgba(255,255,255,0.4)"], [1, "rgba(255,255,255,0)"]])],
  shard: [4, 4, (g) => {
    g.fillStyle = "#fff";
    g.fillRect(0, 1, 4, 2);
    g.fillRect(1, 0, 2, 4);
  }],
  drop: [5, 5, (g) => {
    g.fillStyle = "#fff";
    g.fillRect(1, 0, 3, 5);
    g.fillRect(0, 1, 5, 3);
  }],
  ember: [3, 3, (g) => {
    g.fillStyle = "#fff";
    g.fillRect(1, 0, 1, 3);
    g.fillRect(0, 1, 3, 1);
  }],
  dust: [12, 12, (g, w, h) => radial(g, w, h, [[0, "rgba(255,255,255,0.8)"], [0.6, "rgba(255,255,255,0.35)"], [1, "rgba(255,255,255,0)"]])],
  wisp: [12, 20, (g, w, h) => {
    const grd = g.createRadialGradient(w / 2, h * 0.6, 0, w / 2, h * 0.6, w / 2);
    grd.addColorStop(0, "rgba(255,255,255,0.95)");
    grd.addColorStop(1, "rgba(255,255,255,0)");
    g.fillStyle = grd;
    g.beginPath();
    g.ellipse(w / 2, h * 0.6, w / 2, h * 0.45, 0, 0, Math.PI * 2);
    g.fill();
  }],
  splat: [32, 24, (g, w, h) => pixelBlob(g, w, h, 7, "#fff", 0.95)],
  scorch: [40, 26, (g, w, h) => {
    const grd = g.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, w / 2);
    grd.addColorStop(0, "rgba(20,12,8,0.85)");
    grd.addColorStop(0.7, "rgba(30,18,10,0.5)");
    grd.addColorStop(1, "rgba(30,18,10,0)");
    g.fillStyle = grd;
    g.beginPath();
    g.ellipse(w / 2, h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
    g.fill();
  }],
  crack: [64, 40, (g, w, h) => {
    g.strokeStyle = "rgba(20,10,6,0.9)";
    g.lineWidth = 2;
    let s = 3;
    const rnd = () => ((s = (s * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff);
    for (let k = 0; k < 7; k++) {
      let x = w / 2;
      let y = h / 2;
      const a = (k / 7) * Math.PI * 2 + rnd() * 0.5;
      g.beginPath();
      g.moveTo(x, y);
      for (let j = 0; j < 4; j++) {
        x += Math.cos(a + (rnd() - 0.5)) * (w / 10);
        y += Math.sin(a + (rnd() - 0.5)) * (h / 10);
        g.lineTo(x, y);
      }
      g.stroke();
    }
    g.fillStyle = "rgba(255,120,40,0.7)";
    g.fillRect(w / 2 - 2, h / 2 - 1, 4, 2);
  }],
  shadow: [24, 10, (g, w, h) => {
    g.fillStyle = "rgba(0,0,0,0.38)";
    g.beginPath();
    g.ellipse(w / 2, h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
    g.fill();
  }],
  arc120: [128, 128, (g, w, h) => arc(g, w, h, 120)],
  arc160: [160, 160, (g, w, h) => arc(g, w, h, 160)],
  arc150: [160, 160, (g, w, h) => arc(g, w, h, 150)],
  arc200: [200, 200, (g, w, h) => arc(g, w, h, 200)],
  streak: [64, 16, (g, w, h) => {
    const grd = g.createLinearGradient(0, 0, w, 0);
    grd.addColorStop(0, "rgba(255,255,255,0)");
    grd.addColorStop(1, "rgba(255,255,255,1)");
    g.fillStyle = grd;
    g.beginPath();
    g.ellipse(w / 2, h / 2, w / 2, h / 2 - 2, 0, 0, Math.PI * 2);
    g.fill();
  }],
  cross: [16, 16, (g) => {
    g.fillStyle = "#fff";
    g.fillRect(7, 0, 2, 16);
    g.fillRect(0, 7, 16, 2);
    g.fillRect(5, 5, 6, 6);
  }],
  ring: [128, 128, (g, w) => {
    g.strokeStyle = "#fff";
    g.lineWidth = 4;
    g.beginPath();
    g.arc(w / 2, w / 2, w / 2 - 3, 0, Math.PI * 2);
    g.stroke();
  }],
  gem: [5, 7, (g) => {
    g.fillStyle = "#7fffd0";
    g.fillRect(1, 0, 3, 7);
    g.fillRect(0, 2, 5, 3);
    g.fillStyle = "#ffffff";
    g.fillRect(1, 1, 1, 2);
  }],
  "gem-big": [7, 10, (g) => {
    g.fillStyle = "#c08cff";
    g.fillRect(2, 0, 3, 10);
    g.fillRect(0, 3, 7, 4);
    g.fillRect(1, 1, 5, 8);
    g.fillStyle = "#ffffff";
    g.fillRect(2, 2, 1, 3);
  }],
  arrow: [16, 3, (g) => {
    g.fillStyle = "#d8c8a0";
    g.fillRect(0, 1, 13, 1);
    g.fillStyle = "#e8e8f0";
    g.fillRect(13, 0, 3, 3);
    g.fillStyle = "#c04040";
    g.fillRect(0, 0, 2, 3);
  }],
  blade: [14, 14, (g) => {
    g.fillStyle = "#e8f6ff";
    g.beginPath();
    g.moveTo(7, 0);
    g.lineTo(9, 7);
    g.lineTo(7, 14);
    g.lineTo(5, 7);
    g.closePath();
    g.fill();
    g.fillStyle = "#8fd0ff";
    g.fillRect(6, 6, 2, 2);
  }],
  leaf: [4, 3, (g) => {
    g.fillStyle = "#fff";
    g.fillRect(0, 1, 4, 1);
    g.fillRect(1, 0, 2, 3);
  }],
  dot: [2, 2, (g) => {
    g.fillStyle = "#fff";
    g.fillRect(0, 0, 2, 2);
  }],
  vignette: [256, 144, (g, w, h) => {
    const grd = g.createRadialGradient(w / 2, h / 2, h * 0.35, w / 2, h / 2, w * 0.62);
    grd.addColorStop(0, "rgba(0,0,0,0)");
    grd.addColorStop(1, "rgba(0,0,0,1)");
    g.fillStyle = grd;
    g.fillRect(0, 0, w, h);
  }],
};

/** Copies of a few effects-atlas frames (gibs and bones) so particles can share one texture source. */
const COPY_FX = ["gib-1", "gib-2", "gib-3", "gib-4", "gib-5", "gib-6", "gib-small-1", "gib-small-2", "bone-1", "bone-2", "bone-3", "bone-4"];

function buildProcAtlas(fxPages: TextureSource[], effects: EffectsAtlas): { proc: Map<string, Texture>; source: TextureSource } {
  const W = 1024;
  const H = 1024;
  const canvas = document.createElement("canvas");
  canvas.width = W;
  canvas.height = H;
  const g = canvas.getContext("2d")!;
  const rects = new Map<string, [number, number, number, number]>();
  let x = 1;
  let y = 1;
  let rowH = 0;
  const place = (w: number, h: number): [number, number] => {
    if (x + w + 1 > W) {
      x = 1;
      y += rowH + 2;
      rowH = 0;
    }
    const p: [number, number] = [x, y];
    x += w + 2;
    rowH = Math.max(rowH, h);
    return p;
  };
  for (const [id, [w, h, draw]] of Object.entries(PROC)) {
    const [px, py] = place(w, h);
    g.save();
    g.translate(px, py);
    g.beginPath();
    g.rect(0, 0, w, h);
    g.clip();
    draw(g, w, h);
    g.restore();
    rects.set(id, [px, py, w, h]);
  }
  for (const id of COPY_FX) {
    const a = effects.anims[id];
    if (!a) continue;
    const r = a.frames[0];
    const src = fxPages[r.page].resource as CanvasImageSource;
    const [px, py] = place(r.w, r.h);
    g.drawImage(src, r.x, r.y, r.w, r.h, px, py, r.w, r.h);
    rects.set(`fx-${id}`, [px, py, r.w, r.h]);
  }
  const base = Texture.from(canvas);
  base.source.scaleMode = "linear";
  const proc = new Map<string, Texture>();
  for (const [id, [rx, ry, rw, rh]] of rects) proc.set(id, new Texture({ source: base.source, frame: new Rectangle(rx, ry, rw, rh) }));
  return { proc, source: base.source };
}
