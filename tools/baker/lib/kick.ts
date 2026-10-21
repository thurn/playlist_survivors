/**
 * The can-can kick: a scripted pixel edit that builds a 3-frame kick
 * (stand, knee raised, leg extended) from an LPC skeleton's standing walk
 * frame, for every facing.
 *
 * The kicking leg is erased (front/back views) or left as the standing leg
 * (side views, where both legs overlap), and a new bone leg is redrawn in the
 * sprite's own colours: outline, bone fill and highlight are sampled from the
 * original leg pixels. Drawing the leg as thick pixel line segments keeps a
 * clean 1 px outline at any angle, which rotating the original pixels could
 * not.
 *
 * Angles are in degrees from straight down, positive toward the facing
 * direction (side views) or outward (front/back views).
 */
import { Img } from "./image.ts";

export interface KickParams {
  /** Hip row in the 64 px frame (top of the legs) for side views. */
  sideHipY: number;
  thigh: number;
  shin: number;
  foot: number;
  /** Frame 2 (knee raised): thigh and shin angles. */
  kneeThigh: number;
  kneeShin: number;
  /** Frame 3 (extended): whole-leg angle, side and front/back views. */
  kickSide: number;
  kickFront: number;
  /** Front/back knee raise: thigh angle and how short it looks (foreshortening). */
  frontKneeThigh: number;
  frontThighScale: number;
}

export const DEFAULT_KICK: KickParams = {
  sideHipY: 51,
  thigh: 6,
  shin: 6,
  foot: 4,
  kneeThigh: 128,
  kneeShin: -12,
  kickSide: 136,
  kickFront: 132,
  frontKneeThigh: 25,
  frontThighScale: 0.45,
};

type Rgba = [number, number, number, number];

interface LegColours { outline: Rgba; fill: Rgba; light: Rgba }

/** Sample outline / fill / highlight colours from a region of the sprite. */
function sampleColours(img: Img, y0: number): LegColours {
  const counts = new Map<string, { c: Rgba; n: number }>();
  for (let y = y0; y < img.h; y++) for (let x = 0; x < img.w; x++) {
    const p = img.get(x, y);
    if (p[3] < 255) continue;
    const k = p.join(",");
    const e = counts.get(k);
    if (e) e.n++;
    else counts.set(k, { c: p, n: 1 });
  }
  const lum = (c: Rgba) => 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2];
  const cols = [...counts.values()].filter((e) => e.n >= 3).map((e) => e.c).sort((a, b) => lum(a) - lum(b));
  if (cols.length < 3) throw new Error("kick: not enough leg colours to sample");
  return { outline: cols[0], fill: cols[Math.max(1, cols.length - 2)], light: cols[cols.length - 1] };
}

function stampDisc(img: Img, cx: number, cy: number, r: number, c: Rgba): void {
  for (let y = Math.floor(cy - r - 1); y <= Math.ceil(cy + r + 1); y++)
    for (let x = Math.floor(cx - r - 1); x <= Math.ceil(cx + r + 1); x++) {
      const dx = x + 0.5 - cx, dy = y + 0.5 - cy;
      if (dx * dx + dy * dy <= r * r) img.set(x, y, c);
    }
}

/** Draw a bone polyline: an outlined thick stroke with a highlight on top. */
function drawBone(img: Img, pts: Array<[number, number]>, col: LegColours, width = 1): void {
  const pass = (r: number, c: Rgba, dy = 0) => {
    for (let i = 0; i < pts.length - 1; i++) {
      const [x0, y0] = pts[i], [x1, y1] = pts[i + 1];
      const n = Math.max(1, Math.ceil(Math.hypot(x1 - x0, y1 - y0) * 4));
      for (let k = 0; k <= n; k++) stampDisc(img, x0 + ((x1 - x0) * k) / n, y0 + ((y1 - y0) * k) / n + dy, r, c);
    }
  };
  pass(2.1 * width, col.outline);
  pass(1.25 * width, col.fill);
  pass(0.5, col.light, -0.6);
}

function polar(x: number, y: number, len: number, deg: number, dir: 1 | -1): [number, number] {
  const a = (deg * Math.PI) / 180;
  return [x + dir * Math.sin(a) * len, y + Math.cos(a) * len];
}

/** A leg from the hip: thigh, shin and foot angles in degrees from down. */
function legPoints(hx: number, hy: number, p: KickParams, thighDeg: number, shinDeg: number, footDeg: number, dir: 1 | -1, thighScale = 1): Array<[number, number]> {
  const knee = polar(hx, hy, p.thigh * thighScale, thighDeg, dir);
  const ankle = polar(knee[0], knee[1], p.shin, shinDeg, dir);
  const toe = polar(ankle[0], ankle[1], p.foot, footDeg, dir);
  return [[hx, hy], knee, ankle, toe];
}

