# The sync spike (ps-msz.1.6)

File audio through the music plugin into Unity's output, a click on every committed beat, measured
from Unity's own final mix in process ([the design](../../../docs/design/unity-game.md), The Delay
Line and Unity's Output, Clocks, Sound Timing).

## How it was run

Every set is one windowless player run and its measurement, on the Mac (jarvis, the built-in
speakers as the default output, 48 kHz):

```sh
uv run --project demo3d/tools game spike house-128-il2cpp --file public/reactive/house-128/house-128.ogg --repeat 3 --seconds 200 --build
uv run --project demo3d/tools game spike dnb-174-il2cpp --file public/reactive/dnb-174/dnb-174.ogg --repeat 3 --seconds 200
uv run --project demo3d/tools game spike accelerando-il2cpp --file public/reactive/accelerando-100-140/accelerando-100-140.ogg --seconds 80
uv run --project demo3d/tools game spike house-128-fixed-time --file public/reactive/house-128/house-128.ogg --seconds 65 --player-args=--fixed-time
uv run --project demo3d/tools game spike buffer-512 --file public/reactive/house-128/house-128.ogg --seconds 65 --player-args "--dsp-buffer 512"
uv run --project demo3d/tools game spike buffer-1024 --file public/reactive/house-128/house-128.ogg --seconds 65 --player-args "--dsp-buffer 1024"
uv run --project demo3d/tools game spike house-128-mono --file public/reactive/house-128/house-128.ogg --repeat 3 --seconds 200 --backend mono --build
uv run --project demo3d/tools game spike dnb-174-mono --file public/reactive/dnb-174/dnb-174.ogg --repeat 3 --seconds 200 --backend mono --build
```

`game spike` runs the player as
`PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors -batchmode --file <F> --headless --seconds N --spike-out <dir>`.
`--repeat 3` plays the track three times as an M3U playlist, with the playlist's default 2 s gaps,
so each of those runs is a little over three minutes. The player keeps Unity's final mix (a filter
on the listener, ahead of the output) and its clicks, audio buffers and frames in the gitignored
captures/game/spike/<set>/; the numbers are in `<set>.json` here (no audio is committed).

The measurement (demo3d/tools/src/demo3d_tools/spike.py) uses nothing but that mix and the
source:

- **Music to click**: the music is found in the mix by cross-correlating it with the source
  decoded as the plugin decodes it (afconvert at the output rate), every 10 s; what is left once
  it is subtracted is the clicks. A click's offset is where the music is when the click starts
  (its first sample is its loudest), less its beat's song time: positive is late.
- **Click to schedule**: each click's onset against the DSP time it was scheduled for.
- **Beat to onset**: each beat against the music's nearest spectral-flux onset (within 70 ms):
  the analyzer's placement, for context, not the sync.
- **Frame to heard**: each frame's frameSong against the song heard at its light, taking the
  audio thread's buffer times (a centred fit over 2 s) as the DSP clock's truth.

## Results

All at 48 kHz on the Mac's built-in speakers; every set's `ok` is true.

