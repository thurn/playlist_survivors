# Trailer: kiting formations (ps-hgs.5)

The horde in every horde shot now chases the hero without jamming, in the
shot's formation: a charge, a vortex or a converge ([trailer.md, Kiting and
formations](../../../../docs/tech-demo-3d/trailer.md#kiting-and-formations)).
These sheets are the frame sequences that judged it.

**Verdict:** every visible monster walks or attacks on every beat. The horde
streams in, bends after the hero and re-forms behind it, with no standing
blocks and no shuffling in place. `trailer --checks` reports zero liveness
violations in all ten shots, and every shot holds its target.

## What the sheets show

The player can't play a single shot until the playback bead, so these are
top-down plots of each shot's timeline. The trailer CLI's `--frames` draws
them, with north up:

- **Red dots:** live monsters. Each yellow tick is the ground it covered in
  the last beat.
- **White dot:** the hero.
- **Yellow trapezoid:** the camera's view, turned by the shot's heading.
- **Background:** the environment's layout (structures grey and sand, trees
  green, goals orange).

Frames read left to right, then down.

| file | shot | frames |
|---|---|---|
| `charge-shot2.jpg` | 2, desert charge, bars 81–84 | every beat from the cut (81.1–82.4) |
| `vortex-shot3.jpg` | 3, snow vortex, bars 85–88 | every other beat from the cut |
| `converge-shot4.jpg` | 4, forest, four streams, bars 89–90 | every beat from the cut |
| `converge-shot10.jpg` | 10, forest climax, six streams, bars 97–98 | every beat from the cut (97.1–98.4) |

What to look for:

- **Charge.** The hero meets the wave four beats before the cut, so the
  cut's slam lands inside it and leaves a crater. The wave closes round the
  hero, and what it passes turns and trails behind as the hero runs on
  (upper right).
- **Vortex.** A ring round a cleared pocket, with a dense lobe that swings
  round the loop after the hero. That is the spiral.
- **Converge.** The streams are lobes outside the view that pour inward
  (every tick points at the hero) and merge into a ring at its pocket. The
  hero cuts across them. In the climax the flood fills the widening view
  to its edges.

## Numbers

`trailer --checks` at the commit these sheets were made from:

```
shot  environment  on screen / target  swings/s  kills/swing (mean, peak)  hero acted  kite  liveness
   1  crypt                - / 3           1.21          1.0, 1                 100 %     -         0
   2  desert             597 / 600         3.23         19.1, 197               100 %   2.4         0
   3  snow               710 / 700         2.42         47.8, 160               100 %   2.4         0
   4  forest             501 / 500         2.58         20.8, 111               100 %   2.3         0
   5  crypt              521 / 540         2.58         27.2, 106               100 %   2.4         0
   6  desert             389 / 400         1.29         38.5, 43                100 %   2.3         0
   7  snow               471 / 500         1.94         78.0, 164               100 %   2.5         0
   8  crypt              458 / 500         2.58         18.0, 20                100 %   2.3         0
   9  desert             615 / 600         1.94         16.0, 18                100 %   2.4         0
  10  forest            1283 / 1300        2.84        175.7, 1578              100 %   2.3         0
trailer checks: ok
```

The checks also pass for `--roster forest` and `--roster heroic`. With the
trailer's seed changed, seeds 3, 5, 9, 10, 11, 13, 17, 21, 27, 28, 29 and
42 pass every check too. The roster sets share the trailer's shot fields and roster, so
their tables match the default's. Pinned with
`--environment` to forest, crypt or neon, shot 2 or 5 can miss one bar's
density by up to 20%. The acceptance doesn't cover pins, and the first cut
had the same gap.

## Reproduce

From the repository root:

```
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --checks
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --shot 2 --frames captures/kiting/shot2 --every 1
uv run --project demo3d/tools python -I demo3d/evidence/trailer/kiting/sheet.py \
  demo3d/evidence/trailer/kiting/charge-shot2.jpg "charge shot 2" $(ls captures/kiting/shot2/*.png | head -8)
```

The vortex sheet takes every other frame of shot 3. The climax sheet takes
the first eight frames of shot 10.
