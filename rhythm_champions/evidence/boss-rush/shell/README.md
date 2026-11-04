# Boss Rush mode shell (ps-8z4.1)

The mode's first playable milestone ([boss-rush.md, The shell, as built](../../../../docs/champions/boss-rush.md#the-shell-as-built-ps-8z41)):
the 25 slots in order, each fought by its main-game stand-in until its fight lands.

## Sim (dotnet, `BossRushTests`)

- 5 acts of 5; every slot resolves to a fighter with a duel.
- Each boss: arena forms over exactly 4 beats, the boss is called on the 4th; kill drops the unshed XP budget,
  magnetises every gem, full heal, the reward card first, then the level-ups; the next arena waits for the gems.
- Act card after the 5th boss: Enter skips it, a dash never does; the floor key is held per act.
- Act checkpoint: build, level, XP, held levels and fallbacks; binary round trip; a sim started from it restores
  everything and opens the pending offers; a retry carries the death and the whole run's time.
- `--boss N` reference build: 3 steps and 2 levels per boss before N.
- `"unlanded": "skip"` passes unlanded slots; the last boss played wins the run.
- Beatless forming falls back to 4 × 0.5 s; a slot's health replaces the stand-in's.
- A Boss Rush session (v7) replays to its hash.

## Player (headless, scripted)

`champions play --headless --file house12.m3u --mode bossrush --boss 4 --walk reference --picks reference --seconds 300`
(house-128.ogg ×12): [run-boss4-300s.log](run-boss4-300s.log). From boss 4 with its reference build the scripted
player cleared 14 stand-in bosses through acts 1–4 (forming, arrival, death, reward, act card) in 300 s of run
clock, without a presenter fault. (In this run the scripted dashes still skipped the act cards; Space no longer
does.)

Stills from `--boss 1 --capture-stills ... --stills-at 1.5,9`:

- [act-banner.jpg](act-banner.jpg): act 1's card over the first forming arena, the `ACT 1 · BOSS 1` clock.
- [fight-clock.jpg](fight-clock.jpg): the stand-in Ogre (boss 1) mid-fight, the fight time and boss bar.

Hand-play (title entry, Continue, Retry act, Restart act, Abandon run, the result cards) is an
[operator-pending](../../operator-pending.md) row. The stand-in arena (walls, forming lines) comes with the arenas bead
(ps-8z4.2); fights replace stand-ins as they land (ps-8z4.3 and the fight beads).
