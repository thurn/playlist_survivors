# PX5: client playback and the pixel pipeline (2026-10-29)

The song's timeline played by the 2D client on open ground: the song clock, the sprite crowd and
the hero, the pixel pipeline and the camera. Every run was on the operator's Mac, windowless
(batch mode, Metal), from the worktree's root:

```
uv run --project demo3d/tools demo2d setup                 # vendor packs, the song
uv run --project demo3d/tools demo2d unity BakeSprites     # the atlases (the build bakes them if missing)
uv run --project demo3d/tools demo2d build                 # bakes PlaybackData, then the IL2CPP player
uv run --project demo3d/tools demo2d test                  # 78 of 78, 4 of them PX5's
APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
"$APP" -batchmode -force-metal -logFile "$PWD/demo2d/Logs/px5.log" \
  --capture-stills "$PWD/demo2d/evidence/px5" --stills-at 8,116,172.5
```

## The stills

1920×1080, as the IL2CPP player draws them (the pipeline's output, before PX7's bloom and grade),
with the player's own counts (monsters inside the framing's view):

- [`still-008.000.jpg`](still-008.000.jpg): the intro, 3 on screen (+1 dying), 30 m view (2×
  upscale, 2.02× with an accent's punch). The hero in his warm light among the first skeletons.
- [`still-116.000.jpg`](still-116.000.jpg): the build, 190 on screen (+13 dying) at 34 m (1.76×).
  Struck monsters around the hero flash white; the pale werewolves are the elites.
- [`still-172.500.jpg`](still-172.500.jpg): the final peak, 374 on screen (+30 dying) at 40.5 m
  (the 1.35× zoom-out, a 1.48× upscale).

The IL2CPP player's timeline has 3,817 entities where the Mono player's had 3,803: the sim is
deterministic per runtime, not across them (choreography.md, "Everything is deterministic"), so
every comparison below is within one build.

The hero is hard to find in the climax's crowd, as PX3's readability row predicted; PX7's and
PX9's lighting own that.

## The zoom-out: a larger target

[`zoom-pair.png`](zoom-pair.png) crops the same 480×270 screen pixels of the climax at 172.5 s
from both modes (`--zoom target`, left; `--zoom soft`, right), enlarged 2× nearest-neighbour:

- **target** (chosen, the default): the pixel target grows with the view, always 32 texels per
  metre: sized once for the song's widest view, 1296×729 plus a 16-texel margin each side
  (1328×761). Every sprite and tile texel survives, as a 1.48-pixel square.
- **soft**: a fixed 960×540 target drawing 40.5 m, so 23.7 texels per metre: the 32 px/m art is
  point-resampled before the upscale, dropping and doubling texels (uneven outlines, lost eyes
  and highlights).

## A seek shows the same horde

The stills are functions of the song time alone. Measured on the IL2CPP player (and the same on
Mono), decoded pixel by pixel:

| Run | Stills | Pixels differing from the run from 0 |
|---|---|---|
| from 0, `--stills-at 8,116,172.5` (the stills above) | the reference | |
| `--start-at 100` | 116, 172.5 | 0, 0 |
| `--start-at 116` (the still is the first frame) | 116 | 0 |
| `--start-at 172.5` | 172.5 | 0 |

`PlaybackTests.ASeekDrawsTheSameStillAsARunFromTheStart` checks the same in the editor: a still at
116 s after every 1/60 s frame from 0, after a seek back from 150 s, and from a fresh playback,
are identical. Two causes of difference turned up and were fixed on the way: sprites whose feet
share a texel row tied in the 2D renderer's sort and fell back on its registration order, which
pooling changes (now each frame ranks every sprite by row, then entity, into its sorting order);
and a still drawn straight after a seek lit the hero's light from its old place, because a
`Light2D` refreshes its culling bounds in its own LateUpdate (now the still is drawn in
DemoMain's LateUpdate, ordered after every other script's).

## On the click

`PlaybackTests.EveryBlowFlinchAndDeathShowsItsImpactFrameOnItsBeat` evaluates every attack, hit
and death in the song at its impact time: each shows its clip's impact frame (the blow, the
flinch's or the fall's onset), except where a later clip of the same entity has already cut in
(under 5%), and a killing blow shows the death rather than the flinch. Steps land on beats in the
timeline itself (the core's gait segments start on the beat grid); the walk's frames follow the
distance walked. Whether it all *feels* on the click is the operator's check (docs/tech-demo-2d/README.md, Pending on the operator).
