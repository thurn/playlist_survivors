# Windows Delayed listening spike

Run 2026-10-26/27 on a Windows 11 desktop, natively (no WSL), following
`docs/design/windows-delayed-spike.md`. The spike is `native/spike-windows/` (one binary, `spike`,
with subcommands printing JSON lines); every experiment is a script in
`native/spike-windows/scripts/`, and every number below comes from a file in this directory.
Automated runs use the spike's own click source (`spike source`, run as `clicksource.exe`, see
Notes). **Real-app runs (YouTube in Chrome, by ear) are pending the operator.**

## The machine

| | |
|---|---|
| Windows | 11 Home 25H2, build 26200.9711 (the registry's ProductName still says "Windows 10", a known quirk) |
| CPU | 12th Gen Intel Core i7-12700F (x86_64; target `x86_64-pc-windows-msvc`, Rust 1.98.1) |
| Output | Headset (High Definition Audio Device), the default; 48 kHz float stereo mix (`machine/devices.jsonl`) |
| Unheard output | Speakers (Steam Streaming Speakers), a virtual device; 48 kHz float stereo |
| Microphone | None used. The operator has no microphone, so endpoint loopback of the output stands in for `measure` (below) |

**No microphone.** Q2's "silence at the speakers" and Q5's heard delay are measured on the
endpoint loopback of the output device: the engine's final mix for that device, digitally. It
shows exactly what reaches the device (no room noise), but not the device's own latency after it.
That latency is constant, which Q5 allows for.

## Step 0: the portable crates

`cargo test -p stream -p analysis -p sim -p eval -p players -p audio --no-fail-fast`
(`step0/cargo-test.txt`, classified in `step0/classification.md`): 309 passed, 17 failed, 17
ignored (the existing `release:` tests).

| Failures | Cause | Class |
|---|---|---|
| 15 (eval lib 1, eval `survive.rs` 4, sim lib 10) | `sim/src/content/load.rs:102` recognises `offers.yaml` by `rsplit('/')`; Windows paths use `\`, so the offers file is parsed against the wrong schema (`unknown field "offers"`) | Path separators |
| 1 (sim `jsmath_matches_the_extra_v8_vectors`) | 5 of 9000 `pow` vectors 1 ulp off V8: `jsmath::pow` falls through to the Windows CRT's `pow` | Float (libm) |
| 1 (analysis `pinned::the_phase_arithmetic_is_numpys`) | 7 ulps off numpy after cancellation, from a 1-ulp CRT `sin`/`cos`/`exp` difference | Float (libm) |

No CRLF failures (core.autocrlf false). The fixes were not attempted, per the brief.

## Results

| # | Question | Result | The number | Evidence |
|---|---|---|---|---|
| Q1 | Capture one app by process loopback, without our own output | **Pass** (source); real app pending | Source captured bit-exact: every click fits the ideal at 0.0 dB, residual -155 dB (float precision); 480000/480000 frames, 0 discontinuities, every packet QPC-stamped. Our own 3 kHz tone, playing at -28 dBFS on the endpoint meanwhile, is at -228 dBFS in the capture | `q1/capture-with-tone.jsonl`, `q1/endpoint-tone.json` |
| Q2 A | Session mute | **Fail**: silences the capture too | Capture: no packets with signal (peak -999 dBFS, 0 clicks) | `q2/mute-capture-clicks.json` |
| Q2 B | Tiny session volume, rescaled | **Pass** (source); real app pending | Capture at exactly -60/-80/-100 dB for 1e-3/1e-4/1e-5, residual after the gain fit -148/-149/-153 dB (lossless: the engine mixes in float). The headset gets the -20 dBFS clicks at -80/-100/-120 dBFS peak | `q2/vol-*-capture-clicks.json`, `q2/vol-*-endpoint-level.json` |
| Q2 C | Route the app to an unheard device (Settings) | Pending (needs the operator's clicks in Settings) | | |
| Q2 D | The app's own output device | **Pass** (source rendering to Steam Streaming Speakers) | Process loopback still captures it bit-exact (residual -155 dB); the headset receives nothing (no packets) | `q2/D-*.json` |
| Q3 | Steady delay for 10 minutes | **Pass** (source); real app pending | `M = 3`, route B 1e-5, 600 s: 0 underruns, 0 dropped buffers; fill per-minute median 2957.88 → 2957.73 ms (−0.15 ms over 10 min, spread within a minute ≤ 0.043 ms). Second clock (rendering to Steam Streaming Speakers, 590 s): 0 underruns, per-minute fill medians 2944.96–2945.21 ms (within a minute about ±0.5 ms; one reading 10 ms low in minute 6, a late wake-up of the virtual device, no underrun) | `q3/per-minute.json`, `q3/delay-*.jsonl` |
| Q4 | After `taskkill /F`, does the app come back? Does a guard fix it? | **Fails without a guard; passes with one** (source) | Killed: the source stays muted / at 1e-5, and the state survives the app's relaunch (Windows restores the last level). With `guard`: restored 9.4 ms (A) and 9.5 ms (B) after the spike died; clicks back at -20 dBFS | `q4/summary.json`, `q4/*-guard.json` |
| Q5 | Heard delay against `M` | **Pass** (source) | Headset: delayed click 3001.85 ms after its capture (p05–p95 3001.83–3001.87, n = 1170 over 10 min, drift −0.00005 ms/min); with the capture-to-endpoint offset (20.78 ms) the heard delay is **M + 22.6 ms**, steady. Steam device: M + 11.3 ms, p05–p95 ±0.7 ms, drift −0.004 ms/min | `q3/lag-headset.json`, `q3/lag-steam.json`, `q2/tap-to-endpoint-lag.json`, `q2/D-tap-to-steam-endpoint-lag.json` |

### Q2: fidelity and silence by route

The source's clicks are at -20 dBFS (amplitude 0.1). "Level" is each captured click's
least-squares gain against the ideal click; "residual" is what is left after that fit.

| Route | Capture level | Capture residual | Peak at the headset (endpoint loopback) |
|---|---|---|---|
| None (before) | 0.0 dB | -155 dB | -20 dBFS |
| A: mute | silent | – | silent |
| B: volume 1e-3 | -60.0 dB | -148 dB | -80 dBFS |
| B: volume 1e-4 | -80.0 dB | -149 dB | -100 dBFS |
| B: volume 1e-5 | -100.0 dB | -153 dB | -120 dBFS |
| None (after) | 0.0 dB | -155 dB | -20 dBFS |
| D: the app on Steam Streaming Speakers | 0.0 dB | -155 dB | silent |

- **Process loopback taps after the session volume**, as believed: A silences the capture, B
  scales it exactly. B loses nothing measurable: `x 1e5` restores the source to float precision.
- **B is not silence, only very quiet.** At 1e-5 a full-scale app reaches the device at -100
  dBFS, below a 16-bit DAC's floor (-96 dBFS) but not below a 24-bit one's; whether that is
  audible on real hardware at high gain is for the by-ear check. 1e-5 leaves headroom: the
  residual didn't grow from 1e-3 to 1e-5.
- **D works with no state change at all:** process loopback follows the process to whichever
  endpoint it renders to.

### Q3/Q5: timing

- **Process loopback's QPC stamps are synthetic**: every 480-frame packet is exactly 10.000 ms
  after the previous one. Over Q3's 60002 packets (600 s) the stamps equal the first stamp plus
  frames / 48000 to the 100 ns unit (checked on the capture sidecars, which are regenerated with
  the WAVs). They cannot show the source device's clock drifting from QPC. Endpoint loopback
  stamps are real: over 585 s they wander 0.148 ms from frames / 48000.
- That 0.148 ms in 585 s (about 0.25 ppm) is the headset's clock against nominal, and matches
  the headset fill's creep of −0.15 ms in 10 minutes. The measured heard delay doesn't move: the
  render side maps by the same device clock. Against a second device (Steam's), the per-minute
  fill medians stayed within 0.26 ms over 10 minutes. A device that runs further from nominal would make the fill drift at its ppm
  (50 ppm: 3 ms per minute); the delay line's per-buffer re-anchoring can't see it, since the
  anchors are synthetic. Not measured here: neither device drifted enough to show it.
- **Heard delay is M + 22.6 ms on the headset** (the render latency the spike doesn't subtract),
  constant to ±0.02 ms. macOS measured M + 23 ms. A production version subtracts it, as macOS
  does.

## Verdict (provisional, pending the real-app checks)

Delayed listening looks **feasible on Windows 11**, by:

- **Capture:** process loopback (`INCLUDE_TARGET_PROCESS_TREE`), Windows 11 (build 22000+) only.
- **Silencing:** route B, the app's session volume at 1e-5 and the capture multiplied back by 1e5.
  Lossless in float; leaves the app about -100 dB below its level at the device. Route D (the app
  rendering to an unheard endpoint) is fully silent and changes no state, but needs the app's
  cooperation or the undocumented per-app device API (`IAudioPolicyConfigFactory`, route C).
- **Crash safety needs a guardian process.** Windows persists per-app volume across app restarts,
  so a crash leaves the app at 1e-5 until something restores it. A guard waiting on the app's
  process handle restored it within 10 ms in every kill. Production also needs the saved state
  on disk (as the spike does) for a guard that dies too, or a machine that loses power: restore
  at the next launch. Unlike macOS, there is no OS-level release.
- **The delay line** (`audio::delay`, reused unchanged) holds `M` with no underruns, using QPC
  as host time.

What it would need: the guard, Windows 11 only, and no driver. What the spike did not show:
long runs (hours) against a device far from nominal rate; Bluetooth output; apps that make new
audio sessions mid-run (a browser's new tab would start at full volume); and anything real-app
(pending).

## Notes

- **The source runs as `clicksource.exe`**, a copy of `spike.exe`: Windows keys its remembered
  per-app volume by executable path, so a source left silenced would otherwise silence the
  spike's own output in later runs.
- **A start-up bug, fixed during the run:** `delay` first applied the route after the capture
  started, so the capture's first ~100 ms (unsilenced) were multiplied by 1e5: one clipped click
  3 s into each run, including Q3's (outside the measured window; the per-minute numbers start
  after it). The route now goes on before the capture starts (`fix-check/`: headset peak -20
  dBFS, the click's own level).
- **WAVs are not committed** (Q3's are 230 MB each); they are regenerated by the scripts into
  `%TEMP%\spike-wav`. Everything derived from them is here.
- Process loopback activation: the `PROPVARIANT` holding the activation blob must not be dropped
  (its drop `PropVariantClear`s the blob, which points at the stack: heap corruption).
