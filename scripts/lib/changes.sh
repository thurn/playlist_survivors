# Which sections a check runs, from the paths that differ from the branch's base.
# Sourced (POSIX sh) by scripts/check-fast, from the repository root.
#
#   changes_init  sets CHANGES_BASE and writes the changed paths, one per line, to $CHANGES_FILE
#                 (committed since the base, staged, unstaged and untracked). With no base it
#                 sets CHANGES_UNKNOWN=1, and every section runs.
#   changes_baker / changes_analysis / changes_friction / changes_native / changes_demo3d
#                 succeed when that section
#                 must run.
#
# The base is where the branch left `origin/staging` (Enact bases worktrees on it), else local
# `staging`, else `origin/master`, else the parent commit. Set CHANGES_BASE to a revision
# to choose it instead.

changes_init() {
  CHANGES_FILE=$(mktemp)
  CHANGES_UNKNOWN=0
  CHANGES_BASE="${CHANGES_BASE:-}"
  if [ -n "$CHANGES_BASE" ]; then
    CHANGES_BASE=$(git rev-parse --verify "$CHANGES_BASE^{commit}")
  fi
  for ref in origin/staging staging origin/master; do
    if [ -n "$CHANGES_BASE" ]; then break; fi
    if CHANGES_BASE=$(git merge-base "$ref" HEAD 2>/dev/null); then break; fi
    CHANGES_BASE=""
  done
  if [ -z "$CHANGES_BASE" ]; then
    CHANGES_BASE=$(git rev-parse --verify --quiet HEAD^ || true)
  fi
  if [ -z "$CHANGES_BASE" ]; then
    CHANGES_UNKNOWN=1
    echo "changes: no base found; running every section"
    return
  fi
  {
    # (No rename detection: a moved file's old path matters as much as its new one.)
    git diff --name-only --no-renames "$CHANGES_BASE"
    git ls-files --others --exclude-standard
  } | sort -u > "$CHANGES_FILE"
  echo "changes: $(wc -l < "$CHANGES_FILE" | tr -d ' ') paths since $(git rev-parse --short "$CHANGES_BASE")"
}

# Any changed path matching the extended regular expression $1.
changes_match() {
  [ "$CHANGES_UNKNOWN" = 1 ] || grep -Eq "$1" "$CHANGES_FILE"
}

# The asset baker (tools/baker, the one node tool left) and what it reads and writes.
changes_baker() {
  changes_match '^(tools/baker/|assets-src/|public/assets/|package(-lock)?\.json$|tsconfig\.json$|eslint\.config\.js$)'
}

# The Python analysis tool and the song maps it checks.
changes_analysis() {
  changes_match '^(tools/analysis/|songs/)'
}

# The native app epic's friction ledger and its script.
changes_friction() {
  changes_match '^(native/evidence/|scripts/friction$|scripts/test_friction\.py$)'
}

# The native workspace and the content it reads (content/, levels/*.yaml or *.yml, the baked
# assets, the shipped streams), but not the friction ledger in native/evidence/, which every
# bead writes.
changes_native() {
  [ "$CHANGES_UNKNOWN" = 1 ] \
    || grep -E '^(native/|content/|levels/.*\.ya?ml$|public/assets/|public/reactive/|scripts/lib/changes\.sh$)' "$CHANGES_FILE" \
      | grep -Evq '^native/evidence/'
}

# The 3D tech demo (demo3d/: its tools, its choreography layer, its Unity project) and the song
# map it plays.
changes_demo3d() {
  changes_match '^(demo3d/|songs/action/)'
}

# The demo's sync record must know every GUID its manifest lists; a manifest change rebuilds it.
changes_demo3d_manifest() {
  changes_match '^demo3d/manifest\.json$'
}

# The changed files that still exist and match the extended regular expression $1, one per line.
changes_existing() {
  grep -E "$1" "$CHANGES_FILE" | while IFS= read -r path; do
    if [ -f "$path" ]; then printf '%s\n' "$path"; fi
  done
}
