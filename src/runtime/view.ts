/**
 * The world view's geometry: the visible world rectangle for a camera, and
 * where a world point lands on screen. Shared by the renderer (which composites
 * that rectangle to the screen) and the HUD (which draws over the hero).
 */
import type { Frame } from "../plan/evaluate.ts";

export const VIEW_W = 960;
export const VIEW_H = 540;
/** The world render texture's size (the most world one frame can show, zoomed out). */
export const RT_W = 1400;
export const RT_H = 790;

/** The visible world rectangle (world pixels) for a camera, shake included. */
export function viewRect(camera: Frame["camera"]): { left: number; top: number; viewW: number; viewH: number } {
  const zoom = camera.zoom;
  const viewW = Math.min(RT_W - 2, VIEW_W / zoom);
  const viewH = Math.min(RT_H - 2, VIEW_H / zoom);
  return { left: camera.x + camera.shakeX / zoom - viewW / 2, top: camera.y + camera.shakeY / zoom - viewH / 2, viewW, viewH };
}

/** Where a world point lands on screen, and how many screen pixels one world pixel spans. */
export function worldToScreen(camera: Frame["camera"], out: { x: number; y: number; w: number; h: number }, x: number, y: number): { x: number; y: number; scale: number } {
  const v = viewRect(camera);
  return { x: out.x + ((x - v.left) * out.w) / v.viewW, y: out.y + ((y - v.top) * out.h) / v.viewH, scale: out.h / v.viewH };
}
