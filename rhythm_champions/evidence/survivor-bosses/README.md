# Three retained-horde encounters — ps-3wc.11

## Runtime contract

`SimState.RunClock` remains actual combat duration, including boss fights and
excluding cards/player pauses. `HordeClock` is the spawn ramp, zone fallback and
encounter clock. It freezes from each call through defeat; calls are at
300/600/900 horde seconds. A call stops ordinary spawns and formed waves, warns
with a yellow ground seal and camera ease, then arrives on the first nearby
downbeat after two seconds, or at four seconds without one (two beatless).
Arrival adds one boss. It deletes no monsters, drops, elite telegraphs, spores
or structures, and disables no structure streaming. Attacks can subsequently
break the structures their actual shapes touch.

The first two kills heal fully before immediately holding for an owned-item
upgrade. Ordinary spawning resumes, with time still held by the card. The
third kill heals and ends in victory. No entrance or victory screen clear runs
in current combat. No current dodge emits Break/stagger events or changes boss
damage susceptibility. Archived mechanics/content are isolated in historical
test fixtures; their event IDs remain compatible.

## Patterns and deliberate refinements

- Ogre: club lane, boulder constellation, local stomp, charge, expanding seal.
- Conductor: existing troll bake at 4 m in its own GPU type (retained elite
  trolls still draw), alternating three baton lanes with gaps, successive
  marked strikes and a traveling pulse wall. The existing attack clip conducts
  the lanes/wall. These are telegraphed ground fan lanes rather than newly
  imported projectile VFX or an unverified conductor animation.
- Dragon: breath/sweeping breath, successive fireballs, tail, marked dive,
  two concentric expanding seals. Fronts are at least 1.1 seconds apart,
  rounded up to whole beats, exceeding the baseline 0.6-second dash cooldown.

The seals lock to the hero's position when warned, rather than the boss's
feet: their initial disc and faster outward front prevent walking behind the
origin to avoid crossing. The wall starts behind the full walking reach of
its warning, including maximum Stride, and sweeps across the local arena. Front collision uses the exact
ring/path drawn, current position and ordinary physical i-frames. It needs no
perfect timing grade. Marks persist during travel; collision does not rely on
a launch-beat-only hit or a synthetic dash-start collision.

All new boss warnings reserve at least 1.1 seconds, rounded up to whole beats,
and retain their promised deadline through tempo/confidence changes. New
beatless warnings use 0.5-second pulses. Holds freeze/restore pending warnings,
flights, burns and fronts; remaining reaction time never shrinks. No boss
summons ship: the finite retained horde is the entire ordinary XP budget during
an encounter, so waiting does not create an unlimited farm.

## Verification and visual judgment

- 439 integrated .NET sim tests passed, including three real thresholds, spawn stop/resume,
  retained monsters/drops/structures, reward order, both clocks, victory,
  one launch event per front at exact tick boundaries, walking escape fixtures in both phases at 0/60/174/240 BPM, physical dash
  crossings, cooldown recovery, player holds and confidence loss.
- 112 Unity EditMode tests passed, no skips; Mono player built successfully.
- `fixture-duel` was deliberately re-recorded against the current encounter:
  no entrance clear or Break, stronger legacy arsenal, traveling-front dodge
  inputs, longer synthetic song. Its headless replay reproduces the record.
- Actual player previews: `champions stills --scene duel-ogre --scene
  duel-conductor --scene duel-dragon`. Each starts with 80 tough ordinary
  monsters, including elite trolls, real structures, and six level-5 weapons
  (Sword, Hammer, Bow, Orbit Blades, Lightning, Ember Field). Tough preview
  bodies keep the field populated for every pattern; this is a readability
  stress scene, not representative encounter DPS.

Private captures are retained under
`~/Library/Caches/playlist-survivors/evidence/ps-3wc.11/`. No vendor-derived
images or baked assets are committed. The scripted scenes reproduce them.

Author critique: the traveling red wall is a continuous, unmistakable lane
beneath the cyan/orange crowd. The expanding red ring stays closed and legible
around the six-weapon cluster; its dark interior keeps it separate from white
hero attacks. Fan lanes retain clear dark gaps. Repeated boss hit flashes
initially washed its colour toward white; their strength was reduced to 35%,
so the body keeps its enemy hue. The Conductor's scale and attack choreography
distinguish it from the retained trolls. Screenshots sample real motion at
warning/travel phases; they do not substitute for an operator's timing/taste
judgment at a physical display. Speaker/hardware checks remain operator work.
