/**
 * The display floating over the hero's head: life and groove as two wings
 * around a gem that keeps the beat (a ring closes on each beat and the gem
 * strikes it), or breathes with the bar with sparks orbiting it in free
 * movement. What it shows, apart from how the HUD draws it, so tests read it
 * without a renderer.
 */
import { beatHit, beatSwell, clamp, smoothstep } from "../core/shaping.ts";
import type { Frame, HudState } from "../plan/evaluate.ts";
import { worldToScreen } from "./view.ts";

/** The top of the hero's head over its feet (world pixels; the sprites stand 64 tall, the head ~50 up). */
export const HEAD_HEIGHT = 50;
/** The display's size against the rest of the HUD. */
export const OVERHEAD_SCALE = 1.3;
/** The display's centre over the head (HUD units). */
export const OVERHEAD_GAP = 16;
/** Half the display's width (HUD units): it stays this far inside the view's edges. */
export const OVERHEAD_HALF_W = 36 * OVERHEAD_SCALE;

export interface OverheadState {
  /** The screen point the display centres on. */
  x: number;
  y: number;
  alpha: number;
  /** Life (0–1), the hurt flash and the heal glow, and how low life is (0 above 30%, 1 at none). */
  health: number;
  hurt: number;
  heal: number;
  danger: number;
  /** The groove meter (0–1), its tier, the tier marks and the tier-up flash. */
  groove: number;
  tier: number;
  marks: number[];
  tierFlash: number;
  /** How much of the beat look shows: 1 with a steady beat, 0 in free movement (a count-in fades it in). */
  step: number;
  /** The beat just struck (1 on it, decaying) and the beat's phase (the ring closes as it nears 1). */
  hit: number;
  beatPhase: number;
  /** What the next beat telegraphs: a plain beat, a downbeat, or the beat to press for a cleave. */
  next: "beat" | "down" | "press";
  /** The glow over the beat before a cleave's accent (0–1). */
  glow: number;
  /** Free movement: the breath over the bar (1 on the downbeat) and the sparks' orbit (turns, one per bar). */
  breathe: number;
  spin: number;
}

/** What the overhead display shows this frame, or nothing (on the end card). */
export function overheadState(f: Pick<Frame, "s" | "camera"> & { hero: Pick<Frame["hero"], "x" | "y" | "z"> }, hud: HudState, out: { x: number; y: number; w: number; h: number }): OverheadState | undefined {
  if (hud.end) return undefined;
  const u = out.h / 540;
  const head = worldToScreen(f.camera, out, f.hero.x, f.hero.y - f.hero.z - HEAD_HEIGHT);
  const margin = OVERHEAD_HALF_W * u;
  const x = clamp(head.x, out.x + margin, out.x + out.w - margin);
  const y = clamp(head.y - OVERHEAD_GAP * u, out.y + 16 * u, out.y + out.h - 16 * u);
  const next = hud.ticks.find((t) => t.dt > 0);
  const health = clamp(hud.health, 0, 1);
  return {
    x,
    y,
    alpha: smoothstep((f.s - 0.5) / 1.5),
    health,
    hurt: hud.hurt,
    heal: hud.heal,
    danger: clamp(1 - health / 0.3, 0, 1),
    groove: clamp(hud.groove, 0, 1),
    tier: hud.tier.n,
    marks: hud.tier.marks,
    tierFlash: hud.tier.flash,
    step: hud.free ? (hud.countIn?.fade ?? 0) : 1,
    hit: beatHit(hud.beatPhase),
    beatPhase: hud.beatPhase,
    next: next?.pressHere || next?.cleave ? "press" : next?.down ? "down" : "beat",
    glow: hud.heartGlow ?? 0,
    breathe: beatSwell(hud.barPhase),
    spin: hud.barPhase,
  };
}
