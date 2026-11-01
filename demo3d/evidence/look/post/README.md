# The look's post stack on the crystal cut (ps-wnj.6, 2026-11-01)

The crystal cut under each preset's post option
([neon-look.md, Post-processing stack](../../../../docs/design/neon-look.md#post-processing-stack)),
against the before: the environments' night volumes, shot grades and trailer
dressing, drawn by the same build with `--post today` (not a built option, so
today's post). Monsters, world and VFX are still today's for every preset: only
the post and the palette's colours in it differ.

```sh
# IL2CPP Mac player of this change, windowless, 2x supersampled as a trailer capture is
PlaylistSurvivors3D.app/Contents/MacOS/Playlist\ Survivors\ 3D\ Demo -batchmode -force-metal --fixed-time \
  --trailer crystal --capture-stills DIR --stills-format png --capture-mask \
  --stills-at 4,12,20,28,36,44,52 --seconds 53 --look edge       # solid, hologram, prism; before: --look edge --post today
uv run --project demo3d/tools demo3d look-metrics DIR --palette PALETTE.json --preset PRESET --out OUT
```

- `crystal-PRESET.jpg`: each moment's before above the preset's (neon-bloom,
  punchy-bloom, scanlines, prism-bloom).
- `metrics-PRESET.md` / `.json`: the style guide's rules on the seven stills
  with their ID renders and view json; `metrics-before.*` is the before
  measured against ion and edge's thresholds.
- `action-cut.json`: the action cut at 2, 9 and 16 s from this build against
  the main checkout's older build.

## Value structure: what the post owns

| rule | before | edge | solid | hologram | prism |
| --- | --- | --- | --- | --- | --- |
| floor p50 <= 8 | 1.4-11.5, 2 fail | 2.9-11.5, 2 fail | 3.2-9.4, 1 fail | 2.3-9.8, 2 fail | 2.1-10.2, 2 fail |
| floor p90 <= 30 | 14-51, 2 fail | 13-22 | 11-21 | 12-21 | 11-21 |
| black share >= 0.30 (solid 0.25) | 0.28-0.63, 1 fail | 0.16-0.49, 2 fail | 0.22-0.53, 1 fail | 0.20-0.51, 2 fail | 0.19-0.54, 2 fail |
| clipped <= 0.02, clip blob, white <= 0.02 | pass | pass | white 1 fail (0.023 at 36 s) | pass | pass |
| bright share max (0.22-0.32) | 4 fail | 4 fail | 3 fail | 4 fail | 3 fail |
| halo >= 15 | 4.9-20.3, 3 fail | 4.5-12.1, all fail | 1.8-9.1, all fail | 7.6-14.4, all fail | 5.4-12.1, all fail |
| shot floor range <= 6 | 10.1 | 8.6 | 6.2 | 7.5 | 8.1 |

- **The floor falls to the void.** The options' void toe sinks a lit floor
  while emitters over its knee keep their value: floor p90 drops from 51 to 22
  at worst, and the spread of the shots' floors from 10 to 6-9. The floors that
  still break p50 and the black share are the two lit shots at 20 and 36 s
  (the lava and finale environments' light pools), which the void world kit
  (ps-wnj.8) removes rather than the grade.
- **Halo and bright share are the monsters' coverage.** The stand-in crystal
  bodies are bright solid emitters filling 17-38% of a dense frame; the ring
  round them is mostly the floor between bodies, so a halo that lifts it 15 L*
  lifts the floor rule's pixels with it (tuning showed halo 18-22 only with
  floor p50 12-19). The before's halos pass only where its lit floor lifts the
  ring. Dark-core and wireframe monsters (ps-wnj.10, .12) put emitters on black,
  where the halo has room; ps-wnj.19 re-runs these rules on the finished looks.
- **Clipping and white stay earned**: nothing clips outside impacts.
- Rules outside the post (key, conformance, hero margin, form, separation) are
  the other axes'; they move little here, as expected.

## The action cut is unchanged

Per-pixel RGB differences (0-255) between this build and the older one: at
most 3, 6 and 4, no pixel over 8, within what two runs of one build differ by
(ps-wnj.3's [package evidence](../package/README.md)). The action cut keeps
`NightLook`, the shot grades and `TrailerLook`; `LensHitFeature` and
`LensDirt` moved into the package unchanged.

## Notes

- The first build switched URP to HDR grading at run time, which both players'
  builds strip from the uber shader: dark values came out black. The stack now
  grades in LDR as before, with its own pass before URP's post.
- Saturated reds in the densest crowds lean salmon: the stand-in bodies run far
  past white, so the filmic curve treats whole bodies as cores (`whiten` is
  0.25-0.3). The monster looks bring that down to edges and cores.
