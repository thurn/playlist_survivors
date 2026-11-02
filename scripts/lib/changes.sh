# Which lanes a check runs, from the paths that differ from the branch's base.
# Sourced (POSIX sh) by scripts/check-fast, from the repository root.
#
#   changes_init  sets CHANGES_BASE and writes the changed paths, one per line, to $CHANGES_FILE
#                 (committed since the base, staged, unstaged and untracked). With no base it
#                 sets CHANGES_UNKNOWN=1, and every lane runs.
#   changes_match RE
#                 succeeds when a changed path matches the extended regular expression RE (always,
#                 with no base): that lane must run.
#   changes_existing RE
#                 the changed files that still exist and match RE, one per line.
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
    echo "changes: no base found; running every lane"
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

# The changed files that still exist and match the extended regular expression $1, one per line.
changes_existing() {
  grep -E "$1" "$CHANGES_FILE" | while IFS= read -r path; do
    if [ -f "$path" ]; then printf '%s\n' "$path"; fi
  done
}
