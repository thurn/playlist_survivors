# What differs between the machines that run the checks (the Mac and Cygnus, Windows under Git
# for Windows' bash). Sourced (POSIX sh) by scripts/enact-worktree and .enact.toml's slow command.
#
#   PS_CACHE      the persistent cache directory (slow CI's target directory, the worktrees' Cargo
#                 seed): ~/Library/Caches/playlist-survivors on macOS, %LOCALAPPDATA%/playlist-survivors
#                 on Windows (C:/ form, which both bash and native tools read), else under
#                 $XDG_CACHE_HOME.

if [ -z "${PS_CACHE:-}" ]; then
  case "$(uname -s)" in
    Darwin) PS_CACHE="$HOME/Library/Caches/playlist-survivors" ;;
    MINGW* | MSYS* | CYGWIN*) PS_CACHE="$(cygpath -m "$LOCALAPPDATA")/playlist-survivors" ;;
    *) PS_CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/playlist-survivors" ;;
  esac
fi
export PS_CACHE
