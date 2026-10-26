# SF2 — survivors run clock

The default app level is `levels/survivors.yaml`, with an intrinsic sword and no
additional weapons/offers until SF5/SF7. Its current population director is a
temporary stand-in for SF6. Reactive set pieces are retired in this mode; music
still drives zones. `--legacy` selects the original reactive level and judged
movement; `--level ../levels/reactive.yaml` keeps zen on that original level.

The run clock advances with song time, independently of song ends and world
warps; SF3 supplies freezes. At the duration it waits for a causally known bar
downbeat, plays the existing finale blast and ends three seconds later. Once
the deadline passes the hero cannot fall; after the blast movement keys and
held strides cannot leave the final pose. No beat means waiting for a downbeat,
not inventing an early finale.

## Checks and measurements

- `sim-tests.txt`: confirmed song-end ignored, exact repeated records for a
  2.2-second override (blast at 3.0, end at 6.0), future downbeat withheld until
  `knownAt`, and damage before/after the victory deadline.
- `short-run.txt`: release build, headless Delayed File mode with a 5-second
  override, start at session song time 2.84, Victory at about 11.6 (run about
  8.8 seconds including snap/epilogue). `short-replay.txt`: all 4,135 settled
  records match, 25 kills, full health. Session `1792987896` in the main
  checkout's ignored session store; no audio or raw stream is committed here.
- `song-end-run.txt`: default survivors, 8-second house excerpt, 22-second
  headless run. Run starts at 2.591 and remains active until process exit at
  21.969, ending as Stopped rather than Song. `song-end-replay.txt` matches
  the recorded settled prefix. This is a song-boundary smoke, not a full run.
- `legacy-run.txt` and `reactive-run.txt`: headless File-mode compatibility
  launches, 8 seconds each; no errors.
- `app-tests.txt`, `replay-tests.txt`, `clippy.txt`: focused app/replay tests
  and static checks. Aggregate validation is recorded in `check-fast.txt`.
- `storage.json`: 40 minutes at 44.1 kHz mono i16 is exactly 211,680,000 bytes,
  halving the prior f32 memory cost for that duration. The 22-second session's
  non-audio files project to about 49.94 MB over 40 minutes (a linear estimate,
  not a soak). Together about 262 MB, comfortably below the 5 GiB rolling cap;
  retention still limits the store to 20 sessions or the byte cap, whichever
  comes first. The storage test reserves 100 MB for non-audio files. SF10 owns
  the full-duration scale/soak evidence.

## Commands

From `native/`:

```sh
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 30 --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 18 --delayed --level /tmp/sf2-short.yaml
cargo run --release -p cli -- replay /Users/dthurn/playlist_survivors/native/logs/sessions/1792987896
```

`/tmp/sf2-short.yaml` is a copy of `levels/survivors.yaml` with `durationS: 5`.
The short WAV used in the boundary smoke is an uncommitted eight-second excerpt
of house-128 made with afconvert. Session metadata embeds the exact level source
so replay does not depend on these temporary paths; old sessions without it
continue to load reactive.yaml. All app checks ran headless and muted.

No SF2 cold review: project policy reserves it for SF3, rollback-runner changes,
and the milestone's final bead. The rollback runner was not changed.
