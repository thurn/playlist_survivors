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
  per **stride step** while an arrow key is held. A stride step is a beat, or
  a half or quarter of one for slower songs (see the
  [Key Presses and Late Steps](#key-presses-and-late-steps) section below).
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
6. **No rollback: a small lead plus late steps.** The sim runs a few tens of
   milliseconds ahead of what the player hears on speakers (more on
   Bluetooth headphones). A key press that misses a
   stride step the sim has already simulated is applied as a late step. See
   the [Key Presses and Late Steps](#key-presses-and-late-steps) section.
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
  line sets the silence between tracks). The plugin decodes it and feeds
  the same delay line and analyzer. The analyzer may read the file faster
  than real time, up to `M` ahead, so playback starts at once instead of
  after 3 s of silence.
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

### The C interface

The interface is small and plain C, so both IL2CPP and Mono call it through
ordinary P/Invoke.

```c
typedef struct { int32_t kind; double t, known_at; double v[12]; } ps_entry;

int32_t ps_open(const ps_config *config, ps_session **out); // source, M, rate
int32_t ps_list_apps(ps_app *out, int32_t max);             // apps playing now
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
  records which song sample starts that buffer.
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
  M3U), the models' path (null: `$PS_MODELS`, else the checkout's) and the
  session folder (null: no record).
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
- `ps_list_apps` returns no apps, and the System and Replay sources return
  `PS_ERR_UNSUPPORTED`, until they land.

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
  tap or restores the volume at once.
- **After the watchdog fires**, the world freezes as "music stopped". The
  plugin taps the app again once it produces audio, and the run resumes as
  any music-stopped freeze does (see [The Run](#the-run)). The run ends
  only if the player ends it.
- **Permissions**: on macOS the player app carries an audio-capture usage
  description and an ad hoc signature, so the OS remembers the player's
  grant. Running System capture inside the Unity editor needs the editor's
  own grant; File works in the editor without one.
- **Device change**: an output device change ends the run, because the
  latency changes, as in native.

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
  (150–300 ms of output latency) it grows to about 0.2–0.35 s, and more
  presses become late steps. `B` (0.5 s) stays larger than the lead in both
  cases.
- Nothing the sim has settled is ever revised. Events at or before the
  frontier are final.
- The presentation shows the sim's recorded state at `frameSong`, which is
  normally behind the frontier, so it interpolates recorded history. If
  display latency ever exceeds the lead, the frame shows the frontier's
  state instead, and the debug overlay flags it (see
  [Debug Surfaces](#debug-surfaces)).

### Beats, bars and reliability

Several rules depend on whether the beat can be trusted, and on bars. Both
keep native's definitions.

- A **reliable beat** is a committed beat whose evidence passes the level's
  gate, while the music is clear: the latest tick is at most 0.25 s old and
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

### Key presses and late steps

The hero moves on stride steps. A stride step is a time on native's
**stride grid**: the committed beats, each beat halved until the period is
at most 0.4 s (see the
[Speeds Follow the Tempo](#speeds-follow-the-tempo) section for how the
halving is chosen). When no beat is known, the grid is a free 0.35 s pulse.

A press waits for the next stride step, as in native. Because the sim runs
ahead, a press made right on a step reaches the sim after it has already
simulated that step. A **late step** applies such a press anyway, starting
the stride at once.

```text
press (direction d, heard time h), processed at the sim's current time F:
  P = stride period at h;  g = min(0.10, 0.4 * P)
  s = the stride step in [h - g, h] if any, else the first step after h
  if s >= F:                 queue d for step s          // normal
  else if no stride at s:    stride now (late), from the hero's current tile
  else:                      queue d for the first step after F
the session records (h, F); a replay applies the press at F
F is the previous frame's frontier: steps before F are already simulated
```

A worked example at 120 BPM, with stride steps at 10.00 and 10.25:

```text
press heard at h = 10.02; it reaches the sim when it stands at F = 10.08
g = min(0.10, 0.4 * 0.25) = 0.10, so s = 10.00 (within [9.92, 10.02])
s < F and the hero didn't stride at 10.00  ->  a late stride starts at 10.08
a press heard at 10.15 instead takes s = 10.25 (normal, queued)
```

- A late stride takes native's 110 ms, compressed to end before the next
  stride step if needed.
- Nothing else in the sim reads the hero's tile between steps except
  pickups and contact, which react to the late position naturally. The sword
  strike on the missed step already fired from the old tile.
- Held keys stride on every step and never need late steps. The
  last-pressed held direction wins.
- Because a held key is judged when the sim reaches the step, a late tap
  still down then strides again on it. At 128 BPM with a 60 ms lead, a
  120 ms tap heard more than 54 ms after its step moves two tiles, though
  it was released before the next step was heard. Judging that first
  repeat in heard time would need a late step of its own, so it waits on
  the operator's feel check (game/evidence/operator-pending.md).
- A press made while the world is frozen never strides after it, as in
  native.
- Space stomps (stands still on the step), as in native.
- Keys come from Unity's Input System with their event timestamps, mapped by
  `keySong`.

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
time. It is never started late; a sound that can't make its time is dropped.

- **Impact sounds** (hits, shatters, swings) play at the impact's song time.
  The lead guarantees they are known in time.
- **Off-grid sounds** keep native's musical snapping: a hurt sound on the
  next sixteenth, gem chimes on the grid, climbing a pentatonic scale.
- **Stream-driven sounds and presentation**, such as drop build-ups, read the
  stream directly and may anticipate by up to the lookahead.

### Calibration

Audio-to-audio sync needs no calibration, because the music and the sound
effects share one output. Two offsets remain, each with a screen like
native's:

- **Video calibration**: the player aligns a flash with a click. It corrects
  the display and reported device latencies in `frameSong`.
- **Input calibration**: the player taps along to clicks. It corrects
  `keySong`.
- Both are stored per output device.

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
sim.StepTo(double songTime);
ReadOnlySpan<SimEvent> sim.EventsSince(int cursor);
sim.State                       // hero, enemies, items, run clock
sim.Stream, sim.Beats, sim.Strides // what the sim knows: reads only
```

- **Reads never change the run.** `Add`, `AddKnown`, `Press`, `Release`,
  `StepTo` and `DiscardEventsBefore` are the only mutators. Everything else
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
  attack (wind-up start, impact time, direction, kind, heavy, reach), enemy
  step (from, to, move start, footplant), enemy knock (from, to, start, end),
  hit, death (impact direction and force), cleave (centre, radii, band
  times), drop, structure break and regrowth, spawn (a recycled enemy's
  re-entry flagged), pickup, level-up, chest, freeze, sound cue.
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
- **Watch my run** (W after a run) replays the session in the player with its
  recorded music.
- A headless replay in the same runtime must reproduce the hash.

## Gameplay Rules

This section specifies what the sim does. It starts from native's run as it
plays today (survivors-run.md, revised by feel-restoration.md) and changes
only what the grid, the 3D presentation and the lookahead require.

### The run

The run is native's 30-minute run, built on a **run clock** that counts
playing time.

- **Start**: Enter or Space starts a run on the next beat; headless runs start
  after 2 s of clear beat, as in native.
- **Run clock**: advances with song time except while the world is frozen. It
  drives the schedule, chest tiers (how many rewards a chest holds) and
  victory.
- **Freezes**: a level-up, a chest, a pause (Esc or focus loss) and the music
  stopping each freeze the world. Song time continues; enemies and the hero
  bob in place on the beat.
- **Music stopped**: 5 s below the analyzer's silence threshold, a confirmed
  song end, or the watchdog firing freezes the world with a "MUSIC STOPPED"
  banner. It resumes on the second bar downbeat after the beat locks again.
- **Ends**: victory at 30:00 (the finale blast clears the screen on the next
  downbeat, then the victory card); death ("YOU FELL"); Backspace ends a run;
  an output device change ends it.
- **Easy**: enemy contact damage at half, and floor chickens that heal, as in
  native.

### Hero movement on the grid

The hero occupies one tile of a square grid aligned with the arena, and moves
one tile per stride step in one of four directions.

- Stride steps and presses follow the
  [Key Presses and Late Steps](#key-presses-and-late-steps) section.
- A stride takes 110 ms from its step's time, with a small hop.
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

- It strikes on every main beat. Each point of Amount adds a strike on a
  subdivision (one extra strike means eighth notes).
- Damage is a full circle around the hero, with a reach of about 7 m times
  Area. The swing's direction and arc are presentation only.
- The visible strike must show that radius: the aimed white slash plus a
  crystal wave that rings out to the reach in every direction, not only the
  blade's sweep.
- Its per-hit power comes from native's rank table times Might, with no
  tempo weakening.
- **Heavy strikes**: a beat with a detected accent (a strong onset within
  50 ms of the beat, visible ahead thanks to the lookahead) or an accent
  forecast deals 2.5x damage plus an outward cleave wave. A forecast and
  its onset never make two heavy strikes.
- **Direction**: chosen at wind-up from the enemies in reach, weighted toward
  crowds and bosses, nearest on ties; with nobody in reach, the hero keeps
  facing.
- **Presentation**: alternating left and right slashes on ordinary beats,
  quick follow-ups on subdivisions, a spin on heavy strikes, and a slam on
  the first downbeat after a section change.
- Attacks play on the upper body over the stepping legs, so moving never
  erases the strike pose.

**Shipped (ps-msz.3.3).** The sim's sword is `Sim/Weapons/Sword.cs`, turned
on by `SimConfig.Sword` (weapon, rank, stats, wind-up). As built:

- `n = max(1, floor(rank amount + Amount))` strikes per committed beat, at
  `T + k * p / n`, p the interval to the next beat when it is known at
  planning, else the beat's period. With no beat known ahead, the same pulse
  repeats every rank interval times Cooldown (1.35 s at rank 1), counted
  from the latest pulse, unless a revealed beat comes within half an
  interval.
- A beat's strikes are planned at `T - 0.32 s`, or as soon as the beat is
  revealed; one revealed at or after its time is missed. Each winds up at
  `max(impact - 0.32, the previous impact)`: the attack event's time is its
  wind-up start, always before its impact.
- At the wind-up: heavy (a main strike, the music clear, a revealed strong
  onset or forecast within 50 ms of the beat, decided once per beat), the
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
- The slam goes to the first downbeat at or after a revealed structure
  change, within two beats.
- Hero health (`HeroHealth`): a landed lunge takes
  `max(1, power * (0.5 on Easy) - armor)`, then 0.12 s of lockout (hits in
  it show with 0 damage). At none the hero falls (a death with enemy -1);
  it then neither moves, strikes nor takes hits. `SimConfig.HeroStands`
  keeps a tenth of its health instead.
- Bosses are recognised by role id (`boss-*`, the minotaur) until M3 marks
  bosses at spawn.

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

### Music-driven events

The clock decides who spawns; the music decides when things happen and how
they look. These hooks are native's:

- **Zones**: a detected section change (a structure change, or a loudness
  jump of 6 dB or more) at least 8 s after the previous zone change moves to
  the next of five zones, cycling back to the first after the fifth.
  - It lands on the first downbeat at or after the change's `t` if that is
    known in time, otherwise on the first downbeat after its `known_at`.
  - **Fallback**: with no detected change, the zone changes 60 s after the
    previous zone change during the first 5 minutes, then 4 minutes after.
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

### Pickups, level-ups and chests

Pickups and progression are native's.

- Gems drop on death and climb a pentatonic chime when collected; the pickup
  radius is native's base radius times the Magnet passive.
- Experience follows native's curve.
- **Level-up**: the world freezes, and three cards from the scripted offer
  table flip in on beats; arrows choose and Space or Enter confirms. Keys
  held when the cards open are ignored until released. Play resumes on the
  next beat.
- **Chests**: dropped by scheduled bosses, collected only when the hero is
  clear of the crowd. The roulette ticks on eighth notes and lands on a
  downbeat. Chests may evolve weapons at minutes 10, 15, 20 and 25.

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
- **Zones** recolour runtime lighting with the 3D demo's palettes (neon,
  crypt, lava, blossom, snow), eased over two bars from the change's
  downbeat.

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

### The crowd

The crowd is the 3D demo's GPU-driven renderer, fed by the sim instead of a
timeline.

- Clips are baked to bone textures; a compute pass blends each visible
  monster's bones once; compute culling picks LODs; indirect draws go out per
  type, LOD and submesh.
- Each instance's clip and phase come from the sim's events: idle bob, the
  step clip timed to its footplant, the lunge, the hit flash.
- The crystal style shader draws monsters, refracting the scene behind them
  and tinted by their type's colour, with beat-driven facet sparkle.
- Transparent crystal lacks depth and motion vectors, so the game keeps MSAA
  rather than TAA. A depth-only pass for crystal monsters, so SSAO and fog
  see them, is the executor's call by measurement.

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

**Structure shards keep PhysX.**

- Structure breaks are far fewer and larger, and shards tumbling over each
  other sell them, so they use the 3D demo's baked structure shards and rigid
  bodies in a separate local physics scene at 1/120 s.
- Shards linger about 2.4 s and then sink.

### Weapon and ability effects

Effects use the 3D demo's compute particle runtime and its recipes, extended
for each weapon's look in [The Other Weapons](#the-other-weapons).

- The sword's slashes are white; abilities carry their own colours, white at
  the core.
- Heavy strikes oversize the slash and send the cleave wave out to the reach.
- Effects are spawned by sim events at their song times, never by the
  client's guesses.

### Post-processing and camera

The post stack is the 3D demo's night look, driven by the live stream instead
of the demo's **song map** (a hand-made file of a song's beats and sections).

- **Night look**: bloom, tonemapping, a colour lookup table built in code,
  vignette, white balance, chromatic aberration (zero at rest) and SSAO.
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
  shows each at `max(t, known_at)`. The beat pulses at full on reliable
  beats and at 0.6 on beats known ahead but below the gate (house-128's
  evidence hovers at the gate).
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

### Audio

The game's sounds sit under the player's music and never fight it.

- **Music**: the delayed music source from
  [The Delay Line and Unity's Output](#the-delay-line-and-unitys-output),
  at unchanged volume, never ducked.
- **Sound effects**: the 3D demo's scheduler on a voice pool, with its
  aggregation (at most three voices per beat and category, gain growing with
  the count).
- **Mix**: effects follow the music's loudness through the plugin's follower
  gain, under a tunable limit.
- **Sources**: the vendor packs the 3D demo uses, plus the existing free
  packs; each weapon has fire, hit and evolved sounds.

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
  budgets; the music is never ducked.
- The fast lane's presentation: beat pulses, accent flashes, drum-band
  envelopes, build-ups before drops.

Changed:

- **Movement**: 8-direction free strides at a fixed average speed become
  4-direction grid steps whose speed follows the tempo, with enemies scaled
  to match.
- **Time**: the rollback runner becomes a small lead plus late steps.
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
- **Replay**: a recorded session replays to its hash in its runtime.
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
   - The sim's rules: the grid hero and late steps, enemies, the sword,
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
   measured.
6. **M5, Windows**: the shared crates and the plugin on Windows, process
   loopback with the guardian process, the Windows player, and performance
   against the targets on Cygnus.

Mason and Sage audits close M1, M2 and M3; M3's audit also covers live
capture.

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
  snapshot engineering and cancellable sounds; late steps give nearly the
  same feel.
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
   Force a section change from the debug menu: it regrows on a downbeat.

Expected: no drift between music and sounds over several minutes, and the
debug overlay's lead stays near 60 ms on built-in speakers.

### End-to-end runs

- **A full File run**: a 30-minute playlist (native's `survivors-30.m3u`)
  start to victory card. Check level pacing (around level 10 by minute 5),
  boss chests at minutes 10, 15, 20 and 25, evolutions, and the finale.
- **Delayed listening on the Mac**: play music in Music or a browser, pick it
  on the title screen. The app goes silent and its music returns 3 s later
  through the game, in sync with the game's sounds.
- **Delayed listening on Windows**: the same with Spotify or a browser on
  Cygnus.
- **Watch my run**: press W after a run; the replay matches what happened,
  with its music.

### Timing and feel

- **Late presses**: tap a direction just after a beat. The hero steps at
  once (a late step) instead of waiting a whole step.
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
- **Capture stall**: suspend the source app. Its sound is restored, the
  world freezes as music stopped, and the run resumes after the app plays
  again and the beat relocks.
- **Focus loss and Esc**: both pause; resuming happens on the next beat.

### Load and look

- **Mass kill**: at minute 25 with evolved weapons, trigger the finale blast
  from the debug menu. Frame time stays within the targets; every shatter is
  immediate.
- **Look comparison**: at the run's peak, compare a still with the reference
  command's: dark world, rainbow crystal, white blade, readable monsters.

### Debug surfaces

The game provides these so each state above can be reached on demand:

- Launch flags: `--file <audio or .m3u>`, `--app <name>`, `--seed`,
  `--start-minute <n>`, `--level <file>` (a level's schedule and arena),
  `--fixed-time`, `--headless`, `--seconds <n>`; and for presenters' checks,
  `--events <file>` (a scripted event feed beside the sim's) and `--overlay`.
  Under `--fixed-time` a File run waits each frame until the analysis covers
  its frontier, so reruns show the same frames.
- An F1 debug menu: jump the run clock, spawn a role or boss, grant a weapon,
  passive or evolution, force a section change, an accent or the finale,
  break or regrow structures, and toggle the hero's invulnerability.
- An overlay: song time, heard time, frontier and lead; the stream's next
  beats with their `known_at`; late steps as they happen; the flow field;
  frame and sim timings. F3 toggles it; stills leave it out unless asked.
