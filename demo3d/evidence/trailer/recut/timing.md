# Cut-frame timing (ps-7mx.3)

GPU draw ms (frame time with any switch), windowless IL2CPP player on the Mac at fixed time, load average 18-25 from other sessions.

## Trailer runs, probes centred on each cut (probe-4..6)

The first frame of each window is the cut frame (the switch and the shot's opening blow), the next two the frames after it.

| cut (trailer s) | run 4 | run 5 | run 6 |
|---|---|---|---|
| 2.4833 | 33.35 / 33.99 / 17.11 | 15.50 / 23.90 / 9.51 | 14.96 / 26.21 / 9.75 |
| 14.8833 | 9.10 / 8.68 / 9.01 | 11.76 / 10.08 / 6.91 | 11.72 / 8.21 / 6.42 |
| 27.2667 | 20.91 / 13.12 / 10.92 | 24.37 / 11.81 / 9.18 | 18.89 / 13.47 / 10.31 |
| 39.6500 | 14.98 / 12.63 / 8.22 | 20.36 / 16.74 / 10.60 | 18.27 / 11.18 / 12.35 |
| 45.8500 | 15.33 / 6.77 / 7.04 | 11.68 / 7.90 / 7.67 | 17.35 / 6.52 / 6.65 |
| 48.9500 | 8.09 / 7.01 / 7.70 | 6.66 / 6.56 / 6.93 | 12.83 / 10.06 / 13.03 |
| 52.0333 | 24.00 / 22.08 / 10.13 | 19.17 / 30.08 / 22.32 | 22.81 / 20.44 / 11.24 |

## The same frames with no cut: `--shot N --from-preroll` (pre-a..c)

The shot plays from its pre-roll, so its opening blow's frame is drawn with no environment switch or reset. Frames fall 1/3 frame off the trailer's grid (the run starts at the pre-roll).

| shot | run a | run b | run c |
|---|---|---|---|
| 2 | 2.4872: 104.91 / 2.5039: 10.31 / 2.5205: 10.66 | 2.4872: 25.76 / 2.5039: 16.82 / 2.5205: 13.99 | 2.4872: 27.01 / 2.5039: 13.24 / 2.5205: 11.52 |
| 4 | 27.2614: 15.59 / 27.2781: 10.60 / 27.2947: 9.31 | 27.2614: 18.29 / 27.2781: 9.69 / 27.2947: 11.32 | 27.2614: 17.60 / 27.2781: 6.60 / 27.2947: 6.67 |
| 5 | 39.6485: 23.51 / 39.6652: 11.59 / 39.6818: 11.14 | 39.6485: 26.99 / 39.6652: 10.07 / 39.6818: 9.79 | 39.6485: 22.23 / 39.6652: 13.53 / 39.6818: 10.94 |
| 6 | 45.8421: 14.49 / 45.8588: 8.14 / 45.8754: 7.08 | 45.8421: 13.99 / 45.8588: 6.12 / 45.8754: 6.19 | 45.8421: 16.71 / 45.8588: 12.19 / 45.8754: 17.92 |
| 8 | 52.0356: 29.89 / 52.0523: 16.22 / 52.0689: 16.05 | 52.0356: 43.04 / 52.0523: 18.76 / 52.0689: 11.52 | 52.0356: 35.35 / 52.0523: 10.03 / 52.0689: 10.37 |

## Verdict

- **Read literally, the 2 ms rule fails:** a cut frame runs up to about
  14 ms over the two frames after it (the blossom cut, 27.267, is +7 to
  +14 ms; the snow cut, 45.850, +4 to +11 ms).
- **That cost is the opening blow, not the cut.** Every shot opens on a
  blow landing on its cut beat (a slam or cleave killing dozens, a lens hit
  peaking on that frame, the cut's flash on five of them). Drawn with no cut
  at all (`--from-preroll`), the same frames cost as much or more: shot 4's
  15.6-18.3 ms against the trailer's 18.9-24.4, shot 6's 14.0-16.7 against
  11.7-17.4, and shots 2, 5 and 8 higher without the cut than with it.
- **The switch itself** costs 0.10-0.12 ms of CPU on its frame (the
  player's log: `cut to shot 4 (blossom) at trailer 27.267 s: switch and
  reset 0.12 ms`), as ps-7mx.2's residency runs measured (0.12-0.20 ms, 41 of
  42 switch frames within 2 ms in its blow-free switching run).
- The capture renders at fixed time, so none of this reaches the video;
  whether the live run hitches at a cut stays the operator's check
  ([trailer.md, Manual QA](../../../../docs/archive/tech-demo-3d/trailer.md#manual-qa),
  "Smoothness").

Reproduce (from sh, after `demo3d build`):

```
P="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$P" -batchmode -force-metal -logFile probe.log --trailer --fixed-time \
  --gpu-probe-at 2.465,14.852,27.239,39.626,45.82,48.917,52.013 --gpu-probe-out probe.json --seconds 52.3
uv run --project demo3d/tools python demo3d/evidence/trailer/residency/residency.py timing probe.json
"$P" -batchmode -force-metal -logFile pre.log --trailer --shot 4 --from-preroll --fixed-time \
  --gpu-probe-at 27.239 --gpu-probe-out pre.json
```
