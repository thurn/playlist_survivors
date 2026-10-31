# Garlic and Fire Trail (ps-msz.6.7)

The aura and the burning wake (game/Assets/Game/Sim/Weapons/Garlic.cs and FireTrail.cs, their looks
in game/Assets/Game/Client/Weapons/; unity-game.md's The Other Weapons).

## Filmstrips

Windowless fixed-time stills of the Mac player (Mono) in the run's level (`--run`) on house-128
(0.47 s a beat), the hero holding one weapon at rank 8 (`--arsenal`), in the run's opening zone
before the music moves it on to blossom at 6.6 s:

```sh
uv run --project demo3d/tools game look garlic --backend mono --evidence weapons \
  --at 4.95,5.19,5.23,5.35,5.66,5.9 --strip 3 --crop 560,190,800,700 \
  --player-args "--run --file $PWD/public/reactive/house-128/house-128.ogg --arsenal garlic:8"
uv run --project demo3d/tools game look fire-trail --backend mono --evidence weapons \
  --at 5.0,5.62,5.75,5.95,6.6,6.9 --strip 3 --crop 460,140,1000,800 \
  --player-args "--run --file $PWD/public/reactive/house-128/house-128.ogg --arsenal fire-trail:8 --walk right@4.4-5.8,up@5.8-7"
```

- [garlic.jpg](garlic.jpg): Garlic's mint aura at exactly the radius the sim hits (3.66 m at rank
  8), white-hot at its rim. Its incarnations come every 1 s of run clock on the beat: 3.78, 5.18,
  6.12 s. At 4.95 s it breathes between pulses. At the 5.18 s pulse (5.19, 5.23) the rim swells
  out and flares, and motes lift off its edge. It settles by 5.35, and kicks again on the beat at
  5.65 (5.66).
- [fire-trail.jpg](fire-trail.jpg): the hero walks right from 4.4 s, then up from 5.8 s. Each
  pulse burns the tiles it stood on during the last 1.5 s of run clock (rank 8), skipping the
  latest 0.12 s. Each burn is a disc (1.19 m at rank 8, overlapping its neighbours) in the
  weapon's orange with a white-hot core, and embers rise at the pulse. The wake follows the
  hero's tiles around the corner (6.6 s), and the oldest tiles go out as they age past the
  duration (6.9 s).

The purple ring of dashes is the arena's rune circle at the start tile, not a weapon.

On the bright blossom zone (from 6.6 s on house-128) the additive aura reads paler, nearly
white at the rim. The run's zones are mostly night, the reference look.

## The tests

- game/Assets/Game/Tests/Sim/Weapons/GarlicFireTrailTests.cs (.NET and EditMode), against
  native's engine/weapons.rs and combat.rs and weapons.yaml:
  - Garlic pulses on the beat (rank 1's 1.3 s interval at 0, 1.5 and 3.0 s at 120 BPM; rank 8's
    1 s at 0, 1, 2 and 3).
  - It hits what it touches once a pulse for the rank's power (0.5 to 1.5), reaches 0.32 VS
    units (1.83 m) times Area and no farther, and hits an enemy that walks in at the first tick's
    end, then not again until the next incarnation. It rests while frozen.
  - Fire Trail pulses every 0.5 s with no beat. Its burns at each pulse are exactly the tiles the
    hero stood on within its duration and past its skip, newest first, at 20 px (0.74 m) times
    Area.
  - An enemy in the wake takes 0.4 once a pulse until its tile expires. A standing hero burns its
    own tile from the second pulse, and rank 8 hits for 1.0 at 1.6x the radius. A freeze doesn't
    age the wake.
- game/Assets/Game/Tests/EditMode/EffectsSoundTests.cs: each weapon's pulse and hit sound at its
  event's song time in its own category (`garlic-pulse`, `garlic-hit`, `fire-trail-pulse`,
  `fire-trail-hit`), never the sword's hit.

# Magic Wand and King Bible: crystal bolts and orbiting shards on the grid (ps-msz.6.5)

The survivors test playlist (house-128 first, 0.47 s a beat) on the Mac Mono player, the slice's
level, the hero starting with Magic Wand and King Bible at rank 8 (`--arsenal`) and no sword, so
every hit is theirs.

## Filmstrips

Windowless fixed-time stills (`game look`):

