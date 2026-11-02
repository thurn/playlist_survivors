# The neon look's metrics, calibrated on v2 (ps-wnj.2, ps-wnj.21, 2026-11-01)

`look-metrics` measures a directory of stills against a palette and the
style guide's rules
([neon-look-style.md, Rules and Thresholds](../../../../docs/archive/design/neon-look-style.md#rules-and-thresholds))
and exits 1 when a still or the cut breaks one. This folder is its run on
fifteen frames of the v2 crystal cut, the capture viewers reviewed, and the
calibration of the guide's three provisional thresholds.

```sh
uv run --project demo3d/tools demo3d look-metrics STILLS --palette PALETTE.json [--preset NAME] [--impact GLOB] --out DIR
uv run --project demo3d/tools game look-metrics STILLS --palette PALETTE.json   # the same, for game stills
```

- `--thresholds` defaults to
  [docs/archive/design/neon-look-thresholds.json](../../../../docs/archive/design/neon-look-thresholds.json),
  keyed exactly as the guide's table (`floor_p50_max`, `key60_min`, ...). A
  key's suffix says min or max; per-preset values (`solid`, `prism`, ...) sit
  beside `value`.
- `--preset` picks a preset's values. Without it the palette's pairing does
  (ion edge, arcade solid, phosphor hologram, spectrum prism), and without
  that the loosest preset's value applies: the ceiling every preset meets.
- `--impact` names impact frames (globs over still names). They are judged by
  `clipped_impact_max` alone and left out of the cut's measures.
- A still's ID render and view json, when the player wrote them beside it
  (demo3d's `mask-T.png` and `view-T.json`, game's `still-T-mask.png`), give
  the floor, hero margin, monster form and readability. A video frame gets
  the frame-level rules, with `frame_p25_max` standing in for the floor.

## What is here

- [metrics.md](metrics.md) and [metrics.json](metrics.json): v2's video
  frames, `ffmpeg -ss NN -i captures/trailer-crystal-v2.silent.mp4
  -frames:v 1` at 1, 4, 8, ... 56 s. Each still's numbers, the cut's and the
  rules broken.
- [masked/](masked/metrics.md): the same fifteen moments re-rendered by the
  demo3d player built from the v2 tree, windowless, with their ID renders and
  view json:

  ```sh
  "demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
    -batchmode -force-metal -logFile captures/look-metrics/v2-masked/player.log \
    --trailer crystal --fixed-time --capture-stills captures/look-metrics/v2-masked \
    --stills-format png --capture-mask --stills-at 1,4,8,12,16,20,24,28,32,36,40,44,48,52,56 --seconds 57
  ```

  Its frame-level numbers match the video's within a few tenths, so it is v2;
  the mask rules come from it.
- `*-swarm.jpg`: each still with every dense clump tile outlined and labelled
  by its boundary contrast (dL*). Red is under `swarm_separation_min` (13),
  green at or over it.
- [v2-palette.json](v2-palette.json): v2's colours in the palette format,
  measured from its frames' bright hue peaks (v2 had no palette): nine type
  colours, the gold formations, white blade and sparks, the grey-blue ground.

## What the metrics flag in v2

Every frame breaks rules, and each viewer complaint shows in one:

- **"A bunch of unrelated stitched together parts."** No colour key: key60
  is 0.34-0.54 in 13 of 15 frames (the guide wants 0.55; GW3's stills
  0.56-0.83), and the cut's median is 0.46 against 0.62. The floor jumps
  between shots: the shots' floor p50 spans 24 L* (the guide allows 6).
- **"When the enemies clump up they look like a blob more than a swarm."**
  Swarm separation fails in 13 of 15 video frames and 12 of 15 masked ones.
  The worst are the masses viewers described: t28's cyan horde (1.8 on
  video), t56's finale wall (2.1), t32's climax (4.3), t20's drop (8.2).
- **"Something about the large number of low-poly entities makes it feel
  'flat'."** Monster form fails in every masked frame: a body's own L* span
  is 10-18 against 30 (20 for solid). Halos fail in 8 of 15: emitters are
  flat paint. The hero is dimmer than the monsters in 10 of 15 (margin -30 to
  +12 against +5).
- **The lit world**: the lava and finale shots light the floor (floor p50 up
  to 26, p90 up to 69, frame p25 up to 17), lifting the bright share to
  40-47% against 32%.

What passes: palette conformance (91-99% against v2's own colours: it checks
that a look keeps to its palette, not cohesion), families (1-4), clipping
(at most 1.6%) and readability (33-68 against 17: v2 monsters stand off
their ground; they just do not read as individuals or as forms).

## Calibration

The guide left `halo_min`, `monster_form_min` and `swarm_separation_min`
provisional, set from GW3 and v2 frames without masks. The guide's table and
the thresholds file now agree on these values.

### halo_min: 15, unchanged

The halo is the median L* in a 3-10 px ring round emitters (L* >= 70; ring
pixels under 70), less the frame's L* p25. v2 scores 4-26 (median 12);
GW3's stills 17-26.

- v2's dark-floor frames, where emitters are flat paint, fail: 4-12 (t04,
  t08, t16, t20, t24, t32, t40, t52).
