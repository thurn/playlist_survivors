# Rhythm Champions

A horde survivor rhythm game for the Mac. Build an arsenal of automatic
weapons, move through neon monsters and collect experience to grow stronger.
Attacks follow your music; on-beat dashes enlarge their next activation.
Bosses add rhythmic positioning and projectile-dodging challenges.

**Status: in development.** The Unity game in `rhythm_champions/` is playable
with `just champions`, but still implements the previous combat design.
The 2026-11-02 revision redesigns combat and progression; this documentation
pass does not implement it or change beads.

- [The design](docs/champions/design.md): the current game direction.
- [The plan](docs/champions/plan.md): retained technology and migration scope.
- [Archived combat docs](docs/archive/champions-dash-combat-2026-11-02/README.md):
  the superseded dash-centered design and its implementation map.

The project was called Playlist Survivors; the repository keeps that name.

## Layout

- `rhythm_champions/`: the game (Unity, with its Rust audio plugin, C# sim
  and Python tools); [its README](rhythm_champions/README.md) lists the
  commands.
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

[SETUP.md](SETUP.md) covers the tools, the vendor assets, building and
playing;
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
