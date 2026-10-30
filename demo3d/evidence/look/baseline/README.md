# Look set: baseline (ps-e8r.1, 2026-10-29)

The look pass's starting point, at staging `d7d2dc68` (the clumps of
ps-e8r.9 included), rendered by the look harness on the operator's Mac
(Apple M5 Max, Metal, the IL2CPP player). Every later bead in epic ps-e8r
compares its set with this one:

```
uv run --project demo3d/tools demo3d look <slug> --baseline baseline
```

How to run it, and what each number means: [Running on the Mac](../../../../docs/tech-demo-3d/mac.md#the-look-set)
and [look.md](look.md).

## What's here

- `song-025` … `song-175`: song stills at 25, 60, 100, 145 and 175 s. That's
  one each in groove (light), drop-1, groove-2, drop-2 and the final peak.
  The bead suggested 40, 75, 120, 165 and 190 s, but 75 s is the edge of the
  breakdown, 120 s is in the build and 190 s is in the outro, so each time
  moved into the middle of its section (the song map's bars, about 1.57 s
  each).
- `<shot>-horde` and `<shot>-empty`: TD15's six beauty shots, with the horde
  and empty. The measured still and its ID render stay in the gitignored
  `captures/look/baseline/`.
- [look.md](look.md): the stats, readability and effects tables.
  [look.json](look.json): the numbers the next set's `--baseline` reads.

## Readability against TD15

| shot | TD15 dL* (Cygnus) | baseline dL* (Mac) | difference | TD15 monster share | baseline |
|---|---:|---:|---:|---:|---:|
| opening | 20.6 | 20.8 | +0.2 | 59.0% | 59.0% |
| path | 21.0 | 21.1 | +0.1 | 31.9% | 31.9% |
| glowing-tree | 18.8 | 18.8 | 0.0 | 32.3% | 32.3% |
| graveyard | 20.2 | 20.2 | 0.0 | 33.1% | 33.1% |
| courtyard | 19.4 | 19.4 | 0.0 | 41.6% | 41.6% |
| forest-edge | 19.5 | 19.6 | +0.1 | 28.5% | 28.5% |

Every shot is within 0.2 L* of TD15's, with the same monster pixels, and all
pass the floor (|dL*| >= 17). Metal on the Mac's GPU barely changes the frame:
the IL2CPP player runs the same simulation as Cygnus's IL2CPP player.

## Repeatability

Two runs of the same build give:

- identical ID renders and measured stills (but for a few particle pixels), so
  readability repeats to 0.1;
- stills with effects that differ on 1-7% of pixels, by up to 47 levels: the
  GPU particles land in a different order. The empty stills (only ambient
  particles) differ on under 0.1% of pixels, by 3 levels at most.

None of this moves a stat by more than 0.1, far below what a look change
should move.

## Build path and timings

IL2CPP, the harness's default. Both players built in this worktree from the
same commit, after `setup` (43 s, a warm Library) and `BakeAll` (148 s):

| | build (warm shader cache) | the whole set (23 captures, 4 at a time) |
|---|---:|---:|
| Mono | 29 s | 52-60 s |
| IL2CPP | 113 s | 23-28 s |

The players don't render the same images. Mono's simulation diverges from
IL2CPP's (its song timeline has 3,573 entities against IL2CPP's 3,576; the
two diverged on Cygnus too), so its hordes stand elsewhere. Its beauty shots were 0.1-1.5
L* from TD15's dL*, and its stills' chroma up to 6 C*ab away. IL2CPP matches
Cygnus's numbers, renders twice as fast, and costs 1.4 minutes more to build,
so a bead that renders before and after comes out about even. Sets compared
with each other must use the same player; every set in ps-e8r uses IL2CPP.
