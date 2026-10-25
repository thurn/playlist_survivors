# Playlist Survivors

A rhythm survivors game for macOS that plays to whatever music the Mac is playing. It listens to
the system's audio (or plays a file and listens to that), finds the beat as it goes, and you press
the arrow keys (or WASD) on it: the hero strides on the beat, the sword and every weapon fire by
themselves on it, growing with the tier your groove reaches, through a horde of LPC monsters, and
every strike lands on the music.

The design is [docs/design/native-app.md](docs/design/native-app.md). The game began as a TS web
game; the `ts-freeze` tag keeps it (see [The TS game](#the-ts-game)).

## Running it

Requirements: the Rust toolchain pinned in `native/rust-toolchain.toml` (rustup installs it),
[uv](https://docs.astral.sh/uv/) with Python 3.12 for the analysis tool, and Node 24+ only to
bake assets.

```bash
native/scripts/bundle
```

builds the signed `PlaylistSurvivors.app` (System Audio Recording needs one click the first time).
From `native/`, `cargo run --release -p app -- <flags>` runs it unbundled:

| Flags | What |
|---|---|
| (none) | System mode: whatever the Mac is playing |
| `--file <path>` | plays the file and listens to it |
| `--click` | also clicks on each committed beat |
| `--delayed [--app <name>]` | Delayed listening: the music heard 3 s late, beats from hindsight; in System mode the named app is tapped, muted while tapped, and played late |
| `--mute`, `--volume <0-1>` | everything runs, the device gets silence or less |
| `--headless --seconds <n> [--press-beats] [--watch]` | no window, no Dock icon, muted, on a timer, for checks |
| `--still <png> [--still-track <id> --still-at <s>]` | a still of a bundled track at a time, offscreen |
| `--bench [--check]` | the frame's work over 60 s, offscreen |

In the app: arrows or WASD stride, Space stomps, Esc or Backspace ends the run, Enter or Space
starts the next; C and V calibrate input and video; W watches your run; F1 is the debug menu (F2
plays the next bundled track, F3 listens to the Mac), F8 saves the timing log.

The `ps` CLI (`cargo run --release -p cli -- <command>`) summarises sessions and timing logs
(`ps log`, `ps report`), replays a recorded session (`ps replay`), and runs the analyzer's corpus
parity (`ps analysis-parity`) and the runner's benchmark (`ps runner-bench`).

## Content and tools

- `content/`, `levels/`: the game's content (enemies, weapons, effects, zones) and levels, read by
  the sim.
- `public/assets/`: the baked atlases and sounds. `npm ci && npm run bake` rebakes them from
  `assets-src/` (`tools/baker`); `npm run check:assets` checks them; `npm run bake:contact-sheet`
  draws contact sheets to review.
- `public/reactive/`: the bundled tracks and their belief streams; `corpus/manifest.yaml` lists the
  feel corpus. The analysis tool (`tools/analysis`, `uv run --project tools/analysis ps-analysis
  <command>`) writes them: `corpus-add` ingests the manifest's tracks, `streams` regenerates every
  stream and the index, `fit-corpus` fits the reference grids; `stream`, `fit-grid`, `synth`,
  `encode` work on one track; `export-models` exports the analyzer's models and golden data for
  `native/`.
- `songs/`: the Galop's authored song map (`ps-analysis check` validates it).

## Checks

Validation is staged (AGENTS.md): `scripts/check-fast` before submitting (diff-aware, about a
minute), `scripts/ci-gate` in Tollgate's gate stage, and `scripts/ci-full` (`baker`, `analysis`,
`native`) in its release stage. From `native/`, `cargo test -p <crate>` and
`cargo clippy -p <crate> --all-targets -- -D warnings` while iterating.

## The TS game

The web game (`src/`, its tests, the Vite and Playwright setup and its scripts) was removed in D6.
The `ts-freeze` tag keeps it reproducible, and with it the oracle fixtures in `native/oracle/` and
the generators in `native/crates/sim/tests/data/`: check it out in a worktree
(`git worktree add ../ts ts-freeze`), `npm ci` there, and run the scripts its README describes
(`npm run oracle:export` for the fixtures).

## Licences

Code is under the Apache License 2.0 (`LICENSE`). Art and sound come from CC0, CC-BY, OGA-BY and
CC-BY-SA packs; see `public/assets/credits.json`. Baked atlas pages that contain share-alike art
are CC-BY-SA. The corpus tracks' licences are in `corpus/manifest.yaml`. The development recording
of the Galop is tagged CC0 on Wikimedia Commons but its status is unverified: it is fine for a
private build and must be replaced before any public release.
