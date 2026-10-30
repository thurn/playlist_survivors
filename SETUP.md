# Setup guide

This guide covers cloning, playing and developing Playlist Survivors on an Apple Silicon Mac.
The native app uses macOS audio APIs; Windows, Linux and Intel Macs are outside its current
supported scope. The app declares macOS 14.2 as its minimum; use current Xcode Command Line
Tools and an SDK compatible with the pinned Rust toolchain. The Homebrew route below requires
[a macOS version Homebrew supports](https://docs.brew.sh/Installation).

- [Build and play](#build-and-play)
- [Development tools and checks](#development-tools-and-checks)
- [Python analysis tool](#python-analysis-tool)
- [Asset baker and art sources](#asset-baker-and-art-sources)
- [Enact (optional)](#enact-optional)
- [Earlier Hive beads](#earlier-hive-beads)
- [Local data and troubleshooting](#local-data-and-troubleshooting)

## Build and play

Only Git, Apple's build tools and Rust are needed to build and play. The repository includes
the baked graphics, sounds, analyzer models and sample music. Python, Node, external LPC
sources and Enact are optional for this path.

### Install the build tools

Install Apple's Command Line Tools, finish the installer, then verify the selected tools:

```sh
xcode-select --install
xcrun --show-sdk-path
git --version
```

Install Rust using the [official rustup installer](https://rust-lang.org/tools/install/):

```sh
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
. "$HOME/.cargo/env"
```

Use rustup rather than a standalone Cargo installation: `native/rust-toolchain.toml` selects
Rust 1.98.1 with rustfmt and Clippy. Cargo downloads that toolchain on first use.

### Clone and launch with included music

Choose any parent directory, then run:

```sh
git clone https://github.com/thurn/playlist_survivors.git
cd playlist_survivors
export PS_ROOT="$PWD"
cd "$PS_ROOT/native"
cargo run --locked --release -p app -- --playlist playlists/survivors-30.m3u --delayed
```

The first build downloads Rust dependencies and compiles the game. `--delayed` uses hindsight
beat analysis and plays the music three seconds late. File/playlist playback does not need
System Audio Recording permission. Arrows or WASD move, Space stomps, and Enter starts a run.
See the [README](README.md) for the remaining controls.

Commands below use `PS_ROOT` for your clone, not a prescribed checkout location. In a later
shell, enter the clone and run `export PS_ROOT="$PWD"` again.

### Listen to system audio

From the repository root, build and open the signed app:

```sh
cd "$PS_ROOT"
native/scripts/bundle
open native/target/PlaylistSurvivors.app
```

Start music in another app and grant System Audio Recording when macOS asks. If necessary,
check System Settings → Privacy & Security → Screen & System Audio Recording (the label varies
by macOS version), enable Playlist Survivors, then relaunch it. Silence from an unbundled
system-audio run is not a reliable permission test.

The bundle script creates a local self-signed identity named `Playlist Survivors Dev` in your
login keychain once and reuses it. No Apple developer account or certificate transfer from
another machine is needed. `PS_SIGNING_IDENTITY` overrides the identity name. Keep the login
keychain unlocked when building. With `CARGO_TARGET_DIR` set, the bundle is created there
instead of `native/target`; use an absolute target path if overriding it.

### Launch options

The repository's `justfile` wraps the game, its checks and the Unity demos' tools for the
current checkout, passing extra arguments through: `just native --file <path>`,
`just demo3d-build --backend mono`, `just demo3d-play --start-at 160`. Run `just` to list them
([install just](https://just.systems/man/en/packages.html), or `brew install just`).

From `native/`, run `cargo run --locked --release -p app -- <flags>`:

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


The `ps` CLI (`cargo run --release -p cli -- <command>`) summarises sessions and timing logs
(`ps log`, `ps report`), replays a recorded session (`ps replay`), and runs the analyzer's corpus
parity (`ps analysis-parity`) and the runner's benchmark (`ps runner-bench`).

## Development tools and checks

For Rust development, install the additional command used by the checks:

```sh
cd "$PS_ROOT/native"
cargo install --locked cargo-machete
cargo test --locked -p sim
```

Run focused tests for the crate you changed. From the repository root, run
`scripts/check-fast` once on the final tree before submitting. It checks only the affected
areas, so a pass on an unchanged checkout does not exercise a fresh installation.

For a windowless, muted playback smoke check:

```sh
cd "$PS_ROOT/native"
cargo run --locked --release -p app -- \
  --file ../public/reactive/house-128/house-128.ogg --headless --seconds 10
```

`scripts/check-fast` is the promotion check; `scripts/ci-full` runs the complete suite.
Run the full suite locally too when changing CI scripts, `.enact.toml`, workspace-wide
configuration or shared test infrastructure. It needs the Python and Node toolchains below,
but does not rebake art. Native checks use the GPU offscreen and need a usable macOS graphics
session. Use headless/offscreen checks during routine development to avoid opening a window.

Each worktree uses its own `native/target` by default. Never point two development worktrees at
one Cargo target directory. Enact's slow CI, serialized by its lock, keeps a persistent cache
under `$HOME/Library/Caches/playlist-survivors/`.

`scripts/ci-full demo2d` also runs the 2D demo's Unity EditMode tests, windowless, so the
machine running slow CI needs what `demo2d setup` needs ([2D demo](docs/tech-demo-2d/README.md)):
Unity 6000.6.7f1 at its Hub path (or `UNITY_EXE`) with an activated licence, the vendor packs
in `~/unity_assets` and the Action MP3 in `~/Downloads` (or the paths in
`~/Library/Caches/playlist-survivors/demo3d/roots.json`). Without them the step fails, naming
what is missing.

## Python analysis tool

Skip this section unless changing the analysis tool, regenerating streams/models, or running
its checks. Install [uv](https://docs.astral.sh/uv/getting-started/installation/) and a system
Python 3.12. For example, after installing [Homebrew](https://brew.sh/):

```sh
brew install uv python@3.12 libsndfile ffmpeg
python3.12 --version
cd "$PS_ROOT"
uv sync --locked --project tools/analysis --python "$(command -v python3.12)"
uv run --project tools/analysis ps-analysis --help
uv run --directory tools/analysis pytest -q
```

The project explicitly selects system Python (`python-preference = "only-system"`), so an
installation containing only uv-managed Python is insufficient. Let uv use the committed lock
file; it also fetches/builds madmom. Keep Apple's compiler tools installed for native Python
extensions. The exported models are already in `native/models/`: do not regenerate them just
to play. Corpus/source commands are listed by `ps-analysis --help`.

## Asset baker and art sources

Skip this section unless changing or rebaking assets. Install Node 24+ and FFmpeg; this
Homebrew example selects Node 24 without assuming Homebrew's installation prefix:

```sh
brew install node@24 ffmpeg
export PATH="$(brew --prefix node@24)/bin:$PATH"
node --version
npm --version
ffmpeg -version
```

Repeat the PATH setting in new shells, or add it to your shell startup file. Fetch the LPC
sources below before running `npm run bake`; ordinary Rust development uses the committed
outputs and does not need them.

### Fetching art sources


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

To rebake, run from the repository root after installing the tools above:

```bash
npm ci
FFMPEG="$(command -v ffmpeg)" npm run bake
npm run check
npm run bake:contact-sheet
```

The last command draws contact sheets for visual review.

The baker automatically downloads and verifies the other packs listed in
`tools/baker/sources.json`. Those downloads are cached in `~/.cache/playlist-survivors`
(`PS_CACHE` overrides it); the first bake needs internet access. Review any resulting asset and
credit changes before committing them. If a fetch stops partway through, remove only that
incomplete collection before retrying its command; the block refuses existing collections.

## Enact (optional)

[Enact](https://github.com/thurn/enact) is the delivery and agent orchestration workflow used
by this project's maintainers. You can build, play and edit without it; delivery to this
project's `staging` branch follows [AGENTS.md](AGENTS.md). It needs Git, Python 3.11+,
[Beads](https://github.com/gastownhall/beads) 1.2+ and the `dolt` CLI.

```sh
git clone https://github.com/thurn/enact.git "$HOME/enact"
python3 "$HOME/enact/scripts/enact.py" install
cd "$PS_ROOT"
python3 "$HOME/enact/scripts/enact.py" setup
```

`.enact.toml` holds the fast check (`scripts/check-fast`) and the slow check (`scripts/ci-full`,
with a persistent Cargo target directory). Its worktree hooks (`scripts/enact-worktree`) clone a
warm Cargo target into each new worktree. Deliver a change from a worktree:

```sh
cd "$PS_ROOT"
python3 "$HOME/enact/scripts/enact.py" worktree create my-change
# Enter the printed worktree path, make the change, then:
git add <changed-files>
git commit -m "fix: describe the change"
python3 "$HOME/enact/scripts/enact.py" push
```

`enact push` rebases onto `origin/staging`, runs the fast check, pushes to `staging` and starts
slow CI in the background; a pass moves `master`. Never commit or push to `master` directly.

## Earlier Hive beads

Work before Enact (the native app epic `hv-t8o8`, the survivors run `hv-n7fn`) was tracked in
Hive's private Beads store (`~/brain/.beads` on the maintainer's Mac), which this repository
does not include. New work is filed in this repository's Beads database (prefix `ps`), which
Enact syncs through `refs/dolt/data` on `origin`.

## Local data and troubleshooting

| Data or setting | Default / behavior |
|---|---|
| Cargo output and app bundle | `native/target/` in each checkout; `CARGO_TARGET_DIR` overrides it |
| LPC sources | `~/Documents/lpc`; `LPC_ROOT` overrides it |
| Downloaded asset packs | `~/.cache/playlist-survivors`; `PS_CACHE` overrides it |
| Listening-session recordings | `native/logs/sessions/` in the checkout; `PS_SESSIONS` overrides it |
| Analyzer models | Committed `native/models/madmom.bin`; `PS_MODELS` overrides it |
| Calibration | Local user settings; recalibrate on a new display/audio setup |
| Enact / Hive runtime data | Machine-local; configure afresh rather than copying live state |

- **Python 3.12 is not found:** install a system Python and check `command -v python3.12`;
  uv-managed Python alone does not satisfy this project's setting.
- **FFmpeg is not found when baking:** ensure it is on PATH and use the documented `FFMPEG`
  override, which avoids assuming a Homebrew prefix.
- **System audio is silent:** use the signed bundle, play music, check the macOS permission,
  and relaunch. First verify file playback independently.
- **Art download stopped:** keep the pinned versions and retry only the incomplete collection
  as described above. Do not delete an existing source collection you still use.

The archived TypeScript game is available separately with
`git worktree add ../playlist-survivors-ts ts-freeze`. Install its dependencies with `npm ci`
inside that worktree and follow that version's README.

The Unity demos, [3D](docs/tech-demo-3d/README.md) and [2D](docs/tech-demo-2d/README.md),
are separate projects; their paid vendor assets are not part of this setup and must never be
committed. The commands in this
guide build the native Rust game.
