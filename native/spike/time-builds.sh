#!/bin/sh
# Times a cold build of the spike and a run of the native gate section, each in
# a fresh worktree (a new path and an empty target/, as every Tollgate worktree
# builds), with and without RUSTC_WRAPPER=sccache.
#
#   native/spike/time-builds.sh <commit> <scratch-dir>
#
# sccache gets its own cache and port under the scratch dir, warmed by one
# build in another fresh worktree first, so the timed sccache runs see what a
# gate would: dependencies cached under their stable registry paths, workspace
# crates missing because their absolute paths are new. The cargo registry is
# shared and warm in every run, as on the gate.
set -eu
commit="$1"
scratch="$2"
repo=$(git rev-parse --show-toplevel)
mkdir -p "$scratch"
export SCCACHE_DIR="$scratch/sccache-cache"
export SCCACHE_SERVER_PORT=4227
results="$scratch/build-times.txt"
: >"$results"

fresh() {
  dir="$scratch/wt-$1-$$"
  git -C "$repo" worktree add --detach -q "$dir" "$commit"
  # The models are gitignored: the gate's tests skip parity without them.
  echo "$dir/native"
}

gate() {
  cargo fmt --check
  cargo clippy --workspace --all-targets -q -- -D warnings
  cargo test --workspace -q
  cargo build --release -p spike -q
}

timed() {
  label="$1"
  shift
  start=$(python3 -c 'import time; print(time.time())')
  "$@" >"$scratch/$label.log" 2>&1
  end=$(python3 -c 'import time; print(time.time())')
  printf '%s %.1f s\n' "$label" "$(echo "$end - $start" | bc)" | tee -a "$results"
}

build() { cargo build --release -p spike -q; }

# Without sccache.
d=$(fresh plain-build); (cd "$d" && timed plain-cold-build build)
d=$(fresh plain-gate); (cd "$d" && timed plain-gate gate)

# With sccache: warm the dependency cache once, then measure fresh worktrees.
sccache --stop-server >/dev/null 2>&1 || true
export RUSTC_WRAPPER=sccache
d=$(fresh warm); (cd "$d" && timed sccache-warmup-build build && cargo test --workspace -q --no-run >/dev/null 2>&1 && cargo clippy --workspace --all-targets -q >/dev/null 2>&1 || true)
sccache --zero-stats >/dev/null
d=$(fresh sccache-build); (cd "$d" && timed sccache-cold-build build)
sccache --show-stats >"$scratch/sccache-build-stats.txt"
sccache --zero-stats >/dev/null
d=$(fresh sccache-gate); (cd "$d" && timed sccache-gate gate)
sccache --show-stats >"$scratch/sccache-gate-stats.txt"
sccache --stop-server >/dev/null 2>&1 || true

for w in "$scratch"/wt-*-$$; do git -C "$repo" worktree remove --force "$w"; done
cat "$results"
