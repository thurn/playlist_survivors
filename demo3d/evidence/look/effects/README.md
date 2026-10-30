# Look set: effects (ps-e8r.7, 2026-10-29)

Effects with hot cores and shape, rendered by the look harness (IL2CPP
player, Apple M5 Max, Metal):

| folder | what |
|---|---|
| [before](before/look.md) | staging `7ee8641b`, where this bead started (the quick wins, clumps and stylelab in) |
| [after](after/look.md) | this bead rebased onto staging `eeafd856` (the hero's light, ps-e8r.5, landed meanwhile), compared with [hero-pool](../hero-pool/look.md), that staging without this bead |

[after/contact-sheet.jpg](after/contact-sheet.jpg): hero-pool left, after right.
The tuning was done against `before`; the numbers below come from both.
How each knob works: [effects.md, The look](../../../../docs/tech-demo-3d/effects.md#the-look).

## What made the blob

The final peak's "flat yellow blob" (song-175) was three things, found by
rendering that still with `--only-effects` (a new player flag) one group at a
time:

1. **The horde's white hit flash.** With no effects at all, flashing bodies
   alone clipped to a white blob 2.6% of the frame (`_FlashColor` 2.5, 2.4,
   2.2 on key-lit bodies).
2. **White light cues on every hit.** hit-spark-b's cue (white, intensity 8.9)
   and hit-heavy's (white, range 10), hundreds a second, washed the ground and
   the bodies yellow-white. Shrinking and dimming hit-spark-b's sprites
   changed nothing; recolouring its cue removed the haze.
3. **Large additive hit sprites** (4 m and 3 m billboards per hit) stacking.

## What changed

- **Heat ramp** (draw shader, `heat` in effects.json): fire and impacts go
  white → orange (1, 0.5, 0.08) → red (1, 0.06, 0.02); the ring white → gold
  (1, 0.72, 0.2) → orange (1, 0.25, 0.02); lightning and its circle white →
  lilac (0.78, 0.5, 1) → violet (0.4, 0.08, 1), off the runes' cyan. Gain 1
  throughout. The sword slashes keep their own blue.
- **Large layers shrunk and dimmed** (`tune`): hit-spark's Hit 1 and
  ShockWave to 0.3/0.35 size at 0.4/0.35 HDR; hit-spark-b's Hit 2, Flash and
  ShockWave to 0.35-0.45 size at 0.35-0.4 HDR, its 3.5 m glow quad skipped
  (bloom draws the halo); the slash and spin Hit/Flare flashes to 0.35-0.6;
  slam-shock's ShockWave 0.6 at 0.6 HDR; the death bursts' flash and wave to
  0.4-0.5. Effect-wide HDR 0.8 on the death bursts and the slam.
- **Smoke stays smoke**: hit-spark-b's and death-burst's smoke out of the
  heat ramp, at 0.4-0.6 HDR.
- **Sparks brighter, and added**: hit-spark's sparks and debris at 2x HDR;
  our own spark bursts on the ring (36, 8-16 m/s, mostly outward) and the
  lightning strike (24, violet).
- **Circles smaller, bolder, briefer**: lightning-circle at 0.55 size, 0.45
  life, 1.6 HDR (its Fire layer at 0.4); the ring's wave at 0.6 life, 1.4 HDR.
- **Light cues in heat colours**: the hits orange (1, 0.5, 0.15) at intensity
  1-3, range 3-5; the ring gold; lightning and its circle violet; the bolt
  orange.
- **Hit flash** (`lighting.crowd.flash`): an ember (1.3, 0.65, 0.2) instead of
  white. Tried 0.45-1.6 in red: at 0.8 the bodies keep their shading; above
  1.2 they read as flat yellow silhouettes; none clips. 1.3 kept the most
  contrast (+0.8 over 0.8) with no blob.

## Against the acceptance criteria

**Largest connected clipped region** in the drops and the final peak (the
harness's new `clipped blob` column, on the full PNGs):

| still | before | after, tuning (no hero light) | after, rebased |
|---|---:|---:|---:|
| song-060 (drop-1) | 0.01% | 0.00% | 0.00% |
| song-145 (drop-2) | 0.01% | 0.01% | 0.00% |
| song-175 (final peak) | **6.21%** | **0.01%** | **0.00%** |

hero-pool predates the column and its PNGs are gone; on the 960 px JPEGs
(which understate it), its song-175 blob is 2.07% against before's 2.09%:
the hero's light didn't change it.

**The effects diff** (beauty shots; the pixels the effects move by more than
5 L*): a smaller share and a higher mean chroma in every shot, against both
starting points.

| shot | share before | hero-pool | after | mean C*ab before | hero-pool | after |
|---|---:|---:|---:|---:|---:|---:|
| opening | 16.4% | 15.0% | 11.3% | 57.1 | 59.9 | 67.9 |
| path | 18.2% | 17.2% | 12.1% | 50.9 | 52.5 | 62.3 |
| glowing-tree | 21.3% | 19.2% | 12.5% | 45.6 | 48.2 | 57.9 |
| graveyard | 19.5% | 17.5% | 8.9% | 38.8 | 40.9 | 54.0 |
| courtyard | 10.5% | 9.9% | 6.0% | 42.7 | 43.7 | 57.7 |
| forest-edge | 11.8% | 10.1% | 3.7% | 33.6 | 37.4 | 50.7 |

The share of effect pixels that clip rose (0.1-0.2% to 0.4-3.2%): small
white-hot cores now, where before there were none, only broad pale glows.

**|dL*|** 31.7-39.5 in every beauty shot, as in hero-pool (this bead moves
none). demo3d's EditMode tests: 223/226 (the 3 SkeletonLodPreview skips).

## Side effects to know

- **song-175's RMS contrast fell, 34.6 to 29.8** (hero-pool's 34.6 too),
  under the epic baseline's 31.5. The white blob on a dark ground was most of that still's L* spread.
  A brighter flash barely moves it (29.6-30.3 for flashes 1.0-1.6). The
  palette pass should weigh it against the whole-run criterion.
- The other song stills moved by under 1 point against hero-pool (contrast
  -0.3 to -0.8, chroma within 0.4) and their near-black share rose 0.2-0.8
  points: the white cues no longer light the ground.
- The stage measure (ps-e8r.5) at song-175 fell from dL* 51.8 to 26.1, still
  over its bar of 15: the "lit ground" round the hero there was the blob.
  Its `opening` row fails as it did in hero-pool (the bar is for song
  stills).

## Cost

No new passes or lights. The heat ramp is a few ALU ops per effect fragment
(one branch on a per-emitter value); the emitter record grows from 26 to 28
float4s. The ring and lightning gain 36 and 24 spark particles per spawn;
the shrunk layers and the skipped glow quad cut more overdraw than that adds.
Unmeasured on Cygnus.
