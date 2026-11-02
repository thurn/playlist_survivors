# The outline hull as a seam of its clump (ps-wnj.31, 2026-11-02)

The crystal cut's last `swarm_separation_min` breaks after ps-wnj.30
([body-tiles](../body-tiles/README.md)): edge 4 s 12.0, 50.2 s 9.2, 52 s
11.9; solid 50.2 s 12.0; hologram 36 s 12.5. Two were the metric
mis-measuring, and one preset's look needed a wider hull.

## What was wrong

- **The hull fell out of its own clump.** A hue's clump is its lit pixels
  (C*ab >= 30, L* >= 25) with gaps of up to 2 px closed. The outline hull a
  body draws on its rim (ps-wnj.28, 3.5-4 px at 1080 rows, 2 px at the
  metric's 960) is dark, so where it ran wider than 2 px its pixels, the
  deepest seams, left the clump and the percentile. It also took the tile's
  coverage under half, so the parted tiles left the sample and the median
  was taken over the flattest overlaps that stayed dense. A wider hull
  scored lower. With `--monster-tune body.OutlinePx=6`, solid's 28 s fell
  from 14.6 to 12.7 and its dense tiles from 35 to 12; hologram at 7 px
  kept 2 of 18.
- **A single dense tile decided a still.** A 32 px tile is shorter than
  one fodder (37-55 px), so one to three dense tiles are scattered pairs of
  overlapping bodies, not a clump. Edge's 4 s and 50.2 s broke on one tile
  each, its 52 s on three.

Hypotheses tried and rejected (numbers in the bead's comments):

- Tiles sized by perspective, from the bodies standing under each tile
  rather than at the hero's feet. 50.2 s's bottom-strip bodies stand 58 px
  against 45. The breaks moved both ways: edge 50.2 s passed, solid 47.2 s
  and hologram 50.2 s broke.
- Leaving the frame's 5 px border out of the seam statistics. It moved
  nothing.
- A 4 px closing everywhere. Gaps between v2's flat bodies became seams,
  and its 44 s orange blob passed.
- Look levers measured under the old clump: solid's falloff band, facets
  and key light, and edge's line gain and width, moved nothing. Hologram
  line gain 4.2 passed, but it isn't needed now.

## What changed

- **look.py**: with an ID render, gaps of up to 4 px *inside the monsters'
  silhouettes* close too (`SWARM_HULL`, `enemy_clumps`), so the hull counts
  as the seam it is. Gaps between bodies, which lie outside the render,
  close as before. Video frames have no render, so they measure as before.
- **neon-look-thresholds.json**: `swarm_separation_min` gains
  `unless_below: {dense_tiles: 4}`.
- **MonsterGem.shader**: the gem's hull is 5.5 px (was 4). Under the new
  clump, prism's 50.2 s measured 11.9 on four tiles round the hero's pink
  elites. Its hull sits a quarter radius behind the body (ps-wnj.28), so it
  parts overlaps less than the other looks' hulls do. At 5.5 px the tiles
  measure 15.6. Pushing the hull forward broke the key and bright share.

## Stills and metrics

The IL2CPP Mac player of this change on staging 786d2332 captured the
stills, with ps-wnj.19's recipe:

```sh
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --look PRESET --capture-stills "$PWD/DIR" \
  --stills-format png --capture-mask --stills-at 1.6,4,12,20,28,36,44,47.2,50.2,52,55.95,56.3 --seconds 57
uv run --project demo3d/tools demo3d look-metrics DIR --palette PALETTE.json --preset PRESET \
  --impact still-055.950 --out metrics-PRESET
```

`metrics-PRESET/` holds every rule on every still. It also has the swarm
overlay of each still that broke before, plus the two stills that pass only
because they have fewer than four dense tiles.

**Every rule passes in every still of every preset.** Separation is over
the steady stills; dense tiles are summed over all twelve. "Before" is the
same stills measured by staging's look.py.

| preset | separation before (breaks) | after | dense tiles before | after |
| --- | --- | --- | --- | --- |
| edge | 9.2-17.9 (4, 50.2, 52 s) | 12.7-20.1 | 43 | 112 |
| solid | 12.0-17.4 (50.2 s) | 14.4-19.5 | 370 | 541 |
| hologram | 12.5-19.5 (36 s) | 15.4-19.3 | 90 | 288 |
| prism | 13.3-13.8 | 12.1-15.6 | 13 | 16 |

Two stills judge on one dense tile and pass only by the new exemption:
edge 12 s (12.7) and prism 52 s (12.1). No other still is under 13.

**v2 still fails** ([v2-masked](v2-masked/metrics.md), the fifteen v2
moments re-rendered with ID renders by the Oct 31 v2 player, measured by
this look.py). The blob stills still break: 20 s 11.9, 28 s 9.9, 32 s 8.8,
56 s 2.3. 24 s still passes at 16.2. Every v2 still has at least 7 dense
tiles, so the exemption never applies. v2's video frames measure exactly as
before.

**The game still passes**
([game/evidence/look/hull-seams](../../../../game/evidence/look/hull-seams)).
These are ps-wnj.30's game stills, re-captured with this change (`game look
ps31-PRESET --at 20,30,40,50,58,70 --readability`, the same player
arguments). Every still in every preset passes `swarm_separation_min` and
`monster_form_min`: edge 15.7-33.2, solid 17.4-29.7, hologram 15.9-23.6,
prism 13.7-27.5. Prism's 13.7 is its 58 s, which has two dense tiles. The
only game break is hologram's background light at 58 s (0.162), which was
already there (ps-wnj.30).
