# Trailer: the look (ps-hgs.3)

The trailer's own look on top of each environment's
([trailer.md, Look](../../../../docs/tech-demo-3d/trailer.md#look)): a
per-shot grade, 2× supersampling, impact lens hits, film grain and lens dirt,
plus the style post fix, which also changes normal runs. Everything here comes
from the IL2CPP player on the Mac (Apple M5 Max, Metal), windowless at fixed
time, built at staging `f0369f97` plus this bead.

**Verdict:**

- **Lens hits.** Each slam, ring and mass kill hits the lens as a two-frame
  punch, a radial streak and a red–blue split from the impact. It is clean by
  the third frame and gone within the eighth.
- **The grade.** It separates the shots: the crypt harder and greyer, the
  desert warmer, the snow cooler.
- **Grain and dirt** stay under the picture. They never read as noise.
- **Supersampling** cleans the thin edges that MSAA 4× leaves: visor slits
  and armour plates.
- **The style fix.** Under a style, snow and the desert keep their own
  exposure.

## Before and after

Every sheet is a row per trailer second and four columns:

1. **before:** `--no-trailer-look --supersample 1`, the environments as a
   normal run draws them;
2. **after:** the default, which in a capture is 2×;
3. **neon-before:** `--palette neon --monster-style crystal`, the look off;
4. **neon-after:** the same with the look on.

| file | what |
|---|---|
| [drop.jpg](drop.jpg) | the drop's flash cut into the desert (81.1, trailer 2.481): the lens hit streaks the shot's opening blow out of the white; the desert's grade warms it by 3.0 s |
| [snow-slam.jpg](snow-slam.jpg) | a 1.0-strength mass-kill slam in the snow's vortex (trailer 10.223): 10.233 is the hit's frame, 10.25 a fifth of it, 10.283 clean |
| [final-slam.jpg](final-slam.jpg) | the final slam in the forest (99.1, trailer 30.352): 30.367 the punch, 30.383 fading, 30.40 nearly sharp, 30.55 the aftermath |
| [grade.jpg](grade.jpg) | the grade alone, away from hits: the crypt's duel (0.5, 2.0), the desert (2.6), the snow (10.4) and the forest (30.55) |
| [supersampling.jpg](supersampling.jpg) | the hero at trailer 2.0, 3× nearest-neighbour: 1× (before) against 2× with the look off (plain2x) |
| `after-*.jpg` | the default run's stills, full width |
| [style-fix-forest.jpg](style-fix-forest.jpg), [-snow](style-fix-snow.jpg), [-desert](style-fix-desert.jpg) | normal runs at song 60 s: baseline, crystal under the old overwrite (snow and desert only) and crystal with the fix |
| [look.py](look.py) | the JPEGs, sheets, crops and L* tables |

## The lens hits

The player logs the run's hits. The default trailer has 24, at these trailer
seconds (strength):

```
2.481 (1.00), 3.255 (0.79), 3.642 (0.79), 4.610 (0.75), 8.674 (1.00), 9.836 (0.72), 10.223 (1.00),
11.771 (1.00), 12.932 (0.67), 13.320 (1.00), 14.868 (1.00), 17.965 (1.00), 19.126 (0.73), 19.513 (0.91),
21.061 (0.80), 21.836 (0.88), 22.610 (0.80), 22.997 (0.80), 23.771 (1.00), 27.255 (1.00), 28.223 (0.70),
28.803 (1.00), 29.771 (0.74), 30.352 (1.00)
```

Most cuts open on a heavy blow, so most cuts carry a hit. The shot list's
tuning (ps-hgs.7) can thin them by raising `TrailerLook.MassKillWeight`.

Tuning took three rounds:

1. **The first.** Its 0.6% split decayed as (1 − u)². It washed the drop
   into a smear of red and cyan copies, and left the climax's packed horde
   unreadable for most of the eighth.
2. **The second.** It cut the split to 0.2% and decayed as (1 − u)⁴. Two
   frames after the final slam, its 1% streak still smeared monsters a few
   pixels across.
3. **What shipped.** Full strength holds for 1/60 s, so the impact's frame
   always peaks, then falls as e^(−10x)·(1 − x). The streak grows smoothly to
   its 3% cap 0.6 of the width from the impact.

Grain went from Medium2 at 0.3 to Thin2 at 0.18. At 3× zoom the first grain
was blotchy, like a dirty texture.

## The grade

L* of the grade stills, before against after: the look's grade and 2×, with
no hit in flight.

| trailer s | shot | grade | p2 before → after | p50 before → after | p98 before → after |
|---|---|---|---|---|---|
| 0.5 | 1, crypt | exposure −0.05, contrast +8, saturation −6 | 15.9 → 13.3 | 39.1 → 37.5 | 68.4 → 68.4 |
| 3.0 | 2, desert | +0.1, +6, +10, temperature +6 | 14.2 → 14.6 | 64.5 → 67.3 | 86.1 → 89.2 |
| 10.4 | 3, snow | +0.05, +4, −4, −8 | 9.7 → 9.5 | 64.0 → 65.6 | 99.2 → 99.7 |
| 30.55 | 10, forest | contrast +6, saturation +8 | 2.1 → 2.0 | 39.5 → 40.1 | 83.5 → 86.1 |

## Normal runs

The look harness, windowless, as `demo3d look SLUG --trial`. The p50 against
the committed sets:

- **Baseline** (`ps-hgs3-normal`) against [look/final](../../look/final/README.md):
  - The six beauty shots match within 0.02 L* on p2, p50 and p98.
  - The song stills differ exactly as ps-hgs.6 measured them
    ([normal-run.md](../playback/normal-run.md)): song-025 +0.35, song-060
    +0.74, song-100 +1.41, song-145 +4.59, song-175 +21.52. That is the
    song's timeline drift since look/final, not this bead.
  - A baseline run's code path is unchanged: render scale 1, no style post
    look, no lens pass.
- **Forest plus a style** (beauty shots, `--monster-style NAME`):
  - **hologram** matches [styles/hologram](../../styles/hologram/README.md)
    within 0.01 L* everywhere.
  - **painted** matches [styles/painted](../../styles/painted/README.md)
    within 0.04 L* everywhere.
  - **crystal** is 0.6–2.4 L* darker in p50 than
    [styles/crystal](../../styles/crystal/README.md), with p2 and p98 within
    1.3. The crystal treatment's shatter changed after that set was taken:
    a8bb18fc, 3471d137, 99bc37c7, 64f45b4a and 1c56a7f6 (the shards, the
    burst, the sinking piles). The forest's post values under every style are
    the tuned ones within 1e-5 (MonsterStyleTests'
    AStylesPostLookIsOffsetsFromTheForestsSoOtherEnvironmentsKeepTheirOwn).
    So the drift is crystal's newer look, which this bead doesn't touch.
- **Snow and desert under a style.** These are normal runs at song 60 s with
  `--no-post-feel`, p50 L*. The old overwrite is reproduced by setting the
  environment's base to the forest's (`--post-exposure 0.8 --post-saturation
  30 --vignette 0.29`).

  | environment | baseline | crystal, old overwrite | crystal, fixed |
  |---|---:|---:|---:|
  | forest | 38.5 | 43.5 | 43.5 |
  | snow | 67.2 | 71.7 | 64.3 |
  | desert | 70.1 | 76.1 | 67.3 |

  Crystal takes no exposure offset from the forest's, so snow and desert keep
  their 0.45. Crystal's contrast and saturation offsets move them as they
  move the forest.

## Cost

Two windowless `--gpu-probe-at` runs per setting over the climax, in
[performance.md](../../../../docs/tech-demo-3d/performance.md#the-trailers-look):

- At 1×, the climax runs 11 ms and the final slam's frames 16 ms.
- At 2×, the climax runs 14.5 ms and the slam's frames 24 ms.
- The lens hit adds 0.5–1.5 ms on its frames.

The live default is 1×; captures render at 2×.

## The runs

```
P="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
T=0.5,2.0,2.465,2.481,2.5,2.533,2.6,3.0,10.2,10.233,10.25,10.283,10.4,30.35,30.367,30.383,30.4,30.55
run() { C=$PWD/captures/trailer/look/$1; shift; mkdir -p $C
  "$P" -batchmode -force-metal -logFile $C/player.log --trailer --fixed-time --capture-stills $C --stills-format png \
    --seconds 31 --stills-at $T "$@"; }
run after
run before --no-trailer-look --supersample 1
run plain2x --no-trailer-look
run neon-after --palette neon --monster-style crystal
run neon-before --palette neon --monster-style crystal --no-trailer-look --supersample 1
L() { uv run --project demo3d/tools python demo3d/evidence/trailer/look/look.py "$@"; }
for r in after before neon-after neon-before; do L jpegs captures/trailer/look/$r $r; done
L sheet drop 002.465,002.481,002.500,002.533,002.600,003.000 before after neon-before neon-after
L sheet snow-slam 010.200,010.233,010.250,010.283,010.400 before after neon-before neon-after
L sheet final-slam 030.350,030.367,030.383,030.400,030.550 before after neon-before neon-after
L sheet grade 000.500,002.000,002.600,010.400,030.550 before after
L crops supersampling 760,330,320,180 002.000 captures/trailer/look/before captures/trailer/look/plain2x
```

The individual before and neon JPEGs went into the sheets and aren't kept.
The style-fix stills are normal runs: `--fixed-time --start-at 55
--seconds 6 --stills-at 60 --no-post-feel --environment ENV
[--monster-style crystal]`.
