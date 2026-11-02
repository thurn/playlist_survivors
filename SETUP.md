# Setup Guide

Developing [Rhythm Champions](docs/champions/design.md) on an Apple Silicon
Mac, the only platform V1 supports.

- [Tools](#tools)
- [Vendor assets](#vendor-assets)
- [Building and playing](#building-and-playing)
- [The Unity Accelerator](#the-unity-accelerator)
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

Optional: a local [Unity Accelerator](#the-unity-accelerator) that every
worktree and CI share.

## Vendor assets

The game is drawn from commercial Unity Asset Store and Synty packs that
each machine supplies; the repository is public, so neither they nor
anything derived from them (edge cuts, bakes, Blender files, exports) is
ever committed.

- The packs live outside the tree: Synty's in `~/synty`, the Asset Store's
  (Magic Pig Games monsters, Hovl Studio effects and more) in
  `~/unity_assets/UnityAssets`, and the Heroic Fantasy packs (the Fat Ogre)
  in `~/unity_assets/MoreAssets/MoreAssets`. Another machine maps its own
  folders in `~/Library/Caches/playlist-survivors/champions/roots.json`,
  which the first `champions setup` writes.
- `rhythm_champions/manifest.json` lists every vendor file the game uses, by
  root, path, SHA-256 and GUID. `champions setup` copies them into the
  project's ignored `Assets/Vendor/`; bakes write the ignored
  `Assets/Baked/`.
- A guard refuses any vendor or derived file in a commit. The rules:
  [vendor assets](docs/archive/tech-demo-3d/vendor-assets.md).

## Building and playing

In a worktree (or a checkout) with the tools above and the vendor packs in
place:

```sh
just champions
```

The first run sets the worktree up (`champions setup`): it syncs the
vendor files from `rhythm_champions/manifest.json` into `Assets/Vendor/`
(once per manifest into the cache, then an APFS clone), restores the baked
folder and a warm Library from the cache, builds the audio plugin, installs
the clone's pre-push guard, records every vendor file on the machine for the
guard, and opens the project once to check the Unity licence. A fresh
worktree with a warm cache sets up in seconds; the very first import takes
minutes. Then it builds the Mono player into
`rhythm_champions/Builds/mac/mono/RhythmChampions.app` and plays it
fullscreen at 1080p. Later runs rebuild only when a project file changed.

- Player flags follow: `just champions --windowed`, or windowless,
  `just champions --headless --seconds 5` (batch mode; it waits and exits
  with the player). [rhythm_champions/README.md](rhythm_champions/README.md)
  lists the flags and the commands.
- The single steps: `just champions-build` (`--backend il2cpp` for
  performance runs; Mono is the default), `just champions-play`.
- `just champions-tool editor` keeps a warm editor open for the worktree;
  build, test and the task methods go through it while it runs (it quits
  after 30 idle minutes, or with `editor stop`).
- `just champions-tool test` runs the EditMode tests; `just champions-tool
  check` runs everything before a push (the guard, the tests, the shader
  variant budget).

## The Unity Accelerator

One local Unity Accelerator serves every worktree and CI, so an asset one
worktree imported or a shader it compiled is a download for the next.

1. Install it from Unity's [Accelerator page](https://unity.com/products/unity-accelerator)
   (the macOS installer), as a service on this Mac, listening on its
   default port 10080.
2. Nothing else: every Unity launch of the tools (cold runs, the warm
   editor, CI) passes `-EnableCacheServer -cacheServerEndpoint
   127.0.0.1:10080` with downloads and uploads on whenever that port
   answers, and runs without it otherwise. `UNITY_ACCELERATOR=host:port`
   names another endpoint.
3. Check: a `champions build` log (`rhythm_champions/Logs/BuildPlayer.log`)
   mentions the connection to the Accelerator.

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
| The player's log | `~/Library/Logs/Rhythm Champions/Rhythm Champions/Player.log`; headless runs: `rhythm_champions/Logs/player.log` |
| The game's sessions and calibration | the player's data folder, `~/Library/Application Support/Rhythm Champions/Rhythm Champions/` |

## Frozen prototypes

`native/`, `game/`, `demo3d/`, `demo2d/`, `stylelab/`, `herocap/` and their
tools are frozen outside CI and may no longer build. Their setup guide is
archived: [setup-prototypes.md](docs/archive/setup-prototypes.md). The
original TypeScript game is at the `ts-freeze` tag.
