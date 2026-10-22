/**
 * Keyboard input for play mode: arrows and WASD, each press and release at
 * its exact song time (from the event's timestamp, through the same mapping
 * the picture uses, less the player's calibration offset).
 */
import type { Key } from "../sim/movement.ts";
import type { KeyEvent } from "../sim/player.ts";

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

/** The player's calibration offset (ms): taps land this much late on average. Stored per browser. */
export const INPUT_OFFSET_KEY = "ps.inputOffsetMs";

export function loadInputOffset(): number {
  try {
    const v = Number(localStorage.getItem(INPUT_OFFSET_KEY));
    return Number.isFinite(v) ? v : 0;
  } catch {
    return 0;
  }
}

export function saveInputOffset(ms: number): void {
  try {
    localStorage.setItem(INPUT_OFFSET_KEY, String(Math.round(ms)));
  } catch {
    // Storage blocked: the offset lasts this session only.
  }
}

/** Collects key events between frames. */
export class KeyboardInput {
  private queue: KeyEvent[] = [];
  private held = new Set<Key>();
  enabled = true;
  /** Ignored keys (one step per slot greys them) are the simulation's business: every press goes in. */
  constructor(
    private readonly songTime: (perfMs: number) => number,
    private readonly offsetMs: () => number = () => 0,
  ) {}

  private readonly onDown = (e: KeyboardEvent) => {
    const k = keyOf(e);
    if (!k) return;
    e.preventDefault();
    if (e.repeat || !this.enabled || this.held.has(k)) return;
    this.held.add(k);
    this.queue.push({ s: this.songTime(e.timeStamp) - this.offsetMs() / 1000, kind: "press", key: k });
  };

  private readonly onUp = (e: KeyboardEvent) => {
    const k = keyOf(e);
    if (!k || !this.held.has(k)) return;
    this.held.delete(k);
    if (!this.enabled) return;
    this.queue.push({ s: this.songTime(e.timeStamp) - this.offsetMs() / 1000, kind: "release", key: k });
  };

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
    for (const k of this.held) this.queue.push({ s, kind: "release", key: k });
    this.held.clear();
  }

  /** The events since the last call, in time order. */
  drain(): KeyEvent[] {
    const out = this.queue;
    this.queue = [];
    return out.sort((a, b) => a.s - b.s);
  }
}
