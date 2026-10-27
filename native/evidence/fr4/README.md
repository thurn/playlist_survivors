# FR4 — Musical HUD and judged legacy

The compact shared HUD now runs in Survivors: fixed health and Delayed tag,
local health/groove bars, a separate overhead gem and closing/downbeat ring,
beat track/heart, small elapsed timer, XP/level/kills, ranks and owned icons.
Empty inventory slots are faint dots. F4 toggles saved key lights (F1 hint).
Low health pulses on reliable beats; damage flashes white. Foreground HUD is
composed after combat effects. The overhead gem no longer overlaps its bars.

Heard music uses the sim's shared Judging gate and a fresh tick (250 ms), with
an actual known committed beat. Without it the track says WAITING FOR BEAT,
the heart holds grey and the overhead marker breathes. Zen's MUSIC meter reads
causal heard pulse/loudness; it never reads accuracy or changes damage. Legacy
shows earned GROOVE/TIER, ratings and press marks. Bar position keeps the
ordinal of pruned beats. Watch my run reads recorded playback time/grid,
original run beat origin and recorded inputs, independent of current audio.

Legacy now selects Survivors by default, so `--legacy` validates the same run.
Use `--level ../levels/reactive.yaml --legacy` for the reactive reference level.
Actual judging, settling/calibration, Great/Perfect cleaves and beatless fairness
remain shared. Survivors no longer skips the beat's legacy groove drain or
all-weapons tier damage. The sword's tier radius spans100–190 px before rank
bonuses; zen retains its full190 px baseline and full per-hit catalog power.
Shot metadata carries the strike's captured tier. Legacy hitstop/dips/rate
steps work where applicable; zen still has no broad time warps. No minimum-
difficulty retune or survival-gate tightening was made.

## Evidence

- `legacy-tests.txt`: seven focused cases, real catalog hit damage/radius and
  shot identity, judged press/reward/drain, beatless credit without fake ratings,
  existing delayed judged cleave, contact Armour and main-beat rate changes.
- `hud-tests.txt`: known/heard confidence and stale-gap checks; full vs pruned
  beat history70/120/174BPM; replay clock/grid/bar origin. `app-tests.txt` is the
  app suite before final review fixes; final complete aggregate is check-fast.txt.
- Six labelled HUD-only synthetic states at960x540 and1920x1080: quiet, waiting,
  hurt, legacy, cards and end. No natural progression or human feel claim.
- `game-quiet.png` and `game-legacy.png`: actual House128, four seconds, ordinary
  real combat and damage, no injected loadout. The former is zen at1080p.
- `dense/`: existing explicit diagnostic fixture,300 foes/400 gems, final
  evolutions and scripted HP. Three variants (stationary, moving, deaths),
  1280x720. Captured before the diagnostic's upcoming-tick list was added;
  ring/heart/meter/readability use the restored HUD. It does not show natural
  acquisition or survival pressure. JSONL retains actual hits/pickups/deaths.
- `bench-density.txt`: 1080p,300 foes/400 gems,7,200 frames: frame p992.64 ms,
  below the unchanged4 ms gate. This precedes the two timing-only review fixes;
  it is evidence of this HUD layout's cost, not a broad performance guarantee.
- `legacy-watch-headless.txt`: actual File/Delayed/legacy, muted, scripted
  on-beat inputs and Watch my run; fixture shortens the run deadline to6 s.
  Actual victory, replay PCM start and completion with the same23 kills are observed. The helper tests validate
  replay HUD timing; a headless run cannot demonstrate visual feel.

Cold read-only warden review found two P2 issues, both fixed with regressions:
pruned history lost bar position, and the replay HUD inherited live clock/grid.
No confirmation review. Render inspection found the overhead gem crossing the
health bars; they are now separated. System/speakers-on and subjective play
remain operator-pending, not acceptance claims from screenshots.

Playable from native/: `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed` (add `--legacy` for judged Survivors).
