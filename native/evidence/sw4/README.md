# SW4 — the four late bosses

Base `12d4e39b75d03dc4a9b83b9fc62fa21eddf3cb67`. Native bead notes record
source/integrated delivery SHAs. No baker/asset changes or raw VS data.

## Source and choices

The four scheduled roles already had source health, speed, contact power,
XP and resistances from the curated source appendix. Those numbers and their
schedule minutes are unchanged. SH10 already gives every scheduled holder
source HP × player level, a two-native-beat bar charge (`Beat/x.`, 35% of the
bar moving), contact-only damage, origin-owned treasure and an accent-only
landing squash. SW4 reuses that implementation without another damage area.

| Minute | Role | Art / authored tint | Source HP × player level | Speed | Contact power | XP | Scripted tier |
|---:|---|---|---:|---:|---:|---:|---:|
| 10 | BOSS_XLMANTIS | minotaur ×2 / copper | 15×L | 160 | 20 | 50 | 1 |
| 15 | BOSS_WEREWOLF | orc-elite ×2 / crimson | 20×L | 130 | 20 | 30 | 1 |
| 20 | BOSS_XLMUMMY | brute ×3 / ochre | 25×L | 80 | 20 | 25 | 1 |
| 25 | BOSS_XLFLOWER | brute ×3 / violet | 15×L | 160 | 30 | 50 | 3 |

Raw speed converts by the existing VS/hero ratio and contact power uses the
unchanged 0.5 level multiplier at the actual bite. Tint is an executor art
choice, with the minotaur/brute/orc-elite variants requested by the plan.
Brute art has the existing 0.5 atlas normalization, so its rendered Look scale
is 1.5; the YAML scale remains 3. Collision radii and death recipes are kept.
All four walk cycles use two beats and explicit matching bar gaits. Frozen
actors still bob, and SH10 reanchors charges after menus without backlog.

`levels/survivors.yaml` forces exactly tier 1 at 10/15/20 and tier 3 at 25:
one item for each first evolution window, five for the last window. This
chooses the allowed minimum for the first three to preserve the expected
one-at-a-time evolution path. Original chance rows remain for provenance;
scripted tiers bypass RNG rolls. Tier 3 contains five sequential items, not
three. SW5 owns the actual evolution logic/grants; no offer/easy knob retune.
SP2 owns entrances, warnings and later death/chest showpieces.

## Checks

- `tests-boss-holders.txt`: 12 actual private Sim spawn/drop cases, four
  minutes × player levels 1/7/35. None spawns before its minute; one holder
  spawns at the boundary. HP is [15,20,25,15]×L, and lethal production damage
  drops exactly one physical chest with origin minute and tier 1/1/1/3.
  Repeated lethal delivery/admission cannot duplicate it. At level 35 the HP
  values are 525/700/875/525. The fixture isolates regular population and
  scheduled admission; it does not simulate walking through all 25 minutes.
- `tests-source-schedule.txt`: all 8 schedule/source contracts pass, including
  all 31 rows and 20 enemy HP/speed/damage/XP/scaling contracts against the
  curated appendix, deferred/cap timing and treasure schema validation.
- `tests-roster-art.txt`: all 12 base/boss art mappings, scales, styles,
  walk/gait cycles and real baked directional walk/hurt frames checked.
- Focused clippy and final repository `scripts/check-fast` logs are kept.
  This change touches no CI, analyzer, workspace config or shared test
  infrastructure, so no local full release sweep is required.

## Offscreen captures

`capture/` has four inspected 1280×720 stills and four 640×360 silent clips,
each 52 frames at 24fps (2.166667s), from the actual Engine/Scene. `capture.txt`
and `capture-media.json` keep exact windows, dimensions, frame counts/hashes.

The existing roster diagnostic gains an optional `PS_ROSTER_ROLES` selector;
its default eight base roles remain. A selected boss is scheduled at minute
0 with player level 1 and source HP/XP retained, so SH10's real holder/bar gait
and marker run. Synthetic 128BPM bar accents exercise the landing squash.
Hero damage is exempt, Might and Area are 0, scenery props are suppressed.
These are labelled presentation fixtures, not survival, musical fit or
late-minute density evidence. Zero damage/area can still leave diagnostic
cosmetic pulses; the clips do not claim the player has no visual effects.
No app window or audio output opens. Operator-only feel/readability checks
are a row in `native/evidence/operator-pending.md`.

The final normal File/Delayed headless smoke (`headless-play*.{txt,json}`)
ran 30 app seconds, exit 0 in 36.173s wall, session 1793062590 in the main
checkout's ignored logs. It picked Garlic, resumed, and started 106 sounds,
zero late skips/drops. `Stopped,24s` is song elapsed, not the pause-subtracted
run clock. This early smoke proves the app stays playable; the isolated
boundary/drop tests and captures cover the new late bosses.

## Reproduction

From `native/` after building in this worktree:

```sh
cargo test -p sim --lib big_four_spawn -- --nocapture
cargo test -p sim --test survivor_spawning
cargo test -p app roster::tests -- --nocapture
cargo clippy -p sim -p app --all-targets -- -D warnings
cargo build --release -p app
PS_ROSTER_ROLES=vs-boss-xlmantis,vs-boss-werewolf,vs-boss-xlmummy,vs-boss-xlflower target/release/playlist-survivors --roster-check ../native/evidence/sw4/capture
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions target/release/playlist-survivors --playlist playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30
```

Then `scripts/check-fast` from the repository root, immediate Conventional
Commit, `tg candidate HEAD`, and main-checkout `tg approve --wait` to staging.
SW7 owns the one W milestone cold review; no rollback runner is changed.
