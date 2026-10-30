# Commands for this checkout. `just` lists them; extra arguments pass straight through,
# e.g. `just native --file song.ogg --delayed` or `just demo3d-play --start-at 160`.

set positional-arguments

tools := "uv run --project demo3d/tools"
unity_app := "/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app"

default:
    @just --list --unsorted

# The game: `cargo run -p app` with its flags (SETUP.md, Launch options; paths from native/)
[group('native')]
native *args:
    cd native && cargo run --locked --release -p app -- "$@"

# Build the native workspace (cargo build flags)
[group('native')]
native-build *args:
    cd native && cargo build --locked --release "$@"

# Cargo tests, e.g. `just native-test -p sim`
[group('native')]
native-test *args:
    cd native && cargo test --locked "$@"

# The `ps` CLI: log, report, replay, analysis-parity, runner-bench
[group('native')]
ps *args:
    cd native && cargo run --locked --release -p cli -- "$@"

# Build and sign native/target/PlaylistSurvivors.app (for system audio)
[group('native')]
bundle *args:
    native/scripts/bundle "$@"

# The fast checks Enact runs before a push
[group('checks')]
check *args:
    scripts/check-fast "$@"

# The full suite (slow CI), e.g. `just ci demo2d`
[group('checks')]
ci *args:
    scripts/ci-full "$@"

# The 3D demo's tools: `just demo3d look <slug> --build`, `just demo3d unity BakeAll`
[group('demo3d')]
demo3d *args:
    {{tools}} demo3d "$@"

# Set up if needed, then build the player (--backend mono builds faster)
[group('demo3d')]
demo3d-build *args: (_setup "demo3d")
    {{tools}} demo3d build "$@"

# Open the newest player windowed at 1080p, with player flags (--start-at 160, --click)
[group('demo3d')]
demo3d-play *args:
    @just _play demo3d PlaylistSurvivors3D "$@"

# The 2D demo's tools
[group('demo2d')]
demo2d *args:
    {{tools}} demo2d "$@"

# Set up if needed, then build the player
[group('demo2d')]
demo2d-build *args: (_setup "demo2d")
    {{tools}} demo2d build "$@"

# Open the newest player windowed at 1080p, with player flags
[group('demo2d')]
demo2d-play *args:
    @just _play demo2d PlaylistSurvivors2D "$@"

# The style lab's tools
[group('stylelab')]
stylelab *args:
    {{tools}} stylelab "$@"

# Set up if needed, then build the player
[group('stylelab')]
stylelab-build *args: (_setup "stylelab")
    {{tools}} stylelab build "$@"

# Open the newest player windowed at 1080p, with player flags
[group('stylelab')]
stylelab-play *args:
    @just _play stylelab StyleLab "$@"

# Open a Unity project (demo3d, demo2d or stylelab) in the editor
[group('unity')]
editor project:
    open -na "{{unity_app}}" --args -projectPath "{{justfile_directory()}}/{{project}}"

# A fresh checkout gets its vendor files and the cached warm Library (with the last build's
# shader cache) from setup, rather than importing and compiling every shader from scratch.
[private]
_setup project:
    @[ -d "{{project}}/Library" ] && [ -d "{{project}}/Assets/Vendor" ] || {{tools}} {{project}} setup

[private]
_play project app *args:
    #!/usr/bin/env bash
    set -euo pipefail
    shift 2
    player=$(ls -td "{{project}}"/Builds/mac/*/"{{app}}.app" 2>/dev/null | head -1 || true)
    if [ -z "$player" ]; then
        echo "no {{project}} player yet; run just {{project}}-build" >&2
        exit 1
    fi
    echo "playing $player"
    open -n "$player" --args -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 "$@"