- The v2 frames that pass (t01, t12, t28, t36, t44, t48, t56) do so because
  a lit pool, floor or fog lifts the ring. They break the floor rules instead.
- 17 would sit on GW3's lowest still with no margin, so 15 stays.

### monster_form_min: 30, solid 20

The guide's form is "a monster's L* spans at least 30". Measured over all
the monster pixels at once, p90 less p10 spans the crowd's types, whose L*
differ: v2 scored 35-84 that way, though its bodies are flat. So the measure
is per tile: in each 32 px tile that monster bodies (the ID render, a pixel
in from its edge) cover a quarter of, their L* p90 less p10; the tiles'
median. v2 scores 10-18, median 14.

- 30 for edge, hologram and prism is about twice v2. An edge-lit body (dark
  core, lit rim) spans 50 or more.
- solid rises from 18 to 20 so that every v2 frame fails it (v2's best is
  18.3, the finale). v2's bright crystal bodies already are a solid look, the
  one viewers called flat.
- Video frames have no ID render, so they skip the rule. An estimate from the
  enemy hues' clumps read 17-43 on v2 against the mask's 10-18: too far off
  to judge by.

### swarm_separation_min: 13, a contrast in L*

The guide asks for "boundary contrast between adjacent same-type monsters,
dL*, >= 10 and >= 1.5x v2's dense-clump median". ps-wnj.2's measure was a
share of edge pixels, which is not a contrast and cannot be stated that way,
so it is replaced:

1. A candidate monster pixel is coloured and lit (C*ab >= 30, L* >= 25),
   and its nearest chromatic palette role by HSV hue is an enemy (so the gem
   formations are no clump). Gaps up to 2 px are closed. With an ID render,
   the clump is kept to the monsters.
2. A 32 px tile (at 960 wide) is dense when one enemy's clump covers half
   of it.
3. **Seam depth**: L* is box-averaged over 3x3 (speckle is no seam), then
   each pixel's depth is how far it sits under the clump on both sides
   within 4 px, across rows or columns: the brightest pixel on its darker
   side, less its own L*. A dark gap or outline between two lit bodies is
   deep; so is a dark core between lit edges. A flat body or a smooth falloff
   is 0.
4. A tile's boundary contrast is the 90th percentile of its clump's seam
   depth. The still's `swarm_separation` is its dense tiles' median.

On v2, the dense-clump median (all tiles of all frames) is 8.4 with masks
and 4.5 on video frames, where shards and gore in enemy hues join the clumps.
1.5x the masked median is 12.5, so the threshold is 13. Against it:

- The blob frames fail with and without masks: t20 (8.2 video, 12.2
  masked), t28 (1.8, 9.4), t32 (4.3, 8.4), t56 (2.1, 3.0).
- v2's three least dense crowds pass: t08 (13.5), t24 (13.4, 15.7 masked)
  and, with masks, t36 (13.1).
- The unit tests' crowds: overlapping flat discs score 0, the same discs
  with a dark rim 40 (the solid look's outline), and a flat field with black
  specks on one pixel in twenty stays under 13.

Known limits:

- A large body of an enemy hue reads as a clump: a giant, a boss, or a
  structure in an enemy's hue on a video frame (the ID render removes the
  last). A flat giant failing is intended: it is the "flat" feedback.
- A clump that is mostly dark between lit edges (the edge and hologram looks)
  never covers half a tile, so it is not measured. It cannot read as a colour
  blob.
- Heavy single-pixel speckle (one pixel in ten, black on a bright field)
  still reads as seams; the 3x3 averaging handles sparser shards.

## Other measures, and why

- **Hues are HSV** for the key, the families and conformance, as the guide
  measures them (ps-wnj.2 used CIELAB hue and 30-degree peaks). Conformance
  judges pixels at L* >= 40 and C*ab >= 30 against every chromatic role
  (C*ab >= 15), zone tints included.
- **The floor** is the ground-plane pixels (each pixel's view ray met with
  the plane through the hero's feet), less the monsters and structures of the
  ID render and the hero's region, each dilated 2 px. ps-wnj.2's estimate
  (every pixel not emitting) is gone; `frame_p25` is the video fallback.
- **The hero's region** is a screen capsule from its feet to 1.8 m above
  them, 0.5 m wide: the ID render marks only monsters and structures. Its L*
  p90 is the hero's brightest parts while the hero covers a tenth of it.
- **Everything is measured at 960 px wide**, the guide's width, readability's
  3-10 px ring included, so a 1920 still and a 960 one score alike.
