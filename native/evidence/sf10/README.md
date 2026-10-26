# SF10 — bounded thirty-minute state and F review

The final integration covers all **five rank-8 base weapons and six applied
max passives**. The optimized measured source is
`bdd9e830489c2d77a4ee84b1938f53bd4d598717`, based on SW2 staging
`8e5320736e06e954f10435b74a68c9cccb8327e5`. Final source/integrated commit
SHAs are recorded in native bead notes. Current raw outputs are under
`five-weapon-integration/`; earlier phases remain source-scoped history.

## Implementation and review

Live pools retire objects behind monotonic recording IDs. Sampling/set-piece
roots and queued/deferred effects remain live; retired recoil and missile
targets are rejected. Legacy pools remain unchanged. Growing beat/tick/grid/
stride histories use immutable 256-entry shared pages, shared indices and
snapshot clones that exclude lazy contiguous read caches. Spatial bins and a
64-entry pure separation memo stay outside rollback state. Cache reuse checks
all exact input bits and bin origin, never just a digest.

Displayed journal-prefix caches invalidate on rollback. Tests compare every
view/moment with the old complete-overlay/linear-dedup path at nondefault lead
and delayed keys. Replay retains only the requested settled-hash prefix and
consumes transient moments, with no settled record log. Replay implementation
stays outside the analyzer-cache fingerprint introduced by tooling repair `.41`.

F's cold review used the integrated commits/native notes in
`milestone-f-commits.json`. Its three confirmed findings are fixed: zero/one-
beat gem timing, victory waiting forever without another committed downbeat,
and replay moment retention. Finite free-pulse fallback does not replace a
healthy known grid; causal tests truncate/poison uncommitted futures. The
finale clears even the synthetic high-HP field. `review.md` records fixes,
self-review and integration follow-through; no confirmation review was run.

SH5 integration exposed a missile looking up a retired target; it now takes
the existing retarget path. SH8's attraction collector bypassed shared vacuum
handling; that path is restored, with actual gem-arrival regression checks.
The five-weapon frame case initially exceeded 4 ms. Profiling led to a
conservative padded bounding-box rejection in Bible candidate search; near
candidates still use the original contact test, in the same order. Boundary,
degenerate and large-coordinate tests compare against that real predicate;
the earlier normal recording replays exactly under the optimized binary.
No damage, cooldown, effect, offer or difficulty setting is reduced.

## Frame bar

```sh
native/target/release/playlist-survivors --bench-density --check
```

The full native component passes on the optimized source: **3.38 ms p99**,
max 7.26 ms over 7,200 offscreen 1080p frames (7,080 at verified 300 foes/400 gems).
Engine/scene/encode CPU p99 are 2.781/0.546/0.315 ms. Lead is 0.2 s; scripted
Space keys arrive 5–35 ms late. There are 1,776 rollbacks, re-simulating 2.898 s
wall. The existing bar is p99 < 4 ms, not a maximum-frame threshold.
`density-full-native.txt` prints every weapon/passive rank and actual finalized
stats (Amount 2, Cooldown 0.6, Might 1.5, Recovery 1, Duration 1.5, maxHP 298.5984).
No window or Dock icon opens. The focused optimized check also passes at
3.27 ms p99; the failed 4.17 ms case/profile are retained.

## Snapshot age and minute-25 rollback

From `native/`:

```sh
/usr/bin/time -l target/release/examples/survivors_scale /tmp/sf10-normal-session equal
```

The arrived input is SF9's kept reverse-order Rust stream (SHA256
`1e056fad9ac630251e4c8260b92405fd8f1007730ca1164c196392d5e25deaa3`,
18,760,947 bytes). The synthetic schedule holds minute 25's XLFlower roster at
300 foes with effectively unkillable HP, damage exemption and 400 zero-XP gems.
The complete current base loadout and real passive deltas are installed
before the first snapshot. This is density evidence, not balance evidence.

Thirty-one clones at 59.9/899.9/1799.9 run seconds measure allocator-requested
heap bytes and clone CPU; inline snapshot bytes are added separately. Shared
stream history remains retained once; these are clone costs, not total run
memory. History index bytes are an upper sum of possibly shared directories.

