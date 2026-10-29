# One clip schedule for both demos (ps-tfn.2.3, 2026-10-29)

demo2d's `SpriteEvaluator` and demo3d's `TimelineEvaluator` had two copies of the engine-free
timing rules. Both now use the package's `ClipSchedule`:

- retimed starts, ends and speeds;
- each entity's clips in start order;
- the walked distance;
- the clip playing at a time;
- the hit-stop and the heavy-blow slowdown;
- the flashes and the death's fade.

`SpriteEvaluator` moved into the engine-free `Demo2D.Choreography`, so its frame tests run under
`dotnet test`. Where the copies differed, the schedule chose as follows (choreography.md, "Clip
annotations and retiming"):

- **A killing blow shows the death.** demo2d already did this. demo3d changes.
- **Ties go by action index**, with a stable sort.
- **The heavy-blow slowdown is a constructor flag.** demo3d sets it. demo2d's PX8 will.

Every run was on the operator's Mac, windowless, in the bead's worktree. Each "before" was the
base commit `2932deff`, built and captured in the same worktree before the change.

## The song's timeline

`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- song --out song.json`
gives SHA-256 `09945e3b0a2932713c6d4fd00743bc81bc3cf648241b9cd961a60c804e1cdadd`. That is the
same as PX0's: the core's timeline output didn't change.

## Old against new, state by state

A scratch harness compiled the old evaluators against stub `UnityEngine` types. It evaluated every
living entity at every 1/60 s frame of the song, with both the old and the new evaluator:

| | Evaluations | Differing |
|---|---|---|
| demo2d (the 2D song) | 6,425,393 | 0 |
| demo3d (the 3D song) | 7,241,328 | 4,474, all on 40 of the song's 42 killing blows |

The only demo3d change is the killing-blow rule. Before, those 40 monsters played their flinch
through the death's hold and dissolve. They never fell. Now they play the fall.

The tie-break changed nothing. At the stills' times, two such monsters differ at 45 s, and none
differ at 170 or 196 s.

## Tests

- `dotnet test demo2d/dotnet/Demo2D.Choreography.Tests`: 21 passed. This includes PX5's
  impact-frame, hit-stop and fade tests, moved from `PlaybackTests`. The impact test puts 7,819
  attacks, hits and deaths on their impact frame. 359 are cut in by a later clip, and 29 killing
  blows show the death.
- `dotnet test demo3d/dotnet/Demo3D.Choreography.Tests`: 129 passed. This includes
  `ClipScheduleTests`, on a small hand-built timeline.
- `demo3d test` (EditMode): 217 of 220 passed, 0 failed, 3 skipped. The three skipped are the
  same `SkeletonLodPreviewTests` as PX0's.
- `demo2d test` (EditMode): 78 of 78 passed. This includes `PlaybackTests`' seek still.

## Stills

The IL2CPP players were built before and after the change. The stills use the same commands as
PX0's and PX5's. Each table gives the mean absolute difference per channel and the share of
pixels that are off by over 24.

demo3d, at 45, 170 and 196 s:

| Still | Old vs. new | Old vs. old (a second run) |
|---|---|---|
| 45 s | 0.060, 0 % | 0.063, 0 % |
| 170 s | 0.091, 0.006 % | 0.088, 0.006 % |
| 196 s | 0.442, 0 % | 0.449, 0 % |

The new stills are as close to the old ones as two old runs are to each other. The two changed
monsters at 45 s don't show above JPEG and particle noise, so no still is committed here.

demo2d, at 8, 116 and 172.5 s: old vs. new is 0.000 for every still, and so is old vs. old. The
stills are identical.
