# PX0: the shared choreography package (2026-10-29)

The choreography core moved from `demo3d/Assets/Demo3D/Choreography/` into
`unity-packages/com.playlistsurvivors.choreography/` (namespace
`PS.Choreography`). Every check ran on the operator's Mac (M5 Max, Unity
6000.6.7f1, IL2CPP, Metal), first on the base commit `fd0fd972`, then on the
move, in the same worktree.

## The song's timeline

`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- song --out song.json`:

| | SHA-256 of the dump (19,084,561 bytes) |
|---|---|
| before | `09945e3b0a2932713c6d4fd00743bc81bc3cf648241b9cd961a60c804e1cdadd` |
| after | `09945e3b0a2932713c6d4fd00743bc81bc3cf648241b9cd961a60c804e1cdadd` |

`cmp` finds no difference: the diff is empty. Two runs before the move were
also identical.

## Tests

- `dotnet test demo3d/dotnet/Demo3D.Choreography.Tests`: 121 passed.
- `demo3d test` (EditMode): 211 of 214 passed, 0 failed, 3 skipped (three
  `SkeletonLodPreviewTests` that skip without the isolated skeleton-LOD preview
  bake, unrelated to the move).

## BakeAll, the build and the stills

- **BakeAll:** 138 s before, 130 s after, both exit 0, with the same 436
  files under `Assets/Baked/`. Their bytes differ, but so do two bakes of the
  same commit (68 files; scene file IDs, for one, are not stable),
  so bytes are no test here; the stills are.
- **Build** (`demo3d build`, IL2CPP): 1,196 s before (cold), 45 s after
  (incremental), 1,827 MiB each, no errors.
- **Stills** at 45, 170 and 196 s, by mac.md's windowless command:
  [45 s](still-045.000.jpg), [170 s](still-170.000.jpg),
  [196 s](still-196.000.jpg) (after the move). Pixel difference against the
  old build's stills, and the old build against itself:

  | still | mean abs. difference, old vs. new | old vs. old (a second run) | pixels off by over 24 |
  |---|---|---|---|
  | 45 s | 0.16 | 0.05 | 0 % |
  | 170 s | 0.11 | 0.08 | 0 % |
  | 196 s | 0.45 | 0.42 | 0 % |

  The new build's stills are as close to the old build's as the old build's
  are to themselves (JPEG and particle noise).

## mac.md's commands

`demo3d setup`, `demo3d unity BakeAll`, `demo3d build`, `demo3d test` and the
windowless stills command ran as written, from the worktree's root.
