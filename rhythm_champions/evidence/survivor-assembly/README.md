# Survivor assembly — ps-3wc.13

2026-11-03, jarvis. Actual deterministic simulation with current level/content,
ordinary spawning, structures, dropped XP, real choices and boss targeting.
No injected XP, damage, invulnerability or pre-equipped build in the full runs.
These are scripted input experiments, not claims of manual fun or balance approval.

## Reproduce

```sh
dotnet test rhythm_champions/dotnet/Champions.Sim.Tests --filter FullyQualifiedName~PrintSurvivorRuns --logger 'console;verbosity=normal'
dotnet test rhythm_champions/dotnet/Champions.Sim.Tests --filter FullyQualifiedName~SurvivorAssemblyTests
```

`matrix.csv` records all 24 runs: focused/broad/defensive × seeds 31/32 ×
silence/80/128/174 BPM. 128 is a playlist with tempo jitter, loud/quiet sections
and four-second silent gaps; 80/174 are constant synthetic grids. Every profile
uses `--walk positioning`: it routes through visible drops, reacts to imminent
contact/elite/projectile threats with movement dashes, and crosses traveling
boss waves. It does **not** deliberately dash to horde beats. Incidental timed
judgements can still empower weapons; invulnerability does not depend on them.
No tested successful build owns Landing Blast. No live audio or speakers are used.

Profiles are committed in `LevelRunTests.ConfigureBuild`. Focused prioritizes
Sword, Might and recovery before speed/area; broad acquires six distinct
weapons before deep levels; defensive prioritizes Armor/Regeneration after
Sword. Offers remain the real deterministic three-card pool, so preferences
cannot force an unavailable item. This is a fixed routine, not an adaptive
player or proof that every arbitrary build/seed is viable.

## Findings and tuning

- **13/24 victories**, with wins for all three profiles and all four tempo
  cases. The eleven deaths remain in the table; seeds are not claimed universally
  winnable by this routine. The broad profile wins at 80 BPM and loses the
  other sampled cases; wider human build viability remains a hand-play check.
- Successful runs earn **27–30 normal levels** (the matrix column counts XP
  thresholds, not automatically every completed menu). Regression cases count
  actual normal selections, check the 25–35 target, and distinguish boss picks
  and capped-item reward fallbacks. Final-tick XP may remain unpicked at victory.
- First choices range **18.8–57.1 s**, median 35.9 s across the eight
  seed/tempo starts. Silence seed 31 is 27.9 s; the 128 BPM seed 32 baseline is
  26.1 s. The 20–30 s target is **not met by every route**: seed 31 at 80/128
  and seed 32 at 80/174 can leave XP behind. This is explicitly pending human
  routing/taste tuning, not hidden by synthetic collection samples.
- Boss calls stay at **300/600/900 horde seconds**. The retained field, ordinary
  spawn stop, exact clock freeze, arrival deadline, healing and owned rewards
  are covered by `SurvivorBossTests`; full runs add normal damage to victory.
  Successful combat spans roughly 17.6–22.3 minutes. Broad builds kill faster;
  focused/defensive fights show the intended longer sustained pressure. Some
  sampled Dragon fights exceed the three-minute target (see the CSV).
- Idle with automatic first-card picks naturally dies at **260.5 s in silence**
  and **264.7 s on the playlist**, to elite/contact pressure. Starting
  auto-weapons can clear the opening while stationary. The old plan's
  idle-within-90s expectation was inherited from retired dash combat and is
  not a current design decision; it is no longer asserted against this game.
  Death replay and a new run on the continuing stream are tested in .NET.
  Actual end-card Again/focus behavior remains a windowed operator check.

Current tuning is deliberately explicit: 150→600 spawn ramp remains intact;
opening toughness is 0.65 through 90 s, rising to 1 at 180 and 3.5 at 720.
This avoids the starter's fast-grid fractional damage failing the opening
one-hit threshold. Contact costs 3 health; occasional 10% healing every 100
kills and baseline 3 m attraction remain. XP needs 85 for the introductory
choice, then 20 growing 18% per level, allowing a quick second decision before
the growing costs spread later choices. Boss health is 150/350/650. No level-up
healing, Break, offensive dash requirement, quiet wait or entry wipe was added.
These are starting balance numbers, not operator approval.

## Replay and presentation coverage

Session v6 records an optional typed survivor preset, plus the survivor-mode
flag in both binary inputs and readable metadata. v1–5 remain readable. Presets
reject invalid levels/IDs, duplicate items and overfull slots through the same
build rules as gameplay. Empty presets still start with Sword 1.

- `fixture-attacks`: six weapons with levels spanning forms 3/5/8 and six
  supports including Split/Echo/Persistence; the alternate live case covers
  Orbit/Sentinel persistence. Recorded stream includes a silence gap, tempo
  change, walking, two dashes, pause/resume and quit. Shared hit/proc tests
  continue to enforce lineage bounds and support inheritance.
- `fixture-duel`: survivor Sword/Orbit/Bow and Might/Tempo/Armor against the
  current retained-horde, always-damageable Ogre; arrival clears zero dummies,
  no Break/stagger. Inputs cross its traveling wave and replay to the same hash.
- `fixture-level`: first 75 s of the current level from Sword 1 and real
  collectible XP. `fixture-walk` stays a generic input/clock compatibility
  fixture, without retired combat assertions.
- `SurvivorAssemblyTests`: full input/stream replays to the final hash for
  focused silence/128, broad 80 and defensive 174 victories; death and a fresh
  retry on music/silence. The 128 seed-32 focused case caps its owned reward
  pool and correctly takes max health instead of an empty second boss menu.

The family runtime galleries already inspect each form/empowerment in crowd
motion samples: [melee](../melee-weapons/README.md), [ranged](../spear-bow/README.md),
[constructs](../construct-weapons/README.md), [magic](../magic-weapons/README.md).
[Boss previews](../survivor-bosses/README.md) include retained mobs, structures
and six weapons. Full moving-build effect competition, visual cost and red/yellow
threat priority are the next ps-3wc.14 acceptance; numerical run results do not
approve those visuals. No vendor-derived captures or meshes are committed here.

## Scope and remaining checks

The shipped form table in design.md now describes actual spectral actions and
verified knight poses. Conductor uses marked alternating fan lanes and a
traveling wall, with no summons/renewable XP; it does not promise an unverified
projectile animation. Audio/rendering foundations and `--look trailer` remain.
The independent ps-6y2 work is neither gated nor repurposed.

See operator-pending for actual hand-play choice pace, recovery/pressure,
first-choice routes, speaker timing, complete-build visual judgement and target
hardware. ps-3wc.14 covers load/readability; ps-3wc.15 provides the single final
Warden. Local validation and delivered SHA are recorded on the bead.

Local validation: fast checks passed with 446 sim tests and 161 tools tests;
a further v5 compatibility regression and the updated v6 roundtrip passed.
Unity EditMode is 119/119, no skips; Mono development build succeeded with
279/300 compiled variants. The Mono player replayed all four fixtures to
their .NET hashes with **no presenter faults**. Private logs and sparse preview
frames are in `~/Library/Caches/playlist-survivors/evidence/ps-3wc.13/`.
The 2.05 s six-weapon fixture frame was inspected: white arrows and dim
faceted field strokes remain separate over dark grid, with the hero dominant.
The invisible type-zero collision dummies mean this sparse frame does not
establish crowd or boss-threat visibility; that limitation is handed to .14.
