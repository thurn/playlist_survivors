/**
 * Keyboard input for play mode: arrows, WASD, and Z or Space (a stomp), each press and release
 * at its exact song time (from the event's timestamp, through the same mapping
 * the picture uses, less the player's calibration offset for that kind of key).
 */
import type { InputKey, Key } from "../sim/movement.ts";
import type { KeyEvent } from "../sim/player.ts";
import { keyGroup, type KeyGroup } from "../sim/timing.ts";
import type { ClockProbe, KeyLog, PlayLog } from "./log.ts";

/** Gameplay keys: arrows by key, WASD by physical position. */
export function keyOf(e: { key: string; code: string }): Key | undefined {
  switch (e.key) {
    case "ArrowUp":
      return "up";
    case "ArrowDown":
      return "down";
    case "ArrowLeft":
      return "left";
    case "ArrowRight":
      return "right";
  }
  switch (e.code) {
    case "KeyW":
      return "up";
    case "KeyS":
      return "down";
    case "KeyA":
      return "left";
    case "KeyD":
      return "right";
  }
  return undefined;
}

/** Play mode's gameplay keys: the directions, and Z (by physical position) or Space for a stomp. */
export function gameKeyOf(e: { key: string; code: string }): InputKey | undefined {
  return keyOf(e) ?? (e.code === "Space" || e.key === " " || e.code === "KeyZ" ? "space" : undefined);
}

/**
 * The song time heard at performance time `perfMs` (a key event's timeStamp),
 * from the audio clock's latest output timestamp: the same mapping as
 * songTimeAtPresentation, including the output latency and ?avOffsetMs.
 */
export function songTimeAtPerformance(
  clock: { contextTime?: number; performanceTime?: number; currentTime: number; outputLatency: number; perfNow: number },
  t0: number,
  perfMs: number,
  avOffsetMs: number,
): number {
  const at = perfMs + avOffsetMs;
  if (clock.contextTime !== undefined && clock.performanceTime !== undefined && clock.performanceTime > 0) return clock.contextTime + (at - clock.performanceTime) / 1000 - t0;
  return clock.currentTime - clock.outputLatency + (at - clock.perfNow) / 1000 - t0;
}

/**
 * The player's calibration offsets (ms): taps land this much late on average, for the direction
 * keys, and for the stomp key (a long key may press later) as its difference from theirs, so a
 * change of headphones recalibrated with the arrows moves both. Stored per browser; until the
 * stomp key is calibrated itself, it plays with the directions' offset.
 */
export const INPUT_OFFSET_KEY = "ps.inputOffsetMs";
export const STOMP_EXTRA_KEY = "ps.stompExtraMs";

const readNumber = (key: string): number | undefined => {
  try {
    const raw = localStorage.getItem(key);
    if (raw === null) return undefined;
    const v = Number(raw);
    return Number.isFinite(v) ? v : undefined;
  } catch {
    return undefined;
  }
};

/** A key group's own calibrated offset, or undefined when it has none. */
export function savedInputOffset(group: KeyGroup = "direction"): number | undefined {
  if (group === "direction") return readNumber(INPUT_OFFSET_KEY);
  const extra = readNumber(STOMP_EXTRA_KEY);
  return extra === undefined ? undefined : (readNumber(INPUT_OFFSET_KEY) ?? 0) + extra;
}

/** The offset a key group plays with: its own, else (for the stomp key) the directions', else 0. */
export function loadInputOffset(group: KeyGroup = "direction"): number {
  return savedInputOffset(group) ?? savedInputOffset("direction") ?? 0;
}

/** The offset for a key's group. */
export const inputOffsetFor = (key: InputKey): number => loadInputOffset(keyGroup(key));

export function saveInputOffset(ms: number, group: KeyGroup = "direction"): void {
  try {
    if (group === "direction") localStorage.setItem(INPUT_OFFSET_KEY, String(Math.round(ms)));
    else localStorage.setItem(STOMP_EXTRA_KEY, String(Math.round(ms - (readNumber(INPUT_OFFSET_KEY) ?? 0))));
  } catch {
    // Storage blocked: the offset lasts this session only.
  }
}

/**
 * Which held key a key event is: its game key, but each stomp key apart (Z and Space are both
 * the stomp, and one may go down while the other is held).
 */
const heldSlot = (k: InputKey, e: { code: string }): string => (k === "space" ? `space:${e.code}` : k);

/** Collects key events between frames. */
export class KeyboardInput {
  private queue: KeyEvent[] = [];
  /** The keys down, by held slot. */
  private held = new Map<string, InputKey>();
  enabled = true;
  /** Extra presses (one rated press per slot greys them) are the simulation's business: every press goes in. */
  constructor(
    private readonly songTime: (perfMs: number) => number,
    /** The calibration offset (ms) for a key. */
    private readonly offsetMs: (key: InputKey) => number = () => 0,
  ) {}

  /** Play mode's timing log, and the audio clock's raw readings for it. */
  log?: PlayLog;
  clock?: () => ClockProbe;

  private readonly onDown = (e: KeyboardEvent) => {
    const k = gameKeyOf(e);
    if (!k) return;
    e.preventDefault();
    const slot = heldSlot(k, e);
    const fate: KeyLog["fate"] = e.repeat ? "repeat" : !this.enabled ? "disabled" : this.held.has(slot) ? "held" : "queued";
    if (fate !== "queued") return this.record(e, "down", k, fate);
    this.held.set(slot, k);
    const ev: KeyEvent = { s: this.songTime(e.timeStamp) - this.offsetMs(k) / 1000, kind: "press", key: k };
    this.record(e, "down", k, fate, ev);
    this.queue.push(ev);
  };

  private readonly onUp = (e: KeyboardEvent) => {
    const k = gameKeyOf(e);
    if (!k) return;
    const slot = heldSlot(k, e);
    if (!this.held.has(slot)) return this.record(e, "up", k, "not-held");
    this.held.delete(slot);
    if (!this.enabled) return this.record(e, "up", k, "disabled");
    const ev: KeyEvent = { s: this.songTime(e.timeStamp) - this.offsetMs(k) / 1000, kind: "release", key: k };
    this.record(e, "up", k, "queued", ev);
    this.queue.push(ev);
  };

  /** Log a key event (and give a queued one its id). */
  private record(e: KeyboardEvent, type: KeyLog["type"], k: InputKey, fate: KeyLog["fate"], ev?: KeyEvent): void {
    const log = this.log;
    if (!log) return;
    const id = log.newKeyId();
    if (ev) ev.id = id;
    log.key({ id, type, key: k, code: e.code, timeStamp: e.timeStamp, handledAt: performance.now(), repeat: e.repeat, fate, s: ev?.s, offsetMs: ev ? this.offsetMs(k) : undefined, clock: ev ? this.clock?.() : undefined });
  }

  attach(target: Window = window): void {
    target.addEventListener("keydown", this.onDown);
    target.addEventListener("keyup", this.onUp);
  }

  detach(target: Window = window): void {
    target.removeEventListener("keydown", this.onDown);
    target.removeEventListener("keyup", this.onUp);
  }

  /** Release every held key at song time s (losing focus, pausing). */
  releaseAll(s: number): void {
    for (const k of this.held.values()) this.queue.push({ s, kind: "release", key: k, id: this.log?.newKeyId() });
    this.held.clear();
  }

  /** The events since the last call, in time order. */
  drain(): KeyEvent[] {
    const out = this.queue;
    this.queue = [];
    return out.sort((a, b) => a.s - b.s);
  }
}
