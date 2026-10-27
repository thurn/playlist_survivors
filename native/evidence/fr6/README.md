# FR6 — Collection comfort and resolved upgrades

The starting attraction radius is120px (previously46.280992px), multiplied by
actual Magnet ranks; the existing15.426997px gem body overlap, accelerated pull,
gem colors, pentatonic pickup chain and XP curve/drop values remain. A faint
world-space collection ring follows the actual published radius. At the400
resting-gem cap, overflow merges into the gem nearest the death, with stable
first-entry ties and unchanged total XP, rather than into the farthest gem.
The cap continues to mean resting field gems; attracted gems retain their
existing bounded-duration collection lifecycle.

A resolved card or chest marks one pending celebration. Chained cards keep the
world held and coalesce into one pulse at the final resume; pauses/music holds
cannot spend it. The rollbackable run record supplies its actual origin/time,
collection radius and identity. One level-up sound follows that resume record.
The visual reads held world age, so it retracts/rewinds with the real view;
no frame-local speculative effect history is accumulated.

The clearing pulse reaches160px, moves ordinary foes by at most48px over120ms
as explicitly tagged recoil, cancels their current travel/attack and leaves no
chase velocity. Bosses and authored formation members are protected. It deals
no damage, changes no health and grants no immunity. The existing120ms damage
lockout and food/recovery rules are unchanged. Player menu keys still require
release; existing beat/free resume and weapon debt handling remain shared.

## Evidence

- collection-tests.txt: actual Magnet radius, bounded current-pose recoil and
  boss/formation exclusion, no HP/immunity change, three real card choices,
  one final-resume pulse, identical snapshot replay.
- pickup-tests.txt: actual120px+body boundary, outward kick then accelerated
  attraction, musical arrival, Magnet modifier, fractional freeze and food.
- merge-tests.txt:400 retained IDs, near-death overflow and conserved XP/levels.
- focused-all.txt: complete sim/eval/app suites, including actual XP-funded
  offer choices with30ms-late keys/lead400ms; identical inventory, recap and
  records. Resumed pulse identities each have exactly one actual sound record.
- Existing zero-XP death/recoil and health/food tests pass. No fake gems were
  introduced. The standalone pickup capture adds a labelled synthetic pulse
  over the actual minute-one view; it is a visual check, not a survival claim.

Subjective collection/upgrade feel and speaker/System audition remain operator-
pending for integrated qualification. Playable from native/:
`cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`
(add `--legacy` for judged Survivors).

Visually inspected stills/ at1280x720 using the Rust three-minute timestamp
fixture (actual simulation, silent, not a listening export): stationary minute1 (HP1.0,
level1,184 kills,102 gems), separately injected180ms upgrade pulse, and
labelled synthetic400-gem layout outside actual attraction/body reach. The
stationary scene did not acquire a level; the upgrade image is explicitly
presentation-only. Real choice/resume behavior is exercised by the tests.

Cold review found one confirmed same-grid edge: another held XP arrival could
reopen a card immediately after a provisional resume, after the pulse had
already fired. The pending celebration now settles after due events, before
new combat. A two-arrival regression fails before the fix and passes after;
nested pauses also retain the pending celebration. No confirmation review.
