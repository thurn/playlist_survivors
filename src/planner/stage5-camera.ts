/** Stage 5: plan the camera's path and zoom, baked at 60 samples per world-second. */
import { routeFrame } from "../mapgen/mapgen.ts";
import { lerp, smoothstep } from "../core/shaping.ts";
import { sectionIndexAt, type Ctx } from "./context.ts";
import { progressAt } from "./stage3-warp.ts";
import { worldBeatIndex } from "./stage4-hero.ts";
import type { CameraTrack } from "../plan/types.ts";

export const CAMERA_RATE = 60;
const OMEGA = 5.5;

export function stage5(ctx: Ctx): void {
  const hero = ctx.hero!;
  const warp = ctx.warp!;
  const map = ctx.map!;
  const wb = ctx.worldBeats!;
  const route = { step: map.routeStep, x: map.routeX, y: map.routeY };
  const n = hero.x.length;
  const x = new Float32Array(n);
  const y = new Float32Array(n);
  const zoom = new Float32Array(n);

  const punches: { w: number; amt: number }[] = [];
  for (const a of ctx.song.accents) {
    const sec = ctx.sections[sectionIndexAt(ctx, a.time)];
    if (!sec.level.camera.punchOnAccents) continue;
    punches.push({ w: warp.toWorld(a.time), amt: 0.02 + 0.02 * a.strength });
  }

  const zoomAt = (w: number) => {
    const s = warp.toSong(w);
    const i = sectionIndexAt(ctx, s);
    const sec = ctx.sections[i];
    const z = sec.level.camera.zoom;
    if (i === 0) return z;
    const bpb = ctx.idx.beatsPerBar[sec.song.bars[0]] ?? 2;
    const u = smoothstep((worldBeatIndex(wb, w) - sec.b0) / bpb);
    return lerp(ctx.sections[i - 1].level.camera.zoom, z, u);
  };
  const targetAt = (k: number) => {
    const w = k / CAMERA_RATE;
    const s = warp.toSong(w);
    const sec = ctx.sections[sectionIndexAt(ctx, s)];
    const f = routeFrame(route, progressAt(ctx, w));
    const lead = sec.level.camera.lead;
    return { x: hero.x[k] + f.tx * lead, y: hero.y[k] - hero.z[k] * 0 + f.ty * lead - 12 };
  };

  let t = targetAt(0);
  let px = t.x;
  let py = t.y;
  let vx = 0;
  let vy = 0;
  const dt = 1 / CAMERA_RATE;
  let pi = 0;
  for (let k = 0; k < n; k++) {
    const w = k * dt;
    t = targetAt(k);
    // Critically damped spring, integrated in small substeps for stability.
    for (let sub = 0; sub < 4; sub++) {
      const h = dt / 4;
      const ax = OMEGA * OMEGA * (t.x - px) - 2 * OMEGA * vx;
      const ay = OMEGA * OMEGA * (t.y - py) - 2 * OMEGA * vy;
      vx += ax * h;
      vy += ay * h;
      px += vx * h;
      py += vy * h;
    }
    x[k] = px;
    y[k] = py;
    let z = zoomAt(w);
    while (pi < punches.length && punches[pi].w < w - 1) pi++;
    for (let j = pi; j < punches.length && punches[j].w <= w + 0.05; j++) {
      const age = w - punches[j].w;
      const env = age < 0 ? smoothstep((age + 0.05) / 0.05) : Math.exp(-age / 0.22);
      z *= 1 + punches[j].amt * env;
    }
    zoom[k] = z;
  }
  const track: CameraTrack = { rate: CAMERA_RATE, w0: 0, x, y, zoom };
  ctx.camera = track;
}

export const VIEW_W = 960;
export const VIEW_H = 540;

export function cameraAt(c: CameraTrack, w: number): { x: number; y: number; zoom: number } {
  const f = Math.max(0, (w - c.w0) * c.rate);
  const k = Math.min(c.x.length - 2, Math.floor(f));
  const u = Math.min(1, f - k);
  return { x: lerp(c.x[k], c.x[k + 1], u), y: lerp(c.y[k], c.y[k + 1], u), zoom: lerp(c.zoom[k], c.zoom[k + 1], u) };
}

/** The camera view rectangle at world time w: [minX, minY, maxX, maxY]. */
export function viewRect(c: CameraTrack, w: number): [number, number, number, number] {
  const cam = cameraAt(c, w);
  const hw = VIEW_W / 2 / cam.zoom;
  const hh = VIEW_H / 2 / cam.zoom;
  return [cam.x - hw, cam.y - hh, cam.x + hw, cam.y + hh];
}
