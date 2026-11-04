# Commands for this checkout. `just` lists them; extra arguments pass straight through, e.g.
# `just champions --headless --seconds 5`. Rhythm Champions (rhythm_champions/, docs/champions/plan.md,
# Commands): `just champions` sets up if needed, builds the Mono player and plays it at 1080p; the
# `-build`, `-play` and `-tool` recipes are the single steps.
#
# Frozen (AGENTS.md): the native, game, demo3d, demo2d, stylelab, herocap and unity recipes below
# serve projects kept for reference outside CI. They may no longer work; nothing new builds on them.

set positional-arguments

champions := "uv run --project rhythm_champions/tools champions"
# Human play uses sound; agent sessions inherit the CLI's muted default.
champions_audio := if env("CODEX_THREAD_ID", "") + env("CLAUDE_CODE_SESSION_ID", "") + env("ENACT_SESSION", "") == "" { "--audio" } else { "" }
tools := "uv run --project demo3d/tools"
# The demos' player backend: `just backend=mono demo3d` builds faster (Mono's simulation
# diverges from IL2CPP's, so never compare their look sets).
backend := "il2cpp"
# The demos' players open fullscreen at 1080p; `just fullscreen=0 demo3d` plays in a window.
fullscreen := "1"
unity_app := "/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app"
unity_exe := "C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe"

default:
    @just --list --unsorted

# Set up if needed, build the Mono player and play it at 1080p, with player flags (--windowed, --headless, --seconds N, --fixed-time)
[group('champions')]
champions *args:
    {{champions}} build
    {{champions}} play {{champions_audio}} "$@"

# The Boss Rush (docs/champions/boss-rush.md): set up if needed, build and play it, with player flags (--boss N, --file PATH)
[group('champions')]
champions-bossrush *args:
    {{champions}} build
    {{champions}} play {{champions_audio}} --mode bossrush "$@"

# Set up if needed, then build the player (--backend il2cpp, --development, --force, --clean-cache)
[group('champions')]
champions-build *args:
    {{champions}} build "$@"

# Play the last build without rebuilding, with player flags
[group('champions')]
champions-play *args:
    {{champions}} play {{champions_audio}} "$@"

# The tools themselves: `just champions-tool test`, `just champions-tool check --variants`, `just champions-tool editor`
[group('champions')]
champions-tool *args:
    {{champions}} "$@"

# Build and run the game with its flags (docs/archive/setup-prototypes.md, Launch options; paths from native/)
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

# Film the native trailer (native/trailers/action.yaml) to captures/native-trailer.mp4 (records the showcase once for its stream)
[group('native')]
native-trailer *args:
    #!/usr/bin/env bash
    set -euo pipefail
    sessions="${PS_CACHE:-$HOME/Library/Caches/playlist-survivors}/trailer-session"
    cd native
    cargo build --locked --release -p app
    if ! ls "$sessions"/*/stream.jsonl >/dev/null 2>&1; then
        mkdir -p "$sessions"
        PS_SESSIONS="$sessions" ./target/release/playlist-survivors --showcase --headless
    fi
    ./target/release/playlist-survivors --trailer "$(ls -dt "$sessions"/*/ | head -1)" "$@"

# Build and sign native/target/PlaylistSurvivors.app (for system audio)
[group('native')]
bundle *args:
    native/scripts/bundle "$@"

# The fast checks Enact runs before a push
[group('checks')]
check *args:
    scripts/check-fast "$@"

# The full suite (slow CI), or some of its lanes, e.g. `just ci unity`
[group('checks')]
ci *args:
    scripts/ci-full "$@"

# Build the Unity game (setting up first if needed) and play it at 1080p, with player flags
[group('game')]
game *args: game-build
    @just backend={{backend}} fullscreen={{fullscreen}} _play game PlaylistSurvivors "$@"

# Set up if needed, then build the player (build flags: --development, --scene ...)
[group('game')]
game-build *args: (_setup "game")
    {{tools}} game build --backend {{backend}} "$@"

# Play the last build without rebuilding
[group('game')]
game-play *args:
    @just backend={{backend}} fullscreen={{fullscreen}} _play game PlaylistSurvivors "$@"

# The tools themselves: `just game-tool test`, `just game-tool look <slug>`, `just game-tool check --player`
[group('game')]
game-tool *args:
    {{tools}} game "$@"

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

