# Timed steps in parallel lanes, for scripts/check-fast and scripts/ci-full. Sourced (POSIX sh),
# from the repository root, after setting LANES_SCRIPT to the script's name.
#
#   lanes_init        makes the lanes' scratch directory ($LANES_DIR; lanes_wait removes it)
#   lane NAME FUNC    runs the shell function FUNC in the background, its output kept until
#                     lanes_wait; FUNC chains its steps with && (set -e does not reach a function
#                     run as a condition)
#   step NAME CMD...  runs CMD, then logs "<script> <lane> NAME: N s" (or FAILED); CMD's status
#   lanes_wait        waits for every lane and prints each one's output, failed lanes last (so a
#                     log's tail shows the failure), then each lane's seconds and its steps'; fails
#                     if any lane failed

lanes_init() {
  LANES_DIR=$(mktemp -d)
  LANES=""
}

step() {
  _step=$1
  shift
  _step_started=$(date +%s)
  if "$@"; then
    _step_seconds=$(( $(date +%s) - _step_started ))
    echo "$LANES_SCRIPT $_lane $_step: $_step_seconds s"
    echo "$_step $_step_seconds s" >> "$LANES_DIR/$_lane.steps"
  else
    _step_status=$?
    _step_seconds=$(( $(date +%s) - _step_started ))
    echo "$LANES_SCRIPT $_lane $_step: FAILED (exit $_step_status) after $_step_seconds s"
    echo "$_step FAILED after $_step_seconds s" >> "$LANES_DIR/$_lane.steps"
    return "$_step_status"
  fi
}

lane() {
  _lane=$1
  (
    _lane_started=$(date +%s)
    if "$2"; then _lane_status=0; else _lane_status=1; fi
    echo $(( $(date +%s) - _lane_started )) > "$LANES_DIR/$_lane.seconds"
    exit $_lane_status
  ) > "$LANES_DIR/$_lane.log" 2>&1 &
  LANES="$LANES $!:$_lane"
}

lanes_wait() {
  _passed=""
  _failed=""
  for _entry in $LANES; do
    if wait "${_entry%%:*}"; then _passed="$_passed ${_entry#*:}"; else _failed="$_failed ${_entry#*:}"; fi
  done
  for _name in $_passed; do echo "=== $LANES_SCRIPT $_name: passed"; cat "$LANES_DIR/$_name.log"; done
  for _name in $_failed; do echo "=== $LANES_SCRIPT $_name: FAILED"; cat "$LANES_DIR/$_name.log"; done
  for _name in $_passed $_failed; do
    _steps=""
    if [ -f "$LANES_DIR/$_name.steps" ]; then
      _steps=" ($(paste -s -d , - < "$LANES_DIR/$_name.steps" | sed 's/,/, /g'))"
    fi
    echo "$LANES_SCRIPT: $_name $(cat "$LANES_DIR/$_name.seconds" 2>/dev/null || echo ?) s$_steps"
  done
  rm -rf "$LANES_DIR"
  if [ -n "$_failed" ]; then
    echo "$LANES_SCRIPT: failed:$_failed"
    return 1
  fi
}
