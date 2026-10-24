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
- It **runs reactive mode as a faithful Rust port** of the TypeScript game at a
  pinned commit: same rules, tunings, content and look.
- It **renders natively** with winit and wgpu.

The TypeScript game ends at that pinned commit (play mode included). It lives on
only as an **oracle**: its outputs, exported once and committed, are what the
Rust sim must reproduce bit for bit. The Python analysis tool stays, as the
oracle for the Rust analyzer.

Only reactive mode is in scope. Only macOS on Apple Silicon is in scope, and
the app runs only on the developer's own machine.

Terms from reactive-audio.md keep their meanings: belief stream, `knownAt`,
committed beat, evidence, the gate, hindsight grid, reading, settle delay. New
terms:

- **The freeze commit:** the last TS commit, named by the user before work
  starts and tagged `ts-freeze`. Everything the port reproduces is defined by
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
- **Live exception:** a behaviour that must differ from the TS game because the
  game no longer controls the music, such as the finale. Each is listed in
  [Live Exceptions](#live-exceptions) and excluded from the oracle comparison
  only where it applies.

### The plan in brief

| Phase | What it delivers | Proves |
|---|---|---|
| 0. Spike | Capture → online RNN → DBN → a window flashing on committed beats; latency chain measured; build times measured | The risky native parts work in Rust on this machine |
| 0. Targets | The spike's executor sets latency targets from its own numbers, by fixed rules | — |
| 1. Freeze and foundations | `ts-freeze` tag, oracle fixtures, exported model weights, Cargo workspace, gate sections, AGENTS.md | The oracles exist and CI runs Rust |
| A. Analyzer (parallel with B) | Every stream group as streaming Rust, stage-parity tests, corpus parity report | Rust streams match Python |
| B. Sim (parallel with A) | Content, schedule, sim, judging, director, set pieces, evaluator, virtual players, causality checks, all streaming | Rust sim matches TS bit for bit |
| C. Convergence | Renderer, audio output, app shell, live runner; playable in File mode, then System mode | The game plays, natively, live |
| D. Live parity | Run detection, live exceptions, calibration, session recording, latency report, QA checklist, TS tree removed | Done |
| E. Delayed listening (not needed for parity) | Committed beats from hindsight under a music delay; the tap mutes the picked app and the app plays its audio late | Lookahead buys measurably better beats, safely |

Phases A and B run in parallel after phase 1. Phase C starts when B's sim and
evaluator are green; its live steps also need A. Phase E's analyzer bead can
start once A is done. Its app bead follows D's calibration and timing logs.
Neither blocks D5 or D6.

### Decisions

These were settled with the user while designing. The executor treats them as
fixed.

- **A full Rust rewrite now,** not a webview shell around the TS game. The user
  weighed the sizing (a port of about 17k lines plus tooling, against about 3–4k
  lines changed for an incremental TS refactor) and chose the rewrite.
- **A faithful port.** Same rules, tunings, content and look as the TS reactive
  mode at the freeze commit. Rule changes come after parity, in later plans.
  The only deliberate differences are the [live exceptions](#live-exceptions).
- **TS development ends at the freeze commit,** play mode included. The user
  names the commit when the TS work in flight has wrapped up. Nothing on master
  changes the TS game afterwards.
- **Two oracles, kept separate:**
  - **Analyzer vs Python** — tiered: near-exact per stage on fixed inputs,
    statistical across the corpus.
  - **Sim vs TS** — bit-exact event logs and frame hashes, both sides fed the
    same Python `stream.json` and the same key events.

  Keeping them separate means a one-frame difference in a committed beat never
  shows up as a sim mismatch.
- **Oracle fixtures are committed,** generated once at the freeze. Rust CI needs
  no Node.
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
- **Delayed listening is an opt-in output mode, macOS only, and parity does not
  wait for it.** The user reversed reactive-audio.md's "never delay the music"
  for the native app, because the tap can mute one app safely
  (`CATapMutedWhenTapped`, released with the tap). Direct listening stays the
  default and the mode the oracle and parity are defined against. Delayed
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
- **Tooling ported:** causality checks, virtual players, headless frame capture
  and play timing logs. The parity report against play mode and the feel
  dossiers and raters are not ported in this plan.
- **Content stays where it is.** The app reads `content/`, `levels/reactive.yaml`
  and the baked `public/assets` from the repository, the single source of
  truth, and bundles them into the `.app`.
- **Layout:** a Cargo workspace in `native/`, split into crates as in
  [Workspace](#workspace).
- **The gate is path-gated:** a native section when `native/` differs from
  master, the TS section when TS paths differ. Whether sccache helps
  fresh-worktree builds is measured in phase 0, not assumed.
- **Scope ends at live parity.** Follow-ons are named, not designed.

### Goals

- On the developer's Mac, the app listens to any app's audio (Music, Spotify, a
  browser) and plays a full reactive run to it, from song start to end card.
- The Rust sim, fed the same stream and key events, produces exactly the TS
  game's event log and frame hashes on every oracle run, outside the listed
  live exceptions.
- The Rust analyzer matches the Python stream per stage within tight
  tolerances, and its end-to-end accuracy across the corpus is within a small
  margin of Python's.
- The game never reads a stream entry before its `knownAt`, enforced by the
  ported causality checks, and live by construction.
- The latency chain is measured end to end and reported every session.
- Any live session can be replayed headlessly, bit for bit.
- After parity: in Delayed listening, committed beats are measurably more
  accurate than Direct listening's across the corpus. Quitting or crashing
  always gives the player back the muted app's sound.

### Non-goals

- Windows, Linux, Intel Macs, the App Store, signing for distribution, or
  notarization.
- Changing the game's rules, tuning or look. That waits for parity.
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
  scripts/     # bundle (the .app), full-check
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
  spike's decode times. If a decode isn't done by then, the analyzer waits for
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

A useful consequence: the analyzer hears each sample `L` before the player
does. On built-in speakers that is 10–30 ms of free lookahead, and on Bluetooth
much more.

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
  or more. Phase C measures this. The Rust sim should be far faster than real
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
  device. Prove the tap hears Music and a browser, and does not hear the app's
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

1. Once the user has supplied the freeze commit's hash, tag it `ts-freeze`
   and push the tag. (F1's bead becomes selectable only then.)
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

### Workspace and gate

- Scaffold the workspace with the crates above, `rustfmt.toml`, workspace
  lints (`clippy::pedantic` where it helps), and `rust-toolchain.toml`
  pinning the toolchain.
- `scripts/ci` gains a **native section**, run when `native/` (or
  `content/`, `levels/reactive.yaml`, `public/assets`, `public/reactive`)
  differs from master:

  ```sh
  cargo fmt --check
  cargo clippy --workspace --all-targets -- -D warnings
  cargo test --workspace            # unit, stage parity, core-set oracle, causality, playability
  cargo build --release -p app
  ```

  The TS section becomes path-gated the same way, so after the freeze it
  almost never runs.
- **The gate's budget** is set in F4 from phase 0's build timings, inside
  Tollgate's 15-minute step timeout with room to spare. Every Tollgate worktree
  builds cold, and the section compiles twice (tests and the release app), so:
  `[profile.test]` and `[profile.dev.package."*"]` use `opt-level = 2` (the
  causality and oracle runs simulate whole songs); the gate runs a reduced set
  of causality cuts and oracle runs; and B4 re-times the section once a real
  sim workload exists, trimming the gate's runs into `full-check` if it is
  over budget.
- `native/scripts/full-check` runs what doesn't fit the gate: the whole
  corpus's analysis parity and the full oracle sweep. It is run by hand before
  each phase is called done, and its report is recorded in the bead.
- AGENTS.md gains a native section: `npm ci` only for TS changes; focused
  checks are `cargo test -p <crate>`; the native app needs no dev-server port;
  the capture permission needs one click from the user the first time, and agents record System-mode checks as pending until then;
  and the oracle's inputs stay frozen until D6 (see
  [Oracle fixtures](#oracle-fixtures)).

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

- librosa's chroma and MFCC are reproduced from their definitions (filterbanks
  computed once and compared against librosa's matrices in a test).
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
compares the result with the committed `stream.json`:

- committed-beat accuracy against each track's reference grid (as
  `stream.py`'s `accuracy` computes it): per track at most 1 point worse than
  Python's, and no worse in aggregate;
- evidence, energy and onset statistics, structure events matched within their
  stated tolerances, forecasts and timbre summaries;
- the real-time factor of each group and of the whole analyzer;
- the arrival delay of each group.

The gate runs it on three tracks (a synthetic track, the Galop and one corpus
track). `full-check` runs all of them.

### The stream's own causality

Python's causality test (the stream of audio cut at `X` equals the full
stream's entries known by `X − 0.05`) is ported to Rust and extended to every
group. Live, it holds by construction, but the test still catches any
whole-buffer normalisation.

## Phase B: The Sim

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
| B6 | Director, detected sections, zone waves, set pieces as a state machine, finale and run end (with their exceptions) | Full oracle event logs match on the core set |
| B7 | Recorder, emit (sounds, UI events), the evaluator (`evaluate.ts`, `reactive-look.ts`) | Frame hashes match on the core set; the corpus hash chains match |
| B8 | Virtual players; the causality checks | Players' key logs equal the fixtures' keys; the checks below pass |

B4 to B6 split `simulate.ts` by area. They touch one crate in sequence, so they
run one after another, not in parallel.

### Causality checks

Ported from `scripts/check-reactive.ts`, run on the Galop and one synthetic
track in the gate:

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
available only in Replay and File. The gate's playability check runs the
honest player and the masher on the Galop and asserts the frozen outcomes (the
honest player lives, the masher falls, grade bands), so the checks keep their
meaning once rules change after parity.

## Phase C: Convergence

### The renderer (C1)

- A wgpu renderer drawing the evaluator's frame state, as `renderer.ts` draws it
  today:
  - sprite batches from the baked atlases (`public/assets/*.png` and their JSON),
    instanced, in the same layer order;
  - particle buffers for the three particle containers;
  - the ground mesh;
  - the custom filter and the blur, ported from `shaders.ts` to WGSL;
  - shapes for `Graphics` uses;
  - the bitmap pixel font, HUD, overhead display and menus.
- **Offscreen capture:** `ps capture --track <id> --t <s>` renders a still
  headlessly from a Replay, deterministically. Contact sheets are a grid of
  stills.
- **Visual parity:** stills at the phase 1 reference moments are compared with
  the TS reference PNGs by a perceptual metric (SSIM above an agreed threshold,
  with a diff image written on failure). Pixel identity is not expected:
  different rasterisers. Differences the executor judges deliberate are listed
  in the bead.
- **Frame pacing:** present timing from the display link, at the display's
  native rate (120 Hz on ProMotion). The evaluator is called for the predicted
  heard time of the present, minus the calibrated visual offset.

### Audio output (C2)

- A Core Audio output unit renders a mixer of scheduled voices. All SFX are
  decoded at startup (`symphonia`) to the device's rate.
- Sound events carry heard times. The mixer places them at
  `heard − L` on the output timeline, sample-accurately, and accepts cancel or
  move until the voice starts.
- The TS game's sound rules port as they are, including the percussive fade
  below the gate. `followCurve`'s whole-song ducking becomes a causal envelope
  follower ([live exceptions](#live-exceptions)).
- In File mode, the mixer also plays the file, and the samples it renders feed
  the analyzer, so File mode runs the same path as System from the ring buffer
  on.

### The app shell and the live runner (C3)

- winit window with native fullscreen. Keyboard input with `NSEvent`
  timestamps; the per-key-group input calibration from `src/live/input.ts`.
- The runner ported from `src/live/runner.ts`: snapshots every 1/30 s by
  cloning the sim state; rollback on late keys **and** arrivals; the recording's
  undo journal.
- Menus and flow from `title.ts` and `play.ts`, adapted:
  - a **listening screen** in place of the title screen and track list;
  - pause (Esc or losing focus), the end card, YOU FELL;
  - **Calibrate** as today, plus the visual offset test (below);
  - a debug menu (developer builds, or a flag) with File mode and Watch the
    demo.
- **First playable: File mode on Python streams.** The app plays a corpus file
  and feeds the sim the committed `stream.json`, not the Rust analyzer. This
  proves renderer, output, input and runner against a known stream, before any
  live analysis.

### Live (C4)

- File mode with the **Rust analyzer** in place of the Python stream.
- Then **System mode:** the tap, the analyzer thread, the hindsight worker and
  arrival stamps.
- The latency chain is measured on every frame and written to the play log
  ([Timing logs](#timing-logs)).
- The rollback cost of arrivals is profiled (see
  [Arrivals roll back like late keys](#arrivals-roll-back-like-late-keys)), and
  the fallback taken only if needed.

## Phase D: Live Parity

### Runs on live audio (D1)

The app idles on the listening screen. The analyzer always runs, so the tracker
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

Every deliberate difference from the TS game, with what the oracle compares.
Phase 1's inventory completes this list from the freeze commit.

| Behaviour | TS at the freeze (as written now) | Native | Oracle compares |
|---|---|---|---|
| Finale | `reactiveFinale` picks an end within the last `windowS` of the known length | Fires on a confirmed end (D1): the horde clears and the end card follows | Everything, in Replay with `StreamEnd`; live finales are on the QA checklist |
| Run end | `reactiveRunEnd` and `ctx.duration` | A confirmed end, YOU FELL, pause, a device change, or manual End | Everything, in Replay with `StreamEnd` |
| Song length and stream-end reads | `knownLength`, `audio.duration`, `ctx.duration`, `progressS`, the last section's `s1`, clamps to the last entry | `StreamEnd` absent: progress shows elapsed time, the last section is open-ended, and each read F1 lists gets a stated live behaviour | Everything, in Replay with `StreamEnd` provided |
| Effects ducking | `followCurve`, centred smoothing and the whole song's 90th percentile | A causal envelope follower on the energy ticks | Excluded (audio only) |
| Run start | The song's start, song time 0 | Any time; rebased history (above) | Replay and File runs start at 0, as TS |
| Pause | Freezes the music and the game; resumes after a count-in | Ends the run (D1) | — |
| Output device change | — (the browser handles it) | Ends the run (D1) | — |
| Restart section, Restart song | Restart the audio | Removed: the game doesn't control the music | — |
| Best grade | Per track, in local storage | Kept for Replay and File tracks only; live runs have none | — |
| Watch my run | Re-simulates with the full stream | Replays the saved session (stream with arrival stamps, keys, audio) | — |

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
- `scripts/ci` loses its TS section. AGENTS.md drops the web workflow (dev
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
  corpus category. It records the table in the bead and the appendix.
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

### Checks

- **Lead:** every committed beat in a Delayed stream has
  `t − known_at >= H` at the period believed at commit time. This runs in the
  gate on three tracks.
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
| E1 | `Listening::Delayed` in the analyzer: hindsight commits, stamps, fallback, the grid choice and `M` by rule, `--delayed` in `ps analysis-parity`, the lead and causality checks | `M` set and recorded; Delayed accuracy beats Direct in aggregate and is no worse on any category; checks in the gate |
| E2 | The mode in the app: per-process muting tap, delay line, drift-correcting playback, watchdog, the listening-screen choice and notices, File-source Delayed, session meta, timing-log fields, QA items 14–19 | QA items pass, or are recorded as pending the capture grant; a `SIGKILL` leaves the source audible |

## Targets

Filled in by the spike's executor ([Setting the targets](#setting-the-targets)).
Until then every latency number is report-only.

| Link | Measured in phase 0 | Target | Gate or report |
|---|---|---|---|
| Capture → arrival (beats, ticks, onsets) | | | |
| Hindsight decode → arrival | | | |
| Analyzer real-time factor (all groups) | | | |
| Key event → main thread | | | |
| Main thread → present | | | |
| Sim + evaluate + encode per frame (p99, 120 Hz) | | | |
| Rollback re-simulation per second | | | |
| Committed beat → heard beat (steady tracks) | | | report (set by the commit horizon) |
| Delayed listening: committed-beat accuracy against Direct, per corpus category (E1) | | | report; E1's rule |
| Delayed listening: measured music delay against `M`, over an hour (E2) | | | gate at ±2 ms |

## Automated Checks

### In the gate

- `cargo fmt`, `clippy -D warnings`, unit tests in every crate.
- **Analyzer:** stage parity on fixed excerpts; corpus parity on three tracks;
  the stream causality test.
- **Sim:** the oracle on the core set (event logs and frame hashes); the corpus
  hash chains for a handful of tracks; `jsmath` against the V8 vectors.
- **Causality:** perturbation, truncation, negative control, streaming equals
  batch, on the Galop and one synthetic track.
- **Playability:** the honest player and the masher on the Galop.
- **Sessions:** a small committed session (recorded by the executor in File
  mode) replays bit-exactly with its recorded stream.
- **Delayed listening (from E1 and E2):** the lead check on three tracks; the
  stream causality test by audio position; sim causality on a Delayed Galop
  stream; a small Delayed File-mode session replays bit-exactly.
- **Renderer:** visual parity stills, rendered headlessly.
- A release build of the app.

### In `full-check`

- Corpus parity on every track, with the per-track table.
- The oracle on every fixture.
- `ps bench`: sim, evaluator and rollback costs on the Galop's busiest minute.
- `ps analysis-parity --delayed <M>` over every track, with the per-category
  table against Direct listening.

## Risks

| Risk | Mitigation |
|---|---|
| The tap's API isn't covered by Rust bindings | The spike proves it; a hand-written `objc2` binding is small |
| The capture permission resets every rebuild | A stable self-signed identity signs every bundle (phase 0) |
| madmom's front end has undocumented details (frame origin, filterbank normalisation, padding) | Stage parity against Python per stage, with Python's upstream fed in |
| Hindsight too slow live, or too late for the phase check | Measured in the spike; its own worker; the spike's executor picks a workable form by rule |
| A V8 Math function differs from `libm` in the last bit | V8 test vectors for every function used; shims |
| TS orderings that are hard to reproduce while streaming (queue ties, memo order, object key order) | The derived queue key; first-divergence reports; never tolerances for discrete events |
| Rollback on every arrival costs too much | Measured in C4; two fallbacks named |
| Bluetooth latency reported wrongly | Best-effort with per-device calibration, as decided |
| The TS game moves before the freeze | Nothing starts before the tag; the inventory runs at the tag |
| `simulate.ts` is large and dense | Split by area across B4–B6, each with its own oracle milestone |
| TS reads the stream's end or the song's length in ways the perturbation check never tested | `StreamEnd` in Replay; the truncation check run once on TS in F2; each read listed with a live behaviour |
| False ends from silent breakdowns | A confirmation window and a minimum judged time (D1); logged when they happen |
| The tap's latency differs from the output's | Measured separately by loopback in phase 0; two constants |
| An output device change mid-run shifts the grid | The run ends; the next run uses the new latency |
| Oracle inputs (streams, content, assets) change under the fixtures | Input hashes in fixture metadata; a freeze rule in AGENTS.md until D6 |
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
- **Running the frozen TS game live in the harness.** More flexible (fuzzed key
  sequences, any stream), but Node and the TS tree would stay forever.
- **Reimagining the rules during the port.** Faster to write, but nothing to
  diff against.
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

## Implementation Plan

Tracked by epic `hv-t8o8`. Each bead delivers through its own Tollgate
worktree and keeps `scripts/ci` green.

| Key | Bead | Phase | Work | Needs |
|---|---|---|---|---|
| S0 | `hv-t8o8.1` | 0 | The spike: tap, permission and signing identity, online RNN and DBN, beat flash and click, latency and build-time measurements, crate choices; numbers into the appendix; targets set by rule | — |
| F1 | `hv-t8o8.2` | 1 | `ts-freeze` tag, the inventory, live exceptions completed | The freeze commit's hash from the user (the bead lacks `hive_project` until then) |
| F2 | `hv-t8o8.3` | 1 | `oracle-export.ts`, fixtures, reference stills, V8 math vectors, content hash, `FORMAT.md` | F1 |
| F3 | `hv-t8o8.4` | 1 | `export-models`, golden activations | F1 |
| F4 | `hv-t8o8.5` | 1 | Workspace scaffold, gate sections, `full-check`, bundle script, AGENTS.md | S0 (build-time result), F1 |
| A1 | `hv-t8o8.6` | A | Front end and online LSTM ensemble, stage parity | F3, F4 |
| A2 | `hv-t8o8.7` | A | DBN forward, period smoothing, commit rule | A1 |
| A3 | `hv-t8o8.8` | A | Ticks (evidence uses the beliefs), energy, levels, onsets, strong flags | A2 |
| A4 | `hv-t8o8.9` | A | Hindsight (offline features, BiLSTM, Viterbi), worker, the phase check | A2 |
| A5 | `hv-t8o8.10` | A | Structure: changes, jumps, keys, ends | A3 |
| A6 | `hv-t8o8.11` | A | Accent forecasts (on the phase-checked beats) | A3, A4 |
| A7 | `hv-t8o8.12` | A | Bands, brightness, timbre and remaining groups; corpus parity report; stream causality test | A4, A5, A6 |
| B1 | `hv-t8o8.13` | B | Core, `jsmath`, content loader | F2, F4 |
| B2 | `hv-t8o8.14` | B | Stream store, Replay feeding, growable song map, context, schedule | B1 |
| B3 | `hv-t8o8.15` | B | `sim/*` modules | B1 |
| B4 | `hv-t8o8.16` | B | `simulate.ts` part 1: state, queue keys, enemies, weapons, damage | B2, B3 |
| B5 | `hv-t8o8.17` | B | Judging and settling | B4 |
| B6 | `hv-t8o8.18` | B | Director, sections, set pieces, finale and run end | B5 |
| B7 | `hv-t8o8.19` | B | Recorder, emit, evaluator; frame hashes | B6 |
| B8 | `hv-t8o8.20` | B | Virtual players, causality checks, playability check | B7 |
| C1 | `hv-t8o8.21` | C | Renderer, offscreen capture, visual parity | B7 |
| C2 | `hv-t8o8.22` | C | Audio output, mixer, SFX, causal ducking | F4 |
| C3 | `hv-t8o8.23` | C | App shell, input, runner with arrivals, menus; File mode on Python streams | C1, C2, B8 |
| C4 | `hv-t8o8.24` | C | File mode on the Rust analyzer, then System mode; latency instrumentation; rollback profiling | C3, A7 |
| D1 | `hv-t8o8.25` | D | Run detection, rebasing, pause, live exceptions | C4 |
| D2 | `hv-t8o8.26` | D | Calibration, per-device settings, device changes | D1 |
| D3 | `hv-t8o8.27` | D | Session recording, `ps replay`, Watch my run | D1 |
| D4 | `hv-t8o8.28` | D | Timing logs and `ps log` | D3 |
| D5 | `hv-t8o8.29` | D | Latency report against targets; the QA checklist | D2, D4 |
| D6 | `hv-t8o8.30` | D | Remove the TS game | D5 |
| E1 | `hv-t8o8.31` | E | Delayed listening in the analyzer: hindsight commits, stamps, fallback, grid choice and `M` by rule, `--delayed`, lead and causality checks | A7 |
| E2 | `hv-t8o8.32` | E | Delayed listening in the app: muting tap, delay line, playback, watchdog, listening-screen choice, File-source Delayed, session meta, timing logs, QA items 14–19 | E1, D2, D4 |

- **Parallelism:** after F4, the A and B tracks run in parallel; they touch
  different crates. Within each track, beads run in order. C2 can run
  alongside B. D1 comes first; D2 and D3 then run in parallel; D4 follows D3
  (the session directory and the runner).
- **Phase E is off the parity path.** E1 needs only A7 and can run alongside
  B, C and D, since it touches only the analyzer and CLI. E2 follows D2 and D4,
  which change the same app code (settings, the runner, the log). D5 and D6
  wait for neither. If E2 lands after D5, it runs its own QA items.
- **Review:** the repository's rules apply. Most beads exceed 300 lines of
  code, so each gets one cold warden review. Fixture and model files are data,
  not code, for that count.
- **No stops for the user.** Executors record choices and reasons in bead
  notes and continue. The freeze commit's hash is the only outside input.

## Beyond This Plan

Named so this plan does not block them. Each needs its own design.

- **The endless session:** one run across a whole listening session. Songs
  become stages, the hero keeps its level, and each song gets a grade. The
  session clock and open-ended sections already allow it.
- **The feel loop on the native app:** dossiers, contact sheets and raters,
  rebuilt on `ps capture`, and the reactive-feel rounds restarted.
- **Press fusion:** the player's presses feed the beat forecast, so strikes
  follow a player who keeps time through a breakdown.
- **Rule changes after parity,** including anything the live exceptions
  suggest, such as a finale that anticipates the end.
- **Track identity** from Now Playing metadata, for per-song best grades.
- **Our own beat models,** if the app is ever distributed (madmom's models are
  non-commercial).
- **Other platforms:** WASAPI loopback on Windows. Process loopback captures
  one app but cannot mute it, so a Windows Delayed listening needs its own
  design.
- **More from Delayed listening's lookahead:** accents detected rather than
  forecast, structure events and the boss entry on time, and judging that
  needs no provisional rating. Each changes the sim's rules, so it waits for
  the post-parity rule changes.

## Appendix: Measurements

Filled in by phase 0, then by each phase that measures something.

### Measured: phase 0

(Empty until the spike runs.)
