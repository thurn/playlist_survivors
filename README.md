# Rhythm Champions

A horde survivor rhythm game for the Mac. A white knight fights a horde of
neon monsters to whatever music you are listening to: its sword swings on
the beat by itself, the horde steps to the music, and Space dashes, harder
and wider on the beat. Pick an app that is playing music, or a file, and
play.

**Status: in development.** V1 is level 1, start to victory, built in a new
Unity project, `rhythm_champions/`, played with `just champions` once its
foundation lands. Until then nothing in this repository plays the new game.

- [The design](docs/champions/design.md): what the game is, locked
  2026-11-01.
- [The plan](docs/champions/plan.md): how V1 gets built, and its Enact
  epic's beads.

The project was called Playlist Survivors; the repository keeps that name.

## Layout

- `rhythm_champions/`: the game (Unity, with its Rust audio plugin, C# sim
  and Python tools), arriving with the epic's first beads.
- `docs/champions/`: the design and the plan. `docs/archive/`: every earlier
  design, kept for history.
- `scripts/`: the fast and slow checks and Enact's worktree hooks.

### Frozen prototypes

These stay in the tree for reference, outside CI. Nothing new builds on
them, and a change elsewhere may break them unnoticed.

- `native/`: the native Rust game, a 2D survivors run that listens to any
  app's music, with its content (`content/`, `levels/`, `public/`, `corpus/`,
  `songs/`) and tools (`tools/analysis/`, `tools/baker/`, `assets-src/`).
- `game/`: the first Unity game, native's run in 3D.
- `demo3d/`, `demo2d/`, `stylelab/`, `herocap/`: the Unity tech demos, the
  style lab and the HeroEditor capture.
- `unity-packages/`: the packages those projects share.

Their designs are under [docs/archive/](docs/archive/README.md).

## Setup and development

[SETUP.md](SETUP.md) covers the tools and the vendor assets;
[AGENTS.md](AGENTS.md) defines the workflow every change follows.

## Licences

Code is under the Apache License 2.0 (`LICENSE`). The analyzer's neural
network weights come from madmom under CC BY-NC-SA 4.0
([native/models/README.md](native/models/README.md)), which suits a free,
non-commercial project. The Unity projects use commercial vendor assets
(Synty, Magic Pig Games, Hovl Studio) that each machine supplies locally and
that are never committed, nor anything derived from them
([vendor rules](docs/archive/tech-demo-3d/vendor-assets.md)). The frozen
native game's art and sound come from CC0, CC-BY, OGA-BY and CC-BY-SA packs
(`public/assets/credits.json`).
