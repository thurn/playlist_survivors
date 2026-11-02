# Clumps read as individuals: swarm separation on the crystal cut (ps-wnj.28, 2026-11-01)

The crystal cut's twelve stills in every preset, before and after the line
looks' outline hulls ([neon-look.md, How clumps part in the line looks](../../../../docs/archive/design/neon-look.md#the-axes);
the rule: [neon-look-style.md, Rules and Thresholds](../../../../docs/archive/design/neon-look-style.md#rules-and-thresholds),
`swarm_separation_min` 13). Before is staging at d3929ba9, where ps-wnj.29
left swarm separation the cut's only broken rule
([last-breaks](../trailer/last-breaks/README.md)); after, an IL2CPP Mac
player of this change, with ps-wnj.19's recipe:

```sh
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --look PRESET --capture-stills "$PWD/DIR" \
  --stills-format png --capture-mask --stills-at 1.6,4,12,20,28,36,44,47.2,50.2,52,55.95,56.3 --seconds 57
uv run --project demo3d/tools demo3d look-metrics DIR --palette PALETTE.json --preset PRESET \
  --impact still-055.950 --out metrics-PRESET
```

(palettes: edge `ion`, solid `arcade`, hologram `phosphor`, prism `spectrum`.)
`--monster-tune` tuned it without rebuilds; the shard shaders lack the
outline's keys, so the hull's take the body prefix
(`body.OutlinePx=4,body.OutlineBack=0`).

## Files

- `clumps-PRESET.jpg`: the same crops before (left) and after (right), at
  the capture's 1920 px, round the three stills' densest clumps. The
  before here is this player with the change's values undone by flags
  (edge `body.OutlinePx=0,body.RimSmall=2`; hologram
  `body.OutlinePx=0,body.Edge=3,body.Rim=2,shards.Edge=1.8,shards.Rim=0.6`;
  prism `body.OutlinePx=0,body.Rim=1.7,body.Shine=1.5`), so both sides
  have shot 2's new mix (4 and 12 s); from 20 s on it measures as d3929ba9
  does.
- `sheet-PRESET.jpg`: the twelve stills after, beside ps-wnj.29's
  `../trailer/last-breaks` sheets before.
- `metrics-PRESET/`: every rule on every still after (metrics.md,
  metrics.json), and the swarm overlay of each steady still that still
  breaks the rule (dense tiles labelled with their contrast, red under 13).

## Separation, before and after

Steady stills breaking `swarm_separation_min` (55.95 s, the finale's breath,
is the impact frame; it isn't judged):

| preset | before | after |
| --- | --- | --- |
| edge | 8 (9.2-12.4) | 2 (4 s 11.6 on one tile, 52 s 11.5) |
| solid | 1 (the duel, 10.7) | 1, unchanged |
| hologram | 6 (10.7-12.6) | 2 (36 s 12.5, 56.3 s 11.9) |
| prism | 2 (9.8, 12.0) | none (13.0-13.2) |

Every other rule passes in every still of every preset, after as before.

- **Edge**: before, a type's lines filled its body and touching rims merged:
  the blue demons at 20 s and the magenta goblins at 4 s are one mass
  (`clumps-edge.jpg`); after, each body has a dark seam inside its
  silhouette and its lit lines show over a dark core.
- **Hologram**: the amber horde at 56.3 s and the green troop at 36 s part
  into wire bodies with dark gaps.
- **Prism**: the gem keeps its glow and the hull darkens the neighbour
  behind; the ice-blue horde at 4 s goes from 11.4 to 13.0.

## What was tried, and why it shipped as it did

Tuned first on d8dd782d, before ps-wnj.29's mixes; the numbers in this
section are from there.

1. **An outline for the line looks.** Solid's inverted hull, moved into
   `MonsterOutline.hlsl`, on edge, hologram and gem bodies. At 2 px it
   moved edge by 1-2; at 4 px it took edge from 10 breaks to 3, but halved
   its bright share (4 s: 7.1% to 3.0%).
2. **Why the hull worked: it took the rim.** At the silhouette the hull's
   pushed-out back faces meet the body's front faces at one depth and cover
   its outer rim. Pushed back a quarter of the body's radius along the view
   ray (`_OutlineBack` 0.25) the rims came back and so did the blob: edge
   broke separation in 8-10 stills. The seam that parts a clump is inside
   each body's own silhouette. Edge and hologram draw the hull at the body's
   depth (0); the gem, whose light is its body, not a rim, pushes it back
   0.25 (at 0 it lost half its light: bright share broke in five stills).
3. **Winning the light back.** Brighter edge lines (4.0, with the 4 px
   hull) passed every still's separation but lifted the coarse cyan types
   more than the finely cut magenta ones, so the key fell (4 s 0.60 to
   0.51, 36 s 0.74 to 0.52, the cut's median 0.74 to 0.59, under 0.62). A
   brighter rim gives the magenta types their light back: the rim is 6.5 on
   a body under 60 px in radius (`_RimSmall`; the cut's horde), easing to
   2.0 by 90 px (the game's bodies: a rim of 5.5 and core of 0.3 for every
   body took the knight's margin over the amber swarm at 40 s from 5.8 to
   4.7, and a 6.5 rim alone left it at 5.0). Hologram's lines 3.6 and rim 4
   (were 3.0, 2.0), its shards' 2.2 and 1.2 (were 1.8, 0.6), scaled alike:
   with the shards left dim, separation broke at 20 and 44 s. On ps-wnj.29's
   tree the gem's rim 2.1 and shine 1.65 (were 1.7, 1.5) gave back the
   bright share its pushed hull took at 28 s (3.7% to 4.4%, as before).
4. **Shot 2's mix** (`trailer-crystal.json`): goblins 2, were 3. The
   finely cut goblins took 60% of shot 2's colour from a quarter of its
   mix, the accent family over the key; the hull brought them toward an
   even split with the cyan key family, and edge's key at 4 s fell to 0.53
   (under 0.55) with every 3.5 or 4 px hull tried (pushed back 0.05-0.1,
   rim or lines brighter; 3 px kept 0.55 but broke separation in four stills). With one goblin fewer the key family is the
   colour's majority, ps-wnj.29's rule for the mixes: 4 s keys at 0.61
   (solid 0.81, hologram 0.74, prism 0.58), and the trailer checks pass.
   With the old looks the same mix would key at 0.55 in edge.
5. **Tried and dropped**: fading fine triangles' lines further (edge's
   35% floor to 0 or 15%: bodies went dark, readability broke in 3-10
   stills); a 5 px hull (fewer dense tiles, and the hardest remained);
   wider, dimmer hologram lines (no conformance back); the gem's far glow
   lower (conformance and bright share broke at 28 s); solid's outline 4.5
   px, a wider silhouette band, more faceting or top light (no still moved
   past 13).

## Still breaking, and why

- **Edge 4 s (11.6)**: one tile of shot 2's goblins round the knight.
  **52 s (11.5)**: four tiles of the blue troop under the slam's shards.
- **Solid 1.6 s (10.7)**: the duel, three elites alone; one elite's body
  fills a tile, so the tile is no clump (unchanged by this bead).
- **Hologram 36 s (12.5)**: the green troop where it is thickest, 18
  tiles. **56.3 s (11.9)**: the finale's amber horde, 30 tiles.

## The game

The looks are shared: [game/evidence/look/separation](../../../../game/evidence/look/separation/README.md).
