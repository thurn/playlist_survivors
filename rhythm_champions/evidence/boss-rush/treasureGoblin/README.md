# Boss Rush fight: treasureGoblin

Written by `champions boss review treasureGoblin` (boss-rush.md, Fights). The fight alone in an empty world, a hero
with six weapons wandering the arena; the stills are for judging by eye, nothing here passes or fails.

## Stills

The review (`champions boss review treasureGoblin --motion`, 25 stills and 243 motion frames, 231 MB of PNGs) stays
out of the repository; a fresh review rewrites them here. Kept, as tagged 1280x720 JPEGs:

- [goblin-entrance.jpg](goblin-entrance.jpg): its arrival on the loop across the copse.
- [goblin-bomb-trail.jpg](goblin-bomb-trail.jpg): 15 s in, a bomb filling red behind it as it runs from the hero.
- [goblin-coin-spray.jpg](goblin-coin-spray.jpg): the first coin spray's telegraph at 60%, the coins' launch point on it.
- [goblin-chase.jpg](goblin-chase.jpg) and [goblin-later.jpg](goblin-later.jpg): 35 s and 50 s in.
- [goblin-motion-sheet.jpg](goblin-motion-sheet.jpg): 12 of the motion frames (4 to 12 s), left to right, top to bottom.

The review's hero only wanders, so it rarely corners the goblin: the stills show the trail and the coins, not the ring
of bombs or the panic (the sim tests cover those).

## Notes

Boss 1, the Treasure Goblin, *The Chase* (ps-8z4.6). The design is in
[boss-rush.md](../../../../docs/champions/boss-rush.md): its roster entry has the moves table, and "Arenas, as built" has
`goblinGrove`. The sim is `Assets/Champions/Sim/Rush/TreasureGoblinModule.cs`, and the tests are
`dotnet/Champions.Sim.Tests/Rush/TreasureGoblinTests.cs`.

### Fairness (the framework's harness, in its grove)

`TheGoblinIsFairInItsGrove` plays it at 90, 120 and 180 BPM. Each run is 100 s, the hero wandering the grove's
walkable floor and chasing the goblin on 60% of its picks, with the boss worn down evenly. It finds no problem at
any tempo:

- every telegraph is at least 3 beats and 0.6 s, and lands on a beat or half beat;
- there is a hit-free path for every attack sequence, from every walkable spot;
- dash waves are spaced at least the cooldown and a beat apart;
- every run has at least one dash wave that walking can't escape (the cornered blast sweeps the grove).

The harness's screen is the whole arena: `TheGroveFramesWholeAndItsLoopClearsEveryProp` proves the camera holds the
grove whole, never scrolling, so every hazard in it is on screen.

### The reference builds

[reference-runs.tsv](reference-runs.tsv) comes from `RecordTheReferenceBuildsRuns` (Explicit; about a minute). It
plays the run's first boss (`--boss 1`, the reference build at its slot) with the reference player
(`--walk positioning`) to the 128 BPM dance playlist, at the slot's health of 204. It records each run twice, once
with the hero's 100 health and once immortal. At slot 1 the focused and broad builds are the same build (Sword 1).

- Every run kills it in 100 to 114 s (the act 1 target is about 120).
- It sheds all nine bursts, and the hero gains 2 levels.
- The player takes 3 to 6 hits (38 to 75 damage) and survives every run.
- The test `TheReferenceBuildKillsItInAboutTwoMinutesSheddingEveryBurst` holds the length to 90 to 150 s.

The reference player is a crude walker. It dashes through stabs instead of stepping aside, so its hits are mostly
the dagger and the odd coin. A player who reads the 3-beat line steps out of it. Re-measured against this fight,
its sustained damage is 1.73/s, the stand-in's 1.7 to a tenth, so the slot's health stays 204
([progression](../progression/README.md)).

### Look, sound, variants

- **Body**: the vendor mesh (the designed one from ps-8z4.7 when baked), amber (`enemies.counter.amber`). It has a
  `BossLook` of crystal glints 0.7, with light flowing along its edges at 0.35, 1.6 bands/s. BossEdge's floats only:
  **0 new shader variants**.
- **Clips**: it runs while it flees. The stab plays its StabAttack, the coin spray its SliceAttack and the ring of
  bombs its SpellCast (BossAnimator's fallbacks).
- **Flavour**: amber sparks as its hazards land. The coins glint along their flight, the bombs pop up and round, its
  bursts spray behind it and its death bursts wide. The danger fill stays the telegraphs' red.
- **Sound** (sounds.json `bosses.treasureGoblin`, Universal Sound FX, by name; nothing vendor committed):

  | Role | Sound |
  | --- | --- |
  | entrance | an evil "haha" cackle |
  | tick | coin drops on each telegraph beat |
  | impact | coin sprays |
  | signature | quick small explosions (the bombs) |
  | death | a cash register's cha-ching |

Hand-play and the look in motion are an [operator-pending](../../operator-pending.md) row.
