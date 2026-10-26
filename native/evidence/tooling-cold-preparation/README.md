# Cold Survivors preparation — hv-n7fn.41

The first SF10 full release matrix completed 31 cached-track cases but timed out
while preparing continuous analysis. Its sample was in the offline hindsight
ensemble, not the sim. The original 20-minute deadline is retained.

The continuous cache builder now uses four model jobs, bounded independently
of track jobs. Models have independent scratch and read-only weights. Scoped
threads join before returning (also on unwind), propagate panics, and reduce
outputs in the original model order. Live and per-track constructors retain
one model job. No simulated timing or known-at stamps change.

Replay types/implementation move to `eval/src/session/replay.rs`; the public
session API is unchanged. The cache still fingerprints `session.rs`'s entry
conversion, analyzer modules, source decoding, Cargo.lock, model bytes and
playlist contents/order/gaps. Replay implementation is no longer an analyzer
input. No cache is manually rekeyed or copied to another key.

Validation and timings are recorded below after completion. Upstream stage
checks use the kept madmom features/activations/beats, plus ordered inline
comparisons at two/four jobs. A real PCM analyzer comparison uses live D=0.67.
The short gate test covers empty/truncated windows and job count bounds; it
is not an upstream activation assertion for the truncated window.

Cold smoke (background QoS, alongside corpus parity): 145.992086 s audio,
56.327067 s preparation, 100.14 s user CPU, 189,104,656 B peak footprint.
The six-second normal-damage simulation completes its requested span. This is
a pipeline smoke test, not a survival balance or full-budget certification.
The final-source full release matrix is measured separately at its existing
utility QoS. The smoke predates only a must-use annotation and test naming.

Three-track analyzer corpus parity (`cargo run --release -p cli --
analysis-parity --out /tmp/sf41-parity --check`) passes: 0.7820 Rust/Python
accuracy within 45 ms, 0.7797 with the live delay. No track is outside the
one-point margin. This checks the unchanged default live/inline path; the
new parallel path is compared directly with upstream full-window outputs
and with actual PCM arrivals at nonzero D in the focused tests.

Playable app command (normal production priority; no window):

```sh
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions native/target/release/playlist-survivors --playlist native/playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30
```

Session `1793043886`: 24 run seconds in the 30-second app check; 97 sounds,
zero skipped late/dropped. Headless render pacing is not an interactive
presentation acceptance claim. This tooling change adds no operator-only
checks; the existing milestone presentation rows still apply.

`parallel-continuous-profile.txt` is a one-second sample of the actual cold
continuous preparation. It shows the caller joining scoped workers and
separate model-activation stacks. It supports the concurrency description;
it is not a throughput benchmark by itself. The full matrix timer below
includes initial compilation, cold per-track and continuous preparation,
and every selected simulation.

## Full release matrix

`/usr/bin/time -l scripts/ci-full survivors` exits 0 on the final source:
**41 cases in 670.812988 s** (671.72 s outer wall), including compilation,
five cold track preparations (warmup 159.32 s), and cold continuous preparation
**576.549143 s**. The first continuous simulation then takes 25.078266 s.
All ten continuous cases finish using that actual full-source cache. The
existing deadline is 1200 s; neither it nor the input/survival criteria changed.

All 20 positioning cases finish above 25% HP. Six AFK cases survive too long;
these are kept report-only for SW7, not treated as tooling failures or tuned
here. Pacing remains report-only. Case reports, exact commands and cache-hit/
miss stderr are kept in `matrix/`; the normal session stays in the run folder
shown in the raw log. This source is based on SH5, so this is current-budget
evidence, not a controlled speedup ratio against the earlier Garlic failure.

The required three-track analyzer parity, upstream full-window 2/4-job tests,
actual PCM comparison with nonzero D, replay/session tests, cache invalidation
tests, clippy and headless app check pass. No CI policy or shared test runner
changes were needed in this repair. Final aggregate validation is recorded in
the native bead notes. Review follows the Survivors milestone policy: this
bounded tooling prerequisite does not add a second F confirmation review.

The final aggregate passes in 110 s (native 109 s), including cold app/test
compilation in this independent worktree. That exceeds the stated 60 s warm
budget; no repeat was run solely to produce a faster warm number. The focused
checks and the aggregate are kept, and Tollgate still certifies the candidate.
