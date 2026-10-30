# Commands for this checkout. `just` lists them; extra arguments pass straight through,
# e.g. `just native --file song.ogg --delayed` or `just demo3d --start-at 160`. Each of
# `just native`, `just demo3d`, `just demo2d` and `just stylelab` builds what changed and runs
# it; the `-build`, `-play` and `-tool` recipes are the single steps.

set positional-arguments

tools := "uv run --project demo3d/tools"
# The demos' player backend: `just backend=mono demo3d` builds faster (Mono's simulation
# diverges from IL2CPP's, so never compare their look sets).
backend := "il2cpp"
# The demos' players open fullscreen at 1080p; `just fullscreen=0 demo3d` plays in a window.
fullscreen := "1"
unity_app := "/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app"

default:
    @just --list --unsorted

# Build and run the game with its flags (SETUP.md, Launch options; paths from native/)
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

# Build the 3D demo (setting up first if needed) and play it at 1080p, with player flags (--start-at 160, --click)
[group('demo3d')]
demo3d *args: demo3d-build
    @just backend={{backend}} fullscreen={{fullscreen}} _play demo3d PlaylistSurvivors3D "$@"

# Set up if needed, then build the player (build flags: --development, --scene ...)
[group('demo3d')]
demo3d-build *args: (_setup "demo3d")
    {{tools}} demo3d build --backend {{backend}} "$@"

# Play the last build without rebuilding
[group('demo3d')]
demo3d-play *args:
    @just backend={{backend}} fullscreen={{fullscreen}} _play demo3d PlaylistSurvivors3D "$@"

# The tools themselves: `just demo3d-tool look <slug>`, `just demo3d-tool unity BakeAll`
[group('demo3d')]
demo3d-tool *args:
    {{tools}} demo3d "$@"

# Build the 2D demo (setting up first if needed) and play it at 1080p, with player flags
[group('demo2d')]
demo2d *args: demo2d-build
    @just backend={{backend}} fullscreen={{fullscreen}} _play demo2d PlaylistSurvivors2D "$@"

# Set up if needed, then build the player (build flags: --development, --scene ...)
[group('demo2d')]
demo2d-build *args: (_setup "demo2d")
    {{tools}} demo2d build --backend {{backend}} "$@"

# Play the last build without rebuilding
[group('demo2d')]
demo2d-play *args:
    @just backend={{backend}} fullscreen={{fullscreen}} _play demo2d PlaylistSurvivors2D "$@"

# The tools themselves: `just demo2d-tool test`, `just demo2d-tool setup`
[group('demo2d')]
demo2d-tool *args:
    {{tools}} demo2d "$@"

# Build the style lab (setting up first if needed) and play it at 1080p, with player flags
[group('stylelab')]
stylelab *args: stylelab-build
    @just backend={{backend}} fullscreen={{fullscreen}} _play stylelab StyleLab "$@"

# Set up if needed, then build the player (build flags: --development, --scene ...)
[group('stylelab')]
stylelab-build *args: (_setup "stylelab")
    {{tools}} stylelab build --backend {{backend}} "$@"

# Play the last build without rebuilding
[group('stylelab')]
stylelab-play *args:
    @just backend={{backend}} fullscreen={{fullscreen}} _play stylelab StyleLab "$@"

# The tools themselves: `just stylelab-tool test`, `just stylelab-tool setup`
[group('stylelab')]
stylelab-tool *args:
    {{tools}} stylelab "$@"

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
    player="{{project}}/Builds/mac/{{backend}}/{{app}}.app"
    if [ ! -d "$player" ]; then
        echo "no {{backend}} {{project}} player yet; run just {{project}}" >&2
        exit 1
    fi
    echo "playing $player"
    open -n "$player" --args -screen-width 1920 -screen-height 1080 -screen-fullscreen {{fullscreen}} "$@"