function sideKick(stand: Img, p: KickParams): Img[] {
  const hipY = p.sideHipY;
  const col = sampleColours(stand, hipY);
  // Hip x: the middle of the leg's top row.
  let top0 = -1, top1 = -1;
  for (let x = 0; x < stand.w; x++) if (stand.alpha(x, hipY)) { if (top0 < 0) top0 = x; top1 = x; }
  const hx = (top0 + top1 + 1) / 2 + 1;
  const knee = stand.clone();
  const kneePts = legPoints(hx, hipY, p, p.kneeThigh, p.kneeShin, 70, 1);
  drawBone(knee, kneePts, col);
  stampDisc(knee, kneePts[1][0], kneePts[1][1], 2.4, col.outline);
  stampDisc(knee, kneePts[1][0], kneePts[1][1], 1.5, col.light);
  const ext = new Img(stand.w, stand.h);
  ext.draw(stand, -1, -1); // lean back a little as the leg goes up
  drawBone(ext, legPoints(hx - 1, hipY - 1, p, p.kickSide, p.kickSide, p.kickSide + 15, 1), col);
  return [stand.clone(), knee, ext];
}

function frontKick(stand: Img, p: KickParams): Img[] {
  // The hip is the row below the last row where the centre column is solid.
  const cx = Math.floor(stand.w / 2);
  let hipY = -1;
  for (let y = stand.h - 2; y > 0; y--) {
    if (stand.alpha(cx, y) && !stand.alpha(cx, y + 1)) { hipY = y + 1; break; }
  }
  if (hipY < 0) throw new Error("kick: could not find the hip in a front/back frame");
  const col = sampleColours(stand, hipY);
  // Cut the screen-right leg.
  const base = stand.clone();
  for (let y = hipY; y < stand.h; y++) for (let x = cx; x < stand.w; x++) base.set(x, y, [0, 0, 0, 0]);
  let top0 = -1, top1 = -1;
  for (let x = cx; x < stand.w; x++) if (stand.alpha(x, hipY)) { if (top0 < 0) top0 = x; top1 = x; }
  const hx = (top0 + top1 + 1) / 2;

  // Frame 2: knee raised toward the viewer — a short, foreshortened thigh and the shin hanging from it.
  const knee = base.clone();
  const kp = legPoints(hx, hipY, p, p.frontKneeThigh, 0, 0, 1, p.frontThighScale);
  // The raised shin hangs from the knee and stops well above the ground.
  kp[2] = [kp[1][0], kp[1][1] + p.shin - 2];
  kp[3] = [kp[2][0], kp[2][1] + 1];
  drawBone(knee, kp, col, 1.1);
  // The raised foot, seen from the front: a wide blob.
  drawBone(knee, [[kp[3][0] - 1.5, kp[3][1]], [kp[3][0] + 1.5, kp[3][1]]], col);
  stampDisc(knee, ...polar(hx, hipY, p.thigh * p.frontThighScale, p.frontKneeThigh, 1), 2.2, col.outline);
  stampDisc(knee, ...polar(hx, hipY, p.thigh * p.frontThighScale, p.frontKneeThigh, 1), 1.3, col.light);

  // Frame 3: leg kicked out and up to the side, body tilting away.
  const ext = new Img(stand.w, stand.h);
  ext.draw(base, -1, -1);
  drawBone(ext, legPoints(hx - 1, hipY - 1, p, p.kickFront, p.kickFront, p.kickFront + 10, 1), col);
  return [stand.clone(), knee, ext];
}

/**
 * Build kick frames from per-facing standing frames (up, left, down, right).
 * `kickAlt` kicks the other leg (mirrored front/back frames).
 */
export function buildKick(standing: Img[], p: KickParams = DEFAULT_KICK): { kick: Img[][]; kickAlt: Img[][] } {
  const [up, , down, right] = standing;
  const rightK = sideKick(right, p);
  const leftK = rightK.map((f) => f.flipH());
  const downK = frontKick(down, p);
  // Back view: kick the screen-left leg, which is the same leg as the front view's screen-right one.
  const upK = frontKick(up.flipH(), p).map((f) => f.flipH());
  const kick = [upK, leftK, downK, rightK];
  const kickAlt = [upK.map((f) => f.flipH()), leftK, downK.map((f) => f.flipH()), rightK];
  return { kick, kickAlt };
}
