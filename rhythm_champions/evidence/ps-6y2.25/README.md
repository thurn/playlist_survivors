# ps-6y2.25: a cached Library's Bee graphs and a deleted worktree

Measured on jarvis (M5 Max), 2026-11-02, in .worktrees/ps-6y2.25, whose `champions setup` cloned
the cache's exact Library, saved by slow CI in .worktrees/ci-e25f45c2e493 (since deleted).

## The cause

Unity compiles player scripts with `DeferDagVerification`: it builds with the graph already in
`Library/Bee` (`200b0aP.dag`) and only then checks it against its inputs. A cloned graph's
response files list the saving commit's scripts, so an existing script that uses a type from a
script added since fails to compile with a missing-name error (CS0103 here). For such an error
(CS0103, CS0117, CS0234, CS0246) the script updater reads the assembly's `.mvfrm` from the path
in the graph's output data (`200b0aP.dag.outputdata`, an absolute path into the saving worktree):
a DirectoryNotFoundException once that worktree is gone, thrown before the compiler's message is
logged, and the build fails. A warning alone, or a new script nothing uses, builds fine, and while
the saving worktree exists the updater reads its files and finds nothing to update (ps-6y2.1 and
ps-6y2.4 tested a cached Library that way).

## Before (the cache's Library cloned as it was)

The change: a new `Assets/Champions/Sim/Core/ReproNew.cs` (`static class ReproNew { const int
Value }`) used by one new line in `SimRandom.cs`.

- Warm editor: `champions build` failed in 2.6 s, then again in 1.6 s: `Unhandled exception during
  build: System.IO.DirectoryNotFoundException: Could not find a part of the path
  ".../.worktrees/ci-e25f45c2e493/rhythm_champions/Library/Bee/artifacts/200b0aP.dag/Champions.Sim.dll.mvfrm"`
  from `PotentiallyUpdatableErrorMessages.IsAnyPotentiallyUpdatable`, then `Error building Player
  because scripts had compiler errors`.
- Cold (batch mode): the same exception; `BuildPlayer` exited 1 after 13.4 s.

## After (`setup.restore_library` drops Bee's graphs and keeps its artifacts)

`champions setup --fresh-library` cloned the same entry (`Library cloned (19526 files, exact;
Bee's graphs dropped)`); setup's batch-mode open rebuilt only the editor graph, at this path.

- Warm editor, same change: built in 8.7 s; the new output data names .worktrees/ps-6y2.25.
- Cold, same change: built in 16.8 s.
- Speed, cold, with a change that builds either way (an unused new script): 16.6 s from the
  Library with its graphs, 16.1 s without them. Bee rebuilds the graphs at a new path in both
  cases (their input data names the project's path), and runs the same compile steps (51 and 47
  Csc lines).
