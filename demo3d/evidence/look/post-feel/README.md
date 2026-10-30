# Look set: post feel (ps-e8r.18, 2026-10-30)

The post stack now moves with the music. Every beat pulses the exposure, the bloom and the vignette.
Accents, free sections' anchors and the hero's big hits flash, and each flash decays within a beat.
Each section gets its own grade: breaks and the breakdown are cooler and darker, and the drops and
the final peak are hotter and more saturated.

Rendered by the look harness (IL2CPP player, Apple M5 Max, Metal) against
[ground](../ground/look.md), the last set on staging, and by [feel.py](feel.py) for the stills
that show the feel itself:
- [look.md](look.md): the standard set. [contact-sheet.jpg](contact-sheet.jpg) has ground on
  the left and this set on the right.
- [feel.md](feel.md): the feel's stills and numbers.

## What changed

- **`PostFeel`** (Runtime/Playback/PostFeel.cs), beside `CameraFeel`. `DemoMain` applies it every
  frame at `feelT`, the song time as heard. It reads the song map's beats, accents, sections and
  free sections' anchors, and the timeline's hero attacks. It writes offsets onto the night volume's
  base values, which it reads once when it binds. The base values come from NightLook, including the
  hero pool's vignette and any `--post-*` tuning flags, so the hero-pool and grade beads still own
  them.
  - **Beat:** a pulse of `(1 - u)^2` over the beat (u is the beat phase), in pulsed sections only.
    It adds to exposure and bloom intensity and narrows the vignette.
  - **Flash:** `strength * (1 - u)^3` over the beat of the accent, anchor or hit. Overlapping
    flashes take the strongest, not the sum. It adds to exposure (the white flash), chromatic
    aberration and saturation.
  - **Section grade:** offsets to exposure, saturation, contrast and white-balance temperature, plus
    a multiple on the beat pulse. A section uses the first entry whose `match` starts its label, so
    `break` covers break-1, break-2 and breakdown. Each grade eases in from the previous section's
    over two bars from the section's first downbeat (smoothstep), or over half the section if it is
    shorter, like the camera's zoom arc. It never cuts.
- **NightLook** adds `WhiteBalance` and `ChromaticAberration` to the profile at inert bases (0), so the
  feel has somewhere to write. Both are URP post-stack settings: white balance is folded into the
  colour-grading LUT, and chromatic aberration runs in the uber pass. Neither adds a pass.
- **`ArenaRuntime.Post`** exposes the volume, replacing the private `_post` object.
- **Flags:** `--no-feel` now also turns off the post feel, along with punch and shake. The new
  `--no-post-feel` turns off only the post feel.
- **The look harness** runs all three captures of every beauty shot with `--no-post-feel`. That is
  the steady state: the beat's phase at a shot's `t` never moves the readability, effect and stage
  numbers, so later beads' numbers stay comparable. The beauty rows of look.md match ground's to
  within 0.05 dL*. The song stills keep the feel, because they are the song as played.

## Values (arena-layout.json `lighting.postFeel`)

| parameter | value | note |
|---|---|---|
| `beat.exposure` | 0.12 stops | |
| `beat.bloom` | 0.35 | on bloom intensity (base 1) |
| `beat.vignette` | -0.03 | on vignette intensity (base 0.29): it opens on the beat |
| `flash.exposure` | 0.7 stops | at strength 1 |
| `flash.chromatic` | 0.5 | URP intensity, 0-1 |
| `flash.saturation` | 25 | |
| `hit.minStrength`, `hit.flash` | 0.95, 0.6 | hero attacks this strong flash at 0.6 of their strength |
| `easeBars` | 2 | |

| sections matching | exposure | saturation | contrast | temperature | pulse |
|---|---:|---:|---:|---:|---:|
| `intro`, `ending` | -0.1 | -8 | 0 | -6 | 0.7 |
| `break` (break-1, break-2, breakdown) | -0.3 | -15 | 0 | -14 | 0.6 |
| `build` | +0.05 | +5 | 0 | +3 | 1 |
| `drop` (drop-1, drop-2) | +0.12 | +15 | +6 | +8 | 1.2 |
| `final-peak` | +0.18 | +22 | +10 | +12 | 1.3 |
| `outro` | -0.05 | -5 | 0 | -4 | 0.8 |
| others (groove, groove-2) | 0 | 0 | 0 | 0 | 1 |

These are the first values tried, and the stills read as intended, so I did not tune further. A
0.7-stop flash takes the frame's median L* from 34 to 52 for one frame. It is a clear hit without
whiting out the frame, because Neutral tonemapping holds the highlights (p98 moves less than 1). The
peak's +22 saturation stacks on the grade's +30. It looks hot but not neon in final-peak-a.jpg. The
palette pass may want to temper it.

## Acceptance

- **The feel's curves** (EditMode, Tests/EditMode/PostFeelTests.cs, all passing):
  - In every pulsed section, past its ease, the mean exposure offset on beats is above the offset
    0.6 of a beat later.
  - Every isolated accent's flash is over half its strength just after it, and zero one beat
    later, chromatic aberration included.
  - Mid-drop and mid-peak grades are above every break's in saturation, exposure and temperature.
  - A section boundary has no jump.
  - The offsets are all zero when the feel is off (`--no-feel`, `--no-post-feel`) and are a pure
    function of song time.
  - Rendered: on a drop-2 beat, the frame's median L* is 39.4 against 35.0 0.6 of a beat later, and
    33.1 with `--no-feel`. The [strip](strip.jpg) around the accent at 148.655 s rises from 33.9 to
    52.5 in one frame and is back to 34.1 by +378 ms, inside the 387 ms beat. The next beat's pulse
    shows at +412 ms.
- **Drops and peak above the breakdowns:** in feel.md's section stills, the lowest drop or peak
  mean chroma is 39.8 against the highest breakdown chroma, 20.1. Each still is 0.6 of a beat after
  a quiet downbeat, so the grade dominates. In the standard set against ground, drop-1, drop-2 and
  the final peak gain 9.2, 9.2 and 14.8 chroma. The groove stills (no grade) gain only their
  beat's pulse, about 1.2.
- **Reproducibility:** the post feel is a function of song time, so it adds no variation. The
  mid-flash frame rendered by two processes with `--no-effects` is identical: 0 channel difference.
  With effects, renders already differ between runs (see Surprises).
- **|dL*| >= 17:** 23.0-27.7 in every beauty shot, unchanged from ground (steady state). demo3d's
  EditMode tests pass: 223/233, with 10 skips for bakes absent when the suite ran, all
  bake-dependent tests that predate this bead.
- **Expected cost:** no pass, light or texture sample added. URP rebuilds its grading LUT every
  frame anyway, so white balance is free. Chromatic aberration is a few extra taps in the existing
  uber pass, and only while a flash is decaying (intensity 0 disables it). PostFeel's CPU cost is a
  binary search and a few parameter writes per frame.

## Surprises

- **The effects' particles are not reproducible between runs.** Two single-still runs of the same
  frame differ on 0.2% of pixels (0.03% by more than 2 levels). A run that also captured an earlier
  still differs from a single-still run on 13.6% of pixels (2.1% by more than 2), around the
  effects. With `--no-effects`, renders are byte-identical. This predates the post feel: the same
  differences appear with `--no-post-feel`. The look harness's single-still captures are barely
  affected.
- Song-175's RMS contrast (26.2) stays below the run's original baseline (31.5). It was already
  below at the ground step (26.1); the post feel moves it +0.1.
