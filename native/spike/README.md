# Phase 0 spike

Throwaway: capture, live beat tracking and the latency chain, measured. F4
deletes it. Results are in docs/design/native-app.md, "Measured: phase 0".

```sh
# Weights, tables and golden data from madmom (gitignored, 5 MB):
tools/analysis/.venv/bin/python native/spike/export_models.py native/spike/models
native/scripts/bundle                       # signed native/target/Spike.app
native/target/release/spike check           # parity with madmom
native/target/release/spike bench 1000      # real-time factors, hindsight decode times
native/target/release/spike devices         # output latencies Core Audio reports
```

Capture needs the bundle (TCC grants System Audio Recording to its signature),
launched through LaunchServices so it is its own responsible process:

```sh
L=$PWD/native/logs
open -n -W --stdout $L/out.txt --stderr $L/err.txt native/target/Spike.app --args tap 8 --click
open -n -W --stdout $L/out.txt --stderr $L/err.txt native/target/Spike.app --args loopback 8 $L
open -n -W --stdout $L/out.txt --stderr $L/err.txt native/target/Spike.app --args app 40 $L &
afplay -v 0.4 native/logs/house-128.wav     # a WAV of public/reactive/house-128
tools/analysis/.venv/bin/python native/spike/align.py native/logs house-128
```

`--stdout` appends. `app ... --mute` skips the clicks; `PS_PRESENT`,
`PS_FRAME_LATENCY` and `PS_IDLE` vary the presentation. `time-builds.sh`
times fresh-worktree builds with and without sccache.
