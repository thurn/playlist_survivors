# PX3: the sprite baker and clip library (2026-10-29)

The hero's and the 13 roster types' frames, baked onto the 960×540 grid, and the clip library
the choreography schedules with, measured from those frames. Every run was on the operator's
Mac, in batch mode, from the worktree's root:

```
uv run --project demo3d/tools demo2d setup                                         # vendor packs
uv run --project demo3d/tools demo2d unity BakeSprites                             # Assets/Baked/Characters (31 s)
uv run --project demo3d/tools demo2d unity AnnotateClips -out "$PWD/demo2d/evidence/px3"  # bake, Data/clip-annotations.json, contact sheets
uv run --project demo3d/tools demo2d unity SpriteStills -out "$PWD/demo2d/evidence/px3"   # lineup.png, crowd.png (needs the bake)
uv run --project demo3d/tools demo2d test                                          # 74 of 74, 6 of them PX3's
```

## The bake

`SpriteBaker`, per character, over every clip the library names (4 per monster, 6 for the hero):

- **Scale:** one per character, so its walk's first frame stands its `gameHeightPx`.
- **Anchor:** each clip has its own canvas, so each is anchored at the feet. The walk's first
  frame's feet (the bottom of its lowest solid row, the middle of its lowest eighth) are carried
  into the clip's first frame by sliding the walk's silhouette over it to the best overlap (a
  quarter-resolution search, then full). 43 of the 44 other clips matched (overlap 0.73 or more);
  the knight's shield bash starts shield-up (overlap 0.38, under the 0.45 cut) and uses its own
  feet, which its contact sheet shows on the ground line. Each entry's `anchor` records which.
  Motion the frames carry (a lunge, a fall) stays in them.
- **Downsample:** an exact area filter onto whole grid pixels placed around the feet, colour
  weighted by coverage, alpha snapped to 0 or 1 at half coverage, then trimmed.
- **Atlas:** each frame cropped to its own opaque pixels, shelf-packed (tallest first, a clear
  pixel between) into one point-filtered, uncompressed, mip-less texture per type, with
  `<type>.json` giving each clip's frame size and pivot and each frame's crop and offset.

| Type | Height (px) | Frames | Atlas |
|---|---|---|---|
| knight (hero) | 66 | 105 | 768×684 |
| skeleton, ghoul, zombie-warrior, flame-imp, ice-skull, mask | 56, 54, 58, 48, 56, 56 | 60 to 73 each | 448×360 to 640×648 |
| skeleton-mage, blood-mage (casters) | 56, 56 | 64 each | 448×460, 448×456 |
| ogre, cyclops, werewolf, lava-beast (elites) | 90, 92, 88, 90 | 62 to 78 each | 704×616 to 960×856 |
| boss-devil | 168 | 75 | 1856×1900 |

The 14 atlases are 7.6 MB of PNG in `Baked/` (gitignored). Per-clip boxes (before per-frame
crops) made them 2 to 4 times larger; the boss's was 4096×2048.

## The clip library

`Data/clip-annotations.json`, 59 entries, written by `ClipAnnotator` from a bake 4× finer than
the game's, with a contact sheet per type (`annotations-<type>.jpg`: twice game size, the boss
at its own; impact framed red, end blue, the feet's row as a faint line):

- **Strikes** (attacks, the hero's slashes, bash and spin): the impact at the first peak of
  motion (changed pixels between frames) within 85% of the clip's largest; the end after the last
  frame moving a quarter of that. At 70%, the boss's windup (frame 1) won over its slash (frame 7).
- **The slam** (`jump`): the impact on the frame the feet land after the apex (frame 10 of 20).
- **Hits and deaths:** the impact on the first large motion (frame 1 or 2 everywhere); the end once
  motion drops below a fifth (hits) or a tenth (deaths: the body at rest).
- **Walks:** the cycle (the last frame repeats the first in all 14) and the stride, the planted
  foot's slide over the cycle, from the shift that best matches the soles (the lowest 4% of the
  figure) frame to frame. 0.34 to 1.78 m. The lava beast (a lava blob) and the boss (it hovers)
  have no planted foot (0.01 and 0 m), so they get two of the catalog's gait steps (1.2 m). An
  earlier measure, the feet's widest spread, read tails, wings and weapons (0.23 to 6.7 m).
- **Root motion:** for the hero's combos, how far the feet travel forward from the first frame
  to the end: 0 for the slashes, bash and spin, 0.25 m for the slam.

**Clip choices** (PX4's placeholders otherwise): the skeleton attacks with `skill_3` (an
overhead swing in place) instead of `skill_1` (a lunge that ends lying in an explosion, 2 m from
its start); the skeleton mage casts with `skill_2` (staff raised) instead of `skill_1` (ends
collapsed on the ground). Every other attack is `skill_1`; all were reviewed from galleries of
each pack's skills.

The choreography's dotnet tests pass on the measured library (18 of 18).

## The stills

- `lineup.png`: every type walking at its game size on the forest ground at night (the global
  moonlight, a warm light by each), grunts and casters with the hero in front, elites and the
  boss behind. 2× nearest-neighbour, as the game shows the grid at 1080p.
- `crowd.png`: the hero (centre) among about 130 monsters (grunts and casters, 7 elites, the boss) at the base 30 m view,
  facing him, for readability.

**Readability:** each type is recognizable at its size by silhouette and colour (the white
mask, the pink imp, the bone-white skeletons, the elites' distinct palettes). The night grade
pulls the ghoul, zombie warrior, ice skull and blood mage toward the same dark teal; their
silhouettes still differ. The hero is findable in the crowd only because he stands in the
centre under a warm light: at 60 px he was hardly taller than the grunts, so he is now 66 px. His
purple and steel blend with the horde's palette, so PX7/PX9 should mark him with light (a rim
or a carried light) rather than size. This judgement is in the plan's Pending on the operator.

## Tests

`Demo2D.Tests.SpriteBakerTests`:

| Test | Checks |
|---|---|
| `DownsampleAveragesCoverageAndSnapsAlphaAtHalf` | coverage-weighted colour; under half coverage snaps clear and is trimmed |
| `TheFeetLandOnAGridPixelCornerWhereverTheyFallInTheCanvas` | the pivot sits on a grid corner at the feet |
| `EveryLibraryClipBakesToItsAnnotatedSizeAndPivotWithHardAlpha` | the library's frames, size and pivot are a fresh bake's; alpha only 0 or 1 |
| `EachTypeStandsItsGameHeight` | each walk's first frame is its game height (−2 to +1 px) |
| `TheAtlasHoldsEveryFrameWithoutOverlap` | every frame in the atlas with a gutter, no overlaps, each crop holding all its frame's pixels |
| `TheLibraryIsTheAnnotatorsProposalWithSaneTimings` | the tracked library equals a fresh annotation; impact ≤ end within the clip; strides 0.25 to 2.5 m; the core parses it |
