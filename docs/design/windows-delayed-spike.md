# Windows Delayed Listening Spike

Can Delayed listening work on Windows? This page is the runbook for finding
out on a Windows machine, starting from a clean GitHub clone.

- **Part 1** is for the person at the machine: install the toolchain and start
  a Claude Code session.
- **Part 2** is the brief for that session: build a small spike, run the
  experiments, and write up the answer.

Related reading:

- [native-app.md](native-app.md), "Capture and mute" and "Playback": how the
  macOS app does it.
- [native-app.md](native-app.md), "Beyond This Plan": why Windows needs its own
  design.
- `native/crates/audio/src/delay.rs`: the delay line, which is portable and can
  be reused as is.
- `native/evidence/e2/README.md`: what E2 measured on macOS.

## The Question

On macOS, Delayed listening does three things:

1. **Captures one app's audio** with a Core Audio per-process tap.
2. **Silences that app at the speakers** while the tap exists
   (`CATapMutedWhenTapped`). macOS releases the mute with the tap, so a crash
   can never leave the app muted.
3. **Plays the capture `M` seconds late** (default 3 s) through the output,
   mapping each output frame to the capture by host time.

Windows has a direct equivalent of step 1: WASAPI **process loopback**
(`AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`). Step 3 is ordinary WASAPI
rendering. Step 2 is the open question.

- Process loopback is believed to capture **after** the app's volume-mixer
  level. If so, muting the app in the mixer also silences our capture.
- Windows **remembers** per-app volume and mute. If we change it and then
  crash, the app stays muted, which macOS's design rules out.

So the spike has to answer:

| # | Question | Why it matters |
|---|---|---|
| Q1 | Can we capture one app (Spotify, or a browser playing YouTube) by process loopback, without capturing our own output? | The basic capture |
| Q2 | Can we silence that app at the speakers while still capturing it at full quality? | The crux |
| Q3 | Can we play the capture `M` late with a steady delay (no drift, no underruns) for 10+ minutes? | The delay line on WASAPI clocks |
| Q4 | If the spike is killed (`taskkill /F`), does the app's sound come back? If not, can a guardian process restore it? | Crash safety |
| Q5 | What is the heard delay, measured, against `M`? | Accuracy of the mapping |

## Part 1: Setting Up the Machine

### WSL or native Windows?

**Run everything natively on Windows. Don't use WSL for this.**

- The thing under test is WASAPI, which only exists for Windows processes.
- WSL2 is a Linux VM. Its audio goes through WSLg's PulseAudio bridge, with no
  per-app capture and a different latency path. A test there measures the VM,
  not Windows.
- You could run Claude Code in WSL and call the Windows `cargo.exe` through
  interop, but builds on `/mnt/c` are slow and paths cross a boundary. It buys
  nothing.

Claude Code runs natively on Windows (it uses Git Bash for its shell).

### 1. Check the Windows version

Process loopback needs **Windows 11** (build 22000 or later; technically
Windows 10 build 20348, which only shipped as Server 2022).

- Run `winver`. Windows 10 22H2 (build 19045) is **too old**: stop and note it.

### 2. Install the tools

Open PowerShell (not as administrator unless an installer asks) and run:

```powershell
winget install --id Git.Git -e
winget install --id Microsoft.VisualStudio.2022.BuildTools -e --override "--wait --passive --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
winget install --id Rustlang.Rustup -e
winget install --id GitHub.cli -e
```

- The Build Tools install (MSVC linker and Windows SDK) takes 5–15 minutes.
- `GitHub.cli` is optional. It is only for pushing the results back.
- Close and reopen PowerShell afterwards, so `git`, `cargo` and `gh` are on
  `PATH`.

