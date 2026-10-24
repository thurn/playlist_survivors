# Which sections a check runs, from the paths that differ from the branch's base.
# Sourced (POSIX sh) by scripts/check-fast and scripts/ci-gate, from the repository root.
#
#   changes_init  sets CHANGES_BASE and writes the changed paths, one per line, to $CHANGES_FILE
#                 (committed since the base, staged, unstaged and untracked). With no base it
#                 sets CHANGES_UNKNOWN=1, and every section runs.
#   changes_ts / changes_analysis / changes_native   succeed when that section must run.
#
# The base is where the branch left `staging` (Tollgate bases worktrees and speculative
# checkouts on it), else `origin/master`, else the parent commit. Set CHANGES_BASE to a revision
# to choose it instead.

changes_init() {
  CHANGES_FILE=$(mktemp)
  CHANGES_UNKNOWN=0
  CHANGES_BASE="${CHANGES_BASE:-}"
  if [ -n "$CHANGES_BASE" ]; then
    CHANGES_BASE=$(git rev-parse --verify "$CHANGES_BASE^{commit}")
  fi
  for ref in staging origin/master; do
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
    git diff --name-only "$CHANGES_BASE"
    git ls-files --others --exclude-standard
  } | sort -u > "$CHANGES_FILE"
  echo "changes: $(wc -l < "$CHANGES_FILE" | tr -d ' ') paths since $(git rev-parse --short "$CHANGES_BASE")"
}

# Any changed path matching the extended regular expression $1.
changes_match() {
  [ "$CHANGES_UNKNOWN" = 1 ] || grep -Eq "$1" "$CHANGES_FILE"
}

# The TS game and its tooling: everything except the native workspace, the analysis tool,
# documentation and the Tollgate policy.
changes_ts() {
  [ "$CHANGES_UNKNOWN" = 1 ] || grep -Evq '^(native/|tools/analysis/|docs/|\.tollgate/|[^/]*\.md$)' "$CHANGES_FILE"
}

# The Python analysis tool and the song maps it checks.
changes_analysis() {
  changes_match '^(tools/analysis/|songs/)'
}

# The native workspace (F4 adds the content it reads).
changes_native() {
  changes_match '^native/'
}

# The changed files that still exist and match the extended regular expression $1, one per line.
changes_existing() {
  grep -E "$1" "$CHANGES_FILE" | while IFS= read -r path; do
    if [ -f "$path" ]; then printf '%s\n' "$path"; fi
  done
}
