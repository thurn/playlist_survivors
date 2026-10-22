/**
 * Play-mode rule changes. Each is set from the level file, so the demo (with
 * DEMO_RULES) never sees one.
 */

export interface Rules {
  /** Step movement: only the first press for each grid time steps, is rated and arms a swing. */
  oneStepPerSlot: boolean;
  /** The HUD shows the cleave accents in advance. */
  accentTelegraphs: boolean;
  /** The hero has health: enemies that touch it bite, arrows and slams hurt, and at none the run is over. */
  health: boolean;
  /** Fixed-speed strides: every press strides at once, a held key strides on every step of the stride period. */
  strides: boolean;
  /** Press classes and the groove meter: every press is classed against the grid, and the groove fills, drains and sets a tier. */
  pressClasses: boolean;
  /** Every weapon fires by itself: the sword on every grid time, presses arm nothing, and the tier sets attack area and damage. */
  selfFiring: boolean;
  /**
   * Every percussive gameplay sound plays on a slot of its subdivision, and never late: a sound
   * known less than 60 ms before its slot moves to a later slot (src/planner/emit.ts).
   */
  gridSounds: boolean;
  /**
   * Accents and cleaves: an eligible accent's swing is heavy for everyone, sized by the tier; a
   * counted press of Great or better on the chord (early or late) launches its shockwave from the
   * eighth after it, and only an early press brings a hit-stop.
   */
  pressCleaves: boolean;
}

/** The scripted demo's rules: today's behavior. */
export const DEMO_RULES: Readonly<Rules> = Object.freeze({ oneStepPerSlot: false, accentTelegraphs: false, health: false, strides: false, pressClasses: false, selfFiring: false, gridSounds: false, pressCleaves: false });
