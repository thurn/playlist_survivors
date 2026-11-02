# The weapon effects' gallery (ps-6y2.16)

Every weapon effect of the hero's (design.md, The Look > Weapon effects; Attacks, Dashes, Mods), one still each, kept
by the gallery scene: each cue fired on a clean frame at the hero's feet (the white ring stands in for the hero until its
edge cut lands, ps-6y2.20) on the void and its grid, under the game's camera (60 degrees down, 23 m, FOV 40) and post
at the high tier, its still taken where its shape reads best (its head arrived, before the fade). The stills are crops
of the frame's middle two thirds, 640 x 360.

Reproduce (the build bakes the effect library from the synced Hovl files first):

```sh
just champions-build
just champions-tool stills --scene gallery          # captures/stills: still-, id-, view-gallery-LABEL, gallery-costs.json
just champions-tool look --out rhythm_champions/evidence/look/gallery
just champions-play --headless --fixed-time --capture-stills captures/gallery-timing --stills-scenes gallery \
  --tier low --bench-out captures/gallery-timing/costs.json   # adds each entry's GPU time
just champions-play --gallery --windowed            # the gallery in a window, looping, to watch them move
```

## How they are drawn

- **Hovl's shapes, our shader.** `Render/Effects/Shaders/Slash.shader` draws Hovl's slash meshes (Slash1, the
  crescent; Slash2-4, the rings) and his magic sword, referenced from the synced vendor files by
  `Assets/Baked/Effects/Resources/Effects.asset` (`EffectBaker`, run before every build), never copied. Each mesh is
  normalised to a unit ring by a frame measured from its vertices; an arc shows only within its sweep, its head going
  fast in and slow out, the band coming to a point at the head and narrowing to nothing behind it (Hovl's crescent
  taper), a thin bright core at the outer edge over a faint body, Hovl's noise texture (his sword slash's dissolve
  noise) streaking it along the arc and dissolving it as it fades. White core (`hero.blade`), faint white-blue edge
  (`hero.edge`). Rings (shockwaves) draw an even band; ribbons (dash smears, trails) and the solid sword (rim-lit)
  share the shader.
- **Sparks** are compute: one dispatch seeds a frame's bursts (a thread group each) into a ring buffer, and one
  procedural draw places every live spark in closed form from its birth, so no compute runs on frames without new
  bursts.
- **Recipes**: `Content/effects.json`, a cue's layers (arcs, ribbons, uprights, solids, sparks) with their timing,
  size, brightness and placement; powers scale brightness and sparks (on the beat 1.4x, special moments 1.8x).

## Look

`champions look` on the 29 stills ([metrics](../look/gallery/metrics.md)):

