# Crowd surfaces: per-swatch metal and gloss (prototype)

`--crowd-gloss` gives the default roster's swatches their own metallic and
smoothness ([crowd.md, Surfaces](../../../docs/tech-demo-3d/crowd.md#surfaces)):
armour, blades and buckles metallic, skin, slime and mushroom caps glossy,
cloth, fur and leather left at the old 0.2. The colours, from the baker's
`surfaces-report.md`, are in `Data/crowd-surfaces.json`. The question was
whether heroic's material sense (`--roster heroic`) survives on the low-poly
monsters, lit only by the scene's own lights: no matcap, no recolour.

Every pair is the same build and the same fixed-time frame, without the flag
on the left and with it on the right.

## Stills

- [close-100.jpg](close-100.jpg), [close-145.jpg](close-145.jpg):
  `--view-width 8 --no-effects` (heroic's close stills). The goblins'
  helmets, pauldrons and greaves read as steel, with a lit facet and a dark
  facet each. The skeletons' breastplates and helmets go nearly black at full
  metal, with bright edges where a face turns to the moon. Slime and mushroom
  caps gain a soft sheen; the demons' skin barely changes.
- [metal-sweep-skeletons.jpg](metal-sweep-skeletons.jpg),
  [metal-sweep-goblins.jpg](metal-sweep-goblins.jpg): off, then
  `--crowd-gloss-metal` 0.3, 0.5, 0.7 and 1 (the file's values). Below about
  0.7 the metal reads only as a slightly darker swatch; it only looks like
  metal at 1, where it also turns dark.
- [song-100.jpg](song-100.jpg): the song's own camera at 100 s. The
  skeletons darken and lose some of their tan mass; nothing else is visible
  at this distance.
- [song-175.jpg](song-175.jpg): the final peak, off, 0.7 and 1. The effects
  cover the difference completely.

## What it shows

- **The style takes it.** Flat facets make a metal plate either catch the
  moon or not, which reads as a crisp glint rather than a smear, and the
  goblins look better armoured without looking out of style.
- **Metal goes dark at night.** A metallic surface shows almost only what it
  reflects, and the crowd shader reflects the global probe, which is the dark
  night sky. So the armour is black except on the facets facing a light. The
  heroic set's metal is just as dark; it reads because its normal maps scatter
  small highlights over it. A reflection probe baked from the arena itself
  (the lit ground, the braziers) would give the metal something warm to show,
  still from the scene's own light.
- **It only matters up close.** At the song's camera the change is small, and
  at a climax the effects hide it.

## Commands

From the worktree's root, after `demo3d setup` and `demo3d build`:

```
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --view-width 8 --no-effects --fixed-time \
  --capture-stills "$PWD/captures/surfaces/close-on" --stills-at 60,100,145,175 --seconds 180 --crowd-gloss
"$APP" -batchmode -force-metal --fixed-time \
  --capture-stills "$PWD/captures/surfaces/song-on" --stills-at 60,100,145,175 --seconds 180 --crowd-gloss
```

Drop `--crowd-gloss` for the left halves; add `--crowd-gloss-metal <k>` for
the sweep. The pairs are `magick off.jpg on.jpg +append`.
