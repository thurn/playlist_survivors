# SH10 — boss holders, physical chests and roulette

Boss holders keep source HP × player level, speed and contact damage. Their
charge uses the existing beat-slot gait with pattern `x.`: one native two-beat
bar, with 35% of its time moving and the source average speed. A recorded boss
marker adds a landing squash on known accent flashes, without another hit or
damage area. Menu song time does not become a backlog of missed charges.

## Rules and choices

- `docs/survivors/appendix-feel.md`, Boss treasure by minute, is the source:
  independent tier3/2/1 rolls tried in that order; these hold 5/3/1 items.
  Luck is 1 in this demo. No beginner profile override is introduced.
- At most one successful normal treasure attachment per origin minute. That
  origin and tier follow a cap-delayed holder and its recycled replacement.
  Minute 8 XLBAT has no treasure; a later death never borrows another minute's
  treasure. Optional YAML `tier:1|2|3` is the SW4 scripted-tier seam; invalid
  tiers and non-three-entry chance arrays fail validation.
- Touch is 22px body distance. Walking/standing on the drop opens the chest;
  magnet does not pull it. This is an executor interaction choice, not a
  claimed extracted VS radius. No offer or easy knob changes.
- A chest freezes world/run time, retaining song-owned UI timing. Spin starts
  on the next known downbeat; a stable grid gives two bars and eight eighth
  ticks, landing/resuming on the downbeat. Zero/one beat uses finite free
  pulses. Newly committed grid re-locks the spin; a late bound cannot backdate
  an effect before its `knownAt`. After interruptions the final wait snaps to
  a downbeat, while preserving progress. Pause/music reasons remain distinct;
  queued level-ups wait, then keep the world frozen for their own cards.
- Items resolve sequentially: implemented eligible evolution, lowest owned
  weapon, lowest owned passive, heal. Fixed Decision 5 limits evolutions to
  origin minutes 10/15/20/25; every evolution is still disabled until SW5.
  The raw E/W/A prize/profile labels do not override that fixed demo rule.
  Multiple items see prior grants; Hollow Heart applies its real stat delta
  and added-maximum heal; fallback heals clamp to the current maximum.

## Checks and evidence

- `tests-sim-lib.txt`: **87 unit tests pass**, including tier RNG/scripts,
  per-item fallback/evolution enablement, real passive delta/heal caps,
  eight ticks/downbeat, snapshots, beatless/hidden/late grid, stacked pause
  and music re-lock, cap-delayed origin/recycling and boss resume cadence.
- `tests-source-spawn.txt`: **8 contracts pass**, including the curated 31-row
  schedule and invalid treasure configuration. No raw extracted data is kept.
- `tests-live.txt`: **19 live tests pass**. A real boss kill/drop/touch,
  scripted tier 3 and actual house stream settle **8,741 records**, hash
  `db2f10b9efad09e2`, identically at lead 0 and lead 0.23 with keys 25ms late.
  Three key rollbacks reach 0.254s back across the opening; confirm cannot
  dismiss the active roulette; eight ticks and exactly one grant remain.
- `density.txt`: **3.04 ms frame-work p99**, max 4.45 ms, unchanged 4ms p99 bar,
  1080p/120Hz; 300 live foes/400 gems verified for 7,080 frames, all five
  rank 8 base weapons and six applied max passives. Component p99 values are
  engine 2.394/scene 0.600/encode 0.350ms; their quantiles are not additive.
  1,776 rollbacks re-simulate 2.755s. This fixture has no active chest.
- Focused clippy passed. The final `scripts/check-fast` log is kept alongside
  these files; the gate certifies the candidate. No CI or analyzer input
  changes require a local full release sweep for this bead.

### Offscreen capture

`capture/` has four inspected 1280×720 stills and `boss-chest.mp4` (**460
silent frames at 24fps**). A normal-damage boss bat is moved to minute 0,
its XP is 0 to avoid an unrelated offer, and its chest is forced tier 3.
Synthetic 128BPM bars/accents keep the presentation reproducible. Health,
speed, attacks and source contact damage are otherwise normal. Five actual
sequential grants take Sword 1→6; the world resumes after landing. The kept
`sequence.jsonl` reports sampled observation times: open 20.166667, land
observed 22.508333 (sim landing 22.5), then resumed gameplay. This is a
presentation/integration fixture, not survival or pacing evidence.

`capture-initial/` is an earlier failed capture attempt: 24fps sampling missed
the brief ground-chest frame. The harness now takes that still between video
frames, without altering gameplay or video cadence. Those initial stills
precede the corrected bar cadence/resume and are not final evidence.

### Normal headless playlist

`headless-play-result.json` keeps the exact command, exit 0, 94.86s wall time.
The 90-app-second smoke observed the real minute 1 BAT4 at run 68.097s, normal
Garlic/Wand card choices, and a song-gap MUSIC STOPPED/resume at run 57.957s.
478 sounds started, 0 skipped late, 0 dropped. Session 1793059005 is in the
main checkout's ignored sessions. It observed no chest and predates only the
final late-grid guard/alignment on that separate path. `Stopped, 84s` in the
app log is song elapsed since run start (`end_run`), not the pause-subtracted
30-minute run clock. No 30-minute survival, sound-quality or feel claim.

## Commands

From the worktree's `native/`:

```sh
cargo test -p sim --lib
cargo test -p sim --test survivor_spawning
cargo test -p eval --test live -- --nocapture
cargo clippy -p sim -p eval -p app -p players --all-targets -- -D warnings
cargo build --release -p app
target/release/playlist-survivors --treasure-check ../native/evidence/sh10/capture
target/release/playlist-survivors --bench-density --check
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions target/release/playlist-survivors --playlist playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 90
```

Then repository `scripts/check-fast`, Conventional Commit, `tg candidate HEAD`
and `tg approve --wait` from the main checkout. The native bead notes record
source/integrated SHAs and candidate after staging. Source hashes are in
`source.json`. No app window was opened. Operator-only readability, musical
feel and File/System listening checks are in `operator-pending.md`; SH11
owns H's cold milestone review, and SW4/SW5 consume the boss/chest seams.
