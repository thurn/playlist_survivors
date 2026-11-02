# Hero at humanoid size (ps-b5z.1)

> **Superseded in part (ps-b5z.6).** The outline described here was removed on the operator's
> review ("no yellow glow around the hero please"); the hero stays at true size and the shots
> frame closer instead ([../views](../views/README.md)).

The operator's note on both trailers: the hero, drawn 1.7x by overdrive's
`heroScale`, made the humanoid monsters look small and harmless. The hero is
now drawn at their own size (1.85 m against the skeleton's 1.8 m and the
goblin's 1.5 m), and a thin glowing outline (`HeroOutline`, overdrive's
`heroOutline` 3 px) keeps them easy to find. The default cut's line is cyan
blue, the crystal cut's gold (its look's `heroOutline`). Spec:
[trailer.md, Overdrive](../../../../docs/archive/tech-demo-3d/trailer.md#overdrive-the-starting-point).

- `action-before-after.jpg`: the default cut (`--trailer`).
- `crystal-before-after.jpg`: the crystal cut (`--trailer crystal`).

Each row is one moment, 1.5, 8, 20, 33, 43, 47.5, 50.5 and 55 s, one or two
per shot. On the left is the old capture at 1.7x, from `captures/trailer.mp4`
and the crystal cut's capture of 2026-10-31. On the right is the new player.
Each half is the full frame and a 270-pixel crop round the hero. The crop is
placed from the new run's camera and hero position, which the scale doesn't
change, since the simulation never saw it.

Rendered windowless from one IL2CPP Mac build:

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal --trailer [crystal] --fixed-time \
  --capture-stills DIR --stills-at 1.5,8,20,33,43,47.5,50.5,55 --seconds 55.5
```

## What they show

- **Height.** In every crop the hero stands about as tall as the skeletons
  and goblins round them, not head and shoulders above the crowd.
- **Finding the hero.** At 1.0x with no outline (not shown), the hero was
  lost in most horde shots: the warm hero light paints the monsters nearest
  them the same orange. With the outline the hero is findable in every full
  frame. The hardest frame is 47.5 s in the default cut's snow, where the
  bloom from the kills whites out the centre of the frame.
- **Colour.** Each cut's line is the complement of its palette. Variants
  were tried with `--hero-outline px,r,g,b` on the 8, 20, 33 and 55 s stills:
  - White (2-3 px, 2.2-4 HDR) bloomed into the warm shots' hot centres and
    into the cyan crystals.
  - A black ink line read only on bright ground.
  - Gold read in the crystal cut but not on the default cut's orange.
  - Cyan read in the default cut but vanished into the crystal cut's cyan
    blocks.
- **The duel's close view** (20 m) shows a few inner lines where the
  vendor model's hard edges split the hull. The model's meshes aren't
  readable, so the hull can't use smoothed normals. At the horde shots' 45 m
  and wider views it is one clean line.
- **Gait and reach.** The walk's stride is the clip's own again (it was
  stretched 1.7x with the hero), so the feet plant as they do in a normal
  run. The sim's reach never scaled, so the hits land where they did. The
  slash trails (2.4 m arcs) now reach past the 1.15 m blade's tip, as they
  do in a normal run.
