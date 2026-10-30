# PX7: effects and night lighting (2026-10-30)

The effect library (`Data/effects.json`), the timeline-driven CFXR pool and its lights
(`EffectLayer`, `LightPool`), and the night (`Data/night-lighting.json`, `NightLighting`): the
moon, the hero's light, moonlight shafts, fireflies, glowing plants, bloom and the colour grade.
Every run was on the operator's Mac, windowless (batch mode, Metal), from the worktree's root,
with the IL2CPP player:

```
uv run --project demo3d/tools demo2d setup
uv run --project demo3d/tools demo2d build
APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
E="$PWD/demo2d/evidence/px7"
"$APP" -batchmode -force-metal --capture-stills "$E/effects" --effect-stills
"$APP" -batchmode -force-metal --capture-stills "$E/night" --stills-at 3,60,116,134,172.5
"$APP" -batchmode -force-metal --capture-stills "$E/night-empty" --empty --stills-at 3,60,116,134,172.5
"$APP" -batchmode -force-metal --start-at 160 --seconds 16 --measure "$E/climax-all.json"      # and --no-effects, --no-night, --no-post
uv run --project demo3d/tools demo2d unity EffectLibraryStills -out "$E/library"
```

## Every effect kind fires in the song

`EffectLibraryTests.EveryEffectKindAndProjectileFiresInTheSong` (dotnet) counts them in the song's
timeline: hit-spark 4,257, death-burst 2,185, parry-spark 1,536, slash-trail 133, thrust-trail 43,
spin-trail 5, slam-shock 3, ring 123, bolt-launch 1,132, bolt-impact 347, lightning 99, laser 2;
projectiles in flight: bolt 347, bone-bolt 430, blood-bolt 355. The library has an entry for each
and for nothing else, and every prefab it names is in the manifest.

## A still per kind

- **In the song** ([`effects/`](effects/)): `--effect-stills` picks, for each kind, the event nearest
  the camera's focus and draws the frame 0.15 s after it (after the hit flash; the beam 0.45 s into
  its sweep). [`effect-stills.txt`](effects/effect-stills.txt) maps each kind to its event and still;
  the spin and the ring share `still-037.245.jpg` (the same downbeat). Other effects live at the same
  moment, so each still also shows its neighbours.
- **Alone** ([`library/`](library/)): each library entry as the player sets it up (EffectLayer.Make,
  its scale and sorting), at three ages, over forest ground under the moon with its light cue, the
  knight beside it; flights cross their panel at 10 m/s; the beam at half length to fit.

## The night

- [`night-empty/`](night-empty/) (`--empty`: the hero alone, unflashed, no effects) and
  [`night/`](night/) (the song) at the same five times: the camp glade (3 s), the moss clearing (60 s),
  the bridge (116 s), past the ledge (134 s) and the climax in the great clearing (172.5 s).
- What to look for: the moon's cool fill (0.24), the warm pool on the hero, soft moonlight shafts on
  the clearings, the umbrella plants' teal glow, fireflies by the trees, and in the song the effects
  carrying the light (each kind's cue on a pooled `Light2D`), bloom on the HDR effects and the grade.
  The operator's judgement is in docs/tech-demo-2d/README.md, Pending on the operator.

## Cost at the climax

`--measure` draws each fixed-time frame of 160 to 176 s (the final peak, 336 to 381 on screen in the sampled frames) by hand
at 1080p and times it through a one-pixel readback, so each time includes the GPU's work (PX1's
method; 961 frames; the first frame, about 60 ms, is the player's warm-up):

| Run | mean | p50 | p95 | p99 | most effects, particles, effect lights |
|---|---|---|---|---|---|
| everything ([`climax-all.json`](climax-all.json)) | 7.91 | 8.05 | 8.53 | 9.03 | 110, 3,540, 28 |
| `--no-effects` | 7.84 | 8.05 | 8.46 | 8.86 | 0 |
| `--no-night` (no shafts, fireflies or plant lights) | 7.95 | 8.05 | 8.46 | 8.85 | 110, 3,540, 28 |
| `--no-post` (no bloom or grade) | 7.76 | 8.06 | 8.48 | 8.93 | 110, 3,543, 28 |

- Effects and their lights cost about 0.1 ms mean and 0.2 ms at the 99th percentile, the bloom and
  grade about 0.15 ms mean, and the night's extras nothing measurable (within the runs' noise). The frame is well inside
  16.7 ms; PX10 measures the whole song.
- **Caps:** at most 28 effect lights (always full at the peak, so the brightest win), 24 firefly and
  24 plant lights (10 and 21 used at most), and per kind 2 to 48 live instances (`max` in the
  library; 110 live at most). They hold the climax at this cost; PX10 may raise the light pool.

## Checks

- `dotnet test demo2d/dotnet/Demo2D.Choreography.Tests`: 32 of 32, 5 of them PX7's `EffectLibraryTests`.
- `demo2d test`: 89 of 89 EditMode tests. PX5's seek test now runs with the effects and the night:
  a still at 116 s drawn after a seek forward, after every 1/60 s frame from the start and after a
  seek back is the same image to the pixel, effects re-simulated from their events' seeds.
