# Look set: quick wins (ps-e8r.2, 2026-10-29)

Three settings fixes, rendered by the look harness after each (IL2CPP player,
Apple M5 Max, Metal). Each step's folder compares itself with the one before:

| step | folder | compared with |
|---|---|---|
| 1. fog off the playfield | [1-fog](1-fog/look.md) | baseline |
| 2. a key light on the horde | [2-crowd-light](2-crowd-light/look.md) | 1-fog |
| 3. a punchier grade (post-processing switched on) | [3-grade](3-grade/look.md) | 2-crowd-light |

[contact-sheet.jpg](contact-sheet.jpg): the baseline left, the finished
quick wins (3-grade) right.

## The finding: post-processing never ran

The URP renderer (`Assets/Demo3D/Settings/URP-Demo_Renderer.asset`) had no
`postProcessData`, and URP turns post-processing off altogether without it
(`UniversalRenderer`: `postProcessEnabled = data.postProcessData != null`).
So NightLook's bloom, tonemapping, colour adjustments, night LUT and vignette
never drew, on the Mac or on Cygnus: the baseline and steps 1 and 2 are
ungraded. Retuning the grade first moved at most 5 levels on 1% of pixels.
Step 3 points the renderer at URP's default PostProcessData (ProjectSetup does
it too) and then tunes the grade on what the stack actually does.

## 1. Fog

Linear fog in view depth, starting and ending at multiples of the camera's
focus distance (its view axis to the ground), refitted for each camera as it
renders (`NightLook.FitFogToCamera`). It works the same at every view width,
zoom punches included. Layout: `lighting.fog`
`{ "mode": "linear", "startAtFocus": 1.1, "endAtFocus": 2.4 }`; `"mode":
"exponential"` with `density` still works.

- The frame's centre is at the focus distance, so it gets no fog at any view
  width: song (22.5-30 m) or beauty shot (32-40 m). Before, it got 23-37%.
  `ArenaTests.TheFogClearsTheFrameCentreAndFadesItsFarEdge` checks this from
  camera-and-sound.md's geometry.
- The frame's top edge is 1.23 times the focus distance away, so it gets about
  10% fog, ramping to the full clear colour at 2.4 times. In the widest song
  frame (30 m) that's 38.8 m against a 31.5 m focus, and the arena's far edge
  and the forest beyond it fade into the clear colour.
- Against the baseline: chroma +0.3 to +0.7 and RMS contrast +0.7 to +1.4 in
  every song still; |dL*| 21.8-25.3 in the beauty shots (+2.9 to +4.4).

## 2. Crowd light

| parameter | before | after |
|---|---|---|
| `moon.intensity` | 0.95 | 0.65 |
| `moon.color` | [0.55, 0.66, 1.0] (blue) | [1.0, 0.88, 0.72] (warm-neutral) |
| `moon.elevationDeg` | 30 | 42 |
| `crowd.key` (new) | 1 | 4.6 |
| `crowd.fill` | 2.5 | 0.5 |
| `crowd.rim` | [0.6, 0.7, 0.95] | [0.22, 0.26, 0.36] |
| `crowd.rimPower` (new) | 3 | 5 |
| `ambient.sky` | [0.13, 0.17, 0.28] | [0.055, 0.07, 0.12] |
| `ambient.equator` | [0.08, 0.10, 0.15] | [0.035, 0.042, 0.065] |
| `ambient.ground` | [0.04, 0.04, 0.05] | [0.02, 0.02, 0.025] |

`crowd.key` is new: it multiplies the moon on the horde alone
(CrowdSkinned's `_Demo3DCrowdKeyBoost`, through `DEMO3D_MAIN_LIGHT_SCALE` in
StaticShadow.hlsl). A single moon strong enough to model the monsters lit
the ground as brightly: at intensity 2.4 the ground ring rose from L* 4 to 29
and |dL*| fell to 11-14, under the floor. Keying the horde above the set is
the stage-lighting answer, and costs one multiply. `crowd.rimPower` narrows
the rim to the silhouette. The kit's lightmaps were re-baked; `kitValue`
needed no change (no clipping in the empty shots).

