# PX2: assets and their verification (2026-10-29)

The packs demo2d uses, synced from `demo2d/manifest.json`, and the checks that they work under
URP's 2D renderer on Metal and can't reach git (README.md, Verifying the Assets). Every run was
on the operator's Mac, in batch mode, from the worktree's root:

```
uv run --project demo3d/tools demo2d setup                     # syncs the manifest into Assets/Vendor
uv run --project demo3d/tools demo2d test                      # the checks: 68 of 68 pass
uv run --project demo3d/tools demo2d unity AssetStills -out "$PWD/demo2d/evidence/px2"
demo2d/evidence/px2/guard-drill.sh "$PWD" /tmp/guard-drill.txt # the drill, in a scratch clone
```

## The manifest

3,619 entries, which sync to about 7,400 files (205 MB) with their dependencies and `.meta`s:

| What | Source | Entries |
|---|---|---|
| knight_male and the 13 roster packs: every frame (`<prefix>_png/*.png`) and clip (`<prefix>/*.anim`) | `pixel_assets` | 3,476 |
| AncientForest: the sliced `MainLev` and `Decorative` sheets, the 7 swaying trees (sheets, clips, controllers) | `pixel_assets` | 23 |
| CFXR's runtime: its shaders and their includes, the shader importer and editor scripts, the runtime scripts | `pixel_assets` | 19 |
| CFXR effect candidates: 4 to 6 prefabs per effect kind (textures, materials and meshes follow) | `pixel_assets` | 37 |
| demo3d's sounds, the same 64 entries | `unity_assets` | 64 |

## The checks

`Demo2D.Tests.AssetTests`, in the EditMode run (68 of 68 pass, 45 of them PX2's):

| Check | Test | Result |
|---|---|---|
| Every clip of the hero and the 13 types imports as a complete sequence, at its rate | `EveryCharacterClipImportsAsACompleteSequenceAtFifteenFps` | 196 clips: frames 0 to n-1, every one imported, n = ⌈15·length⌉ + 1 |
| Every clip the clip library names has its frames | `EveryClipTheClipLibraryNamesHasItsFrames` | 59 of 59 |
| The forest sheets: 32 px tiles at 32 per unit, point filtered, uncompressed, no mips | `TheForestSheetsAreSlicedInto32PixelTiles…` (×2) | pass |
| Each transition set gives every corner pattern but the diagonals | `EveryTransitionSetHasEveryCornerPatternButTheDiagonals` | 14 sets |
| The tile test map renders without seams | `TheTileTestMapRendersExactlyAsItsTilesCompose` | 61×54 tiles: all 3.4 M pixels equal the CPU composite |
| The swaying trees' clips play | `TheSwayingTreesPlay` | 7 trees, 10 frames at 10 fps, looping, each controller plays its clip |
| Every CFXR candidate compiles and draws under the 2D renderer, lit and unlit | `EveryEffectCandidateDrawsUnderThe2DRendererLitAndUnlit` (×37) | no missing script, mesh or shader; every shader compiles and has a pass the renderer draws (`Universal2D`); each changes over 100 pixels, under the moon and with no light |
| The effect list names 2 or 3 of the pool's prefabs per kind | `TheEffectListNamesTwoOrThreeOfThePoolsCandidatesPerKind` | 8 kinds |

## The stills

All tagged renders, drawn at 32 grid pixels per metre.

- **`contact-<character>.jpg`**, one per character: a row per clip with every frame, labelled
  with its frame count and its `.anim`'s length; `*` marks the clips the clip library uses.
  Each clip is cropped to its opaque pixels, at one scale per character.
- **`tile-test-map.jpg`** (half size): the sheet's authored stamps (cliffs, plateaus, ground
  fills, bridges, cliffs over water) and a plus-shaped patch from each transition set,
  corner-autotiled, over base grass. **`tile-test-map-day.jpg`** and **`-night.jpg`**: a 960×540
  view of the transition sets' top-left corner (about six of them), as the textures are and
  under the moon with six point lights.
