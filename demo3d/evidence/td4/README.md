# TD4: client playback

Measured on Cygnus, 2026-10-28, IL2CPP player (release build), the bone-texture crowd (TD5) unless
noted.

## The song through the client

Whole song in fixed-time mode, batch mode (`--fixed-time --stats`): on-screen counts are the
monsters whose centre projects into the 16:9 view at the framing track's view width.

| Section | Target on screen | Mean on screen (alive + dying) | Max | Ratio | Mean eval ms | Mean crowd ms |
|---|---|---|---|---|---|---|
| intro | 12 | 15 | 17 | 1.25 | 0.01 | 0.17 |
| groove | 300 | 266 | 323 | 0.89 | 0.16 | 0.05 |
| break-1 | 300 | 301 | 304 | 1.00 | 0.16 | 0.06 |
| drop-1 | 800 | 686 | 789 | 0.86 | 0.44 | 0.06 |
| breakdown | 600 | 596 | 749 | 0.99 | 0.40 | 0.06 |
| groove-2 | 1000 | 736 | 841 | 0.74 | 0.63 | 0.10 |
| build | 1000 | 834 | 879 | 0.83 | 0.58 | 0.09 |
| break-2 | 800 | 784 | 803 | 0.98 | 0.53 | 0.09 |
| drop-2 | 1600 | 1108 | 1369 | 0.69 | 0.90 | 0.10 |
| final-peak | 2000 | 1211 | 1314 | 0.61 | 1.17 | 0.12 |
| outro | 1000 | 1046 | 1211 | 1.05 | 1.02 | 0.10 |
| ending | 500 | 945 | 968 | 1.89 | 0.98 | 0.10 |

- The fill lags the level script's targets where the hero travels (drop-2, final-peak), as TD3
  measured, and the ending doesn't clear the horde. Both are TD13's tuning; the placeholder
  envelope (2,000) is TD12's to replace.
- "Eval" is the client's timeline evaluation on the main thread (plain C#, one thread): about
  1 ms at 1,300 live monsters. crowd.md asks for a Burst job; at this cost it stays plain C#
  until a profile says otherwise (TD12's per-pass captures will show it).
- Timeline build in the IL2CPP player: 2.0 to 2.2 s (under the 3 s budget); in a Mono player
  6.8 s, and sim tick p99 18 ms there against 5.3 to 5.6 ms in IL2CPP (the Mono JIT; TD12
  reports IL2CPP).

## Seeks

`--start-at 45` against a run from 40 s, both fixed time, still at 45.000 s (envelope 750, the
stock crowd): the two JPEGs are byte-identical ([seek-045-from-040.jpg](seek-045-from-040.jpg)).
A first attempt differed (PSNR 15.6): new stock-crowd slots showed their bind pose for one frame,
since an Animator first evaluates a new graph on the next update. StockCrowd now evaluates a
fresh slot at once.

## Capture tool

- Stills: tagged JPEGs at the requested song times (`--capture-stills DIR --stills-at s,s`).
- Video: raw frames streamed into ffmpeg (libx264, CRF 16, 1080p60) with `ps-render` in the
  comment metadata; a 2 s pair (`--aa msaa4` against `--aa taa`, 160 to 162 s) gave 121 frames
  each, PSNR 26.5 between them. Videos stay in the gitignored `captures/`.
- Three things that broke and how they were fixed:
  1. A batch-mode player draws no frames, and drawing the camera by hand each frame
     (Camera.Render or a render request) grew memory about 90 MB a frame in URP 17.6 (its pooled
     targets are freed only at the end of a frame the player loop renders). Captures therefore
     run as a windowed player and read back after URP draws the camera.
  2. IL2CPP's `Process` can't start a child with redirected input on Windows (Win32Exception,
     "Native error= Success"): `PipeProcess` starts ffmpeg through CreateProcess with a pipe.
  3. Through IL2CPP's FileStream the pipe never reached end of input; `PipeProcess` writes with
     WriteFile and closes the handle itself.

## Stills

- [song-030-bone.jpg](song-030-bone.jpg), [song-170-bone.jpg](song-170-bone.jpg): the song at 0:30
  and in the final peak on the bench ground. At 2:50 the dragon reads white: lingering ticks
  keep its hit flash saturated (TD10 tiers tick feedback).
