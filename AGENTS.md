# Working on Rhythm Champions

Rhythm Champions (formerly Playlist Survivors) is a horde survivor rhythm game. It is a free, open source, non-commercial project.

We move quickly. Code and documentation get out of date fast. Ask clarifying questions at the start of sessions if you're not sure what's still current.

Vision: Rhythm Champions is "the game that doesn't need a trailer". This is because *every moment of gameplay is cinematic*. The aesthetic experience is prioritized over everything.

The look is retro 3D neon wireframe in the synthwave style: low-poly 3D models, usually built from simple faceted shapes, whose glowing edges connect into closed facets over a dark body (docs/champions/design.md, The aim: retro 3D neon wireframe).

The design was locked on 2026-11-01 after months of prototypes: docs/champions/design.md is the design and docs/champions/plan.md the implementation plan. Every earlier doc is archived under docs/archive/ and is not authoritative; when an old doc, comment or evidence file disagrees with docs/champions/, docs/champions/ wins.

## Overview

- `rhythm_champions/`: the game, a Unity project with its Rust audio plugin (`plugin/`), its pure C# sim (`Assets/Champions/Sim/`, tested under `dotnet/`) and its tools (`tools/`, the `champions` CLI; `just champions`). It is built by the Enact epic in docs/champions/plan.md, so check what has landed before relying on any of it; operator checks go in `rhythm_champions/evidence/operator-pending.md`.
- `docs/champions/`: the design, the plan and (once written) the style guide. `docs/archive/`: earlier designs, for history.
- `scripts/`: checks (`check-fast`, `ci-full`) and Enact's worktree hooks.
- SETUP.md covers installing and the vendor assets.
- **Frozen**, kept for reference, outside CI, never extended: `native/` (the Rust 2D game, with `content/`, `levels/`, `public/`, `corpus/`, `songs/`, `tools/analysis/`, `tools/baker/`, `assets-src/`), `game/` (the first Unity game), `demo3d/`, `demo2d/`, `stylelab/`, `herocap/` and `unity-packages/`. Rhythm Champions copies what it needs from them and references none of them at build time. Don't fix or extend a frozen project unless the operator asks.
- Outside the tree: caches in `~/Library/Caches/playlist-survivors/` (`$PS_CACHE`), the vendor packs in `~/synty` and `~/unity_assets`, and captures in the tree's `captures/`. Never search with `find /` or `find ~`: such a crawl runs for an hour.
- This repository is public: never commit vendor assets (Synty, Magic Pig Games, Hovl Studio, anything in `~/synty` or `~/unity_assets`) or anything derived from them, monster meshes, bakes and Blender files included (docs/archive/tech-demo-3d/vendor-assets.md).

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
- Assume a filed bead is started immediately: a running Enact run can claim
  it within minutes. Put everything it relies on (docs, skills, code it
  builds on or that removes what it would touch) on staging first, then
  file it, or hold it back with a dependency until then.
- `/enact <bead|epic|task>` runs Enact work inline in one session (subagents
  for an epic's beads) and waits for slow CI; `/enact-plan <bead|plan|task>`
  runs it unattended across sessions. Most work goes through neither.
- **Runs are light by default**: treat every `/enact` and `/enact-plan`
  invocation as if it carried the keyword **light** (the skills' "Light
  runs" sections: `run:light` label, no Mason, Sage or Archivist work, at
  most one final Warden), unless the invocation says **full**.
- Enact roles in use: light runs use Executor, Warden and Steward; a full
  run uses all (Executor, Warden, Steward, Mason, Sage, Archivist).
- Review policy: light runs get the skills' single final Warden; a full run
  gets one Warden bead per substantial implementation bead.