```sh
P="$PWD/native/playlists/survivors-30.m3u"
uv run --project demo3d/tools game look wand-hits --build --backend mono --evidence weapons/wand \
  --at 5.65,5.7675,5.885,6.0025,6.12,6.2375 --strip 3 --crop 560,280,900,800 \
  --player-args "--file $P --arsenal magic-wand:8,king-bible:8"
uv run --project demo3d/tools game look bible-hits --backend mono --evidence weapons/bible \
  --at 11.20,11.28,11.48,11.72,12.20,12.42 --strip 3 --crop 460,90,1000,800 \
  --player-args "--file $P --arsenal magic-wand:8,king-bible:8"
```

- [wand/wand-hits.jpg](wand/wand-hits.jpg), on the beat at 5.65 s and each sixteenth after it to
  6.24 s: violet bolts streaming from the hero to the red horde coming in from below, one landing
  on each sixteenth (the hits at 5.7675, 5.885, 6.0025 and 6.12 s in the event log), while the
  four gold shards of the ring born on the run's first beat (3.78 s) circle the hero, leaning into
  their turn with a streak of light behind each. Its quarter turns fall on the beats: snapped from
  2.19 x 1.6 rad/s to 3.34 rad/s, a quarter turn a beat.
- [bible/bible-hits.jpg](bible/bible-hits.jpg), 11.20 to 12.42 s, in the first zone's warm
  palette: the ring born at 10.81 s sweeps through the violet bat swarm above the hero (its
  shards' hits at 11.275 and 11.725 s), while bolts
  burst in prism sparks on the horde below. Each hit throws the effects' `prism-hit` in its
  weapon's colour over the white chips every hit has.

## On the grid

A 60 s headless run of the same (real time, `--headless --seconds 60 --events-out`), measured by
[grid.py](grid.py) against its committed beats;
[headless-weapons.csv](headless-weapons.csv) keeps its first 14 s of beats, the two weapons' shots
(`Attack`), bolts' legs (`Projectile`: `i0` the bolt, `i1` its target, `i2` phase | weapon << 4),
rings (`Orbit`: `i0` the ring, `i1` blades, `i2` snapped | weapon << 4) and hits (`Hit`, `i2` the
weapon: 4 the wand, 6 the bible).

| what | count | on the grid |
| --- | --- | --- |
| Magic Wand shots | 56 | 55 (98.2%) on eighths |
| Magic Wand bolts (a burst's 0.1 s apart, as native's) | 222 | 55 (24.8%) on sixteenths |
| Magic Wand hits | 1352 | 1291 (95.5%) on sixteenths |
| King Bible rings | 8 | 8 (100.0%) on beats |

- The one shot off its eighth (50.41 s) was planned on the latest beat's grid carried on at its
  period while the next beat was not yet committed; the next came a little early.
- A shot's first bolt is on its point; the burst's later ones follow 0.1 s apart, native's repeat
  over the speed. Only the shot sounds (`wand-fire`, on its eighth); every bolt's hit sounds.
- Hits off the sixteenths are flights longer than the grid then known ahead (the latest beat's
  continuation, 1.5 beats): they arrive after their straight flight, as native's would.
- All 8 rings snapped (rank 8's four blades at speed 1.6 are within 15% of a quarter turn a beat
  at 128 BPM); one was cut short by a level-up's freeze (13.03 s) and announced again.
- The run: 1497 killed by 60 s, 2023 sounds scheduled, 16 dropped.

## The tests

- game/Assets/Game/Tests/Sim/Weapons/MagicWandTests.cs: shots on eighths and bursts spaced by
  native's repeat; a bolt at the nearest enemy, arriving on the first sixteenth after its flight
  for native's power x Might at ranks 1, 5 and 8; kills costing no pierce; pierce 2; turning when
  its enemy dies in flight; an aimless bolt; a freeze; the same run's hash; and nothing allocated
  while stepping 50 s with both weapons.
- game/Assets/Game/Tests/Sim/Weapons/KingBibleTests.cs: native's orbit lattice table (its own
  test's cases and boundaries); a ring's blades, radius, duration, speed and power by rank; a
  blade's contact on the tick its sweep touches and its knock along the way; a second lap's hit
  once the memory clears; the blades on quarter turns on every beat of a snapped ring; a freeze.
- game/Assets/Game/Tests/EditMode/EffectsSoundTests.cs: the wand's shot and hits and the ring's
  birth (not its cut) and hits scheduled at their times; a weapon's spark cue at its own frame.

Hearing the weapons' sounds against the music is an operator check
([operator-pending.md](../operator-pending.md)).
