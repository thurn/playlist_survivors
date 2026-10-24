/**
 * What the HUD writes, apart from how it draws it: the end card's lines and
 * the tier's label. Pure, so tests read them without a renderer.
 */
import type { HudState } from "../plan/evaluate.ts";
import { gradeFor } from "../plan/grade.ts";

/** Press ratings and styles: gold for Perfect, white for Great, pale blue for Good, dim for loose; grey for a double tap, red for a stray press. */
export const RATING_COLORS: Record<string, number> = { perfect: 0xffd040, great: 0xffffff, good: 0x9fd0ff, loose: 0x5a6070, extra: 0x4a4a54, stray: 0xe04848 };

/** Each tier's colour on the groove meter: cool at 0, gold at 3. */
export const TIER_COLORS = [0x7088a8, 0x60c0ff, 0x70f0c0, 0xffd040];

export interface HudLine {
  text: string;
  color: number;
}

/**
 * The end card's totals: kills, the rating counts (loose too), stray presses, the average groove,
 * the time at each tier and the experience collected of all that dropped.
 */
export function endCardLines(hud: Pick<HudState, "kills" | "ratings">, end: NonNullable<HudState["end"]>): HudLine[] {
  const lines: HudLine[] = [
    { text: `KILLS ${hud.kills}`, color: 0xffffff },
    { text: `PERFECT ${hud.ratings.perfect}`, color: RATING_COLORS.perfect },
    { text: `GREAT ${hud.ratings.great}`, color: RATING_COLORS.great },
    { text: `GOOD ${hud.ratings.good}`, color: RATING_COLORS.good },
  ];
  lines.push({ text: `LOOSE ${end.loose}   STRAY ${end.strays}`, color: RATING_COLORS.stray });
  lines.push({ text: `AVERAGE GROOVE ${Math.round(end.averageGroove * 100)}%`, color: 0x80e0ff });
  lines.push({ text: `TIME AT TIER  ${end.tierTime.map((t, i) => `${i}: ${Math.round(t)}S`).join("  ")}`, color: 0xc0c8e0 });
  lines.push({ text: `GEMS ${end.gems.collected} OF ${end.gems.dropped}`, color: 0x80ffd0 });
  return lines;
}

/**
 * The end card's grade: play mode's own (with the best grade and where the player took over), or
 * for a recorded run (the demo, or ?player=) the grade of its average groove.
 */
export function endCardGrade<T extends { grade: string; newBest: boolean }>(end: NonNullable<HudState["end"]>, given: T | undefined): T | { grade: string; newBest: false } {
  return given ?? { grade: gradeFor(end.averageGroove, end.reactive), newBest: false };
}

/** The tier's label beside the groove meter. */
export const tierLabel = (n: number): string => `TIER ${n}`;
