# Setup Guide

Developing [Rhythm Champions](docs/champions/design.md) on an Apple Silicon
Mac, the only platform V1 supports.

- [Tools](#tools)
- [Vendor assets](#vendor-assets)
- [Building and playing](#building-and-playing)
- [Checks and delivery](#checks-and-delivery)
- [Earlier Hive beads](#earlier-hive-beads)
- [Local data](#local-data)
- [Frozen prototypes](#frozen-prototypes)

## Tools

| Tool | Install | Used for |
| --- | --- | --- |
| Xcode Command Line Tools | `xcode-select --install` | Compilers and the macOS SDK |
| Rust | [rustup](https://rust-lang.org/tools/install/) | The audio plugin; its `rust-toolchain.toml` pins the version |
| Unity 6000.6.7f1 | Unity Hub, at `/Applications/Unity/Hub/Editor/6000.6.7f1`, with Mac build support (Mono and IL2CPP) and an activated licence | The game |
| uv | `brew install uv` | The `champions` tools (Python) |
| .NET SDK 10 | `brew install --cask dotnet-sdk` | The sim's tests outside Unity |
| just | `brew install just` | The project's commands (`just` lists them) |
| Blender 5.2 LTS | blender.org, at `/Applications/Blender.app` | Edge-cut bakes, run headless |
| Git, Python 3.11+ | Xcode's tools, or Homebrew | Everything else |

The foundation bead (RC1 in the [plan](docs/champions/plan.md#beads)) adds
the shared Unity Accelerator's setup here.

## Vendor assets

The game is drawn from commercial Unity Asset Store and Synty packs that
each machine supplies; the repository is public, so neither they nor
anything derived from them (edge cuts, bakes, Blender files, exports) is
ever committed.

- The packs live outside the tree: Synty's in `~/synty`, the Asset Store's
  (Magic Pig Games monsters, Hovl Studio effects and more) in
  `~/unity_assets/UnityAssets`.
- `rhythm_champions/manifest.json` lists every vendor file the game uses, by
  root, path, SHA-256 and GUID. `champions setup` copies them into the
  project's ignored `Assets/Vendor/`; bakes write the ignored
  `Assets/Baked/`.
- A guard refuses any vendor or derived file in a commit. The rules:
  [vendor assets](docs/archive/tech-demo-3d/vendor-assets.md).

## Building and playing

`just champions` sets up, builds (Mono) and plays at 1080p, taking player
flags such as `--file <song>` or `--app`. It arrives with the epic's
foundation bead, which replaces this paragraph with the full steps; until
then there is nothing to build here.

## Checks and delivery

Every change goes through an Enact worktree and CI, as
[AGENTS.md](AGENTS.md) describes.
[Enact](https://github.com/thurn/enact) needs Git, Python 3.11+,
[Beads](https://github.com/gastownhall/beads) 1.2+ and the `dolt` CLI.

```sh
git clone https://github.com/thurn/enact.git "$HOME/enact"
python3 "$HOME/enact/scripts/enact.py" install
cd <your clone>
python3 "$HOME/enact/scripts/enact.py" setup
```

Deliver a change from a worktree:

```sh
python3 "$HOME/enact/scripts/enact.py" worktree create my-change
# Enter the printed worktree path, make the change, then:
git add <changed-files>
git commit -m "fix: describe the change"
python3 "$HOME/enact/scripts/enact.py" push
```

`enact push` rebases onto `origin/staging`, runs the fast check
(`scripts/check-fast`), pushes to `staging` and starts slow CI
(`scripts/ci-full`) in the background; a pass moves `master`. Never commit
or push to `master` or `staging` directly.

The plan's CI bead replaces both scripts' prototype sections with Rhythm
Champions' own: check-fast within 60 s, slow CI within 300 s.

## Earlier Hive beads

Work before Enact (the native app epic `hv-t8o8`, the survivors run
`hv-n7fn`) was tracked in Hive's private Beads store (`~/brain/.beads` on the
maintainer's Mac), which this repository does not include. New work is
filed in this repository's Beads database (prefix `ps`), which Enact syncs
through `refs/dolt/data` on `origin`.

## Local data

| Data | Where |
| --- | --- |
| Caches (Unity Libraries, bakes, Cargo targets, CI state) | `~/Library/Caches/playlist-survivors/` (`PS_CACHE` overrides it) |
| Vendor packs | `~/synty`, `~/unity_assets/UnityAssets` |
| Captures | the tree's `captures/` (ignored) |
| The game's sessions, logs and calibration | the player's data folder, as the foundation bead sets it |

## Frozen prototypes

`native/`, `game/`, `demo3d/`, `demo2d/`, `stylelab/`, `herocap/` and their
tools are frozen outside CI and may no longer build. Their setup guide is
archived: [setup-prototypes.md](docs/archive/setup-prototypes.md). The
original TypeScript game is at the `ts-freeze` tag.
