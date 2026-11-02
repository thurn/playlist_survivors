# The crystal cut in tiles at the bodies' size (ps-wnj.30, 2026-11-02)

The crystal cut's twelve stills in every preset, re-measured after look.py's
swarm and form tiles began following the bodies' height on screen (the
change and the game's numbers:
[game/evidence/look/body-tiles](../../../../game/evidence/look/body-tiles/README.md)).
IL2CPP Mac player of this change on staging a610bf6f, ps-wnj.19's recipe:

```sh
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --look PRESET --capture-stills "$PWD/DIR" \
  --stills-format png --capture-mask --stills-at 1.6,4,12,20,28,36,44,47.2,50.2,52,55.95,56.3 --seconds 57
uv run --project demo3d/tools demo3d look-metrics DIR --palette PALETTE.json --preset PRESET \
  --impact still-055.950 --out metrics-PRESET
```

(palettes: edge `ion`, solid `arcade`, hologram `phosphor`, prism
`spectrum`.) `metrics-PRESET/`: every rule on every still (metrics.md,
metrics.json, with `body px` and `tile`), and the swarm overlay of each
steady still that breaks separation. Before is the same stills measured by
staging's look.py.

**The cut keeps its calibration.** Its fodder stand 37-55 px (the metric's
45 px reference is their median), so its tiles are 32 px in eight stills and
33-39 px in the closer ones (44, 47.2, 55.95 and 56.3 s); the duel's two
elites (1.6 s) stand 133 px and take 95 px tiles, where they are no clump
(solid broke there at 10.7 before). Speckle filling moves the rest by
0-0.6.

Steady stills breaking `swarm_separation_min` (55.95 s is the impact frame):

| preset | before | after | dense tiles before / after |
| --- | --- | --- | --- |
| edge | 2 (4 s 11.6, 52 s 11.5) | 3 (4 s 12.0, 50.2 s 9.2, 52 s 11.9) | 57 / 43 |
| solid | 1 (the duel, 10.7) | 1 (50.2 s 12.0) | 377 / 366 |
| hologram | 2 (36 s 12.5, 56.3 s 11.9) | 1 (36 s 12.5) | 64 / 60 |
| prism | none (13.0-13.2) | none (13.3-13.8) | 13 / 13 |

50.2 s is new in edge and solid: its tiles now reach the frame's bottom 28
rows, which were never tiled, and the clumps of purple and magenta fodder
there score 8.7-10.5 (edge's one dense tile, solid's three of seven). Those are
clumps of the cut's own small bodies, which the metric measured as before;
4 and 52 s (edge) and 36 s (hologram) are ps-wnj.28's open breaks, unchanged.
Every other rule passes in every still of every preset.
