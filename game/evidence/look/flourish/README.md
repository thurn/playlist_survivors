# The death flourish and the spawn warp in game (ps-wnj.16, 2026-11-01)

The look's death flourish and spawn warp
([neon-look.md, VFX](../../../../docs/archive/design/neon-look.md#vfx)), with their
sounds. They come with the vfx axis's options (`light-streaks`,
`prism-streaks`) and only with them: `--vfx classic` draws and sounds neither,
so each `classic` strip below is the same run without them (and with the vendor
slashes; ps-wnj.15's strips isolate those).

- **Death**: on the death's impact frame, a soft pop a body wide on the body
  (gone in 0.1 s) and a thin ring of light on the ground racing out from it,
  both in the role's gem, over the crystal burst and the impulse ring. One
  instant's deaths within 3 m share one ring, larger: a mass kill is a few
  rings, not one per body.
- **Spawn**: each spawn in view (`Simulation.InView`, the spawner's own
  footprint test; the ring of spawns past the view's edge never warps) gets two
  thin rings on the ground collapsing onto it faster and faster and a soft
  glow where they meet 0.14 s in, before its first step.
- **Sounds**: a short bright pluck at each death (`death-pop`) and a rising
  swell at each spawn in view (`spawn-warp`), at their events' times, through
  the beat's aggregation (at most three voices per beat and sound).

Windowless fixed-time Mono Mac player, on jarvis, `--look edge`, a calm early
run with six monsters spawned in view at 4 s and a bat swarm at 8 s. From the
worktree's root:

```sh
SONG=public/reactive/house-128/house-128.ogg
D="--start-minute 1 --bot positioning --debug spawn:werewolf@4,spawn:werewolf@4,spawn:werewolf@4,spawn:skeleton@4,spawn:skeleton@4,spawn:skeleton@4,bats@8"
for V in classic light-streaks; do
  uv run --project demo3d/tools game look spawn-$V --backend mono --strip 6 --at 4.0 --frames 18 --evidence look/flourish/spawn-$V --player-args "--file $PWD/$SONG $D --look edge --vfx $V"
  uv run --project demo3d/tools game look death-$V --backend mono --strip 4 --at 11.28 --frames 12 --evidence look/flourish/death-$V --player-args "--file $PWD/$SONG $D --look edge --vfx $V"
  uv run --project demo3d/tools game look masskill-$V --backend mono --strip 4 --at 10.34 --frames 12 --evidence look/flourish/masskill-$V --player-args "--file $PWD/$SONG $D --look edge --vfx $V"
done
uv run --project demo3d/tools game perf --only peak --player-args "--look edge" --evidence look/flourish/perf-edge
uv run --project demo3d/tools game perf --only peak --player-args "--look prism" --evidence look/flourish/perf-prism
uv run --project demo3d/tools game beat flourish-sounds --sounds --file $SONG --seconds 16 --evidence look/flourish --player-args "$D --look edge"
```

## The strips

Each is consecutive frames, 1/60 s apart.

- **Spawn** (`spawn-V/`, 4.000 s on): the six debug spawns around the hero
  (the orange werewolves, the skeletons). Under `light-streaks` each one's
  rings close in over about eight frames and its glow lands under it at
  +9 to +14; `classic` shows them simply standing there from the first frame.
- **A single death** (`death-V/`, 11.283 s on, the kill counter's 112 to 113):
  the ghost below right of the hero breaks with a white pop on the impact
  frame and its ring opens out over the next eight frames, faint against the
  grid.
- **A mass kill** (`masskill-V/`, 10.350 s on, 28 deaths at 10.34 s, the
  counter's 82 to 110): a pop on each body on the impact frame and the clumps'
  shared rings (two or three for the whole kill) racing out; nothing washes
  out.

## Perf and sounds

`game perf --only peak` (IL2CPP), on a loaded machine (load average 24 to 33:
other sessions' Unity builds), every run ok, no frame over 33 ms after the
first frames. Edge with and without the flourishes, alternated to read the
noise:

| run | peak probe p50/p99 | main p99/max |
| --- | --- | --- |
| edge (`--vfx classic`) | 3.04/6.13, 3.80/7.44 ms | 1.8/2.9, 3.3/7.6 ms |
| edge (flourishes) | 3.64/5.67, 2.91/11.57 ms (`perf-edge/`) | 3.9/9.4, 5.0/20.8 ms |
| prism (flourishes) | 3.25/5.06 ms (`perf-prism/`) | 2.0/3.7 ms |

Two earlier edge runs under the heaviest load read 7.6/12.0 and 6.8/15.5 ms;
with classic and flourished edge alternating the two are within each other's
noise. Prism's ps-wnj.15 reading was 4.85/8.08 ms.

`game beat` on the calm run, `--look edge`:

- **Moments** (`flourish-moments.json`): 2829 moments, 0 off their frame; the
  pops, rings and warps are Effect moments, each on the first frame reaching
  its time (a probe run at the peak minute, 6027 moments, also 0 off).
- **Sounds** (`flourish-sounds.json`, `--sounds`, a real-time headless run):
  267 sounds, 0 dropped, 0 off their event or anchor; `death-pop` 34 (one per
  death, as `death`), `spawn-warp` 4 (the six spawns at 4 s share their beat's
  three voices, and one more spawn in view later).

A headless player builds no effects, so the sounds follow the look's vfx
option itself (`EffectLibrary.ActiveVfx`), not the drawn effects.
