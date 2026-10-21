/**
 * A tiny hand-made 5×7 pixel font, rendered into a texture at load. Text is
 * drawn with one sprite per glyph, scaled by whole pixels so it stays crisp.
 */
import { Container, Rectangle, Sprite, Texture } from "pixi.js";

const GLYPHS: Record<string, string[]> = {
  A: [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
  B: ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
  C: [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
  D: ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
  E: ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
  F: ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
  G: [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####"],
  H: ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
  I: [".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
  J: ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
  K: ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
  L: ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
  M: ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
  N: ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
  O: [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
  P: ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
  Q: [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
  R: ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
  S: [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
  T: ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
  U: ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
  V: ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
  W: ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#."],
  X: ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
  Y: ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
  Z: ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
  "0": [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
  "1": ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
  "2": [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
  "3": ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
  "4": ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
  "5": ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
  "6": [".###.", "#....", "#....", "####.", "#...#", "#...#", ".###."],
  "7": ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
  "8": [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
  "9": [".###.", "#...#", "#...#", ".####", "....#", "....#", ".###."],
  ".": [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
  ",": [".....", ".....", ".....", ".....", ".##..", "..#..", ".#..."],
  ":": [".....", ".##..", ".##..", ".....", ".##..", ".##..", "....."],
  "!": ["..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.."],
  "?": [".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.."],
  "-": [".....", ".....", ".....", "#####", ".....", ".....", "....."],
  "—": [".....", ".....", ".....", "#####", ".....", ".....", "....."],
  "+": [".....", "..#..", "..#..", "#####", "..#..", "..#..", "....."],
  "/": ["....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...."],
  "'": ["..#..", "..#..", ".#...", ".....", ".....", ".....", "....."],
  "(": ["...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#."],
  ")": [".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..."],
  "×": [".....", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "....."],
  "&": [".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#"],
  "©": [".###.", "#...#", "#.#.#", "##..#", "#.#.#", "#...#", ".###."],
  " ": [".....", ".....", ".....", ".....", ".....", ".....", "....."],
};
const ALIASES: Record<string, string> = { É: "E", é: "E", è: "E", ê: "E", à: "A", ö: "O", ü: "U", ñ: "N", "–": "-", "’": "'", "‘": "'", "“": "'", "”": "'", '"': "'" };

export const GLYPH_W = 5;
export const GLYPH_H = 7;
const ADV = 6;

let glyphTextures: Map<string, Texture> | undefined;

export function fontTextures(): Map<string, Texture> {
  if (glyphTextures) return glyphTextures;
  const keys = Object.keys(GLYPHS);
  const cols = 16;
  const canvas = document.createElement("canvas");
  canvas.width = cols * (GLYPH_W + 1);
  canvas.height = Math.ceil(keys.length / cols) * (GLYPH_H + 1);
  const g = canvas.getContext("2d")!;
  g.fillStyle = "#fff";
  keys.forEach((k, i) => {
    const ox = (i % cols) * (GLYPH_W + 1);
    const oy = Math.floor(i / cols) * (GLYPH_H + 1);
    GLYPHS[k].forEach((row, y) => {
      for (let x = 0; x < GLYPH_W; x++) if (row[x] === "#") g.fillRect(ox + x, oy + y, 1, 1);
    });
  });
  const base = Texture.from(canvas);
  base.source.scaleMode = "nearest";
  glyphTextures = new Map();
  keys.forEach((k, i) => {
    glyphTextures!.set(k, new Texture({ source: base.source, frame: new Rectangle((i % cols) * (GLYPH_W + 1), Math.floor(i / cols) * (GLYPH_H + 1), GLYPH_W, GLYPH_H) }));
  });
  return glyphTextures;
}

export function textWidth(text: string, scale: number): number {
  return Math.max(0, text.length * ADV - 1) * scale;
}

/** A reusable text line drawn with glyph sprites. */
export class PixelText extends Container {
  private sprites: Sprite[] = [];
  private current = "";
  private currentScale = 0;
  private currentTint = -1;

  set(text: string, scale: number, tint = 0xffffff, outline = true): this {
    text = text.toUpperCase();
    if (text === this.current && scale === this.currentScale && tint === this.currentTint) return this;
    this.current = text;
    this.currentScale = scale;
    this.currentTint = tint;
    const tex = fontTextures();
    const layers = outline ? 2 : 1;
    let n = 0;
    for (let layer = 0; layer < layers; layer++) {
      const shadow = outline && layer === 0;
      for (let i = 0; i < text.length; i++) {
        const ch = ALIASES[text[i]] ?? text[i];
        const t = tex.get(ch) ?? tex.get("?")!;
        if (ch === " ") continue;
        let s = this.sprites[n];
        if (!s) {
          s = new Sprite(t);
          this.sprites.push(s);
          this.addChild(s);
        }
        s.texture = t;
        s.visible = true;
        s.scale.set(scale);
        s.x = i * ADV * scale + (shadow ? scale : 0);
        s.y = shadow ? scale : 0;
        s.tint = shadow ? 0x000000 : tint;
        s.alpha = shadow ? 0.75 : 1;
        n++;
      }
    }
    for (let i = n; i < this.sprites.length; i++) this.sprites[i].visible = false;
    return this;
  }

  get textWidth(): number {
    return textWidth(this.current, this.currentScale);
  }
}
