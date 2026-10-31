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