- **Palette conformance passes**: 100% on every still with colourful pixels at L* 40+ (only the grid's blue near the
  rings); the rest have none: every effect pixel is white or the faint white-blue edge (C* under 30). One family per
  still (the zone's), key60 1.0. An EditMode test (`EffectsTests`) renders a slash and a shockwave and finds no pixel at
  C* 30 or more, and the ID pass marking the lit shapes as effects.
- Value and restraint pass on every still: black share 86-98%, bright share at most 5.4%, clipped at most 1.0%, the
  largest clipped region at most 0.5%, white at most 2%, the floor's p50/p90/p99 within their rungs.
- **Exception, `background_light_max`** (16-52% against 15% on 20 of the 29 stills): the rule is the frame's light off
  everything the ID pass marks, and the gallery's frame holds nothing but the grid and one effect, so the grid's own
  light and the effects' bloom are most of it. The world scene's stills (the grid and the hero's ring, no effects)
  measure 73-80% on the same rule; the effects lower it. It judges a full frame with its crowd (the palette scene's
  is 3.9%), which the assembly's stills (ps-6y2.21) measure.
- The special moments' stills are judged as steady frames (they are taken mid-effect, not on the impact frame), so
  Full Moon's and Skyfall's rings sit dimmer than their first frames would allow.

## Cost per cue

Draws and shapes are what the still's frame drew for that cue alone; triangles and sparks what it adds when it fires;
compute groups its sparks' seed dispatch on the frame it fires (64 threads each, no compute after). GPU time is the
median, over 31 pairs, of a 1080p frame drawn by hand to the GPU's completion with the effect less without (the CPU's
submission included: an upper bound), on an Apple M5 Max at the low tier, in two runs: every cue is under 0.15 ms, and
most sit inside the method's noise (about 0.05 ms either way, so some read negative). Hovl's sword (2,908 triangles) is
the heaviest mesh: Sword Storm's eight carry most of its 28k triangles.

| still | cue | power | draws | shapes | triangles | sparks | compute groups | GPU ms (two runs) |
|---|---|---|---:|---:|---:|---:|---:|---:|
| [sword-slash](sword-slash.jpg) | SwordSlash | Plain | 3 | 2 | 1072 | 8 | 1 | -0.008 / 0.061 |
| [sword-slash-on-beat](sword-slash-on-beat.jpg) | SwordSlash | OnBeat | 3 | 2 | 1082 | 13 | 1 | 0.036 / 0.023 |
| [sword-spin](sword-spin.jpg) | SwordSpin | OnBeat | 3 | 2 | 1128 | 36 | 6 | 0.079 / 0.072 |
| [spectral-slash](spectral-slash.jpg) | SpectralSlash | Plain | 1 | 1 | 528 | 0 | 0 | 0.004 / 0.013 |
| [tempest](tempest.jpg) | Tempest | Special | 1 | 1 | 528 | 0 | 0 | -0.006 / -0.005 |
| [orbit-blades](orbit-blades.jpg) | Orbit | Plain | 2 | 6 | - | 0 | 0 | 0.038 / 0.018 |
| [blade-halo](blade-halo.jpg) | Orbit | Special | 2 | 12 | - | 0 | 0 | 0.073 / 0.061 |
| [blade-rain](blade-rain.jpg) | BladeRain | Plain | 3 | 2 | 3464 | 14 | 1 | 0.013 / 0.060 |
| [sword-storm](sword-storm.jpg) | SwordStorm | Special | 4 | 17 | 28336 | 160 | 8 | 0.033 / 0.075 |
| [crescent](crescent.jpg) | Crescent | Plain | 1 | 1 | 528 | 0 | 0 | 0.020 / 0.024 |
| [full-moon](full-moon.jpg) | FullMoon | Special | 1 | 1 | 1056 | 0 | 0 | 0.140 / 0.071 |
| [phantom](phantom.jpg) | Phantom | Plain | 2 | 2 | 576 | 0 | 0 | 0.008 / 0.018 |
| [legion](legion.jpg) | Legion | Special | 2 | 6 | 1728 | 0 | 0 | 0.030 / 0.041 |
| [dash](dash.jpg) | Dash | Plain | 1 | 2 | 96 | 0 | 0 | 0.039 / -0.007 |
| [dash-on-beat](dash-on-beat.jpg) | Dash | OnBeat | 1 | 2 | 96 | 0 | 0 | 0.023 / 0.050 |
| [blade-rush](blade-rush.jpg) | BladeRush | OnBeat | 3 | 3 | 348 | 30 | 3 | 0.011 / 0.030 |
| [thousand-cuts](thousand-cuts.jpg) | ThousandCuts | Special | 2 | 6 | 608 | 0 | 0 | 0.020 / 0.021 |
| [leap-slam](leap-slam.jpg) | LeapSlam | OnBeat | 3 | 2 | 1184 | 64 | 8 | 0.019 / 0.033 |
| [leap-ring](leap-ring.jpg) | LeapRing | Plain | 1 | 1 | 528 | 0 | 0 | 0.023 / 0.049 |
| [skyfall](skyfall.jpg) | Skyfall | Special | 3 | 2 | 1416 | 180 | 12 | 0.088 / 0.061 |
| [shield-charge](shield-charge.jpg) | ShieldCharge | Plain | 2 | 1 | 592 | 32 | 8 | 0.002 / -0.020 |
| [shield-bash](shield-bash.jpg) | ShieldBash | Plain | 2 | 1 | 548 | 10 | 1 | 0.038 / 0.005 |
| [juggernaut](juggernaut.jpg) | Juggernaut | Special | 2 | 1 | 816 | 144 | 12 | -0.024 / 0.019 |
| [blink-burst](blink-burst.jpg) | BlinkBurst | OnBeat | 3 | 6 | 980 | 26 | 1 | 0.082 / 0.072 |
| [phase-walk](phase-walk.jpg) | PhaseWalk | Special | 3 | 6 | 508 | 30 | 1 | 0.017 / 0.033 |
| [momentum-trail](momentum-trail.jpg) | MomentumTrail | Plain | 1 | 1 | 48 | 0 | 0 | 0.037 / 0.029 |
| [afterimage](afterimage.jpg) | Afterimage | Plain | 1 | 2 | 96 | 0 | 0 | -0.002 / 0.021 |
| [shatterburst](shatterburst.jpg) | Shatterburst | Plain | 2 | 4 | 344 | 12 | 1 | 0.056 / 0.052 |
| [encore](encore.jpg) | Encore | OnBeat | 2 | 1 | 560 | 16 | 1 | -0.004 / 0.068 |

The cost of a busy frame is one draw per Hovl mesh in use (at most five) plus one for every spark, however many
effects are up: shapes are instanced records written once when their cue fires, and the shaders animate them.
