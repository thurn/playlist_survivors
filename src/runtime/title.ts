/**
 * The title screen: Play (the default), Watch the demo, Reactive (experimental: a track list) and
 * Calibrate. Up and
 * Down (or W and S) move the selection; Enter, Space, Right or a click
 * chooses (which also unlocks audio). And the optional tap-along calibration,
 * for the direction keys or the stomp key, whichever the player taps with.
 */
import { gameKeyOf, keyOf, savedInputOffset, saveInputOffset } from "../live/input.ts";
import { keyGroup, type KeyGroup } from "../sim/timing.ts";
import type { Grade } from "../plan/grade.ts";
import type { AudioEngine } from "./audio.ts";
import type { Overlay } from "./app.ts";

export type TitleChoice = "play" | "demo" | "reactive" | "calibrate";

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
  const ms = (v: number) => `${v > 0 ? "+" : ""}${v} ms`;
  const offsets = [savedInputOffset("direction"), savedInputOffset("stomp")];
  const shown = [offsets[0] ? ms(offsets[0]) : undefined, offsets[1] !== undefined ? `stomp ${ms(offsets[1])}` : undefined].filter(Boolean);
  const items = [`Play${best ? `   best ${best}` : ""}`, "Watch the demo", "Reactive (experimental)", `Calibrate${shown.length ? `   (${shown.join(" · ")})` : ""}`];
  const i = await overlayMenu(overlay, items);
  return (["play", "demo", "reactive", "calibrate"] as const)[i];
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

/** Which keys a calibration's taps were made with: the group most of them came from (the directions on a tie). */
export function calibrationGroup(groups: readonly KeyGroup[]): KeyGroup {
  const stomps = groups.filter((g) => g === "stomp").length;
  return stomps > groups.length - stomps ? "stomp" : "direction";
}

/**
 * The tap-along calibration: tap along to 8 clicks with an arrow or with the stomp key (Z or
 * Space); the offset is saved for the kind of key tapped with.
 */
export async function calibrate(overlay: Overlay, audio: AudioEngine): Promise<void> {
  overlay.status.textContent = `Tap along with the ${CALIBRATION_CLICKS} clicks, with the key you play with: an arrow, or the stomp key (Z or Space)`;
  const taps: { t: number; group: KeyGroup }[] = [];
  const onKey = (e: KeyboardEvent) => {
    const k = gameKeyOf(e);
    if (!k || e.repeat) return;
    e.preventDefault();
    taps.push({ t: audio.contextTimeAt(e.timeStamp), group: keyGroup(k) });
  };
  window.addEventListener("keydown", onKey);
  const clicks = await audio.clicks(CALIBRATION_CLICKS, CALIBRATION_BPM);
  await new Promise((r) => setTimeout(r, (clicks[clicks.length - 1] - audio.ctx.currentTime + 0.6) * 1000));
  window.removeEventListener("keydown", onKey);
  const group = calibrationGroup(taps.map((x) => x.group));
  const offset = calibrationOffset(
    taps.filter((x) => x.group === group).map((x) => x.t),
    clicks,
  );
  const which = group === "stomp" ? "stomp-key" : "arrow";
  if (offset === undefined) overlay.status.textContent = `Not enough ${which} taps on the clicks: the offset is unchanged.`;
  else {
    saveInputOffset(offset, group);
    overlay.status.textContent = `Your ${which} taps land ${Math.abs(Math.round(offset))} ms ${offset >= 0 ? "late" : "early"}: play mode allows for it.`;
  }
  await overlayMenu(overlay, ["Back"]);
}
