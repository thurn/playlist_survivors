/**
 * The title screen: Play (the default), Watch the demo and Calibrate. Up and
 * Down (or W and S) move the selection; Enter, Space, Right or a click
 * chooses (which also unlocks audio). And the optional tap-along calibration.
 */
import { keyOf, loadInputOffset, saveInputOffset } from "../live/input.ts";
import type { Grade } from "../plan/grade.ts";
import type { AudioEngine } from "./audio.ts";
import type { Overlay } from "./app.ts";

export type TitleChoice = "play" | "demo" | "calibrate";

/**
 * Where a level's best grade is saved. (Grades from before the gameplay redesign were saved under
 * `ps.best.<level>`; they aren't comparable with today's, so they are left behind.)
 */
export const bestGradeKey = (level: string) => `ps.grade.v2.${level}`;

/** The best grade for a level, saved per browser (storage may be blocked: then there is none). */
export function loadBestGrade(level: string): Grade | undefined {
  try {
    const g = localStorage.getItem(bestGradeKey(level));
    return g === "S" || g === "A" || g === "B" || g === "C" ? g : undefined;
  } catch {
    return undefined;
  }
}

export function saveBestGrade(level: string, g: Grade): void {
  try {
    localStorage.setItem(bestGradeKey(level), g);
  } catch {
    // Storage blocked: the best grade lasts this session only.
  }
}

const ITEM_CSS = "font-size:22px;padding:6px 26px;border:1px solid transparent;border-radius:6px;cursor:pointer;color:#b8b0d8;min-width:280px;";
const SELECTED_CSS = "border-color:#ffe08099;color:#fff;background:#ffe08014;";

/** Show a menu in the overlay; resolves with the chosen index. */
export function overlayMenu(overlay: Overlay, items: string[], initial = 0): Promise<number> {
  const box = document.createElement("div");
  box.style.cssText = "display:flex;flex-direction:column;align-items:center;gap:6px;margin-top:6px;";
  box.dataset.menu = "title";
  let selected = initial;
  const els = items.map((label, i) => {
    const el = document.createElement("div");
    el.textContent = label;
    el.dataset.item = String(i);
    el.addEventListener("pointerenter", () => select(i));
    box.appendChild(el);
    return el;
  });
  const select = (i: number) => {
    selected = (i + items.length) % items.length;
    els.forEach((el, k) => (el.style.cssText = ITEM_CSS + (k === selected ? SELECTED_CSS : "")));
  };
  select(initial);
  overlay.prompt.style.display = "none";
  overlay.root.appendChild(box);
  return new Promise((resolve) => {
    const done = (i: number) => {
      window.removeEventListener("keydown", onKey);
      box.remove();
      resolve(i);
    };
    const onKey = (e: KeyboardEvent) => {
      const k = keyOf(e);
      if (k === "up") select(selected - 1);
      else if (k === "down") select(selected + 1);
      else if (k === "right" || e.key === "Enter" || e.key === " ") done(selected);
      else return;
      e.preventDefault();
    };
    window.addEventListener("keydown", onKey);
    els.forEach((el, i) => el.addEventListener("pointerdown", () => done(i)));
  });
}

/** The title screen's choice. */
export async function titleScreen(overlay: Overlay, best: Grade | undefined): Promise<TitleChoice> {
  overlay.status.textContent = "Arrows or WASD · Enter to choose · headphones recommended";
  const offset = loadInputOffset();
  const items = [`Play${best ? `   best ${best}` : ""}`, "Watch the demo", `Calibrate${offset ? `   (${offset > 0 ? "+" : ""}${offset} ms)` : ""}`];
  const i = await overlayMenu(overlay, items);
  return (["play", "demo", "calibrate"] as const)[i];
}

/** Clicks for the calibration: 8 at 120 BPM; the first two taps are dropped. */
export const CALIBRATION_CLICKS = 8;
export const CALIBRATION_BPM = 120;

/** The calibration offset (ms) from tap and click times (seconds): the median error after the first two taps. */
export function calibrationOffset(taps: number[], clicks: number[]): number | undefined {
  const errs: number[] = [];
  taps.slice(2).forEach((t) => {
    let best = Infinity;
    for (const c of clicks) if (Math.abs(t - c) < Math.abs(best)) best = t - c;
    // A tap more than a quarter-beat from any click is not aimed at one.
    if (Math.abs(best) <= 60 / CALIBRATION_BPM / 4) errs.push(best * 1000);
  });
  if (errs.length < 3) return undefined;
  errs.sort((a, b) => a - b);
  const m = errs.length >> 1;
  return errs.length % 2 ? errs[m] : (errs[m - 1] + errs[m]) / 2;
}

/** The tap-along calibration: tap any arrow along to 8 clicks; the offset is saved. */
export async function calibrate(overlay: Overlay, audio: AudioEngine): Promise<void> {
  overlay.status.textContent = `Tap any arrow along with the ${CALIBRATION_CLICKS} clicks`;
  const taps: number[] = [];
  const onKey = (e: KeyboardEvent) => {
    if (!keyOf(e) || e.repeat) return;
    e.preventDefault();
    taps.push(audio.contextTimeAt(e.timeStamp));
  };
  window.addEventListener("keydown", onKey);
  const clicks = await audio.clicks(CALIBRATION_CLICKS, CALIBRATION_BPM);
  await new Promise((r) => setTimeout(r, (clicks[clicks.length - 1] - audio.ctx.currentTime + 0.6) * 1000));
  window.removeEventListener("keydown", onKey);
  const offset = calibrationOffset(taps, clicks);
  if (offset === undefined) overlay.status.textContent = "Not enough taps on the clicks: the offset is unchanged.";
  else {
    saveInputOffset(offset);
    overlay.status.textContent = `Your taps land ${Math.abs(Math.round(offset))} ms ${offset >= 0 ? "late" : "early"}: play mode allows for it.`;
  }
  await overlayMenu(overlay, ["Back"]);
}
