# Playlist Survivors

A rhythm survivors game for macOS that plays to whatever music the Mac is playing. It listens to
the system's audio (or plays a file and listens to that) and finds the beat as it goes. You hold
the arrow keys (or WASD) and the hero hops on the beat, while the sword and every weapon fire by
themselves on it through a horde of LPC monsters, and every strike lands on the music.

This is **zen mode**, the default: nothing is judged, every move lands on the beat, and the groove
stays full at the top tier. **Legacy mode** (`--legacy`, or Z between runs) is the TS game's
original rules, where you press on the beat, each press is rated and the groove rises and drains
with your timing. Legacy mode is kept for older sessions and the sim's tests, but is no longer
developed.

The design is [docs/design/native-app.md](docs/design/native-app.md). The game began as a TS web
game; the `ts-freeze` tag keeps it (see the [setup guide](SETUP.md#local-data-and-troubleshooting)).

## Setup

See the [complete setup guide](SETUP.md) for cloning and playing on a new Mac, development
tools, fetching art sources, and optional Enact configuration.

## Playing

In the app: arrows or WASD stride, Space stomps, Esc or Backspace ends the run, Enter or Space
starts the next; Z switches zen and legacy mode between runs; C and V calibrate input and video; W
watches your run; F1 is the debug menu (F2 plays the next bundled track, F3 listens to the Mac), F8
saves the timing log.

## Content and tools

- `content/`, `levels/`: the game's content (enemies, weapons, effects, zones) and levels, read by
  the sim.
- `public/assets/`: the baked atlases and sounds; `assets-src/` contains their recipes. See the
  [asset setup instructions](SETUP.md#asset-baker-and-art-sources) for rebaking and checking them.
- `public/reactive/`: the bundled tracks and their belief streams; `corpus/manifest.yaml` lists the
  feel corpus. The analysis tool (`tools/analysis`, `uv run --project tools/analysis ps-analysis
  <command>`) writes them: `corpus-add` ingests the manifest's tracks, `streams` regenerates every
  stream and the index, `fit-corpus` fits the reference grids; `stream`, `fit-grid`, `synth`,
  `encode` work on one track; `export-models` exports the analyzer's models and golden data for
  `native/`.
- `songs/`: the Galop's authored song map (`ps-analysis check` validates it).

## Development

The [setup guide](SETUP.md#development-tools-and-checks) covers checks and delivery;
[AGENTS.md](AGENTS.md) defines the maintained project's workflow. The original TypeScript game
is preserved at `ts-freeze`; see [its setup instructions](SETUP.md#local-data-and-troubleshooting).

## Licences

Code is under the Apache License 2.0 (`LICENSE`). Art and sound come from CC0, CC-BY, OGA-BY and
CC-BY-SA packs; see `public/assets/credits.json`. Baked atlas pages that contain share-alike art
are CC-BY-SA. The corpus tracks' licences are in `corpus/manifest.yaml`. The development recording
of the Galop is tagged CC0 on Wikimedia Commons but its status is unverified: it is fine for a
private build and must be replaced before any public release.
