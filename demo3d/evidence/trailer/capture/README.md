# Trailer: the capture (ps-hgs.4)

`demo3d trailer --out FILE.mp4 [-- player flags]` makes the finished, mixed
trailer video in one command
([trailer.md, Capture](../../../../docs/tech-demo-3d/trailer.md#capture)):

1. the windowed player captures the picture at 2× and writes the cue list;
2. `demo3d mix` mixes the song window under the cues, with the trailer's cut
   and end fades;
3. the mix is checked.

The capture itself needs a window, so the operator records it (qa.md).
Everything here is windowless, from the IL2CPP player on the Mac (Apple M5
Max, Metal), built at staging `27301ca0` plus this bead. `--stand-in` runs
every step but the window: a batch-mode player dumps the same cues, and a
black video of the capture's length stands in for the picture.

**Verdict:**

- **No clipping.** The default mix peaks at 1.34 before the limiter and
  exactly its 0.95 ceiling after.
- **Cut fades.** Every cut fades the sounds ringing into it to silence within
  60 ms.
- **End fade.** From the cut to black, the music and the sounds fall together
  to silence over 1.5 s.
- **2× capture.** The capture draws 3840×2160 and keeps 1920×1080.

| file | what |
|---|---|
| [mix.jpg](mix.jpg) | the default stand-in's mix. On top, the whole trailer, with the cuts (orange), the cut to black (red) and silence (blue). Below, a row per cut from −40 to +100 ms: the sounds ringing into it (orange) over the mix (grey), gone by +60 ms (blue) |
| [supersample.jpg](supersample.jpg) | the hero at trailer 2.0, 3× nearest-neighbour: a windowless still at `--supersample 1` beside the trailer capture's default 2× |
| [capture.py](capture.py) | both images |

## The cue list

Trailer mode's `--dump-sounds` writes the cues the live run plays, from the
same array the live scheduler gets, plus the trailer's fades in song seconds:

```
{"trailerStart": 121.400000, "start": 121.400000, "end": 154.026000, "cutFadeS": 0.0600,
 "fadeOut": {"at": 152.526000, "seconds": 1.5000}, "sounds": [
  {"time": 121.580300, "sound": "swing/1", "clip": "SWORD_Whoosh_Hit_Armor_Hard_RR3_mono",
   "gain": 0.1870, "pitch": 1.0000, "fadeAt": 123.880800}, ...
```

- `fadeAt` is the cut a cue fades out from: the next cut after its shot's.
  231 of the default trailer's 267 cues have one. The last shot's cues have
  none, since the end fade takes them.
- With `--shot`, no cue has one, as live.
- The mixer applies the fades as the live player's envelope does:
  - each cue's gain falls linearly over `cutFadeS` from its `fadeAt`;
  - the whole mix falls linearly over `fadeOut.seconds` from `fadeOut.at`.

The mixer's music decode lines up with the live player's song
(`Assets/Baked/Audio/action.wav`): the cross-correlation over song 120–124 s
peaks at 0 samples.

## The stand-in runs

| run | cues (clips) | peak before / after the limiter | over the knee | cuts silent by +60 ms | end fade RMS by quarter |
|---|---|---|---|---|---|
| default | 267 (16) | 1.34 / 0.950 | 2.27% | 9 of 9 | 0.371, 0.255, 0.162, 0.060, then 0 |
| `--palette neon --monster-style crystal` | 786 (25) | 1.59 / 0.950 | 2.95% | 9 of 9 | 0.371, 0.255, 0.162, 0.060, then 0 |
| `--shot 10` | 36 (11) | 1.16 / 0.948 | 3.33% | (no cuts) | (ends at the cut to black) |

Each video is 1959 frames (`--shot 10`: 234) of 1920×1080 at 60 fps H.264,
with 32.65 s of AAC.

A cut's ringing sounds peak at 0.04–0.30 at the cut. The orange rows in
mix.jpg show them fading under the next shot's opening blow.

**The limiter.** It works 2–3% of the time.

- Half of that is the song alone. The MP3 decodes with overs (peak 1.10), so
  it crosses the limiter's knee at the mixer's music level of 0.8.
- The rest is the effects stacking on the drops.

The limiter is the soft knee the normal capture mix already uses (TD13). The
operator's listening check (plan.md) covers whether its saturation is ever
audible.

## The 2× capture

The trailer's captures default to `--supersample 2`, which is URP's render
scale (ps-hgs.3, Supersampling). With it, URP draws the capture's frame at
3840×2160 and its final blit downsamples it into CaptureTool's 1920×1080
target, a 2×2 box per pixel. Stills and video are therefore kept and encoded
at 1920×1080 either way.

CaptureTool now logs what it draws:

```
--supersample 2 (the trailer capture's default):
  DemoMain: --supersample 2: rendering at 2x the output, downsampled
  CaptureTool: drawing 3840×2160, kept at 1920×1080
--supersample 1:
  CaptureTool: drawing 1920×1080, kept at 1920×1080
```

Both stills are 1920×1080 PNGs. In supersample.jpg, the 2× still's visor
slits, plate edges and blade are clean where 1× steps.

## Reproducing

From the worktree's root, after `uv run --project demo3d/tools demo3d build`:

```
T() { uv run --project demo3d/tools demo3d trailer --stand-in "$@"; }
T --out captures/trailer/capture/stand-in.mp4
T --out captures/trailer/capture/neon-crystal.mp4 -- --palette neon --monster-style crystal
T --out captures/trailer/capture/shot10.mp4 -- --shot 10
P="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
for s in 2 1; do C=$PWD/captures/trailer/capture/x$s; mkdir -p $C
  "$P" -batchmode -force-metal -logFile $C/player.log --trailer --start-at 1.5 --stills-at 2.0 \
    --capture-stills $C --stills-format png --supersample $s; done
E() { uv run --project demo3d/tools python demo3d/evidence/trailer/capture/capture.py "$@"; }
E crops 760,330,320,180 captures/trailer/capture/x1/still-002.000.png captures/trailer/capture/x2/still-002.000.png
E mix captures/trailer/capture/stand-in.sounds.json
```
