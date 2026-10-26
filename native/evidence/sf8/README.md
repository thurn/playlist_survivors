# SF8 — playlists and the Engine runner

## App and playlist

`native/playlists/survivors-30.m3u` repeats four tempo cycles: house 128,
hiphop 92 swing, accelerando 100–140, drum and bass 174, and Kimiko Ishizaka's
Aria (including its beatless opening and slower tempo). The decoded source is
2,294.04245 seconds (38:14), leaving room for freezes in a 30-minute run.
All paths are relative to the playlist. Silence precedes each track: 2 seconds
by default, with `#PS-GAP:8` before the first accelerando. No trailing gap.

The pure `stream::playlist` parser is shared by the app and CLI. File mode
concatenates all decoded samples at the output rate, including silent samples,
then takes its ordinary `Source::File` path. It never switches the source
between tracks. The debug menu can still explicitly switch sources.

From `native/`, playable command:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
```

The headless 120-second normal-damage check adds
`--headless --press-beats --pick-first --seconds 120`. Its initial and repaired
logs are kept here. The repaired run stays in one session, resumes after the
first gap and advances into minute 1, without a track-change end card.

### Necessary freeze repair

The first run exposed an existing freeze bug: beat confidence below the gate
was treated as silence. On the second, clearly audible track, intermittent
confidence dips repeatedly reset `music_lock`, leaving MUSIC STOPPED held.
Session 1793025340's stream has 200 ticks at song seconds 65–75, 120 above the
evidence gate, peak energy 1.0 and fifteen committed beats. The old run clock
stayed at 60.078 seconds for the rest of the check.

Silence now reads the known tick's normalized energy (<= 0.01); a weak or
beatless but audible passage is not stopped music. Committed-beat evidence
still gates re-lock. Two silence fixtures now also mark their energy silent.
Regression tests cover audible weak evidence and alternating evidence after
a gap; existing 8-second silence, nested pause, deterministic freeze and
rollback tests remain. No enemy behavior or Engine rollback algorithm changed.

## Runner interfaces for SF9

- `eval::survive::drive(&mut Engine, &[Arrival], &mut dyn Player,
  until_song, run_seconds) -> Report` uses the existing Player interface.
  Decisions see only the current observation; the next 120 Hz frame receives
  real keys and only arrivals known by then. It keeps Engine's rolling hash,
  not a parallel simulation or an unbounded settled-record log.
- CLI `ps survive` defaults to the playlist, seed 1, 30 run minutes and the
  `held` stand-in. `--minutes`, `--seed`, `--playlist`, `--jobs`, `--cache`,
  `--only N` (a small track-prefix smoke), `--fixture` and `--record` are
  available. SF9 adds bots and gate/release integration.
- The stand-in holds Right, releases it at a freeze, waits for the third
  card's reveal, presses and releases Confirm once, and freshly presses
  movement after resume. It is not the positioning bot or a balance claim.
- Reports contain both clocks, minimum HP share, HP history, level by minute,
  chest weapon ranks, evolutions, peak alive, kills, settled count and hash.
  Chest/evolution lists remain empty while that gameplay is not implemented.
- Native Rust analysis uses live hindsight and Delayed listening's chosen
  M=3 s parameters. SHA-256 cache keys include audio bytes, the compiled
  analyzer/conversion sources, Cargo lock and actual model bytes. Worktree
  paths do not enter the key. Bounded track workers join and re-raise panics;
  complete cache files are atomically renamed using distinct temporary names.
- Each track's samples determine its exact length. All song-time fields are
  shifted, including both hindsight lists. Gaps add no stream entries;
  this approximation does not reproduce analyzer state or silence ticks in
  gaps. `--concatenated` analyses the full silent-padded source continuously,
  for SF9's release sweep. It changes input preparation, not the driver.
- `fixtures/survivors-3m.json` holds 180 song seconds of the Rust stream
  (duration/key/arrivals envelope), about 1.3 MiB, not Python reference data.
  A 3-minute song prefix contains less run time once choices freeze: report
  both clocks. Rebuild with `--only 3 --write-fixture <new-path>`.
- `--record <new-dir>` writes only meta.json, stream.jsonl and keys.jsonl.
  It preserves the exact seeded level, zen mode, actual applied keys and
  settled hash/count. `ps replay <dir>` needs no audio.
- Long local survive/replay commands re-exec under `taskpolicy -b`. Release's utility-class
  wrapper should set `PS_NATIVE_BATCH=1` while invoking the CLI, so it keeps
  the caller's utility QoS rather than re-entering background.

## Measurements and limits

The initial reports below were collected on the SF7/SF11 base `da2561c`
before SH6 landed. The final candidate was rebased onto SH6 `b99d7ee`; its
updated gameplay numbers and replay checks are recorded separately below.

The ordinary held-direction run requested 30 minutes but died at 223.491 run
seconds, level 5, 450 kills and peak 289 enemies. It prepared the remaining
cold track caches in 284.392 seconds, then drove through 238.342 song seconds
in 5.113 seconds. Its hash is `5d4ee8627815cdfa`. This is a stand-in result,
not SF9/SW7's survival gate; normal runs keep `noHeroDamage:false`.
The replay helper advances one extra song second and compares the recorded
settled prefix, so its later kill total can differ while that prefix matches.

The separate `--runner-soak` diagnostic disables hero damage to exercise the
whole Engine clock/session path before the positioning bot exists. It is
explicitly marked `noHeroDamage:true` in its report and recording; it is never
survival or pacing evidence. The full Rust stream already generated by the
ordinary playlist run is reused as input. Results are in `runner-soak.json`: 1,800 run seconds / 1,849.617 song seconds,
Victory settled, 300 peak enemies and 29,558,256 settled records, in 126.160
wall seconds after prepared-input loading (about 14.3× faster than run time).
Hash `4eecbcad11a6c3f0` replays exactly. This measures the runner, not balance.

The final one-track cache check takes 53.498 seconds cold and 0.164 seconds
warm, with identical hash `3d6a393e92095de6`. The final aggregate check passes
in 14 seconds (native 13); the earlier cold aggregate included fresh dependent
crate builds and took 72 seconds. No full CI run is required by AGENTS.md for
these source changes; there are no CI, policy, workspace-configuration or
shared-test-infrastructure edits.

Raw reports, headless logs, replay results and final checks are kept in this
directory. The committed fixture supplies fast automated exact-replay and
future-entry truncation tests without analyzing audio in the gate.

The continuous-analysis smoke decodes the first two real tracks and their
2-second gap as 126.99794 seconds of PCM. It reaches 90 run seconds / 97.933
song seconds with normal damage, 70.8% HP and 98 peak enemies; its 295,695
settled records replay exactly (`3993257585d736b6`). Analysis takes 101.474
seconds at background QoS; driving takes 0.587 seconds. This exercises the
continuous-analysis path and boundary, not the release's full-playlist sweep.

### Delivery rebase onto SH6

Tollgate found only the two independently appended operator-pending rows in
conflict. Both rows were kept; all code merged cleanly onto SH6 `b99d7ee`.
The required final aggregate passes in 32 seconds. The actual 120-second app
playlist check was repeated (`app-playlist-rebased.txt`) and advances beyond
the first boundary in the same run.

The unchanged full Rust input was reused after the rebase, without re-running
analysis. `rebased-normal.json` is normal damage: the held actor dies at
1,098.023 run seconds, level 10, 2,397 kills and 300 peak enemies. Driving
1,132.267 song seconds takes 54.433 wall seconds. Its 16,702,160 settled
records replay exactly (`014435e5cebb5e93`). These are stand-in measurements,
not the positioning bot or the survival gate.

`rebased-soak.json` is the explicitly damage-exempt diagnostic: 1,800 run
seconds / 1,850.925 song seconds, Victory, 300 peak enemies and 29,643,047
settled records, driven in 115.067 seconds (about 15.6× faster than run time).
Its hash is `191d27698549a17a`; the full replay result is retained separately.
The older reports remain as labelled baseline evidence, not final balance
numbers. No knobs or offers were retuned during integration.


Both final replay sessions' exact meta/keys and their shared full Rust stream
are retained in `recorded/` (stream gzip has a fixed timestamp). To replay
one, copy `recorded/normal/{meta.json,keys.jsonl}` or the `soak/` equivalents
to a new directory and decompress `recorded/stream.jsonl.gz` there as
`stream.jsonl`, then run `ps replay <directory>`. The final soak replays all
29,643,047 records exactly; no audio or analyzer is required.
