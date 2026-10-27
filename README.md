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
| `--legacy` | legacy mode: presses judged on the beat (zen mode is the default) |
| `--delayed [--app <name>]` | Delayed listening: the music heard 3 s late, beats from hindsight; in System mode the named app is tapped, muted while tapped, and played late |
| `--mute`, `--volume <0-1>` | everything runs, the device gets silence or less |
| `--headless --seconds <n> [--press-beats] [--watch]` | no window, no Dock icon, muted, on a timer, for checks |
| `--still <png> [--still-track <id> --still-at <s>]` | a still of a bundled track at a time, offscreen |
| `--bench [--check]` | the frame's work over 60 s, offscreen |

In the app: arrows or WASD stride, Space stomps, Esc or Backspace ends the run, Enter or Space
starts the next; Z switches zen and legacy mode between runs; C and V calibrate input and video; W
watches your run; F1 is the debug menu (F2 plays the next bundled track, F3 listens to the Mac), F8
saves the timing log.

The `ps` CLI (`cargo run --release -p cli -- <command>`) summarises sessions and timing logs
(`ps log`, `ps report`), replays a recorded session (`ps replay`), and runs the analyzer's corpus
parity (`ps analysis-parity`) and the runner's benchmark (`ps runner-bench`).

## Content and tools

- `content/`, `levels/`: the game's content (enemies, weapons, effects, zones) and levels, read by
  the sim.
- `public/assets/`: the baked atlases and sounds. `npm ci && npm run bake` rebakes them from
  `assets-src/` (`tools/baker`); `npm run check:assets` checks them; `npm run bake:contact-sheet`
  draws contact sheets to review. Rebaking also needs the [external art sources](#fetching-art-sources).
- `public/reactive/`: the bundled tracks and their belief streams; `corpus/manifest.yaml` lists the
  feel corpus. The analysis tool (`tools/analysis`, `uv run --project tools/analysis ps-analysis
  <command>`) writes them: `corpus-add` ingests the manifest's tracks, `streams` regenerates every
  stream and the index, `fit-corpus` fits the reference grids; `stream`, `fit-grid`, `synth`,
  `encode` work on one track; `export-models` exports the analyzer's models and golden data for
  `native/`.
- `songs/`: the Galop's authored song map (`ps-analysis check` validates it).

## Fetching art sources

The baked assets, analyzer models and sample music are committed: a fresh clone can build and
play the native game without fetching art sources or running the baker. Fetch these sources
only when changing or rebaking assets. They stay outside this repository.

The baker needs four LPC collections. These snapshots match the source versions used for the
current recipes; download archives rather than cloning their Git histories:

| Directory under `LPC_ROOT` | Source version |
|---|---|
| `ulpc-generator` | [Universal LPC generator](https://github.com/LiberatedPixelCup/Universal-LPC-Spritesheet-Character-Generator), `9bf06be193b053db256b2536824af426de046aa2` |
| `lpc-revised` | [LPC Revised](https://github.com/ElizaWy/LPC), `f07f7f5892e67c932c68f70bb04472f2c64e46bc` |
| `source-of-tales` | [Source of Tales](https://gitlab.com/tales/sourceoftales), `ce4457dfd9da49be9daf54098b15ac73ed830fd0` |
| `lpc-tile-atlas/files` | [LPC Tile Atlas](https://opengameart.org/content/lpc-tile-atlas), `Atlas_0.zip` (checksum below) |

On macOS, run the following in Bash. Use a new, empty `LPC_ROOT`; its default is
`~/Documents/lpc`. If that directory already holds your collections, choose another directory
for a fresh download. Keep the same `LPC_ROOT` exported when running the baker, including in
later shells if you chose a non-default location.

```bash
(
  set -eu
  export LPC_ROOT="${LPC_ROOT:-$HOME/Documents/lpc}"
  mkdir -p "$LPC_ROOT"

  fetch_lpc() {
    name="$1"
    url="$2"
    # Refuse to overwrite an existing collection.
    test ! -e "$LPC_ROOT/$name"
    curl --fail --location --retry 3 "$url" -o "$LPC_ROOT/$name.tar.gz"
    mkdir "$LPC_ROOT/$name"
    tar -xzf "$LPC_ROOT/$name.tar.gz" -C "$LPC_ROOT/$name" --strip-components=1
    rm "$LPC_ROOT/$name.tar.gz"
  }

  fetch_lpc ulpc-generator \
    https://codeload.github.com/LiberatedPixelCup/Universal-LPC-Spritesheet-Character-Generator/tar.gz/9bf06be193b053db256b2536824af426de046aa2
  fetch_lpc lpc-revised \
    https://codeload.github.com/ElizaWy/LPC/tar.gz/f07f7f5892e67c932c68f70bb04472f2c64e46bc
  fetch_lpc source-of-tales \
    https://gitlab.com/tales/sourceoftales/-/archive/ce4457dfd9da49be9daf54098b15ac73ed830fd0/sourceoftales-ce4457dfd9da49be9daf54098b15ac73ed830fd0.tar.gz

  test ! -e "$LPC_ROOT/lpc-tile-atlas"
  curl --fail --location --retry 3 \
    https://opengameart.org/sites/default/files/Atlas_0.zip -o "$LPC_ROOT/Atlas_0.zip"
  (cd "$LPC_ROOT" && printf '%s  Atlas_0.zip\n' \
    4b8d0030c4e5c18029a0298caf11c74d38eb10273ac44e364ef620b81bcaa5fd | shasum -a 256 -c -)
  mkdir -p "$LPC_ROOT/lpc-tile-atlas/files"
  unzip -q "$LPC_ROOT/Atlas_0.zip" -d "$LPC_ROOT/lpc-tile-atlas/files"
  rm "$LPC_ROOT/Atlas_0.zip"
)
```

These are mixed-license collections, not all CC0. Keep their upstream credit and license files;
the baker selects allowed licenses through `assets-src/licences.yaml` and the generator's
`CREDITS.csv`, then writes `public/assets/credits.json`. The tile atlas includes unreviewed
regions: the current recipes use only the reviewed `#ruins` region.

To rebake, install Node 24+ and FFmpeg, then run from the repository root:

```bash
npm ci
FFMPEG="$(command -v ffmpeg)" npm run bake
npm run check
```

The baker automatically downloads and verifies the other packs listed in
`tools/baker/sources.json`. Those downloads are cached in `~/.cache/playlist-survivors`
(`PS_CACHE` overrides it); the first bake needs internet access. Review any resulting asset and
credit changes before committing them. If a fetch stops partway through, remove only that
incomplete collection before retrying its command; the block refuses existing collections.

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
