# Working on Playlist Survivors

Playlist Survivors is a horde survivor rhythm game. It is a free, open source, non-commercial project.

We rely heavily on prototyping, and move quickly. Code and documentation get out of date fast. Ask clarifying questions at the start of sessions if you're not sure what's still current.

Vision: Playlist Survivors is "the game that doesn't need a trailer". This is because *every moment of gameplay is cinematic*. The aesthetic experience is prioritized over everything.

## Overview

- `native/`: the game, a Rust Cargo workspace (`native/crates/`). Design in docs/design/native-app.md and docs/design/survivors-run.md.
- `game/`: the Unity game, native's run in 3D (docs/design/unity-game.md, epic ps-msz), on the demos' tools (`uv run --project demo3d/tools game`, `just game`); evidence and its operator list in game/evidence/.
- `tools/analysis/`: the Python music analysis tool; `songs/` holds its song maps.
- `tools/baker/`, `assets-src/`, `public/assets/`: the asset baker and its inputs and outputs.
- `demo3d/`: the Unity 3D tech demo (docs/tech-demo-3d/).
- `demo2d/`: the Unity 2D pixel-art demo (docs/tech-demo-2d/README.md), on the same tools (`uv run --project demo3d/tools demo2d`).
- `stylelab/`: the Unity shader style lab, demo3d's horde in cyclable stylized treatments (docs/tech-demo-3d/stylelab.md), on the same tools (`uv run --project demo3d/tools stylelab`).
- `unity-packages/com.playlistsurvivors.choreography/`: the engine-free choreography core both demos share (docs/tech-demo-3d/choreography.md).
- `scripts/`: checks (`check-fast`, `ci-full`) and Enact's worktree hooks.
- SETUP.md covers installing, building and playing.
- This repository is public: never commit vendor assets (Synty, `~/unity_assets`) or anything derived from them (docs/tech-demo-3d/vendor-assets.md).

## Enact

`enact` below means `python3 ~/enact/scripts/enact.py` (`py ~/enact/scripts/enact.py` on
Windows, where `python3` is the Microsoft Store stub).

- **Every change goes through a worktree and CI**, whether it is made by a
  human, an ad hoc agent session or an Enact run:
  1. From the main checkout, `enact worktree create <short-name>`. Work only
     in the printed path; never edit or commit in the main checkout.
  2. Commit there, using Conventional Commits syntax.
  3. Run `enact push` in the worktree. It rebases onto `origin/staging`
     (merging `master` if someone bypassed staging), runs the fast checks,
     pushes to `staging`, starts slow CI in the background and removes the
     worktree. On exit code 2, fix the reported conflict or failing check in
     the same worktree and run it again.
  4. If its output has `watch_ci`, no Enact run is around to repair a
     slow-CI failure: follow it (`enact ci --wait <sha>` until it passes;
     on failure, claim and fix the Steward bead) before finishing.
- Never commit to, or push directly to, `master` or `staging`. `master`
  advances only when slow CI passes on `staging`; a slow-CI failure files a
  P0 Steward bead. `enact ci --status` shows the runner and its log. Enact
  fast-forwards local `staging` and `master` to match the remote, so keep
  the main checkout clean.
- Fast and slow check commands are in `.enact.toml`.
- Beads live in this repo (`bd`); publish bead changes with `enact sync`.
- `/enact <bead|epic|task>` runs Enact work inline in one session (subagents
  for an epic's beads) and waits for slow CI; `/enact-plan <bead|plan|task>`
  runs it unattended across sessions. Most work goes through neither.
- Enact roles in use: all (Executor, Warden, Steward, Mason, Sage, Archivist).
- Review policy: one Warden bead per substantial implementation bead.