| Run minute | Heap bytes | Total with 5,480 inline bytes | Allocations | Clone CPU median / p99,µs |
|---|---:|---:|---:|---:|
|1|257,385|262,865|88|12.584 /13.208|
|15|267,583|273,063|139|16.416 /25.792|
|30|259,080|264,560|96|14.958 /26.500|

Minute 30/minute 1 is **1.006448×**, below 2×, with exactly 300 foes/400 gems at
every checkpoint. Effect/projectile phase can vary. The pure separation memo
stays below 1,228,800 payload bytes. Final cleanup leaves zero foes and the
400 original zero-XP gems; no settled record log is retained.

The age/rollback dataset is source `02165bbd087b522bc324ece4f4691cd8e776d6d7`,
before the stateless broad-phase optimization. The only runtime-file diff to
the optimized source is kept as `stateless-broadphase.patch`: no state fields,
clone code or lifetime rules change. The contact-equivalence checks support
reuse of the age measurements; the optimized frame cost is measured above.
The preoptimization background-QoS minute 25 window has 7,699 frames,642 injected
25 ms-late keys,2,231 rollback frames and 459.367 re-simulated song seconds.
Engine CPU p50/p99 is 0.628/12.000 ms; rollback-frame p99 is 12.735 ms,18.241 s CPU
total. These are explicitly preoptimization/lower-priority values, not the
production frame certification. Simulation wall time is 641.363 s under
concurrent checks; full command timing is kept.

## Normal run, matrix and replay

The optimized 41-case release matrix completes in **276.882 s**, below its
unchanged 1,200 s budget. All 20 positioning cases pass. Seven AFK cases survive
too long and remain reports for SW7; pacing is never asserted. Exact commands,
per-case reports and cache-hit stderr are in `matrix/`.

The normal-damage original-order seed 1 run finishes 1,800 run seconds above
**84.583%minimum HP**, level 17 with 623 kills. The release replay component
settles all **31,399,652 records** exactly, hash`1fb4266a6a8eae06`,
diagnostic=False, in 28.773 s. Its wrapper reports 76,791,808 bytes maximum RSS.
A separate direct replay has 50,348,416 bytes physical peak (SF9's direct
baseline was 5,014,343,640 bytes); both raw commands/timings are kept.

`recorded-normal/` keeps final metadata/keys and input provenance. Its
19,063,791-byte original-order stream is byte-identical to the gzip under
`../pre-sh8/recorded-normal-current/`, avoiding a duplicate blob. Copy metadata/
keys to a temporary session, decompress that gzip as `stream.jsonl`, then run
`ps replay <temporary session>` using the matching source. Later deliberate
weapon/offer changes are not an oracle for old recordings.

## Validation and operator checks

Final source aggregate:`scripts/check-fast`,40 s/native 39 s, immediately followed
by its Conventional Commit. Full native validation passes in 254.95 s, including
ignored tests, old runner/renderer bars and the five-weapon density bar.
Unchanged eight-track corpus-parity inputs reuse their kept pass. Fresh full
baker and Python-analysis components also pass. After these checks only
measurement/documentation changes are added; source equivalence is verified
before final delivery.

```sh
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions native/target/release/playlist-survivors --playlist native/playlists/survivors-30.m 3 u --headless --delayed --press-beats --pick-first --seconds 30
```

Session`1793051130` stops normally after 24 run seconds:106 sounds, zero skipped
late/dropped, two speculative cancellations. It takes a real Garlic offer and
resumes on the grid. Muted headless checks do not establish eye/ear/feel
acceptance. File and System commands/capture permission are operator-pending.

Older density runs had max passive ranks without applying their effects;
`pre-sh8/SCOPE.md` and`pre-applied-passive-stats/` correct that scope. They are
diagnostics, not full-passive acceptance. The final setup asserts actual
stats before/after rollback. `source-561-report.md` preserves the earlier
corrected three-weapon phase; it is superseded by this five-weapon integration.
The first cold matrix timeout/profile are also kept: prerequisite`.41` repaired
continuous preparation without changing deadline, input or survival criteria.
Tollgate certifies combined integration; SP5 remeasures final evolved effects.
