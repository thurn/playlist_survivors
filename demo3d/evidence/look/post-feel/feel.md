# Post feel stills (ps-e8r.18)

Rendered by feel.py (IL2CPP player, fixed time). L* p50 and p98, mean chroma C*ab and RMS contrast
(the standard deviation of L*) over the whole frame.

## On and off a beat (drop-2)

| still | t (s) | L* p50 | L* p98 | chroma | RMS |
|---|---|---|---|---|---|
| on the beat | 142.850 | 39.4 | 98.3 | 44.7 | 23.3 |
| 0.6 beat later | 143.083 | 35.0 | 97.3 | 41.3 | 22.8 |
| on the beat, --no-feel | 142.850 | 33.1 | 97.1 | 33.3 | 22.6 |

## Sections (0.6 beat after a quiet downbeat)

| still | t (s) | L* p50 | L* p98 | chroma | RMS |
|---|---|---|---|---|---|
| breakdown-a | 79.217 | 34.0 | 87.4 | 20.1 | 20.2 |
| breakdown-b | 83.867 | 34.0 | 85.6 | 19.9 | 20.5 |
| drop-1-a | 51.350 | 38.9 | 93.4 | 39.8 | 22.5 |
| drop-1-b | 63.733 | 39.9 | 94.9 | 42.8 | 23.1 |
| drop-2-a | 138.050 | 34.3 | 97.1 | 42.7 | 23.0 |
| drop-2-b | 147.350 | 37.0 | 97.4 | 45.8 | 22.2 |
| final-peak-a | 170.567 | 35.3 | 97.7 | 48.4 | 24.3 |
| final-peak-b | 178.317 | 34.9 | 98.0 | 47.5 | 25.1 |

Lowest drop or peak chroma 39.8 against the highest breakdown chroma 20.1: above.

## Strip around the accent at 148.655 s (every 2 frames)

| still | t (s) | L* p50 | L* p98 | chroma | RMS |
|---|---|---|---|---|---|
| -22 ms | 148.633 | 33.9 | 96.8 | 41.7 | 22.3 |
| +12 ms | 148.667 | 52.5 | 97.8 | 62.0 | 22.2 |
| +45 ms | 148.700 | 48.8 | 97.6 | 59.1 | 23.0 |
| +78 ms | 148.733 | 45.6 | 97.4 | 55.5 | 23.3 |
| +112 ms | 148.767 | 42.8 | 97.2 | 52.1 | 23.0 |
| +145 ms | 148.800 | 40.1 | 97.2 | 49.1 | 22.8 |
| +178 ms | 148.833 | 38.4 | 97.1 | 47.1 | 23.0 |
| +212 ms | 148.867 | 37.0 | 97.0 | 45.6 | 22.9 |
| +245 ms | 148.900 | 35.9 | 96.8 | 44.5 | 22.9 |
| +278 ms | 148.933 | 35.2 | 96.8 | 43.8 | 22.9 |
| +312 ms | 148.967 | 34.6 | 96.7 | 43.2 | 22.9 |
| +345 ms | 149.000 | 34.2 | 96.5 | 42.8 | 22.8 |
| +378 ms | 149.033 | 34.1 | 96.2 | 42.7 | 22.6 |
| +412 ms | 149.067 | 38.6 | 97.3 | 46.2 | 22.7 |

## Reproducibility

The accent's frame (148.667 s, mid-flash) rendered by two player processes:

- with effects (the strip's run and another): max channel difference 46, 13.5872% of pixels differ, 2.0763% by more than 2;
- with `--no-effects`: max channel difference 0, 0.0000% of pixels differ, 0.0000% by more than 2.
