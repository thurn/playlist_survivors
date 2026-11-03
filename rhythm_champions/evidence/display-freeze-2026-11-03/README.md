# Mac display freezes during Rhythm Champions playtests

Investigation recorded 2026-11-03 (America/Los_Angeles). This is an incident
record, not a root-cause finding. The operator reported two playtests in which
the built-in display stopped updating while audio continued. Only the built-in
display was in use; whether a dock was attached was not separately confirmed.
The time of the earlier incident is unknown.

## Sources

These sources were read on `JARVIS` (Mac17,6, Apple M5 Max, 64 GB RAM). They
are local to that Mac and are **not committed**:

- `~/Library/Logs/Rhythm Champions/Rhythm Champions/Player-prev.log` **as
  inspected before 11:15**. It held the 10:29–10:54 playtest then, but a
  subsequent launch rotated it again. The original log is no longer at that
  path. The observations and excerpts below preserve what was read; they are
  not a complete copy of the original log.
- `/Library/Logs/DiagnosticReports/WindowServer_2026-11-03-092700_JARVIS.cpu_resource.diag`.
- `/Library/Logs/DiagnosticReports/git_2026-11-03-004307_JARVIS.diag` and
  `git_2026-11-02-092046_JARVIS.diag`.
- `~/Library/Logs/DiagnosticReports/git-2026-11-02-073944.ips` and
  `Unity-2026-11-02-075533.ips`.
- `~/Library/Logs/Unity/Unity.Licensing.Client.log`, `last reboot`, `pmset -g
  log`, Git reflogs and worktree file timestamps. Reflogs and worktree files
  can change as work continues.

No macOS panic, GPU reset or WindowServer crash report for the 10:54 restart
was found in the inspected DiagnosticReports directories. Queries of the
pre-restart unified log yielded no specific GPU fault. A forced restart can
lose recent events; absence of a report does not rule out a graphics fault.

## 2026-11-03 timeline (PST)

| Time | Observation | Source and limit |
| --- | --- | --- |
| 09:24–09:27 | WindowServer used about 90 CPU seconds in 175 seconds (52% average), mostly display updates and Metal compositing. | CPU resource report. This was **before** the recorded playtest and is not a crash report. |
| 10:29:38 | Game log file created. Unity player used Metal, a 3456×2168 surface and High quality: 4× MSAA, eight bloom iterations and a 3,000-piece shatter budget. | Player log and file birth time. The built-in panel is 3456×2234; 3456×2168 is the logged surface size. |
| 10:52:11–10:52:27 | `origin/staging` updated by a push; Enact created worktree `ci-ce9fc0706181`. | Git reflog and worktree index timestamp. |
| About 10:53 | A Unity build in that CI worktree produced `resources.assets` (102 MB each in the player build and cache), updated `ArtifactDB` and `SourceAssetDB`, and contacted Unity licensing at 10:53:02–10:53:05. | File timestamps and licensing log. They do not measure GPU use or disk bandwidth. |
| 10:53:30–10:53:32 | Enact created `beat-step-experiment`; its reflog records `reset: moving to HEAD` at 10:53:32. About 11,270 files totaling 1.07 GB acquired modification times in that minute. | Git reflog and file timestamps. File sizes are **not** physical bytes written; APFS cloning and caches may apply. |
| 10:54:03 | The game log's modification time. Its final status lines show music playing, about 962–965 monsters alive, level 29, health 100 and zero reported music-analysis stalls. | Player log. The status lines use game time, not wall time; they cannot locate the exact instant the display stopped. |
| 10:54 | Mac restarted; `pmset` places the new boot at about 10:54:48. | Restart and power logs; operator reports display frozen while audio continued. |

Selected lines from the original game log as captured during investigation:

```text
Metal RecreateSurface[0xc40c0d980]: surface size 3456x2168
Quality: High: bloom from half resolution, scatter 0.80, 8 iterations; MSAA 4x; 3000 shard pieces; slash distortion, line halos and chromatic flashes on
Boot: Unity 6000.6.7f1, Metal, batch mode False
Music: song 1437.64 s, heard 1437.54 s, analysed 1440.66 s (3.02 s ahead), playing, 2908 beats known, lead 170 ms, stalls 0; run clock 826.0 s, 962 alive, level 29, health 100
Music: beat 2912 heard at 1442.040 s, known at 1441.218 s, 0.822 s ahead; reached on the frame heard at 1441.877 s
```

At the time of inspection, this file's birth time was 10:29:38 and its last
modification time was 10:54:03. Later launches changed both the path's
contents and timestamps. Future investigations should copy a suspect player
log immediately to a safe local location outside the public repository.

The 3456×2168 surface has 3.61 times the pixels of a 1920×1080 target; 4×
MSAA yields 14.45 times the sample positions for passes using that count.
This is arithmetic, **not** a GPU cost measurement. The
[full-build evidence](../full-build/README.md) measures 1080p Low-tier
offscreen rendering and explicitly does not establish windowed High-tier GPU
performance.

## Earlier occasion and other reports

`last reboot` lists 2026-11-02 08:10 and 2026-10-30 12:27. The operator could
not identify which, if either, followed the earlier playtest freeze. Before
the November 2 restart, an agent-associated Git process crashed at 07:39:44
and a Unity Editor process crashed at 07:55:26. Those process crashes do not
establish a game session or display failure at the same time.

Separate resource reports show an agent-associated `git reset` writing
137.44 GB over 6,076 seconds ending November 3 at 00:43, and another writing
34.36 GB over 3,106 seconds ending November 2 at 09:20. Both say "Action
taken: none." They establish unusually heavy Git work, but do **not** place
either reported process at the November 3 10:54 restart. Roughly 505 GiB was
free on the volume after the restart; no evidence points to disk exhaustion.

## Assessment and follow-up evidence

Agent work **did overlap** the November 3 playtest: CI Unity build and
worktree operations ran in its final minutes. Audio continuing while the sole
display stopped updating is consistent with a display/compositor or GPU
rendering stall. Concurrent load is a plausible trigger, but the records do
not distinguish a game rendering defect, Unity/Metal fault, macOS display
fault or another cause. The WindowServer CPU report predates the game session.

Keep CI, Unity builds and large worktree operations out of interactive
playtests as immediate risk reduction. Medium quality alone is not established
as a fix. If this recurs, copy the rotating player log at once, record the
wall-clock freeze time and whether audio, pointer and keyboard continue, and
capture a contemporaneous macOS system diagnostic if possible. Windowed GPU
and frame timing would help identify the failing component. Avoid
intentionally reproducing a hard display freeze solely for diagnostics.
