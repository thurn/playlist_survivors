# Rhythm judgement and durable run diagnostics — ps-oli.2

Validated November 3, 2026 on staging base `a210898`. These are deterministic
fixtures and scripted player runs, **not operator hand-play**. The scope is
judgement and local telemetry; sword impact, crowd work and integrated design
edits belong to the other ps-oli workers.

## Baseline and diagnosis

Read the current design/style/plan, game-feel and in-place-cleave evidence,
and the display-freeze incident report. Inspected the archived startup logs
in local cache `evidence/nov3-f-impact-baseline` (19,300 bytes total), current
rotating Player logs, and the earlier ps-uyc/ps-ueo recorded-run evidence.
The archived observed launch has no scripted marker; its provenance is an
observed operator-era launch, not proof of the earlier frozen run. Current
rotating logs can belong to the separate beat-step experiment or unknown
launches and are **not** attributed to that baseline. The lost incident log
cannot be reconstructed from these files. Its preserved excerpts report
962–965 enemies and zero analyzer stalls, but no individual F attempts.

Old `Judgement` allowed ±80 ms at beats, ±60 ms at halves **only when an onset
was detected within 50 ms**, and ±60 ms at strong transients. Thus even an
exact eighth note could fail with a reliable grid and missing onset; a press
81 ms from a beat could fail too. Both are reproducible code limits, not a
claim about which presses the operator made. Off returned the press itself
as its measured point, hiding the signed error. Cooldown drops were a
separate issue, incompletely logged: shared F/Space cooldown remains 600 ms,
with a 150 ms final buffer. At music periods shorter than 600 ms, pressing on
every beat can legitimately encounter cooldown.

The live path is Input System event time → `MusicClock.KeySong(real)` →
heard song time minus input calibration → `Simulation.Press` at the current
frontier → judgement over entries revealed at that moment. The heard time,
not processing/frontier time, determines the reward. Output latency includes
the device **and Unity DSP buffering**; video calibration changes frame time,
not input time. `SimDriver.Add` reveals entries from max(stamp + budget,
frontier); a later delivery cannot retroactively change a reward. Buffered
actions retain their original grade until the shared cooldown fires. Walking
history can clamp the action's execution time without regrading its press.
The audio presenter schedules heard-time cues as soon as available when
already late; ordinary future cues can be dropped. Diagnostics now distinguish
these scheduler counters from analyzer stalls and from judgement failure.

## Exact new contract

- Beats: ±min(100 ms, 20% of the local committed interval).
- Half beats: ±min(80 ms, 16% of that interval), independent of onset detection.
- Strong transients: ±80 ms; retain the transient's actual timestamp.
- No reliable beat (including silence, low evidence or a tracking gap): every
  eligible action still earns the existing aura. No new phase guessing or
  calibration offset is introduced.
- Misses retain their nearest grid candidate, its kind, time and window.
  Boundary comparisons include the existing 1 ns numerical tolerance.
- F/Space movement, cooldown, buffer arbitration, shield, damage and aura
  magnitude/duration are unchanged. An off-beat action still executes.

| BPM | Beat half-window | Half-beat half-window | Exact quarter-beat, no onset |
| --- | --- | --- | --- |
| 80 | 100 ms | 80 ms | Miss |
| 90 | 100 ms | 80 ms | Miss |
| 128 | 93.75 ms | 75 ms | Miss |
| 174 | 68.97 ms | 55.17 ms | Miss |

These caps deliberately preserve gaps at fast tempos; the fast-tempo beat
window alone is slightly narrower than the old fixed 80 ms. Removing the
half-beat onset gate increases total accepted grid coverage: without onsets,
the ideal 174 BPM coverage rises from 46.4% to 72%, while 28% still misses.
At 80 BPM it rises from 21.3% to 48%. Actual strong onsets may fill some gaps.
This is a stated tuning tradeoff, not a measured claim of improved subjective
feel. Tests exercise early/late boundaries at all four tempos, weak versus
strong onsets, late reveals, silence/low evidence, an 80→174 BPM change, and
a delayed 6.500 s input delivered at 6.550 s whose buffered fire at 6.640 s
retains the original on-beat grade despite 6.640 s itself being off-beat.

## Finding and reading a run

Every normal run, Again, script and replay automatically writes to:

`~/Library/Caches/playlist-survivors/champions/run-diagnostics/`

Each UTC+UUID `.log` has a `.log.summary` beside it. The corresponding engine
log prints `Run diagnostics:` with both exact paths; `just champions-tool logs`
locates the isolated engine logs. This common independent root also covers
raw Unity launches and survives worktree deletion. Unlike engine logs, it
retains **runs**, including Again, rather than only launches.

