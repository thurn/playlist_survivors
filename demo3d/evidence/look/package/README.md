# The look package skeleton (ps-wnj.3)

`unity-packages/com.playlistsurvivors.look/` in both players, with no visual
change yet ([neon-look.md](../../../../docs/design/neon-look.md), The Package).

## `--look edge` headless in both players

IL2CPP Mac players built from this change.

```sh
PlaylistSurvivors.app/Contents/MacOS/Playlist\ Survivors -batchmode --headless --seconds 3 --look edge
```

```text
Look: look edge, palette electric; monsters dark-core-edges, structures edge-glass, world void-grid, post neon-bloom, vfx light-streaks, hero edge; motion springs, swarm, trails
Look: not built yet, so today's look: monsters dark-core-edges, structures edge-glass, world void-grid, post neon-bloom, vfx light-streaks, hero edge, motion springs, motion swarm, motion trails
Boot: quitting, --seconds 3 reached
```

`--look neon` quits with 1 at boot:
`Boot: --look: no preset neon (have: edge, hologram, prism, solid)`.

```sh
PlaylistSurvivors3D.app/Contents/MacOS/Playlist\ Survivors\ 3D\ Demo -batchmode -force-metal \
  --trailer crystal --fixed-time --capture-stills DIR --stills-at 2.0,9.0,16.0 --stills-format png --look edge
```

logs the same two `Look:` lines. With `--trailer` (the action cut) it logs
`DemoMain: the look flags are for the crystal cut; the action cut keeps its look`.

## The cuts' stills are unchanged

Stills at 2, 9 and 16 s of each cut: before from the staging build
(34950f68, whose inputs matched its tree), after from this change's build
with `--look edge`. Per-pixel RGB differences (0-255):

| Cut, still | before vs after: max, pixels differing | before vs a second before run |
| --- | --- | --- |
| action 2 s | 3, 15406 | 4, 2195 |
| action 9 s | 7, 1389 | 2, 558 |
| action 16 s | 3, 3124 | 3, 2303 |
| crystal 2 s | 2, 2954 | 3, 1570 |
| crystal 9 s | 3, 539 | 3, 519 |
| crystal 16 s | 2, 149 | 228, 12777 |

The differences are within what two runs of the same build differ by: the
change draws nothing new.
