# TD6 tick timing

Sim tick timing after the 5 s warm-up (benchmarks.md, Sim tick timing), under dotnet in Release on Cygnus,
built by `dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- timing --repeat 2` (the fastest of
two builds per row). Every timeline runs on the arena: the song along its goals, S1 to S5 in the opening clearing,
S6 in the walled courtyard. Other agent sessions (TD5, TD7) were running, so p99 and max run high: back-to-back
runs on the same load (below) put TD6 within noise of TD3 at N = 1000 and 2000.

| timeline | N | off-screen reserve | mean on screen | deaths/s (target) | build s | ticks | p50 ms | p99 ms | max ms | mean ms | flow rebuilds (slices) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| song | per section | 0.5 × target | – | – | 2.050 | 11640 | 0.015 | 4.368 | 19.888 | 0.1639 | 84 (1968) |
| S1 (none, 60 s) | 500 | 250 | 500 | 0.0 (0.0) | 0.184 | 3301 | 0.003 | 1.162 | 2.021 | 0.0474 | 2 (27) |
| S1 (none, 60 s) | 1000 | 500 | 1000 | 0.0 (0.0) | 0.359 | 3301 | 0.007 | 2.249 | 4.425 | 0.0946 | 2 (27) |
| S1 (none, 60 s) | 2000 | 1000 | 1856 | 0.0 (0.0) | 0.818 | 3301 | 0.013 | 5.244 | 9.407 | 0.2177 | 2 (27) |
| S1 (none, 60 s) | 2800 | 1400 | 1865 | 0.0 (0.0) | 1.597 | 3301 | 0.014 | 10.714 | 14.128 | 0.4307 | 2 (27) |
| S2 (demo, 60 s) | 500 | 250 | 514 | 25.0 (25.0) | 0.190 | 3301 | 0.005 | 1.111 | 2.137 | 0.0494 | 3 (53) |
| S2 (demo, 60 s) | 1000 | 500 | 1029 | 50.0 (50.0) | 0.426 | 3301 | 0.011 | 2.618 | 6.194 | 0.1151 | 1 (1) |
| S2 (demo, 60 s) | 2000 | 1000 | 1621 | 89.4 (100.0) | 1.219 | 3301 | 0.025 | 8.245 | 11.326 | 0.3375 | 3 (51) |
| S2 (demo, 60 s) | 2800 | 1400 | 1612 | 88.9 (140.0) | 1.937 | 3301 | 0.023 | 12.951 | 14.856 | 0.5254 | 4 (78) |
| S3 (demo, 60 s) | 500 | 250 | 513 | 24.9 (25.0) | 0.212 | 3301 | 0.005 | 1.254 | 2.730 | 0.0557 | 5 (104) |
| S3 (demo, 60 s) | 1000 | 500 | 1029 | 49.9 (50.0) | 0.458 | 3301 | 0.011 | 2.780 | 5.079 | 0.1230 | 4 (78) |
| S3 (demo, 60 s) | 2000 | 1000 | 1516 | 99.2 (100.0) | 1.340 | 3301 | 0.029 | 8.636 | 16.044 | 0.3704 | 13 (306) |
| S3 (demo, 60 s) | 2800 | 1400 | 1381 | 136.7 (140.0) | 1.691 | 3301 | 0.022 | 11.133 | 16.954 | 0.4555 | 13 (308) |
| S4 (demo, 60 s) | 500 | 250 | 513 | 24.9 (25.0) | 0.218 | 3301 | 0.005 | 1.252 | 2.633 | 0.0569 | 5 (104) |
| S4 (demo, 60 s) | 1000 | 500 | 1029 | 49.9 (50.0) | 0.467 | 3301 | 0.011 | 2.810 | 5.719 | 0.1249 | 4 (78) |
| S4 (demo, 60 s) | 2000 | 1000 | 1516 | 99.2 (100.0) | 1.297 | 3301 | 0.029 | 8.519 | 15.082 | 0.3521 | 13 (306) |
| S4 (demo, 60 s) | 2800 | 1400 | 1381 | 136.7 (140.0) | 1.665 | 3301 | 0.021 | 10.797 | 15.613 | 0.4448 | 13 (308) |
| S5 (heavy, 60 s) | 500 | 250 | 498 | 50.0 (50.0) | 0.178 | 3301 | 0.006 | 0.908 | 2.270 | 0.0428 | 6 (128) |
| S5 (heavy, 60 s) | 1000 | 500 | 941 | 100.0 (100.0) | 0.454 | 3301 | 0.013 | 2.840 | 5.229 | 0.1214 | 6 (128) |
| S5 (heavy, 60 s) | 2000 | 1000 | 1055 | 155.0 (200.0) | 1.144 | 3301 | 0.018 | 7.816 | 10.659 | 0.3100 | 5 (104) |
| S5 (heavy, 60 s) | 2800 | 1400 | 1068 | 156.3 (280.0) | 1.405 | 3301 | 0.018 | 9.344 | 11.912 | 0.3736 | 7 (154) |
| S6 (demo, 60 s) | 500 | 250 | 492 | 25.0 (25.0) | 0.336 | 3301 | 0.006 | 2.101 | 3.567 | 0.0882 | 5 (103) |
| S6 (demo, 60 s) | 1000 | 500 | 865 | 50.0 (50.0) | 0.704 | 3301 | 0.014 | 4.343 | 6.951 | 0.1899 | 17 (407) |
| S6 (demo, 60 s) | 2000 | 1000 | 1123 | 98.4 (100.0) | 1.693 | 3301 | 0.020 | 11.396 | 17.263 | 0.4670 | 11 (254) |
| S6 (demo, 60 s) | 2800 | 1400 | 1084 | 101.4 (140.0) | 1.909 | 3301 | 0.021 | 13.006 | 17.538 | 0.5155 | 5 (101) |

## Back to back with TD3 (same machine state, one build each, CLI `stress <scene> --n N` / `song`)

| run | TD3 (`f4b3dcb`, open ground) | TD6 (arena) |
|---|---|---|
| song build | 1.57-1.66 s | 2.07-2.13 s (1.87 s best under dotnet test) |
| S1, N = 1000, p99 | 1.76 ms | 1.60-2.25 ms |
| S1, N = 2000, p99 | 3.19 ms | 2.89 ms |
| S3, N = 2000, p99 | 4.40 ms | 5.20 ms (4.15 ms on open ground through TD6's code) |
| S6, N = 1000, p99 | - | 2.59-4.34 ms |

- Flow-field rebuilds toward the hero: 84 in the song (1,968 slices, about 23 a rebuild, one per tick of the beat
  it starts on), 2 to 17 per stress run. A whole field over the arena settles about 85,000 cells in 4.5 ms; the
  song's slices total about 120 ms. The 7 goal fields cost about 40 ms at load.
- The song's build grows by about 0.5 s over TD3: heading (line of sight and field) about 0.1 s, wall steps
  0.1 s, the extra sidesteps near walls about 0.2 s, flow slices and goal fields 0.16 s. Under Mono in Unity's
  editor it builds in 9.1-9.5 s (TD3: 6.6-7.0 s); the 3 s budget is the player's, measured under .NET.
- Not measured: the player build (IL2CPP); TD7 times the sim in the player.