# Build the 3D demo and capture the trailer's mixed video (default captures/trailer.mp4); player flags after `--`
[group('demo3d')]
trailer *args: demo3d-build
    #!/usr/bin/env bash
    set -euo pipefail
    out=1
    for a in "$@"; do
        [ "$a" = -- ] && break
        case "$a" in --out|--out=*) out= ;; esac
    done
    [ -z "$out" ] || set -- --out captures/trailer.mp4 "$@"
    {{tools}} demo3d trailer --backend {{backend}} "$@"

# Capture a trailer in each look preset to captures/trailer-CUT-PRESET.mp4: the crystal cut, or `just trailer-looks action` the neon action cut; `demo3d trailer` flags next (--stand-in), player flags after `--`
[group('demo3d')]
trailer-looks *args: demo3d-build
    #!/usr/bin/env bash
    set -euo pipefail
    cut=crystal
    if [ $# -gt 0 ] && [ "${1#-}" = "$1" ]; then cut="$1"; shift; fi
    ours=() player=() after=
    for a in "$@"; do
        if [ -z "$after" ] && [ "$a" = -- ]; then after=1
        elif [ -n "$after" ]; then player+=("$a")
        else ours+=("$a"); fi
    done
    failed=
    for look in edge solid hologram prism; do
        {{tools}} demo3d trailer --backend {{backend}} --out "captures/trailer-$cut-$look.mp4" ${ours[@]+"${ours[@]}"} \
            -- --trailer "$cut" --look "$look" ${player[@]+"${player[@]}"} || failed="$failed $look"
    done
    [ -z "$failed" ] || { echo "trailer-looks: $cut failed in:$failed" >&2; exit 1; }

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

# Set up if needed, capture HeroEditor's recipes to PNG frames in the machine's cache (--recipe hero, --clip walk),
# then bake them into native's `--art heroeditor` atlas there
[group('herocap')]
herocap *args: (_setup "herocap")
    {{tools}} herocap capture "$@"
    {{tools}} herocap bake

# The tools themselves: `just herocap-tool evidence` (contact sheets, faces, budget), `just herocap-tool setup`
[group('herocap')]
herocap-tool *args:
    {{tools}} herocap "$@"

# Open a Unity project (game, demo3d, demo2d, stylelab or herocap) in the editor
[group('unity')]
editor project:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ "{{os()}}" = windows ]; then
        "{{unity_exe}}" -projectPath "{{justfile_directory()}}/{{project}}" >/dev/null 2>&1 &
    else
        open -na "{{unity_app}}" --args -projectPath "{{justfile_directory()}}/{{project}}"
    fi

# A fresh checkout gets its vendor files and the cached warm Library (with the last build's
# shader cache) from setup, rather than importing and compiling every shader from scratch.
# A set-up checkout re-syncs its vendor files whenever the manifest differs from the one it last
# synced (kept in Library/), so a pull that adds vendor assets doesn't break the bake.
[private]
_setup project:
    #!/usr/bin/env bash
    set -euo pipefail
    cd "{{justfile_directory()}}"
    stamp="{{project}}/Library/vendor-manifest.json"
    if [ ! -d "{{project}}/Library" ] || [ ! -d "{{project}}/Assets/Vendor" ]; then
        {{tools}} {{project}} setup
    elif ! cmp -s "{{project}}/manifest.json" "$stamp"; then
        echo "{{project}}: manifest changed since the last sync; syncing vendor files"
        {{tools}} {{project}} sync
    else
        exit 0
    fi
    cp "{{project}}/manifest.json" "$stamp"

# The Mac's players are apps under Builds/mac/; Windows' (only demo3d builds one) are exes under
# Builds/, started in the background as `open -n` starts an app.
[private]
_play project app *args:
    #!/usr/bin/env bash
    set -euo pipefail
    shift 2
    # --sound unmutes the demos' players (PlayerMute); the game has no such flag and would list it as unrecognized.
    sound=(--sound); [ "{{project}}" = game ] && sound=()
    flags=(-screen-width 1920 -screen-height 1080 -screen-fullscreen {{fullscreen}} ${sound[@]+"${sound[@]}"} "$@")
    if [ "{{os()}}" = windows ]; then
        player="{{project}}/Builds/{{backend}}/{{app}}.exe"
        [ -f "$player" ] || { echo "no {{backend}} {{project}} player yet; run just {{project}}" >&2; exit 1; }
        echo "playing $player"
        "$player" "${flags[@]}" >/dev/null 2>&1 &
    else
        player="{{project}}/Builds/mac/{{backend}}/{{app}}.app"
        [ -d "$player" ] || { echo "no {{backend}} {{project}} player yet; run just {{project}}" >&2; exit 1; }
        echo "playing $player"
        open -n "$player" --args "${flags[@]}"
    fi
