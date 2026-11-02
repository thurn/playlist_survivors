# Clumps read as individuals: swarm separation on the crystal cut (ps-wnj.28, 2026-11-01)

The crystal cut's twelve stills in every preset, before and after the line
looks' outline hulls ([neon-look.md, How clumps part in the line looks](../../../../docs/design/neon-look.md#the-axes);
the rule: [neon-look-style.md, Rules and Thresholds](../../../../docs/design/neon-look-style.md#rules-and-thresholds),
`swarm_separation_min` 13). Before is staging at d8dd782d, ps-wnj.19's
[trailer evidence](../trailer/README.md), whose recipe this repeats on an
IL2CPP Mac player of this change:

```sh
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --look PRESET --capture-stills "$PWD/DIR" \
  --stills-format png --capture-mask --stills-at 1.6,4,12,20,28,36,44,47.2,50.2,52,55.95,56.3 --seconds 57
uv run --project demo3d/tools demo3d look-metrics DIR --palette PALETTE.json --preset PRESET \
  --impact still-055.950 --out metrics-PRESET
```

(palettes: edge `ion`, solid `arcade`, hologram `phosphor`, prism `spectrum`.)
`--monster-tune` tuned it without rebuilds; the shard shaders lack the
outline's keys, so the hull's are `body.OutlinePx=4,body.OutlineBack=0`.

## Files

- `clumps-PRESET.jpg`: the same crops of three stills before (left) and
  after (right), at the capture's 1920 px: the densest clumps of each preset.
- `sheet-PRESET.jpg`: the twelve stills after, beside ps-wnj.19's
  `../trailer/sheet-PRESET.jpg` before.
- `metrics-PRESET/`: every rule on every still after (metrics.md,
  metrics.json), and the swarm overlay of each steady still that still
  breaks the rule (dense tiles labelled with their contrast, red under 13).

## Separation, before and after

Steady stills breaking `swarm_separation_min` (55.95 s, the finale's breath,
is the impact frame; it isn't judged):

| preset | before | after |
| --- | --- | --- |
| edge | 10 of 11 (8.1-12.1) | 2 (44 s 10.4, 52 s 11.5) |
| solid | 3 (10.7-11.7) | 3, unchanged |
| hologram | 7 (7.9-12.0) | 1 (20 s 12.4) |
| prism | 5 (9.8-12.0) | 2 (44 s 10.5, 56.3 s 11.5) |

- **Edge**: before, the magenta and orange types under ion filled their
  bodies with line light and touching rims merged (`clumps-edge.jpg`, 36 s:
  a pink mass); after, each body has a dark seam inside its silhouette and
  its lit lines show inside a dark core.
- **Hologram**: the amber horde at 36 and 56.3 s parts into wire bodies
  with dark gaps; the green troop at 28 s keeps its rings.
- **Prism**: the gem keeps its glow and the hull darkens the neighbour
  behind; the pink horde at 4 s goes from 12.0 to 14.0.

## What tried, and why it shipped as it did

1. **An outline for the line looks.** Solid's inverted hull, moved into
   `MonsterOutline.hlsl`, on edge, hologram and gem bodies. At 2 px it
   moved edge by 1-2; at 4 px by 3-5 (10 breaks to 3), but halved edge's
   bright share (4 s: 7.1% to 3.0%).
2. **Why the hull worked: it took the rim.** At the silhouette the hull's
   pushed-out back faces meet the body's front faces at one depth and cover
   its outer rim. Pushed back a quarter of the body's radius along the view
   ray (`_OutlineBack` 0.25) the rims came back and so did the blob: edge
   broke separation in 8-10 stills. The seam that parts a clump is inside
   each body's own silhouette. Edge and hologram draw the hull at the body's
   depth (0); the gem, whose light is its body, not a rim, pushes it back
   0.25 (at 0 it lost half its light: bright share in five stills).
3. **Winning the light back.** Brighter edge lines (4.0, with the 4 px
   hull) passed every still's separation but lifted the coarse cyan types more than the finely
   cut magenta ones, so the key fell (4 s 0.60 to 0.51, 36 s 0.74 to 0.52,
   the cut's median 0.74 to 0.59, under 0.62). A brighter rim gives the
   magenta types their light back: the rim is 6.5 on a body under 60 px in
   radius (`_RimSmall`; the cut's horde), easing to 2.0 by 90 px (the game's
   bodies: a rim of 5.5 and core of 0.3 for every body took the knight's
   margin over the amber swarm at 40 s from 5.8 to 4.7, and a 6.5 rim alone
   left it at 5.0). Hologram's lines 3.6 and rim 4 (were 3.0, 2.0), its
   shards' 2.2 and 1.2 (were 1.8, 0.6), scaled alike: with the shards left
   dim, separation broke at 20 and 44 s.
4. **Tried and dropped**: fading fine triangles' lines further (edge's
   35% floor to 0 or 15%: bodies went dark, readability broke in 3-10
   stills); a 5 px hull (fewer dense tiles, and the hardest remained);
   wider, dimmer hologram lines (no conformance back); the gem's far glow
   lower (conformance and bright share broke at 28 s); solid's outline 4.5
   px, a wider silhouette band, more faceting or top light (no still moved
   past 13).

## Still breaking, and why

- **Edge 44 s (10.4)**: four tiles of the round violet type, whose bodies
  overlap more than they touch; they read as rings by eye
  (`clumps-edge.jpg`, bottom). **52 s (11.5)**: four tiles of the blue troop
  under the slam's shards.
- **Solid** (unchanged by this bead): the duel at 1.6 s (three elites alone:
  one elite's body fills a tile, no clump), one tile at 12 s, and the violet
  troop at 28 s (11.0), which reads as outlined individuals by eye
  (`clumps-solid.jpg`, top): solid's bodies are flat, and only their 3.5 px
  outline is a valley at the metric's 960 px.
- **Hologram 20 s (12.4)**: shot 3's three families in one frame, six tiles.
- **Prism 44 s (10.5)** on two tiles, **56.3 s (11.5)**: the finale's pink
  horde, whose far bodies keep the gem's glow (`GemFar`) and touch.

## The other rules

Every other rule passes as it did, except:

- **Edge's key at 4 s**: 0.532 (was 0.60; shot 2's magenta and cyan troops
  in nearly equal shares, and the hull takes more of the magenta type's
  light). The cut's median holds (0.726, was 0.737).
- **Hologram's palette conformance** at 28 s (0.846, was 0.877), 36 s (0.83,
  0.86) and 56.3 s (0.794, 0.853): the off-palette light is the lines' own
  brightest pixels drifting in hue through the post (green toward
  yellow-green, amber toward yellow); the hull takes dim on-palette rim
  pixels, so their share grows. Hologram's key improved: 36 s now passes
  and the cut's median passes (0.630, was 0.616).

Still breaking from before, unchanged: bright share in the duel (every
preset), the key in shot 3 and a few others, solid's bright share at 28 s,
prism's floor p99 at 12 and 36 s (ps-wnj.29).

## The game

The looks are shared: [game/evidence/look/separation](../../../../game/evidence/look/separation/README.md).
