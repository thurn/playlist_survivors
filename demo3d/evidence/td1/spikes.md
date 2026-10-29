# TD1 spikes and setup checks (Cygnus, 2026-10-28)

All times are wall-clock on Cygnus (i7-12700F, RTX 4070 Ti, DDR4-2133, Dev Drive D:).

## What TD1 verifies

| Check | Result |
|---|---|
| Unity probe, explicit `-projectPath` | `-createProject D:\scratch\probe` exited 0 in 29 s; log: `Licensing is initialized (took 1.36s)` |
| .NET SDK | `dotnet --list-sdks`: `10.0.403` |
| uv | `uv 0.13.6` |
| PIX | `C:\Program Files\Microsoft PIX\2603.25` |
| D: | `Get-Volume D`: ReFS, label `DevDrive`, 107 GiB free of 120 GiB (`fsutil devdrv query` needs admin; not run) |
| Vendor sources | `D:\vendor\synty`: the 11 packages listed in windows.md; `D:\vendor\unity_assets\UnityAssets\Assets`: Hovl Studio, KriptoFX, Magic Pig Games (Infinity PBR), AmplifyImpostors, MeshCombineStudio and the four sound and music packs, each with its `.meta`; the song MP3 (6,369,253 bytes) |
| Relay | pushed `td/push-test` and deleted it; `demo3d` created from `origin/master` (`e5d0906`) |
| Power plan | `powercfg /getactivescheme`: High performance |
| Memory | 4 × 8 GiB at 2133 MHz (`Win32_PhysicalMemory`: Speed and ConfiguredClockSpeed 2133); XMP off, as recorded |
| GPU | RTX 4070 Ti, driver 610.88, 12,282 MiB |

## The C# version Unity 6.6 accepts

A probe file with a file-scoped namespace and a `record struct` failed with
`CS8773: Feature 'file-scoped namespace' is not available in C# 9.0` and
`CS0518: Predefined type 'System.Runtime.CompilerServices.IsExternalInit' is not defined`.
Unity 6000.6.7f1 compiles **C# 9.0** (`-langversion:9.0` in its response files) over .NET
Standard 2.1, without `IsExternalInit`, so no records or `init` setters. The `dotnet` projects
pin `LangVersion` 9.0 and `netstandard2.1` for the layer to match.

## `-logFile -`

Streams the editor log to stdout on Windows: a cold `Tasks.Noop` with `-logFile -` printed 334
lines to the console, exit 0. The task runner uses it and tees the stream to `demo3d/Logs/`.

## The compile and test loop

EditMode tests (4 tests: 1 choreography, 3 project settings), on a warm Library:

| Loop | Cold launch | Warm editor |
|---|---|---|
| Tests, no change | 7.0 s (first run in a session: 12.3 s) | 0.8 s |
| One `.cs` changed, recompile and test | 7.4 s | 3.2 to 4.3 s |
| A compile error reported | (a cold run's exit 1) | 1.2 s |
| A task method (`Tasks.Noop`) | 5.8 s | 0.7 s |
| Editor start | | 5.0 s |

The warm editor is reliable over these runs, a compile error, a domain reload and a stop, so it
stays (WarmEditor.cs). Its advantage grows once the vendor folder is imported, since a cold
launch re-checks the asset database each time. Before a push, `demo3d check` stops it and runs
the Unity checks cold.

## Player builds

An empty scene, Windows x64, Direct3D 12, from a cold batch-mode launch on a warm Library:

| Backend | Build time | Output |
|---|---|---|
| IL2CPP (first build) | 100.7 s (107 s with editor start) | 811 MiB, including IL2CPP's do-not-ship backup folder |
| Mono | 26.9 s (34 s with editor start) | 95 MiB |

## Windowed 1920×1080 on the 4K desktop

`PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0
--frame-probe --warmup 3 --seconds 10` (IL2CPP, vsync off) on the 3840×2160 display at 200%
scaling ([frame-probe.json](frame-probe.json)):

- `Screen` 1920×1080, `Display.renderingWidth/Height` 1920×1080, windowed, dpi 192: the
  backbuffer is exactly 1920×1080 pixels despite the 200% scaling.
- `graphicsDeviceType` Direct3D12 on the RTX 4070 Ti.
- FrameTimingManager reports GPU times on D3D12: 18,935 frames in 10 s, GPU p50 0.091 ms,
  p99 0.127 ms; main thread p99 0.51 ms; render thread p99 0.30 ms. The GPU mean (0.064 ms) is
  below its median, so some frames report 0 GPU time; TD7's harness counts and excludes
  zero readings rather than averaging them in.

This shows the measurement path works; an empty scene says nothing about budgets.

## Setup's clone time on the Dev Drive

Python's `shutil.copy2` calls CopyFile2, which block-clones on ReFS: a 1 GiB file in 10 to 12 ms
with no free space used (`shutil.copyfile`, a plain copy: 612 ms and 1 GiB used). 2,000 small
files: 466 ms cloned, 3,270 ms copied.

`demo3d setup` in a fresh worktree:

| Step | Time |
|---|---|
| Vendor (empty manifest until TD2) | 0.00 s |
| Baked/Audio (song converted, then cached) | 2.4 s first, 0.00 s cloned |
| Library: 16,086 files, 792 MiB, cloned | 2.66 s |
| First batch-mode open of the cloned Library | 43.4 s |
| Total | 46 to 49 s |
| Without a cached Library (cold import) | 56 s open, 61 s total |
| The same worktree's next launch | 5.8 s |

The first open of a cloned Library spends 34 s compiling scripts: Bee rebuilds its build graph
because it records the project's absolute path (`Rebuilding DAG because FileSignature timestamp
changed`), though it reuses the compiled assemblies. Asset imports are reused, which is where the
warm Library pays once the vendor folder holds thousands of assets.
