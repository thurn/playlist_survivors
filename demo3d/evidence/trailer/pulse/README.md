# The horde's beat pulse

Two bars of each cut, 91.1 to 92.4 (trailer 17.965 to 21.06 s, drop-2's full arrangement in
shot 3, lava), every frame, after and before the beat pulse (ps-b5z.3; [crowd.md, The beat
pulse](../../../../docs/tech-demo-3d/crowd.md#the-beat-pulse)):

| Sheet | What it is |
|---|---|
| `action.jpg` | The default cut, baseline monsters, with the pulse |
| `crystal.jpg` | The crystal cut, crystal monsters, with the pulse |
| `action-before.jpg`, `crystal-before.jpg` | The same frames from the build before it (staging c3ffb0eb) |

Each row is one beat (gold labels: downbeats). The columns are frames from the first frame at or
after the beat: 3 before it (the anticipation), the impact, then +2 to +18 (300 ms; a beat is
23.2 frames). The crop is 600 x 400 px of the 1080p frame, just south-east of the hero. Under the
grid, the crop's mean luma per frame, with the beats marked.

What they show:

- **Every beat lands on its own frame.** The horde lights up in its own colours on the impact
  frame and fades within the beat, on all eight beats of both cuts. The crop's luma jumps on the
  impact frame by these factors over 3 frames before it (before the change, PostFeel's exposure
  pulse alone):

  | Beat | 91.1 | .2 | .3 | .4 | 92.1 | .2 | .3 | .4 |
  |---|---|---|---|---|---|---|---|---|
  | default, after | 2.68 | 1.57 | 1.77 | 1.85 | 2.95 | 1.74 | 1.81 | 1.86 |
  | default, before | 1.60 | 1.12 | 1.14 | 1.16 | 1.66 | 1.14 | 1.14 | 1.13 |
  | crystal, after | 2.05 | 1.69 | 1.73 | 1.83 | 2.29 | 1.79 | 1.83 | 1.86 |
  | crystal, before | 1.39 | 1.20 | 1.17 | 1.22 | 1.46 | 1.24 | 1.21 | 1.21 |

- **Downbeats hit harder**: a squash of about 21% of their height against 12% and a throb 1.7 times a beat's, at this
  energy. Their frames are also the camera's punch, whose radial blur (in both builds) smears the
  impact column on the downbeat rows; the luma jumps and the +2 frames show them.
- **The squash.** Monsters drop and widen on the impact (most visible in the crystal cut's
  silhouettes, +0 against -3) and stretch back narrower at +6 to +9. The game camera looks down
  at 55 degrees, so the footprint's widening reads more than the height: the throb carries the
  pulse at the trailer's view widths, the squash adds weight up close.

Made with (the before runs add `--app` with the earlier build's player):

```
uv run --project demo3d/tools demo3d filmstrip pulse-action --at 17.85 --frames 200 \
  --player-args="--trailer" --crop 1150,600,600,400 --chunk 100 --workers 2
uv run --project demo3d/tools python demo3d/evidence/trailer/pulse/sheet.py \
  captures/filmstrip/pulse-action 17.85 17.96474 8 1150,600,600,400 \
  demo3d/evidence/trailer/pulse/action.jpg pulse-action
```

and the same with `pulse-crystal` and `--player-args="--trailer crystal"`. The pulse is a
function of song time (StyleBeat), so a rerun draws the same pulse; the trailer's frames already
differed between runs by a few levels in a few thousand pixels before it (both builds, checked
with `--check`).
