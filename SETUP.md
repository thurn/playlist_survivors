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
- [Tollgate (optional)](#tollgate-optional)
- [Hive and Beads (optional)](#hive-and-beads-optional)
- [Local data and troubleshooting](#local-data-and-troubleshooting)

## Build and play

Only Git, Apple's build tools and Rust are needed to build and play. The repository includes
the baked graphics, sounds, analyzer models and sample music. Python, Node, external LPC
sources, Tollgate and Hive are optional for this path.

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

`scripts/ci-gate` is the promotion check; `scripts/ci-full` runs the complete release suite.
Run the full suite locally too when changing CI scripts, Tollgate policy, workspace-wide
configuration or shared test infrastructure. It needs the Python and Node toolchains below,
but does not rebake art. Native checks use the GPU offscreen and need a usable macOS graphics
session. Use headless/offscreen checks during routine development to avoid opening a window.

Each worktree uses its own `native/target` by default. Never point two development worktrees at
one Cargo target directory. Tollgate manages separate persistent caches for its gate and
release steps under `$HOME/Library/Caches/playlist-survivors/`.

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

## Tollgate (optional)

Tollgate is the local macOS validation and delivery service used by this project's maintained
workflow. You can build, play and edit without it; delivery to this project's `staging` branch
follows [AGENTS.md](AGENTS.md). Hive is not required to use Tollgate.

### Install and register

Install Node as above and Rust stable for Tollgate's own build, then follow the
[Tollgate installation instructions](https://github.com/thurn/tollgate#install-a-local-checkout).
For a source installation, choose any directory outside this repository:

```sh
mkdir -p "$HOME/src"
git clone https://github.com/thurn/tollgate.git "$HOME/src/tollgate"
(cd "$HOME/src/tollgate" && ./scripts/install-local.sh)
export PATH="$HOME/.local/bin:$PATH"
tg --version
```

The installer builds the desktop app and CLI, installs the app to `/Applications/Tollgate.app`,
and links `tg` into `~/.local/bin`. Set `TOLLGATE_INSTALL_DIR` to an existing writable
Applications directory if needed. Keep `~/.local/bin` on PATH in your login shell, along with
Cargo, uv, Python 3.12, Node/npm and cargo-machete. Configure the Python and Node sections first:
the release suite uses both even when your own work is Rust-only.

In a fresh main checkout, register the existing policy and inspect the result:

```sh
cd "$PS_ROOT"
tg init
tg config validate
tg env show
tg doctor
```

`tg init` uses the committed `.tollgate/config.toml`; do not replace it with a generated
single-step example. It creates local `staging` and `release` refs and local runtime state.
The policy's Cargo cache paths expand `$HOME` in the step commands and require no username
edits. `tg env reload` refreshes the service's login-shell environment after installing tools
or changing PATH. Existing configured installations can simply run `tg doctor` and
`tg config validate` rather than initialize again.

The tracked policy enables remote publishing. To publish from this Mac, configure Git
credentials and permission to push to the configured remote. On a second machine, choose
which Mac publishes; keep one publisher for the shared remote. A local-only installation can
set `[remote] enabled = false` in its local policy before registration, following Tollgate's
configuration instructions; keep that local override out of feature commits. Do not copy
another machine's `.git/tollgate` state, sockets, build caches or worktrees.

### Deliver a change

Create one fresh worktree per prompt or bead, use the path printed by Tollgate, then validate
and commit there with a Conventional Commit message:

```sh
cd "$PS_ROOT"
tg worktree create my-change
# Enter the worktree path printed above, make the change, then:
scripts/check-fast
git add <changed-files>
git commit -m "fix: describe the change"
tg candidate HEAD
```

Copy the returned candidate ID and run approval from the main checkout, because promotion can
remove the clean worktree:

```sh
cd "$PS_ROOT"
tg approve <candidate-id> --wait
```

A successful gate promotes to `staging`: the work is landed. The release suite runs
asynchronously and Tollgate publishes a passing `release` to remote `master`. Do not wait for
release to finish a bead. Make changes in feature worktrees; Tollgate owns publishing to
master. Do not use `tg push-master` for this project.
Consult `tg status` for blocked work and the project's CI-fix rules in AGENTS.md for failures.

## Hive and Beads (optional)

Hive adds bead-based task selection, agent roles and local observations. It is not needed to
build, play, run tests, or use Tollgate directly. The task database is separate from this public
repository; cloning the game does not copy work items or thread history.

### Install Hive and its task tools

Follow [Hive's current setup instructions](https://github.com/thurn/hive#configure). Its current
supported task stack is Python 3.12, [Beads 1.2.2](https://github.com/gastownhall/beads/releases/tag/v1.2.2)
in server mode, [Dolt 2.2.0](https://github.com/dolthub/dolt/releases/tag/v2.2.0), and configured
Tollgate. Install the macOS ARM64 binaries from those releases on PATH, then verify
`bd --version` and `dolt version`. Use Hive's upstream pins if they change together.

```sh
git clone https://github.com/thurn/hive.git "$HOME/src/hive"
export PATH="$HOME/src/hive/bin:$HOME/.local/bin:$PATH"
hive --help
```

Hive's launcher runs the committed source from its checkout; it does not require installing
its Python package globally. `HIVE_PYTHON` can name your Python 3.12 executable if it is not
available as `python3.12`.

### Select the task store and configure this checkout

To continue existing project work, restore the private Beads store using
[Hive's backup/restore and remote-sync instructions](https://github.com/thurn/hive/blob/master/docs/operations.md).
Restore into a fresh destination and verify its database identity before enabling writers.
The maintained project uses a `.beads` directory inside a separate `brain` repository; its
location on your Mac is configurable. Do not initialize an empty replacement for existing
project work or put private task data in the game's repository. Keep one publishing machine
for private-store sync as well.

For an independent new task store instead, initialize a separate directory with
`bd init --server --prefix hv --skip-agents --skip-hooks` and explicitly arrange its Dolt
server using Beads' server setup instructions. Hive requires server-mode `.beads/metadata.json`
and never initializes or starts a replacement database itself.

Create a local bootstrap JSON with resolved paths. This example assumes Hive was cloned above
and that the separate store is at `~/brain`; change `PS_BEADS_ROOT` to your restored or newly
prepared store. Use a new config file, or merge the project into your existing configuration
rather than overwriting it:

```sh
export PS_BEADS_ROOT="$HOME/brain"
export HIVE_BOOTSTRAP_CONFIG="$HOME/.config/playlist-survivors/hive.json"
python3.12 - <<'PYCONFIG'
import json
import os
from pathlib import Path

root = Path(os.environ["PS_ROOT"]).resolve()
store = Path(os.environ["PS_BEADS_ROOT"]).resolve()
assert (store / ".beads/metadata.json").is_file(), "Prepare the server-mode Beads store first"
config = Path(os.environ["HIVE_BOOTSTRAP_CONFIG"])
config.parent.mkdir(parents=True, exist_ok=True)
with config.open("x") as output:
    json.dump({
        "repository": str(Path.home() / "src/hive"),
        "state": str(Path.home() / ".local/state/hive"),
        "beads": str(store),
        "projects": [{
            "id": "playlist_survivors",
            "repository": str(root),
            "invariants": str(root / "AGENTS.md"),
        }],
    }, output, indent=2)
    output.write("\n")
PYCONFIG
hive source --json
```

Keep `HIVE_BOOTSTRAP_CONFIG`, Hive's PATH and any `HIVE_PYTHON` setting in the environment of
every shell or agent using this setup. Hive does not expand `$HOME` strings inside JSON;
the example writes actual paths on the current machine. Verify the native Beads route before
claiming anything:

```sh
BEADS_DIR="$PS_BEADS_ROOT/.beads" BEADS_DOLT_AUTO_START=0 \
  BD_NON_INTERACTIVE=1 BD_NO_HOOKS=true \
  bd --sandbox --dolt-auto-commit off context --json
```

Compare the reported store, database and server with the intended restored store. Follow
Hive's role/routing instructions for claims and actual agent session IDs. Its skills can be
installed with `"$HOME/src/hive/scripts/install-skills"`; review existing skill-name conflicts
first. Collectors, dashboards and executor hooks are opt-in; follow Hive's operations guide
if you want them. They are not prerequisites for game development.

## Local data and troubleshooting

| Data or setting | Default / behavior |
|---|---|
| Cargo output and app bundle | `native/target/` in each checkout; `CARGO_TARGET_DIR` overrides it |
| LPC sources | `~/Documents/lpc`; `LPC_ROOT` overrides it |
| Downloaded asset packs | `~/.cache/playlist-survivors`; `PS_CACHE` overrides it |
| Listening-session recordings | `native/logs/sessions/` in the checkout; `PS_SESSIONS` overrides it |
| Analyzer models | Committed `native/models/madmom.bin`; `PS_MODELS` overrides it |
| Calibration | Local user settings; recalibrate on a new display/audio setup |
| Tollgate / Hive runtime data | Machine-local; configure afresh rather than copying live state |

- **A tool is missing in Tollgate:** put it on the login-shell PATH and run `tg env reload`.
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

The [Unity 3D tech demo design](docs/tech-demo-3d/README.md) is a separate project; its paid
vendor assets are not part of this setup and must never be committed. The commands in this
guide build the native Rust game.
