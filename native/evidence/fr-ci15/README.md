# FR8 follow-up — Cancelled signature deadlines

Release01a17eeb-6cad-7723-888f-8efcc43cb395 passed native tests/benchmarks but
its full survival matrix hit the20-minute budget. The replay step was skipped,
not an independent failure. Cached original positioning seed4 and continuous
original positioning seed5 kept simulating one abandoned signature deadline;
a retained sample shows Engine.run/Sim.advance_to and4.5GB process footprint.

When the early minotaur died with a pending slam, or reached retreat time before
that slam, tick_signature returned without cancelling the deadline. Once reached,
signature_stop kept selecting the current clock. Death now cancels descent and
slam deadlines; retreat cancels the slam and its marks. One-time boss identity,
mainbeat travel, damage, HP, retreat timing and run pressure remain unchanged.

before.txt fails against the old source using an actual catalog boss and an
actually scheduled marker. after.txt covers both death and retreat and advancing
past the cancelled deadline. The two exact long-run commands are kept below.
No CI budget, survival assertion or feel behavior was relaxed.

Playable from native/: `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed` (also `--legacy`).

Both exact30-minute cases now finish normally: cached original positioning
seed4 completes1800s, minimum HP1.0,22,203,956 records/hashf9e6625092c353c4.
Continuous original positioning seed5 also completes1800s (summary.json and raw
reports keep exact values). Cached run used the normal background batch class;
continuous used release-style utility QoS with PS_NATIVE_BATCH=1. Final
check-fast passes23s. One cold warden review found no actionable issues; the
regression was strengthened to advance beyond the captured cancelled deadline.
Full combined qualification follows in FR9/FR10; no confirmation review.
