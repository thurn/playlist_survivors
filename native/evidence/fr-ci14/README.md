# FR7 release follow-up: constant density injection

Release01a17eca-a9ed-79f3-ac6d-069d67683f41 failed ci-full-native:
--bench-density --check asserted294 actors vs the required300. All native tests,
runner benchmark(p99 1.217ms) and ordinary1080p benchmark(p99 1.54ms) passed.
The two later release sections were skipped, not separate failures.

The dense fixture requested minimum300 from the production spawner. FR7's
causal quiet/loud scale can make that first refill less than300 (294 in the
kept run). The benchmark intentionally requires a constant300 injected actors.
It now uses a fixture-only population scale1.0 at each music level, retaining
minimum300, the300 cap, and actual quiet/loud travel and presentation. Source
schema correctly rejects an attempted minimum500; that approach was discarded.
Shipped run rosters and feel are unchanged. No performance threshold or actor-count assertion is weakened.
The actual House-stream fixture regression checks300 actors on each displayed
frame after1s, including late inputs and230ms lead.

Focused regression, full local scripts/ci-full, and final scripts/check-fast
outputs are retained here. This is under300 non-test lines and touches no sim
code, so the repository policy requires no cold review. Integrated musical
formation performance remains FR10; this60s fixture isolates dense combat.

Playable from native/: cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed (also --legacy).

Local scripts/ci-full all passed: native ignored tests and8-track parity,
ordinary1080p p99 1.18ms, dense p99 2.61ms/max3.02ms with unchanged4ms bar.
The41-case matrix completed in457.182s with zero active survival failures;
all20 active cases reached1800s, minimum HP share0.9375. AFK remained diagnostic.
Exact30-minute replay passed22,440,197 records/hash5a7c45c98237e3cd in108.244s.
Raw matrix and summary retained; final scripts/check-fast passed11s.
