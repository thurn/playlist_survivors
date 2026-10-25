# Native App: Reactive Mode on System Audio, in Rust

## Introduction

Reactive mode plays to music the game has not analysed in advance
([reactive-audio.md](reactive-audio.md), [reactive-feel.md](reactive-feel.md)).
Today it is a mode of the browser game. It reads a **belief stream** that the
Python tool computes offline from a file, and checks prove the game never reads
an entry before its `knownAt` time.

The goal was always to play along to *any* audio on the computer. This plan
builds that as a **native macOS app written in Rust**:

- It **listens to system audio** through a Core Audio process tap, excluding its
  own sounds. By default it never mutes, delays or replays what the player
  hears.
- As an opt-in, **it can delay the music**: the tap mutes one app, and the app
  plays that audio itself a few seconds late, so committed beats come from
  hindsight instead of forecasts ([Phase E](#phase-e-delayed-listening)).
  Parity does not wait for it.
- It **analyses the audio live**, with a streaming Rust port of every belief
  stream group, hindsight included.
- It **runs reactive mode in Rust, grown from a playable slice.** It starts
  from the TypeScript game's rules, content and art and keeps what plays well,
  but the TS game is a prototype to learn from, not a specification to
  reproduce.
- It **renders natively** with winit and wgpu.

TypeScript development ends at that pinned commit (play mode included). The
TS game stays playable at the tag, as a reference to read and to play side by
side. The Python analysis tool stays, as the reference for the Rust analyzer's
accuracy.

**Revised 2026-10-24: prototype, not specification.** The plan began as a
bit-exact port, with the TS game's exported outputs as an oracle. After the
playable slice (Phase V) worked end to end in 40 minutes, the user dropped
bit-exactness: the TS game was a prototype for trying ideas, and the native
game is where ideas get tried from now on. B1 to B5 (the core, content, stream
store, schedule, the sim's modules, its engine and judging) landed bit-exact
and stay as the sim's code. Everything after them is planned in
[Phase G](#phase-g-grow-the-slice) without an oracle. Sections that describe
the oracle are kept as the record of how B1 to B5 were built.

Only reactive mode is in scope. Only macOS on Apple Silicon is in scope, and
the app runs only on the developer's own machine.

Terms from reactive-audio.md keep their meanings: belief stream, `knownAt`,
committed beat, evidence, the gate, hindsight grid, reading, settle delay. New
terms:

- **The freeze commit:** the last TS commit, `e97ef4a` (named by the user on
  2026-10-24), tagged `ts-freeze`. Everything the port reproduces is defined by
  the tree at that tag.
- **Oracle fixtures:** files exported once from the TS game at the freeze
  commit and committed under `native/oracle/`: key events, event logs and frame
  hashes per track and virtual player.
- **Analysis oracle:** the Python tool's `stream.json` output, already committed
  for every shipped and corpus track.
- **Session clock:** seconds of host time (`mach_absolute_time`) since the app
  began listening. Every timestamp in the app (captured samples, key presses,
  stream arrivals, display presents, sound output) is converted to it.
- **Heard time:** the session-clock time at which the player hears a given
  sample: its capture time plus the output latency, plus the music delay `M`
  in Delayed listening. The sim's song time is heard time.
- **Direct listening:** the default. The player hears the source app as it
  plays, and the tap only listens.
- **Delayed listening:** the opt-in mode of [Phase E](#phase-e-delayed-listening).
  The tap mutes one app the player picks, and the app plays that app's audio
  through its own output `M` seconds late.
- **Music delay `M`:** how late Delayed listening plays the music, a constant
  set in E1 by rule (about 2–4 s).
- **Arrival stamp:** the session-clock time at which a live stream entry
  becomes available to the game. Live, it *is* the entry's `knownAt`.
- **Source:** where audio comes from: **System** (the process tap), **File**
  (the app plays a file itself; developer only) or **Replay** (headless, no
  audio device).
- **Run:** one game from start to its end card. In this plan, one run per song.
- **Live rule:** a behaviour that must differ from the TS game because the
  game no longer controls the music, such as the finale. Each is listed in
  [Live exceptions](#live-exceptions).

### The plan in brief

| Phase | What it delivers | Proves |
|---|---|---|
| 0. Spike | Capture → online RNN → DBN → a window flashing on committed beats; latency chain measured; build times measured | The risky native parts work in Rust on this machine |
| 0. Targets | The spike's executor sets latency targets from its own numbers, by fixed rules | — |
| W. Workflow (now, alongside the TS wrap-up) | Staged validation (a gate stage of about 60 s, a release stage for everything else), fast local checks, `ci-fix` beads, the friction ledger and its triggers | CI stays fast and tool failures get fixed |
| 1. Freeze and foundations | `ts-freeze` tag, oracle fixtures, exported model weights, Cargo workspace, both stages' native sections, AGENTS.md | The oracles exist and CI runs Rust |
| V. Playable slice (landed 2026-10-24) | A rough game in a native window on live system audio, then the game's real sprites, effects and sounds (V1, V2) | Capture, analysis, input, render and output work end to end |
| A. Analyzer | Every stream group as streaming Rust; A1–A6 at stage parity, A7 and later judged by corpus accuracy | Rust beats are as accurate as Python's |
| B. Sim (B1–B5, landed) | Core, content, stream store, schedule, the sim's modules, its engine and judging, ported bit-exact | The sim's foundations, as code to grow |
| G. Grow the slice | The slice runs on the `sim` engine, then gains the director and set pieces, the game's look, its sound, the rollback runner, runs on live audio and checks without an oracle. The app stays playable after every bead | The game plays well, natively, live |
| D. Live play | Calibration, session recording and replay, timing logs, latency report, QA checklist, TS tree removed | Done |
| E. Delayed listening (not needed for live play) | E0 and ES now: the payoff measured offline from committed streams, and the muting tap and delay line spiked. Later E1 and E2: committed beats from hindsight under a music delay; the tap mutes the picked app and the app plays its audio late | Lookahead buys measurably better beats, safely |
| R. Retrospectives | R1 ran; phase and cadence retrospectives are paused from 2026-10-24, with R5 at the end | — |

Phase G follows the slice, on two executors: one grows the app, the other the
sim. Phases A and B ran in parallel after phase 1. Phase D's tools follow G's runner and runs.
Phase E's E0 and ES ran early. Its analyzer bead E1 can start once A is done,
and its app bead E2 follows D's calibration and timing logs. No phase E bead
blocks D5 or D6.

### Decisions

These were settled with the user while designing. The executor treats them as
fixed.

- **A full Rust rewrite now,** not a webview shell around the TS game. The user
  weighed the sizing (a port of about 17k lines plus tooling, against about 3–4k
  lines changed for an incremental TS refactor) and chose the rewrite.
- **Velocity first: a playable game before fidelity.** On 2026-10-24 the user
  moved a rough end-to-end game ahead of the port
  ([Phase V](#phase-v-the-playable-slice)). It landed the same afternoon, with
  the game's art and sounds, and it is the trunk the rest of the plan grows.
- **Prototype, not specification** (2026-10-24, replacing "a faithful port"
  and "two oracles"). The TS game was a prototype for trying ideas. The native
  game starts from its rules, tunings, content and art, read from the TS code
  as a reference, and is free to change them. Nothing is checked against the
  TS game's outputs from here on:
  - B1 to B5, already bit-exact, stay as the sim's code. Their oracle tests
    stay while they pass. A deliberate rule change deletes or rewrites the
    oracle tests it breaks; it never bends the sim back to match TS.
  - The sim must still match **itself**: the same stream and keys always give
    the same recording. Rollback and session replay depend on it.
  - The causality rule is unchanged: the game never reads a stream entry before
    its `knownAt`.
  - Each change is judged by playing it, by the causality checks, by a
    virtual-player smoke test and by the operator's play.
- **The analyzer is judged by accuracy, not bits.** A1 to A6 landed at stage
  parity with Python and keep those tests while they pass. From A7 on, the bar
  is corpus accuracy: committed beats about as accurate as Python's (hit rate
  within 45 and 70 ms of the reference beats), with the stream's causality
  test. No new library-arithmetic pins.
- **TS development ends at the freeze commit,** play mode included. The TS
  game stays runnable at `ts-freeze` as a reference to play side by side; new
  ideas are tried in the native game.
- **Oracle fixtures stay committed** as reference data (`native/oracle/`), no
  longer frozen: content, levels and streams may change, and a test that
  depends on a fixture's hash is updated or deleted with the change.
- **The analyzer is a streaming Rust port of every stream group,** with madmom's
  weights exported once by a Python script. madmom's model licence
  (CC BY-NC-SA 4.0) is acceptable: the app runs only on the developer's machine.
- **winit + wgpu with a custom renderer.** No game engine.
- **The sim is plain data-oriented Rust:** dense `Vec`s of plain structs, cloned
  for rollback snapshots, with systems as ordinary functions called in a fixed
  order. No ECS framework: snapshots must be cheap clones and iteration order
  must be deterministic, and at about 500 enemies an ECS buys nothing
  measurable.
- **One run per song, auto-detected,** with manual Start and End as fallbacks.
  An endless session across songs is the eventual goal and a
  [follow-on](#beyond-this-plan). Every clock in the app is the session clock,
  and a run is a span of it starting at any offset, so chaining songs later
  does not fight the time base.
- **Sources:** System is the product. File and Replay are developer tools
  (a CLI flag and a debug menu), never on the player's title screen.
- **Outputs:** built-in speakers and wired headphones first. Bluetooth is
  best-effort: Core Audio's reported latency plus a per-device calibration.
- **Delayed listening is an opt-in output mode, macOS only, and live play does
  not wait for it.** The user reversed reactive-audio.md's "never delay the music"
  for the native app, because the tap can mute one app safely
  (`CATapMutedWhenTapped`, released with the tap). Direct listening stays the
  default. Delayed
  listening taps only the app the player picks, never all output, so other
  apps' sounds are neither muted nor delayed.
- **No approval points.** The user named none and wants none. Latency targets
  are set by the spike's executor from its measurements, by the rules in
  [Setting the targets](#setting-the-targets). Every other open choice is the
  executor's, recorded with its reasons in the bead's notes. The only outside
  input is the freeze commit's hash, which the user supplies when the TS work
  wraps up.
- **Every session is saved:** captured audio, the stream with arrival stamps and
  the key events, with a rolling cap of the last 20 sessions.
- **Tooling ported:** causality checks, a virtual-player smoke test, headless
  frame capture and play timing logs. The parity report against play mode and
  the feel dossiers and raters are not ported in this plan.
- **Content stays where it is.** The app reads `content/`, `levels/reactive.yaml`
  and the baked `public/assets` from the repository, the single source of
  truth, and bundles them into the `.app`.
- **Layout:** a Cargo workspace in `native/`, split into crates as in
  [Workspace](#workspace).
- **Validation is staged** (as in `~/dreamtides_web`, decision D44 there). A
  gate stage of about 60 s blocks promotion to `staging`. A release stage runs
  everything else asynchronously on the newest `staging` tip and moves
  `release` and remote `master`. A bead is done when its commit is on
  `staging`, and never waits for the release stage. A red release run is fixed
  by a `ci-fix` bead, filed by the first session to see it, which preempts all
  other ready work. Locally, an executor runs focused checks and one fast check
  of under 60 s before submitting. See [Workflow](#workflow).
- **Introspection is paused** (2026-10-24). The friction ledger, its triggers
  and the phase and cadence retrospectives ran through phase B and are paused
  while the plan favours velocity; R5 still closes the epic. A tool failure is
  still never silently worked around: it is filed as a `tooling` bead at once.
  See [Introspection](#introspection).
- **The gate is path-gated:** a native section when `native/` differs from
  master, the TS section when TS paths differ. Phase 0 measured no consistent
  gain from sccache in fresh worktrees, so the gate builds without it
  ([appendix](#measured-phase-0)).
- **Scope ends when the native game plays a full run live,** with the D phase's
  tools, and the TS tree is removed. Follow-ons are named, not designed.

### Goals

- On the developer's Mac, the app listens to any app's audio (Music, Spotify, a
  browser) and plays a full reactive run to it, from song start to end card.
- The game plays well to live music: the operator's play is the judge. It
  starts from the TS game's rules, content and art, and keeps or changes them
  by feel.
- The Rust sim is deterministic: the same stream and key events always give the
  same recording.
- The Rust analyzer's committed beats are about as accurate across the corpus
  as Python's.
- The game never reads a stream entry before its `knownAt`, enforced by the
  ported causality checks, and live by construction.
- The latency chain is measured end to end and reported every session.
- Any live session can be replayed headlessly, bit for bit.
- In Delayed listening, committed beats are measurably more
  accurate than Direct listening's across the corpus. Quitting or crashing
  always gives the player back the muted app's sound.

### Non-goals

- Windows, Linux, Intel Macs, the App Store, signing for distribution, or
  notarization.
- An endless session across songs, a restarted feel loop, press fusion (see
  [Beyond This Plan](#beyond-this-plan)).
- Play mode, the authored Galop and the song-map toolchain.
- Delaying or muting by default, tapping every app for Delayed listening, or a
  delayed mode on any platform but macOS.
- Using Delayed listening's lookahead for anything but committed beats, such
  as accents detected rather than forecast or structure events on time (see
  [Beyond This Plan](#beyond-this-plan)).
- Track identity for live audio, such as reading Now Playing metadata. Live
  runs have no per-track best grade.
- Gamepad input. The TS game is keyboard-only.

## Related Information

- [reactive-audio.md](reactive-audio.md): the belief stream, its file format,
  committed beats, judging, structure, accents, set pieces, and causality.
  [Beyond This Plan](reactive-audio.md#beyond-this-plan) named this work.
- [reactive-feel.md](reactive-feel.md): the corpus, fitted grids, bands and
  timbre, and the [live-readiness](reactive-feel.md#live-readiness) rule that
  every stream feature "could run live in an optimised Rust rewrite".
- [AGENTS.md](../../AGENTS.md): Tollgate worktrees, validation and review rules.
- The analysis oracle: `tools/analysis/src/ps_analysis/stream.py` (beats,
  ticks, onsets, hindsight, the phase check), `bands.py`, `structure.py`,
  `accents.py`, and `tools/analysis/tests/test_stream.py` (stream causality).
- The TS game to port (sizes at the time of writing):
  - `src/sim/simulate.ts` (3.6k lines), `src/sim/*` (2.4k more);
  - `src/reactive/*` (1.2k): stream, settle, director, sections, set pieces,
    players, grade;
  - `src/planner/*` (context, plan, emit, schedule inputs) and `src/plan/*`
    (`evaluate.ts` 1.5k, types, reactive look);
  - `src/live/*` (runner with rollback, recording, input, sounds, log);
  - `src/runtime/*` (renderer, shaders, HUD, overhead, font, audio, title, play
    flow);
  - `src/content/*` (YAML loader and zod schema), `src/core/*` (rng, geometry,
    warp, song map).
- The TS causality checks: `scripts/check-reactive.ts` and
  `src/planner/reactive-checks.ts` (the perturbation check, its negative
  control).
- Apple: [Capturing system audio with Core Audio taps](https://developer.apple.com/documentation/coreaudio/capturing-system-audio-with-core-audio-taps)
  (macOS 14.2+), `CATapDescription`, `AudioHardwareCreateProcessTap`.
- [madmom](https://github.com/CPJKU/madmom): code BSD, models CC BY-NC-SA 4.0.
- [winit](https://github.com/rust-windowing/winit), [wgpu](https://wgpu.rs).

## Architecture

```text
 System: Core Audio process tap ─┐
 File:   the app's own playback ─┼─► AudioSource ─► ring buffer ─► analyzer thread ─► arrivals ─┐
 Replay: decoded file / stream ──┘   (RT thread)     (lock-free)    (+ hindsight worker)          │
                                                                                                 ▼
 keys (NSEvent timestamps) ─────────────────────────────────────────────► main thread: StreamLog + key queue
                                                                                 │
                                                          LiveRunner: sim speculates 0.2 s ahead,
                                                          rolls back on late keys and new arrivals
                                                                                 │
                                         recording ─► evaluator(state, s) ─► renderer (wgpu) ─► display
                                                  └─► sound events ─► mixer (output callback) ─► speakers
                                                                        ▲
 Delayed listening only: captured stereo ─► delay line (M s) ───────────┘
```

The pure crates (`stream`, `analysis`, `sim`, `eval`, `players`) have no I/O, no
threads and no clocks. The app and the headless CLI run exactly the same code
through them. That is what makes Replay a faithful test of System.

### Workspace

```text
native/
  Cargo.toml          # workspace
  crates/
    stream/    # belief-stream types; append-only StreamLog; serde for stream.json
    analysis/  # the live analyzer: front end, online RNN, DBN, commits, ticks,
               #   onsets, bands, timbre, structure, accents, hindsight (pure)
    audio/     # Core Audio tap, file source, AUHAL output, mixer, host-time clock
    sim/       # content loading, song map, context, schedule, director, set pieces,
               #   the deterministic sim, settling, grade (pure)
    eval/      # evaluator: recording + song time -> frame state (pure)
    render/    # wgpu renderer; offscreen capture for stills
    players/   # virtual players (pure)
    app/       # winit binary: runner, input, menus, HUD flow, calibration, logs
    cli/       # `ps`: replay, oracle, causality, capture, log summaries, bench
  models/      # exported madmom weights
  oracle/      # committed TS fixtures (gzipped)
  scripts/     # bundle (the .app), check-fast
  logs/        # gitignored: play logs and saved sessions
```

### Threads

- **Audio input (real time).** The tap's IOProc copies samples into a lock-free
  single-producer ring (`rtrb`), with the buffer's host time. It never
  allocates, locks or logs.
- **Analyzer.** It drains the ring, downmixes and resamples to 44.1 kHz mono
  (madmom's rate; files already at 44.1 kHz skip resampling), and steps the
  analysis one 10 ms frame at a time. Every finished entry goes to the main
  thread over a channel, stamped with its arrival time.
- **Hindsight worker.** Every 1 s of audio it decodes the last 20 s, off the
  analyzer thread, so the analyzer's frame rate never stalls. To keep the
  analyzer a pure function of its samples, a hindsight entry for a window
  ending at sample time `T` becomes usable inside the analyzer (by the phase
  check) at sample time `T + D`, a fixed delay set in phase 0 from the
  spike's decode times: `D = 670 ms` (see [Targets](#targets)). If a decode isn't done by then, the analyzer waits for
  it and the play log records the stall.
- **Main thread.** winit's event loop: it drains arrivals and keys, runs the
  live runner, evaluates the frame, encodes and presents it. It posts sound
  events to the mixer.
- **Audio output (real time).** The mixer renders scheduled voices into the
  output buffer, sample-accurately, from pre-decoded SFX.

### One clock

Every timestamp becomes session-clock seconds, from host time:

| Event | Host time from |
|---|---|
| A captured buffer | `AudioTimeStamp.mHostTime` in the tap's IOProc |
| An output buffer | `AudioTimeStamp.mHostTime` in the output callback |
| A key press | `NSEvent.timestamp`, through a local event monitor installed beside winit (winit drops it) |
| A frame's present | The display link's target timestamp |
| A stream entry's arrival | `mach_absolute_time()` when the analyzer publishes it |

The sample clocks of the input and output devices drift against host time. Each
is mapped through a smoothed linear fit (a delay-locked loop) of sample index
against host time.

**Heard time.** A sample captured at host time `h` reaches the player at
`h + L`, where `L` is the output device's latency: device latency, safety
offset and stream latency as Core Audio reports them, plus the buffer size,
plus the per-device calibration. The game's own sounds go out through the same
device, so they share `L` automatically. The sim's song time is heard time, so
everything the player sees and hears lines up with the music as heard.

Caution: the tap's capture timestamps and the output callback's timestamps
are not guaranteed to refer to the same point in the device's pipeline, so
"the game's sounds share `L`" is a hypothesis. The spike measures the tap's
offset and the output's offset separately with a loopback test (built-in
speakers into the built-in microphone), and the app carries two constants,
`L_tap` and `L_out`, wherever they differ.

Measured in phase 0 ([appendix](#measured-phase-0)), this hope of free
lookahead did not hold on built-in speakers. The tap stamps a sample 23 ms
later than the output's render timeline stamps the same instant, so
`L_tap = L_out - 23 ms`, about -6 ms: the tap's timestamp falls after the
sample is heard. Its IOProc then runs 31 ms after that timestamp. The analyzer
sees each sample about 33 ms (median) after the player heard it. Committed
beats are still forecast about 0.78 s ahead of their heard times, so beats,
clicks and flashes land on time; ticks and onsets simply arrive after their
`t`, which [Arrivals roll back like late keys](#arrivals-roll-back-like-late-keys)
already handles. Bluetooth is unmeasured.

**In Delayed listening** the app plays the music itself, so a sample captured
at `h` is heard at `h + M + L_out`. The music and the game's sounds then leave
through the same output unit and share `L_out` exactly, with no hypothesis to
measure. `L_tap` no longer affects what the player hears. The analyzer's
lookahead becomes `M + L_out`, and [Phase E](#phase-e-delayed-listening) spends
it on committed beats.

### The stream, live

`stream` defines one store, used by every source:

```rust
/// Every group is append-only, ordered by `known_at`; nothing is revised.
pub struct StreamLog {
    pub beats: Vec<Beat>,          // committed beats
    pub ticks: Vec<Tick>,          // every 50 ms
    pub onsets: Vec<Onset>,
    pub hindsight: Vec<Hindsight>,
    pub changes: Vec<Change>,
    pub jumps: Vec<Jump>,
    pub ends: Vec<End>,
    pub forecasts: Vec<Forecast>,
    pub timbre: Vec<Timbre>,
    // ...and every group the freeze commit's stream has.
}

pub struct Beat { pub t: f64, pub known_at: f64, pub period: f64, pub evidence: f64 }
```

- **Replay from `stream.json`:** entries keep Python's `knownAt`. They are fed
  into the log in `knownAt` order as the run advances, never all at once.
- **Live (System or File):** each entry's `known_at` is its arrival stamp. Its
  content times (`t`) are heard times of the samples it describes.
- **Ticks gain a `known_at`.** In TS they have none, and readers find them by
  `t` (`tickIndexAt`). In Replay a tick's `known_at` is its `t`, so nothing
  changes. Live, a tick can arrive before or after its `t` (capture plus
  processing, against the output latency `L`), so every tick reader means "the
  latest tick with `t <= s` **and** `known_at <= s`".
- **The end of the stream.** The TS sim also knows things no live sim can: the
  song's length and the final entry of each group. It reads them in a few
  places, such as the set pieces' clamps to `beats.length - 1` and
  `ctx.duration` in the schedule and weapons. The Rust sim takes an optional
  `StreamEnd { duration, counts }`. Oracle Replay provides it at time 0, so
  those reads match TS; live it is absent, and each such read takes its
  [live exception](#live-exceptions). Phase 1's inventory lists every such
  read.
- **The reference** (offline beats, sections) is a separate type that only the
  virtual players and checks can reach. The sim crate cannot name it.

### Arrivals roll back like late keys

The TS runner runs the sim 0.2 s ahead of the audio clock, assuming no new
presses, and rolls back when a press arrives late. Live, the stream behaves the
same way: an entry that arrives at `a` was not visible to a sim that had
already speculated past `a`.

- **Rule:** the runner treats arrivals exactly like late keys. All arrivals and
  keys drained in one frame cause one rollback, to the newest snapshot before
  the earliest of their times. Then it re-simulates ahead.
- **Too late to roll back:** the runner keeps snapshots only back to its
  settled time (`now - SETTLE_S`, 0.04 s). An arrival stamped before that (a
  main-thread stall) is re-stamped to the settled time, as TS does with very
  late keys, and the effective stamp is what the session records.
- **The lead covers the output latency.** A sound heard at `s` must reach the
  mixer by `s - L`, so the sim must have run to at least `now + L + margin`.
  The lead is `max(0.2 s, L + mixer margin)`. On built-in speakers that stays
  0.2 s. On AirPods (`L` around 150–300 ms) it grows, and so does each
  rollback's depth. Sounds that answer a press can never be heard earlier than
  `L` after it, a limit of the output, not the game.
- **Why:** the settled state then equals a run that saw every entry at exactly
  its `knownAt`, so the oracle reasoning carries over to live play unchanged.
  Speculation only ever assumes the absence of new input.
- **Cost:** ticks arrive 20 times a second. Each rollback re-simulates about
  one lead (0.2 s or more), so the sim does about 4 s of simulation per second
  or more. G5 measures this. The Rust sim should be far faster than real
  time. If profiling disagrees, the fallbacks, in order:
  1. optimise the sim's step and the snapshot clone, guided by `ps bench`;
  2. snapshot more often, so each rollback replays less;
  3. as a last resort, stamp ticks at `arrival + lead` instead of their
     arrival, so they land at the speculation frontier and never cause a
     rollback. This delays every tick-driven decision (the gate, judging's
     evidence) by one lead, so it is recorded as a live exception if taken.

  Rolling back only for "arrivals the sim reads" would save little: the sim
  reads ticks' evidence and level for the gate, judging and set pieces.
- **Sounds** scheduled from speculated state may be cancelled or moved, as the
  TS `LiveSoundScheduler` does. The mixer accepts changes until a voice is
  rendered.

## Phase 0: The Spike

Quick and throwaway in quality, but it answers the questions that could change
the plan. It lives in `native/` from the start, as `native/spike/`, deleted in
phase 1.

- **Capture:** a process tap on all output, excluding the app's own process
  (`kAudioHardwarePropertyTranslatePIDToProcessObject`), through an aggregate
  device. (Measured: the exclusion only works when the app's output unit is
  started before the tap is created. The app starts the mixer first.) Prove the tap hears Music and a browser, and does not hear the app's
  own test click.
- **Permission:** the tap needs the "System Audio Recording" permission
  (`NSAudioCaptureUsageDescription`), which needs a bundle with an
  `Info.plist`. TCC keys the grant on the code signature, so an ad-hoc
  signature, whose hash changes every build, would re-prompt after every
  rebuild. The spike creates a **stable self-signed signing identity** in the
  login keychain once, and `native/scripts/bundle` signs every build with it.
  Prove the grant survives a rebuild.
- **Analysis:** madmom's online front end, the online LSTM ensemble and the DBN
  forward step, ported directly enough to run, using weights exported by a
  small Python script. Committed beats with the stream's commit rule.
- **Output:** a winit + wgpu window that flashes on committed beats (at their
  heard times) and plays a click on each through a native output unit.
- **Measure and report** (median, p95 and max, in ms):
  - capture buffer size and interval;
  - capture → entry arrival (the analyzer's latency);
  - the analyzer's real-time factor, and the estimated factor for hindsight
    (one 20 s BiLSTM and Viterbi decode, timed);
  - key event → main thread (from `NSEvent.timestamp`);
  - main thread → present (from the display link);
  - the output latency Core Audio reports on built-in speakers, wired
    headphones and AirPods;
  - by ear and by a loopback test (built-in speakers into the built-in mic),
    the flash and click offset against the music's beat.
- **Build times:** a cold build of the spike in a fresh Tollgate worktree, and
  a run of the native gate section, each with and without
  `RUSTC_WRAPPER=sccache`. sccache keys workspace crates on absolute paths, so
  every new worktree misses on them; whichever is faster becomes the gate's
  setting.
- **Crate check:** confirm `objc2-core-audio` (or a hand-written binding)
  covers `CATapDescription`, aggregate devices and the tap APIs. Pick the YAML
  crate (`serde_yaml` is unmaintained) and the output path (AUHAL via objc2,
  with `cpal` as the fallback).

The spike writes its numbers into this document's
[appendix](#appendix-measurements), then sets the targets.

### Setting the targets

The spike's executor fills in [Targets](#targets) from its own measurements,
by these rules, and records its reasoning in the bead. Nobody stops to approve
them.

- **Links the app controls** (capture → arrival, key → main thread, main
  thread → present, per-frame cost, rollback re-simulation): a gate at the
  measured p95 plus 50% headroom, rounded up to a whole millisecond. A gate
  that later proves too tight on unrelated work may be loosened once, with the
  measurement that justifies it.
- **Links set by hardware** (output latency, Bluetooth): report-only.
- **The analyzer's real-time factor:** a gate at 0.5 for all groups together,
  hindsight included, on one core.
- **Hindsight's delay `D`:** the p99.9 of the spike's decode time, doubled and
  rounded up to 10 ms.
- **If hindsight cannot run live** (one decode takes longer than its 1 s
  period, even after obvious optimisation), the executor chooses the nearest
  workable form (decoding every 2 s, or a shorter window), records the cost to
  accuracy against Python, and updates this document. A4 then implements that
  form.

## Phase 1: Freeze and Foundations

### The freeze

1. Tag the freeze commit, `e97ef4a47c246b4654dc1e628499b9b8d983ce57`,
   `ts-freeze` and push the tag.
2. Inventory the reactive features at the freeze: every stream group, every
   behaviour that reads `audio.duration`, `knownLength` or `ctx.duration`, and
   anything non-causal outside the sim (like `followCurve`). The
   [live exceptions](#live-exceptions) list is completed from this inventory.
   The TS reactive-feel work still landing (length-blind pacing, confirmed
   endings, the signal bus) may already have removed some.
3. Record the frozen numbers: `check:reactive`'s targets and the golden hashes,
   for reference.

### Oracle fixtures

A one-off exporter, `scripts/oracle-export.ts`, is added on top of the freeze
commit. It is instrumentation only: the golden hashes and every TS check must
pass unchanged with it in place.

- **Runs:** the level's seed, batch mode, over:
  - the core set (the Galop, the synthetic tracks and about six corpus tracks
    across genres) × every virtual player profile the TS reactive checks use;
  - every other track × two profiles (an honest player and a masher).

  A virtual player runs *inside* the TS sim (its `decide` events share the
  queue), but the Rust harness replays keys. So the exporter first records the
  player's key events, then re-runs the sim fed only those keys, asserts that
  the two runs' recordings are identical, and exports the keys-only run.
- **Per run, it writes:**
  - `keys.jsonl`: every key event the virtual player produced (so the Rust side
    needs no TS player to compare the sim);
  - `events.jsonl.gz`: the full event log in a canonical order: beats, presses
    and their provisional and settled ratings, groove changes, tier changes,
    spawns, deaths, hits and damage, level-ups, orbs, set pieces, zone waves,
    sounds, UI events, the end;
  - `frames.bin.gz`: a 64-bit FNV-1a hash of the evaluator's frame state at
    every 1/60 s, over a canonical serialization (fields in a fixed order,
    numbers as their f64 bits, arrays length-prefixed). **A frame is defined
    as the TS checks define it:** every key event queued up front at its time,
    the sim advanced to `s + SPECULATE_S` with stream entries visible by *sim*
    time (`knownAt <=` the sim's clock), the recording refreshed, then
    `evaluate(s)`. That makes a frame independent of how keys and arrivals
    are grouped into app frames. The serialization and this procedure are
    written down once, in `native/oracle/FORMAT.md`, and implemented
    identically on both sides;
  - a few dense frames as JSON (every field) at fixed times, for debugging a
    hash mismatch.
- **Size:** full event logs and frame hashes for the core set. For the rest of
  the corpus, a hash chain per second of song over the event log, another over
  the frame hashes, and summary numbers. F2 measures the sizes and holds the
  total under about 30 MB compressed, trimming the core set's profiles before
  anything else.
- **Inputs are pinned:** each fixture's metadata holds hashes of every input
  it depends on: the track's `stream.json`, `content/`, `levels/reactive.yaml`
  and the baked assets. The harness fails loudly, naming the file, when an
  input no longer matches. Until D6, AGENTS.md forbids changing those inputs
  (including regenerating streams with the Python tool) except by a bead that
  also regenerates the fixtures from `ts-freeze`.
- **Reference stills:** PNGs from the TS renderer (`?capture&t=`) at about 20
  chosen moments on the core set, for the renderer's perceptual comparison.
- **Reproducible:** `native/oracle/README.md` names the tag and the command.
  Regenerating fixtures means checking out `ts-freeze` plus the exporter commit.

F2 measured ([native/evidence/f2/](../../native/evidence/f2/README.md)): 215
runs (the 13 core tracks × 9 profiles, the other 49 tracks × 2), 15 with full
logs and the rest with per-second hash chains, 29.5 MB of fixtures plus 26.4 MB
of stills. The keys-only assertion holds on every run, but the TS sim depends
on where it stops in two reads (a sound's pan, one warp break), which B7 must
pin. The truncation check exposed three non-causal reads, now live exceptions.

### Models

`tools/analysis` gains `ps-analysis export-models`, which writes madmom's online
and offline beat ensembles (and any other model the stream uses), the DBN
state spaces and transition models the stream's parameters produce (for
example the CSR arrays with `num_tempi` unset), and the front ends'
filterbank matrices, to
`native/models/` as a small self-describing binary: a JSON header naming each
tensor's shape and dtype, followed by little-endian f32 data. It also writes
golden activations for a 12 s excerpt of a synthetic track, for the first
stage-parity test.

### Workspace and both stages

- Scaffold the workspace with the crates above, `rustfmt.toml`, workspace
  lints (`clippy::pedantic` where it helps), and `rust-toolchain.toml`
  pinning the toolchain.
- Phase W's staged validation already exists ([Workflow](#workflow)). F4 adds a
  **native section to each stage**, run when `native/` (or `content/`,
  `levels/reactive.yaml`, `public/assets`, `public/reactive`) differs from the
  stage's base:
  - **gate stage (about 60 s with the TS section, at most):** `cargo fmt
    --check`, `clippy -D warnings` and the unit tests of the crates the diff
    touches and their dependents, plus the cheapest oracle and causality
    smoke (one core track, two cuts);
  - **release stage** (unconditionally, on every `staging` tip: it is
    asynchronous, and a skipped section could hide a break): the whole
    workspace's clippy and tests, every check in
    [Automated Checks](#automated-checks) (the full corpus parity and oracle
    sweep included; nothing is left to run by hand), `ps bench`, and a release
    build of the app.
- **Builds:** S0 measured a cold build at about 30 s and the native section at
  about 90 s in a fresh worktree, with no consistent gain from sccache. The gate
  stage therefore builds into a **persistent target directory** outside the
  worktrees (`CARGO_TARGET_DIR` in the Tollgate step's environment), so a gate
  run recompiles only the workspace's own crates. The release stage uses its
  own persistent directory. F4 measures both against their budgets.
- `[profile.test]` and `[profile.dev.package."*"]` use `opt-level = 2`: the
  causality and oracle runs simulate whole songs.
- **Purity is enforced, not hoped for.** The pure crates (`stream`,
  `analysis`, `sim`, `eval`, `players`) carry `#![forbid(unsafe_code)]`. A gate
  stage check reads `cargo metadata` and fails if any of them depends, directly
  or transitively, on `audio`, `render` or `app`, or on `objc2`, `winit` or
  `wgpu`.
- **Unused dependencies:** `cargo-machete` runs in the gate whenever a crate changes (a fraction of a second; hv-t8o8.70), and in the release stage.
- **Test runner:** F4 times `cargo-nextest` against `cargo test` on the
  workspace and adopts it only for the stages where it is measurably faster,
  recording the numbers in `metrics.md`.
- **The spike is gone:** F4 deleted `native/spike/`. Its code and README (the S0 and ES
  commands the appendix cites) are in history at `f37435a`
  (`git show f37435a:native/spike/README.md`); the ES evidence stays in
  `native/evidence/es/`.
- **Locally,** `native/scripts/check-fast` is the native half of the fast check
  ([Validation](#validation)): format, clippy and tests for the crates the diff
  touches, under 60 s warm.
- AGENTS.md gains a native section: `npm ci` only for TS changes; focused
  checks are `cargo test -p <crate>`; the native app needs no dev-server port;
  the capture permission needs one click from the user the first time, and
  agents record System-mode checks as pending until then; and the oracle's
  inputs stay frozen until D6 (see [Oracle fixtures](#oracle-fixtures)).

## Phase V: The Playable Slice

Bead V1 (`hv-t8o8.62`). One executor, about five hours, landing on `staging` by 18:30 on
2026-10-24 so the operator can play it at 19:00. Deliberately rough.

- **Where:** a `slice` module in the `app` crate, so `native/scripts/bundle`
  and the existing System Audio Recording grant apply unchanged. Game rules
  lived in the slice, not in `sim` or `render`. The pure crates' purity rules
  still hold; the slice may depend on `analysis`. G1 replaces the slice's own
  rules with the `sim` engine, and the slice becomes the app.
- **Reuse:** the phase 0 spike, deleted by F4 and still in history
  (`git show 57e846a^:native/spike/src/...`): the process tap (`tap.rs`,
  `coreaudio.rs`), the winit + wgpu window (`app.rs`) and output (`output.rs`).
  Live beats come from the `analysis` crate (A1 to A4), not the spike's
  analyzer.
- **The game:** the player moves with the keys; enemies approach and step on
  committed beats; a key pressed on a beat attacks, judged against the
  committed beat (an on-beat window of about ±90 ms); contact costs health; a
  score, a combo and a beat flash; the run ends on death or when audio stops
  for a few seconds, then restarts. Simple shapes are fine. No TS content,
  director, set pieces or exact tunings.
- **Order:** end to end first, then polish. Land early and often: each step
  is its own candidate.
  1. The tap feeds the analyzer on a thread; the window flashes on committed
     beats (about 15:30).
  2. Player, enemies stepping on beats, attacks and judging (about 17:00).
  3. Health, score, run end and restart; tuning by feel (about 18:30).
- **Fallback:** if System mode is blocked (permission, a device), a
  `--file <path>` flag plays a file through the app's output and taps it the
  same way. Record which mode was verified.
- **Process for V1:** no cold review; the gate stage must pass; one friction
  record with the last candidate. The operator's System Audio Recording click
  and any check needing the speakers go in `operator-pending.md` as usual.
- **Run it:** `native/scripts/bundle`, then open the `.app`; the bead's notes
  and `operator-pending.md` give the exact command.
- **Landed:** V1 (`hv-t8o8.62`) at 14:00, about 15 minutes of work, and V2
  (`hv-t8o8.63`, the atlases, effects and SFX, with `render::atlas` and
  `render::sprites` for the rest of the plan) at 14:21. Still:
  [native/evidence/v2/](../../native/evidence/v2/README.md).
- **The operator's feedback** is filed as beads under `hv-t8o8` with the label
  `velocity`; they come before plan beads (after `ci-fix`).

## Phase A: The Analyzer

A streaming port of `stream.py` and its modules. Each group is a struct with a
`push(frame)` step and bounded memory, whatever the song's length.

```rust
pub struct Analyzer { /* front end, rnn, dbn, ticks, onsets, structure, ... */ }

impl Analyzer {
    /// Feed 44.1 kHz mono samples; returns entries finished by this block.
    pub fn push(&mut self, samples: &[f32], out: &mut Vec<Entry>);
}
```

### Stage parity

Each stage is compared against Python on fixed inputs, within tight float
tolerances (about 1e-5 relative with an absolute floor of about 1e-7 for
features and activations, exact for discrete outputs given identical inputs).
The front end is fed Python's own decoded PCM (exported with the golden
data), because Vorbis and FLAC decoders differ in the last bits. Each stage's test feeds Python's
*own* upstream output into the Rust stage, so a near-tie upstream can never
cascade into a downstream mismatch.

| Bead | Stage | Matches Python's |
|---|---|---|
| A1 | Front end: framing with madmom's online origin, STFT, log filterbank, positive differences, stacking | Spectrogram frames |
| A1 | Online LSTM ensemble | Activations |
| A2 | DBN forward step, most likely state, period smoothing, commit rule, `biasS` | Beliefs per frame; committed beats given Python's activations |
| A3 | Ticks: evidence (from the beliefs), energy (running maximum), level hysteresis | Tick values |
| A3 | Superflux onsets, online peak picking, strong flags | Onsets |
| A4 | Hindsight: offline features of each 20 s window, BiLSTM ensemble, offline DBN Viterbi, kept span | Hindsight entries |
| A4 | The phase check (committed grid takes hindsight's phase) | Committed beats with phase shifts |
| A5 | Structure: features (chroma, MFCC, loudness), novelty, running threshold, jumps, key, ends | Changes, jumps, keys, ends |
| A6 | Accent forecasts | Forecasts |
| A7 | Bands, brightness, timbre, and every other group the freeze has | Those groups |

A1 measured ([native/evidence/a1/](../../native/evidence/a1/README.md)): on both
golden excerpts, fed in uneven blocks, the online log spectra are within 0.52 of
their tolerance and the activations within 0.27; the positive differences are
checked exactly as `max(now - back, 0)` on both sides, since a difference of
near-equal spectra inherits their float32 rounding.

A2 measured ([native/evidence/a2/](../../native/evidence/a2/README.md)): fed
Python's activations, the DBN's states, the smoothed periods, the evidence and
the committed beats are bit-equal to Python's on all three golden excerpts; fed
from PCM through A1, no state flips on the 12 s excerpts.

A3 measured ([native/evidence/a3/](../../native/evidence/a3/README.md)): fed
Python's upstream, ticks (evidence, energy, level) and onsets are bit-equal; the
whole analyzer from PCM reproduces Python's beats, energies, levels, onset times
and strong flags to the bit, with evidence and strengths within tolerance.

A4 measured ([native/evidence/a4/](../../native/evidence/a4/README.md)): fed
Python's upstream with D = 0, the offline beats, the hindsight entries and the
phase-checked commits are bit-equal; the whole analyzer from PCM too, inline or
on the worker; a full 20 s decode takes about 280 ms, well inside D = 670 ms.

A5 measured ([native/evidence/a5/](../../native/evidence/a5/README.md)): the
features are within 1e-10 (1 + |value|) of Python's (the loudness bit-equal), every
tick's key equals Python's (to the bit from Python's chroma, with numpy's BLAS
`corrcoef` reproduced), and on synthetic pieces up to 150 s with changes, jumps
and ends, their times are bit-equal (the change strengths within tolerance),
from the stage and from the whole analyzer; a change on real music is first
met in A7's corpus parity.

A6 measured ([native/evidence/a6/](../../native/evidence/a6/README.md)): fed
every committed stream's beats and onsets a frame at a time, the forecasts are
Python's to the bit (75 on 24 of 62 streams), as are those of synthetic cases
at the rule's edges and of a 40 s accented pulse through the whole analyzer.

A7 measured ([native/evidence/a7/](../../native/evidence/a7/README.md)): bands,
brightness and timbre within 1e-6 of Python's (onset bands equal); the stream
of audio cut at any point holds exactly the full stream's entries known by then,
in all nine groups; across all 61 corpus tracks the committed beats match
Python's within 70 ms at D = 0 (hit rates equal on 60, one 0.8 points under),
and the live D costs up to 3 points on 10 of 47 tracks measured. `ps analysis-parity` reports the comparison.

- librosa's chroma and MFCC use librosa's own matrices, exported with the models
  (`librosa.chroma`, `librosa.mel` and `librosa.dct` in `madmom.bin`), so the
  features differ from librosa's only by summation order (A5).
- Python resamples non-44.1 kHz files with a symmetric filter (about 2 ms of
  lookahead). Replay of a 44.1 kHz file needs no resampling. Live capture
  resamples 48 kHz causally with `rubato`. The difference is measured, not
  matched.

### Hindsight's live latency

Python stamps a hindsight entry at the end of its window, as if decoding took
no time. Live, it arrives later by the decode's duration. Since the phase check
feeds hindsight back into committed beats, the Rust analyzer applies the fixed
delay `D` from [Threads](#threads): a window ending at `T` is usable by the
phase check from sample time `T + D`, whatever the thread timing. That keeps
corpus parity and re-analysed session replays reproducible. Stage parity for
the phase check runs with `D = 0` to compare against Python exactly; corpus
parity runs with the real `D` and reports its cost.

### Corpus parity

`ps analysis-parity` (in the CLI) runs the Rust analyzer over every track's
audio at faster than real time, in blocks the size of capture buffers, and
compares the result with the committed `stream.json`. It runs in the release
stage only, with tracks in parallel (one track at a time decodes hindsight in
order, about 3.5 times real time), as a long batch job; a local command over
three tracks, outside `check-fast`, serves the bead while it iterates:

- committed-beat accuracy against each track's reference grid (as
  `stream.py`'s `accuracy` computes it): per track at most 1 point worse than
  Python's, and no worse in aggregate;
- evidence, energy and onset statistics, structure events matched within their
  stated tolerances, forecasts and timbre summaries;
- the real-time factor of each group and of the whole analyzer;
- the arrival delay of each group.

The release stage runs it on every track. The gate stage runs no corpus
parity at all; a bead changing the analyzer runs it locally on three tracks (a
synthetic track, the Galop and one corpus track).

### The stream's own causality

Python's causality test (the stream of audio cut at `X` equals the full
stream's entries known by `X − 0.05`) is ported to Rust and extended to every
group. Live, it holds by construction, but the test still catches any
whole-buffer normalisation.

## Phase B: The Sim

**Status (2026-10-24):** B1 to B5 landed bit-exact against the TS oracle and
are the sim's code from here on. B6 to B8 are replaced by
[Phase G](#phase-g-grow-the-slice): B6's director and set pieces become G2,
B7's evaluator becomes part of G3, and B8's checks become K1 without the
oracle. B6's unfinished work is on branch `b6-set-pieces` (`cb17679`) for G2 to
reuse. The rest of this section records how B1 to B5 were built.

A faithful port, written to be compared. The TS code is the specification:
each Rust module names the TS file and functions it ports, and keeps their
structure where that helps a reader compare them.

### What makes bit-exactness possible

- **Arithmetic:** JS numbers are IEEE f64, so Rust `f64` in the same order of
  operations gives the same bits. The port keeps the order of operations,
  including in sums.
- **Math functions:** V8 implements `Math.sin`, `cos`, `exp`, `log`, `atan2`
  and the like with an fdlibm port. Rust's standard library calls the
  platform's libm, which may differ in the last bit. The sim uses the `libm`
  crate (from musl, also fdlibm-derived) through one `jsmath` module. Phase 1
  writes test vectors from V8 for every Math function the TS sim calls, and
  `jsmath` must match them; where it doesn't, it gets a shim.
- **Integer tricks:** `|0` is ECMAScript's ToInt32: truncate, wrap modulo
  2^32, NaN and infinities to 0. Rust's `as i32` saturates instead, so a
  `to_int32` helper is used everywhere. `>>>` and `Math.imul` become explicit
  `u32` and wrapping operations. `hash32` hashes strings by **UTF-16 code units**
  (`charCodeAt`), not bytes.
- **Order:** JS `Array.prototype.sort` is stable, and so is Rust's `sort_by`.
  Comparators port exactly, including how they treat ties and NaN. JS `Map`
  and `Set` iterate in insertion order (`IndexMap`, `IndexSet`). Plain JS
  objects iterate integer-like keys first in ascending order, then string keys
  in insertion order; any object iterated as a map is checked for this.
- **Randomness:** `src/core/rng.ts` (sfc32 seeded by `hash32`) ports line for
  line, and every draw happens in the same order.

### Streaming without changing results

The TS sim builds its whole input before the run: the song map, context and
schedule as fixed arrays, set pieces from one pass over the whole stream, and
an event queue with every future event pushed at time 0. The Rust sim ingests
the stream as it arrives, and still produces the same events.

- **The event queue:** TS orders events by time, then a per-kind rank
  (`ORDER[kind]`), then push order (`seq`). Rust can't push events at time 0
  that it doesn't know yet. So each event gets a derived key that reproduces
  the TS order:

  ```rust
  /// Sorts events exactly as the TS queue's (s, ORDER[kind], seq) does.
  #[derive(PartialEq, Eq, PartialOrd, Ord)]
  struct QKey {
      t: OrderedF64,
      /// ORDER[kind], ported as is.
      order: u8,
      /// Where TS pushed it: one value per init() loop (beats, slots, strides,
      /// weapon fires from grantWeapon, set pieces, zone waves, landmarks, ...),
      /// in init's order, then RUNTIME for events pushed while running.
      phase: u16,
      /// Its index within that phase (e.g. the beat's index).
      index: u32,
      /// For RUNTIME events, a counter in push order.
      seq: u64,
  }
  ```

  Kinds pushed both at init and at runtime (such as `vacuumDrop`) need both
  paths mapped. Ingesting an entry is itself a sim stop at its `knownAt`, and
  the port defines and tests which comes first when an event falls at the same
  instant, matching the TS readers' `+ 1e-9` tolerances. Phase B4 proves the
  mapping on the oracle. Any TS ordering it can't reproduce is a bug to
  understand, never to paper over with tolerances.
- **Growable inputs:** the song map, section bounds, `gridTimes`, `gridSlots`
  and stride steps grow as committed beats arrive. Every reader indexes only
  entries that have arrived.
- **Set pieces as a state machine:** `setpieces.ts` decides each set piece at a
  `knownAt` moment, but reads later entries by index. Rust keeps the same
  decision rules and decides each one when every entry it reads has arrived.
  Where a TS rule clamps to the stream's last entry (`beats.length - 1`), the
  Rust rule uses `StreamEnd` when present (oracle Replay) and otherwise waits
  for the entry. The TS perturbation check never deletes entries, so it does
  not prove these reads causal: F2 runs the new truncation check on the TS game
  once, and every read it exposes joins the live exceptions.
- **Ingestion is snapshot state:** the sim's state holds, per group, how many
  entries it has ingested. A rollback restores those counters with everything
  else, and re-ingests from there.
- **Memos become running values:** the TS memos over the whole stream
  (`judging()`, `paletteSwitches`, the judged-time running totals) become
  incremental values, extended as entries arrive.

### The oracle harness

`ps oracle` runs every fixture:

1. Load the track's `stream.json` and the fixture's `keys.jsonl`.
2. Run the Rust sim in Replay by `FORMAT.md`'s procedure: keys queued at their
   times, each entry ingested when the sim's clock reaches its `knownAt`, each
   frame evaluated at `s` after advancing to `s + SPECULATE_S`.
3. Compare the event log and the frame hashes with the fixture's.
4. On a mismatch, print the first differing event or frame with ±2 s of
   context from both sides, and the dense frame nearest it.

Replay provides `StreamEnd`, so the oracle compares whole runs. The live
exceptions are the behaviours that differ when `StreamEnd` is absent; those are
covered by the truncation check, D1's tests and the QA checklist, not the oracle.

### The beads

| Bead | Ports | Done when |
|---|---|---|
| B1 | `core/*`, `jsmath` with V8 vectors, content schema and loader (serde, the zod rules as validation) | Every content file loads; the loaded structures' canonical hash equals the TS loader's (exported in phase 1) |
| B2 | Stream types, `StreamLog`, Replay feeding; reactive song map, context, schedule, all growable | The schedule's arrays, built incrementally to the end, equal the TS batch schedule exactly |
| B3 | `sim/*` modules: timing, strides, movement, aim, tiers, upgrades, world, the player's key handling | Unit parity on per-module fixtures |
| B4 | `simulate.ts`: state, the event queue with `QKey`, enemies, weapons, strikes, damage, healing, orbs | Oracle event logs match up to the first judging-dependent event |
| B5 | Judging: provisional ratings, `settle.ts` readings and settled groove, the gate, beatless credit | Oracle event logs match through judging on all core tracks |
| B6 (superseded by G2) | Director, detected sections, zone waves, set pieces as a state machine, finale and run end (with their exceptions) | Full oracle event logs match on the core set |
| B7 (superseded by G1, G3) | Recorder, emit (sounds, UI events), the evaluator (`evaluate.ts`, `reactive-look.ts`) | Frame hashes match on the core set; the corpus hash chains match |
| B8 (superseded by K1) | Virtual players; the causality checks | Players' key logs equal the fixtures' keys; the checks below pass |

B4 to B6 split `simulate.ts` by area. They touch one crate in sequence, so they
run one after another, not in parallel.

**Measured (B4):** the Rust simulation's own event-log categories match the TS oracle line for
line on all 15 full core runs, up to the first judging-dependent moment and, with judging and the
settled groove ported alongside, up to B6's first event (to 53 s on one corpus track); the gate
takes 5 s warm on a sim change ([evidence](../../native/evidence/b4/README.md)).
**Measured (B5):** judging, the settled groove and the tier match over every full core run's whole
length, and without hindsight on three TS runs ([evidence](../../native/evidence/b5/README.md)).

### Source modules

Every TS source file at `ts-freeze` (tests aside) and the bead that ports it (or, after B5, reads it as a reference), so no module falls
between beads (hv-t8o8.68, from the cadence retrospective: B1 and B2 lost time to files no bead
named). "Landed" means on `staging`. Line counts are at the tag.

| File (`src/`) | Lines | Bead | Notes |
|---|---|---|---|
| `core/geometry.ts`, `rng.ts`, `shaping.ts`, `songmap.ts`, `warp.ts` | 650 | B1, landed | |
| `content/schema.ts`, `content/loader.ts` | 1200 | B1, landed | B2 added the level loader |
| `reactive/stream.ts`, `reactive/songmap.ts`, `planner/context.ts` | 477 | B2, landed | `judging()` landed with B4 (`sim::reactive::judging`, growing tick by tick) |
| `sim/schedule.ts`, `sim/strides.ts`, `planner/musical.ts` | 580 | B2, landed | |
| `sim/timing.ts`, `movement.ts`, `aim.ts`, `gait.ts`, `tiers.ts`, `upgrades.ts`, `world.ts` | 650 | B3, landed | |
| `sim/player.ts` | 781 | B3 (key handling), landed; K1 (skill profiles) | |
| `sim/simulate.ts` | 3699 | B4, B5, G2 | B4 ported everything but set pieces, archers, the boss, zone waves, landmarks and the finale (G2); judging and the settled groove came with it |
| `sim/clone.ts` | 73 | Not ported | Rollback snapshots are Rust's `Clone` (B4) |
| `reactive/settle.ts` | 230 | B4, landed (ported with the engine); B5 verified it over whole runs | |
| `reactive/director.ts`, `sections.ts`, `setpieces.ts` | 498 | G2 | B4 took the population, roster and loudness blend |
| `sim/recorder.ts`, `planner/emit.ts`, `planner/camera.ts` | 1332 | G1, G3 | B4 took the camera rig (simulation state) and the recorder's simulation records |
| `plan/evaluate.ts`, `reactive-look.ts`, `types.ts`, `view.ts`, `sample.ts`, `tier-look.ts`, `effects-life.ts` | 2442 | G1, G3 | |
| `reactive/grade.ts` | 24 | G1, G3 | Read by emit and the recording |
| `mapgen/mapgen.ts`, `mapgen/openfield.ts` | 235 | G1, G3 | The open field's ground, read by the evaluator and G3's ground |
| `planner/plan.ts` | 390 | K1 | `planWithSim`: the virtual-player runs |
| `reactive/players.ts` | 76 | K1 | |
| `planner/checks.ts`, `reactive-checks.ts`, `parity.ts`, `reactive/plan-checks.ts` | 1486 | K1 | What `check:reactive` runs |
| `planner/play-checks.ts`, `planner/sim-checks.ts` | 572 | Not ported | `check:play` and `check:plan`'s own checks; reactive play's are K1's |
| `assets/types.ts`, `runtime/assets.ts` | 488 | G3 | V2's `render::atlas` covers the atlases |
| `runtime/renderer.ts`, `shaders.ts`, `font.ts`, `hud.ts`, `hud-lines.ts`, `overhead.ts`, `view.ts` | 1897 | G3 | |
| `runtime/audio.ts`, `live/sounds.ts` | 651 | G4 | |
| `runtime/app.ts`, `title.ts`, `play.ts`, `reactive.ts`, `sources.ts`, `reactive/tracks.ts`, `plan/grade.ts` | 1476 | G1, G6 | Menus, the title, the HUD's grade |
| `live/input.ts`, `live/runner.ts` | 341 | G1, G6 | |
| `live/recording.ts`, `live/session.ts` | 1083 | G5 (the growing recording the runner rewinds), D3 (sessions) | |
| `live/log.ts` | 352 | D4 | |
| `runtime/planCache.ts`, `runtime/planWorker.ts` | 78 | Not ported | IndexedDB and the web worker; the native app plans on its own threads |
| `runtime/debug-moves.ts` | 188 | Not ported | A web QA overlay; D5 decides if its numbers belong in the QA checklist |
| `main.ts`, `vite-env.d.ts` | 5 | Not ported | The web entry point |

### Causality checks

Ported from `scripts/check-reactive.ts`, run on the Galop and one synthetic
track in the release stage (the gate stage runs two cuts on the Galop):

- **Perturbation:** for each cut `C`, change every entry known after `C`. The
  run up to `C` must be identical.
- **Truncation (new):** delete every entry known after `C` instead, with no
  `StreamEnd` on either run. The run up to `C` must be identical. This is
  exactly the live situation. (Perturbation keeps `StreamEnd` fixed, as the TS
  check holds `knownLength` fixed.)
- **Negative control:** a test-only switch that leaks one future entry must
  make both checks fail.
- **Streaming equals batch:** the same Replay with every frame's arrivals
  delivered in random-sized bursts (while keeping each entry's `known_at`)
  must give the identical recording. This exercises the rollback path that
  live arrivals take.

### Virtual players and playability

The reactive players port from `src/reactive/players.ts` and the player
profiles in `src/sim/player.ts`. They read the stream's reference and are
available only in Replay and File. The release stage's playability check runs the
honest player and the masher on the Galop and asserts the frozen outcomes (the
honest player lives, the masher falls, grade bands), so the checks keep their
meaning once rules change after parity.

## Phase G: Grow the Slice

The slice (Phase V) is the app. Phase G grows it into the game, one playable
step at a time. Every bead:

- **keeps the app playable** and lands in small candidates, each passing the
  gate, and writes the exact run command in its notes;
- **reads the TS code as a reference** (`git show ts-freeze:src/...`), porting
  what plays well and simplifying freely. No oracle comparison, no bit-exact
  arithmetic: `jsmath` stays where B1 to B5 use it, and new code uses Rust's
  `f64` functions;
- **keeps the sim deterministic and causal:** pure functions of the stream and
  the keys, never reading an entry before its `knownAt`;
- **is verified** in File mode by the executor (a still from `--still`, a
  short muted run, the virtual-player smoke once K1 lands), and by the
  operator's play. What only the operator can check goes in
  `operator-pending.md`.

### The engine in the app (G1)

- The app runs the `sim` engine (B4, B5) in place of the slice's own rules,
  which are deleted.
- The analyzer's entries go into a `stream::StreamLog`, each stamped with its
  arrival time as `known_at`; key presses go into the sim's key queue with
  their `NSEvent` times converted to heard time.
- The first runner is the simplest that plays well: the sim steps to the
  current heard time every frame, and keys and arrivals apply at their times
  when they are not yet past. G5 adds speculation and rollback.
- The sim's state is drawn with `render`'s atlases and sprites, from a minimal
  view of the state (hero, enemies, projectiles, pickups), by a function in
  `eval`. G3 grows it into the full look.
- The SFX that V2 plays move onto the sim's sound events where the sim has
  them.
- The rules the sim lacks until G2 (set pieces, the finale, the run's end) get
  the simplest stand-in: the run ends on death or the slice's silence rule.

### Director, sections and set pieces (G2)

Ports `reactive/director.ts`, `sections.ts`, `setpieces.ts` and the rest of
`simulate.ts` (archers, the boss, zone waves, landmarks, the finale), starting
from B6's branch. Live first:

- every decision is made when the entries it reads have arrived, and nothing
  reads the stream's end or the song's length (no `StreamEnd`): the
  [live exceptions](#live-exceptions) table gives each live rule;
- the finale fires on a confirmed end (G6's rule), and the run ends there;
- no oracle tests. B4's and B5's oracle tests that this breaks are deleted.

### The look (G3)

Ports the evaluator and the renderer from `plan/evaluate.ts`,
`reactive-look.ts`, `runtime/renderer.ts`, `shaders.ts`, `font.ts`, `hud.ts`
and `overhead.ts`, in order of what the player notices:

1. the HUD with the bitmap pixel font: health, grade, the groove, the beat
   marker;
2. the open-field ground (`mapgen/openfield.ts`) and the camera;
3. particles and effects; the tier and palette look;
4. the custom filter and the blur, in WGSL;
5. overhead text and the menus' styling.

Offscreen capture (`--still`, and later `ps capture`) renders stills for the
bead's notes. Visual comparison with the TS game is by eye, side by side, not
by a metric.

**Landed** (stills in [`native/evidence/g3/`](../../native/evidence/g3/), by
eye against `native/oracle/stills/`): the HUD in `render::font`; the open
field's tiles and chunk props (`eval::field`); the content's effect recipes
(bursts, rings, atlas sprites, shake, flashes, floating text, a level-up);
`render::post`'s passes (soft lights from the hero, lamps and effects; a bloom;
the zone's grade; vignette on bars, ambient pulse on beats, heat haze,
aberration and shockwaves); the overhead display and an end card in the pixel
font. The frame-time re-measure with the drawable's presented time stays with
D5.

**Corrected** by the VFX parity bead (`hv-t8o8.84`, stills and the condensed TS
spec in [`native/evidence/vfx/`](../../native/evidence/vfx/)). G3 drew the
recipes' layers, but not as the TS game fires and draws them: no recipe per
cause, nearest-sampled rings, no procedural textures, and none of the fast lane,
the tier look, the weapons or the gems. In seven parts it added the open field's
zones and transform waves; the procedural textures (`render::proc`, smooth
sampled) with sprite rotation, skew and additive blending; the recipe each cause
fires (swings by kind and tier, hits by weight, deaths by kind with debris,
weapons, cleave bands, landings, level-ups, decals); the reactive fast lane
(`slice/lane.rs`: the pulse from the ticks' evidence, accent flash and punch,
onset bursts in the key's tint, press ripples, the ground glow, the horde's
squash and flinch, and the key's look on the grade); the tier look and the
weapons (`slice/tier.rs`); the gem field (`slice/gems.rs`); and enemies'
hit flash, knockback and fall, and the props' sway on the bar. Not done: the
free-movement count-in has no moment in the native flow (a run starts on music
already playing, with nothing held), the upgrade orbs' look, and arrows and slam
markers.

### Sound (G4)

- A Core Audio output unit renders a mixer of scheduled voices, from SFX
  decoded at startup to the device's rate.
- Sound events carry heard times; the mixer places them at `heard − L` on the
  output timeline, sample-accurately, and accepts cancel or move until a voice
  starts.
- The TS game's sound rules as a reference, including the percussive fade below
  the gate; the whole-song ducking becomes a causal envelope follower.
- In File mode the mixer plays the file, and the samples it renders feed the
  analyzer, so File mode runs the same path as System from the ring buffer on.

**Landed:** `audio::mixer` (voices at host times on the render timeline,
started on their sample, moved or cancelled until they start, started partway
while their onset is ahead and skipped after; the file under a duck; the
effects' bus, compressor and level; a limiter), `audio::follow` (the level
follower, causal: the last second's level against the last minute's 90th
percentile), and the app's `slice/sounds.rs` (the crisp-steel palette for the
simulation's sound causes, kills and weapons on the next sixteenth at least
`NEVER_LATE` (or the output's latency, if longer) after they are known, kills
sharing a slot as a multi-kill, faded by the evidence of their beat below the
gate, panned by the camera, scheduled at heard - `L_out` - onset). The played
file's samples feed the analyzer with each rendered buffer's timing. Until
G5's runner simulates ahead, a swing's strike and a step are known only as
they happen: they play as soon as the output can (about 60 ms late) and the
whoosh before a strike is dropped. Not yet: the director's palettes by
loudness, tuned palettes, and the voice budget. `--mute` renders everything
and sends silence, logging the sounds started, skipped and dropped.

### The runner (G5)

- Speculation with rollback, as `src/live/runner.ts` and
  [Arrivals roll back like late keys](#arrivals-roll-back-like-late-keys)
  describe: snapshots by cloning the sim state, rollback on late keys and
  arrivals, sounds scheduled from speculated state and moved or cancelled.
- The per-key-group input calibration from `src/live/input.ts`.
- The rollback cost measured against the [targets](#targets), and the fallback
  taken only if needed.
- Taken only when G1's simple runner shows a problem the operator can feel
  (late sounds, presses judged against stale state), or before D3, whose
  replays need the settled recording.

**Landed:** `eval::live::Engine` journals the simulation's records (rewound
with the snapshots), runs `max(0.2 s, L_out + margin)` ahead with a snapshot
every 1/30 s, and rolls back for keys and arrivals that land behind it; keys
later than the settling window are applied at the settled time and arrivals
stamped before it are restamped to it. The app widens the window to its key
delivery and calibration (40 ms + 50 ms + the largest offset). The view
settles only the records before the oldest snapshot's mark and lays the rest
up to now over them each frame; moments are passed on once; the sounds follow
the speculation, cancelled before they start when a rollback undoes them.
Per-key-group calibration is read from the settings file or the command line.
Cost ([evidence](../../native/evidence/g5/runner.md)): about 5x real time of
song re-simulated, about 20 ms of CPU per second, frames well under 2 ms; no
fallback needed. A key later than the settling window can't be compared with
a plain run (the sim's stops depend on when a future key was queued,
`hv-t8o8.80`).

### Runs on live audio (G6)

D1's rules ([Runs on live audio](#runs-on-live-audio-d1)): the listening
screen, automatic start and confirmed end, rebasing a run to its start, pause
ending the run, a device change ending the run, the end card and YOU FELL, and
a debug menu with File mode.

**Landed:** the app listens on start (the listening screen, the beat's
evidence against the gate) and starts a run on D1's rule (2 s clear after
1.5 s of quiet, an end, or the app's start) or on Enter; the last 20 s of
entries are fed to the new run rebased to its start (`slice/feed.rs`). Runs
end on YOU FELL and on the sim's confirmed end (both in the final state), on
Esc or losing focus (Paused), Backspace, a new output device (its latency
taken for the next run) or a new source; the end card says which. F1 shows a
debug menu; F2 plays the next bundled track (File mode), F3 listens to the Mac
again. For checks, `--headless --seconds N` runs it all with no window
(`--start-at`, `--switch-at` steer it).

### Checks without an oracle (K1)

Ported from `scripts/check-reactive.ts`, on the Rust sim alone:

- **Causality:** perturbation and truncation at cuts on the Galop and one
  synthetic track, with the negative control
  ([Causality checks](#causality-checks)).
- **Determinism:** the same Replay twice gives the identical recording; the
  same Replay with arrivals in random-sized bursts gives the identical
  recording (streaming equals batch).
- **A virtual-player smoke test:** the honest player and the masher on the
  Galop, asserting broad outcomes (the honest player usually lives, the masher
  usually falls) that a rule change may retune with a reason in the commit.

## Phase D: Live Play

### Runs on live audio (D1)

Built by G6 (bead `hv-t8o8.25`). The app idles on the listening screen. The analyzer always runs, so the tracker
is warmed up before a run starts.

- **Start:** automatically when the evidence has stayed at or above the gate
  for 2 s after at least 1.5 s of quiet (or after the previous run's end), or
  manually with Start. A run started mid-song is normal.
- **The run's clock:** run time is heard time minus the run's start `s0`, a
  fixed offset for the whole run. It never pauses.
- **What the sim sees at the start:** what it would have seen had the song
  begun at `s0`, except that the tracker is already warm. Every time is shifted
  by `-s0`, and per group:
  - committed beats, ticks, onsets, changes, jumps, ends and forecasts with
    `t < s0` are dropped. Before the first beat after `s0`, the sim is in its
    usual "no beat yet" state, as TS is at song time 0;
  - entries with `t >= s0` but known before `s0` (beats committed ahead) are
    kept, known at run time 0;
  - hindsight entries known from `s0` on are kept, with their beat lists
    clipped to `t >= s0`, as Python's windows clip at the file's start;
  - timbre entries known from `s0` on are kept as they are. They may summarise
    audio from before `s0`, a harmless difference from TS's first entries.
- **End:** a confirmed end, YOU FELL, or manual End. Then the end card;
  listening continues, and the next song starts a new run.
- **A confirmed end.** An `ends` entry fires after any 1.5 s of quiet, and TS
  only accepts ends near the known length. Live, an end counts once the quiet
  has held for a confirmation window (3 s from its start, tuned in D1) with
  the evidence below the gate, after at least 30 s of judged time in the run.
  A breakdown that resumes within the window cancels it. A silent breakdown
  longer than the window ends the run: an accepted cost, logged. If the TS work
  landing before the freeze ("confirmed endings" in reactive-feel.md's G3)
  already defines such a rule, the port uses that instead.
- **Pause ends the run.** Esc or losing focus freezes the game and shows the
  end card, marked Paused, with the grade so far. The music has moved on, so
  there is nothing to resume into: listening continues, and the next clear beat
  starts a new run. Freezing run time across a gap was rejected: committed
  beats either side of the gap would sit next to each other at an arbitrary
  interval, which breaks every rule that counts beats.
- **An output device change ends the run** the same way. Entries already in
  the stream carry heard times computed with the old latency, so the beat grid
  would jump by the difference. The next run uses the new device's latency and
  calibration.
- **Gapless and crossfaded playlists** may produce no detected end. The run
  continues across the songs, which is the endless session's behaviour in
  miniature. Manual End is the fallback.

### Live exceptions

The differences from the TS game that live play forces, because the game no
longer controls the music. Other differences are free choices (see
[Decisions](#decisions)) and need no entry here. F1 completed this list from the freeze commit (`ts-freeze`, `e97ef4a`); the
file-by-file inventory behind it is in the
[appendix](#measured-f1-the-inventory-at-ts-freeze).

| Behaviour | TS at the freeze (as written now) | Native |
|---|---|---|
| Finale | `reactiveFinale` (`setpieces.ts:93`): the first `structure.ends` entry with `knownAt` within the last `windowS` (10 s) of `knownLength`, firing at its `knownAt`, else at `knownLength` itself; no evidence gate. reactive-feel's confirmed endings (G3) never landed | Fires on a confirmed end (D1): the horde clears and the end card follows |
| Run end | `reactiveRunEnd` = `max(duration + 0.5, finale.endS)`, emitted once song time reaches `ctx.duration` (`recording.ts:746`); the sim runs to `max(duration + 1, finale.endS)`; the grade covers `[playerFrom, duration)`. YOU FELL is a pause menu with restarts, not an end | A confirmed end, YOU FELL, pause, a device change, or manual End; the grade covers the run's own span |
| Song length and stream-end reads | `knownLength`, `audio.duration`, `ctx.duration`, `progressS`, the last section's `s1`, the bar count from every beat, clamps to the last entry (listed in the appendix) | `StreamEnd` absent: progress shows elapsed time, the section marks go, the last section is open-ended, `endS` is unbounded until the run ends, and every clamp uses the latest known entry |
| Events past the last committed beat | Dropped or brought forward: a section start, a set-piece candidate or a palette switch whose beat isn't in the stream is dropped (`sections.ts:46`, `setpieces.ts:141`, `director.ts:78`), so a boss whose drop-in downbeat (`sEnter`, `setpieces.ts:155`) isn't committed yet loses its slot to a ring; `downbeatAtOrAfter` (`simulate.ts:2515`) returns the time itself past the last bar, so a vacuum drop or a level-up waiting for the next downbeat fires at once; the kick line reads the beats of its entrance ahead. F2's truncation check exposed each of these | Pending until that beat arrives |
| Extrapolation past the last beat | `core/songmap.ts` extrapolates from the last two beats' gap; `knownTimeOfBeat` from the last beat's `period` | Live: the latest beat's `period` everywhere |
| Archer groups' end | `volleyGroup` (`setpieces.ts:313`) sets a group's `until` from its whole volley run at its start, reading the beats, levels and evidence of every later draw, and drops a volley drawn before the stream's end whose arrival beat (`beats[i + 1]`) isn't in it: the spawn mix and the archers' shots before a cut change when later entries are deleted (F2's truncation check) | Live: the group lasts while its volleys continue, each draw deciding the next, and ends at the first draw that fails (a beat or more after TS's `until`, which knows the failure in advance) or at a run end. |
| Scheduling | `init` enqueues every beat, slot and set piece up front (`simulate.ts:3253`) | Each entry is enqueued when it arrives ([streaming without changing results](#streaming-without-changing-results)) |
| Effects ducking | `followCurve` (`audio.ts:36`): a centred 0.4 s RMS window, centred 1 s smoothing and the whole song's 90th percentile (the music ducking under SFX is causal and kept) | A causal envelope follower on the energy ticks |
| Run start | The song's start, song time 0, or `?start=` (the reference-beat bot plays until the hand-over) | Any time; rebased history (above). The judged-from time (16 s) and the title card count from the run's start |
| Pause | Freezes the music and the game; resumes after a one-bar count-in (`play.ts:407`) | Ends the run (D1) |
| Output device change | — (the browser handles it) | Ends the run (D1) |
| Restart section, Restart song | Restart the audio (in reactive mode Restart section is Restart song) | Removed: the game doesn't control the music |
| Best grade | Per track, in local storage (`ps.grade.v2.reactive.<trackId>`) | Kept for Replay and File tracks only; live runs have none |
| Watch my run | Re-simulates with the full stream (`play.ts:95`), the bot's part before a hand-over included | Replays the saved session (stream with arrival stamps, keys, audio) |

### Calibration (D2)

- **Input offset:** the existing Calibrate (tap along to 8 clicks at 120 BPM),
  per key group, as today.
- **Visual offset:** a flash and a click repeat on a beat, and the player nudges
  the flash until it lands with the click. That measures display latency.
- **Output latency:** Core Audio's reported value, plus an optional per-device
  correction from a tap-along test against the app's own clicks.
- Calibrations are saved per output device (by its UID) in
  `~/Library/Application Support/PlaylistSurvivors/settings.json`. The app
  re-reads the latency and switches calibration when the default output device
  changes mid-session. A Bluetooth output shows a notice that sync is
  best-effort.

**Landed:** `slice/calibration.rs` keeps a calibration per output device UID
(input and stomp offsets, display offset, latency correction; top-level values
are the default, so G5's flat file still reads). On the listening screen, C
runs the tap-along (8 clicks at 120 BPM, the median error after the first two
taps within a quarter beat, saved for the group most taps came from) and V the
visual test (a flash and a click on each beat; left and right nudge the flash
by 5 ms, Enter saves). Frames are drawn the display offset later, and the
settling window grows by it. The latency correction is added to the reported
output latency; it has no test of its own, since a tap-along against the
app's clicks measures the input offset the same way, so it is set in the file.
A device change switches calibration and latency; a Bluetooth output shows the
best-effort notice.

### Session recording (D3)

Every session (from listening start to quit) is saved to
`native/logs/sessions/<timestamp>/`, gitignored, keeping the last 20 and at
most 5 GB in all, oldest deleted first. Audio costs about 3 MB a minute, so a
three-hour listening session is about half a gigabyte:

- `audio.flac`: the analyzer's input (44.1 kHz mono, 16-bit);
- `stream.jsonl`: every entry with its arrival stamp;
- `keys.jsonl`: every key event with its session time;
- `meta.json`: app version, output device, its latency, calibrations, runs and
  their bounds.

`ps replay <session>` re-runs it headlessly in two ways:

- **with the recorded stream:** bit-exact, the same settled recording (every
  event) as the live run, and frames re-evaluated from it by `FORMAT.md`'s
  procedure. This is the test that the live runner's rollbacks settled
  correctly. The frames the player actually saw were drawn from speculative
  state and are not reproduced;
- **re-analysed:** the recorded audio through the current analyzer, for
  regression-testing analyzer changes on real listening sessions. The audio
  is stored at 16 bits, so this is close to, not identical with, what the
  analyzer heard live; compare re-analyses of the same file before and after a
  change.

Watch my run on the end card replays the run from the session in memory.

**Landed:** `slice/session.rs` records every session (from listening's start
to quit or a new source; the analyzer's input as WAV, converted to FLAC when
it closes; `stream.jsonl` of `eval::session::Arrival`s; `keys.jsonl` of the
keys as the engine applied them; `meta.json` with each run's bounds and the
FNV-1a hash of its settled records). `eval::session` holds the shared rebasing
rule and `replay_run`; `ps replay <session>` replays each run and checks the
hash, `--reanalyse` puts the audio through the current analyzer and reports
the outcome against the live stream's. A 14 s session with a press on every
beat is committed in `native/fixtures/session-house/` and replayed in the gate
(`eval/tests/session.rs`; recorded again whenever a deliberate sim or content
change moves what settles). On the end card, W replays the run on a fresh
engine from its own arrivals and keys. For checks, `--press-beats` stands in
for a player and `--watch` watches each run as it ends.

### Timing logs (D4)

The play log from `src/live/log.ts` (and its summary command) ported to the app
and extended with the latency chain, per frame or per event:

- capture buffer host times and sizes;
- per stream group: arrival stamp minus content time;
- key: `NSEvent.timestamp` to main thread;
- rollbacks: count, depth, re-simulated seconds, and cause (key or arrival);
- frame: sim, evaluate and encode time, present lateness against the display
  link's target;
- sounds: schedule margin (how long before output each voice was committed),
  cancellations;
- output latency and device changes.

`ps log [<path>] [--from s] [--to s] [--no-table]` summarises one, like
`npm run log:play`. F8 saves the current log beside the session.

**Landed:** `slice/timing.rs` writes `timing.jsonl` beside the session (kinds
`cap`, `arr`, `key`, `rb` with the key-caused share and depth, `frame`, `snd`
with the schedule margin, `cancel`, `dev`, `run`, `end`; the engine counts its
rollbacks by cause). `ps log` (`cli/src/log.rs`) summarises the newest
session's, or a given one: arrival lag per group, key delivery, rollbacks a
second, frame work, the sounds' least margins; a table per second unless
`--no-table`. F8 flushes it and prints its path. Present lateness against the
display link is not logged yet (no display link in the app: the encode time
stands in), left to D5.

### The latency report and QA (D5)

`ps report <session>` prints the latency chain's distributions for a session
against the [targets](#targets). D5 runs the [QA checklist](#qa-checklist) as
far as an agent can and records the results. It never waits for a person:
anything that needs one (an AirPods test, judging sync by ear) is noted for the
user's ordinary play.

### Removing the TS game (D6)

After D5:

- Remove the web game: `src/`, the TS tests, `index.html`, Vite and Playwright
  configuration, and the TS scripts. First port any script still needed (corpus
  ingest moves to the Python tool or the `ps` CLI).
- `scripts/ci-gate`, `scripts/ci-full` and `scripts/check-fast` lose their TS sections. AGENTS.md drops the web workflow (dev
  ports, play logs from the browser).
- The `ts-freeze` tag keeps the TS game reproducible, and with it the fixtures.
- `tools/analysis`, `content/`, `levels/`, `public/assets`, `public/reactive`
  and `corpus/` stay. They are the app's content and the analyzer's oracle.

## Phase E: Delayed Listening

An opt-in output mode that trades a few seconds of delay for better beats.
Parity does not wait for it: D5 and D6 never depend on its beads, and E2 may
land before or after them.

### Why

In Direct listening, committed beats are forecasts. They must be known at
least the commit horizon `H` (1 period plus 0.35 s) before they are heard, and
the analyzer has only `L` of audio past the present. On the Galop's pulsed
sections that put 67% of committed beats within 45 ms of the song map, against
84% when the experiment's forecast was only 0.25 s ahead. Hindsight grids,
which hear both sides of each beat, matched 92% (online) and 97% (offline) on
steady beat-driven tracks ([reactive-audio.md](reactive-audio.md#measured-stage-1)
and its appendix).

Delaying the music by `M` lets the analyzer hear about `M − H` seconds past
each beat before it must be committed. With `M` near 3 s that is the 0.5–2 s
of hindsight where the measured accuracy plateaus.

What it does not fix: tempo-octave ambiguity. The offline decode settles on
half tempo on the Galop and on much orchestral music. Readings chosen from
presses still decide that, exactly as in Direct listening.

### What the player sees and hears

- On the listening screen, **Listening: Direct / Delayed**. Delayed shows the
  apps currently producing audio and taps the one the player picks; the
  default is the app that most recently started playing.
- Entering Delayed listening gives `M` seconds of silence, then the music
  resumes from the app, late. A note says the music is delayed by `M` s.
- Leaving it unmutes the app live. The `M` seconds still in the delay line are
  dropped with a 50 ms fade.
- Switching mode, or switching the picked app, ends the run, as a device
  change does: heard times jump by `M`.
- When the picked app is a browser, a notice warns that its video will run `M`
  ahead of its sound.
- Pausing, skipping or seeking in the source app is heard `M` late. That is the
  accepted cost of the mode.
- Other apps are not tapped. They are heard live and not analysed.

### Capture and mute

- A **per-process tap** (`CATapDescription` over the picked app's process
  objects) with `muteBehavior = CATapMutedWhenTapped`. The app is muted only
  while the tap's IOProc runs, and macOS unmutes it when the tap is destroyed,
  including when the process exits (E2 verifies both).
- The app changes no volumes and stores no mute state. So nothing can stay
  muted after a quit, a crash or a relaunch; E2 proves this with `SIGKILL`.
- **Watchdog:** if the delay line underruns for 0.5 s, or the main thread has
  not checked in for 2 s, a watchdog thread destroys the tap, which unmutes the
  app. The app falls back to Direct listening, ends the run, and logs why. So
  a hang never leaves the app muted for more than about 2 s.

### Playback

- The tap's IOProc writes the full-quality stereo capture into a second
  lock-free ring, the **delay line**, beside the analyzer's ring. The delay
  line is sized `M` plus 1 s, so about 2 MB at 48 kHz.
- The mixer plays the sample captured at host time `h` at output time `h + M`,
  through the two clocks' delay-locked loops. When the tap's device and the
  output device differ (AirPods, say), a drift-correcting resampler (`rubato`)
  holds the delay at `M`.
- The music and the game's sounds then share `L_out` exactly. Calibration's
  output latency only matters for presses. On Bluetooth, the game's sounds are
  in sync with the music by construction; only presses need the per-device
  correction.
- **File source:** Delayed listening also works with the File source. The
  mixer plays the file `M` late, and the analyzer reads it as it is decoded.
  That tests everything but the tap, without the capture permission.

### Stamps: every group as Python stamps it, except committed beats

In Direct listening, an entry's `known_at` is its arrival stamp. With `M` of
lookahead that would hand the sim ticks, onsets and structure seconds before
the audio they describe is heard. The sim's readers were never written for
that.

So in Delayed listening, each entry's `known_at` is the **heard time of the
analysis position that produced it**, which is exactly Python's `knownAt`
semantics. It is always later than the entry's arrival, so the stream is
Replay, live. Hindsight keeps its fixed delay `D`. If a stall makes an entry
arrive after its stamp, the runner uses the arrival, as it does for very late
keys, and logs it.

**Committed beats are the one exception.** They are stamped `M − A` earlier
than the analysis position that produced them, which is how they spend the
lookahead (`A` is defined in [E1](#committed-beats-from-hindsight-e1)). The stamp stays a pure function of the samples, so session replay
and the causality checks keep working.

The sim, the evaluator and the oracle do not change. The sim reads every entry
by `known_at` as before. Beats simply arrive with better times.

### Testing it now (E0 and ES)

Two beads test phase E's two big unknowns before anything waits on them. Both
need nothing else in this plan, so they run now, alongside the TS wrap-up.

**E0: the payoff, measured offline.** Every committed `stream.json` already
holds its track's hindsight entries (online and offline grids, each with its
`knownAt`) and a reference grid. So the delayed commit rule of
[E1](#committed-beats-from-hindsight-e1) can be replayed in Python over those
entries, with no audio and no Rust:

- for each `M` in {1.5, 2, 2.5, 3, 4, 5} s, and each grid (online, offline, and
  online with the phase check's offline phase), commit beats from each
  hindsight entry stamped `k = T + D − M + A`, using S0's `D` (670 ms) and `A`
  (48 ms), with the online forecasts as the fallback, exactly as E1 specifies;
- score committed-beat accuracy (within 45 ms of the reference) per track and
  per corpus category, against Direct listening's committed beats in the same
  stream;
- write the table to the appendix, propose `M` and the grid by E1's rules, and
  give a verdict.

E0 lives in `tools/analysis` (`ps-analysis delayed-payoff`), adding a module
and a subcommand without changing the stream code. **If Delayed listening is
not better than Direct in aggregate,** E0 records that, and E1 and E2 are
cancelled with that reason. Otherwise E1 starts from E0's `M` and grid and
confirms them with the Rust analyzer (Python's hindsight entries carry no
decode delay, which E0 models only through `D`).

**ES: the platform, spiked.** A throwaway extension of S0's spike in
`native/spike/` (F4 deletes the directory after ES closes):

- a per-process tap with `CATapMutedWhenTapped` over Music, `afplay` and a
  browser: the app goes silent while tapped and audible when the tap is
  destroyed;
- `SIGKILL` of the spike while tapping: the source app becomes audible again,
  checked by loopback (speakers into the built-in microphone);
- the delay line: the captured stereo played `M` late through AUHAL, the delay
  measured by loopback once a minute over an hour on built-in speakers, with
  and without a drift-correcting resampler between two devices if a second
  device is available;
- the watchdog: a stalled main thread destroys the tap within 2 s.

ES records the numbers in the appendix and turns what it learns into notes on
E2's bead. If muting proves unsafe (the app can stay muted after a crash), ES
says so and E2 is rescoped before anyone builds it.

### Committed beats from hindsight (E1)

The analyzer gains `Listening::Delayed { m }`. In it:

- **Source of beats:** when a hindsight decode of the window ending at sample
  time `T` becomes usable (at `T + D`), it is stamped
  `k = T + D − M + A`, where `A` is the capture → arrival target from
  [Targets](#targets), so the stamp is never earlier than the decode's
  arrival. Its beats with `k + H <= t < k + 1 + H` that come at least half a
  period after the last committed beat are committed with `known_at = k`.
  Each is known at least `H` before it is heard. The analyzer has already
  heard between `M − A − D − H − 1` and `M − A − D − H` seconds past it.
- **Which grid:** online hindsight, offline hindsight, or online with the
  phase check's offline phase. E1 measures all three across the corpus and
  picks the best in aggregate that is no worse than Direct listening on any
  corpus category. It records the table in the bead and in
  `native/evidence/e1/` ([Measurements](#measurements)).
- **Fields:** `period` from the chosen grid's local spacing; `evidence` from the
  beliefs around `t`, which the analyzer has already heard. The gate and
  judging read them as before.
- **Fallback:** if a band has no beat from the decode (a beatless stretch, low
  evidence, or a decode stall), its beats come from the online tracker's
  forecasts, committed as in Direct listening and stamped by the same rule. So
  the beat stream never stalls.
- **Choosing `M`:** E1 measures committed-beat accuracy (within 45 ms of each
  track's reference grid) at `M` ∈ {1.5, 2, 2.5, 3, 4, 5} s over the corpus.
  `M` is the smallest value within 1 point of the best aggregate, rounded to
  0.5 s. It is a constant: players don't tune it.
- **Headless:** `ps analysis-parity --delayed <m>` runs the analyzer in Delayed
  listening and reports accuracy per track and category against Direct
  listening and Python.
- **No Python oracle.** Python has no delayed rule. Stage parity covers the
  pieces E1 reuses (hindsight, the forecast commits), and corpus accuracy
  against the reference grids judges the rule itself.
- **Measured (E1, `hv-t8o8.31`):** offline hindsight at `M = 3 s`, 85.9% of
  committed beats within 45 ms over the 61-track corpus against Direct's 78.6%
  at the same `D`, and no category worse (classical 84.4 against 62.8). The
  lead holds on every track. Forecasts that fill a band are known with the
  band at `k`: E0's earlier stamps would precede the decode that chooses them.
  A forecast that can't lead by `H` at its own period isn't committed. The
  table and its caveats (half-tempo recall on a few tracks) are in
  [`native/evidence/e1/`](../../native/evidence/e1/README.md).

### Checks

- **Lead:** every committed beat in a Delayed stream has
  `t − known_at >= H` at the period believed at commit time. This runs in the
  release stage on every track.
- **Stream causality:** the ported test compares the stream of audio cut at
  `X` with the full stream, matching entries by the audio position that
  produced them (`known_at`, or `known_at + M − A` for committed beats), not by
  `known_at`.
- **Sim causality:** perturbation, truncation and streaming-equals-batch also
  run on a Delayed stream of the Galop. They need no change, because the sim
  only reads by `known_at`.
- **Sessions:** a Delayed session in File mode replays bit-exactly. `meta.json`
  records the listening mode, `M` and the picked app, and `ps replay`'s
  re-analysis takes the mode from it.
- **Timing logs** gain the delay line's fill level and underruns, the watchdog's
  trips, and the measured delay against `M`.

### The beads

| Bead | Work | Done when |
|---|---|---|
| E0 | `ps-analysis delayed-payoff`: E1's commit rule replayed in Python over every committed stream's hindsight entries; accuracy by `M`, grid and category against Direct listening | Table in the appendix; `M` and grid proposed by E1's rules; a verdict, with E1 and E2 cancelled if Delayed is not better in aggregate |
| ES | The muting tap, the delay line over an hour, `SIGKILL` and watchdog behaviour, in the spike | Numbers in the appendix; muting proven safe, or E2 rescoped |
| E1 | `Listening::Delayed` in the analyzer: hindsight commits, stamps, fallback, the grid choice and `M` by rule, `--delayed` in `ps analysis-parity`, the lead and causality checks | `M` set and recorded; Delayed accuracy beats Direct in aggregate and is no worse on any category; checks in the release stage |
| E2 | The mode in the app: per-process muting tap, delay line, drift-correcting playback, watchdog, the listening-screen choice and notices, File-source Delayed, session meta, timing-log fields, QA items 14–19 | QA items pass, or are recorded as pending the capture grant; a `SIGKILL` leaves the source audible |

## Targets

Set by the spike's executor from the phase 0 numbers
([appendix](#measured-phase-0)), by the rules in
[Setting the targets](#setting-the-targets). Gates are p95 unless stated.

| Link | Measured in phase 0 | Target | Gate or report |
|---|---|---|---|
| Capture → arrival (beats, ticks, onsets): the app's share, IOProc run → published | median 0.58 ms, p95 0.97, max 28 | ≤ 2 ms | gate (0.97 + 50%) |
| Capture → arrival, from the tap's timestamp | median 26.9 ms, p95 31.8, max 78; Core Audio delivers each buffer 31.3 ms after its timestamp (512-frame buffers) | ≤ 48 ms (31.8 + 50%); this is E1's `A` | report (the device and its buffer size set most of it; a later arrival falls back to its arrival stamp) |
| Hindsight decode → arrival | decode median 146 ms, p95 240, p99.9 332 (1000 decodes); beside the live app p95 131–188 (three runs, 20–40 decodes each) | decode ≤ 360 ms; entries usable at `T + D`, `D = 670 ms` | gate on the decode (240 + 50%); `D` fixed (332 × 2, rounded up) |
| Analyzer real-time factor (all groups) | beat group only: online path 0.005 (bench), 0.010–0.015 live; hindsight 0.146; together about 0.16. Ticks, onsets, structure, accents, bands: not yet ported | ≤ 0.5, hindsight included, one core | gate |
| Key event → main thread | median 5.1 ms, p95 30.5, max 41.7 (n = 185 events posted from a thread; real presses pending) | ≤ 46 ms | gate (30.5 + 50%); D5 re-measures on real presses |
| Main thread → present | to the first vsync after the present: median 15.6 ms, p95 15.7, p99 23.8 (120 Hz, Fifo, frame latency 1; a vsync extrapolated from the display link, not a measured present time) | ≤ 24 ms | gate (15.7 + 50%); G3 re-measures with the drawable's presented time and may loosen it once |
| Sim + evaluate + encode per frame (p99, 120 Hz) | G5 ([evidence](../../native/evidence/g5/runner.md)): p99 1.04–1.19 ms, p95 0.80–0.92 at 1920 × 1080 offscreen, under load; the engine alone p95 0.53–0.64 ms | ≤ 4 ms (half a frame) | gate at the target, p99 in thread CPU time: frame work ≤ 4 ms (`playlist-survivors --bench --check`), engine ≤ 4 ms (`ps runner-bench --check`), in the release stage (p95 + 50% flaked under load: [evidence](../../native/evidence/g5/runner.md#gates-after-the-cold-review)) |
| Rollback re-simulation per second | G5: p95 17–20 ms of CPU per second (4.8× real time replayed, 0.2 s lead, presses 5–35 ms late) | ≤ 250 ms of CPU per second | gate: the song re-simulated per second of play ≤ 7.2× (measured 4.8× on every run), and p95 ≤ 250 ms (the target), `ps runner-bench --check`, in the release stage |
| Committed beat → heard beat (steady tracks) | house-128: clicks at the ear +4.7 ms median, \|e\| p95 9.7 ms, max 11 | — | report (set by the commit horizon) |
| Output latency | built-in speakers 16.6 ms reported (27.3 with the 512-frame buffer); wired and AirPods not yet measured | — | report (hardware) |
| Delayed listening: committed-beat accuracy against Direct, per corpus category (E1) | E0, replayed from Python's hindsight entries: offline grid at `M = 3 s` 86.1% within 45 ms against Direct's 79.0%, every category better ([appendix](#measured-e0-delayed-listenings-payoff)) | | report; E1's rule |
| Delayed listening: measured music delay against `M`, over an hour (E2) | ES, the spike's delay line at `M = 3 s`: per-minute medians spread 0.02 ms over 60 minutes, no underruns (stability; heard `M + 23 ms` late, phase 0's tap-versus-render offset, which E2 subtracts; loopback pending, [appendix](#measured-es-delayed-listenings-tap-and-delay-line)) | | gate at ±2 ms |

## Automated Checks

### Gate stage (about 60 s, blocks promotion)

- `cargo fmt --check`; clippy and unit tests for the crates the diff touches
  and their dependents.
- B1 to B5's oracle smoke while it passes (a deliberate rule change deletes
  it).
- A causality smoke: perturbation and truncation at two cuts on the Galop
  (from K1).
- Whatever else fits the budget, as F4 and the retrospectives measure it.
  Anything that pushes the gate stage over budget moves to the release stage:
  tests that run whole excerpts through slow stages carry
  `#[ignore = "release: ..."]`, and the gate keeps a short smoke test of each
  such path (hv-t8o8.59: the analyzer's hindsight-decoding parity tests moved,
  taking the analysis crate's gate check from about 34 s to 3 s).

### Release stage (asynchronous, moves `release`)

- The whole workspace's clippy and tests.
- **Analyzer:** A1 to A6's stage parity while it passes; corpus accuracy on
  every track against the reference beats and Python's, with the per-track
  table; the stream causality test.
- **Sim:** B1 to B5's oracle tests while they pass; determinism (the same
  Replay twice, and in random-sized bursts, gives the identical recording).
- **Causality:** perturbation, truncation and the negative control on the
  Galop and one synthetic track.
- **Playability:** the virtual-player smoke test on the Galop (K1).
- **Sessions:** a small committed session (recorded by the executor in File
  mode) replays bit-exactly with its recorded stream.
- **Delayed listening (from E1 and E2):** the lead check; the stream causality
  test by audio position; sim causality on a Delayed Galop stream; a small
  Delayed File-mode session replays bit-exactly; `ps analysis-parity --delayed
  <M>` over every track against Direct listening.
- **Renderer:** a still renders headlessly without error.
- `ps bench`: sim, evaluator and rollback costs on the Galop's busiest minute.
- A release build of the app.
- The TS checks, while the TS game exists.

## Risks

| Risk | Mitigation |
|---|---|
| The tap's API isn't covered by Rust bindings | The spike proves it; a hand-written `objc2` binding is small |
| The capture permission resets every rebuild | A stable self-signed identity signs every bundle (phase 0) |
| madmom's front end has undocumented details (frame origin, filterbank normalisation, padding) | Stage parity against Python per stage, with Python's upstream fed in |
| Hindsight too slow live, or too late for the phase check | Measured in the spike; its own worker; the spike's executor picks a workable form by rule |
| Rollback on every arrival costs too much | Measured in G5; two fallbacks named |
| Bluetooth latency reported wrongly | Best-effort with per-device calibration, as decided |
| `simulate.ts` is large and dense | B4 and B5 ported its core bit-exact; G2 ports the rest from B6's branch, live first |
| Without an oracle, port mistakes go unnoticed | The app stays playable after every bead and the operator plays it; causality, determinism and virtual-player checks (K1); the TS game stays runnable at `ts-freeze` to compare by eye and ear |
| The game drifts away from what made the TS game feel good | Tunings and content stay data in `content/` and `levels/`; the TS code is the first reference for every system G ports; feel problems are filed as `velocity` beads |
| TS reads the stream's end or the song's length in ways the perturbation check never tested | `StreamEnd` in Replay; the truncation check run once on TS in F2; each read listed with a live behaviour |
| False ends from silent breakdowns | A confirmation window and a minimum judged time (D1); logged when they happen |
| The tap's latency differs from the output's | Measured separately by loopback in phase 0; two constants |
| An output device change mid-run shifts the grid | The run ends; the next run uses the new latency |
| Thread timing leaks into analysis results | Hindsight usable at a fixed sample-time delay `D`; the analyzer is a pure function of its samples |
| Delayed listening leaves an app muted | The mute exists only while the tap does (`CATapMutedWhenTapped`); no volume or mute state is stored; a watchdog destroys the tap on underrun or a stalled main thread; E2 proves a `SIGKILL` restores the sound |
| Delayed listening's lookahead buys less than hoped | E1 measures it before any app work; `M` set by rule; if Delayed is not better than Direct in aggregate, E1 records that and E2 is cancelled |
| Delayed listening confuses a browser's video or transport controls | Taps only the app the player picks; a notice for browsers; the delay is the accepted cost of an opt-in mode |
| The tap's and output's clocks drift apart over a long session | The delay line is played through both clocks' fits with a drift-correcting resampler; the measured delay is gated |
| Streaming services' terms on capturing playback | The delayed audio stays in memory and is never saved beyond the session recording's analyzer input, which Direct listening already keeps; the app runs only on the developer's machine |

## Alternatives Considered

- **A Tauri or Electron shell around the TS game, with a Rust audio core.**
  Far less work (an incremental TS refactor of about 3–4k lines) and every TS
  check keeps working. Rejected by the user in favour of a full rewrite. Note
  also that WebKit plays a webview's audio from a helper process, so excluding
  the game's own sounds from the tap would have needed native sound output
  anyway.
- **Bevy, or an ECS for the sim.** Rejected: rollback needs cheap whole-state
  snapshots and the oracle needs deterministic iteration order, and an ECS
  framework fights both. At this game's scale, plain dense arrays are as fast.
- **A Python sidecar for live analysis first.** Bundling isn't a concern on
  the developer's machine, but hindsight runs slower than real time in Python,
  and the sidecar would be thrown away.
- **ONNX for the nets.** A heavy native dependency for very small LSTMs.
- **A bit-exact port against a TS oracle.** Chosen at first, and B1 to B5 were
  built that way: the oracle caught real port mistakes, but V8-exact maths,
  UTF-16 hashing, queue-key orderings and frame hashes reproduced accidents of
  the prototype, and parity gaps drove most review findings (about 70 across
  the parity beads). Abandoned on 2026-10-24, once the playable slice showed
  the end-to-end path worked, in favour of growing the slice with the TS code
  as a reference.
- **Delaying the music for lookahead.** Rejected in reactive-audio.md for the
  browser game. Adopted here as the opt-in [Delayed listening](#phase-e-delayed-listening),
  because the tap's mute is released with the tap, so a crash can't leave an
  app silent.
- **Always delaying, or tapping all output for Delayed listening.** Rejected:
  a browser's video would run ahead of its sound, every notification would be
  delayed, and transport controls would always feel late.
- **Committing beats from a fixed lookback instead of hindsight decodes.**
  Rejected: the decodes already exist and are measured. A new lookback model
  would have no oracle.

## QA Checklist

Run by D5's executor on the developer's Mac, as far as an agent can. Audio for
System mode can come from another app driven from the shell (`afplay`, or Music
through AppleScript). The capture permission needs one click from the user the
first time; until it has been granted, System-mode items are recorded as
pending, not waited for.

1. First launch: the capture permission prompt appears once. After a rebuild,
   it doesn't appear again.
2. Play a steady pop or electronic track in Music. A run starts by itself
   within a few seconds of the beat. Strikes and sounds land on the beat as
   heard. Presses on the beat rate well.
3. The same track in a browser (YouTube). Same result. The game's own sounds
   never confuse the tracker: mute the music mid-run and the game must not
   keep finding a beat in its own clicks.
4. A classical track with a free prelude (the Galop in Music): unjudged and
   muted below the gate, then locks on, as in the TS game.
5. A podcast or speech: no run starts. Pressing Start gives a beatless run
   whose groove fills by itself.
6. Pause the music mid-run: the run ends on a confirmed end about 3 s later.
   Resume the music: a new run starts.
7. Pause the game mid-run: the end card shows, marked Paused, while the music
   continues. The next clear beat starts a new run.
8. A gapless playlist: the run carries across songs; End run works.
9. Switch output from speakers to wired headphones mid-run: the run ends
   cleanly, and the next run is in sync on the headphones.
10. AirPods: the best-effort notice shows. After calibration, sync is
    acceptable by ear.
11. Every session appears under `native/logs/sessions/`, and `ps replay` on it
    reproduces the run.
12. Watch my run replays the run with its music.
13. Calibrate (input and visual) changes judging and the flash as expected.

Delayed listening (E2):

14. Choose Delayed and pick Music. Music goes silent for `M` seconds, then
    plays from the app, late. Another app (a system sound, a browser) is heard
    live and unchanged.
15. A run in Delayed listening on a steady track: strikes and sounds land on
    the beat as heard. The play log's committed-beat offsets are tighter than a
    Direct run's on the same track.
16. `kill -9` the app mid-run: Music is audible again at once. Relaunch: no
    app is muted.
17. Stall the main thread (a debug-menu item): within about 2 s the watchdog
    unmutes Music, the run ends, and the log says why.
18. Pick a browser: the video notice shows. Switch back to Direct: the run
    ends, and the browser is heard live.
19. AirPods in Delayed listening: the game's sounds are in sync with the music
    by ear without calibration, and the measured delay holds at `M`.

## Workflow

This plan's beads follow AGENTS.md, plus the rules below. They are modelled on
`~/dreamtides_web`'s plan (its decisions D42 and D44, and its workflow's
Validation ladder and Introspection sections), scaled down. Phase W builds
what they need, and AGENTS.md is updated to match.

### Validation

Cheapest first:

1. **While iterating:** focused checks: `cargo test -p <crate> <filter>`,
   `npm test -- <file>`, `npm run typecheck`.
2. **Before submitting:** `scripts/check-fast`, once, on the final tree. It is
   diff-aware: lint and typecheck for changed TS files and their related tests,
   and `native/scripts/check-fast` for changed crates. Its budget is **60 s**.
   It replaces running the whole `scripts/ci` locally.
3. **Only when the change touches** CI scripts, the Tollgate policy,
   workspace-wide configuration or shared test infrastructure: the release
   stage's checks locally too (`scripts/ci-full`).
4. **Tollgate's gate stage** (`scripts/ci-gate`, budget **about 60 s**) runs on
   every candidate and blocks promotion to `staging`. The executor waits for it
   (`tg approve <id> --wait`), then moves on.
5. **Tollgate's release stage** (`scripts/ci-full`) runs the rest
   asynchronously on the newest `staging` tip. A pass moves `release` and remote
   `master` to that commit. Nobody waits for it.

### `ci-fix` beads

- **A red release run** is fixed by a follow-up commit, never a revert. The
  first session to see it (`tg release`, or a candidate's report) files a
  `ci-fix` bead at once, keyed by the failing release step (metadata
  `release_step=<step>`), after searching for an open one with the same key.
  Every later run on a newer tip fails the same way until the fix lands, so the
  key is the step, not the run.
- **A `ci-fix` bead preempts all other ready work** in this epic: an executor
  choosing its next bead picks `ci-fix` beads first, then `velocity` beads
  (the operator's feedback), then plan beads in table order.
- **Release lag:** the Tollgate policy sets `max_release_lag` (5 to start), so
  `staging` can't run far ahead of a broken `release`.
- **The end of the plan** requires `release` to equal `staging`.

### Measurements

Each bead writes its measurements, tables and raw outputs to
`native/evidence/<plan-key>/` (for example `native/evidence/a1/`), and puts a
short summary with a link in its own section of this plan (its phase, or the
Targets table). Nothing is appended to the end of this document or its
appendix: beads running in parallel collided there (the cadence retrospective
`hv-t8o8.47`). The fast check and the gate fail on a conflict marker left by a
rebase.

### Tool failures

Nobody works around a broken tool silently. When Tollgate, Hive, Beads, cargo,
uv, a flaky test or a check script fails in a way that is not the bead's own
bug, the executor:

- files a bead for it at once, with the label `tooling`, the exact command, its
  output and how often it has happened (or adds a note to the open bead for
  the same failure);
- continues its own bead if it can, or makes the tooling bead a prerequisite if
  it can't.

Defects in other repositories (Tollgate, Hive) are filed and fixed under
Hive's tooling-fix rule, at their cause, as their own beads.

### Introspection

**Paused on 2026-10-24.** Beads no longer write friction records, and
`scripts/friction`'s triggers and cadence retrospectives are not acted on; R2
to R4 are cancelled and R5 closes the epic. The machinery stays in the
repository and the records written so far stay as evidence. What follows
describes it as it ran through phase B.

**Friction ledger.** Every bead writes one compact record,
`native/evidence/friction/<bead-id>.json`, in its own commit:

```json
{"bead":"hv-t8o8.14","phase":"B","recorded":"2026-11-20T14:05:00Z","implementMin":140,"localValidationS":{"fast":41,"focused":95},"gateS":58,"friction":[{"tag":"oracle-diff-unreadable","minutes":30,"note":"first divergence printed 4 MB of state"}]}
```

Tags come from `native/evidence/friction-tags.json`, which maps aliases to
canonical tags. `scripts/friction` (phase W, written in Python so it outlives
the TS tree) validates records, reports fired triggers and due
retrospectives, and fails on a malformed record. Gate and release times are
taken from Tollgate at retrospective time (`scripts/friction stages`), not
trusted from the record.

**Budgets** live in `native/evidence/metrics.md`: the fast local check (60 s),
the gate stage (about 60 s), the release stage (30 min to start), a cold
worktree build, and the per-crate focused test times. A retrospective may
revise a budget, with its reason recorded there.

**Triggers.** An **improvement bead** (label `introspection`) is filed as soon
as either holds:

- the same friction tag appears in **3 beads** of the epic since the last
  retrospective;
- a budget is exceeded by **more than 50% on 3 consecutive beads** at
  comparable host load.

An improvement bead names its evidence (friction files, bead IDs,
measurements), targets the cause (a faster check, a fixed flaky test, a missing
tool, a simpler abstraction, a structural refactor), shows before and after
numbers for a speed change (reverted if the metric doesn't improve by at least
10%), and preserves behaviour: it never changes the oracle's expectations or
this plan's decisions. It preempts plan beads, after `ci-fix` beads. Each fired
trigger gets one line in `native/evidence/introspection.jsonl`: the bead that
fixed it, or the reason it wasn't fixed (such as a harness problem outside the
repository).

**Retrospectives.** Two kinds, both run by a read-only subagent using the
`sage` skill, scoped to this epic since the last retrospective:

- **Phase retrospectives** are explicit beads in the plan (R1 to R5 in the
  table), one at the end of each phase. Each blocks the next phase's beads, and
  the next phase starts only after every bead it files as blocking has closed.
- **Cadence retrospectives:** whenever `scripts/friction` reports 8 closed
  plan beads (any phase but R) since the last retrospective of either kind,
  the session that sees it files one, keyed
  `retrospective=<epic>/since-<last retrospective's bead>`, after searching for
  the key. It blocks nothing. Retrospectives and improvement beads (phase R)
  count for triggers but not toward the cadence, so a retrospective's own
  fixes don't bring the next one due (hv-t8o8.74).

Each retrospective reads the friction records, `metrics.md`, bead notes,
Tollgate history (gate times, release-run failures, CI repair cycles), review
dispositions, `tooling` beads and waiting time (on the gate, on prerequisites,
on usage limits). It compares the current stage times and check budgets with
their targets, files an improvement bead for every recurring cost no trigger
has covered, records a summary in the epic's notes, and appends a line to
`introspection.jsonl` listing the friction records it covered.

## Implementation Plan

Tracked by epic `hv-t8o8`. Each bead delivers through its own Tollgate
worktree, passes the gate stage and follows [Workflow](#workflow). Revised on
2026-10-24: Phase G replaces B6 to B8 and phase C; R2 to R4 are cancelled.

**Landed:** W1 (`hv-t8o8.33`), W2 (`.34`), E0 (`.35`), ES (`.36`), S0 (`.1`),
F1 to F4 (`.2` to `.5`), R1 (`.37`), V1 (`.62`), V2 (`.63`), A1 to A6 (`.6`
to `.11`), B1 to B5 (`.13` to `.17`). A7 (`.12`) is finishing with corpus
accuracy in place of bit-level parity.

**Superseded:** B7 (`.19`) and C1 (`.21`) by G3; C3 (`.23`) by G1, G5 and G6;
C4 (`.24`) by V1 and G1 (the app already runs the Rust analyzer live) and G5
(rollback profiling). R2 to R4 (`.38` to `.40`) cancelled.

| Key | Bead | Phase | Work | Needs |
|---|---|---|---|---|
| A7 | `hv-t8o8.12` | A | Bands, brightness, timbre and remaining groups; corpus accuracy report; stream causality test | A4, A5, A6 |
| G1 | `hv-t8o8.75` | G | The app runs the `sim` engine: analyzer entries into a `StreamLog`, keys into the sim, the simplest runner, sim state drawn with `render`'s sprites; the slice's own rules deleted | B5, V2 |
| G2 | `hv-t8o8.18` | G | Director, sections, zone waves, set pieces, the boss, archers, landmarks, finale and run end, live first, from B6's branch (was B6) | B5 |
| G3 | `hv-t8o8.76` | G | The look: HUD and pixel font, ground and camera, particles and effects, tier and palette look, WGSL filter and blur, overhead text | G1 |
| G4 | `hv-t8o8.22` | G | Sound: the mixer, sample-accurate scheduled SFX from sim events, the game's sound rules, causal ducking (was C2) | V2 |
| K1 | `hv-t8o8.20` | G | Checks without an oracle: causality, determinism, the virtual-player smoke test (was B8) | G2 |
| G5 | `hv-t8o8.77` | G | The rollback runner, input calibration, sounds from speculated state; rollback cost measured | G1, G4 |
| G6 | `hv-t8o8.25` | G | Runs on live audio: listening screen, start, confirmed end, rebasing, pause, end card, debug menu (was D1) | G1, G2 |
| D2 | `hv-t8o8.26` | D | Calibration, per-device settings, device changes | G6 |
| D3 | `hv-t8o8.27` | D | Session recording, `ps replay`, Watch my run | G5, G6 |
| D4 | `hv-t8o8.28` | D | Timing logs and `ps log` | D3 |
| D5 | `hv-t8o8.29` | D | Latency report against targets; the QA checklist | D2, D4 |
| D6 | `hv-t8o8.30` | D | Remove the TS game | D5 |
| E1 | `hv-t8o8.31` | E | Delayed listening in the analyzer: hindsight commits, stamps, fallback, grid choice and `M` by rule, `--delayed`, lead and causality checks | A7, E0 |
| E2 | `hv-t8o8.32` | E | Delayed listening in the app: muting tap, delay line, playback, watchdog, listening-screen choice, File-source Delayed, session meta, timing logs, QA items 14–19 | E1, ES, D2, D4 |
| R5 | `hv-t8o8.41` | R | Final retrospective | D6, E2 |

- **Now (2026-10-24, evening):** two executors. The app executor finishes A7,
  then takes G1, then G3 and G4. The sim executor takes G2 (it touches only
  the `sim` crate, so it runs alongside G1), then K1. G5 and G6 follow, then
  D2 to D6.
- **Order between ready beads:** `ci-fix`, then `velocity` (the operator's
  feedback, filed from their play), then this table's order. G1 comes before
  anything else on the app side: until it lands, the app runs the slice's own
  rules.
- **Conflicts:** G1, G3, G4, G5 and G6 all change the `app` crate; one executor
  holds the app side at a time, and the other works in `sim`, `analysis` or
  `audio`. G2 and K1 change only `sim` (and `cli` for K1).
- **The app stays playable** after every candidate. The operator plays
  whatever is on staging; `native/scripts/bundle && open
  native/target/PlaylistSurvivors.app`.
- **Review:** no cold review, except G5 (rollback correctness). The parity
  checklist in AGENTS.md no longer applies.
- **No friction records, no triggers, no cadence retrospectives** until R5
  ([Introspection](#introspection)).
- **No stops for the user.** Executors record choices and reasons in bead
  notes and continue. Feel questions go to the operator's play, not to a stop.

## Beyond This Plan

Named so this plan does not block them. Each needs its own design.

- **The endless session:** one run across a whole listening session. Songs
  become stages, the hero keeps its level, and each song gets a grade. The
  session clock and open-ended sections already allow it.
- **The feel loop on the native app:** dossiers, contact sheets and raters,
  rebuilt on `ps capture`, and the reactive-feel rounds restarted.
- **Press fusion:** the player's presses feed the beat forecast, so strikes
  follow a player who keeps time through a breakdown.
- **Track identity** from Now Playing metadata, for per-song best grades.
- **Our own beat models,** if the app is ever distributed (madmom's models are
  non-commercial).
- **Other platforms:** WASAPI loopback on Windows. Process loopback captures
  one app but cannot mute it, so a Windows Delayed listening needs its own
  design.
- **More from Delayed listening's lookahead:** accents detected rather than
  forecast, structure events and the boss entry on time, and judging that
  needs no provisional rating.

## Appendix: Measurements

Phase 0, F1, E0 and ES recorded their measurements here. From the first
cadence retrospective on, each bead writes them to `native/evidence/<plan-key>/`
and links them from its own section ([Measurements](#measurements)); these
sections are not extended.

### Measured: phase 0

Measured by `hv-t8o8.1` with `native/spike/` on 2026-10-24, on the
developer's MacBook Pro (Apple M5 Max, 18 cores, macOS 26.5.2, built-in
speakers at 48 kHz, 512-frame buffers, 120 Hz display). The machine was shared
with other agent sessions (load average 14–39), so tails are pessimistic.
Commands: `spike check`, `spike bench 1000`, `spike tap`, `spike loopback`,
`spike app` (in the signed bundle from `native/scripts/bundle`), then
`native/spike/align.py` and `native/spike/time-builds.sh`.

**Capture and permission**

- The tap (`CATapDescription` stereo global tap excluding our process, through
  a private aggregate device on the default output) hears `afplay` and does not
  hear our own AUHAL clicks: tap peak 0.000 with only our clicks playing, 0.49
  with a tap that includes us. The exclusion works only when the output unit is
  started **before** the tap is created. The other way round, our own process
  object is not yet the one that plays, and the tap hears our clicks (peak 0.58).
- Permission: the first launch prompted for System Audio Recording, and the
  user granted it. After three rebuilds (each a new binary) there was no prompt
  (`tccd` logged no `AUTHREQ_PROMPTING`) and capture still worked. The bundle is
  signed by the self-signed identity "Playlist Survivors Dev"
  (`native/scripts/signing-identity`, login keychain, imported with
  `-T /usr/bin/codesign` so signing never prompts). Designated requirement:
  `identifier "ca.thurn.playlist-survivors.spike" and certificate leaf = H"6d57…55fd"`.
  The microphone (loopback test) did not prompt.
- Capture buffers: 512 frames at 48 kHz, every 10.667 ms (p95 10.672, max 10.69).
- The tap's IOProc runs 31.30 ms (p95 31.31, max 31.33) after its buffer's
  input timestamp.

**The latency chain (ms; median / p95 / max)**

| Link | Measured |
|---|---|
| Capture → arrival, every activation frame | 26.8 / 31.7 / 78.4 (n = 4007) |
| Capture → arrival, committed beats | 27.4 / 32.0 / 78.4 (n = 88) |
| IOProc run → arrival (the app's share; a later 40 s run) | 0.58 / 0.97 / 28.2 (n = 4005), p99 1.08 |
| Committed beat's heard time − its arrival (lead) | 783 / 789 / 1117 |
| Online analyzer, per 512-sample block | 0.049 / 0.102 / 0.29; real-time factor 0.005 |
| Hindsight decode of 20 s, 1000 runs | 146 / 240 / 345; p99 294, p99.9 332 |
| — front end / nets / Viterbi | 40 / 84 / 22 (medians) |
| Hindsight decode beside the live app | 126 / 131 / 133 (60 s run, n = 40); 150 / 188 / 188 and 130 / 133 / 133 (40 s runs, n = 20 each) |
| Key event → main thread (NSEvent.timestamp) | 5.1 / 30.5 / 41.7 (n = 185, events posted from a thread) |
| Main thread → display link's target | 7.2 / 7.4 / 12.0 |
| Main thread → first vsync after present | 15.6 / 15.7 / 24.1 (p99 23.8) |
| Flash frame's vsync − beat's heard time | 4.4 / 14.4 / 26.1 (n = 86) |

- The analyzer's real-time factor live (online path only, beside the window)
  was 0.010–0.015, against 0.005 in the bench.
- Key events: winit drops `NSEvent.timestamp`, so a local event monitor reads
  it, and winit's `KeyboardInput` arrives with the same latency. Synthetic
  presses through `CGEventPostToPid` are dropped without Accessibility
  permission, so these are `NSEvent`s posted from a thread at random phases.
  They measure the main thread's pickup, not the HID and window-server path.
  Real presses: pending (record in D5).
- The clicks hit their targets: 86 played, 0 late. The flash is drawn for the
  vsync the frame actually reaches. With Fifo and frame latency 1 the present
  blocks about one period, so that is the first vsync after `now` plus the
  previous frame's blocking time. It matched the first vsync after the present
  in 95% of frames. (The warden review caught a first version that timed the
  flash one vsync too early.) "Present" here is a vsync extrapolated from the
  display link: wgpu hides the drawable, so the compositor's delay after that
  vsync was not observed.
- The app's share of capture → arrival (IOProc run → published, including a
  0.5 ms poll) is under 1 ms at p95. The rest is Core Audio's delivery: a
  constant 31.3 ms after the buffer's timestamp at 512-frame buffers. That part
  depends on the device and its buffer size, so it is report-only. The gate is
  on the app's share.

**Output latency and the loopback test**

- Reported by Core Audio on the built-in speakers: device 60 + safety 48 +
  stream 690 frames = 16.6 ms, plus the 512-frame buffer = 27.3 ms. Wired
  headphones and AirPods were not connected: **pending**, report-only.
- Loopback (built-in speakers into the built-in microphone; `afplay` plays
  1 kHz clicks, which the tap hears; we play 3 kHz clicks through AUHAL):
  mic − tap = 23.9 ms (n = 10, spread under 0.1 ms) and mic − render = 46.2 ms
  (n = 12). So `L_tap − L_out = −22.3 ms`. Independently, a tap that includes
  us hears our clicks 23.0 ms after their render time (n = 12). The tap's
  timeline runs 23 ms behind the output's render timeline.
- The microphone's reported input latency (51.5 ms, mostly a 2399-frame stream
  latency) cannot be the microphone's real offset: it would make `L_out`
  negative. So absolute values anchor on the reported output latency:
  `L_out = 16.6 ms`, `L_tap = L_out − 23.0 ms ≈ −6.4 ms`. The app carries the
  two constants. The analyzer sees each sample about 33 ms after it is heard.
- House-128 played by `afplay` into the live spike (40 s): committed beats are
  +4.7 ms late against the track's reference grid (|e| median 4.7, p95 9.7,
  max 11.0, after 5 s). The clicks at the ear land at the same offset: +4.7 ms
  using the tap's 23.0 ms, +4.0 using the microphone's 22.3 (`L_out` cancels
  in that comparison). The flash adds the frame offset above (+4.4 ms median),
  so it reaches its vsync about 9 ms after the beat. Add the compositor's
  delay, and note that its absolute time rests on the reported `L_out`, which
  the loopback could not verify. By ear: **pending** (needs the user).
- The 23 ms tap-versus-render offset was measured at 512-frame buffers on the
  built-in speakers only. It likely depends on the buffer size and the device,
  so D2's calibration should measure it per device.

**Analysis port**

- The Rust port of madmom's online front end, LSTM ensemble and forward
  algorithm, and of the offline front end, BiLSTM ensemble and Viterbi, matches
  madmom on the golden house-128 excerpts. Features agree within 1e-6 and
  activations within 3e-7. Forward states, the committed beats (29 of 29, times
  within 4e-7 s) and the offline beats (42 of 42) are identical
  (`spike check`). The commit golden runs without the hindsight phase check,
  and its evidence is not compared (the spike computes evidence per frame,
  `stream.py` per tick). The test skips when the gitignored models are
  absent, so the gate never runs it.
- Weights and tables come from `native/spike/export_models.py` (5.3 MB,
  gitignored). The online and offline DBNs both have 5617 states (55–215 BPM,
  every whole-frame interval).
- Hindsight's cost fits its planned form: one 20 s decode takes 146 ms
  median and 332 ms at p99.9, far inside its 1 s period. `D = 670 ms`. The
  spike times the live decodes but does not feed them to a phase check.

**Build times (s; fresh worktree, warm cargo registry)**

| | Cold `cargo build --release -p spike` | Native gate section (fmt, clippy, test, release build) |
|---|---|---|
| Without sccache | 33.8, 27.5 | 87.5, 79.9 |
| `RUSTC_WRAPPER=sccache` (cache warmed by another worktree) | 38.5, 28.2 | 69.7, 103.4 |

sccache hit 86% of compilations, the dependencies, under their stable registry
paths. It still won no consistent time: the cold builds were equal or slower,
and the gate ran 18 s faster in one run and 23 s slower in the other, under the
shared machine's load. **F4 builds without sccache**: it adds a daemon and
a cache for no measured gain. The gate's budget starts from about 90 s for the
spike's dependency tree (wgpu, winit, objc2), well inside Tollgate's 15 minutes.

**Crate choices**

- `objc2-core-audio` 0.3.2 covers everything the tap needs:
  `CATapDescription`, `AudioHardwareCreateProcessTap`, the aggregate device and
  sub-tap keys, `kAudioTapPropertyFormat` and
  `kAudioHardwarePropertyTranslatePIDToProcessObject`. No hand-written binding.
- Output: AUHAL (the default output unit) via `objc2-audio-toolbox` works, with
  sample-accurate clicks scheduled on the render timestamps. `cpal` is not
  needed.
- YAML: `serde-saphyr` 2. It parses all 12 files in `content/` and `levels/`
  to exactly what the TS loader's `yaml` package produces (compared as JSON).
  (`serde_yaml` is unmaintained; `serde_yaml_ng` has not been released since
  2024.)
- Ring: `rtrb` 0.4; FFT: `realfft` 3; batched input projections:
  `matrixmultiply`; window and GPU: `winit` 0.30 and `wgpu` 30 (Metal); display
  link: `objc2-core-video` (`CVDisplayLink`); key timestamps: `objc2-app-kit`
  local event monitor.
- The spike resamples 48 kHz to 44.1 kHz linearly. The analyzer runs at 44.1 kHz (A1); live capture resamples with `rubato`, as planned, where the live input is built (V1).

### Measured: E0, Delayed listening's payoff

Measured by `hv-t8o8.35` on 2026-10-24 with `npm run analysis -- delayed-payoff`
over all 62 committed `public/reactive/*/stream.json` files (the frozen
streams at `ts-freeze`), with S0's `D = 0.67 s` and `A = 0.048 s`. Each
hindsight entry's grid commits the beats in its band `[k + H, k + 1 + H)`,
stamped `k = T + D − M + A`, as [E1](#committed-beats-from-hindsight-e1)
specifies. Choices made in the replay (recorded on the bead):

- **Fallback:** within a band, Direct listening's committed beats fill every
  stretch where the grid has no beat: all of the band when the entry has fewer
  than 2 beats, and every gap of more than 1.5 periods, the band's ends
  counting as beats half a period outside it. Past the last entry, Direct's
  beats continue. Python's entries carry no evidence, so the rule's
  low-evidence trigger is not modelled.
- **Fallback stamps:** a forecast committed at analysis position `x` is stamped
  `x − (M − A)`, without `D`, since a forecast needs no decode. That keeps every
  committed beat at least `H` ahead of its stamp at every `M` (checked over all
  259,278 replayed beats). E1 should stamp forecasts the same way.
- **`H`** is 1 period plus 0.35 s at the grid's local period (its median
  spacing), or Direct's believed period where the grid has fewer than 2 beats.
- **`phased`** shifts the causal tracker's believed beats (`online`) by the
  stream's own phase check (`phase_shift`, two agreeing entries).
- **Accuracy** is the stream's own: committed beats at t ≥ 4 s within 45 ms of
  the reference, averaged per track. Recall (the share of reference beats at
  t ≥ 4 s with a committed beat within 45 ms) is reported beside it, because a
  grid at half tempo can be precise and miss half the beats.
- **Categories** are `index.json`'s groups. `causality-mix` is not listed
  there, so it is a one-track category (`unlisted`). It could veto a grid on
  its own under E1's rule; here it vetoed nothing the other categories didn't.
  Every committed stream counts, held-out tracks included: these are stream
  accuracy numbers, not feel dossiers.

**Within 45 ms, per category (%, mean of tracks)**

| Category | Tracks | Direct | online 1.5 | online 2 | online 2.5 | online 3 | online 4 | online 5 | offline 1.5 | offline 2 | offline 2.5 | offline 3 | offline 4 | offline 5 | phased 1.5 | phased 2 | phased 2.5 | phased 3 | phased 4 | phased 5 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| all | 62 | 79.0 | 78.9 | 77.0 | 74.9 | 75.1 | 75.1 | 75.2 | 79.2 | 80.4 | 84.4 | 86.1 | 86.6 | 86.9 | 78.8 | 78.3 | 78.3 | 78.6 | 78.4 | 78.4 |
| cc | 12 | 75.9 | 76.0 | 76.8 | 79.2 | 80.1 | 80.4 | 80.5 | 76.0 | 77.5 | 82.3 | 85.5 | 87.1 | 88.1 | 76.0 | 76.1 | 77.8 | 78.6 | 78.9 | 78.8 |
| classical | 5 | 63.4 | 63.8 | 69.6 | 76.0 | 77.4 | 77.3 | 77.1 | 63.9 | 68.9 | 79.6 | 84.4 | 84.8 | 85.5 | 63.8 | 68.2 | 73.0 | 73.7 | 73.8 | 73.8 |
| notjam | 39 | 81.2 | 81.0 | 77.0 | 72.0 | 72.0 | 71.9 | 72.0 | 81.3 | 82.1 | 84.1 | 84.6 | 84.9 | 84.9 | 80.9 | 79.5 | 78.6 | 78.6 | 78.3 | 78.4 |
| synthetic | 5 | 83.0 | 82.8 | 82.6 | 82.0 | 81.7 | 81.6 | 81.5 | 83.0 | 82.9 | 94.9 | 98.9 | 98.4 | 98.6 | 82.8 | 81.7 | 80.3 | 80.1 | 79.9 | 79.8 |
| unlisted | 1 | 90.1 | 90.1 | 90.2 | 91.7 | 93.9 | 93.9 | 93.9 | 90.1 | 92.4 | 94.7 | 95.5 | 97.8 | 99.2 | 90.1 | 90.2 | 90.2 | 91.6 | 92.4 | 91.6 |

**Within 45 ms by reference source (%).** The `offline` references are
madmom's whole-song offline decode, and the `fitted` ones are grids fitted
through it, so on 55 of the 62 tracks the offline grid is partly graded
against its own model. Only the `exact` (synthetic) and `songmap` (the Galop)
references are independent of it.

| Reference | Tracks | Direct | offline 1.5 | offline 2 | offline 2.5 | offline 3 | offline 4 | offline 5 |
|---|---|---|---|---|---|---|---|---|
| exact | 6 | 84.2 | 84.2 | 84.5 | 94.8 | 98.3 | 98.3 | 98.7 |
| fitted | 51 | 79.9 | 80.1 | 81.0 | 83.7 | 84.8 | 85.4 | 85.7 |
| offline | 4 | 64.8 | 65.3 | 70.9 | 81.6 | 86.2 | 86.7 | 87.5 |
| songmap | 1 | 58.2 | 58.5 | 60.9 | 71.5 | 77.1 | 77.1 | 77.3 |

**Recall at the proposal (%)**: Direct 84.6, offline at 3 s 87.0 overall;
cc 75.8 → 80.9, classical 64.4 → 79.0, notjam 87.9 → 88.7, synthetic
98.7 → 95.2, unlisted 88.8 → 94.0. On the offline grid, aggregated over all
tracks, the fallback supplies 99% of beats at 1.5 s (the bands lie past each
entry's window), 60% at 2 s, 12% at 2.5 s and 4–5% from 3 s. The other grids
are within a few points of that, and categories vary more (cc 68% at 2 s).

**Proposal, by E1's rules**

- **Grid: offline.** At its own `M` it is the best in aggregate (86.1% against
  Direct's 79.0%) and no worse than Direct on any category. `online` and
  `phased` never beat Direct in aggregate (best 78.9% and 78.8%, both at 1.5 s,
  which is almost all fallback) and are worse on notjam, synthetic and
  unlisted: the believed beats are the causal tracker's own, jitter and phase
  included, and hearing past them does not correct them.
- **`M = 3 s`:** the smallest value within 1 point of the offline grid's best
  aggregate (86.9% at 5 s; 86.1% at 3 s; 84.4% at 2.5 s). The independent
  references agree (98.3% at 3 s against 98.7% at 5 s on `exact`, 77.1%
  against 77.3% on the Galop). At 3 s the analyzer has heard 0.4–1.4 s past
  each committed beat (`M − A − D − H − 1` to `M − A − D − H` at a 0.5 s
  period).

**Verdict: Delayed listening pays.** On the independent references alone,
the offline grid at 3 s gains 14.1 points on the synthetic tracks
(84.2 → 98.3%) and 18.9 on the Galop (58.2 → 77.1%). Over every track it gains
7.1 points in aggregate, with every category better (classical +21.0,
synthetic +15.9, cc +9.6, notjam +3.4) and recall up 2.4 points. The gains on
madmom-derived references (fitted +4.9, offline +21.4) are an upper bound. E1
goes ahead from the offline grid at `M = 3 s` and confirms both with the Rust
analyzer. What it does not fix, as [Why](#why) says, is the tempo octave: the
offline decode settles on half tempo on parts of dnb-174 (recall 100 → 83%)
and cc-alex-beroza-good-day (95 → 57%), which is where recall falls, and the
Galop's recall stays at 54% while its accuracy rises from 58 to 77%.
notjam-locrian-chipdoom gets worse (47.6 → 31.7%) and notjam-baroque-chips
drops from 100 to 91.7%. E1 should watch those tracks and report recall beside
accuracy.

**Per track, Direct against offline at 3 s (%)**

| Track | Category | Reference | Direct within 45 ms | Direct recall | offline 3 within 45 ms | recall | fallback |
|---|---|---|---|---|---|---|---|
| accelerando-100-140 | synthetic | exact | 79.8 | 100.0 | 100.0 | 100.0 | 4.0 |
| beethoven-fifth | classical | offline | 47.3 | 47.1 | 85.1 | 82.4 | 2.5 |
| bumblebee | classical | offline | 75.4 | 69.6 | 97.8 | 89.7 | 2.2 |
| causality-mix | unlisted | exact | 90.1 | 88.8 | 95.5 | 94.0 | 3.9 |
| cc-alex-beroza-good-day | cc | fitted | 96.3 | 94.9 | 97.8 | 56.9 | 1.4 |
| cc-benjamin-orth-where-you-are-now | cc | fitted | 99.7 | 100.0 | 97.5 | 98.7 | 1.8 |
| cc-hairy-larry-and-the-flying-hungarians-ride-me-to-heaven | cc | fitted | 91.8 | 94.4 | 100.0 | 100.0 | 1.5 |
| cc-josh-woodward-swansong | cc | fitted | 89.8 | 73.1 | 93.0 | 88.4 | 1.3 |
| cc-kevin-macleod-black-vortex | cc | fitted | 52.8 | 64.2 | 73.9 | 72.5 | 4.1 |
| cc-kevin-macleod-go-cart | cc | fitted | 89.0 | 89.0 | 88.9 | 88.5 | 1.7 |
| cc-kevin-macleod-long-stroll | cc | fitted | 99.3 | 99.3 | 99.3 | 98.8 | 1.7 |
| cc-kevin-macleod-mirage | cc | fitted | 9.7 | 11.7 | 24.2 | 21.8 | 21.5 |
| cc-kimiko-ishizaka-aria | cc | fitted | 32.1 | 32.7 | 78.5 | 77.9 | 1.2 |
| cc-maelstrom-i-dreamt-for-a-brighter-sky | cc | fitted | 77.7 | 86.4 | 85.4 | 85.0 | 1.0 |
| cc-the-crypts-no-life-after-crypts | cc | fitted | 72.6 | 64.2 | 88.2 | 82.2 | 1.2 |
| cc-zep-hurme-hang-low | cc | fitted | 99.8 | 100.0 | 100.0 | 100.0 | 1.0 |
| dnb-174 | synthetic | exact | 100.0 | 100.0 | 100.0 | 83.4 | 5.8 |
| hiphop-92-swing | synthetic | exact | 50.6 | 100.0 | 100.0 | 100.0 | 7.1 |
| house-128 | synthetic | exact | 100.0 | 100.0 | 100.0 | 100.0 | 5.1 |
| mountain-king | classical | offline | 61.8 | 81.8 | 80.5 | 92.7 | 2.9 |
| notjam-baroque-chips | notjam | fitted | 100.0 | 100.0 | 91.7 | 91.7 | 10.3 |
| notjam-battle-ceases | notjam | fitted | 75.0 | 81.8 | 100.0 | 100.0 | 6.2 |
| notjam-battle-commences | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 2.8 |
| notjam-breakbeat-chips | notjam | fitted | 50.0 | 100.0 | 50.0 | 100.0 | 6.0 |
| notjam-chill-menu | notjam | fitted | 80.2 | 80.7 | 89.8 | 87.1 | 3.4 |
| notjam-crashing-out | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 5.8 |
| notjam-critical-theme | notjam | fitted | 99.5 | 100.0 | 100.0 | 100.0 | 1.5 |
| notjam-crystal-caverns | notjam | fitted | 3.6 | 3.6 | 12.0 | 12.0 | 6.4 |
| notjam-dark-cavern-phone-home | notjam | fitted | 85.6 | 88.9 | 91.1 | 77.8 | 2.8 |
| notjam-descend-gameplay | notjam | fitted | 96.8 | 97.2 | 96.4 | 96.8 | 2.7 |
| notjam-drag-and-dread-theme | notjam | fitted | 75.4 | 75.4 | 93.1 | 91.0 | 3.5 |
| notjam-grumpy-shopkeep | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 9.1 |
| notjam-harold-paranormal-instigator-theme | notjam | fitted | 67.8 | 98.5 | 69.4 | 86.1 | 3.4 |
| notjam-klepto-linda-caverns-a | notjam | fitted | 63.1 | 63.7 | 74.6 | 75.2 | 1.6 |
| notjam-klepto-linda-caverns-b | notjam | fitted | 38.6 | 38.9 | 44.4 | 45.3 | 2.0 |
| notjam-klepto-linda-credits | notjam | fitted | 100.0 | 100.0 | 100.0 | 99.0 | 5.4 |
| notjam-klepto-linda-mountain-a | notjam | fitted | 63.5 | 63.5 | 81.6 | 83.5 | 10.4 |
| notjam-klepto-linda-mountain-b | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 2.7 |
| notjam-klepto-linda-titles | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 4.7 |
| notjam-lazy-river-rally | notjam | fitted | 50.0 | 100.0 | 50.0 | 100.0 | 2.9 |
| notjam-leading-the-charge | notjam | fitted | 51.5 | 68.4 | 60.7 | 60.3 | 2.7 |
| notjam-lich-king-ascendant | notjam | fitted | 41.5 | 41.1 | 50.4 | 50.0 | 3.7 |
| notjam-locrian-chipdoom | notjam | fitted | 47.6 | 95.3 | 31.7 | 43.8 | 2.7 |
| notjam-meltdown-theme | notjam | fitted | 97.1 | 97.4 | 97.7 | 97.1 | 1.2 |
| notjam-peak-performance | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 2.6 |
| notjam-pitcher-perfect-theme | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 2.6 |
| notjam-pool-party | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 2.6 |
| notjam-rainy-foothills | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 5.0 |
| notjam-really-long-arms | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 9.8 |
| notjam-safe-space | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 6.6 |
| notjam-seeing-double | notjam | fitted | 95.6 | 94.8 | 96.5 | 95.7 | 3.2 |
| notjam-solemn-advance | notjam | fitted | 50.0 | 100.0 | 50.0 | 100.0 | 4.6 |
| notjam-switch-with-me-theme | notjam | fitted | 100.0 | 100.0 | 99.5 | 99.5 | 2.5 |
| notjam-the-wall | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 4.4 |
| notjam-title-theme-phone-home | notjam | fitted | 100.0 | 100.0 | 100.0 | 100.0 | 3.8 |
| notjam-type-cast-theme | notjam | fitted | 73.2 | 75.2 | 74.1 | 73.4 | 4.3 |
| notjam-untitled-track-01 | notjam | fitted | 89.6 | 85.6 | 100.0 | 100.0 | 4.9 |
| notjam-victory-lap | notjam | fitted | 100.0 | 100.0 | 100.0 | 99.2 | 3.0 |
| notjam-waltz-of-the-lich-king | notjam | fitted | 71.2 | 78.6 | 94.5 | 94.5 | 2.6 |
| orpheus-galop | classical | songmap | 58.2 | 53.7 | 77.1 | 54.5 | 2.2 |
| pop-arrangement | synthetic | exact | 84.4 | 93.6 | 94.4 | 92.3 | 2.8 |
| william-tell-finale | classical | offline | 74.5 | 69.8 | 81.3 | 75.5 | 2.7 |

### Measured: F1, the inventory at ts-freeze

`ts-freeze` (annotated tag, pushed) is `e97ef4a`. Files and lines below are at
the tag. The frozen numbers are committed beside the friction ledger:
`native/evidence/ts-freeze/check-reactive.txt` (the whole `check:reactive`
output, 203 s, every gate passing) and
`native/evidence/ts-freeze/golden-plan-hashes.json` (Watch the demo's plan
hashes for seeds 7, 1858, 1859 and 2024 and the perfect, skilled and casual
players, as in `tests/fixtures`).

**Stream groups** (schema `src/reactive/stream.ts:58-86`, writer
`tools/analysis/src/ps_analysis/stream.py`):

| Group | Carries | Stamp | Read by the game |
|---|---|---|---|
| `audio`, `params` | The file's length; `gate`, `tickS` and every analyzer knob | — | `knownLength` (`runtime/reactive.ts:81`), the song map, `gate` throughout |
| `beats` | Committed beats `{t, knownAt, period, evidence}`, phase-checked (the phase check shifts them; it has no group of its own) | `knownAt` | Everywhere: judging, sections, director, set pieces, sim, emit, evaluator |
| `ticks` | Every 0.05 s: `evidence`, `energy`, `level`, `key` (from 10 s), `bands`, `brightness` | None: found by `t` | Judging, director, set pieces, sim, effects; **`bands` and `brightness` are unread** |
| `onsets` | `{t, knownAt, strength, strong, band}` | `knownAt` | Accent onsets, set pieces, flashes; `band` is unread |
| `hindsight` | Every 1 s: the offline decode and the online beats over the last 8 s | `knownAt` | Settling (`simulate.ts:755,924,1653`) |
| `timbre` | Every 1 s: brightness, flatness, percussive share, density | `knownAt` | **Unread** (only the checks perturb it) |
| `structure.changes`, `.jumps` | Novelty peaks (known 6 s late); loudness jumps | `knownAt` | Sections; stampedes |
| `structure.ends` | Candidate endings: 20 dB below the past 30 s's loudest second, held 1.5 s | `knownAt` | Only the finale (`setpieces.ts:106`) |
| `accents.forecast` | `{t, knownAt, cycle, confidence}` | `knownAt` | Sim accents, set pieces |
| `reference` | Offline beats and sections | — | Stripped from the sim's input; virtual players and checks only |

The analyzer port keeps every group, read or not, so the stream stays
comparable with Python's. **The commit invariant is load-bearing:** when a beat
arrives, the next is already known (`stream.py:404-409`; all 14,530 shipped
beats have `knownAt <= t`). The evaluator and the live recording read the beat
after `s` without checking its `knownAt` (`evaluate.ts:732-740`,
`recording.ts:257-259`), so the native analyzer's commit rule must keep it.

**Song length and stream-end reads, with their live behaviour** (all on
reactive play's path unless noted):

- Inputs: `knownLength = stream.audio.duration` (`runtime/reactive.ts:81`); the
  song map's bar count, crop end and one section from every beat
  (`reactive/songmap.ts:17-25`); the last section's `s1` and `ctx.duration`
  (`planner/context.ts:124-139`). Live: absent, and the song map grows with an
  open-ended last section.
- Extrapolation: `beatIndexAt` and `timeOfBeatIndex` past the last beat use the
  last gap, `energyAt` clamps to the last value (`core/songmap.ts:84-163`),
  `beatDurationAt` clamps to `length - 2` (`context.ts:154`). Live: the latest
  beat's `period`.
- Schedule: rate cuts capped at `ctx.duration + 1` (inert in reactive mode);
  the beat grid over every bar (`schedule.ts:89-163`, `strides.ts:52`); the
  finale's landmark, blast, swell and `endS` (`schedule.ts:284-304`). Live: the
  grid grows, no cap, the finale added on a confirmed end.
- Dropped past the last beat: `effectiveDownbeat`, `firstBeatAtOrAfter`
  (`stream.ts:234-251`), section starts (`sections.ts:46`), set-piece
  candidates (`setpieces.ts:141-148`), palette switches and the zone-wave
  fallback (`director.ts:78-109`). Live: pending until the beat arrives.
- Clamped to the last beat: rings, formation release, kick lines, boss slams,
  volleys (`setpieces.ts:249-318`); `knownAtOfSlot` (`simulate.ts:3238`); bar
  entries and `moveFoes` to `barCount` (`:2294`, `:1880`); `downbeatAtOrAfter`
  (`:2515`); `takeOrb`'s "at the song's end" (`:2739`); headings and trails
  (`:1712`, `:2435`, `:3218`). Live: decided as beats arrive, with the latest
  known interval.
- The sim's end: `endS = min(untilS, max(ctx.duration + 1, reactive.endS))`
  (`simulate.ts:3587`), bounding decisions, keys, fires, blasts and landmarks
  (`:2264-3555`); `fireBeat` skips the final beat (`:2360`). Live: unbounded
  until the run ends; the commit invariant makes the final-beat case moot.
- `judging()` takes the last judged time from the next tick, or the last tick
  at the end (`stream.ts:171`). Live: `t + tickS`.
- Live recording: the end at `s >= ctx.duration`, the grade over
  `[playerFrom, ctx.duration)` (`recording.ts:746-752`); landmark bars, world
  beats, zone-wave end and slot settling from every beat (`:257-779`). Live:
  run end, latest known beat.
- Plan header: `duration`, sections, every beat, `progressS = knownLength ??
  duration` (`plan.ts:107-132`); the batch grade over `[0, duration)`
  (`plan.ts:220,282`, the demo and checks only); `soundFade` from `knownLength`
  (`emit.ts:91`). Live: it grows; no `progressS`; the fade from the confirmed
  end.
- HUD and renderer: the progress bar (`evaluate.ts:1517`, `hud.ts:248`) and
  section marks (`hud.ts:249`); tier times and weapons until `blastS ??
  plan.duration` (`evaluate.ts:280-295,939`); `beatIndex`, `barAt`,
  `beatLenAt`, `energyAt` clamps (`evaluate.ts:528-557,1065-1077`). Live:
  elapsed time only, no marks, until run end, latest known beat.
- Runtime: count-ins from `barBefore` (`play.ts:232`), Watch my run's
  `runToEnd` (`play.ts:95-104,532`), offsets clamped to `music.duration`
  (`audio.ts:192-295`). Live: removed with pause, session replay, and the game
  doesn't play the music. `JUDGED_FROM_S = 16` and the title card at 0.5–5 s
  assume the run starts at 0 (`reactive/grade.ts:9-13`). Live: relative to the
  run's start.
- Not live: `reactive/players.ts` (virtual players and the `?start=` bot) and
  `audio.ts:440` `renderOffline` (QA).

**Non-causal outside the sim:** only `followCurve` (`runtime/audio.ts:36-67`):
a centred 0.4 s RMS window, centred 1 s smoothing and the whole song's 90th
percentile as the reference, scheduled to the end as automation by
`Mix.follow` (`:504-511`). `reactiveFx` copies every tick and onset, but its
readers look back only (a trailing 0.5 s window, a decaying peak, the dominant
key so far). Nothing else in the renderer, HUD or audio reads ahead.

**Confirmed endings did not land.** reactive-feel's G3 (`hv-jtsz.12`: an
`ends` entry after at least 16 bars with the beat clear, its quiet lasting 3 s,
an encore) is not in the code; `levels/reactive.yaml` still has `afterS: 90`,
`zoneFallbackS: 60`, `levelUpWaitS: 20`, `gapS: 6` and `windowS: 10`. The
finale at the freeze is `reactiveFinale` above, keyed to the file's length, so
D1's confirmed-end rule is the native app's own and is compared live only on
the QA checklist.

### Measured: ES, Delayed listening's tap and delay line

Measured by `hv-t8o8.36` on 2026-10-24 with `native/spike/` (`spike procs`,
`mute`, `sigkill`, `watchdog`, `delay`, `taps`, `cleanup`; see its README), in
the signed bundle on the same MacBook Pro. Each run's output is kept in
`native/evidence/es/` (F4 deletes the spike, not the evidence). **The
operator's speakers were muted** (output volume 25, muted), and the spike does not unmute them. So
every result that needs loopback (speakers into the built-in microphone) to
tell whether an app is audible is **pending**. A global tap cannot stand in
for it: it hears a muted app at full level (1.000 of its level before the
tap, in every phase: `mute-afplay-global-tap.txt`), because taps read a
process's audio before its mute.

**The muting tap**

- A per-process tap (`initStereoMixdownOfProcesses` over the app's process
  objects, `muteBehavior = CATapMutedWhenTapped`, private) read by an IOProc
  on a private aggregate device on the default output. Over `afplay` it
  captures the app (peak 0.15 of a 0.15 tone). Creating the tap, the aggregate
  and starting the IOProc takes 12–22 ms; stopping and destroying them 7–14 ms.
- Silent while tapped, audible after destroy, and what stopping the IOProc
  alone does: **pending** loopback (`spike mute`).
- **Which process:** Core Audio's process objects are per process, not per
  app. Chrome plays from `com.google.Chrome.helper` processes; Safari from one
  of five `com.apple.WebKit.GPU` processes (shared with other WebKit apps; the
  one playing had `IsRunningOutput` set). A tap over that one process captured
  a Safari tab (peak 0.50). Music plays from its own process (`com.apple.Music`,
  pid of the app); it was not tapped, since that would play the operator's
  library.

**`SIGKILL`**

- A child process tapped `afplay` and was killed with `SIGKILL` while
  tapping. A **public** tap and aggregate (made public only for this test)
  outlive the process: both are still listed 4 s later, until `spike cleanup`
  destroys them, though no IOProc runs on the aggregate any more
  (`DeviceIsRunningSomewhere` 1 → 0).
- A **private** tap and aggregate are invisible to other processes even by
  their object IDs (`'!obj'` while the child lives and after), so their
  cleanup cannot be observed from outside.
- Whether the source is audible after the kill: **pending** loopback
  (`spike sigkill`, with `PS_CHILD_PRIVATE=1` for the private case).
  `CATapMutedWhenTapped` mutes only while the tap is read, and nothing reads it
  after the kill (Apple's documented behaviour; the spike's IOProc-stopped
  phase would test it, also pending), which is what loopback must confirm.

**The watchdog**

- The main thread stalls for 6 s while tapping. A watchdog thread polling
  every 50 ms destroys the aggregate and the tap from its own thread **2,028
  and 2,056 ms after the stall began** in two runs (2 s plus at most one
  poll). Both destroy calls return 0, and both objects are gone afterwards
  (`'!obj'`). Audible again afterwards: **pending** loopback
  (`spike watchdog`).

**The delay line**

- The tap's IOProc writes full-quality stereo into a ring of `M + 2` s; the
  AUHAL render callback plays the frame captured at its buffer's host time
  minus `M`, located from the latest tap buffer's host-time anchor. The tap's
  aggregate runs on the output device's clock, so no resampling is needed: the
  read position is corrected only if it drifts more than 2 frames from where
  the anchors put it.
- **An hour at `M = 3 s`** (`afplay` clicks every 1.3 s, tapped and muted,
  played late; the delay measured from the tap's onset of each click to a
  global tap's onset of our late copy): median 3,023.00 ms over 2,770 clicks,
  every click within 3,022.99–3,023.02 ms, per-minute medians spread 0.02 ms
  (first minute 3,023.00, last 3,023.00). Read-position error at most 0.23
  frames, no corrections, no underruns. A 2-minute rerun after a review fix
  to the onset bookkeeping gave 3,022.99 ms. Both ends are Core Audio
  host-time stamps on the same timelines the render callback places frames
  by, so this shows the delay line is **stable** (spread 0.02 ms, inside E2's
  ±2 ms) and consistent with its own bookkeeping.
- **The 23 ms is phase 0's tap-versus-render offset**
  ([Measured: phase 0](#measured-phase-0)): the tap stamps an instant 23 ms
  later than the render timeline does, so `L_tap = L_out − 23 ms`. A frame
  captured at tap time `h` would have been heard at `h + L_tap`; played at
  render time `h + M`, it is heard at `h + M + L_out`. So the music is heard
  `M + (L_out − L_tap)` = `M + 23 ms` late, which is what the global tap
  measured (it hears our output 23 ms after its render time). To be heard
  exactly `M` late, E2 plays the frame captured at `h` at render time
  `h + M − (L_out − L_tap)`, with the offset measured per device (D2's
  calibration, as phase 0 says). Confirming it acoustically by loopback is
  **pending**.
- **Two devices:** only the built-in speakers and microphone were connected,
  so the drift-correcting resampler is **pending** (E2, with AirPods or a USB
  device).

**What E2 takes from this** (also in its notes)

- Taps and aggregates are always private: a killed public one leaks until
  someone destroys it. At launch, destroy any leftover public tap or aggregate
  with the app's names (`spike cleanup` shows how).
- The picker lists apps, not processes: group process objects by their
  responsible app (the Chrome helpers with Chrome, WebKit's GPU process with
  the app it serves) and tap all of an app's playing processes.
- The watchdog can destroy the devices from its own thread while the main
  thread is stuck: within 2.06 s here, both calls succeeding.
- The delay line's design holds: host-time anchors from the tap's buffers, a
  ring of `M + 2` s, no resampler on one device clock. Played at `h + M`, the
  music is heard `M + (L_out − L_tap)` late (`M + 23 ms` here): subtract the
  device's tap-versus-render offset from the read time, and confirm by
  loopback.
- Before E2 ships the mode, loopback must confirm, with the speakers on, that a
  tapped app is silent, that it is audible after the tap is destroyed, after
  `SIGKILL` (private tap) and after the watchdog fires. These are QA items, run
  with the spike's commands until E2 replaces them.