- **`trees.jpg`** (half size): the seven trees under the moon, in three rows a third of a
  cycle apart.
- **`cfxr-<kind>.jpg`**, one per effect kind, at the grid's 960×544: the pool's prefabs in
  columns; in rows, as authored at 0.08 and 0.25 s, then laid flat (their ground plane turned to
  face the camera) at the same times; over forest ground at night with a soft light under each,
  the knight at game height beside each.

## The effect candidates

In `demo2d/Assets/Demo2D/Data/effect-candidates.json`, best first, with reasons:

| Kind | Candidates |
|---|---|
| slash arc | Slash (Orange), Slash (Blue), Slash (Cross, Orange) |
| hit spark | Hit A (Red), Hit C, Hit B (Blue) |
| death burst | Smoke Poof, Explosion 1, Magic Poof |
| ring | Fire Ring (Blue), Fire Ring, Impact Glowing HDR (Blue) |
| bolt | Fireball + Fire Trail, Fireball Plain, Impact Glowing HDR (Blue, No Flare) |
| lightning | Lightning Strike + Impact, the same in purple, Electric Explosion |
| beam | Electric Barrier Simple (HDR), Fire (line), Fire Breath |
| boss slam | Big Impact HDR, Impact Glowing Cross HDR (Orange), Explosion 2 Bigger |

- **All draw best as authored.** The top-down camera sees CFXR's 3D effects from the side.
  Laid flat, nothing looked better, and the lightning strike lost its fall from the top of the
  screen.
- **Dropped from the pool**, so the manifest doesn't sync them:
  - Electric Surface 2, Fire Trail (stretched), Water Ripples (Arcs), Smoke Poof Circle Flat:
    they draw nothing to the 2D camera, in either orientation.
  - ScreenDistortion Ring: it draws a grey blur, not a distortion (the 2D renderer gives it no
    opaque texture to distort), so no distortion prefab is a candidate.
- **The slashes are spark bursts, not arcs**: their slivers are stretched billboards. That
  suits knight_male, whose two slash skills draw their own arcs.
- **CFXR has no true beam**: the beam's candidates are a jagged electric line and lines of
  flame, which PX7 has to aim and stretch along the sweep.
- **The effects are large at native scale:** a hit spark is about 2 m across (a grunt is 1.75 m
  tall). PX7 sets each one's scale.

## The vendor guard drill

`guard-drill.sh`, in a scratch clone of the PX2 branch: each case force-stages files
(`git add -f`), runs `demo2d guard --index` and resets. `guard-drill.txt` has every line.

- **89 cases, every one refused.** For each of the 14 character packs: a frame force-staged in
  `Vendor/`, the frame copied under `Demo2D/Art/`, its `.meta` alone, a clip as Unity rewrote
  it, and the source's binary clip under `docs/`. For AncientForest: `MainLev.png` in `Vendor/`,
  renamed under `docs/`, its `.meta` alone, a tree clip, and a tree controller under
  `unity-packages/`. For CFXR: a prefab, its `.meta` alone, the ubershader, a script, and a
  texture in `Vendor/`. For each of the 3 sound packs: a sound and its `.meta`. And a baked atlas
  in `Baked/`, under `Demo2D/Art/`, and untagged under `evidence/px2/`.
- **Both controls pass:** the clean index, and a tagged still copied under `evidence/px2/`.
- **The first run found a gap, now fixed.** Unity re-serializes the packs' binary Spine clips
  (`.anim`, Unity 5.6's binary format) as YAML text when it imports them into this project,
  which forces text serialization. The rewritten copies in `Vendor/` match no source file's
  hash, carry no GUID in their body and aren't binary, so all 14 copies under `Demo2D/Art/`
  passed the guard. The derived record now records every file in each demo's `Assets/Vendor/`
  as well as its `Assets/Baked/`, and the 14 cases fail on it
  (`derived: demo2d/Assets/Vendor/<pack>/<prefix>/death.anim`).
