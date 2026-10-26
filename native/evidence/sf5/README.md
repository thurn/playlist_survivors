# SF5 — weapon tables, deadlines and sword cadence

The 118-line curated table in `content/survivors/weapons.yaml` contains the ten
base/evolved definitions and all 45 combat ranks from SF1's feel appendix.
Rank 1 is full; later numbers add and other fields replace. It is a separate,
immutable compiled table, so editing files cannot change a running sim. The
prototype content stays independent; no extracted game files or assets enter
the repository. Only the sword's implementation flag is enabled; future weapon
beads enable their delivery as it lands. Six weapon/passive slots are fixed
arrays, with owned upgrades consuming no additional slot.

## Timing and behavior

Cooldown deadlines advance from the previous **ready** time, never the previous
fire time. Snapping uses only committed bounds at the current sim time. Shared
steps stagger interval shots on sixteenths; without grid ahead they use the raw
interval. Ready times and pending interval shots survive freezes. Song deadlines
use the accumulated frozen-time anchor, avoiding frame-cut subtraction drift.
Existing deadlines keep their remaining interval on an upgrade; subsequent
intervals read current stats at the preceding ready event.

The sword is beat cadence, replacing Auto/light/heavy/cleave/groove-scaled
attacks in this mode. A beat makes `Whip amount + Amount` equally spaced spins,
each dealing `Whip power × Might × period / 1.35`, radius `80 × weapon Area ×
character Area`. A known bounding beat gives the period, otherwise the committed
beat's period does; no beat uses the 0.35 s free pulse. Sword subdivisions are
regenerated at resume, like SF3's Auto cadence, so old song subdivisions cannot
become a catch-up burst. Interval weapons retain their ready deadlines.

Shot records carry finalized damage and delivery parameters once (area, speed,
duration, pierce, hit delay, knockback and crit fields) for later handlers.
Forecast accents have their own flag and heavier existing ring/sound treatment;
they change no damage. Whip's raw 0.2 crit field remains disabled, as sourced.
H1 can consume inventory snapshots. Reactive and legacy retain their rules.

## Kept measurements

From `native/`:

- `cargo test -p sim --test survivor_weapons -- --nocapture`: 45 ranks / 450
  numeric fields compared with the sourced appendix; numeric/boolean merging,
  validation, Holy Wand 0.3 s at Tome 5, Bible's duration+interval and crit flags.
- `cargo test -p sim survivors::arsenal -- --nocapture`: five tests. At 70,
  128 and 174 BPM, 600 s of Garlic 1.3/1.0/0.6 s and Holy Wand 0.3 s produce
  throughput within **0.1167%** of VS. Every shot is at/after ready and less than
  a main grid step late. The collision test requires actual sixteenth staggering.
  Raw fallback is exact. A 19 s freeze adds no cooldown debt. Six slots are bounded.
- `cargo test -p sim survivor_sword_actual -- --nocapture`: actual hits on a
  stationary, knockback-immune high-HP target over 60 s at all three tempos:

  | Case | Hits at 70 / 128 / 174 BPM | Damage / 60 s | Expected |
  |---|---|---:|---:|
  | Rank 1, base stats | 70 / 128 / 174 | 44.444444444 | 44.444444444 |
  | Rank 8, Might 1.5, Amount +2 | 280 / 512 / 696 | 1066.666666667 | 1066.666666667 |

  The second case also sets Area 1.5 (144 px radius) and Cooldown 0.6 to test
  non-default stats; this is a controlled formula test, not the demo's progression.
  Base radius is 80 px. The dummy cannot hurt the hero or leave the hitbox.
- `cargo test -p sim survivor_accent -- --nocapture`: accent flags appear and
  actual target damage is unchanged; old swings and cleaves are absent.
- `cargo test -p sim survivor_interval_framework -- --nocapture`: identical
  full recordings and arsenal state, batch versus streaming, 37 ms frame cuts,
  non-default 0.3 s interval, through a pause/resume.
- `cargo test -p sim --test survivors -- --nocapture`: all 15 freeze/clock cases.
- `cargo test -p eval --test live late_confirm -- --nocapture`: all 3,790 settled
  records/hash `0b40bb6972baeaff` match, two key rollbacks, deepest 420 ms.
- `cargo test -p eval --test session -- --nocapture`: all three pass, including
  both stored legacy session hashes and rejection of unknown keys.
- `cargo clippy -p sim -p eval -p app --all-targets -- -D warnings`: clean.
- `taskpolicy -b cargo test -p sim --test causality -- --ignored`: full existing
  every-cut causality sweep passes, 69.00 s, background QoS.
- Final root `scripts/check-fast`: raw output in `check-fast.txt`.

These are weapon formula checks, not pacing or survival measurements. No
analyzer or CI/infrastructure changes require corpus parity or local ci-full.
SF5 joins SF10's milestone review; the eval edits add records and recorder/view
handling, with no rollback runner algorithm change.

## Headless play and rendering

```
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions \
  target/release/playlist-survivors --file ../public/reactive/house-128/house-128.ogg \
  --delayed --headless --seconds 20
target/release/ps replay /Users/dthurn/playlist_survivors/native/logs/sessions/1792997022
```

Live session 1792997022: 14.2 game seconds, 12 kills, full HP; **21,149** settled
records match final-build replay (24,218 total replay records including tail).
Four live stream-arrival rollbacks, no late keys or dropped/late sounds. This is
still the temporary director/HP roster until SF6, not a balance claim.

A separate offscreen render (the `--still` snapshot path, not a live recording):

```
target/release/playlist-survivors --file ../public/reactive/house-128/house-128.ogg \
  --delayed --headless --seconds 20 --still evidence/sf5/still.png
```

[still.png](still.png) was inspected: the hero's circular spin is visible,
with 62 enemies, 1,688 sprites and five lights. Neither check opened a window.
The initial recipes are reused; SH3 owns trail/afterimage/radius polish, and
operator eye/ear feel remains in operator-pending.