- Monster L* spread (p90 - p10) in the measured stills, baseline then after:
  opening 43.0 → 77.4, path 44.3 → 84.4, glowing-tree 44.6 → 80.0,
  graveyard 43.6 → 78.9, courtyard 42.9 → 78.8, forest-edge 43.1 → 76.5.
- |dL*| 28.1-31.9 in every beauty shot.
- Clipped share in the empty beauty shots: 0.00% in every shot, as in the
  baseline.
- RMS contrast in the song stills against step 1: +1.0 to +5.1, **except
  song-175 at 31.7 against 32.2 (-0.6)**. That still is the final peak, whose
  centre is the hero's area attack, a flat clipped blob 8% of the frame (the
  effects bead, ps-e8r.7). Any light on the ground lowers the spread against
  it. Each try narrowed the gap: moon 1.1 → -2.8, 0.8 → -1.5, 0.65 → -0.8,
  plus lower ambient → -0.6. Step 3 takes it to 35.9.

## 3. Grade

`NightLook.CreateProfile` and `NightLook.Grade`, chosen from trial renders
(the `--tonemap`, `--post-exposure`, `--post-contrast`, `--post-saturation`
and `--bloom-threshold` player flags override them for tuning):

| setting | before (never ran) | after |
|---|---|---|
| tonemapping | Neutral | Neutral |
| postExposure | 0.35 | 0.8 |
| contrast | 6 | 25 |
| saturation | 4 | 30 |
| bloom threshold / intensity / scatter | 0.9 / 1 / 0.65 | 1.5 / 1 / 0.65 |
| LUT | shadows lifted toward blue-violet, cool mids, S-curve 0.18 | no lift, shadows a touch cool, warm midtones, near-neutral highlights, tint fading out with saturation, S-curve 0.3 on luminance |

- ACES crushed the dark ground: at exposures that kept the ground off black,
  the key-lit horde went white and the near-black share stayed over 30%.
  Neutral at 0.8 kept the ground's values and the effects' hues.
- Bloom: any threshold under about 1.5 bloomed the whole key-lit horde,
  whose lit sides reach 1-1.5 in linear HDR (at 0.6 the frame washed to
  L* 50 at the final peak). At 1.5, effects, wisps, flames and pickups glow
  and monsters don't. The old 0.9 never ran, so this is the first bloom the
  demo has had.
- The LUT scales channels together through its S-curve, so saturated colours
  keep their hue: `ArenaTests.TheGradeKeepsSaturatedHues` checks primaries and
  the effects' colours stay within 10°.
- Against step 2: L* p2 lower in every song still; mean chroma +1.1 to +4.7;
  RMS contrast +3.6 to +4.7. L* p98 higher in song-025, -060 and -145, but
  **not in song-100 (96.8 against 97.2) or song-175 (99.7 against 100.0)**.
  Song-175 was already at the ceiling. Song-100's p98 is in its effect cores,
  where Neutral tonemapping tops out near L* 96.8. ACES reaches 99, but at the
  cost above.
- |dL*| 35.4-41.3 in every beauty shot.

## Against the baseline

Every song still: mean chroma 2.1 times the baseline's, RMS contrast up 4.4 to
10.1, and the near-black share down 9 to 21 points (now 14.6-25.8%). Every
beauty shot's |dL*| roughly doubles (35.4-41.3, floor 17).

## Cost

Per epic Decision 5: no lights or passes added. The fog refit is two floats
per camera. The crowd key is one multiply per crowd pixel. The rim's `pow`
replaces `rim^3`. Post-processing now runs: URP's bloom, the uber pass with
tonemapping, the LUT and vignette, and the LUT build, all drawn by the stack
performance.md always budgeted for but never ran. Its frame cost on Cygnus
is unmeasured: the S0/S3 re-check row the palette bead adds covers it.
