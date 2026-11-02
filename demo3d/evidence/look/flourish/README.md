# The death flourish and the spawn warp in the crystal cut (ps-wnj.16, 2026-11-01)

The look's death flourish and spawn warp
([neon-look.md, VFX](../../../../docs/design/neon-look.md#vfx)) in demo3d's
crystal cut, with their sounds. `EffectDriver` draws them under the look's vfx
option (the selection's `client.deathPop`, `deathRing` and `spawnWarp`); the
action cut and `--vfx classic` draw neither, so each `classic` strip is the
same shot without them (and with the vendor slashes).

- **Death**: at each crystal death's break, on the blow's frame, a soft pop on
  the body where the blow lands, and a ring of light on the ground; one
  instant's breaks within 3 m share one ring (`Flourishes.Rings`), so a mass
  kill is a few larger rings.
- **Spawn**: every monster spawning after the shot's cut inside the camera's
  ground footprint (`CameraGeometry.GroundFootprint` round the framing's focus
  at its spawn, turned by the shot's heading; `LookCrowd.Warps`) gets the
  collapsing rings and the glow. **The crystal cut has none**: the choreography
  spawns every monster outside the view (its spawn margin and band) and walks
  it in, and a cut opens on the monsters already there, so `--shatter-log`
  (which now logs each warp too) lists 0 warps over the whole cut. The warp
  shows in the game, where set pieces, swarms and boss entries spawn in view
  ([game/evidence/look/flourish](../../../../game/evidence/look/flourish/README.md));
  a re-staged shot that spawns in view (ps-wnj.18) gets it with no change here.
- **Sounds** (`FlourishSounds`, in the cue list beside the shatter's, so the
  live mix and the offline mix agree): a pluck at each break's pop, a swell
  for each warp started 0.12 s early so its attack lands on the spawn, at most
  three voices per beat each. The clips are the free samples game uses
  (`demo3d/Assets/FreeSounds/`, cloned by setup).

Windowless fixed-time Mono Mac player, on jarvis, `--trailer crystal --look edge`.
From the worktree's root:

```sh
for V in light-streaks classic; do
  uv run --project demo3d/tools demo3d filmstrip death-$V --at 2.40 --frames 12 --columns 4 --crop 394,247,720,600 \
    --backend mono --evidence look/flourish --player-args "--trailer crystal --look edge --vfx $V"
  uv run --project demo3d/tools demo3d filmstrip masskill-$V --at 7.80 --frames 24 --columns 6 \
    --backend mono --evidence look/flourish --player-args "--trailer crystal --look edge --vfx $V"
done
uv run --project demo3d/tools demo3d trailer --stand-in --backend mono --out captures/flourish-standin.mp4 -- --trailer crystal --look edge
uv run --project demo3d/tools demo3d trailer --stand-in --backend mono --out captures/flourish-standin-classic.mp4 -- --trailer crystal --look edge --vfx classic
```

## The strips

- `death-V.jpg` (2.40 s on, cropped round the duel): the opening duel's troll
  breaks at 2.417 s (song 123.817, `--shatter-log`). Under `light-streaks` a
  blue pop on the body and its ring open on that frame and race out over the
  next three, under the slash; `classic` has the burst alone. The cut into the
  crypt follows at 2.48 s.
- `masskill-V.jpg` (7.80 s on): the crypt charge's kill of 66 at 7.93 s (song
  129.317): the white wireframes are the 66 bodies' shards (both strips), and
  under `light-streaks` a handful of shared rings in the gems' colours open
  over the clearing, not one per body; nothing washes out.

## Sounds

The stand-in trailer's cue list (`demo3d trailer --stand-in`, whose mix finds
the free samples in `public/assets/sfx/`): 1165 cues under `edge`, 964 under
`--vfx classic`: 201 `death-pop`, as many as the death cries (both at most three voices a
beat), no `spawn-warp`; 0 cues without a file. The mix's peak before
the limiter is the same in both (1.55, 3.25% of samples over its knee): the
pops sit inside the shatter's beats rather than on top of them.