Then install Claude Code (see Anthropic's setup docs if this changes):

```powershell
irm https://claude.ai/install.ps1 | iex
```

### 3. Clone the repository

Set Git's line endings **before** cloning. Git for Windows defaults to CRLF
checkouts, which breaks the byte-exact JSON fixtures the tests compare
against.

```powershell
git config --global core.autocrlf false
git config --global core.longpaths true
mkdir C:\src
cd C:\src
git clone https://github.com/thurn/playlist_survivors.git
cd playlist_survivors
```

- The repository is public, so cloning needs no sign-in. It is about 500 MB.
- Keep the path short (`C:\src\...`). Deep paths under `native/target/` can hit
  Windows' path limits.

### 4. Hardware to have ready

- **Speakers or headphones** on the default output.
- **A microphone** (a laptop's built-in one is fine) for the measured checks
  (Q2's residual, Q5). Without one, the session falls back to listening by ear.
- **Spotify desktop** or **Chrome/Edge**, to test a real app.
- **Optional, for one of Q2's routes:** a second output device that nothing
  hears (a monitor's HDMI audio with its speakers off, or a USB headset not
  worn), or the free [VB-CABLE](https://vb-audio.com/Cable/) virtual device.
  Installing VB-CABLE is a driver install, so do it only if the session asks
  for it.

### 5. Start the session

```powershell
cd C:\src\playlist_survivors
claude
```

Then give it this prompt:

> Read docs/design/windows-delayed-spike.md and carry out Part 2. Ignore
> AGENTS.md's Tollgate, Hive and beads workflow: none of it exists on this
> machine. Ask me whenever you need me to play music, listen, or click
> something.

Expect it to ask you to start music, confirm what you hear, and stay quiet
during the microphone checks.

## Part 2: The Brief for the Windows Session

You are on a clean Windows 11 machine with Rust (MSVC), Git and the repository
at `C:\src\playlist_survivors`. There is no Tollgate, Hive, `uv` or Node
here; ignore those parts of AGENTS.md. The person at the machine can play
music, listen and click things for you; ask them when you need it.

### Ground rules

- **Never leave an app muted.** Before any experiment that changes an app's
  volume or mute, note its current level. Restore it afterwards, including
  after a crash, and tell the person if you could not.
- **Stay native.** Build for `x86_64-pc-windows-msvc` (or `aarch64-pc-windows-msvc`
  on an ARM machine). No WSL.
- **Keep raw outputs.** Every number in the write-up comes from a file under
  `native/evidence/windows-spike/`.

### Step 0: Do the portable crates build?

A Windows Delayed listening would also need the analyzer, so check this first:

```bash
cd native
cargo test -p stream -p analysis -p sim -p eval -p players -p audio
```

- `rust-toolchain.toml` pins the compiler; rustup installs it on first use.
- The `app` crate (objc2, Core Audio, AppKit) and `cli` (`libc::clock_gettime`,
  `/usr/bin/afconvert`) are macOS-only today. Don't try to build them.
- Record which tests pass and fail, with the output, in the evidence. Don't fix
  failures; just classify them (CRLF, path separators, float differences,
  something else).

### Step 1: Create the spike crate

Create `native/spike-windows/` as its **own** Cargo project, outside the native
workspace:

```toml
[package]
name = "spike-windows"
version = "0.0.0"
edition = "2024"
publish = false

# Its own workspace, so cargo doesn't treat it as a member of native/'s.
[workspace]

[dependencies]
audio = { path = "../crates/audio" }   # the delay line, reused unchanged
windows = { version = "…", features = [
    "Win32_Foundation",
    "Win32_Media_Audio",
    "Win32_Media_KernelStreaming",
    "Win32_Media_Multimedia",
    "Win32_System_Com",
    "Win32_System_Com_StructuredStorage",
    "Win32_System_Threading",
    "Win32_System_Performance",
    "Win32_System_Variant",
    "Win32_System_ProcessStatus",
] }
```

- Use the current `windows` crate version, and add features as the compiler
  asks.
- One binary with subcommands, like the macOS spike was (`spike tap`,
  `spike loopback`, ...).

**Reusing `audio::delay`.** `delay_line(rate, delay, keep)` returns a
`Writer` (capture side) and a `DelayLine` (render side).

- The capture thread calls `writer.write(frame, frames, host, fill)` with each
  buffer's first frame index, its length, and its capture time in seconds.
- The render thread calls `line.pull()`, then `line.at(t)` for each output
  frame's time `t`, and gets `Read::Frame(l, r)`, `Before` (silence) or
  `After` (an underrun).
- "Host time" on Windows is QPC time in seconds. `IAudioCaptureClient::GetBuffer`
  reports each buffer's QPC position (`pu64QPCPosition`, 100 ns units), and
  `IAudioClock::GetPosition` gives the render side's position with its QPC
  time. If process loopback reports a zero or unusable QPC position, say so and
  fall back to `QueryPerformanceCounter` at the event.

### Step 2: The subcommands

Build these, roughly in order. Each one prints JSON lines so its output can
be saved as evidence.

| Subcommand | What it does |
|---|---|
| `apps` | Lists processes with audio sessions on the default render endpoint (`IAudioSessionManager2`, `IAudioSessionControl2::GetProcessId`), with each session's volume and mute |
| `source` | A child process that plays a click track (a 1 kHz, 10 ms click every 500 ms, generated in code) through WASAPI. It is the controllable "app" for automated tests, like E2's `--tap-child` |
| `capture <pid> <seconds>` | Process loopback in `INCLUDE_TARGET_PROCESS_TREE` mode on the pid, 48 kHz float stereo; writes a WAV and prints the RMS level and the QPC positions once a second |
| `mute <pid> <mode>` | Applies one silencing route (below) to the pid's sessions, recording the previous state to a file so `restore` can undo it |
| `restore` | Undoes whatever `mute` recorded |
| `delay <pid> <M> <seconds>` | Capture, silence, and play `M` late through the default endpoint, via `audio::delay`; prints the line's fill and underruns once a second |
| `guard <parent-pid>` | A separate process that waits on the parent's process handle and runs `restore` when it exits for any reason |
| `measure <seconds>` | Records the default microphone to a WAV, for the click-track checks |

Browsers render audio in a child process, so pass the browser's **main** pid
with the include-tree mode. Check that `capture` on Chrome or Edge picks up a
YouTube tab.

### Step 3: The experiments

Run each against the `source` child first (automated, measurable), then
against a real app (Spotify, or YouTube in a browser) with the person
listening.

**Q1, capture.** `capture` the source and a real app for 30 s each.

- Pass: the WAV holds the music; the RMS is non-zero; the spike's own sounds
  are absent.

**Q2, silencing.** Try each route in turn. For each one, record two things:

- **Capture fidelity:** compare the capture under the route with an unsilenced
  capture of the same source (the clicks line up, so subtract and report the
  residual in dB, or at least the RMS ratio).
- **Silence at the speakers:** with `measure` running, how loud the undelayed
  clicks are at the microphone, against the room's noise floor. Then ask the
  person whether they hear the real app at all.

The routes:

| Route | How | What we expect |
|---|---|---|
| A. Session mute | `ISimpleAudioVolume::SetMute(TRUE)` on the app's sessions | Probably silences the capture too. Confirm or refute |
| B. Tiny session volume | `SetMasterVolume(1e-5)` (about -100 dB) and multiply the float capture back by 1e5 | The engine mixes in float, so this may lose nothing audible; it may also be quantized somewhere. Measure it. Try 1e-3 and 1e-4 too |
| C. Route the app elsewhere | The person sets the app's output device to an unheard device (Settings, System, Sound, Volume mixer, the app, Output device); capture by process loopback, and also by plain endpoint loopback of that device | Silent at the speakers. Does process loopback still capture a process rendering to another endpoint? |
| D. App's own output setting | Spotify's own output-device setting, pointed at the unheard device | Like C, for one app only |

Route C's per-app device can also be set programmatically through the
undocumented `IAudioPolicyConfigFactory` (as EarTrumpet does). Note it as an
option; don't build it unless C is the only route that works.

**Q3, the delay line.** With the best route from Q2, run `delay` at `M = 3`
for 10 minutes on the source, then on a real app.

- Pass: no underruns after the first `M` seconds; the fill holds within a few
  ms of `M` (report its per-minute median and spread, as ES did on macOS).
- Also try a second output device (USB or Bluetooth headphones) if there is
  one: its clock drifts against the capture's, and the per-buffer re-anchoring
  should follow it.

**Q4, crash safety.** For each route that changes state (A, B, C):

1. Silence the source with the spike, then `taskkill /F` the spike.
2. Is the source audible again? (Expect not, since Windows persists per-app
   volume.) Check `apps` for the session's state.
3. Repeat with `guard` running. Does it restore the state within about 1 s?
4. Kill the source app too, relaunch it, and check its volume came back
   right (Windows restores a relaunched app's last level).

**Q5, the heard delay.** With `measure` recording and `delay` running on the
source at `M = 3`, find the delayed clicks in the microphone recording and
report the heard delay's median and spread against `M`. A constant offset is
fine (macOS measured `M + 23 ms` before subtracting it); drift is not.

### Step 4: The write-up

Write `native/evidence/windows-spike/README.md`:

- **The machine:** Windows build (`winver`), CPU, output and microphone
  devices, sample rates.
- **Step 0's test results,** classified.
- **A results table** for Q1–Q5: pass, fail or partial, the number behind it,
  and the evidence file.
- **The verdict:** is Delayed listening feasible on Windows, and by which
  route? What would a production version need (a guardian process, a driver
  install, an undocumented API, Windows 11 only)? What did the spike not show?
- Keep raw outputs (JSON lines, short WAV excerpts under a few MB, the commands
  run) beside the README.

Use the style of the other evidence READMEs (`native/evidence/e2/README.md`):
short sections, tables, every claim tied to a file.

### Step 5: Hand the results back

Commit the spike and the evidence on a branch, using Conventional Commits:

```bash
git switch -c spike/windows-delayed
git add native/spike-windows native/evidence/windows-spike
git commit -m "feat(spike): measure Delayed listening on Windows"
```

- If the person has signed in to GitHub (`gh auth login`), push the branch:
  `git push -u origin spike/windows-delayed`. Never push to `master`.
- Otherwise, tell them to copy `native/spike-windows/` and
  `native/evidence/windows-spike/` to the Mac.
- Don't commit `native/spike-windows/target/`, and keep WAVs small.

On the Mac, the branch is then landed through Tollgate like any other change,
and the verdict linked from native-app.md's "Beyond This Plan".
