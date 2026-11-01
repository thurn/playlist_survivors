# The Unity Game: Native's Run in 3D

## Introduction

Playlist Survivors is a horde survivor rhythm game: the hero moves on the beat
of whatever music is playing, every weapon strikes on the beat, and the horde
steps to the music. Its vision is "the game that doesn't need a trailer":
every moment of play should look cinematic.

Two prototypes each hold half of that vision today:

- **The native game** (`native/`, Rust with a custom wgpu renderer) is the real
  game. It listens to any app's music, finds the beat live, and plays a
  30-minute Vampire Survivors style run in 2D pixel art.
- **The 3D tech demo** (`demo3d/`, Unity) is the look. It plays one song as a
  pre-computed choreography, with no player input, in a dark world built
  from Synty's commercial low-poly asset packs, with a crystal horde that
  shatters.

This document plans a **new Unity game** that plays the native game's run with
the 3D demo's visuals. The game lives in its own Unity project, `game/`. It is
not an extension of `demo3d/`. It ports what the native game does, but not
how it does it: every technical piece of both prototypes was reviewed from
first principles, and the
[What Carries Over, and What Changes](#what-carries-over-and-what-changes)
section near the end records the result.

### The game, as the player sees it

The player picks an app that is playing music, or a local file or playlist.
The game mutes that app and plays its audio itself, 3 s late, so it knows
each beat before the player hears it.

- A Synty hero with a white crystal blade hops one tile of a 4-direction grid
  on the frame an arrow key goes down, then per **stride step** while it is
  held. A stride step is a beat, or a half or quarter of one for slower songs
  (see the [Key Presses](#key-presses) section below).
- The blade slashes on every beat by itself, aimed at the nearest crowd, with
  heavier strikes on the music's accents.
- Crystal monsters, each type a different gem colour, step toward the hero in
  sharp moves on the **main beats** (the beats themselves, never their
  subdivisions), and burst into tumbling shards on the frame they're hit.
- The world is dark Synty ruins and forest. Coloured crystal structures catch
  and reflect the hero's light, block the way, shatter under heavy hits, and
  grow back when the music moves to a new section (verse to chorus, say).
- Levelling up adds abilities (crystal bolts, orbiting shards, lightning, a
  prism aura) with their own particle effects, all firing on the beat grid.
- A run lasts 30 minutes of run time across songs and ends in victory or
  death.

### The reference look

The look to match is the 3D demo rendered with this command:

```sh
just demo3d --monster-style crystal --palette neon \
  --structure-style crystal --structure-breaks walls
```

It shows refractive crystal monsters tinted by the colours of their original
asset packs, crystal structures that reflect the hero's light, a violet
moonlit night, a cyan hero light, and walls that shatter into physics shards.

### Decisions

These were settled with the user on 2026-10-30 in a design interview, and
decisions 1, 18 and 19 were revised the same day when the work was filed as
beads. **Executors**, the agents or people implementing this plan, treat
them as fixed. Every other choice in this document is a recommended
default: an executor may change it with a recorded gameplay or measurement
reason, without weakening a decision below.

1. **Platforms: macOS (Apple Silicon) and Windows, development builds only.**
   - **Windows comes last.** Everything else ships on the Mac first, which is
     the main play and development machine. A final milestone then brings
     the plugin, Delayed listening and the player to Windows.
   - Performance is measured on **Cygnus**, the operator's Windows desktop
     (RTX 4070 Ti), standing in for the target hardware, an RTX 4060 laptop.
     The **operator** is the developer who plays and judges the game.
   - No signing for distribution, notarization or public release. The
     analyzer's neural network weights come from madmom, a beat-tracking
     library, under CC BY-NC-SA 4.0, which stays acceptable on that basis.
2. **The game always plays the music, a fixed delay late.**
   - **Delayed listening**: the game taps one app's audio, mutes that app,
     and plays the audio itself 3 s late. This is native's opt-in mode, now
     the only live mode.
   - **File**: a local file or playlist plays through the same delay path and
     the same streaming analyzer, so a File run tests the live path
     faithfully.
   - **Direct listening** (hearing the source app live, native's default) is
     dropped. So is native's **rollback runner**, which re-simulated the game
     whenever a key or an analysis result arrived later than the sim had run,
     and existed mostly for Direct listening.
3. **Music analysis and capture run in a Rust plugin, in-process.** A native
   library built from native's Rust crates captures, decodes, delays and
   analyses the audio. Unity calls it through a small C interface.
4. **The native game is kept indefinitely** as a separate, maintained 2D
   game. The Rust crates the plugin uses are shared with it.
5. **The gameplay simulation is a new C# library**, free of Unity
   dependencies, rebuilt from native's rules rather than translated line by
   line. It diverges from native's sim over time.
6. **No rollback: a small lead plus press strides.** The sim runs a few tens
   of milliseconds ahead of what the player hears on speakers (more on
   Bluetooth headphones). A key press strides at once when the sim takes it,
   and is drawn from when it was heard, so the lead never delays the hero.
   See the [Key Presses](#key-presses) section. The free mode's sword presses
   resolve in heard time the same way, against the world's recent history,
   with nothing re-simulated (ps-w9p.10, [The sim lead](#the-sim-lead)).
7. **The run is native's 30-minute run**: run clock, schedule, **freezes**
   (the world pausing while the music plays on, for level-up cards and the
   like), chests, evolutions and victory, with at most 300 enemies alive.
   See [The Run](#the-run).
8. **The hero moves on a 4-direction grid; enemies move freely.** Structures
   occupy grid tiles. Enemies keep continuous positions and step on main
   beats. See the [Hero Movement on the Grid](#hero-movement-on-the-grid) and
   [Enemies](#enemies) sections.
9. **One large bounded arena**, generated like the 3D demo's, not an endless
   streamed world. See [The Arena](#the-arena).
10. **Crystal structures are breakable obstacles that regrow.** Heavy hits
    shatter them; they grow back on later section changes. See the
    [Crystal Structures](#crystal-structures) section.
11. **Native's arsenal, restyled.** The same weapons, passives, offers and
    evolutions, each with a 3D crystal and particle look.
12. **Native's enemy roles are cast onto the 3D demo's models**, told apart by
    crystal colour, scale and gait.
13. **The 3D demo's code is copied in and owned**, reviewed from first
    principles. `demo3d/` keeps evolving on its own, and the two projects
    diverge.
14. **Monster shards are simulated on the GPU; structure shards keep PhysX.**
    See the [Shatter](#shatter) section.
15. **The HUD keeps native's layout, restyled in crystal**, built with UI
    Toolkit.
16. **Enemy speeds scale with the hero's tempo-driven speed**, so the speed
    ratios between hero and enemies hold at every tempo. See the
    [Speeds Follow the Tempo](#speeds-follow-the-tempo) section.
17. **The project is `game/`.**
18. **Build order: foundations, a cinematic slice, the run, polish, then
    Windows.** Independent tracks run in parallel inside each milestone, and
    Mac live capture runs alongside the slice and the run. See the
    [Build Order](#build-order) section.
19. **Tracked as the Enact epic ps-msz**, under AGENTS.md's review policy:
    a Warden per substantial bead, Mason and Sage audits after M1, M2 and
    M3, and an Archivist at the end. M4 and M5 are broken into beads when
    they open, and every Windows bead is pinned to Cygnus.

Two standing project rules also apply:

- **No approval or playtest gates.** Checks only the operator can make (a
  visible window, the speakers) go on an **operator-pending list**, a running
  list of checks for the operator like native's, and never block work.
- **Deaths and breaks pay off on the impact frame.** No build-up before a
  shatter, no cracks showing before the pieces move, and no twinkling or
  blinking particles: one coherent burst.

### Goals

- A full 30-minute run, start to victory card, played live to any app's
  music in Delayed listening, on the Mac and on Windows.
- Every moving thing lands on the music: hero steps, sword impacts, enemy
  footfalls, ability shots, hits, deaths, pickups, and the level-up cards.
- Every frame looks like the reference look: dark world, rainbow crystal,
  white blade, coherent bursts.
- At least native's density: 300 enemies alive at the run's peak, inside the
  frame budget in the
  [Performance Targets](#performance-targets) section.
- The sim is deterministic: the same stream and keys always give the same
  run, on the same runtime and platform. Any session replays exactly.
- The sim never uses an analysis result before it is known. See the
  [Stream Entries and What the Sim May Know](#stream-entries-and-what-the-sim-may-know)
  section.
- A **positioning bot** (native's virtual player that kites the crowd and
  picks cards) survives the full run, and an **idle bot** (no input) dies
  early, measured headlessly.

## Related Information

- [game-modes.md](game-modes.md): `--mode`, other ways of playing this game
  for prototyping (the free mode: free movement, attacks on Z graded by the
  beat). This document describes the default, `grid`.
- [native-app.md](native-app.md): the native game's architecture. It defines
  the belief stream, entries' known-at times, Delayed listening, the clocks
  and the session format that this plan reuses or replaces.
- [survivors-run.md](survivors-run.md): the 30-minute run's rules, schedule,
  freezes, chests and evolutions. The C# sim's rules start from here.
- [feel-restoration.md](feel-restoration.md): the settled combat and enemy
  movement rules that supersede parts of survivors-run.md: the sword's
  visible swing, enemies stepping only on main beats, holding with no beat,
  and the HUD's beat track.
- [native-showcase.md](native-showcase.md): native's run compressed into one
  song, with per-section experience scaling.
- [docs/survivors/progression.md](../survivors/progression.md): Vampire
  Survivors' formulas and tables behind the run's numbers.
- [docs/tech-demo-3d/](../tech-demo-3d/README.md): the 3D demo's design.
  The most relevant parts:
  - [crowd.md](../tech-demo-3d/crowd.md): the bone-texture crowd renderer.
  - [effects.md](../tech-demo-3d/effects.md): the compute particle runtime.
  - [environment.md](../tech-demo-3d/environment.md): the arena, lighting,
    palettes and readability floor.
  - [camera-and-sound.md](../tech-demo-3d/camera-and-sound.md): the camera
    and the song clock.
  - [stylelab.md](../tech-demo-3d/stylelab.md): the crystal and other style
    shaders.
  - [performance.md](../tech-demo-3d/performance.md) and
    [benchmarks.md](../tech-demo-3d/benchmarks.md): the frame budget and how
    it was measured.
  - [alternatives.md](../tech-demo-3d/alternatives.md): engine and technique
    choices already rejected, such as VFX Graph authoring and NavMesh agents.
  - [vendor-assets.md](../tech-demo-3d/vendor-assets.md): the rule that no
    vendor asset or derivative is ever committed.
  - [mac.md](../tech-demo-3d/mac.md) and
    [windows.md](../tech-demo-3d/windows.md): running Unity on each machine.
- [native/evidence/windows-spike/README.md](../../native/evidence/windows-spike/README.md):
  the measured case that Delayed listening works on Windows 11, and what it
  needs (process loopback, a guardian process).
- [native/models/README.md](../../native/models/README.md): the exported
  madmom weights and their licence.
- [AGENTS.md](../../AGENTS.md): the worktree, Enact and vendor rules every
  change follows.

## Architecture

The game has three parts with strict boundaries. Each can be tested without
the others.

- **The plugin** (Rust): captures or decodes audio, delays it, analyses it,
  records it, and hands Unity two things: analysis results and the delayed
  music's samples. It knows nothing about gameplay.
- **The sim** (C#, no Unity dependencies): the deterministic game rules. It
  takes analysis results and keys, and produces state and events. It knows
  nothing about rendering or audio devices.
- **The client** (Unity): input, clocks, rendering, effects, sound, HUD and
  menus. It feeds the sim and presents its results.

The analysis results form the **belief stream**: append-only lists of
entries such as beats, onsets and loudness readings. Each entry has a
**known-at time** (`known_at`), the song time from which the sim may use it.
The [Stream Entries and What the Sim May Know](#stream-entries-and-what-the-sim-may-know)
section below defines both.

Threads:

- **Plugin capture thread** (real time): the macOS tap or the Windows
  loopback client copies samples into a lock-free ring.
- **Plugin analyzer thread**: as in native, it steps the analysis one 10 ms
  frame at a time.
- **Plugin hindsight worker**: every second it re-analyses the last 20 s of
  audio offline. This **hindsight** analysis hears both sides of each beat,
  so it places beats more accurately than a live forecast can.
- **Unity audio thread**: the music source's audio callback pulls delayed
  samples from the plugin; Unity mixes the sound effects on the same output.
- **Unity main thread**: polls entries, stamps keys, steps the sim, and
  presents the frame.

One frame on the main thread, in order. The **frontier** is the song time the
sim has simulated up to, slightly ahead of what the player hears (see
[The Sim Lead](#the-sim-lead)). The clock functions are defined in the
[Clocks](#clocks) section.

```text
for each new plugin entry:   sim.Add(entry)       // used from its known_at
for each new key press:      sim.Press(key, heard) // at the sim's current time
frontier = songAtDsp(dspNow) + margin
sim.StepTo(frontier)                              // fixed 1/120 s ticks
client.Present(frameSong, sim.EventsSince(cursor), sim.State)
```

**Shipped (ps-msz.8.7).** The client's presenters (the crowd, the hero,
effects, sound, structures, the HUD...) are isolated from each other's
faults. `EventDispatch` calls each presenter's `OnEvent` and `Present` in
its own try: a throw is contained, so the others still get every event
exactly once and draw every frame, and the cursor into the sim's event log
always moves on. The first throw of each presenter, call and exception type
is logged as a `PresenterFaultException` wrapping it; later ones are only
counted. The run's summary line always gives `presenter faults N` (with
each kind's count when N > 0), and the game tools' log check (spike.py's
`exceptions`, used by `game spike`, `game replay --record` and `game perf`)
fails on any. Before, ps-msz.6.14's crowd throwing on every frame from
minute 25 stopped every later presenter and the HUD for the rest of the run.

## Music: the Rust Plugin

The plugin is a native library loaded into the Unity process. It is built
from native's Rust workspace, which keeps one analyzer for both games.

### Sources

The plugin has one job for every source: produce the music as the player
will hear it, `M` seconds late, and analyse it ahead of that. **`M`**, the
music delay, is 3 s, native's measured choice (native-app.md, "Committed
beats from hindsight").

- **System (Delayed listening)**: the player picks an app from those currently
  producing audio. The plugin taps that app's audio, mutes it, and plays it
  `M` late.
  - macOS: a Core Audio process tap on the app, created muted
    (`CATapMutedWhenTapped`), as native does.
  - Windows 11: process loopback capture (`INCLUDE_TARGET_PROCESS_TREE`) with
    the app's session volume at 1e-5, multiplied back up in the capture, as
    the Windows spike measured.
- **File**: a local audio file, or an M3U playlist (native's `#PS-GAP`
  line sets the silence between tracks). With `--shuffle` the game passes the
  plugin a fresh seed for each File run (`ps_config.shuffle`, logged), and the
  plugin plays the playlist's tracks in that seed's order, the gaps staying
  in their slots. The plugin decodes it and feeds
  the same delay line and analyzer. The analyzer may read the file faster
  than real time, up to `M` ahead, so playback starts at once instead of
  after 3 s of silence. Under `--fixed-time` the frames drive it, as fast as
  they compute ([Fixed time](#fixed-time)).
- **Replay**: a recorded session's audio and entries, for watching a run
  again and for headless replays.

### Stream entries and what the sim may know

The belief stream is native's design: append-only groups of entries, each
with a content time `t` (when, in the music, the thing it describes happens)
and a known-at time. The groups are:

- **Beats**: **committed beats**, beats the analyzer publishes as final and
  never revises, each with its period and **evidence** (the tracker's
  confidence, 0 to 1). In Delayed listening they are committed from
  hindsight.
- **Ticks**, every 50 ms: evidence, energy, a quiet, medium or loud level,
  the musical key, four drum bands (kick, snare, hat, bass) and brightness.
- **Onsets**: note attacks, with a strength and a "strong" flag.
- **Hindsight grids** (each hindsight window's beat grid), **timbre**
  (spectral colour), and **song ends**.
- **Changes**: structure changes, where the music moves to a new section.
- **Jumps**: sudden loudness increases, in dB.
- **Forecasts**: **accent forecasts**, predicted accented beats, from
  2, 4 or 8 beat patterns confirmed three times.

The game changes one thing: every group, not only committed beats, is
stamped so the sim can use the delay's lookahead. Native limited that to
committed beats, to stay equivalent to Direct listening. The plugin stamps
every entry by native's committed-beat rule, and the game adds a fixed
budget on top:

```text
plugin stamp  s        = position - (M - A)
game          known_at = max(s + B, frontier at arrival)
  position: how far into the music the analyzer had read when it emitted it
  M = 3.0 s (music delay), A = 0.048 s (capture-to-arrival target),
  B = 0.5 s (more than the sim's lead, even with 300 ms Bluetooth latency)
```

- So the sim can use an entry about 2.45 s (`M - A - B`) before the music
  at its position is heard. In wall-clock terms that is a moment after the
  analyzer emits it: `B` minus the sim's lead.
- An entry that describes music before its position (a structure change is
  confirmed about 2 s after it happens) is usable correspondingly later.
- `B` is larger than the sim's lead, so in normal play the first term wins
  and stamps are a pure function of the audio. File runs therefore see
  exactly what a System run on the same audio would see.
- The second term applies only when an entry arrives late, such as after an
  analyzer stall, or when the lead exceeds `B`: the entry is known no earlier
  than the sim's frontier at the end of the frame it arrived in. The session
  records the result (see [Sessions and Replay](#sessions-and-replay)), so
  replays stay exact either way.
- The sim never uses an entry before its `known_at`. A causality test
  truncates and perturbs the stream after a cut time and checks that nothing
  before the cut changes, as native's does.

Three examples:

```text
onset at t = 61.00, emitted at position 61.05  -> known_at 58.60 (2.40 s early)
section change at t = 90.0, emitted at 92.1     -> known_at 89.65 (0.35 s early)
entry from position 120.0 arriving late, frontier 118.00
                         -> max(117.55, 118.00) = known_at 118.00
```

A song end is confirmed about 10 s after the music stops, so it becomes
known about 7.5 s after the player hears the silence.

**Shipped (ps-msz.1.1).** The mode is `Stamps::EveryGroup` on native's
`DelayedParams` (`native/crates/analysis/src/delayed.rs`); native's app keeps
`Stamps::Beats`. Every group but the committed beats is restamped
`DelayedParams::stamp(position)` as it leaves the analyzer, where the position
is the entry's native `known_at` (a tick's or a timbre entry's own time), so
the content never changes. The fixed-size records are
`native/crates/analysis/src/flat.rs`, laid out as `ps_entry`, with each
kind's `v` slots in its table. A hindsight entry's grids don't fit 12 slots,
so it becomes a header record and one record per grid beat.

`ps entries <audio | m3u> [--out FILE] [--seconds S]` runs a File source
through the analyzer in this mode, faster than real time. It writes JSON
lines: a header, then one record per line (`kind`, `t`, `known_at` and the
kind's `v` slots), in the analyzer's emission order. Dumps are cached in
`~/Library/Caches/playlist-survivors/entries` (or `$PS_ENTRIES_CACHE`), keyed
by every analyzer input and the audio's bytes, as `ps survive` caches its
streams. Two runs write the same bytes. For tests without the cache,
`native/fixtures/entries/house-128-20s.jsonl` holds the first 20 s of
house-128 (`--seconds 20`), and `accelerando-100-140-30s.jsonl` the first
30 s of accelerando-100-140, whose accent forecasts, strong onsets and
structure changes the sword's tests read. Regenerate it when the analyzer changes.

### Shared Rust code with native

Native stays a maintained game, so the plugin shares crates with it instead
of forking them.

- The analyzer, the stream types and the delay line are used as they are.
- Stamping every group by the committed-beat rule is a new mode of the
  analyzer. Native's own app keeps its current stamping.
- Native keeps its capture code (the tap, the analysis thread that runs the
  analyzer and hindsight worker, and the watchdog described in
  [Platform Capture and Crash Safety](#platform-capture-and-crash-safety))
  inside its app. That code
  moves into a shared crate that both native's app and the plugin depend
  on, gaining a Windows backend.
- The plugin itself is a thin crate built as a dynamic library, exposing the
  C interface below.
- A change to any shared crate keeps native's app building and its tests
  passing. Native's analyzer accuracy checks against Python still apply.

**Shipped (ps-msz.5.1).** The shared crate is `native/crates/capture`: the
process taps (`tap`, created muted for one app's processes), the apps
producing audio (`apps`), the watchdog (`watchdog`, which calls the tap's
release), the analysis thread over any `analysis::Source` (a tap, or a file
as the output plays it), the host clock (`clock`), and the one `afconvert`
decoder (`decode`) that native's app and the plugin's File source share.
Core Audio code is behind `cfg(target_os = "macos")`, so M5's process
loopback goes beside it. The plugin has one analysis loop of its own for
every source (ps-msz.5.2): a File source feeds it in `ps entries`' 4096-sample
blocks, paced by the render position, so its records stay byte-identical to
the dump; a System source feeds it as the tap captures. `capture` is impure:
the pure crates may not depend on it (`native/scripts/crates`).

### The C interface

The interface is small and plain C, so both IL2CPP and Mono call it through
ordinary P/Invoke.

```c
typedef struct { int32_t kind; double t, known_at; double v[12]; } ps_entry;

int32_t ps_open(const ps_config *config, ps_session **out); // source, M, rate
int32_t ps_list_apps(ps_app *out, int32_t max);             // apps playing now
int32_t ps_output_device(ps_device *out);                   // calibration's key
int32_t ps_poll(ps_session *s, ps_entry *out, int32_t max);
int32_t ps_render_music(ps_session *s, float *out, int32_t frames,
                        int32_t channels, double dsp_time);
void ps_status(ps_session *s, struct ps_status *out); // anchor, latency
void ps_close(ps_session *s);
const char *ps_last_error(void);
```

Contract details:

- **Entries are fixed-size and blittable.** `kind` names the stream group.
  Each kind documents what its `v` slots hold, such as a beat's period and
  evidence, or a tick's energy, level, key and drum bands. `known_at` here is
  the plugin's stamp; the game adds `B`. Polling never allocates on either
  side. The sim's `StreamEntry` is the one C# mirror of `ps_entry` (120
  bytes; the C `known_at` is its `Stamp`), so the client polls straight into
  what the sim takes; a sim test checks its layout and `EntryKind` against
  the header's `PS_KIND_*`.
- **`ps_render_music` runs on Unity's audio thread.** It never blocks,
  allocates or logs. It writes the delayed music for one output buffer and
  records which song sample starts that buffer. (Under `--fixed-time` the
  main thread calls it instead, one thread at a time either way: see
  [Fixed time](#fixed-time).)
- **`ps_status` carries the clock anchor**: the latest pair of (DSP time,
  song time) written by `ps_render_music`. The client derives song time from
  it. See the [Clocks](#clocks) section.
- **`ps_status` also reports** the output device's latency as the OS reports
  it, capture stalls, whether the muted app is still muted, and a
  **follower gain** that tracks the music's loudness, which the sound mix
  uses.
- **Errors never cross as panics.** Every entry point catches panics. Those
  that can fail return an error code, and `ps_last_error` describes it;
  `ps_status` and `ps_close` cannot fail.

**Shipped (ps-msz.1.2).** The plugin is `native/crates/plugin`
(`libps_plugin.dylib`); its header, `native/crates/plugin/include/ps_plugin.h`,
is the contract the C# mirrors: every struct, return code, source, state and
entry kind with its `v` slots (`analysis::flat`'s table). Decisions:

- `ps_config` holds the source, `M`, the output rate, the path (a file or an
  M3U), the models' path (null: `$PS_MODELS`, else the checkout's), the
  session folder (null: no record) and, since ps-msz.7.5, `hindsight_jobs`
  (0 or 1 live; 8 when frames drive the analysis, [Fixed time](#fixed-time)),
  which makes the struct 56 bytes.
- The status is `struct ps_status`, a tag rather than a typedef, because C
  can't give a type and the function that fills it one name.
- It reports the anchor (DSP time, song time and the frame count it came
  from), the default output device's latency (device, safety offset and
  stream latency, without Unity's buffering), how far the music is buffered
  and analysed, stalls, the state (waiting, playing, ended), the mute (0 for
  File), whether the analysis is done, a failure flag and the follower gain.
- A File source's threads: a decoder (`afconvert`, a track ahead: the
  44.1 kHz mono decode `ps entries` uses for the analyzer, and a stereo one
  at the output rate for the music), a player that fills native's delay line
  to `M` past the render position, and the analyzer (with its hindsight
  worker), fed in
  `ps entries`' 4096-sample blocks up to `M` past the render position. So a
  File run's records are byte for byte the dump's
  (`crates/plugin/tests/c_interface.rs`).
- The music is resampled once, by macOS's resampler as it decodes, so the
  delay line reads whole frames. Its linear interpolation from 44.1 kHz
  would dull the treble and alias it: a 15 kHz tone at 48 kHz loses 3 dB
  and gains a spur 11 dB down. Each track starts where the analyzer's
  timeline puts it, rounded to an output frame, so the music and the
  analysis never drift apart over a playlist.
- `ps_render_music` starts the music once 0.25 s is buffered. From then on
  song time advances by exactly the frames rendered. A buffer the line can't
  fill plays silence and counts as a stall. It takes no lock and allocates
  nothing (`tests/no_alloc.rs`, with a counting allocator); native's level
  follower now preallocates for it.
- A failure after `ps_open` (a track that won't decode, a record that can't
  be written) sets the failure flag. `ps_poll` returns `PS_ERR_FAILED` once
  the records before it are polled.
- The record, in the session folder: `audio.wav` (the analyzer's input,
  44.1 kHz mono float, exact), `entries.jsonl` (`ps entries`' line format,
  without its header) and `meta.json` (the source, `M`, `A`, the output rate,
  the duration, the records, the hindsight stalls and whether it finished).
- The Replay source (ps-msz.6.10) takes a session folder as its path: see
  [Sessions and Replay](#sessions-and-replay).

### The delay line and Unity's output

The music and the game's sounds leave through one output: Unity's. That is
what keeps them in sync without a calibration.

- A dedicated music audio source runs an audio callback that calls
  `ps_render_music` for each buffer.
- Sound effects are scheduled on Unity's DSP clock (`PlayScheduled`) at the
  DSP time of their song time.
- Because both go through the same mixer, a sound scheduled for song time `t`
  is heard exactly with the music at `t`, whatever the device latency.
- The delay line (native's `audio::delay`) absorbs the drift between the
  capture device's clock and the output's by re-anchoring each buffer, and
  resamples from the capture or file rate to Unity's output rate.
- Unity's DSP buffer uses the lowest-latency setting that plays without
  underruns on each machine (256 frames is the starting point on the Mac).

**Shipped (ps-msz.1.6, the sync spike).** The client's music is
`game/Assets/Game/Client/Music/`: the bindings (`Plugin`), the music source
(`MusicPlayer`), the clocks (`MusicClock`), a click on every committed beat
(`BeatClicks`) and the measured run (`SyncSpike`, `MixCapture`). Measured
from Unity's own final mix ([the spike's evidence](../../game/evidence/spike/README.md)):

- **A source's audio callback fills the buffer that plays one buffer later
  than the DSP time it reads.** The first run put every click exactly 256
  frames (5.333 ms) before its beat, and 512 and 1024 at those buffer
  sizes. So the music source passes `dspTime + frames / rate` as the
  buffer's anchor, and the clicks now land on the music's sample (0 to −1
  sample), with no drift over minutes.
- **The main thread's DSP clock reads the end of the buffer being mixed**,
  a buffer at a time, so a fit through its readings ran half a buffer ahead
  of the audio thread's buffer times: frameSong was 2.7, 5.5 and 10.5 ms
  ahead at 256, 512 and 1024 frames. The clock takes each reading half a
  buffer back, and frameSong now matches the audio thread's clock to within
  half a millisecond (p5 to p95). Where the light and the speakers actually
  are is the operator's loopback check.
- **Batch mode keeps Unity's mixer running.** A `-batchmode` player has no
  window but runs its audio in real time on the default device, so the
  windowless check is the real audio path. `--headless` silences the output
  after the listener's in-process capture, so checks are measured, not heard.
- Unity's DSP buffer is 256 frames x 4 (the project's setting; `--dsp-buffer
  N` overrides it), with no underruns or stalls in any measured run. The
  output latency the clock uses is the device's (16.6 ms on the Mac's
  speakers) plus Unity's buffering (21.3 ms): 38 ms.
- `--file <audio or .m3u>`, `--headless` and `--seconds N` are the player's
  flags; `--spike-out DIR` keeps the mix for `game spike`.

### Platform capture and crash safety

A muted app must always get its sound back.

- **macOS**: the OS releases the tap's mute when the tap is destroyed, so a
  crash restores the app.
- **Windows**: a session volume of 1e-5 persists across the app's restarts,
  so the plugin starts a small **guardian process** when it mutes an app.
  The guardian waits on the game's process handle and restores the app's
  volume within milliseconds of the game exiting or crashing. The saved
  volume is also written to disk and restored at the next launch, in case
  the guardian dies too or the machine loses power.
- **Watchdog**, on both platforms, as native's: if capture stops for 0.5 s
  or the main thread misses its heartbeat for 2 s, the plugin releases the
  tap or restores the volume at once. The watchdog is one of the game's
  threads, so a game frozen whole (a debugger's pause, `SIGSTOP`) keeps the
  app muted until it resumes or exits. On the Mac a suspended source app is
  no capture stall: its tap goes on capturing silence on the device's clock,
  so the app stays muted and the run hears a song gap (measured, ps-msz.5.5).
- **What M5's backend reuses** (ps-msz.7.3): a platform's capture callback
  hands each buffer to `capture::sink` (the mono mixdown, the delay line's
  first two channels, `Meta`, dropped frames, the watchdog's heartbeat), and
  its live handles go in a `capture::teardown::Teardown`, dropped once, in
  field order, under a lock the watchdog shares. `capture` and the plugin
  build for Windows in the checks (`native/scripts/check-windows`).
- **After the watchdog fires**, the world freezes as "music stopped". The
  plugin taps the app again once it produces audio, and the run resumes as
  any music-stopped freeze does (see [The Run](#the-run)). The run ends
  only if the player ends it.
- **Permissions**: on macOS the player app carries an audio-capture usage
  description and a stable self-signed identity (ad hoc only without it), so
  the OS remembers the player's grant across builds. Running System capture inside the Unity editor needs the editor's
  own grant; File works in the editor without one.
- **Device change**: an output device change ends the run, because the
  latency changes, as in native.

**Shipped (ps-msz.5.2).** The System source is `native/crates/plugin/src/system.rs`
beside `file.rs`; `session.rs` is what every source shares (the render path and
anchor, the records' ring, the record and the one analysis loop). Decisions:

- `ps_config.pid` (in the padding after `source`, so the struct stays 48
  bytes) names the app: a `ps_app`'s pid, the process responsible for its
  audio, or any one process with audio (a tool launched from a terminal is
  grouped under the terminal). `ps_list_apps` lists the apps producing audio
  now (`capture::apps`, playing ones only). No icons: they aren't cheap.
- `ps_open` taps the app at once, muted, so a missing app or a failed tap is
  its error (`PS_ERR_SOURCE`). The source's timeline is the tap's frames at
  its rate from the first frame: song time 0. The line plays from frame 0 once
  it holds `M`, read by frame (interpolated if Unity's rate differs), so the
  analyzer reads `M` ahead of what is heard. The tap and Unity both run on the
  default output device's clock, so they never drift; a device change ends
  the run anyway.
- One thread, `ps-system`, drains the tap's mono mixdown, lines it up with its
  buffers (silence for frames the tap's ring dropped), resamples it to
  44.1 kHz and feeds the analysis loop in blocks of two analyzer hops
  (20 ms, inside `A`), so a recorded session's audio analysed in whole hops
  gives the same entries.
- **The heartbeat** is every `ps_poll` and `ps_status` call. The thread
  relays it to the tap's watchdog (`capture::watchdog`), which releases the
  tap on a 0.5 s capture stall or a 2 s heartbeat gap (2.03 s measured).
- **After a release** the state is `PS_STOPPED`: silence plays at once (the
  app is heard directly; the delayed copy would double it), and the analyzer
  hears silence up to `M` past the render position, so its timeline keeps up
  with song time. From 1 s after the release, every 0.25 s while the
  heartbeat is alive (so a hung game never flickers the mute), the plugin
  looks for the app playing and taps it again into the same line, numbering
  the new tap's frames from where the analyzer's silence reached. The music
  resumes there, `M` later, and the state returns to `PS_PLAYING` when the
  render position reaches it. `ps_status.releases` counts releases.
- `ps_status.device_changed` is set once the default output device differs
  from the one at `ps_open` (looked at once a second, from `ps_status`), and
  the reported latency follows the new device. `ps_status` is now 80 bytes.
- The record covers System sources: `meta.json`'s `source` is
  `system: <app> (pid <pid>)`, `complete` is false (a live source has no
  end), and `releases` lists each release's song time and reason.
- Tested without the grant through a hand-driven tap (`tests/system.rs`,
  and `tests/no_alloc.rs` for the render path). `tests/system_tap.rs`
  (ignored) taps a real `afplay` child; without the grant it captures
  silence but mutes, releases and taps again
  ([evidence](../../game/evidence/system-source/README.md)). Hearing the
  delayed music needs the grant: an operator row.

**Shipped (ps-msz.5.3).** The client's side of live runs: the title screen,
`--app` and the player's permission. Decisions:

- **The title screen** (`game/Assets/Game/Client/Title/`, UI Toolkit, its
  UXML and USS in `Resources/Title/`) lists the apps producing audio now
  (`ps_list_apps`, refreshed each second), then the recently played files and
  those in `~/Music/Playlist Survivors` (`--music-dir` overrides). A player
  has no file dialog, so Cmd-V adds a copied path. Up and Down or pointing
  choose, Enter or Space starts, and a click on the chosen row starts (so
  pointing and clicking starts at once). It opens when a windowed launch
  names no source (`--title` forces it, for stills).
- **A run started there returns to it** when it ends (the file's end, a
  failure, an output device change, Backspace): the boot scene reloads and the
  title screen says why. Runs started by flags quit as before. The run's own
  ends (ps-msz.6.1) end it the same way, and the title screen then offers
  Watch my run again under LAST RUN, or W, to watch the run (ps-msz.6.10).
- **`--app <name>`** picks as native's `capture::apps::pick` does by name
  (exact, ignoring case, else a part); a number is a pid, listed or not, since
  the plugin taps any process with audio. It waits up to 3 s for the app to
  produce audio, then fails listing what is playing.
- **An output device change ends the run** cleanly (`ps_status.device_changed`).
- **The source tag** is `MusicSource.Tag` (the app's name, or the file's), on
  `GameLoop.Source` and `MusicPlayer.Source`, for the HUD.
- **Permissions**: every Mac build gets `NSAudioCaptureUsageDescription` in
  its Info.plist and is signed again (`Editor/MacPlayerSigning.cs`), with the
  bundle ID `ca.thurn.playlist-survivors.game`. It signs with native's
  stable self-signed identity when the keychain has it, since TCC keys an ad
  hoc signature's grant on its code hash and every build would ask again;
  without the identity it signs ad hoc. A player started from a terminal
  (`--app` in a check) gets the terminal's grant, since TCC asks of the
  responsible app; `just game` opens it on its own.
- **The editor** needs its own grant for System capture: allow Unity in
  System Settings, Privacy & Security, Screen & System Audio Recording
  (System Audio Recording Only). File runs need none.

### Building and loading the plugin

The plugin builds per platform from native's workspace, outside Unity, and
the game's tools copy the result into the Unity project as a plugin.

- macOS: an arm64 bundle. Windows (in M5): an x86-64 DLL built on a Windows
  machine, either Cygnus or the Windows runner of the repository's
  asynchronous full check suite (**slow CI**). Native's workspace builds
  only on macOS today (objc2), so M5 first makes the plugin's crates build
  on Windows.
- Unity locks a native plugin once loaded, so rebuilding normally needs an
  editor restart. In the editor, the C# side instead loads a copy named
  after its build hash through `dlopen` or `LoadLibrary`, and resolves the
  functions itself. Rebuilding and reloading the scripts picks up the new
  copy. Players use ordinary `DllImport`.
- A headless plugin command writes a File run's entries, so the sim's tests
  and sweeps use real analyzer output without Unity.

The plugin builds with `cargo build --release -p plugin` in `native/`
(`native/target/release/libps_plugin.dylib`), and native's fast and slow
checks run its tests with the workspace's.

**Shipped (ps-msz.1.6).** `game plugin` (which `game setup` and `game build`
run first) builds it with cargo and copies it to the gitignored
`game/Assets/Plugins/macOS/`, and the analyzer's models to
`game/Assets/StreamingAssets/ps/`, where a player passes them to `ps_open`.
The editor's `PluginLoader` dlopens `Library/PsPlugin/libps_plugin-<hash>.dylib`
(the first 16 hex digits of its SHA-256), copied on first use; a rebuilt plugin
is a new hash, so the next domain reload loads it beside the old copy, which
stays mapped until the editor quits. An EditMode test re-signs a copy and
sees the loader pick up the new one and call it.

## Time and Sync

Sync is the game's core promise, so every clock is defined once, here. The
plugin's clock anchor makes song time exact on the audio side; the visual and
input sides are mapped to it through the output latency.

### Clocks

**Song time** is the position in the music, in seconds: the time base of the
stream, the sim and the session. The plugin's anchor pins a song time to a
DSP time, and the rest follows:

```text
songAtDsp(d)  = anchor.song + (d - anchor.dsp)
heardSong(r)  = songAtDsp(dspAt(r)) - outputLatency
frameSong     = heardSong(now + displayLatency) + videoCalibration
keySong(r)    = heardSong(r) - inputCalibration
```

- `dspAt(r)` maps real time to DSP time with a least-squares fit over the
  last 2 s, as the 3D demo's song clock does.
- **Output latency** is the device latency the plugin reports plus Unity's
  DSP buffering.
- **Display latency** is one frame plus the compositor's delay, as in the 3D
  demo.
- The two calibrations are described in the [Calibration](#calibration)
  section.
- `frameSong` never runs backwards.
- During a freeze, song time keeps running; only the sim's world and run
  clocks stop.

### The sim lead

The sim must have computed an event before Unity's mixer reaches that event's
song time, so its sound can be scheduled on the exact sample. It therefore
runs ahead of the DSP write position by a margin, and so ahead of what the
player hears by the output latency plus that margin.

```text
frontier = songAtDsp(dspNow) + margin    // margin: 2 frames + 1 tick + 2 DSP buffers
lead     = frontier - heardSong(now)     // = outputLatency + margin
```

- The margin keeps every event a frame settles schedulable. A frame settles
  the events after the previous frame's frontier, which is a frame older
  and up to a tick (1/120 s) short of its frontier, since the sim steps
  whole ticks. The DSP clock's reading runs up to a buffer ahead of the
  mixer's position, and PlayScheduled needs one buffer past the reading
  (the sync spike's scheduling headroom: the buffers Unity keeps queued
  are behind the mixer, in the output latency). The second frame is slack
  for frame jitter. That is about 52 ms at 60 fps and 256-frame buffers.
- On built-in speakers the lead is about 90 ms (38 ms of output latency,
  game/evidence/loop). On Bluetooth headphones
  (150–300 ms of output latency) it grows to about 0.2–0.35 s. A press is
  drawn from when it was heard either way. `B` (0.5 s) stays larger than the
  lead in both cases.
- Nothing the sim has settled is ever revised. Events at or before the
  frontier are final: none is changed or withdrawn.
- **Heard-time strikes** (ps-w9p.10) are the one kind of event a frame
  settles before the previous frontier. A free-mode press heard at h lands
  its blow at `h + 1/30 s` (game-modes.md, Attacking on the beat), which a
  live key reaches the sim after: about the lead later, at F. So when the
  sim takes the key, it resolves the press's moments it has already passed
  (the wind-up, the blows, a heavy cleave's first bands), each at its own
  time against the world as it was then: the free hero's kept path
  (`IHeroControl.PositionWas`, 64 ticks) and the horde's history of each
  enemy's latest two moves and its life's start (`Horde.Rewound`). Their
  damage applies at F, and their events (attack, hits, knocks, deaths,
  drops, glints, breaks) are stamped at their own times, at most 0.5 s back
  (`Sword.PressBackS`) and never before a freeze or fall.
  - **The rule kept: no rollback.** Nothing simulated is rewound or run
    again; what the sim's systems did in (h, F] stands. An enemy the blow
    killed at h + 1/30 may still have bitten the hero, been hit by another
    weapon or stepped in between; its knock, as a press stride's re-walk
    does for the hero, replaces the moves it made after its time, in the sim
    and in the crowd. A bounded re-simulation of the lead was rejected: it
    would revise settled events, sounds and the run's random draws, for a
    difference no one sees in a tenth of a second.
  - **Why it shows on time**: frameSong trails the frontier by the lead, so
    when the frame after the key settles the press, the screen is about a
    display latency past h: the blow at h + 1/30 is on that frame or the
    next. A death that settles after the frames passed its time breaks on the
    frame it arrives (CrowdView).
  - **Sound**: the mixer is past h + 1/30 by then (a DSP buffer past the
    heard song plus the output latency), so the press's sounds start as
    soon as the mixer can rather than drop ([Sound timing](#sound-timing)).
  - Measured by `LoopTests.AFreePressIsSeenAndHeardAtOnceWhateverTheLead`
    ([game/evidence/press-latency](../../game/evidence/press-latency/README.md)):
    key to the blow seen in 35-51 ms on speakers and on a 250 ms Bluetooth
    output alike, and to its swing heard in the output latency plus at most a
    frame and two DSP buffers; before, both took the lead (about 90 ms, and
    0.3 s on Bluetooth).
- The presentation shows the sim's recorded state at `frameSong`, which is
  normally behind the frontier, so it interpolates recorded history. If
  display latency ever exceeds the lead, the frame shows the frontier's
  state instead, and the debug overlay flags it (see
  [Debug Surfaces](#debug-surfaces)).

### Fixed time

Checks, stills and filmstrips run on frame time instead (`--fixed-time`): frame
`n` shows song time `n / 60` however long it takes, so a rerun shows the same
frames. No music plays in time with those frames, so the output is silent. The
sounds still keep their books on the frames' mixer position (a DSP buffer past
`song` below, the live rule), playing nothing, so a fixed-time run counts the
sounds a live one would drop or cut off (ps-msz.8.6).

```text
song     = frame + 0.038                 // FrameClock: the built-in speakers' output latency
frontier = song + margin                 // the margin at 60 fps and 256-frame buffers, 52 ms
needed   = frontier - B                  // the latest stamp the frame may take
```

- **The frames drive the music** (ps-msz.7.5): a File or Replay source's
  render position is not Unity's audio thread's. Each frame, the main thread
  renders the music to the frame's `song` and drops it
  (`MusicPlayer.RenderTo`; `ps_render_music` from the main thread, the audio
  source never playing). The source then decodes and analyses up to `M` past
  that, so a run takes as long as its frames and its analysis compute, not
  its song time.
- **Each frame waits for its entries**: it polls until the analysis covers
  its frontier's stamps (`needed + M - A`, plus a block) and takes every
  entry at exactly stamp + B. Rendering to the frame's `song` lets the
  analysis read about 0.3 s past that, so the wait is for the analysis's
  work, never for the render position. Nothing then depends on the plugin's
  timing: a rerun's hash is the same.
- **The analysis decodes hindsight in parallel**: the frames wait on it, and
  each 20 s window's decode (its front end, eight networks and DBN, once a
  second of music) is on that path. A driven session asks the plugin for 8
  `hindsight_jobs` (`ps_config`): the window's front end and networks run on
  8 threads, with the same entries as one (model order and each frame's
  arithmetic are kept). Live runs keep one: the decode has 0.67 s and its
  own thread. Measured on jarvis: with one job the analysis read about 4
  times real time (as fast as `ps entries`); with eight the full run's 35
  minutes of song took 4 minutes, its 30-minute session's replay 1 minute
  and a still at 25:00 of run clock 4 minutes
  ([game/evidence/full-run](../../game/evidence/full-run/README.md#retaking-it-quickly-ps-msz75)).
- **A frame shows its song time and a microsecond** (`FrameClock.Reached`,
  ps-msz.8.5): Unity's frame time, a sum of 1/60 steps, falls a rounding
  short of the sim's tick at the same time for most frames, which showed the
  moments timed on that tick a frame late. The frontier is the frame's own.
- **An app's music can't be driven**: it arrives in real time, so `--app`
  under `--fixed-time` keeps the audio thread's render position.
- **`--seconds N` counts song seconds** under `--fixed-time` (Unity's frame
  time), so a still at 25:00 is reached in the time 25 minutes of frames and
  analysis take.
- Real-time runs (no `--fixed-time`: windowed, `--headless`, the sync spike,
  `--watch` and live capture) keep Unity's audio thread as the render
  position.

### Beats, bars and reliability

Several rules depend on whether the beat can be trusted, and on bars. Both
keep native's definitions.

- A **reliable beat** is a committed beat whose evidence passes the level's
  gate (native's, 0.2: the analyzer's `gate` in native/models/manifest.json;
  a clear track's evidence is about 0.5), while the music is clear: the latest tick is at most 0.25 s old and
  confident (native's `enemy_reliable`). **The beat locks** when beats
  become reliable again after a gap; a **clear beat** is a reliable one.
- **Bars** are two beats long, counted from the first committed beat after
  a lock, and a **downbeat** is a bar's first beat. The analyzer detects no
  metre, as in native.
- **The latest beat continues.** Committed beats come from hindsight in
  batches, often less than a beat ahead (house-128's dump has beats known
  0.1 s after the beat before them). While the next beat is not yet known,
  the latest one counts as still going for 1.5 of its periods, and the
  stride grid keeps its step, as native's last grid time keeps the step
  before it. Only after that is no beat known ahead.
- The game is in one of three states, and each rule names the state it
  needs:

```text
state                  hero strides on     weapons fire on   enemies
reliable beats         the stride grid     the beat grid     step on main beats
beats below the gate   the stride grid     the beat grid     hold
no beat known ahead    a free 0.35 s pulse raw intervals     hold
```

### Key presses

The hero moves on stride steps while a key is held. A stride step is a time
on native's **stride grid**: the committed beats, each beat halved until the
period is at most 0.4 s (see the
[Speeds Follow the Tempo](#speeds-follow-the-tempo) section for how the
halving is chosen). When no beat is known, the grid is a free 0.35 s pulse.

A press strides at once, on the frame the key goes down. Native's zen mode,
its default, makes a press wait for the next stride step (0 to one period,
about 117 ms on average at 128 BPM). The operator found that wait laggy, and
the game feel comes first (ps-msz.4.3), so a press here strides at once, as
in native's classic mode.

The sim runs ahead, so a key reaches it the lead after the player heard it.
The sim can't revise what it has simulated, so every move is judged in heard
time and taken late. The HeroStep event is emitted when the sim takes the
move, and carries its start in heard time, so the client draws it where it
happened and the lead never delays the hero.

```text
press (direction d, heard time h), processed at the sim's current time F:
  s = the latest stride step at or before h + 0.11   // one the stride would still be on is its
  if a press already strode for s, and d repeats it or that stride is still drawing:
                                    queue d for the first step after F no press took
  else:                             stride now: the tile changes at F, drawn from h
                                    (the held stride on s, if it hadn't set off by h, gives way)
held keys, on a stride step t that no press took:
  judged once every key heard before t is in: at the next key change heard after t,
  or the longest lead (F - h) of the last 16 keys after t; stride with the keys held then,
  drawn from t
the session records (h, F); a replay applies the press at F
F is the previous frame's frontier: steps before F are already simulated
```

A worked example at 120 BPM, with stride steps at 10.00 and 10.25:

```text
press heard at h = 10.02; it reaches the sim when it stands at F = 10.08
s = 10.00 (10.25 is after 10.13): the tile changes at 10.08, drawn 10.02 to 10.13
a press heard at 10.15 takes s = 10.25: drawn 10.15 to 10.26, no held stride on 10.25
Up held, Right pressed (Up released) heard at 10.22, taken at 10.31: the sim passed 10.25
  with Up down, but judges it at 10.31 with Right's press: no Up stride on 10.25
```

- A press stride sets off from where the hero is drawn at h, cutting short a
  move in progress, and takes native's 110 ms. It is compressed to end before
  the next step that may stride.
- A press takes the step whose stride it would overlap, so a hold's first
  repeat is on the step after.
- One press stride per step and direction: a second press for the same step
  waits for the next step no press took, unless it turns (goes another way)
  once the first press's stride has drawn (110 ms), when it strides at once:
  a quick L moves twice, as in native's classic mode. Mashing one key, or
  faster than a stride, stays at the grid's pace, and the last press wins.
- A tap released before the next step is heard never strides on it, however
  long the lead, and a turn heard before a step turns on it.
- The hero's tile changes when the sim takes a move, up to the lead after it
  is drawn. Pickups, contact and the enemies read the tile from then on, and
  the position along the drawn move. The sword strike on an earlier step
  already fired from the old tile.
- Held keys stride on every step a press hasn't taken. The last-pressed held
  direction wins.
- The lead a held stride waits is the longest of the last 16 keys'. A key's
  lag runs up to a frame short of the lead with where in its frame it fell,
  and waiting only the latest key's let a key change heard just before a
  step, arriving a little later than the key before it, find the step judged
  without it. The longest of recent keys is the lead, give or take a
  sub-frame. A press heard before the held stride on its step set off takes
  that stride's place, the hero striding from where it stood at h, so a turn
  never hops diagonally over both. A key heard at a step's own time counts
  for it.
- So a held stride reaches the client about a display latency (a refresh)
  after its step and draws from there, as an on-time step does whose start
  fell just after the frame before: its takeoff is at most a frame behind
  the beat. No frame can show a step on time and also know every key heard
  in the refresh before it.
- A press made while the world is frozen never strides after it, as in
  native.
- Space stomps (stands still) at once, as a press.
- Keys come from Unity's Input System with their event timestamps, mapped by
  `keySong`.

**Shipped (ps-msz.8.5).** Measured against native's classic mode (a tap moves
one tile on its key-down frame, a turn turns at once) by Game.Sim's
LateStepTests: taps, turns, rollovers and double taps at 48 phases of the
step, on a 90 ms and a 250 ms lead, at 100, 128 and 170 BPM, the keys reaching
the sim at frame starts. Taps were already right. A turn hopped diagonally in
1 to 3 phases of 96 (the latest key's lag), and a quick second tap the other
way waited up to a step (468 ms on Bluetooth) in half the phases (one press
stride per step); both now answer at once everywhere, by the longest-lag wait,
the press taking a held stride's place, and the turning second press above.
Numbers: game/evidence/beat.

### Attacks and steps land on the beat

An animation's key moment (a sword impact, an enemy's footplant) must land on
the beat, so the sim schedules each one ahead of its beat, and the client
retimes the clip to fit.

- **Sword**: at `impact - windup` the sim chooses the swing's direction from
  the enemies present then and emits an attack event carrying the impact
  time. The wind-up starts at about 0.32 s (the Synty clip's), retimed
  between 0.75x and 1.33x speed by the 3D demo's clip scheduling.
- **Strikes closer together than a wind-up** (subdivision strikes at fast
  tempos) use a short follow-up clip that continues from the previous
  swing's follow-through, retimed into the gap.
- **Enemy steps**: the sim decides each step's destination at the start of
  its move window and emits the step with its footplant time. See the
  [Enemies](#enemies) section.
- **Contact lunges**: notice, peak on the beat, return, as in native.
- The client never guesses future sim state. Every anticipation it shows
  comes from an event the sim has already emitted, or from the stream.

### Sound timing

Every sound is scheduled on Unity's DSP clock at the DSP time of its song
time. It is never started late; a sound that can't make its time is dropped,
but for **heard-time sounds** (ps-w9p.10): a free press's swing, hits,
deaths and breaks, whose events the sim stamps before the frontier it
settles them at ([The sim lead](#the-sim-lead)). The mixer is already past
most of them, and dropping them would leave a press silent, so each starts
as soon as the mixer can (a DSP buffer past its clock's reading), at its
attack, its lead-in skipped (`SoundCue.Asap`; the run's summary counts them
as late). The presenter knows them by their stamp: before the previous
frame's frontier.
When the mixer can make a sound's attack but not all of the clip's lead-in
before it, the sound starts into its clip so the attack still lands on time
(ps-msz.8.6). Unity's mixer honours that start (`AudioSource.time` before
`PlayScheduled`): the sound probe has a 100 ms lead-in before its click, so
`game spike --sounds` and `game beat --sounds` find on the music both the
probes started early by it and those started into it (ps-msz.8.13).

- **Impact sounds** (hits, shatters, swings) play at the impact's song time.
  The lead guarantees they are known in time.
- **Off-grid sounds** keep native's musical snapping: a hurt sound on the
  next sixteenth, gem chimes on the grid, climbing a pentatonic scale.
- **Stream-driven sounds and presentation**, such as drop build-ups, read the
  stream directly and may anticipate by up to the lookahead.
- Every sound is asked for from its event as it settles, never from a
  presenter's frame, which is already past its time (ps-msz.8.5: a regrowth's
  eruption sound was, and dropped).

### Calibration

Audio-to-audio sync needs no calibration, because the music and the sound
effects share one output. Two offsets remain, each with a screen like
native's:

- **Video calibration**: the player aligns a flash with a click. It corrects
  the display and reported device latencies in `frameSong`.
- **Input calibration**: the player taps along to clicks. It corrects
  `keySong`.
- Both are stored per output device.

**Shipped (ps-msz.6.11).** game/Assets/Game/Client/Calibration/, with the
screens' UXML and USS in Resources/Calibration/.

- **The device**: `ps_output_device` reports the default output device's
  Core Audio UID, name, latency and whether it is Bluetooth, without a
  session, so the title screen can calibrate before any music plays.
- **Storage**: `calibration.json` in the player's data folder
  (`--calibration FILE` in its place), whole milliseconds per device UID, as
  native's settings keep them. A device not calibrated yet plays with no
  offsets. A run reads its device's offsets when its music opens; a device
  change ends the run, so they hold for it.
- **The screens' clock** is the game's own `MusicClock`, with song time set
  to DSP time and the device's latency plus Unity's buffering as the output
  latency, and the clicks scheduled on their DSP samples as the game's
  sounds are. Each offset is therefore exactly what the game's clocks lack.
- **Video**: a click on every beat at 120 BPM and a crystal flash drawn at
  frameSong. Left and Right (or the EARLIER and LATER buttons) move the flash
  5 ms earlier or later (up to 200 ms), Enter (SAVE) saves, Esc (BACK) leaves.
- **Input**: native's tap-along, eight clicks at 120 BPM tapped with an
  arrow key or Space, read through the sim's own key path with each key's
  timestamp. The median error after the first two taps, counting only taps
  within a quarter beat of a click, is saved. Native also keeps the stomp
  key's offset apart; the game keeps one input offset. The taps are keys,
  the ones the game is played with, never the mouse; AGAIN (Enter) and BACK
  (Esc) are buttons too.
- **Access**: two rows under CALIBRATE on the title screen, and the pause
  menu (Esc or the pause button in a run: resume, calibrate video, calibrate
  input, end run). In
  a run the music is ducked under the clicks while a screen is open.
- Flags: `--calibrate video|input` opens a screen at launch; under
  `--fixed-time` its song time is the frames' and no click plays (stills);
  `--tap-along MS,...` taps each click that late, for windowless checks.
  Evidence: game/evidence/calibration.

## The Simulation

The sim is a plain C# library with no Unity references. It builds in Unity
and in .NET, as the 3D demo's shared choreography library does, so its
tests, bots and sweeps run with `dotnet test` and a .NET command line,
without Unity.

### Shape and determinism

The sim is data-oriented: dense arrays of plain structs, systems as ordinary
methods called in a fixed order, no ECS framework and no allocation per tick.

- It steps at a fixed 120 ticks per song second. Events that have exact
  times (beats, stride steps, impacts) fire at those times inside a tick, in
  a fixed order.
- Randomness comes from one seeded generator per run, never from time or
  hash ordering.
- The same stream, keys and seed always give the same run on the same
  runtime and platform. Across runtimes (IL2CPP, Mono, .NET) and CPUs
  results may differ by a floating-point rounding, as the 3D demo measured,
  so each is checked only against itself.
- Every run hashes its settled event log; replays check the hash.

### The sim's contract

The client talks to the sim through a narrow interface: it feeds entries,
keys and time, and reads state and an event log.

```csharp
sim.Add(StreamEntry e);         // held by the sim until its known_at
sim.AddKnown(StreamEntry e, double knownAt); // a replay's recorded known_at
sim.Press(Key k, double heard);  // effective time = the sim's current time
sim.Command(DebugCommand c, int arg = 0); // the debug surfaces', effective now, like a key
sim.Control(RunControl c);       // start, pause, resume, Backspace, device change, watchdog: effective now
sim.StepTo(double songTime);
ReadOnlySpan<SimEvent> sim.EventsSince(int cursor);
sim.State                       // hero, enemies, items, run clock
sim.Stream, sim.Beats, sim.Strides // what the sim knows: reads only
```

- **Reads never change the run.** `Add`, `AddKnown`, `Press`, `Release`,
  `Command`, `Control`, `StepTo` and `DiscardEventsBefore` are the only mutators. Everything else
  the client reaches is read-only (`{ get; internal set; }`, dense arrays as
  `ReadOnlySpan` getters) or a pure function of the revealed entries, so an
  overlay may ask about any song time, as often as it likes. The run's random
  generator and the stepping machinery are internal to the sim (its tests
  see them through `InternalsVisibleTo`). The sim keeps each entry's
  known-at time beside it (`sim.Stream.KnownAt(kind, i)`).

- **The sim enforces causality itself**: it accepts entries as soon as they
  arrive and reveals each to its rules only when the sim reaches its
  `known_at`.
- **Events** carry what presentation needs to anticipate and react:
  attack (wind-up start, impact time, direction, kind, heavy, reach, a free
  press's beat grade), enemy
  step (from, to, move start, footplant), enemy knock (from, to, start, end),
  hit, death (impact direction and force), cleave (centre, radii, band
  times), drop, structure glint, break (strike point, direction, force) and
  regrowth (start, end), zone change, spawn (a recycled enemy's
  re-entry flagged), pickup, level-up, chest, freeze, sound cue, and the
  run's start, finale blast and end.
- **State** is the latest settled snapshot of positions and stats. The
  client keeps a short history of past states to show `frameSong`, which
  trails the frontier.
- The sim never calls the client.

### Content data and units

Content is JSON owned by the game (game/Content/, whose README maps every
native role and weapon to its entry), converted once from native's YAML and
then edited as the game's own. The sim loads it directly, so tests read the
same files as the game.

- One scale converts native's pixels to metres: **about 27 px per metre**.
  It matches a native character's height (about 48 px) to the 1.8 m Synty
  hero, and native's medium-zoom view (800 px across) to the 3D demo's 30 m
  view.
- At that scale native's sword reach (190 px) is about 7 m, its hero speed
  (140 px/s) about 5.2 m/s, and a grid tile 1.5 m (native's stride at a
  0.29 s step).
- Health, contact damage (`power`), experience and knockback keep native's
  Vampire Survivors units.
- Executors tune any of these by feel and record the reason.

An enemy role, for example. `refSpeedMps` is its speed when the hero
strides at the reference period (see
[Speeds Follow the Tempo](#speeds-follow-the-tempo)): native's actual speed,
VS's `0.231 x 1.1 x speed / 100` U/s, 0.28 of the hero's at `speed` 100. Its
gait pattern `x` means it steps on every main beat; `x.` would mean every
other beat. Its model, scale and crystal colour are in cast.json.

```json
{ "id": "skeleton", "refSpeedMps": 1.4519, "health": 1.5, "power": 10,
  "xp": 2, "radiusM": 0.4444, "knockback": 1, "maxKnockback": 3,
  "levelScaled": false, "brute": false, "gait": { "pattern": "x" } }
```

### Sessions and replay

Every run is saved, so any run can be watched again, replayed headlessly and
debugged.

- The plugin writes the analyzer's input audio and every entry with its
  stamp.
- The client writes each key press with its heard and effective times, each
  entry's final `known_at`, the seed, the build and runtime, the flags, and
  the settled hash.
- The last 20 sessions are kept, as in native.
- **Watch my run** (its title row, or W, after a run) replays the session in
  the player with its recorded music; Backspace or a click stops it.

**Shipped (ps-msz.6.10).** A session is a folder: the plugin's `audio.wav`,
`entries.jsonl` and `meta.json`, and the client's `inputs.bin` and `run.json`
(`Sim/Session/SessionRecord.cs`). Decisions:

- **What the sim records is the record.** The sim keeps every accepted entry's
  `EntryRecord`, the time it took the entry (the frame's starting frontier) and
  its final `known_at` (`sim.Stream.Accepted`, in arrival order, so entry i is
  `entries.jsonl`'s line i), beside its keys, debug commands and run controls
  (start, pause, resume, quit, device change, watchdog), which carry their
  order among each other. A replay (`SessionReplay`) adds each entry at the
  time it was taken with its recorded `known_at`, and gives each key, command
  and control at its effective time in that order, so it steps through the live
  run's states exactly. Adding entries any earlier changes the run: an entry
  that arrives in a frame that steps no tick is known at the frontier itself,
  and an earlier add would reveal it a tick sooner.
- **`inputs.bin` is binary**: the run's inputs (`Sim/Session/RunInputs.cs`:
  seed, start time, start minute, gate, `B`, Easy, the level, the arena and the
  camera footprint, with a byte for the hero standing, which is the level's), the run's end frontier, event count and
  hash, the entries' records and the inputs, doubles as their bits, so no
  runtime's number formatting can move a replay. `run.json` repeats it for
  people, with the build, runtime, platform, source and command line.
  `RunInputs` is the one value a run's `SimConfig` is built from
  (`RunInputs.Build`, live and in replay): the client parses the command line
  once (`Client/LaunchOptions.cs`), puts its run inputs on the build (the level
  bake, the arena, the camera's footprint) and builds the config from them,
  and the session writes that same value; every other config field is the
  level's, the content's or fixed (`SessionTests` holds each field to one
  list). A session records the run's level, the slice's, an open one or the
  run's; version 2 (ps-msz.6.12) added each input's argument (a debug command's
  role, weapon or time) and the start minute, and version 1 still reads. A run
  a replay couldn't rebuild (`--level`, whose files a replay doesn't have,
  `--cast`, `--arsenal` and `--no-cards`) isn't recorded: the launch says so as
  it starts (ps-msz.7.4), and `--watch` refuses it.
- **The Replay source** (`native/crates/plugin/src/replay.rs`) plays
  `audio.wav` (the analyzer's 44.1 kHz mono input, on both channels,
  interpolated to the output rate by the render thread as a System source's
  is) from song time 0, and hands over every recorded record as fast as
  `ps_poll` takes them, parsed back exactly. No analyzer, no models and no
  record. A replay's client applies each entry when its run took it, waiting
  for the source only if it falls behind.
- **Where**: one folder per run in the player's data folder,
  `~/Library/Application Support/Playlist Survivors/Playlist Survivors/sessions/<time>`
  (`Client/Loop/Sessions.cs`), the newest 20 kept. Windowless runs (checks and
  evidence) record only with `--sessions DIR` or `--watch`; `--no-session`
  records nothing.
- **Replays**: `--replay DIR` plays one in the player and quits at its end,
  with 1 if the hash differs in the runtime that recorded it; `--watch`
  replays a run as soon as it ends (and `--seconds N` then ends the run, not
  the launch). After a run started from the title screen, the title screen
  offers W, which watches it and returns. `game replay DIR` replays one in
  .NET (`game/dotnet/PS.Game.Replay`), and `game replay DIR --player` in the
  built player, headless and at fixed time (its frames drive the recorded
  music: a 30-minute session in minutes, [Fixed time](#fixed-time)). Each checks the hash only against a session of its
  own runtime, since runtimes may round differently: a player's session
  replayed in .NET reports a difference without failing.
- Evidence: [game/evidence/replay](../../game/evidence/replay/README.md). Slow CI
  records a 10 s headless run of the Mono player, watches it at once to its hash
  and replays its session in .NET (`game replay --record FILE`), then replays it
  at fixed time and runs the file twice at fixed time to one hash (the frames-driven
  path, [Fixed time](#fixed-time)).
- A headless replay in the same runtime must reproduce the hash.

**The timing log (ps-sba).** A replay rebuilds what the sim did, not how the
frames ran: a hitch, a late entry or a dropped sound on the machine that played
the run happens only once. So every recorded run also writes `timing.csv` into
its session folder (`Client/Loop/TimingLog.cs`), always on, as native writes
its `timing.jsonl` beside each session. One row per frame of the loop, from
the run's setup:

- `interval_ms` (the time since the previous frame) and `cpu_ms` (the main
  thread's CPU time over that interval, from the previous frame's Update to
  this one's, so it holds the previous frame's loop; IL2CPP only, -1
  elsewhere). A long interval with little CPU time waited on the GPU, the
  display or the OS.
- `loop_ms` (GameLoop's Update), `step_ms` (the sim's step, the dispatch and
  the presenters) and `sim_ms` (the step alone).
- `render_ms`, `gpu_ms` and `present_wait_ms`: Unity's FrameTimingManager for
  the latest frame it has timed, a few frames behind; -1 where it timed
  nothing (most of a batch-mode player's frames).
- `frame_song`, `lead_ms`, `phase`, `frozen`, `alive` and the hero's
  `hero_x`/`hero_y` (nan before the sim's first step).
- The frame's share of the run's counts: sim events, kills, entries and late
  entries, key changes (the player's, the bot's and `--walk`'s), sounds scheduled, dropped and cut off, effects spawned
  and rejected, collections, presenter faults, freezes, the plugin's stalls.

Rows are written without allocating, into a 64 KB buffer flushed about once a second.
A 30-minute run at 120 fps is about 30 MB, a tenth of its `audio.wav`. The log
never stops a run: one it can't open or write says so once in the player's log
and the run goes on without it. A run killed mid-flush can leave its last row
cut off; `game log` leaves that row out.

`game log [SESSION] [--from S] [--to S] [--hitch MS] [--no-table]`
(`demo3d/tools/src/demo3d_tools/timing.py`) summarises the newest session's
log, or SESSION's: interval and CPU percentiles, each part's work, the lead,
the counts, every hitch over 33 ms at the frame that took it (its song time
and work beside the interval and CPU time), and a table per second of song.
For what the sim did frame by frame (events, moments, the camera), replay
the session with the `--*-out` flags instead.

## Gameplay Rules

This section specifies what the sim does. It starts from native's run as it
plays today (survivors-run.md, revised by feel-restoration.md) and changes
only what the grid, the 3D presentation and the lookahead require.

### The run

The run is native's 30-minute run, built on a **run clock** that counts
playing time.

- **Start**: Enter, Space or a click starts a run on the next beat; headless
  runs start after 2 s of clear beat, as in native.
- **Run clock**: advances with song time except while the world is frozen. It
  drives the schedule, chest tiers (how many rewards a chest holds) and
  victory.
- **Freezes**: a level-up, a chest, a pause (Esc, the pause button or focus loss) and the music
  stopping each freeze the world. Song time continues; enemies and the hero
  bob in place on the beat.
- **Music stopped**: 5 s below the analyzer's silence threshold, a confirmed
  song end, or the watchdog firing freezes the world with a "MUSIC STOPPED"
  banner. It resumes on the second bar downbeat after the beat locks again,
  or, when the music has played 8 s without a lock (a beatless track after
  the gap), on the free pulse, as a beatless passage plays.
- **Ends**: victory at 30:00 (the finale blast clears the screen on the next
  downbeat, then the victory card); death ("YOU FELL"); Backspace ends a run;
  an output device change ends it.
- **Easy**: enemy contact damage at half, and floor chickens that heal, as in
  native.

**Shipped (ps-msz.6.1).** The sim's `RunRules` (game/Assets/Game/Sim/Run/):

- Every input that changes the run is a recorded **run control**
  (`sim.Control`, `sim.Controls`, effective at the sim's time like a key):
  start, pause, resume, quit, device change and watchdog.
- **Start**: `SimConfig.Start` is `OnKey` with a window (Enter or Space),
  `AfterClearBeat` headless or under `--fixed-time` (the first committed beat
  2 s after the beat turned reliable), and `Immediately` without music, and
  for tests and bots. Before the start the world is held and the run clock
  is 0; a `RunStart` event marks the beat.
- **The run clock** is exact: a freeze stops it at its own song time, not at
  a tick's end.
- **Freezes** emit a `Freeze` event for each reason's start, and one for each
  at the resume. A reason let go (resume, a confirmed card) resumes play on
  the next committed beat, or on the next stride step when no beat is known
  ahead. `State.FreezeReasons` holds the banners' reasons.
- **Music stopped**: native's silence is a tick's energy at or below 0.01.
  The stop needs 5 s of it, or 3 s around a confirmed end, or the watchdog
  (the client gives `Watchdog` when the plugin's `Releases` count rises). It
  freezes on the first stride step after that is known. The beat locks again
  on the first reliable beat after the quiet, and play resumes on the second
  downbeat after it: one whole bar is heard. Quiet before then restarts the
  lock.
- **A beatless track after a stop** (ps-msz.6.14, a change from native, which
  waits for the lock): once the music has been above the silence threshold for
  8 s since the freeze (`RunRules.BeatlessResumeS`) with no lock, the music
  stop lets go, and play resumes on the next committed beat, or the next
  stride step on the free pulse. The world then plays as in a beatless passage
  mid-song. Native's rule held the world under MUSIC STOPPED for 56 s while
  the survivors playlist's piano aria played, whose beat turns reliable only
  56 s in, about 220 s of each pass of the playlist. Most tracks lock well
  inside 8 s and keep the second-downbeat resume; a hard one (hiphop-92-swing,
  reliable 11.6 s in) now resumes on its own weak beats.
- **Victory**: the run clock stops at exactly 30:00 (schedule.json's
  `durationS`). The finale blasts on the next downbeat the world plays: 1000
  damage to every enemy and a `FinaleBlast` to every structure within
  33.3 m of the hero. The world then holds, and `RunEnd(Victory)` follows 3 s
  later. A fall, Backspace and a device change each emit `RunEnd` at once,
  and the run clock stops there.
- **Easy's chickens**: a dying brute drops one 5% of the time (the run's
  generator). A scheduled boss (a minute's `bosses`, native's survivor
  boss) drops `bossChickens` (`FloorChickens.BossDied`). The hero eats one within 0.89 m while the world plays:
  it heals 30 at once (native waits for the next sixteenth) and can't be
  hurt for 0.3 s.
- **The client**: Esc, focus loss or the HUD's pause button pauses under the pause menu (ps-msz.6.11), and Esc
  or its Resume resumes. The pause button sits bottom right and shows only while the mouse moves, fading 2 s
  after it rests, so the play screen stays clean. The menu's rows light under the mouse and a click picks.
  The loop finishes on Backspace or a device change at once, and on a
  victory or a fall 5 s after the end, for the HUD's card. `--easy` turns
  Easy on. The bob in place during a freeze is the presenters' idle.

**Shipped (ps-msz.8.4).** The run's pacing and its late crowd, tuned in the
game's content (native's stays as it was). Evidence:
[game/evidence/sweeps](../../game/evidence/sweeps/README.md).

- **Pacing**: the experience curve is the game's own (Pickups below), so the
  positioning bot's four sweep runs track progression.json's `startLevels`
  within 20% at every five minutes: level 10 to 11 at 5:00 (design 10), 13
  to 14 at 10:00 (15), 17 to 21 at 15:00 (19), 22 to 23 at 20:00 (23), 25
  to 26 at 25:00 (27) and 31 to 34 at 30:00 (30), against 14 to 17 at 5:00
  and 57 to 65 at 30:00 before. All four still win, lowest health 62% to
  87%; the idle bot falls at 2:05.
- **The late crowd's mix**: minutes 20 to 29 hold four to six roles of
  different crystals, and minute 19 three (schedule.json), where minutes 21
  to 26 were mostly the mushrooms' and 28 and 29 the bats'. The immobile flower left the
  rosters, since it piles up uncaught at the cap; minute 25's flower walls
  (50 each now, from 100) and the brute xlflower keep the orange. The slow
  brutes are spread over three roles (xlmummy, xlmantis, xlflower), and the
  level-scaled bats split between bat4 (minute 28) and bat5 (29). Minute
  20's two bat swarms and minute 29's are now a bat swarm and a ghost swarm
  of 20 each (from 50). With the waves' turns (Enemies), no role holds more
  than 46% of 250 or more alive at any minute from 20 to 30 in the sweeps
  (297 of 300 were mushrooms at 25:00 before), and every minute's peak
  shows four to seven crystal colours.
- **Boss chests**: the sweeps took each chest 0.5 to 4.7 s after it fell, so
  neither the 2 m clear radius nor the bot's seeking is slow. Chests came
  late because their bosses died late (minute 20's at 25:25 lived five
  minutes at the hero's level 41), which the slower levels shorten. One
  run's minute 20 chest fell on a walled-in tile and was never taken (the
  bot stood beside it for six minutes): a chest now lands only where the
  hero can walk (Pickups below).

### Hero movement on the grid

The hero occupies one tile of a square grid aligned with the arena, and moves
one tile per stride step in one of four directions.

- Stride steps and presses follow the [Key Presses](#key-presses) section.
- A stride takes 110 ms from its step's time (a press's: from the press),
  with a small hop.
- The hero faces its travel direction, and turns toward the swing's target
  for each attack.
- A step into a structure tile or the arena's edge doesn't move: the hero
  bumps in place and the step is spent.
- Enemies don't block the hero's tile; contact damage comes from their
  lunges.
- While the world is frozen, held keys don't move the hero.

### Speeds follow the tempo

On a fixed grid the hero's speed is one tile per stride period, so it changes
with the music. To keep Vampire Survivors' speed ratios, every enemy's travel
is measured against the hero's.

The stride period is the beat halved until it is at most 0.4 s, so it always
lies between 0.2 s and 0.4 s, and the hero's speed between 3.75 and 7.5 m/s
(a little wider with the hysteresis below).

- **Hysteresis**: a song keeps its current number of halvings while the
  period stays between 0.18 s and 0.45 s. It switches only when the period
  leaves that range, and only on a downbeat (on any beat while no lock has
  given the song bars), so a song near a boundary never flickers between
  speeds.
- **Pace bands**: pace rises with tempo within a band, then halves when a
  song crosses into the next one. A 149 BPM song strides every 0.20 s; a
  151 BPM song every 0.40 s.

An enemy role's speed `r` is its speed divided by the hero's reference speed
(5.2 m/s). Per hero stride, the role travels `r` tiles' worth of distance:

```text
enemy step distance = r * tile * (hero strides per enemy step)
skeleton: r = 1.45 / 5.2 = 0.28, steps every beat
100 BPM  beat 0.60  2 strides per beat  hero 5.0 m/s  step 0.28*1.5*2 = 0.84 m
140 BPM  beat 0.43  2 strides per beat  hero 7.0 m/s  step 0.84 m
155 BPM  beat 0.39  1 stride per beat   hero 3.9 m/s  step 0.28*1.5*1 = 0.42 m
```

- In every row the skeleton moves at 0.28 times the hero's speed.
- The run clock, spawn schedule, damage and cooldowns are unaffected.
- Knockback and death launches, which are forced reactions, use fixed
  distances.
- With no beat, enemies hold (see the [Enemies](#enemies) section), so no
  scaling is needed.

### The sword

The sword is the starting weapon, a white crystal blade. Its numbers use
Vampire Survivors' stat names: **Might** multiplies damage, **Area**
multiplies size, **Cooldown** multiplies intervals, and **Amount** adds
strikes or projectiles.

- It strikes on every main beat, or on every other one when the beat runs
  faster than 150 BPM: a beat tracked at double time (Sunflower's 90 BPM
  heard as 180) would otherwise swing back to back with no follow-through.
  Each point of Amount adds a strike on a subdivision (one extra strike
  means eighth notes of the strikes).
- Damage is a full circle around the hero, with a reach of about 7 m times
  Area. The swing's direction and arc are presentation only.
- The visible strike shows the blade's cut and that radius: the trailer's
  gold slash, sweeping the clip's way round on its arc, plus a crystal wave
  that rings out to the reach in every direction, a faint line on an
  ordinary strike and a white-hot band on a heavy one (ps-w9p.3).
- Its per-hit power comes from native's rank table times Might, with no
  tempo weakening.
- **Heavy strikes**: a beat with a detected accent (a strong onset within
  50 ms of the beat, visible ahead thanks to the lookahead) or an accent
  forecast deals 2.5x damage plus an outward cleave wave. A forecast and
  its onset never make two heavy strikes, and two strikes in a row are
  never both heavy.
- **Direction**: chosen at wind-up from the enemies in reach, weighted toward
  crowds and bosses, nearest on ties; with nobody in reach, the hero keeps
  facing.
- **Presentation**: alternating left and right slashes on ordinary beats,
  quick follow-ups on subdivisions, a spin on heavy strikes, and a slam on
  the first downbeat after a section change.
- Attacks play on the upper body over the stepping legs, so moving never
  erases the strike pose.

**Shipped (ps-msz.3.3).** The sim's sword is `Sim/Weapons/Sword.cs`, turned
an arsenal slot since ps-msz.6.4 (`SimConfig.Arsenal`: its rank, the hero's stats, `SwordWindupS`). As built:

- `n = max(1, floor(rank amount + Amount))` strikes per strike beat, at
  `T + k * p / n`, p the interval to the next strike beat when it is known
  at planning, else its period (two beats' on the half-time pulse).
- **Strike beats** (ps-at1): every committed beat, unless the tracker's
  period (the beat's `period`) is under 0.4 s (`Sword.HalfTimeBelow`, 150
  BPM): then every other beat, the **half-time pulse**. Either way a beat
  strikes only 0.75 of its strike interval (a period, or two on the pulse)
  or more after the previous strike beat. Counted in time, the pulse
  survives the tracker flipping to the half tempo and back (Sunflower's
  0.33 s grid turns 0.67 s for three beats every few bars): a flip that
  shifts the phase costs one 1.5-beat pause, never two swings a fast beat
  apart. Two accented beats in a row that the pulse skipped, each within
  8 beats of the one before and no accented strike beat between, move the
  pulse onto the second; a stray accent never does. The move is a 1.5-beat
  pause too, never a swing a fast beat after the last: when the second
  accent is known as the beat before it is planned, that beat gives way and
  the accent strikes; known later, the pulse pauses past it to the beat
  after next. Skipped beats count their accents even when Amount's
  subdivisions land on them (ps-avv). A stream's first two
  beats carry the analyzer's 0.28 s placeholder period, so the pulse skips
  the second.
- With no beat known ahead, the same pulse
  repeats every rank interval times Cooldown (1.35 s at rank 1), counted
  from the latest pulse, unless a revealed beat comes within half an
  interval.
- A beat's strikes are planned at `T - 0.32 s`, or as soon as the beat is
  revealed; one revealed at or after its time is missed. Each winds up at
  `max(impact - 0.32, the previous impact)`: the attack event's time is its
  wind-up start, always before its impact.
- At the wind-up: heavy (a main strike, the music clear, a revealed strong
  onset or forecast within 50 ms of the beat, decided once per beat, and
  the previous main strike on a beat not heavy), the
  kind (`HeroAttackKind`: slam, spin, follow-up, or alternating slashes),
  the direction (native's aim: 16 directions, a 150° scoring arc, bosses
  1000 and brutes 2, nearest on ties, else the grid facing) and the reach
  (`7.04 m * rank area * Area`, x1.3 heavy), in the event's `Reach`.
- The impact lands after the enemies' steps and lunges on its beat. Bodies
  whose centre is within reach plus half their radius take
  `rank power * Might` (x2.5 heavy), then die or are knocked back by the
  sword's knockback. A death carries its launch distance as its force
  (`10/27 m * 5`, x2.5 heavy) and drops a gem (`SimEventKind.Drop`).
- A heavy strike's cleave wave (`SimEventKind.Cleave`) rings from 9.15 m to
  15.07 m in seven 0.93 m bands, the first on the next eighth and then one
  a sixteenth, each enemy hit once at the strike's power.
- The slam goes to the zone change's downbeat (Music-Driven Events, Zones:
  a structure change or loud jump at least 8 s after the previous zone
  change), when the change is known at that strike's wind-up, else to the
  next downbeat. On the half-time pulse, which may strike only between the
  two-beat bars' downbeats, it goes to the first strike beat after the zone
  change instead. Each detected zone change gets one slam; the fallback gets
  none.
- Hero health (`HeroHealth`): a landed lunge takes
  `max(1, power * (0.5 on Easy) - armor)`, then 0.12 s of lockout (hits in
  it show with 0 damage). At none the hero falls (a death with enemy -1);
  it then neither moves, strikes nor takes hits. `SimConfig.HeroStands`
  keeps a tenth of its health instead.
- Bosses are marked at spawn (`EnemyState.Boss`: a minute's `bosses`,
  ps-msz.6.2), and the aim weighs them by that mark.

### The other weapons

The arsenal is native's, with its numbers, its scripted **offer table**
(the three cards offered at each level) and its evolutions.
Each weapon gets a crystal and particle look; an evolution adds a distinct
layer.

- **Magic Wand**: crystal bolts at the nearest enemy, on eighths. Evolves
  into Holy Wand, a stream of prism bolts.
- **King Bible**: crystal shards orbiting the hero, snapped to the beat when
  near its natural speed. Evolves into Unholy Vespers, a permanent larger
  ring.
- **Lightning Ring**: lightning striking from the sky. Evolves into Thunder
  Loop, double strikes.
- **Garlic**: a pulsing prism aura. Evolves into Soul Eater, larger and
  draining health.
- **Chain Lightning**: bolts arcing between enemies.
- **Fire Trail**: a burning prism wake on the hero's path.
- **Sword**: evolves into Bloody Tear.

Rules for every weapon:

- A weapon fires on the first point of its grid (beats or eighths, per
  weapon) after its interval times Cooldown has elapsed, counted on an
  unsnapped schedule, so snapping never loses throughput.
- With no beat, weapons fire on their raw intervals.
- Six weapon slots and six passive slots. The passives are native's eight
  stat items: Hollow Heart, Empty Tome, Spellbinder, Duplicator, Pummarola,
  Spinach, Magnet and Armour.
- Weapons rise through ranks 1 to 8 and passives to 5 (Duplicator to 2), one
  rank per pick, as in native. An evolution needs the weapon at rank 8 and its partner passive, and comes
  from a chest.
- The hero has a visual aura from the start (not Garlic's damage aura),
  growing with the build.

**Built ahead (ps-msz.4.4).** The sim has one damage path for every weapon,
the sword's today:

- `Horde.InReach(t, x, y, reach, into)` lists the live enemies whose
  bodies touch a circle at t, in slot order. `Horde.Damage` applies a hit:
  the hit event, then the death and gem drop or the knock, all at t. The hit
  and the death's launch go along the blow; the knock goes away from the hero,
  as native's survivor recoil does for every weapon (ps-msz.6.18). It
  adds the whole amount to the weapon's tally, `RunRecap.Damage`
  (`SimState.Recap`), indexed by content's weapon order, for the end card.
- The hero's attacks and hits name their weapon by content index, and
  attacks also name its shape. The views read them typed:
  `AttackEvent.ByHero`, `Enemy`, `Weapon`, `Shape`, `IsSword`,
  `SwordKind` and `EnemyKind`; `HitEvent.OnHero`, `Weapon`, `WeaponHit`
  and `EnemyKind`. The sword is content's weapon 0 with shape 0, so its
  events are bit for bit what they were.
- The knight's clips, the slashes and the swing sounds play only for
  `IsSword` attacks (the sword and Bloody Tear). A new weapon draws,
  sounds and animates nothing until its own bead gives it a look. A hit's
  white chips and hit sound play for any weapon.

**Shipped (ps-msz.6.4).** The arsenal's core, `Sim/Arsenal/` (`SimConfig.Arsenal`,
with `SimConfig.Content` now the one content field for the horde and the arsenal):

- `Arsenal` holds six weapon and six passive slots (`State.WeaponSlots`,
  `State.PassiveSlots`) and the hero's final stats (`State.Stats`): base, plus each
  passive rank (Hollow Heart compounds max health 20% a rank and heals what it adds),
  clamped to progression.json's caps. Armor and max health reach `HeroHealth` at once;
  Recovery heals per second of run clock.
- It is **one timed source** in `Simulation.RunTick`, its slots in slot order. A
  weapon is a `Weapon` (Sim/Weapons/Weapon.cs). The sword keeps its beat cadence
  (`OnBeats`) through its own `NextTime`/`Fire`/`Tick`. Every other weapon is shot by
  the arsenal: its cooldown counts on the run clock from when it was learned,
  interval times Cooldown at a time, never from a shot. A shot ready at s fires on the
  first point of its grid (beats or eighths) at or after s among the beats revealed
  then (`BeatClock.GridAtOrAfter`), the sixteenth after the slot's previous shot if
  that one holds the point, or at s with no beat known ahead. While the next beat is
  not yet committed the latest one continues, its grid carried on at its period as the
  stride grid carries its step, so shots stay on the grid when beats are known only just
  ahead. An interval that depends on duration (King Bible) adds its duration times
  Duration, as native's. A freeze unplans the
  ready shots and the resume plans them again, so they fire on the resume beat.
  Throughput stays within 0.2% of `600 / interval` at 70, 128 and 174 BPM.
- A weapon whose bead hasn't landed is the base `Weapon`: ranked, timed and shot, each
  shot an attack event naming it. 6.5 to 6.7 subclass it (override `Shoot`, or add
  times of their own) and add their case to the factory in `Simulation`'s constructor.
- `ArsenalConfig.Cards = false` counts levels without opening cards, for tests of
  other systems.

**Shipped (ps-msz.6.7).** Garlic and Fire Trail, each in its own files
(`Sim/Weapons/Garlic.cs` and `FireTrail.cs`, `Client/Weapons/`), with native's numbers:

- **Garlic** (and Soul Eater, any aura): each shot is a new incarnation, an attack event with
  shape Aura whose reach is the aura's radius (0.32 VS units, 1.83 m, times Area). Then, at once
  and at each tick's end until the next shot, every live enemy touching the circle around the hero
  takes power times Might, once an incarnation, as native's GarlicAura. An incarnation keeps its
  shot's radius and power. Each of its hits on a survivor adds native's knockback debuff: 0.3 more
  knockback from every later blow while below the role's maximum (`Horde.Weaken`). It rests while
  frozen, fallen or not playing.
- **Fire Trail**: on the grid the wake is the tiles the hero stood on. Each shot is a pulse: an
  attack event with shape Trail, then each tile the hero stood on during the last duration times
  Duration of run clock, skipping its latest 0.12 s, newest first, burns. A burn is a
  `SimEventKind.Burn` event at the tile's centre (`BurnEvent`: weapon, x, y, radius), and every
  enemy touching that circle (20 px, 0.74 m, times Area) takes power times Might, once a pulse.
  A standing hero burns its own tile, as native's burns the samples under it. Run clock, so a
  freeze doesn't age the wake.
- **Looks** (`GarlicAura`, `FireTrailView`, on `PrismGround`: shader Game/PrismGround, a ground
  ring or disc in the ability's colour with a prism sheen, white-hot where brightest). Garlic is a
  mint aura at exactly the radius hit. Its rim swells out and flares at each pulse, it breathes
  with a kick on every beat, and motes lift off its edge (`garlic-pulse`, a ring of sparks). Each
  burning tile is an orange disc, white at its centre, flaring at the pulse and glowing while the
  pulses renew it, with embers rising (`fire-trail-flame`). Soul Eater's own layer is the chests
  bead's (`GarlicAura.Tint` has its colour).
- **Sounds**: each pulse and hit has its own category (`garlic-pulse`, `garlic-hit`,
  `fire-trail-pulse`, `fire-trail-hit`: native's palette). A weapon that voices its own hits
  tells the sound presenter (`SoundPresenter.VoiceHitsElsewhere`), so the sword's hit sound
  leaves them. The white hit chips are every weapon's.
- `Weapon.Tick` takes the sim; `Power`, `Radius`, `Launch` and `Holding` moved from the sword
  to `Weapon` for every weapon.
- The player's `--arsenal "garlic:8,fire-trail:3"` sets the starting weapons and `--walk
  "right@9-11"` holds keys over song-time spans, for stills of a walking hero.

**Shipped (ps-msz.6.5).** Magic Wand and King Bible, `Sim/Weapons/MagicWand.cs` and
`KingBible.cs` (each also serves its evolution's rules; the evolutions' own layers are the
chests'). Native's numbers throughout; its flight model is adapted:

- **Magic Wand**: a shot on an eighth is a burst of `max(1, floor(amount + Amount))` bolts,
  `repeat / (speed x Speed)` apart, as native's. A bolt flies a **leg per target**, at the nearest
  live enemy, at `9.43 m/s x speed x Speed`, arriving on the first sixteenth at or after its
  straight flight (at that flight with no grid known that far ahead), so its hit sound lands on the
  grid. It is aimed where its enemy will stand then, as the enemy moves now, and hits that enemy at
  the arrival wherever it stands, for `power x Might`. A hit that doesn't kill uses a point of pierce;
  a kill uses none (native's), so a bolt flies on through kills. A bolt whose enemy dies before it
  arrives turns at the tick's end for the next. Native homes each frame and hits whatever it
  crosses; legs keep the hits on the grid and the flight drawable from events.
- **King Bible**: a shot on a beat starts a ring of `max(1, floor(amount + Amount))` evenly
  spaced blades for `duration x Duration`, `2.86 m x min(area x Area, 4.5)` out, each blade's body
  `0.457 m x (1 + 0.3 area x Area)` growing in over 0.5 s. Its speed, `2.19 rad/s x speed x
  Speed`, snaps to native's beat lattice (`KingBible.OrbitSpeed`, `bible_orbit_speed`: every blade
  on a quarter turn on beats, within 15%) from the latest committed beat's period. Each tick every
  blade's swept path hits the enemies it touches, once until its memory clears every hit delay
  (1.7 s). Unholy Vespers' permanent ring is the chests'.
- A freeze or the hero's fall ends bolts in flight and cuts rings short. A burst's bolts still to
  leave are spaced on the run clock, so a freeze holds them and they follow the resume as far
  apart, as native's held events do.
- **Events**: `Projectile` (a leg: from, aim, arrival, the target; phases Launch, Turn and End)
  and `Orbit` (a ring: born, end, angular speed, radius, blade size; again with its new end when
  cut short). Each shot is still an attack event naming the weapon.
- **Client** (`Client/Weapons/`): `WeaponLooks` gives each weapon its family and colour (the
  wand violet, the bible gold, white at the core: `Resources/Weapons/WeaponCrystal.shader`).
  `WandView` draws each bolt as a spinning crystal with a streak of afterimages along its legs,
  bending onto its enemy once the leg's end has settled; `BibleView` draws the shards on the
  sim's ring around the hero, leaning into the turn. Their casts and hits are the effects'
  `prism-cast` and `prism-hit`, tinted by the weapon (`EffectCues.Spark`), and the sounds'
  `wand-fire`, `wand-hit`, `bible-fire` and `bible-hit` (native's palette), which the views ask
  for themselves, as Garlic's do. A session refuses to record a starting arsenal other than the
  run's own (`--arsenal`), which its replay couldn't rebuild.
  Evidence: [game/evidence/weapons](../../game/evidence/weapons/README.md).

**Shipped (ps-msz.6.6).** The lightning, by native's rules (engine/lightning.rs and its chain), each
bolt a `SimEventKind.Bolt` (`BoltEvent`: weapon, `BoltKind`, its place in its volley or chain, from
and to, the radius it hits) followed by its hits (`WeaponHit.Bolt`); each shot is still an attack event:

- **Lightning Ring** (`LightningRing`, shape `strike`): a shot is a volley of `Weapon.Amount` strikes
  (native's `amount(stats)`), the first on the shot's grid point, each next on the sixteenth after
  (`BeatClock.SixteenthAfter`, a quarter of the free period with no beat). Each lands on a live
  enemy in view drawn at random by the sim's generator, hitting every body touching
  `0.4571 m x rank area x Area` with `rank power x Might`, struck out from its centre. Native's
  view is its camera's; the game's zooms with the music, so in view is the ground every zoom
  shows: `LightningRing.ViewShare` (0.6) of the way to `HordeConfig.CameraFootprint`, the loud
  view grown by the focus's slack. With nobody in view a strike lands nowhere.
- **Thunder Loop**, its evolution, strikes each point again on the first sixteenth at or after
  1.5 s later (`BoltKind.Return`), wherever its enemy went.
- **Chain Lightning** (`ChainLightning`, shape `chain`): a chain of `Amount` jumps a sixteenth
  apart, each to the live enemy the chain hasn't hit nearest where it starts (the hero, then the
  enemy before), closer than `9.63 m x area` for the first and `5.19 m x area` after, hitting
  every unhit body touching `0.59 m x area` around it; with nobody in range the chain ends.
- A time planned on a beat's continuation moves to the sixteenth a beat committed since says
  (`BeatClock.SixteenthAtOrAfter`, native's deferral). Nothing strikes or jumps while the world is
  frozen or the hero has fallen; what a freeze held goes on from the beat play resumes on.
- The player's `--no-cards` counts levels without opening their cards, so `--arsenal` stays as
  given in weapon stills ([evidence](../../game/evidence/weapons/README.md)).

**Reviewed (ps-msz.6.18).** The six weapons against native's rules, and what changed:

- **Enemies by life.** A slot freed by a death goes to the next spawn, often within the same
  tick (the free list is last in, first out). `EnemyState.Life` numbers each spawn, never
  reused, and every weapon that remembers an enemy past the tick keeps its life: a wand bolt's
  target and hit memory, the bible's blades, Garlic's incarnation and a chain's hits. Before,
  a bolt whose enemy died could hit the newcomer in its slot wherever it stood.
- **Knockback away from the hero** for every blow (`Horde.Damage`); the hit and the death's
  launch keep the blow's own way (a blade's sweep, a bolt's flight, a strike's centre, a jump).
- **Shots keep their numbers.** A lightning volley's strikes and returns, and a chain's jumps,
  keep the power, radius and ranges of their shot through a level-up, as native's pending strikes
  and chain state do; so does Garlic's incarnation.
- **The run's end.** No weapon shoots, hits or burns once the run isn't playing (`Weapon.Rests`,
  native's `weapons_active`), the finale's wait for its blast included.
- Left as is: the wand's bolts reach enemies past the view (native's die 96 px outside it); a
  chain's spacing with no beat is the lightning's anchored sixteenth, not native's plain quarter
  free period; shape `jumpBeats` is loaded but unread.

**Shipped (ps-msz.6.8).** The five evolutions, each its base weapon's class with its own numbers,
rules and layer (`Client/Weapons/EvolutionLayers.cs`, colours from native's slice/evolution.rs):

- **Holy Wand**: the wand's rules at its numbers (twice the shots, twice the speed). Its bolts are
  gold crystals streaming twelve prism afterimages through six hues.
- **Unholy Vespers**: its newest ring has no end (`End` infinity in its `Orbit` event) until the next
  shot starts one, which ends it at that shot or at its own duration if later: a permanent ring, as
  native's bridges each cycle into the next. A freeze still cuts it, as King Bible's. Its shards are
  violet, over a violet ring of light on the ground that kicks on the beat.
- **Thunder Loop**: the ring's double strikes (ps-msz.6.6) in magenta; each strike and return also
  throws a magenta shock ring along the ground.
- **Soul Eater**: Garlic's aura, larger by its numbers, draining health (native's combat.rs): each
  hit heals the hero 1 + Recovery, and a shot's power grows by a point for every 600 health healed
  since the evolution, by any means (`HeroHealth.Healed`), up to 6. Each hit draws a teal soul out of
  its enemy, arcing into the hero's chest, and the hero glows teal as they arrive.
- **Bloody Tear**: the sword at its numbers, its blows critical at its crit chance times Luck
  (drawn from the run's generator only for a rank with crits), for its crit multiple
  (`HitEvent.Critical`); a critical blow heals the hero 8, at most once a second of run clock. Its
  slashes are crimson (`EffectCues.SlashTint`), and each hit bursts crimson, larger when critical.
- **The arrival**: a reward that evolves sweeps three rings in its colour out from the hero at the
  landing, with six rays of sparks.
- **Structure breaks** (Crystal Structures): an evolution's hit within 50 ms of an accented beat
  (`Accents`, shared with the sword's heavy strikes) breaks the structures it reaches
  (`Weapon.BreakOnAccent`, `StructureHit.EvolvedAccent`): Holy Wand's bolt, Unholy Vespers' blades,
  Thunder Loop's strikes, Soul Eater's pulse and Bloody Tear's strike. All of one accent's evolved
  hits count as one breaking hit on a structure, as a heavy strike and its wave do.

### Enemies

Enemies keep native's roles, stats and spawn schedule, with continuous
positions on the arena floor, and all their travel is on main beats.

- **Travel happens only on main beats with enough evidence** (native's
  reliability gate). Main beats are the committed beats themselves, never the
  stride grid's subdivisions.
- **Each step is sharp**: the move takes at most `min(100 ms, 20% of the
  beat)` immediately before the footplant, which lands on the beat. The rest
  of the beat is a stationary hold.
- **Cadence**: roles step every beat (`x`) or every other beat (`x.`); fast
  roles take longer steps, never extra ones. Distances follow the
  [Speeds Follow the Tempo](#speeds-follow-the-tempo) section.
- **Pathing**: each step heads down a flow field toward the hero that routes
  around structures and the arena's edges. Separation between enemies is
  resolved as part of the step, never as a shove between beats.
- **Long steps** test contact along the whole move, so a fast bat can't pass
  through the hero.
- **Contact lunges**: a notice pose, contact on the beat, a return. The
  notice comes half a beat ahead, for enemies within contact range plus
  their step and the hero's travel in half a beat; only those lunge.
- **Knockback** is immediate and between beats; pursuit resumes on a later
  main beat. A death launch is free.
- **A decided step is final**, like every settled event: if the beat's
  state drops inside its move window, it still plants (native cancels it).
- A step follows the flow field's path and ends at the farthest point a
  straight move still reaches without crossing a structure, so long steps
  round wall ends. It heads straight for the hero only when the hero is in
  reach and in sight. Where a step meets a structure it slides along it, and
  no step slips between structures that meet at a corner. The flow field
  reaches about 150 m of path from the hero; beyond, steps head straight
  for the hero.
- **No reliable beat**, or a frozen world: enemies hold, and lunges stop. On
  relock or the thaw they resume on a future main beat, with no catch-up
  steps.
- **Bosses** telegraph with an in-place pose and deliver charges and slams as
  steps on main beats.
- **Cap**: at most 300 alive.

Spawning follows Vampire Survivors' ring, bounded by the arena:

- New enemies appear just outside the camera's footprint on the ground, at
  points inside the walkable arena and not inside structures.
- When the arena's edge is in view, spawns use the ring's in-bounds arc.
- Out-of-sight enemies far from the hero are recycled onto the ring, as in
  native.

The cast maps native's roles onto the 3D demo's nine models:

- Bats (five tiers, the XL bat, the bat swarm): the bat, scaled up (x2) for
  the XL bat and the boss tiers 4 and 5.
- Zombie (hopping): the slime.
- Ghost and ghost swarm (fast flyers): the demon.
- Skeleton: the skeleton.
- Mudman 1: the goblin. Mudman 2: the big orc.
- Werewolf: the troll.
- XL mantis and XL mummy (brutes): the big orc and the troll at 2x.
- Flower and XL flower (slow thorn plants): the mushroom, or a new
  thorn-plant model if it doesn't read as a plant.
- Bosses: the mantis boss is the dragon; the werewolf boss a troll at 2x; the
  mummy boss a big orc at 3x; the flower boss the mushroom at 3x.
- The signature minotaur (see the
  [Music-Driven Events](#music-driven-events) section): the troll at 2.5x.
- Set-piece formations reuse these models.

Each type keeps one crystal colour, from its original pack's colours, and
spawns in same-type clumps so the horde reads as masses of colour.

**Shipped (ps-msz.6.2).** The spawner is `Sim/Enemies/Spawner.cs` (split out
of the horde), the bosses' moves `Sim/Enemies/BossMoves.cs`, turned on by
`HordeConfig.Schedule`; the player plays them with `--run` (`RunLevel`; the
slice stays the default until the full run, ps-msz.6.14). Evidence in
[game/evidence/schedule](../../game/evidence/schedule/README.md) and
[game/evidence/cast](../../game/evidence/cast/README.md). As built:

- **Waves** follow native's rule on the run clock: every minute's interval,
  one of each roster role, then whole rosters while fewer than the minimum
  are alive. The minimum scales by loudness as native's `levelScale` (0.6
  quiet, 1.3 loud, blended over the last 2 s; schedule.json's `run`), and a
  quiet wave with that many alive is skipped. Waves spawn at tick ends;
  native snaps them to the next known eighth, which the game's enemies don't
  need, since they hold until a main beat anyway. A pass that the cap cuts
  short starts one role further along the roster each wave (ps-msz.8.4):
  native's always starts at the first, so at the cap the first-listed role
  took every free slot and filled the crowd.
- **The opening** (ps-w9p.4, the game's own numbers, in every mode): native's
  first minutes (15 bats every 1 s, then 30 and 50) left the screen empty
  for seconds at a time once the sword cleared a clump, the crowd a single
  violet mass from one side. Minute 0 is now bat3 and bat1 every 0.5 s to
  a minimum of 40 (52 loud), minute 1 zombies and bat1 every 0.5 s to 60,
  minute 2 to 80; minute 3 on are native's. The first wave lands as two
  clumps at once and is on screen 1.3 s into the run; a hero who keeps
  killing faces about 30 to 60 alive through minute 0 and 60 to 90 in minute 1,
  where native's left it 3 to 24 and 25 to 53. With nothing dying the cap
  fills at about 1:05 instead of 2:18. Evidence in
  [game/evidence/opening](../../game/evidence/opening/README.md).
- **The crowd's floor** (ps-w9p.12, in every mode): a hero on the move
  outruns the crowd (5.2 m/s against 1.2 to 2.9, on main beats only), and
  native's later minimums dip to 10 to 60, so the screen still emptied for
  seconds at a time, often at the cap with the whole crowd out of sight
  behind. Three changes hold it: while fewer than `run.viewFloor` (60) are
  in view and the hero is on the move (its displacement over the last
  1.5 s longer than 1 m), enemies out of sight behind it, beyond the spawn
  ring, are brought round onto the ring ahead of it, a clump a tick within
  0.8 rad of its heading (anywhere walkable at an edge or a wall); the
  waves' minimum counts only the horde's own, not the music-driven events'
  scripted enemies (a flower wall left behind no longer stops the waves);
  and no minute past the opening has a minimum below 80 (minutes 3 to 6, 9,
  10, 12, 14, 17 and 18 raised; loudness still scales it). Over a 30-minute
  run, the positioning bot's stretches with fewer than 15 on screen for over
  2 s fell from 46 to 1 on the survivors playlist and from 55 to none on
  house-128. The experience curve's factor rose to 3.2 to keep the levels on
  `startLevels`. Evidence in
  [game/evidence/floor](../../game/evidence/floor/README.md).
- **Clumps**: a role's share of a wave lands as one clump, and a role keeps
  landing within about 7° of a point of the ring for `run.clumpS` (3 s)
  after choosing it, so a trickle of one a wave still gathers into one mass
  of colour, and the masses come from all round.
- **Bosses**: a minute's `bosses` come due at its start and land on the next
  committed downbeat (or at a tick end 4 s of run clock later with none),
  70% of the way to the footprint's edge, so they're seen to arrive. Waves
  leave a slot for each boss waiting; a boss with no room waits for one.
  A boss's health, and a level-scaled role's, is its role's times
  `HeroState.Level`. The first boss of a minute with treasure carries its
  chest, its tier rolled at landing as native's `treasure_tier`; its death
  drops it (`SimEvent.Chest`: a `Drop` of `ItemKind.Chest`, value the tier,
  `Minute` the treasure's minute), and on Easy its chickens.
- **Boss moves** (schedule.json's `bossMoves`, for the four `boss-*`
  roles): 4 main beats after landing and then every 8, with the beat
  reliable and the delivery beat already committed, the boss telegraphs on
  a footplant (an `Attack` of `EnemyAttackKind.Charge` or `Slam`, impact the
  delivery beat 2 beats later) and holds in place, neither stepping nor
  lunging. With the hero within 4 m it slams: on the beat, the hero within
  reach takes the role's power and every structure within 4 m is struck
  (`StructureHit.BossSlam`, 4.63 m throw). Otherwise it charges: the
  delivery beat's step, a sharp step like any other, up to 9 m along the
  line to where the hero stood at the telegraph, stopped by structures and
  at contact, hitting the hero for its power if in contact along it. A
  delivery beat that comes unreliable, frozen or with the boss knocked back
  fizzles.
- **Clips**: the bosses' models (dragon, troll, big orc, mushroom) bake a
  telegraph and a charge clip (Editor/Crowd/roster.json). The telegraph
  fills the warning and holds; a slam then plays the attack clip, its
  contact on the beat; a charge's step runs the charge clip.
- **The mushroom stays the flowers' model**: no pack has a thorn plant, and
  in crystal at cast scale its cap and stalk read as a plant (game/evidence/cast).

### Crystal structures

Crystal structures are obstacles that shape the arena, catch and reflect the
light, and break on big moments.

- **Blocking**: each structure occupies grid tiles. The hero can't enter
  them; the flow field routes enemies around them.
- **What breaks them**: the sword's heavy strike and its cleave wave, a
  boss's slam, the finale blast, and an evolved weapon's hit within 50 ms
  of an accented beat. A small structure breaks in one such hit, a large
  one in two or three. Every other hit only glints.
- **On the impact frame**: shards start moving on the hit's frame, released
  outward from the strike point; the whole structure is in flight within
  50 ms. Cracks never show before the pieces move.
- **Feel**: a post flash, a small camera punch and shake, layered glass
  sounds pitched by size.
- **Regrowth**: on each zone change (a detected section change or the timed
  fallback, both in the [Music-Driven Events](#music-driven-events) section),
  broken structures regrow at their sites on the next downbeat, erupting
  from the ground over one beat with a burst.
  - A site with the hero within 3 m waits for the next zone change.
  - Enemies on a site are pushed out, as a forced reaction.
- **Navigation**: breaks and regrowths patch the flow field's obstacle grid,
  with time-sliced rebuilds.
- Structure breaks stay far rarer than deaths: structures near the hero's
  path clear early in a section and return only at the next one.

**Shipped (ps-msz.3.4).** `Sim/Structures/Structures.cs`, built from the
level data's sites in every sim, with `SimState.Structures` (standing,
hits, grow times) for the client. As built:

- A hit is a disc around a centre: every standing structure with a tile in
  reach is hit at its nearest point. The sword's heavy strike (from the
  hero, at its reach) and each cleave band (from the wave's centre, at the
  band's outer radius) break; one heavy strike and its wave hit a structure
  once. Ordinary strikes glint (`SimEventKind.Glint`). The boss slam,
  finale blast and evolved weapon are `StructureHit` sources M3 passes to
  `Structures.Strike`.
- A small structure breaks in one breaking hit; a large one in two, or
  three when it covers 9 tiles or more (3 of the arena's 8 large sites).
  Hits short of the break glint with their count.
- `StructureBreak` carries the strike point, the direction from the hit's
  centre through it, and the hit's throw in metres as its force (a death's
  launch: 4.63 m heavy, 1.85 m from the wave). The tiles open to the hero
  at once and the nav grid is patched; the flow field restarts its
  time-sliced rebuild on the next tick.
- Regrowth goes to the first downbeat after the zone change, for the
  structures broken at the change; `Regrowth` carries the start (the
  downbeat, when its tiles block again) and the end, one beat period
  later. Hits while it grows only glint. The 3 m test measures from the
  hero's position and from the tile it stands on or is striding to, to the
  site's nearest tile edge. Pushed enemies get a 0.12 s `EnemyKnock` to the
  nearest open cell, or stop short when they were stepping into the site.
- While the world is frozen, regrowth waits for a downbeat after the thaw.

### Music-driven events

The clock decides who spawns; the music decides when things happen and how
they look. These hooks are native's:

- **Zones**: a detected section change (a structure change, or a loudness
  jump of 6 dB or more) at least 8 s after the previous zone change (or the
  run's start) moves to
  the next of five zones, cycling back to the first after the fifth.
  - It lands on the first downbeat at or after the change's `t` if that is
    known in time, otherwise on the first downbeat after its `known_at`.
  - **Fallback**: with no detected change, the zone changes 60 s after the
    previous zone change (or the run's start) during the first 5 minutes,
    then 4 minutes after.
- **Swarms**: each minute's scheduled swarm event (bats, ghosts, a wall of
  flowers) waits for the first big loudness jump in that minute.
- **Musical set pieces**, each from its native start time:
  - the **stampede**, a charging line after a loud jump that follows quiet;
  - the **kick line**, a marching line during sustained medium loudness;
  - the **wedge**, a V formation every 16 beats in quiet passages;
  - the **ring**, a closing ring every 16 beats in loud passages.
- **The signature minotaur**: a scripted mini-boss that lands on a reliable
  beat at 90 s and slams every 8 beats.
- **Camera zoom by loudness**: the view is about 28 m wide when quiet, 30 m
  at medium and 34 m when loud, measured across the ground through the hero.

The lookahead lets these land exactly: a zone change can start on the
downbeat where the new section is heard, and a swarm can arrive with the
jump instead of after it.

**Shipped (ps-msz.3.4): zones.** `Sim/Structures/Zones.cs`, on in any sim
with content (schedule.json's `zones`), emits `SimEventKind.ZoneChange`
(new and previous zone, cause, the cause's time and known-at) and keeps
`SimState.Zone`. As built:

- Changes and jumps are taken in known-at order, once the run has started.
  The 8 s gap is from the previous zone change's downbeat, or the run's
  start (ps-at1), to the candidate's `t`. The analyzer reports a loudness
  jump about 6 s into a stream as the music comes in, and the player
  usually starts after it: counted from the song's start, that jump opened
  the run with a zone change and a slam. One kept change waits for its
  downbeat at a time, and the next is judged against it once it lands.
- "Known in time" reduces to the first downbeat at or after both `t` and
  `known_at`.
- The fallback's "first 5 minutes" reads the run clock at the previous
  zone change. It lands on the first downbeat at or after its due time; with
  no downbeat 4 s after that (no beat locked), at that tick's end. A
  change waiting for its downbeat holds the fallback off.
- Nothing lands while the world is frozen.

**Shipped (ps-msz.6.3): swarms, set pieces, the minotaur and the zones'
lighting.** `Sim/Enemies/MusicEvents.cs` (with `Swarms`, `Pieces`,
`SignatureBoss` and `Scripts`), on in a run (`HordeConfig.MusicEvents`,
`RunLevel.Config`). Each start emits `SimEventKind.SetPiece` (its
`SetPieceKind`, members, and the cause's time and known-at); evidence in
game/evidence/music-events/. As built:

- **Within the cap**: a start makes room by sending the ordinary enemies
  farthest from the hero away (`SimEventKind.Leave`, no death or gem). The
  members move by script on their gait's main beats (a line, a closing ring
  or a slot beside the hero), unbent by separation; fliers (the swarms, the
  bees) cross structures. When an event ends, members out of sight leave
  and those in sight join the horde's pursuit: nothing vanishes on screen
  (native expires them all).
- **Swarms**: a minute arms as the run clock enters it; the first revealed
  jump of 6 dB or more that sounds in it, judged by the run clock it will
  sound at, unlocks it, and its first attempt lands on the jump's own main
  beat. The repeats follow every `delayS`, one per row after a gap; the
  fallback is 10 s before the minute's end. Bats and ghosts come as a flock
  from 0.72 view widths out and fly through; the flower wall rings the hero
  with a 60-degree exit and closes to 9 m over 16 beats (native's closes
  at the flowers' crawl and is never seen).
- **Set pieces**, judged on reliable main beats, one at a time, 30 s apart,
  none while swarm members or a boss due are about: the stampede on the
  first beat at or after a qualifying jump; the ring and wedge on beats
  whose index is a multiple of 16; the kick line after 16 medium beats.
  The kick line stands across the hero's heading (native's ran along it),
  and its dancers pose lunges in turn as their kick. A swarm or the
  minotaur takes the stage from a piece in play.
- **The minotaur** lands once, on the first reliable main beat at 90 s or
  later with no boss waiting, just inside the view, as a boss without a
  chest. Its slams (`BossMoves.Signature`) land on every 8th main beat from
  its landing, telegraphed two beats before at a mark under the hero (the
  attack event's `SlamX`/`SlamY`), so stepping off the mark dodges it. It
  retreats after 90 s.
- **Loudness zoom**: the camera reads the stream's ticks through the fast
  lane (ps-msz.3.7); the set pieces' level is the spawner's 2 s blend.
- **Zones' lighting**: `Client/Arena/ZoneLighting.cs` eases the moon,
  ambient, fog, clear colour, hero light, ground tint and mist to the
  zone's palette over two bars from the change's downbeat, beside the
  post's grade offset.
- **Every zone is a night** (ps-msz.6.24). The 3D demo's blossom and snow
  are day looks (ambient and fog about 4x neon's, ground tints of 2 to
  3.2): every run turned pink at its first zone change and white by its
  second, the horde at dL* +8 and -6.6. The game's arena-layout.json keeps
  their hues at neon's darkness (the demo's palettes are unchanged); the
  call was to keep each zone's colour in the moon, ambient, fog, mist and
  ground tint and take only the light down:
  - blossom, a magenta-rose night: moon (1, 0.5, 0.85) at 0.8, ambient sky
    (0.1, 0.05, 0.13), equator (0.14, 0.04, 0.12), ground (0.05, 0.015,
    0.045), fog (0.15, 0.04, 0.13), hero light 40 in (1, 0.65, 0.85),
    ground tint (0.7, 0.45, 0.68), mist (0.62, 0.16, 0.45);
  - snow, an ice-blue night: moon (0.62, 0.76, 1) at 0.8, ambient sky
    (0.05, 0.09, 0.17), equator (0.04, 0.07, 0.12), ground (0.015, 0.025,
    0.05), fog (0.05, 0.09, 0.16), hero light 40 in (0.7, 0.88, 1), ground
    tint (0.52, 0.64, 0.92), mist (0.24, 0.36, 0.6);
  - crypt keeps its grey light, its ground tint (0.55, 0.62, 0.8) and mist
    (0.22, 0.22, 0.24) taken down from the demo's mid-grey ground (ring L*
    57 to 28); lava already met the bar.
  - The post's zone grades lift no exposure for blossom and snow (they had
    +0.08 and +0.1 stops); their saturation, contrast and temperature
    leanings stay.
  - Settled, the horde reads at dL* +44 (blossom), +34 (snow), +47 (crypt)
    and +65 (lava) on house-128, and +55 or more mid-ease
    ([game/evidence/music-events](../../game/evidence/music-events/README.md)).
- **Debug**: `DebugCommand.Stampede` to `Minotaur` (player
  `--debug "stampede@T,kick@T,wedge@T,ring@T,bats@T,ghosts@T,flowers@T,minotaur@T"`)
  start one on the next main beat in any run with enemies.

### Pickups, level-ups and chests

Pickups and progression are native's.

- Gems drop on death and climb a pentatonic chime when collected; the pickup
  radius is native's base radius times the Magnet passive.
- Experience follows native's curve.
- **Level-up**: the world freezes, and three cards from the scripted offer
  table flip in on beats; arrows choose and Space or Enter confirms, or the
  mouse chooses (pointing) and confirms (clicking). Keys
  held when the cards open are ignored until released. Play resumes on the
  next beat.
- **Chests**: dropped by scheduled bosses, collected only when the hero is
  clear of the crowd. The roulette ticks on eighth notes and lands on a
  downbeat. Chests may evolve weapons at minutes 10, 15, 20 and 25.

**Shipped (ps-msz.6.4).** Gems, experience and the level-up (`Sim/Arsenal/`):

- **Gems** (`Gems`): a death drops a gem worth `floor((r + 0.5) * xp)` from the run's
  generator, so a weak enemy's is often worth nothing and none drops. At the field's
  cap of 400 a drop grows the nearest gem (a `Drop` again, same item). At a tick's end
  a gem within the pickup radius (4.44 m times Magnet) plus its body takes off
  (`GemFlight`) and flies by native's attraction, stepped at 60 Hz of run clock: an
  outward kick, then a pull ever faster. Collected, it arrives on the first sixteenth
  at least 0.06 s later: the `Pickup`, which the client chimes up its pentatonic chain,
  and the experience. Gems wait while the world is frozen.
- **Experience** (`LevelUps`): value times Growth (plus 1 at levels 20 and 40) against
  `factor x level²`. `State.Level`, `Experience`, `ExperienceFill` and
  `PendingLevels`, copied with the offer into the client's `SimSnapshot`.
- **Offers** (`Offers`): native's offer table and fallback exactly; the tests check
  every level from 2 to 70 against native's own cards along two pick paths.
- **The level-up**: each level opens an offer (`LevelUp`: its three cards packed, and
  the beats they flip in on, `State.Offer`) and freezes the world (`LevelUp`). Past the
  revealed beats the next card flips a period of the latest beat on, where native
  waits its free period. The keys are recorded keys: Left, Up and Right select card 1,
  2 and 3 (native's), Space or Enter (`Key.Enter`) confirms once the third is in, each
  a `Card` event. Keys held when the cards open wait for a fresh press, and the hero
  lets go of its held keys, so the key that picked a card never walks it afterwards.
  The next level's offer opens at once; after the last, play resumes on the next beat.
  While the pause menu is up the cards take no keys. The mouse taps the same recorded
  keys (`KeyInput.Tap`, ps-072): pointing at a card taps its key, and a click taps it
  and Enter, so a session replays a mouse pick like a key's. Each tap carries the
  serial of the offer the HUD shows and reaches the sim only if, when the keys are
  handed over, the sim still has that offer open: a card still on show after a key
  or an earlier click took it never walks the hero.
- **The client**: `GemView` draws the gems as instanced faceted crystals
  (Resources/Gems: Gem.shader, and Gem.mat with instancing on, which keeps the shader's
  instanced variant in the player) in progression.json's blue, green and red, hovering and
  throbbing on the beat, popping in at their drop, kicked out and pulled into the
  hero's chest on their flight. The HUD draws the cards (`Client/Hud/LevelUpCards`). Headless and fixed-time runs confirm the
  selected card through `AutoCards`, a recorded Enter half a second after the third card
  flips in (game/evidence/arsenal).
- **Pacing**: on the slice's horde (220 alive from the start) in an open arena, a hero
  walking a square through its kills reaches level 33 by 5:00 of run clock, far faster
  than native's run (about level 10). On the run's schedule (ps-msz.6.2) the bots' sweeps
  and the full File run measure level 14 to 17 at 5:00 (game/evidence/sweeps,
  game/evidence/full-run), against the design's level 10 there (progression.json's
  `startLevels`); the run's tuning is M4's.

**Shipped (ps-msz.6.8).** The bosses' chests (`Sim/Arsenal/Chests.cs`; native's survivors/chests.rs and
engine/chests.rs), presented by `Client/Pickups/ChestView.cs`:

- **The drop**: a boss carrying treasure drops its chest on the centre of the nearest open tile where
  it dies (`Drop` of `ItemKind.Chest`; `SimState.FieldChests`).
- **Clear of the crowd**: at a tick's end, while the world plays and the hero stands, the hero takes
  a chest within 0.81 m (native's 22 px) when no live enemy's body is within 2 m (`Chests.ClearM`).
  Native's engine has no such rule (its showcase's bot waits for a gap); the game makes it the sim's,
  so the roulette never freezes the world mid-brawl. In a 30-minute .NET run on the survivors
  playlist, a hero put on each chest as it fell took all 15, after 0.01 to 3.1 s.
- **The roulette** (`SimState.Chest`, `SimEventKind.Chest`: Open, Tick, Land, Reward): taking it
  freezes the world (`FreezeReason.Chest`); it ticks on eight eighths from the next downbeat (two of
  the beat clock's two-beat bars) and lands on the downbeat after them. Its times are points of the
  revealed beats' grid, carried on through the latest beat's continuation; with no beat known it ticks
  half a free period apart. A pause or the music stopping holds it, and it goes on from the next point
  after. It fires before the beat at one time, so play resumes on the landing's own beat, and levels
  reached meanwhile open their cards there (an offer never opens over a spinning roulette).
- **Rewards** (native's chest_item): the tier's 1, 3 or 5 items in turn, never one not owned: an
  evolution when the minute's treasure evolves (schedule.json's minutes 10, 15, 20 and 25), in
  progression.json's evolution order, of a weapon at its top rank whose partner passive is owned
  (`Arsenal.EvolutionOf`, `Arsenal.Evolve`: the evolution takes its slot at rank 1, ready on the run
  clock now); else a rank of the lowest-ranked owned weapon, then passive; else the heal. In the
  .NET run the hero evolved Bloody Tear at 15:00, Soul Eater at 20:00 and Holy Wand at 25:00.
- **The look**: on the field, a gold crystal reliquary hovers over its tile, turning and throbbing on
  the beat, a shard for each of its items circling it, under a faint gold column with a glow on the
  ground; it pops in with a gold burst. Taken, it rises over the hero's head; on each eighth it
  flashes and kicks and its shards take the next weapons' colours, as the HUD's cards cycle. At the
  landing it bursts in gold and each shard, in its reward's colour, flies into the hero's chest under
  a sweeping ring of light. Sounds (native's palette): coins as it is taken and as it lands, a glass
  ping on each eighth climbing the major pentatonic, one transformation for a landing that evolves.
  The HUD's `ChestCards` (ps-msz.6.9) takes the chest events: its card opens with the roulette,
  its gems cycle on each tick as it comes, and TREASURE FOUND shows each reward's name and rank from
  the landing, until level-up cards opening meanwhile take the screen.
- **Debug**: `--debug chest@S` (`DebugCommand.Chest`) drops a tier-3 chest of the last evolving
  minute's treasure two tiles ahead of the hero, for it to walk onto. Evidence: [game/evidence/evolutions](../../game/evidence/evolutions/README.md).

**Shipped (ps-msz.8.4).** The game's own experience curve, and chests the hero can always reach:

- **The curve**: `factor x level^exponent` (progression.json's new `experience.exponent`), then 1.9 x
  level^2.8 in one band, against native's 5, 6.5 and 8 x level². Reaching level 10 takes 893
  experience in all (native's 405), level 20 7232 (1805) and level 30 23630 (5467). The factor is 3.2
  since ps-w9p.12's denser crowd (The crowd's floor, Enemies): level 10 takes 1503, 20 12180 and 30 39798. The steeper power follows the
  design's flattening pace (`startLevels`: nine levels in the first five minutes, three in the last
  five) as the bot's income grows. Growth's bonus at levels 20 and 40 is unchanged.
- **Chest drops**: a chest lands on the nearest open tile joined to the hero's own by steps in the four
  directions (a search of the arena at each drop), never in a pocket the arena's dressing walls off,
  where the old nearest open tile could put it.

## Presentation

This section specifies what the client draws and plays. Its baseline is the
reference look, and most of its machinery is copied from the 3D demo and
reworked to consume sim events.

### The look

The look is the reference command's, held on every frame of a live run.

- **Dark world**: Synty ruins and forest in near darkness under a violet
  moon (the neon palette: purple ambient and fog, magenta mist, a dark
  violet vignette).
- **Light comes from crystal and combat**: the cyan hero light, the
  structures' crystal reflecting it, emissive effects through bloom, and a
  few flame lights.
- **Rainbow crystal**: monster types in distinct gem colours; structures in
  hues across the spectrum, shifting with the zone's palette.
- **White blade**: the hero's procedural crystal blade, white, flaring on
  each strike.
- **Readability**: monsters stay at least 17 L* away from the ground, darker
  or lighter, the 3D demo's floor.
- **Zones** recolour runtime lighting with night versions of the 3D demo's
  palettes (neon, blossom, snow, crypt, lava), eased over two bars from the
  change's downbeat: each zone a night in its own hue.

**Shipped (ps-msz.8.3).** M4's look pass, measured against the reference
stills ([evidence](../../game/evidence/look-m4/README.md)): every zone settled
and mid-ease at the run's density reads +37.7 to +71.8 dL*, and no still of
the run's peak clips more than 0.65% of its frame (the heavy strike 5.5%
before).

- Snow's moon is down to 0.55 and crypt's is a cool slate at 0.55 with darker
  ambient and fog (arena-layout.json): both read as overcast dusk before
  (ground L* p50 about 12, against neon's 4 and the reference's 3.5 to 8).
- A hit lights a crystal half white and half its gem colour
  (`CrystalSurface.hlsl`), so a crowd under constant hits, and a mass kill's
  shards on their break frame, keep their hue instead of washing to pale.

### The arena

The arena is one large bounded space, generated in the editor from a single
placement list, as the 3D demo's is (ps-msz.3.6; evidence in
[game/evidence/arena](../../game/evidence/arena/README.md)).

- **The layout**, game/Content/arena-layout.json, is the 3D demo's format:
  240 m square (the 3D demo's is 160 m), 160 × 160 tiles of the hero's
  1.5 m grid. Eight clearings, paths 10 m wide between them, ruins,
  landmarks and forest groves give the run varied places to kite: about
  17,400 m² of open ground.
- **Bounded by dense forest**: every tile past a fighting ground's edge band
  is forest, which the level data blocks whole, so the outer forest (at
  least 15 m deep) bounds the arena and the groves are obstacles. The edge
  bands block only under their bushes and logs.
- **The engine-free layout code**, game/Assets/Game/Arena/ (the
  PS.Game.Arena assembly, also built under .NET), is the 3D demo's
  ArenaLayout copied and owned. Its LevelExport turns the placement list
  into the sim's `ArenaData` (game/Assets/Game/Sim/Level/Arena.cs): the
  tile size, the origin and size in tiles, the blocked tiles, the
  structure sites and the hero's start. A piece covers a tile when it
  reaches more than 0.25 m into it.
- **The bake**, `game unity BakeArena` (and `game build` when its inputs
  change), builds the scene into the gitignored Assets/Baked/Arena/ in
  about 30 s and writes the level data to game/Content/arena.json, which the
  sim's tests check is current.
- **Lighting**: the ground is lightmapped, and its shadowmask holds the
  static kit's shadows; the kit is lit by a light-probe grid (every 8 m) and
  samples the static shadow map, as the 3D demo's lit rung is. Crystal
  structures are excluded from the bake, because they break and regrow:
  they cast realtime shadows and are lit by the moon and Forward+ lights
  (the crystal takes no ambient, so the probes don't matter to it).
- The moon's static shadow map renders at load and again after a break or
  regrowth (`ArenaRuntime.RefreshShadows`).
- **Structures snap to the hero's 1.5 m grid**: their anchors on tile
  centres, quarter-turn yaws, gaps an odd number of tiles. Each set of a
  structure's pieces that share a tile is one site, large from 6 tiles; the
  navigation grid is twice as fine (0.75 m).
- **The crystal swap** (`CrystalStructures`) gives every site the crystal
  material at load, its gem colour a hue by its angle around the arena's
  centre, so the arena holds the whole spectrum and neighbours read as a
  family.
- **The void world** (neon-look.md, The Axes > World; ps-wnj.8): under a
  look's world option `VoidArena` hides the Synty set, the mist and the
  flames' lights (the grid floor replaces the ground), and under its
  structure look each site's kit pieces give way to the look package's
  crystal props standing on the site's tiles in the build's `arena.json`, in
  the palette's `shatter.gem`. The crystal swap remains only for a look
  without a structure look. Zones then tint the floor and fog with the
  palette's zone hues instead of easing the zone grade and lighting.
- **The default zone** is the neon palette over the night: purple ambient
  and fog, magenta mist, a violet moon, a cyan hero light and eight flame
  lights. The player's `--arena` shows the arena alone in the game camera's
  framing (`ArenaStage`).

### The hero

The hero is the 3D demo's Synty knight, driven by sim events.

- It is animated by a playable graph on song time, with no state machine.
- Steps play a short hop clip timed to the 110 ms stride.
- Attack clips layer over the upper body and are retimed so the impact lands
  on the attack event's impact time.
- The blade is the 3D demo's procedural crystal blade, gripped from the
  hand's bind pose.
- A low-health pulse beats on the hero.

**Shipped (ps-msz.3.10).** game/Assets/Game/Client/Hero/, subscribed by the
frame loop when the build has the hero bake (`HeroBaker`, `game unity
BakeHero`: the knight with its shield, URP materials and clips from
game/Content/hero-clips.json, into the gitignored Baked/Hero/). As built:

- `HeroMotion` is the motion as a function of the HeroStep and hero Attack
  events and the song time; `HeroRig` sets its clip times on a playable graph
  with every speed at 0 and evaluates it at once (manual update), so stills
  show the exact frame.
- There is no hop clip short enough in the Synty packs (their jumps run
  about a second), so a hop is the run clip's half-cycle, alternating feet,
  across the step's move, with a parabolic lift (14 cm, 7 cm for a bump in
  place). A stomp stands. The hero faces its travel, and turns to the
  swing's direction from its wind-up through its follow-through. The free
  hero walking keeps its legs on the travel and twists its upper body to the
  swing instead, up to 120 degrees, past which the whole body turns and the
  legs come off the forward run to a backpedal (the run clip backward)
  ([Swinging on the move](game-modes.md#swinging-on-the-move), ps-w9p.2).
- Attacks: slashes alternate left and right (Light Combo A and B), a
  follow-up continues the combo (A, then B, then C, then B again), heavy
  strikes spin (Heavy Flourish) and section changes slam (Light Leaping),
  each retimed by `HeroClips.Fit` between 0.75x and 1.33x so its measured
  impact (demo3d's annotations) lands on the event's. A clip that would need
  more than 1.33x is entered part-way at the wind-up, crossfading from the
  swing before, which keeps its own impact. Heavy blows hold 50 ms at 0.6x
  after the impact, as in demo3d.
- The legs layer blends the idle, the hop or walk, the backpedal and the
  attack (full body when standing); the torso layer (the mask's body: hips
  and spine) shows the sword idle or the attack except where the free hero's
  walk carries it; the upper-body layer (head, arms) always shows either the
  sword idle's arms or the attack, so the unarmed run never swings the blade
  and stepping never erases the strike pose. After the graph, a walking
  swing's twist turns the spine's bones about the vertical.
- The blade is demo3d's mesh and grip, drawn by Game/CrystalBlade on the
  structures' shared include; it flares to 1 at each impact (1.5 heavy) and
  fades over 0.22 s.
- The low-health pulse: under a third of its health the knight's emission
  beats red on each beat. The aura's hook is `HeroPresenter.AuraAnchor` (the
  feet, unturned) and `Aura` (0.2 at the start, growing with the build).
- Evidence: [game/evidence/hero/](../../game/evidence/hero/README.md).

### The crowd

The crowd is the 3D demo's GPU-driven renderer, fed by the sim instead of a
timeline (ps-msz.3.8; evidence in
[game/evidence/crowd](../../game/evidence/crowd/README.md)). Since ps-wnj.4 the
renderer, its instance struct, slots and shaders, the shatter and the beat
globals live in the shared look package
(`unity-packages/com.playlistsurvivors.look/Runtime/Crowd/`, namespace
`PS.Look.Crowd`; [neon-look.md](neon-look.md), The Package); the game keeps
its feeder (`CrowdView`, `MonsterTrack`) and its baker in
game/Assets/Game/Client/Crowd/ and Editor/Crowd/, the baker calling the
package's unweld step (`StyleMesh.Unweld`) and shard cutter.

- **The bake**, `game unity BakeCrowd` (and `game build` when its inputs
  change), turns the nine models of Editor/Crowd/roster.json into the
  gitignored Assets/Baked/Crowd/: per model, three LODs (LOD1 about half the
  triangles, LOD2 a quarter) as unwelded style meshes in one submesh, and
  the idle, walk, attack and hit clips at 30 Hz in a bone texture. The
  library also holds the cast (cast.json: each role's model, scale and gem
  colour), so a cast change rebakes.
- **Weapons** ride their hand bone rigidly, baked into the mesh. The two
  roster.json attaches, the big orc's Slayer and the troll's club, sit in
  the fist by their `grip` (ps-9rw, demo3d's ps-b5z.2; crowd.md, Common
  requirements): `Roster.Spawn` runs the handle through `Fist` (from the
  hand's finger bones, shared with the hero's blade) instead of leaving
  Synty's blade pointing out of the palm, then turns it by the grip's
  `palm`, `pitch`, `yaw` and `roll` (both elites: 0.02 m, 20, 90, 0).
  Prefab-carried weapons (the skeleton's and goblin's) are placed by their
  pack. `GripTests` checks the elites' grips; evidence in
  [game/evidence/crowd/grips](../../game/evidence/crowd/grips/README.md).
- **Drawing**: a compute pass culls each type's monsters against the frustum
  and the moon's shadow sweep, sorts them into depth bins and picks a LOD by
  projected height (60 and 30 px, dithered crossfades); a second blends each
  visible monster's bones once into a palette; one indirect draw per type and
  LOD, and one LOD2 shadow proxy per type, go out whatever the count.
- **Events to clips** (`MonsterTrack`, tested in EditMode): the idle loops at
  each monster's own phase; a step squeezes the walk's next half cycle into
  its move window, so the footfall lands on the beat, holds 0.12 s, then eases
  back to idle; a lunge retimes the attack clip so its annotated contact lands
  on the impact, with a 0.4 m reach toward the hero; a hit flinches from the
  hit clip's onset and flashes white over 0.12 s. Places follow the step and
  knock events' moves, as the sim's do. A death or a recycled re-entry ends
  the body on its frame; a death's frame writes it once more as Dying, which
  the palette pass blends but no draw shows, for the shatter to seed from.
  The track also keeps the look's motion clocks (neon-look.md, Motion): its
  latest step's plant, move and travel, and its latest kick (a lunge toward
  the hero from its notice, a knock along its push, a hit away from the way it
  faces), which `CrowdView` writes beside the instances for the package's
  springs; with the preset's swarm on it places each life a little off the
  sim's spot, and `--beat-ripple` delays each step event by its plant's
  distance from the hero, up to a sixteenth note. The hero's hops and dashes
  take the same spring (`HeroMotion`'s stretch and lean, drawn by `HeroRig`).
- **Slots** (`CrowdSlots`): each life keeps its type's instance slot for its
  whole life, and a freed slot shows Hidden for a frame before its next
  owner. So a frame holds last frame's slots beside this frame's new owners,
  and each type has room for twice the living (`CrowdView.SlotsPerType`:
  600 at schedule.json's cap of 300, read from the content since
  ps-msz.8.7): a crowd of one model replaced on one beat (a set piece making
  room for its members) needs it. With 375, minute 25 of the full run threw on
  every frame from its first set piece, and the crowd and the HUD stopped
  drawing (ps-msz.6.14).
- **The monster look** is the preset's (`MonsterLooks.Active`;
  [neon-look.md](neon-look.md#monsters)): the edge preset's dark-core-edges
  (`EdgeLook`: dark glass, the type's palette slot in rim, feature lines and
  a core), the solid preset's solid-emissive (`SolidLook`: bodies self-lit
  in the type's palette slot with a dark silhouette band and outline), the
  hologram preset's wireframe (`HologramLook`: lines and a faint fill added
  over the arena; `wireframe-all` on every triangle edge) and the prism
  preset's refractive-gem (`GemLook`: clear gem showing the void and grid
  behind in the slot's hue family, with reflected bands, a rim and a core)
  are built; an option not built yet draws the gem (`MonsterLooks.StandIn`),
  which replaced the crystal as the stand-in (ps-wnj.13). `CrowdView` turns
  on the camera's opaque texture whenever the gem or the crystal draws.
- **The crystal** (`Game/MonsterCrystal`, `MonsterLooks.Classic`) is the
  monsters' `classic` option alone, for parity with the look before the
  epic. It shares its surface with the structures (the package's
  CrystalSurface.hlsl): it refracts the scene behind, tinted by the role's
  gem colour, with the beat globals' facet sparkle, graded to a luminance
  peak of 0.3 so effects stay brightest.
- The looks draw in the transparent queue without motion vectors, so the
  game keeps MSAA rather than TAA. Each look applies its own fog and takes no
  ambient, so SSAO would only darken the ground beside it.

### Shatter

Deaths and breaks are the game's payoff, so they must be immediate and cheap
at scale.

**Monster shards run on the GPU.**

- Each type's rest pose is cut offline into 20–40 shards (the 3D demo's shard
  baker).
- On the death event's frame, a compute pass seeds that monster's shards from
  its current bone palette, so they start exactly where the body was.
- A compute integrator moves every live shard: gravity, a kick along the
  blow, tumble, ground bounce, then a sink after about 1.2 s.
- Shards draw as GPU instances with the crystal shader. There are no
  GameObjects and no main-thread posing, so fifty deaths in one frame cost
  about what one does.
- Each death also spawns one coherent crystal burst (chips and dust) and
  layered glass sounds pitched by size.

**Shipped (ps-msz.3.9)**: `MonsterShatter`, in the look package's
Runtime/Crowd/ since ps-wnj.4 (its tests, `ShatterTests`, in the package's),
evidence in [game/evidence/shatter](../../game/evidence/shatter/README.md).

- **The cut** is part of the crowd's bake (`CrowdShardBaker`, the package's): each
  model's LOD0 in 20 to 40 chunks closed to an apex inside the body, joined
  into one mesh per model whose corners carry their chunk and skin weights;
  each chunk's pivot carries weights too.
- **The seed**, on the death's first frame, after the crowd's palette pass:
  per model, one upload of the frame's deaths and two dispatches freeze each
  dying monster's palette into a ring slot and place its chunks' pivots on
  that pose, kicked outward from the hit point (0.6 of its height up, toward
  the hero), along the blow (harder for a heavy blow: the death's force over
  the strike's 1.85 m) and up, spinning. The body is gone on that frame.
- **The integrator** steps every live shard to the frame's song time at
  1/120 s: 2.2 g, tumble, bounces at 0.35 restitution on the ground with
  friction, then after 1.2 s a 0.6 s sink. Song time drives it, so fixed-time
  runs repeat exactly; a seek backwards clears the shards.
- **The draw** is one indirect draw per model of its live deaths' instances
  (the monster look's shard material): each corner skinned from its death's frozen palette
  and moved rigidly with its chunk, in the look's surface, inside faces
  brighter, with a `shatter.core` flash on the break. It casts shadows and draws under
  the stills' ID mask on the crowd's layer.
- **The burst hook** is `MonsterShatter.Broke` (`CrowdView.Shatter`): each
  death's time, hit point, blow, force, height, gem colour and role, raised
  on its break frame for the effects' chips and dust.
- **Cost**: the windowless bench (`--shatter-bench`) measures 1, 50 and 300
  deaths at once ([its numbers](../../game/evidence/shatter/README.md)).

**Structure shards keep PhysX.**

- Structure breaks are far fewer and larger, and shards tumbling over each
  other sell them, so they use the 3D demo's baked structure shards and rigid
  bodies in a separate local physics scene at 1/120 s.
- Shards linger about 2.4 s and then sink.

**Shipped (ps-msz.3.12)**: `StructureShatter` in game/Assets/Game/Client/Structures/
(since ps-wnj.8 in the look package's `Runtime/Crystal/`, fed the sim's events by the
game's `StructurePresenter`; under a structure look its sites are the crystal props,
whose shards are cut in code, not baked),
subscribed by the frame loop with the arena, on shards cut by `StructureShardBaker`
(`game unity BakeStructureShards`, after the arena: every kit mesh the baked sites
draw, 10 to 48 chunks each by surface area, with the crowd's cutter, into the
gitignored Baked/Structures/). As built:

- **The break**: on the first frame at or after the StructureBreak's time the site's
  renderers hide and its shards take their place, in the site's crystal and gem
  colour. There is no crack front: the quarter of the structure nearest the strike
  launches on the strike, the farthest shard 40 ms later. The physics runs one 60 fps
  frame ahead of the song clock, so the impact frame already shows that frame's flight
  (house-128's evidence: 4 of 16 and 11 of 27 shards moving 10 and 21 cm on it, every
  shard flying 50 ms later). A shard's collider waits for its launch, so the shards
  still in the wall never block the ones flying. Shards flash and their fresh inside
  faces glow as they launch.
- The flight is demo3d's (outward from the strike, along the blow and up, the nearest
  hardest), scaled by the break's force against a heavy strike's throw (4.63 m).
  FeelMoments now takes the force the same way, so a cleave band's break flashes and
  shakes at 0.4 of a heavy strike's.
- **Regrowth**: from the Regrowth event's downbeat the site rises out of the ground over
  its beat (the structure shader's `_SL_Sink`, easing out with a small overshoot; the
  facets ride with it), flashing and glowing as it comes. A third of its shards, at
  0.45 scale, burst up from its footprint. `StructureShatter.Erupted` hands the
  effects their structure burst and the sound its shatter. A break's shards still
  lying on the site sink as it comes up.
- **Glints**: a hit that doesn't break flashes the site (`_SL_PieceFx.x`), at full for a
  hit toward a large structure's break and at half for one that can't break it.
- **Shadows**: `ArenaRuntime.RefreshShadows(regions)` re-renders the static shadow map
  only where the changed sites fall on it. It clears that patch with a far-depth
  triangle and redraws the casters overlapping it through the map's projection narrowed
  to it: 0.3 ms instead of a full 8.7 ms redraw, identical to a full redraw in stills.
  It runs on the break's frame and when a regrowth is whole; the shards and the growing
  crystal cast realtime shadows meanwhile.
- Navigation needs nothing from the client: the sim patches its grid and flow field at
  the hit and at the regrowth (ps-msz.3.4).
- Cost (IL2CPP, Apple M5 Max): a break's physics is about 29 ms over its 3.4 s life, at
  most 0.9 ms in any frame; all 38 sites at once peak at 2.9 ms.
- Evidence: [game/evidence/structures/](../../game/evidence/structures/README.md).

**Checked at the peak (ps-msz.8.6)**: filmstrips of minute 25's mass kills,
a heavy strike breaking four structures, a regrowth and the finale's
290-monster break, all moving on their impact frame with nothing before it
([evidence](../../game/evidence/payoff/README.md)). Over a peak minute at 300
alive (13 deaths a second) the live monster shards are 802 median, 2,050 at
most (`--shatter-out`): scattered glass, not a carpet, so the shard counts,
kick and linger are unchanged. The finale's blast now hits through
`Horde.Damage` (a `WeaponHit.Blast` Hit event, no weapon's), so the survivors
flash and show their numbers.

### Weapon and ability effects

Effects use the 3D demo's compute particle runtime and its recipes, extended
for each weapon's look in [The Other Weapons](#the-other-weapons).

- The sword's slashes are the trailer's gold crescents, white at the core
  (ps-w9p.3; they were white); abilities carry their own colours, white at
  the core.
- Heavy strikes oversize the slash and send the cleave wave out to the reach.
- Effects are spawned by sim events at their song times, never by the
  client's guesses.

**Shipped (ps-msz.3.11).** demo3d's runtime is copied into
`Client/Effects/` (EffectSystem, EffectLibrary, EffectLights and the four
shaders), without the demo's cuts (none of the effect cuts was accepted), the
VFX Graph sink and the overdraw pass. Its converter is
`Editor/Effects/EffectConverter`, baking `game/Content/effects.json` into the
gitignored `Assets/Baked/Effects/` (`game unity BakeAll -only Effects`). Since
ps-wnj.5 the runtime, its shaders and the converter are the look package's
(`unity-packages/com.playlistsurvivors.look/`, shared with demo3d), and the
game keeps only its cues, presenter and crystal wave in `Client/Effects/`. As
built ([evidence](../../game/evidence/fx/README.md)):

- `EffectCues` holds each event's effect until the first frame whose
  frameSong reaches its time; `EffectsPresenter` spawns it. A slash belongs
  to its strike's impact, not its wind-up, and the hero's position is read
  from the frame's state then.
- The sword's slash, spin and slam are demo3d's Hovl prefabs without fire or
  smoke, through an ice-to-white heat ramp, spawned at the strike's reach
  (`scaleRef` 3.5 m), a heavy strike's 1.25x more. The crystal wave is our
  own (`CrystalWaves`, shader `Game/CrystalWave`): a ground ring from the hero
  to the reach over 0.12 s, its front exactly at the radius hit; a heavy
  strike's cleave wave picks up there and follows the bands' radii and times.
- A death is demo3d's crystal shatter burst (chips and dust along the blow),
  tinted by the role's crystal colour (`RoleLooks`, from cast.json; ice blue
  without content); one instant's deaths share their light, 1/sqrt(n) each.
  A hit that doesn't kill throws a few white chips; a structure break the
  larger `structure-burst`, and `EffectCues.Burst` is the hook for a
  regrowth's.
- A headless player has no compute, so the effects sit it out.

**Shipped (ps-msz.6.6).** The lightning's bolts (`Client/Weapons/LightningView`,
`Resources/Weapons/LightningBolt.shader`): a camera-facing ribbon mesh rebuilt each frame, additive
and HDR, a thin white-hot core in the weapon's colour (`WeaponLooks`). A strike falls 14 m from the
sky with two forks, in Lightning Ring's electric blue (native's). Thunder Loop's return is thicker,
in native's magenta, with a third fork (the evolution's layer). A chain's jump arcs from body to
body, bowed up over the crowd, in lemon yellow, and outlasts the next jump so the chain reads as
one. Each flashes overbright, then fades and flickers out, its path re-drawn 30 times a second from
the bolt and the song time alone, so fixed-time stills repeat. The effects' `strike-sparks` and
`jump-sparks` spray where they land, tinted by the weapon, with their light (`EffectCues.Spark`).
The view voices them itself (sounds.json, demo3d's packs): a strike's `lightning-strike` thunder (a
return's lower), a jump's `lightning-jump` zap climbing the major pentatonic, and a `lightning-hit`
crackle for each of their hits, in place of the sword's.

**Shipped (ps-msz.8.3).** The effects tuned so the big moments stand out and
nothing blows out ([evidence](../../game/evidence/look-m4/README.md)):

- The sword's slash, spin and slam at HDR 0.3, 0.15 and 0.7 (effects.json):
  Hovl's slash material (emission 7, opacity 20) blew a heavy spin's crescent
  to white.
- A strike's crystal ring is an ice ring on an ordinary strike (intensity
  0.2) and white-hot on a heavy one (0.8): the ordinary strike's new light
  peaks at L* p98 90, a heavy one's at 99.6 (both at white before).
- A tinted spawn's heat ramp runs at 0.6 of its emitter's gain, so Bloody
  Tear's slashes, the gem bursts and the weapons' sparks keep their colour
  further into the core.
- The lightning's coloured glow is 2.5 (was 4) around its white core, so a
  bolt reads in its colour rather than as a white band.
- Holy Wand's prism afterimages are 0.55 of the bolt's length (they are 0.6 m
  apart at its speed), so the six hues show as bands instead of summing to
  white; Unholy Vespers' crystal is at 0.6 of its violet, so its shards read
  lilac, not white.
- Every weapon's and evolution's effect is visible in each of the five
  zones in the evidence's stills.

**Shipped (ps-msz.8.11).** A slam on a zone change's downbeat clipped 1.44%
of the frame in crypt at the run's peak, where Thunder Loop's bolts landed on
the same sixteenth under the slam's flash and crypt's desaturating grade. A
bolt's overbright first frames now brighten only its white core, the core is
thinner (`exp(-160 a²)`, was 90) and the coloured glow is 2 (was 2.5): the
same frames clip 0.88% and the bolts still read in their colours
([evidence](../../game/evidence/look-m4/README.md#the-warden-ps-msz811)).

**Shipped (ps-w9p.3).** The free mode's playlist feedback (epic ps-w9p): the
swings didn't read as the trailer's slashes but as a generic white circle
([evidence](../../game/evidence/slashes/README.md)). The white slash was
Sword Slash 2 through the ice ramp at the strike's reach (twice its stock size
at free mode's 7 m), a near-full white ellipse round the hero, and every
press rang a 0.7 m ice band out to the reach. Now:

- The slash and the spin are demo3d's `spin-trail`, the trailer's gold
  crescent (Hovl's Sword Slash 11, its cleaves' and spins' slash), in its own
  colours (no heat ramp; HDR 0.6 and 0.3), with a warm light cue.
- Drawn at the blade's arc, not the reach: `EffectCues.SlashArcM`, 3 m for
  the sword's base 7.04 m reach (`scaleRef` 2.8 is the crescent's stock
  radius), growing and shrinking with the reach so a weak free press's swing
  is visibly smaller and Area's larger; 1.25x heavy and 0.85x a follow-up. The
  slam's shock still covers the reach.
- On the clip's arc and its way round: `EffectCues` picks the hero's clip
  (`HeroMotion.ClipFor`, the same attacks in the same order), and
  `EffectCues.Arc` gives each clip's sweep, measured from the right hand's
  path through its impact. SlashL, Slash3 and the spin cut right to left, SlashR
  back left to right, all through the front right (40 to 60 degrees off the
  facing; the spin 15). The crescent wipes clockwise round its back left
  (middle -135 degrees), so a swing turns it onto the clip's arc and mirrors it
  for a counter-clockwise cut: a spawn's `Mirror` (a negative scale on the
  GPU) negates the effect's x in its own frame (EffectCommon's `Frame.mirror`).
- At the blade's height, carried with the free hero: a slash spawns with the
  velocity of the hero's glide at its impact, so it stays round the knight on
  the run (a grid hero, which never glides, leaves it where it struck).
- An ordinary strike's crystal ring is a 0.3 m line at intensity 0.04 (was a
  0.7 m band at 0.2): it still shows the reach without reading as the swing.
  A heavy strike's is unchanged, so the big moments still stand out.

### Post-processing and camera

The post stack is the look preset's (the look package's `PostStack`,
[neon-look.md](neon-look.md#post-processing-stack), since ps-wnj.6; it was
the 3D demo's night look), driven by the live stream instead of the demo's
**song map** (a hand-made file of a song's beats and sections).

- **The look's stack**: the post option's tonemap, bloom and wide halo,
  colour, vignette toward the palette's void, chromatic aberration, grain,
  retro scanlines, and the renderer's SSAO. The arena keeps its ambient, fog
  and clear colour (`NightLook`) until the void world replaces it. Under
  the classic look (`--look classic` or `--post classic`) the volume is the
  night's again (`NightLook.CreateProfile`: bloom, neutral tonemap, the
  night grade's LUT, vignette) at the night's beat feel.
- **Beat feel**, read from the stream: an exposure and bloom pulse and a
  vignette breath on every beat; an exposure flash, chromatic aberration and
  saturation spike on accents, heavy strikes and breaks; a grade offset per
  zone.
- **Lookahead feel**, as native's **fast lane** (presentation that reads the
  stream directly, bypassing the sim): the drum-band envelopes, kick
  anticipation about 90 ms ahead, and build-ups in the 1.6 s before a drop.
- **Camera**: 55 degree pitch and a 30 degree vertical field of view; a
  critically damped follow spring (0.4 s) with a lead along the hero's
  heading; the view width set by loudness and eased over two bars; bounded
  punch on heavy hits and shake on breaks.

**Shipped (ps-msz.3.7).** game/Assets/Game/Client/Look/, subscribed by the
frame loop, which now plays in the baked arena (`--no-arena` for the
placeholder plane); evidence in game/evidence/look/feel/.

- The fast lane (`FastLane`, native's lane.rs) reads the sim's revealed
  entries, known by the frontier, so about 2.45 s before they sound, and
  shows each at `max(t, known_at)`; nothing is anticipated (a kick's
  lead-in, a drop's build) before its `known_at` either, so a frame depends
  only on the entries known by its song time, never on the lead. The beat
  pulses at full on reliable beats and at 0.6 on beats known ahead but below
  the gate.
- Accents are strong onsets, at most one per 0.5 s (native's flashes). A
  heavy strike is the deaths sharing an impact time, weighted 0 at 2 kills to
  1 at 24. Drops (loudness jumps of 6 dB or more) flash, punch and shake as
  they land. Breaks flash, punch and shake by their force, past the hits'
  0.25 m shake cap up to 0.4 m.
- Zone grades are keyed by the arena's palettes, neon being the base;
  `PostFeel.SetZone` is M3's hook.
- The camera's lead is 1.5 m along the heading plus the hero's smoothed
  velocity times 2τ: the 0.4 s spring alone trails a striding hero by about
  5 m. The view is 28, 30 or 34 m wide by the last 2 s's mean level, with
  hysteresis. `CameraRig.Footprint` is the loud view's ground trapezoid
  grown 4 m each side, for `HordeConfig.CameraFootprint` (the assembly
  wires it).
- The beat globals (`_SL_Beat`, `_SL_Pulse`, `_SL_BarPulse`, `_SL_Energy`,
  `_SL_HeroPos`, `_SL_WaveSpeed`) come from the lane each frame, with the
  drums as `_SL_Drums` and the build-up as `_SL_Rise`.

**Shipped (ps-msz.8.3).** A full flash lifts exposure by 0.35 stops (was
0.7; `PostFeel.FlashExposure`), its aberration and saturation unchanged: at
0.7 an accent, a break or a 24-kill impact pushed every highlight past white.
The zone grades, bloom and beat pulse are unchanged
([evidence](../../game/evidence/look-m4/README.md)).

**Shipped (ps-wnj.6).** The arena's volume is the look's stack
(`PostStack.CreateVolume`), Boot turns on its own passes (`PostStack.Use`),
and the arena sets the scene's exposure (`lighting.exposure`, 0.8 stops, the
night grade's: the baked night is lit dim). Under the options' void toe the
arena falls almost to black, so the game already reads as the void, its
monsters and effects the light.
The beat and flash amounts are the post option's `feel` (every option's flash
lifts 0.5 stops), the build-up and kick stay `PostFeel`'s, the zone grades
ride on top as before, and on a beat or a flash the bloom leans toward the
palette's `grid.pulse` and `spark` (`PostBinding`). A `--post` option not
built draws `PostOption.Plain` (neutral tonemap, nothing else): game keeps no
post of its own. Evidence per option in game/evidence/look/post/.

### HUD

The HUD keeps native's layout and hierarchy, restyled in faceted crystal and
built with UI Toolkit, whose text files coding agents can edit directly.

- XP bar and level along the top; health fixed at the top left; kills at the
  top right; the run timer.
- The six weapon and six passive slots, with empty slots kept quiet early in
  the run.
- The beat track, with beats sliding toward a heart, and a clear waiting
  state when no reliable beat is known.
- In world space: the overhead beat gem with its closing ring and a distinct
  downbeat, the life ring under the hero, damage numbers (shown times 10).
- The source tag: the tapped app's name or the file.
- Banners for pause and music stopped; level-up and chest cards that flip in
  on beats; an end card with each weapon's damage.

**Shipped (ps-msz.6.9).** `Client/Hud/` and Resources/Hud (Hud.uxml, Hud.uss, the HudMark
shader). The HUD is one UI Toolkit overlay on a 1920 x 1080 panel, native's HUD units doubled,
drawn at frameSong, with marks in world space:

- **Layout**: the mint experience bar along the top with the level left of it; health top left
  (a bite flashes it white for 0.2 s, and under 30% a red glow pulses on each beat); under it
  `DELAYED 3 S` and the source tag; the slots right of health, six weapons over six passives, each
  a crystal tile with its item's gem in native's item colour, its mark (MW, GA, HH...) and rank
  pips, an empty slot a faint dot; kills top right; the run clock bottom left. Faceted crystal is
  dark glass plates with lit top and shaded bottom hairlines, and rotated squares for gems.
- **Beat track**: the beats known by frameSong (a prefix of the revealed list, so a frame never
  depends on the frontier) slide in from both sides to a heart that beats on each; downbeats
  (`BeatClock.IsDownbeat`) are taller and warm. Past the latest committed beat the beats its
  period predicts show at 40%, without bar marks, and brighten once committed. Without a reliable beat the track empties and the
  heart greys under WAITING FOR THE BEAT. While the run waits it prompts CLICK OR PRESS SPACE.
- **World space** (`BeatGem`, Game/HudMark on camera-facing quads, HDR for the bloom): the gem
  floats 2.75 m over the hero's feet, drawn over everything. A ring closes on it from 1 m toward
  the next beat, lands on it on the beat's own frame and the gem bursts; before a downbeat the
  ring is gold and heavier, and the downbeat's burst is gold. Without a reliable beat the gem is a
  breathing pale blue disc. The life ring lies on the ground under the hero, an arc as full as its
  health. Damage numbers are panel labels projected from the world, times 10, summed per enemy and
  beat (damage.rs' rules, heavy strikes gold).
- **Screens**: MUSIC STOPPED, and PAUSED unless the pause menu is up (its heading is the banner).
  The level-up's cards flip over in the 0.2 s before each card's beat, so a face is whole on its
  beat; the chosen card swells, then lifts as the others fall away. A card has its item's gem,
  name, rank, native's description and EVO PAIR with an owned partner. The chest's card
  (`ChestCards`) cycles its gems on eighth-note ticks and lands on a downbeat; the sim's chests
  (ps-msz.6.8) fill it, and `--chest-preview S[:TIER]` still opens a client-only preview for
  stills. The end card shows VICTORY or YOU FELL, the run clock, kills and level, and per weapon its
  damage, time held and DPS (times 10).

**Shipped (ps-msz.8.7).** The end card's numbers are the sim's `RunRecap` (`SimState.Recap`,
native's eval/recap.rs): the kills, and each weapon's damage and time held on the run clock, from
joining the slots until it is evolved away (a rank up doesn't restart it, and a jump of the run
clock isn't time held). The sim tallies it as the run plays and closes it at the run's end, so the
card is the same at any frame rate and in a session's replay (RecapTests and SessionTests). Before,
the HUD tallied held times and kills per presented frame, mixing the sim's latest state (about 90
ms ahead) with the frame's run clock. The live kill counter top right still counts deaths as
frameSong reaches them.
- **Stills**: every overlay panel is composited over the camera in order (`Stills.Overlays`).
  `--debug finale@S` (the sim's `DebugCommand.Finale`) brings the finale and the victory card in a
  short run. Evidence: game/evidence/hud/.

### Audio

The game's sounds sit under the player's music and never fight it.

- **Music**: the delayed music source from
  [The Delay Line and Unity's Output](#the-delay-line-and-unitys-output),
  at unchanged volume, never ducked in play (a calibration screen opened
  from the pause menu ducks it under its clicks, ps-msz.6.11).
- **Sound effects**: the 3D demo's scheduler on a voice pool, with its
  aggregation (at most three voices per beat and category, gain growing with
  the count).
- **Mix**: effects follow the music's loudness through the plugin's follower
  gain, under a tunable limit.
- **Sources**: the vendor packs the 3D demo uses, plus the existing free
  packs; each weapon has its own fire and hit sounds, an evolution shares
  its base weapon's, and an evolving chest's landing plays one `evolve`
  sound (ps-msz.6.8).

**Shipped (ps-msz.3.11).** `Client/Sound/`, with the bank baked from
`game/Content/sounds.json` (`Editor/Sound/SoundBaker`: vendor clips by file
name, free samples by id with their onsets, per-category levels and root
notes, and the mix's `limit`, 0.8). As built
([evidence](../../game/evidence/fx/README.md)):

- `SoundScheduler`: 32 voices (64 since ps-msz.8.6); each sound PlayScheduled at
  `TryDspAt(song - onset / pitch)` so its attack lands on its song time, and
  dropped when the mixer can't make it. Measured in Unity's own mix with the
  sound probe (`game spike --sounds`): 108 of 108 sounds on their sample, none
  dropped.
- `SoundAggregator` makes demo3d's rule streaming: per beat and category the
  first three times asked for take the voices (one instant layers variants),
  and later requests raise the scheduled voices' gain
  (`(0.35 + 0.15 log2 n)(0.7 + 0.3 strongest)`, capped at 1).
- `SoundPresenter` schedules each event as it settles: the swing (heavy, the
  slam) at the impact, hits (heavy, cleave bands), a death's cry under
  demo3d's three-layer crystal shatter pitched by size, a structure break's
  shatter lower; the hurt on the next sixteenth and gem pickups' chimes on the
  sixteenth grid climbing C6 to C8 (`MusicalGrid`, `PentatonicChain`, native's).
- The voices' volume is gain times the limit times the plugin's follower
  gain; the music's source is never touched in play. It plays at priority 0, and the
  project has 64 real voices, so a full pool never makes Unity virtualize
  (silence) the music or a scheduled sound. A dropped sound leaves its
  beat's voice to a later request.

**Shipped (ps-msz.8.6)**: the mix tuned from the full run's drops
([evidence](../../game/evidence/payoff/README.md)). The run summary counts each
category's sounds scheduled, dropped and cut off (still ringing when a later
sound took the voice), and fixed-time runs keep those books (Fixed time), so the
30-minute run measures them in 4 minutes. Before: 3.9% dropped, 32% cut off.

- Dropped: every Garlic pulse and evolution sound (clip lead-ins of 54 and
  94 ms, longer than an event's slack) and every regrowth's shatter (raised on
  the frame that reached it, after the mixer). A clip now starts into its
  lead-in when only that part is late, and the sound presenter shatters a
  regrowth from its event (ps-msz.8.5 found and fixed it the same way). None
  dropped now.
- Cut off: the pool is 64 voices and the project 128 real voices; a full pool
  takes the oldest voice that isn't the hero's own (swings, the slam, the
  hurt), a chest's or an evolution's. 3.6% cut off now, all ice-break,
  thunderclap and hit tails; no swing.
- The aggregation is unchanged: the finale's 290 deaths are three layered
  shatters at full gain.

### Performance targets

The target is an RTX 4060 laptop at 12.5 ms a frame, at native's density.
Cygnus measures it against the 3D demo's pass lines, which the demo set to
stand for that laptop budget, with GPU headroom kept
([performance.md](../tech-demo-3d/performance.md)). Cygnus measures in M5;
until then M4 profiles on the Mac, and its numbers only guide the work.

- On Cygnus at 1080p with MSAA 4x, at the run's peak (300 alive, evolved
  weapons, mass shatter, a structure break): GPU p99 at most 4.2 ms, and
  main and render threads at most 10.4 ms p99.
- The sim's step at 300 alive: at most 1 ms p99 per frame.
- The plugin's analyzer keeps up in real time with its hindsight worker on
  both machines, with capture to arrival near 48 ms.
- No frame over 33 ms during a 30-minute run on Cygnus.
- On the Mac the game plays at 60 fps or more; the Mac's numbers are not the
  performance claim.

**Shipped (ps-msz.8.2): the measurement.** `uv run --project demo3d/tools game perf`
builds the player if needed and runs it windowless at fixed time over the
standard scenarios: the peak (`--start-minute 25` with Bloody Tear, Soul Eater
and Holy Wand evolved, the crowd near 300), a structure break and the finale's
mass kill there, the run's first minute, and the 30-minute File run. It writes
a JSON summary per scenario and exits 1 if a player fails. Two player flags
measure:

- `--gpu-probe-at T1,T2,... --gpu-probe-out FILE.json` (`Client/GpuProbe.cs`,
  demo3d's probe): nine hand-drawn 1080p frames with MSAA 4x at each song time,
  the first a warm-up, each timed through GPU completion. CPU submission and
  GPU in series: an upper bound on a windowed frame. Batch mode with a device
  only (not `--headless`), and only a handful of times per run.
- `--profile-out FILE.csv` (`Client/Loop/FrameProfile.cs`): each frame's main
  thread by part (sim, events, each presenter's Present, the crowd's GPU
  shatter and the structures' physics apart, the loop and Unity's engine), its
  managed allocation and collections. A fixed-time frame's music rendering and
  analysis wait, and the probe's draw, are left out, so the run measured in 4
  minutes reports its frames' own work. IL2CPP lacks the per-thread allocation
  counter, so a native hook counts allocations
  (`Client/Loop/AllocationCounter.c`).

The baseline on jarvis (M5 Max, IL2CPP), in
[game/evidence/perf](../../game/evidence/perf/README.md): at the peak a probe
frame takes 7.0 ms median and 7.7 ms p99, the main thread's update 0.8 ms and
1.6 ms p99, and the sim's step 0.31 ms p99 at 285 or more alive. The full run
has no frame over 33 ms. Drawing the frame is the top cost by far, then
Unity's own player loop, then the crowd.

**Shipped (ps-msz.8.8): the peak on the polished look.** The probe now
splits each frame into its CPU share (the camera's Render call) and the wait
on the GPU, and `game perf --ablate` (`--gpu-probe-ablate`) times each pass
off beside full frames: SSAO, post, bloom, shadows, the opaque texture,
MSAA, the crowd, the effects and their lights. `--gpu-probe-without PASS`
turns passes off in every probe frame, for moments too short for that
cycle. A Metal System Trace showed the GPU busy only 1 to 5 ms of a 6 ms
probe frame at the peak: the CPU's 2.3 ms of submission is the largest cost,
then the crowd's draws (about 1.5 ms with their CPU share) and the main
light's shadows (0.7 ms). SSAO now runs at half resolution: its GPU cost
fell from about 0.5 ms to the noise, the probe's tails shortened (p99 at the
peak 9.9 ms against 14.9, side by side) and no still changed beyond JPEG
noise. On the Mac at the peak: probe p99 9.0 ms, main thread p99 2.9 ms,
main plus render at most 8.6 ms against Cygnus's 10.4, and the sim's step
0.39 ms p99 at 300 alive. The finale's first frames after the blast are the
run's heaviest, 8 to 20 ms in the probe (CPU share 4.5 ms), for M5 on
Cygnus (ps-msz.9.2). Numbers in
[game/evidence/perf](../../game/evidence/perf/README.md#the-runs-peak-at-60-fps-on-the-mac-ps-msz88).

**Shipped (ps-msz.8.9): no frame over 33 ms on the Mac.** Three kinds of
hitch, each found with a new measure in [game/evidence/perf](../../game/evidence/perf/README.md):

- **Garbage.** The frame profile writes an allocation census beside it
  (`profile-alloc.csv`: the main thread's allocations by type and by the part
  of the frame that made them). It named the HUD's per-frame texts, the
  damage numbers' strings and closure, per-frame delegates, the bot's lambdas,
  UI trees rebuilt at each level-up, and the sim's stream lists doubling to
  53 MB of garbage over a run. The texts are made once and set on a change,
  the delegates once, pools and capacities replace the growth, and the run's
  level sizes the sim's storage for 40 minutes of song
  (`SimConfig.HindsightCapacity` and `SparseCapacity` with the others). A full
  run allocates 18 B a frame (from 1125) with 2 collections (from 38); the
  peak 36 B a frame, 480 B at p99, none in most frames.
- **First use.** `--gpu-probe-firsts` probes the first sighting of each kind
  of moment. On a first launch of a build, macOS compiles each shader
  variant's pipeline as it first draws: 3.5 s for the first frame, up to 666
  ms at a first sighting (a zone's change, the first spawn, the first deaths).
  The boot now warms up a traced `GraphicsStateCollection`
  (`Client/GraphicsStates.cs`) per graphics API, from the build's
  StreamingAssets (gitignored, traced per machine by `game perf --trace` in a
  development player, as Unity traces only there): 3.3 to 4.5 s at boot on a
  cold launch. A trace holds no compute, and each compute kernel's first
  dispatch stalled its frame 54 to 164 ms on a cold launch (the first frame,
  spawn, emitter burst and death), so each system hands its kernels to
  `GraphicsStates.Prepare` as it loads (`ComputeShader.IsSupported` builds
  them, 0.4 s). A cold launch then has no first sighting over 27 ms and no
  main-thread frame over 5 ms.
- **Load in the run's frames.** The arena's scene finished loading in the
  frame of the sim's first step (36 to 48 ms of CPU, its static shadow map 23
  ms of it); the loop now steps from the frame after.

The frame profile also reports each frame's main-thread CPU time (`cpu_ms`),
which tells a frame's waits (the machine's other builds, a compiler) from its
own work: no frame's own work went over 8 ms in any take after the fixes. The
graphics states are traced per graphics API: Cygnus needs its own (M5).

**Reviewed (ps-msz.8.10).** `game perf` re-run on the Mac reproduced the
evidence: allocations the same to the byte, timings within the load's spread.
The frame probe made garbage the run's collections counted: a capture every
frame of a probed run, and its JSON rebuilt whole after each point. A probed
scenario's collections now track the run's (the 30-minute run with its 102
first sightings: 3, from 8; the run alone: 2).

## What Carries Over, and What Changes

Both prototypes were built under constraints this game doesn't have. Native
matched an old TypeScript game bit for bit and had to support Direct
listening. The 3D demo plays a pre-computed timeline with no player. This
section records, piece by piece, what the game keeps, changes or drops, and
why.

### From the native game

Kept, re-implemented in C# from native's code and docs:

- The 30-minute run: run clock, the minute-by-minute schedule, freezes,
  scripted offers, chests and evolutions, victory and death.
- The arsenal's numbers: weapon ranks, cooldowns snapped to the beat grid,
  passives, evolution pairs, the six plus six slots.
- Enemy gaits: travel only on main beats, holding with no reliable beat,
  beat-timed contact lunges, immediate knockback.
- The music hooks: zones, swarm unlocks, the set pieces, the signature
  minotaur, the camera's zoom by loudness.
- The causal rule: the sim uses an entry only once it is known.
- Determinism, sessions with replay, and headless bots and sweeps.
- The sound rules: sounds start on their exact sample, never late; voice
  budgets; the music is never ducked in play.
- The fast lane's presentation: beat pulses, accent flashes, drum-band
  envelopes, build-ups before drops.

Changed:

- **Movement**: 8-direction free strides at a fixed average speed become
  4-direction grid steps whose speed follows the tempo, with enemies scaled
  to match.
- **Time**: the rollback runner becomes a small lead plus press strides.
- **Presses**: native's zen mode waits for the next stride step; here a press
  strides at once, as native's classic mode does.
- **Lookahead**: every stream group may use it, not only committed beats.
- **Accents**: heavy strikes come from detected accents as well as accent
  forecasts.
- **Sound output**: native's Rust mixer is replaced by Unity's audio output,
  with the delayed music fed to Unity by the plugin.
- **Content**: YAML read through a TypeScript-compatible schema becomes JSON
  owned by the game, converted once, in metres instead of pixels.
- **Watchdog**: falling back to Direct listening and ending the run becomes
  a music-stopped freeze and a fresh tap.

Dropped:

- Bit-exactness with the TypeScript game: the V8-exact math library, the
  schema loaders and the TypeScript oracle tests.
- Classic mode, where presses are rated and a groove meter rises and drains.
  The default unjudged mode is the only mode.
- Native's older, shorter level (a level file holds a run's schedule and
  settings) and the set pieces only it used.
- Direct listening.
- The older virtual players (the TypeScript port, the masher, the holder).
- The 2D pixel-art asset pipeline.
- World-time warps and hit-stop, already off in native's default mode.

### From the 3D demo

Kept, copied into the game and owned there:

- URP Forward+ settings, MSAA 4x, the night post stack, the palettes.
- The bone-texture crowd renderer and its compute culling and bone-palette
  passes.
- The crystal shaders: monsters, static structures and the hero's blade.
- The compute particle runtime and its effect recipes.
- The beat-driven post effects.
- The sound scheduler and its aggregation rules.
- The arena baker and its single placement list, and the flow-field
  navigation.
- The swap that gives structures the crystal material at run time,
  structure shatter (PhysX), and the shard bakers.
- The Synty hero's playable-graph animation and clip retiming, which makes a
  clip's impact land on a beat.
- The look, filmstrip and image-comparison tools, and the vendor guard.

Changed:

- **Driver**: evaluating a pre-computed timeline becomes consuming live sim
  events.
- **Post effects**: driven by the live stream instead of a song map.
- **Monster shatter**: CPU posing plus a PhysX rigid body per shard becomes a
  GPU simulation.
- **Structure breaks**: a crack front that spreads before shards release
  becomes a break on the impact frame.
- **Structures and lighting**: structures move out of the lightmap bake,
  because they break and regrow during play.
- **Camera**: a baked framing track becomes a live follow spring.
- **Wall breaks**: scripted at fixed song times become gameplay hits.

Dropped:

- The choreographer that pre-computes the whole song, and its timeline;
  the hero's parries; the overdrive mode (a much larger horde); alternative
  monster sets; and the trailer shot system.
- Monster styles other than crystal, and environments other than the dark
  arena. The palettes survive as zone looks.

## Tools, Checks and Evidence

The game's tools join the repository's Python tool package for its Unity
projects, as a new command beside the 3D demo's, the 2D demo's and the
shader style lab's, with `just game` recipes. Agents check everything
headlessly; nothing they run opens a window that takes the operator's focus.

```sh
just game --file public/reactive/house-128/house-128.ogg  # build and play
uv run --project demo3d/tools game check --player         # every check
uv run --project demo3d/tools game look <name>            # windowless stills
uv run --project demo3d/tools game perf                   # frame probe and CPU profile
uv run --project demo3d/tools game sim-test               # the sim, no Unity
```

Checks, by layer:

- **Plugin**: the shared crates' Rust tests, native's analyzer accuracy
  checks, and a plugin build on both platforms.
- **Sim**: .NET tests, including the causality test, determinism hashes, the
  late-step rules, the tempo scaling and hysteresis, and golden runs on
  recorded streams.
- **Unity**: a cold compile with EditMode tests, an IL2CPP player build, and
  a headless player run on a File source (batch mode, no graphics, muted)
  that must finish a short run.
- **Look**: windowless stills checked against the readability floor, and
  frame-exact filmstrips at fixed time.
- **Bots and sweeps**: native's positioning bot, ported to C#, plays
  recorded streams of tracks from the repository's analysis corpus (licensed
  tracks with reference beats) in .NET. Four 30-minute runs must survive
  above 25% health; an idle bot must die before minute 10.
- **Replay**: a recorded session replays to its hash in its runtime (`game replay DIR`, in .NET
  or with `--player` in the player).
- **Performance** (ps-msz.8.2): `game perf` runs the player windowless at fixed time over the
  standard scenarios with its frame probe and CPU frame profile, into a JSON summary
  ([Performance targets](#performance-targets)); its `firsts` scenario probes each first
  sighting, and `--trace` traces the graphics states the boot warms up (ps-msz.8.9).
- **Vendor guard**: the repository's check that blocks committing vendor
  files covers the new project.

The fast checks run before every push and cover the plugin's and the sim's
tests. Slow CI adds the Unity EditMode tests, a short batch-mode File run
and the four .NET sweeps. A machine without the vendor packs or a plugin
build skips the Unity part, as demo2d's section does. The IL2CPP player
build stays out of slow CI, which is budgeted at about 5 minutes; each
executor builds and runs the player in its own checks.
Each implementation task keeps its measurements and stills in an evidence
folder with a short README, and operator-only checks go on the game's
operator-pending list.

**Shipped (ps-msz.6.13).** The bots are `Sim/Bots/SurvivorBot.cs`, driven by `Sim/Bots/BotRun.cs`
(native's `eval::survive::drive`: 60 fps frames, the bot's keys before each step, its report of
survival, lowest health, the level each minute, chests, evolutions and frozen time). A bot reads only
the sim's public state and events, as the client does. The positioning bot keeps native's aim
(escape the enemies within 240 px weighted 1 / (d² + 24²); clear of 90 px, the nearest chest, else
the nearest gem bent 0.35 along the escape; 27 px a metre). What the grid changed:

- **Four directions and a route**: the aim becomes a goal (the chest or gem, or 6 m along the
  escape), and the bot takes the first step of the shortest path to the open tile nearest it within
  16 tiles. A greedy four-way step pinned the hero in a structure's pocket until the crowd killed it.
  A gem off the hero's tile that the route comes no nearer to (inside a structure, past the arena's
  edge) is forgotten and the bot aims again: standing for one, the hero took the signature
  minotaur's slams to death once its health scaled by the hero's level (ps-b3v).
- **On the step**: it steers once a stride step, 80 ms before the next is due, so a turn's press
  takes that step and the held key strides on the rest, never twice on one step.
- **Cards**: Left and Enter one beat after the third card flips in, as native's. The idle bot gives
  no input but the cards' Enter (`AutoCards`), as a headless run does.
- **Slams** (ps-msz.6.14): native's bosses don't telegraph. A telegraphed slam whose blow would reach
  the hero (its radius from the content, plus the hero's hurt radius and 0.25 m) is danger until its
  impact, the escape pointing away from where it lands. The signature minotaur's lands where the
  hero stood, so a bot standing still for any reason (a gem underfoot, or one out of reach before
  ps-b3v) takes every one. Clear of a slam's blow, the route keeps out of it until it lands (ps-6bl).
- **Every telegraph** (ps-6bl): a telegraphed charge whose line passes within its contact of the
  hero is danger until its impact, the escape across the line; a noticed lunge is danger until its
  impact, the escape away from the lunger. The crowd's pull counts every enemy alike, so a far crowd
  walked the hero into a boss's bite. In danger with no step toward the escape (a wall across it),
  the bot steps along the wall, the escape's smaller axis first, as native's hero slides; it had
  stood against the wall and taken every lunge. The sword's half-time pulse (ps-at1) moved the
  sweep's reversed run into those gaps (lowest health 0.14); with them closed, seeds 1 to 4 of all
  four positioning cases win, none below 86% health.
- **In the player** (ps-msz.6.14): `--bot positioning|idle` plays the run with the bot in place of the
  keyboard (`Client/Loop/BotInput.cs`), as `BotRun` drives it; its keys are recorded like a player's.

`game sweep [--assert]` (demo3d/tools botsweep.py, slow CI's game section) runs native's matrix in
parallel in .NET (`game/dotnet/PS.Game.Sweep`): the survivors playlist's original, reversed and
rotated orders over per-track dumps of `ps entries`, and the original order over the whole
playlist's dump, seed 1, plus the idle bot. Each stream is the playlist played twice, since the
run's freezes take 250 to 320 s of song time (up to 640 s before ps-msz.6.14's beatless thaw) and
the reversed order starts 182 s in, needing 2280 s of one pass's 2294. A pass over committed inputs
is stamped by their hash and skipped after. Evidence: game/evidence/sweeps (all four win, none
below 86% health; the idle bot falls at 2:06).

**Shipped (ps-msz.8.5): the on-the-beat audit.** `game beat NAME --file F`
(demo3d/tools beat.py) runs the player windowless at fixed time with
`--moments-out` (Client/Loop/Moments.cs: each presenter marks a key moment on
the frame it first shows it, with its song time) and `--events-out` (every sim
event with the frameSong it settled at), and checks that every moment is on
the first frame at or after its time: the hero's hops setting off and landing,
the blade's flare, footfalls, deaths, breaks and regrowths, every particle
effect as the GPU emits it, the beat gem's ring and the cards' flips; that
each event settled before its moment's frame; and counts them against the
sim's events, failing when a moment shown once per event (hero strides, blade
flares, deaths, breaks, regrowths) is shown fewer times than the sim made it,
or a kind the sim made is never shown (ps-msz.8.13). `--sounds` runs it in real time, headless, with the sound probe:
every sound on its event's song time (but those snapped to the grid by design)
and its anchor's DSP time, each probe click on the music in Unity's mix, and
the lead at output latency plus margin. At the peak (minute 25, three
evolutions, a break, a regrowth, the finale) and over the run's first 87 s,
every one of 10,584 and 5,275 moments is on its frame, and 888 sounds are on
their events with the lead at 90.3 ms. It found moments a frame late from
fixed time's rounding (Fixed Time above), the weapons' and chests' effects a
frame late (the effects now present after the views that ask for them as they
present), the gem's ring landing when the beat locked after the beat (a beat
before the lock no longer lands) and the regrowth's dropped sound. Slow CI
runs a 20 s peak on house-128 (`game beat ci`). Evidence: game/evidence/beat.
Its Warden made breaks and regrowths a frame late and put the effects back
before the views in a scratch build: the audit failed on all 14 breaks, 5
regrowths and 20 effects (ps-msz.8.13).

## Build Order

The order was decided with the user. Foundations come first, then a slice
that already looks and syncs like the final game, then the run's breadth,
then polish. Windows comes last, once everything else has shipped on the
Mac. The game stays playable after every milestone, and no step waits on
the operator. Inside each milestone, independent tracks run in parallel.

1. **M1, foundations**: three parallel tracks that meet in a spike.
   - Plugin: the analyzer's stamp-every-group mode with a headless entries
     dump, then the plugin crate with the File source, the delay line and
     the C interface.
   - Unity: the `game/` project, its `game` tools command and recipes, and
     the vendor guard.
   - Sim: the .NET library's skeleton (entries and causality, ticks, events,
     the hash, beats, bars and the stride grid), and native's content
     converted to JSON.
   - **The spike**: File audio through the plugin into Unity's output on
     the Mac, with a click on every committed beat. It measures
     music-to-click sync from Unity's own final mix, captured in process,
     and the frame-to-heard offset. An acoustic loopback check goes on the
     operator-pending list.
2. **M2, the cinematic slice**, on File audio, in a bounded arena polished
   to the final look. Two tracks run in parallel, then meet in the
   assembled slice:
   - The sim's rules: the grid hero and its presses, enemies, the sword,
     structures.
   - The client's port: its clocks and sim stepping, the arena, the night
     look and camera, the crowd, GPU shatter, the hero, effects and sound,
     and structure shatter.
3. **Mac live capture**, from M2 on, in parallel with the slice and the run:
   native's capture code moved into a shared crate, the plugin's System
   source, and the title screen's source picker.
4. **M3, the run's systems**: the run clock and freezes, the full schedule
   and cast, the music-driven events, the arsenal (three weapon pairs in
   parallel), chests and evolutions, the HUD, sessions and replay,
   calibration, the debug surfaces, the bots and sweeps, and a full File
   run.
5. **M4, polish and performance on the Mac**, planned from what M1 to M3
   measured (ps-msz.8.1). Six tracks start in parallel, each with a Warden:
   - **Measurement**: a windowless GPU frame probe and a per-frame CPU
     profile in the player, a `game perf` command over the peak's
     scenarios and the 30-minute fixed-time run, and the Mac's baseline
     (ps-msz.8.2).
   - **Look**: the five zones' nights and the run's peak polished to the
     reference, with neon's thin readability margin (+19.7 dL*) raised,
     big moments clipping at most 1% and the ordinary strike's ring
     calmed (ps-msz.8.3).
   - **The run's tuning**: levels brought down to `startLevels` (the bot
     reaches 15 at 5:00 and 55 at 30:00, against 10 and 30), and the late
     crowd's rosters mixed so the peak reads rainbow rather than 297
     mushrooms (ps-msz.8.4).
   - **Feel**: an on-the-beat audit of every moment's frame at the peak and
     late steps measured across leads and tempos (ps-msz.8.5); the payoff
     tuned from measurements: the full run's 5% of dropped sounds, the
     finale's blast emitting hits, shatter and structure breaks at the
     peak (ps-msz.8.6).
   - **What M3 left**: presenters isolated from each other's faults, the
     end card from a sim recap, the crowd's capacity from content
     (ps-msz.8.7).
   - Then **performance**, from the baseline: the peak at 60 fps on the
     Mac after the look (ps-msz.8.8), and no frame over 33 ms across the
     30-minute run (allocations, first-use hitches, spikes; ps-msz.8.9).
   - Last, **a closing retake** of the whole run on the combined tree: the
     fixed-time full run and its replay, the frame budget, the peak's
     stills, and the operator rows brought up to date (ps-msz.8.14).

   The Mac's numbers only guide this work; Cygnus measures against the
   targets in M5.

   **Shipped (ps-msz.8.14): what M4 left, retaken together.** On the
   combined tree the 30-minute fixed-time File run with the positioning bot
   wins at 30:00 with no exception or presenter fault, and its session
   replays in the player to its hash. Its levels follow `startLevels`
   (10, 13, 18, 23, 25 and 31 at minutes 5 to 30, against 15 to 55 before
   M4), every boss chest is taken by 72 s after its minute, three
   evolutions arrive, and none of its 46,226 sounds is dropped (5% before).
   No frame goes over 33 ms in the run, nor over 16.7 ms in any `game perf`
   scenario; the peak's probe frame is 5 to 6 ms at the median and the
   sim's step 0.29 ms p99 at 300 alive. The late crowd reads rainbow: four
   to seven gem colours in the busy stills from minute 25, every still
   readable and none clipping past 0.3%. The sweeps and the on-the-beat
   audit pass. Evidence:
   [game/evidence/full-run](../../game/evidence/full-run/README.md#retaken-after-m4-ps-msz814)
   and [perf](../../game/evidence/perf/README.md#retaken-after-m4-ps-msz814).
   The operator's checks wait in operator-pending.md for the Archivist
   (ps-msz.10).
6. **M5, Windows**: the shared crates and the plugin on Windows, process
   loopback with the guardian process, the Windows player, and performance
   against the targets on Cygnus, with Cygnus's own traced graphics states
   for the boot's warm-up (the Mac's trace is Metal's; ps-msz.8.9).

Mason and Sage audits close M1, M2 and M3; M3's audit also covers live
capture.

**Shipped (ps-msz.3.13): the assembled slice.** `GameLoop` plays the slice's
level (`Sim/Level/SliceLevel.cs`) on File audio: the arena's level data, a horde
of five early-run roles in gem colours (bats, slimes, demons, skeletons and
goblins: a roster a second, whole rosters below 220 alive), the starting sword,
and a hero that stands until M3 brings the fall's card. The spawn ring stays out
of the camera's loud view (`CameraRig.Footprint`). A level bake (`LevelBaker`,
BakeAll's "Level") copies the sim's content and arena.json into the player.
`--events FILE` still drives the presenters alone, with no horde or sword.

- **Debug hooks**: F5 forces a section change, F6 an
  accent, F7 breaks every structure within 15 m of the hero (large ones at
  once) and F8 regrows the broken ones. `--debug "change@44,break@47"` gives
  them at song times. They are the sim's `Command(DebugCommand)`, recorded with
  their effective time like keys. M3's F1 menu keeps them
  ([Debug surfaces](#debug-surfaces)).
- **The evidence gate is native's 0.2.** At the sim's earlier 0.5, house-128's
  beat (evidence 0.48 to 0.51) flickered below the gate, and the horde held
  still for most of the song.
- A zone change eases the post's grade to its zone's from its downbeat. The
  arena's palette swap waits for M3's music events.
- A headless player renders no static shadow map, since it has no device.
  `--events-out FILE` writes every sim event as CSV.
- Slow CI's File run is the slice's: 30 s of house-128 with every scheduled
  sound a click, measured from Unity's mix (`game spike --sounds`). Evidence
  and the comparison with the reference look:
  [game/evidence/slice](../../game/evidence/slice/README.md).

## Alternatives Considered

The design interview weighed these and chose otherwise.

- **Mac only.** Simpler audio work, but leaves the performance target, a
  Windows laptop GPU, unmeasured.
- **A public release.** Brings signing, notarization and madmom's licence
  terms into scope before the game exists.
- **Keeping Direct listening.** Needs a rollback runner or delayed stamps,
  and brings back the unmeasurable gap between the tap's and the output's
  latencies.
- **File only to start, or whole-song analysis of files.** The first defers
  the product's defining feature; the second gives files a different
  knowledge regime from live play.
- **A Rust sidecar process.** Crash isolation and free rebuilds, but streams
  audio and entries over shared memory and needs process lifecycle
  management.
- **Porting the analyzer to C#.** Rewrites a validated 7k-line analyzer and
  still needs native capture code per platform.
- **Freezing native.** Rejected: native stays a maintained 2D game.
- **Sharing native's Rust sim, or a new shared Rust sim.** One rule set, but
  every gameplay change crosses Rust, the C interface and C#; grid movement
  and 3D timing would become modes of a shared sim.
- **A rollback runner in C#.** Exact semantics for presses, at the cost of
  snapshot engineering and cancellable sounds; press strides drawn from
  their heard time give the same feel for the hero.
- **Lockstep at heard time.** Simplest, but every sim-triggered sound would
  be one output latency late.
- **One song per run, or a shorter run.** Both move away from native's run;
  the showcase's per-section experience scaling remains available as a level
  option.
- **Enemies on the grid, or on a finer grid.** Readable, but a 300-strong
  horde reads as a checkerboard and needs occupancy rules.
- **An endless streamed world, or an arena per zone.** The first fits
  Vampire Survivors' open plane but gives up composed spaces and baked
  lighting; the second interrupts the run at every section change.
- **Structures broken only by the music, or decorative structures.** Both
  remove the hero's agency over the arena, and the second removes
  structures from navigation.
- **The 3D demo's spells as the arsenal, or a smaller arsenal.** More design
  and fewer of native's tuned numbers, or less of native's run.
- **A model per role, or fewer roles.** More vendor baking, or a departure
  from native's table.
- **Shared Unity packages with the 3D demo, or growing inside its project.**
  The first keeps two diverging projects in step; the second inherits a
  timeline-shaped codebase and its settings churn.
- **All shatter on the GPU, or the 3D demo's PhysX path.** The first loses
  structure shards tumbling over each other; the second costs 12–15 ms a
  frame in the demo's climax and over 100 ms in its worst frames.
- **A mostly diegetic HUD, or native's pixel HUD as is.** Less readable at a
  glance, or clashing with the 3D look.
- **Systems first, or live capture early.** The first delays anything that
  sells the vision; the second front-loads the riskiest audio work.
- **Live capture strictly after the run's systems.** It is Rust work that
  touches nothing in the slice, so it would leave capacity idle.
- **Both platforms from the start.** Windows work waits on Cygnus, and
  doing it early slows everything else down; it comes last instead.
- **The IL2CPP player build in slow CI.** The fullest coverage, but it
  would break slow CI's 5-minute budget on every pass.
- **Fixed enemy speeds, or narrowing the hero's speed range.** The first
  makes fast songs easy and slow songs hard; the second puts steps on less
  obvious subdivisions of the beat.

## Manual QA

Manual QA exercises the assembled game: plugin, sim and client together, on
real audio, on both machines. These checks need the operator (a window and
speakers), so they live on the operator-pending list and never gate work.

### First assembled check

Do this as soon as the slice exists, before any broad validation:

1. Play a corpus track from a file:
   `just game --file public/reactive/house-128/house-128.ogg`.
2. Hold an arrow key. The hero hops one tile per stride step, landing on the
   music.
3. Let the horde arrive. Each sword impact lands on a beat; enemies step on
   main beats and hold between them.
4. Kill a monster. It shatters on the impact frame, with the glass sound on
   the same instant, and no flicker or build-up.
5. Break a structure with a heavy strike on an accent. It shatters at once.
   Force a section change from the debug menu (F5 in the slice; F6 forces an
   accent): it regrows on a downbeat.

Expected: no drift between music and sounds over several minutes, and the
debug overlay's lead stays near the output latency plus the margin (90 ms on
jarvis's built-in output: 38 ms latency plus the 52 ms margin).

### End-to-end runs

- **A full File run**: a 30-minute playlist (native's `survivors-30.m3u`)
  start to victory card. Check level pacing (around level 10 by minute 5),
  boss chests at minutes 10, 15, 20 and 25, evolutions, and the finale.
- **Delayed listening on the Mac**: play music in Music or a browser, pick it
  on the title screen. The app goes silent and its music returns 3 s later
  through the game, in sync with the game's sounds.
- **Delayed listening on Windows**: the same with Spotify or a browser on
  Cygnus.
- **Watch my run**: click its title row (or press W) after a run; the replay
  matches what happened, with its music.

**Measured (ps-msz.6.14).** The full File run, headless in the IL2CPP player
with the positioning bot (`--bot positioning`) on `survivors-30-twice.m3u` (the
playlist twice, since one pass can end first): victory at 30:00 of run clock,
song 2117 s, level 15 at 5:00 and 55 at the end, the minutes 10, 15, 20 and 25
bosses' chests taken, all five evolutions, the cap of 300 alive first reached at 11:00, no error,
and its session replaying to its hash in the player.
Evidence: [game/evidence/full-run](../../game/evidence/full-run/README.md).

### Timing and feel

- **Presses**: tap a direction anywhere between beats. The hero steps on the
  frame the key goes down, never waiting for the next step, and a tap moves
  one tile. Hold a direction and press another: the hero turns at once.
- **Tempo bands**: play a 100 BPM and a 140 BPM track. The hero is visibly
  faster at 140, and enemies keep the same pace relative to the hero.
- **Band boundary**: play a track near 150 BPM. The pace stays steady
  through the song and changes, if at all, only on a downbeat.
- **Beatless passages**: during an ambient intro, enemies hold still, the
  beat track shows its waiting state, and the hero strides on the free
  pulse.
- **Song gaps**: a playlist gap over 5 s freezes the world with "MUSIC
  STOPPED"; it resumes on the second bar after the beat returns.
- **Bluetooth headphones**: switching devices ends the run; on a new run the
  lead grows to cover the latency and sync still holds after calibration.
- **Calibration**: the video and input screens change the offsets and are
  remembered per device.

### Failure states

- **Crash safety, Mac**: kill the game during Delayed listening
  (`kill -9`). The muted app's sound returns at once.
- **Crash safety, Windows**: end the game's process from Task Manager. The
  guardian restores the app's volume. Then kill the guardian and the game
  together; the next launch restores it.
- **Capture stall**: on the Mac, disconnect the output device in use
  (headphones) during a live run: the app's sound returns at once, on the
  new output, and the run ends for the device change. Suspending the source
  app is a song gap, not a stall (see
  [Platform capture and crash safety](#platform-capture-and-crash-safety)):
  the world freezes as music stopped, and resumes after the app plays again
  and the beat relocks.
- **Focus loss, Esc and the pause button**: each pauses; resuming happens on
  the next beat.

### Load and look

- **Mass kill**: at minute 25 with evolved weapons, trigger the finale blast
  from the debug menu. Frame time stays within the targets; every shatter is
  immediate.
- **Look comparison**: at the run's peak, compare a still with the reference
  command's: dark world, rainbow crystal, white blade, readable monsters.

### Debug surfaces

The game provides these so each state above can be reached on demand:

- Launch flags: `--file <audio or .m3u>`, `--shuffle`, `--app <name>`, `--seed`,
  `--start-minute <n>`, `--level <file>` (a level's schedule and arena),
  `--fixed-time`, `--headless`, `--seconds <n>`; and for presenters' checks,
  `--events <file>` (a scripted event feed beside the sim's) and `--overlay`.
  Under `--fixed-time` the frames drive a File or Replay source's music, so a
  run takes minutes rather than its song time, and a File run waits each
  frame until the analysis covers its frontier, so reruns show the same
  frames; `--seconds N` counts song seconds then ([Fixed time](#fixed-time)).
  The run's summary logs its seconds since launch.
  The look flags (`--look <preset>`, `--monster-look`, `--structure-look`,
  `--world`, `--post`, `--vfx`, `--hero-look`, `--palette`, `--motion`, `--beat-ripple`)
  are the look package's ([neon-look.md](neon-look.md), Flags): Boot resolves
  and applies the preset first and logs it (`Look: look edge, palette
  ion; monsters ...`). `--palette` names a look palette or, as before,
  the arena's zone palette.
  A flag or argument the client doesn't read (an unknown flag, one typed
  with an en dash or one hyphen, a flag missing its value, a value after a
  flag that takes none) opens a popup listing each with a note before
  anything starts (`FlagsPopup`; a near flag is suggested), and is logged as
  a warning; windowless launches only log, unless they keep stills, which
  show the popup. Unity's and the OS's one-hyphen
  flags and their values aren't judged.
- An F1 debug menu: jump the run clock, spawn a role or boss, grant a weapon,
  passive or evolution, force a section change, an accent or the finale,
  break or regrow structures, and toggle the hero's invulnerability.
- An overlay: song time, heard time, frontier and lead; the stream's next
  beats with their `known_at`; press strides as they happen; the flow field;
  frame and sim timings. F3 toggles it; stills leave it out unless asked.

**Shipped (ps-msz.6.12).** Every command is the sim's `Command(DebugCommand,
arg)`, recorded with its argument and effective time like a key, so sessions
replay it (`Sim/DebugCommands.cs`). Decisions:

- **The F1 menu** (`Client/Loop/DebugMenu.cs`) is text at the view's top
  right. A row's letter gives its command and Shift with the letter moves the
  row's choice on: J jumps the run clock to the next minute, M spawns a role in
  view, B a boss (due now, landing on the next downbeat with the minute's
  treasure if no boss carried it), G and P grant a rank of a weapon or passive
  as a card would, E grants an evolution (its base raised to the top rank and
  its partner passive added as needed), N starts a music-driven event, C forces
  a section change, X an accent, F the finale, T drops a chest ahead, K breaks
  structures, R regrows them and I toggles invulnerability (hits land for
  nothing). Its letters are none of the hero's keys, so nothing pauses: the run
  plays on with the menu open.
- **`--debug "name[:arg]@seconds,..."`** gives any command at a song time for
  headless runs and stills: jump:MINUTES, spawn:ROLE, boss:ROLE, weapon:ID,
  passive:ID, evolve:ID and invulnerable[:0] beside the plain names.
- **A jump** moves the run clock forward only. The timers that count it and
  would otherwise burst (the weapons' cooldowns, recovery, the gems in flight)
  move with it, and the minutes passed bring no bosses or swarms: the new
  minute's come due.
- **`--start-minute N`** starts the run clock at N:00 through the same jump,
  and builds the hero to progression.json's `startLevels` for that minute
  (levels 1, 10, 15, 19, 23, 27 and 30 at minutes 0 to 30, straight between,
  the design's pacing), each level taking its offer's first card as a run
  nobody plays does. At 25:00 that is level 27 with Sword 8, Hollow Heart,
  Garlic 8 and Pummarola: the minute's boss chest evolves both.
- **`--level PATH`** is a folder of level files (the content files and
  arena.json; game/Content itself works) or one such file, in place of the
  bake's copies, so an edited schedule plays without a rebake. Its arena
  changes the sim's tiles only; the arena drawn is the build's. It and
  `--start-minute` imply `--run`.
- **The overlay** adds the run clock, level and invulnerability, and draws the
  enemies' flow field on the ground within 18 m of the hero: an arrow per
  0.75 m cell, cyan near the hero to magenta at the field's reach, and a red
  cross on blocked cells (`Client/Loop/FlowFieldLines.cs`).
- Fixed in passing: the hero's level never reached the bosses and level-scaled
  roles, whose health took level 1; it now scales by the hero's level.
- Evidence: [game/evidence/debug](../../game/evidence/debug/README.md).