| set | backend | DSP buffer | music | clicks | music to click (ms): median, p5..p95, min..max | drift (ms/min) | stalls, gaps | beat to onset (ms): median, p5..p95 | frame to heard (ms): median, p5..p95 |
|---|---|---|---|---|---|---|---|---|---|
| [house-128-il2cpp](house-128-il2cpp.json) | il2cpp | 256 x 4 | 192 s | 410/410 | +0.000, -0.021..+0.000, -0.021..+0.000 | +0.0016 | 0, 0 | +5.3, -1.3..+12.7 | +0.07, -0.20..+0.36 |
| [house-128-mono](house-128-mono.json) | mono | 256 x 4 | 192 s | 410/410 | +0.000, -0.021..+0.000, -0.021..+0.000 | +0.0017 | 0, 0 | +5.3, -1.3..+12.7 | +0.08, -0.29..+0.38 |
| [dnb-174-il2cpp](dnb-174-il2cpp.json) | il2cpp | 256 x 4 | 192 s | 476/476 | +0.000, -0.021..+0.000, -0.021..+0.000 | +0.0013 | 0, 0 | +8.0, +0.7..+14.0 | +0.09, -0.20..+0.37 |
| [dnb-174-mono](dnb-174-mono.json) | mono | 256 x 4 | 192 s | 476/476 | +0.000, -0.021..+0.000, -0.021..+0.000 | +0.0007 | 0, 0 | +8.0, +0.7..+14.0 | +0.14, -0.14..+0.34 |
| [accelerando-il2cpp](accelerando-il2cpp.json) | il2cpp | 256 x 4 | 76 s | 150/150 | +0.000, -0.021..+0.000, -0.021..+0.000 | +0.0012 | 0, 0 | +7.3, +2.0..+13.3 | +0.03, -0.25..+0.32 |
| [buffer-512](buffer-512.json) | il2cpp | 512 x 4 | 63 s | 134/134 | +0.000, -0.021..+0.000, -0.021..+0.000 | -0.0009 | 0, 0 | +5.3, -1.7..+11.7 | -0.06, -0.55..+0.48 |
| [buffer-1024](buffer-1024.json) | il2cpp | 1024 x 4 | 63 s | 134/134 | +0.000, -0.021..+0.000, -0.021..+0.000 | -0.0053 | 0, 0 | +5.3, -1.7..+11.7 | -0.09, -1.04..+0.89 |
| [house-128-fixed-time](house-128-fixed-time.json) | il2cpp | 256 x 4 | 63 s | 134/134 | +0.000, -0.021..+0.000, -0.021..+0.000 | +0.0020 | 0, 0 | +5.3, -1.7..+11.7 | +0.06, -0.25..+0.42 |

## What it found

- **Clicks land on the music's own sample, with no drift.** In every set, every scheduled click
  starts exactly where the music is at its beat's song time, or one sample (0.021 ms) early
  (PlayScheduled rounding a DSP time to a sample). The fitted drift is a few thousandths of a ms per
  minute, the music's lag in the mix never moves over 18 windows of a 3-minute playlist (so
  nothing stalled, skipped or resampled), and the music arrives at gain 1.0001. The target was a
  millisecond or two.
- **The first run was a buffer off, and that was Unity's.** Before the fix, every click sounded
  exactly one DSP buffer before its beat: 256 frames (−5.333 ms), and −10.667 and −21.333 ms at 512
  and 1024 frames, while each click started exactly at its scheduled DSP time. A source's audio
  callback fills the buffer the mixer plays one buffer later than the DSP time it reads, so the
  music source now passes `dspTime + frames / rate` as each buffer's anchor
  (game/Assets/Game/Client/Music/MusicPlayer.cs). The buffer-512 and buffer-1024 sets show the fix
  holds at every buffer size.
- **frameSong matches the audio thread's clock to within half a millisecond.** Before its fix, the
  frame-to-heard offset was +2.7, +5.5 and +10.5 ms at 256, 512 and 1024 frames: half a buffer
  each. The main thread's DSP clock reads the end of the buffer being mixed, stepping a buffer at a
  time, so the least-squares fit through its readings ran half a buffer ahead. The clock now takes
  each reading half a buffer back. The run with `--fixed-time` measures the same, since frameSong
  reads the real clocks (fixed time only paces the frames' own Time). What this can't measure is
  the light and the air: the display's real latency and the device's beyond what macOS reports
  (16.6 ms on the built-in speakers). Those are the operator's loopback check
  ([operator-pending](../operator-pending.md)).
- **Batch mode keeps Unity's mixer running.** A `-batchmode` player opens no window but runs its
  audio in real time on the default device, so the windowless check is the real audio path, and
  `--headless` silences the output after the in-process capture. No underruns at Unity's 256 x 4
  buffer: no plugin stalls and no gap in the captured DSP clock in any set, on IL2CPP or Mono.
- **Beat to onset** (context): committed beats sit a median 5 to 8 ms after the music's
  spectral-flux onsets, p5 to p95 within about −2 to +14 ms. That spread is the analyzer's beat
  placement and the flux detector's frames, not the sync; native's analysis accuracy checks own it.
- **Output latency**: the clock's 38.0 ms is the device's 16.6 ms (macOS's report) plus Unity's
  4 x 256 frames (21.3 ms). Whether Unity really keeps 4 buffers queued is part of the operator's
  check.

Slow CI runs the same measurement for 15 s on a Mono player (`game spike ci ... --no-evidence`, in
scripts/ci-full's game section), and fails if the clicks' median or 5th to 95th percentile is
more than 2 ms off the music, over 2% of the clicks are missing, frameSong ran backwards, the plugin
stalls or the capture has a gap.
