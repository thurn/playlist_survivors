/**
 * The camera: a critically damped spring that follows the recorded hero, plus
 * the section's lead along the route. A spring only reacts to past motion, so
 * the camera is causal; it never looks at future hero positions.
 */
import { lerp } from "../core/shaping.ts";
import type { CameraTrack } from "../plan/types.ts";

/** Camera samples per world-second. */
export const CAMERA_RATE = 60;
const OMEGA = 5.5;
export const VIEW_W = 960;
export const VIEW_H = 540;

/** The spring, stepped once per camera sample (1/60 world-second). */
export class CameraRig {
  x = 0;
  y = 0;
  private vx = 0;
  private vy = 0;
  private started = false;
  readonly xs: number[] = [];
  readonly ys: number[] = [];
  readonly zooms: number[] = [];

  /** Advance one sample toward the target and record it. */
  sample(tx: number, ty: number, zoom: number): void {
    if (!this.started) {
      this.x = tx;
      this.y = ty;
      this.started = true;
    } else {
      const dt = 1 / CAMERA_RATE;
      // Integrated in small substeps for stability.
      for (let sub = 0; sub < 4; sub++) {
        const h = dt / 4;
        const ax = OMEGA * OMEGA * (tx - this.x) - 2 * OMEGA * this.vx;
        const ay = OMEGA * OMEGA * (ty - this.y) - 2 * OMEGA * this.vy;
        this.vx += ax * h;
        this.vy += ay * h;
        this.x += this.vx * h;
        this.y += this.vy * h;
      }
    }
    this.xs.push(this.x);
    this.ys.push(this.y);
    this.zooms.push(zoom);
  }

  /** The latest view rectangle: [minX, minY, maxX, maxY]. */
  view(): [number, number, number, number] {
    const n = this.zooms.length;
    const zoom = n > 0 ? this.zooms[n - 1] : 1;
    const hw = VIEW_W / 2 / zoom;
    const hh = VIEW_H / 2 / zoom;
    return [this.x - hw, this.y - hh, this.x + hw, this.y + hh];
  }

  track(): CameraTrack {
    return { rate: CAMERA_RATE, w0: 0, x: Float32Array.from(this.xs), y: Float32Array.from(this.ys), zoom: Float32Array.from(this.zooms) };
  }
}

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
