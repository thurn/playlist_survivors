/**
 * Play-mode rule changes. Each is set from the level file, so the demo (with
 * DEMO_RULES) never sees one.
 */

export interface Rules {
  /** Step movement: only the first press for each grid time steps, is rated and arms a swing. */
  oneStepPerSlot: boolean;
  /** The HUD shows the cleave accents in advance. */
  accentTelegraphs: boolean;
}

/** The scripted demo's rules: today's behavior. */
export const DEMO_RULES: Readonly<Rules> = Object.freeze({ oneStepPerSlot: false, accentTelegraphs: false });