The detail file includes raw keyboard event/mapped heard timestamps for
physical F/Space, and action outcome rows for physical, scripted and replayed
inputs: accepted, cooldown, buffered, buffer-fired, buffer-replaced,
preserved-timed-buffer, hold cancellation and card/held-key blocks. Count
`accepted` plus `buffered` plus initial rejections to count attempts; a later
buffer-fired/replaced/cancelled row describes the same earlier attempt.
Every action has grade, signed heard-minus-candidate delta, window,
reliability, revealed beat count, processing age and cooldown deadline.
Keyboard events suppressed by an open menu never enter the sim; queue
overflow is recorded separately in the summary.

Summaries include mode, version/build GUID, seed, source category (no media
path/name), fixed/muted settings, quality, resolution, clock/calibration and
audio counters; action reason/grade totals; hits/kills, hero contact outcomes
and damage, XP, upgrades/build, holds and boss phase events. Aura seconds are
sampled active-combat uptime, with at most frame-scale boundary error.
Clock/audio/settings fields are the most recent five-song-second snapshot,
whose heard/frontier timestamps are explicit; gameplay totals update on
close. No input, media or telemetry is uploaded.

Details stop at **512 KiB**; cumulative totals and `detailOmitted` continue.
Summaries are replaced atomically every five song seconds and at disposal.
Retention prunes to 100 inactive runs when opening a run; open detail files
lease active runs against deletion, so concurrent active runs can temporarily
exceed that count. Interrupted runs are retained and later eligible for the
same pruning. The detail writer flushes each row; `running-or-interrupted`
without a final outcome identifies an interrupted session. This is process
interruption resilience, not a promise of power-loss durability. Normal quit,
Again and destruction close with an outcome. I/O failures are contained and
initial failures are reported in the engine's diagnostic-path line.

## Validation and observed limits

- Simulation suite: 507 passing tests. Includes bounded saturation with
  5,000 cooldown rejections, continuing totals, unique paths and active-file
  retention under another logger's pruning; deterministic buffer/replay tests.
- Unity EditMode: 143/143 passed, no skips. Indicator's old onset-free half-beat
  miss fixture moved to a true quarter beat to preserve its intended check.
- Final Mono player build passed. Recorded `fixture-walk` was deliberately
  updated for the new grading: hash `A2DB9F587F8C7519`, 254 events, replacing
  `83E6617CC8EC756D`. Historical recordings can differ under changed rules.
- Actual realtime headless native player, local soundtrack, seed 31, normal
  progression, 24-second bounded launch, scripted walking/F/Space, muted:
  final sim time 23.642 s, 42 kills, health 91, XP 29; three accepted F presses
  (two Beat, one Off), two F cooldown drops, one buffered/fired Space and one
  immediate Space. An accepted F at 11.280 s missed the nearest half at
  11.185 s by **+95 ms**, outside **±62.4 ms**, without preventing its attack.
  At 8.400 s F was dropped by cooldown, separately logged with its grade.
  Snapshot at heard 19.929 s: lead 186.3 ms, effective output 102.0 ms,
  input/video calibration zero, 0 analyzer stalls, 109 sounds scheduled,
  23 late/as-soon-as-possible sounds, **0 scheduler drops**. Final detail
  2,286 bytes, summary 1,115 bytes. This run preceded the final quality-name
  reporting correction; its `quality=Default` is Unity's old label, not the
  actual game tier, and is not evidence of Low quality.
- Final-build interruption check: a separate owned scripted native player,
  test tone, Low tier and a bounded 640×480 engine surface, killed after
  13 wall seconds without a quit callback. Its 10.192 s snapshot and flushed
  detail survived: `running-or-interrupted`, two accepted Beatless F presses,
  one cooldown-rejected Beatless F, one buffered/fired Beatless Space,
  health 97, 47 kills, XP 17. Zero stalls/drops at that snapshot. Detail
  2,137 bytes, summary 1,091 bytes. The tone still generated some analyzer
  beats; unreliable inputs correctly used the generous fallback.

Local untracked artifacts: cache `evidence/ps-oli2/` holds the interruption
engine log and summary-path record; the UTC diagnostic pairs begin
`20261103T194641452-9a9bb259...` (normal abort) and
`20261103T194813837-37d0554f...` (interruption). The isolated normal engine log
is under checkout identity `dc40e713bdd20f5680ec3fa3`, test launch
`20261103T194640Z-862675456a994aa88963fafca67a057f`. No captured media,
vendor-derived assets or private audio paths are committed here.

These short checks do not establish long-run performance, subjective
calibration, loudness, display-freeze safety or boss-phase runtime coverage.
Operator hand-play remains pending; integrated audiovisual validation belongs
to ps-oli's assembly task. No new human approval gate is introduced.
