# Concurrent decode temporary files — hv-n7fn.38

SH2 exposed one `afconvert` error −54 when two palette tests decoded at once. The decoder previously reused `ps-slice-<pid>.wav` for every call in a process. Each conversion now atomically creates a private directory using process ID plus an atomic counter, skipping existing directories. A scoped guard removes the entire directory on success, converter error, read error or malformed output; conversions never reuse each other’s output. No dependency or asset changes.

Two focused regressions use the actual macOS converter: two threads start together on distinct WAV files containing known 0.125 and −0.25 float PCM, then verify all 1,600 output samples against those upstream values. They join both workers on the caller. Another check verifies unique paths and cleanup after an intentionally missing input (the converter error in its log is expected).

Validation: `focused.txt`, `clippy.txt`, final `validation.txt`, and headless `headless.txt`. This bounded decoder repair needs no per-bead review under the survivors policy; no rollback-runner or sim behavior changed. SH2’s one shared decoded palette per test binary remains appropriate.

Playable from `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
```
