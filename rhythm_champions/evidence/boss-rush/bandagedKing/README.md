# Boss Rush fight: bandagedKing

Written by `champions boss review bandagedKing` and `champions boss review bandagedKing --motion` (boss-rush.md,
Fights): the fight in its arena's act kit, a hero with six weapons wandering the court, at 1920 by 1080; the raw PNG
stills stay out of the tree, a few kept here as tagged JPEGs. For judging by eye; nothing here passes or fails.

## Stills

- [bandagedKing-entrance.jpg](bandagedKing-entrance.jpg): his arrival at the court's boss start before his bier (a
  fallen obelisk between two braziers), the colonnade, obelisks, sand floor and pyramids of the act 2 kit; still air.
- [bandagedKing-lesson-lash.jpg](bandagedKing-lesson-lash.jpg): the first storm, the lesson: one lash filling from him
  through the hero's mark to the far wall. The grid and props fade beyond the hero's sight, the walls and the King glow,
  sand streams across the court in his cyan.
- [bandagedKing-storm-lashes.jpg](bandagedKing-storm-lashes.jpg): a later lash of the first storm, the hero's circle of
  sight round it.
- [bandagedKing-front.jpg](bandagedKing-front.jpg): the calm before the second storm, its sand front filling along the
  top wall on the downbeat to come; he has begun gliding back.
- [bandagedKing-front-sweep.jpg](bandagedKing-front-sweep.jpg): the front sweeping down the court past the hero (the
  dash wave) as the storm closes the sight, the storm's first lash filling.
- [bandagedKing-calm.jpg](bandagedKing-calm.jpg): 50 s in.
- [bandagedKing-motion-sheet.jpg](bandagedKing-motion-sheet.jpg): 12 of the motion frames (4 to 12 s), left to right,
  top to bottom: the calm entrance, then the first storm rising, the grid fading out from the hero's sight.

## Notes

ps-8z4.19, Boss Rush boss 7 (act 2, "Sandstorm"). The design as built is in
[boss-rush.md](../../../../docs/champions/boss-rush.md) (The roster, The Bandaged King); the code is
`Assets/Champions/Sim/Rush/BandagedKingModule.cs`, its tests `dotnet/Champions.Sim.Tests/Rush/BandagedKingTests.cs`,
the storm `Client/Bosses/BandagedKingPresenter.cs` and World.hlsl's `StormSight` (in GridFloor.shader and
ArenaEdge.shader), with an EditMode test that the sight closes only in his fight
(`BossRushTests.TheBandagedKingsStormClosesTheSightOnlyInHisFight`).

**The cycle.** Every move is planned on a beat and lands three beats later (act 2's least telegraph), on a cycle of 32
beats (eight bars) counted from a downbeat. In his arena (`kingsTomb` in Content/arenas.json: an octagonal court 34 by
28 m, four lotus columns, four obelisks, his bier, act 2's kit, held whole by a fixed camera):

- *The storm* (cycle beats 0 to 15; 0 to 23 below half): rises over the beat either side of the downbeat. Its *sand
  front* (`Stomp`, `BandageWhipAttack2Forward`; the bank's signature, a wind swell) is a line the court's width, square to it,
  starting 0.6 m beyond the wall behind the hero's side and sweeping the whole court at 22 m/s: the dash wave. As it
  fills he glides (eased) to 10 m from the hero, or the nearest place on his bounds that far. From beat 4 he lashes
  every other beat (`Smash`, `BandageWhipAttack3Forward`): a 0.9 m line from his body through the hero's mark to the wall, at
  most 24 m. The first storm is the lesson: no front, lashes alone.
- *The calm* (the rest): the sand settles over two beats and he stalks the hero (3 m/s, 2 m off). A hero within 7 m
  gets the whip combo (`Fans`, `BandageWhip3HitComboForward`: three 10 m lashes turned left, right and centre on the beat, the
  half beat and the next beat); one farther off a reaching lash. Two beats' rest after each; all done before he
  withdraws for the next storm.
- *He wakes fully* below half: four untouchable beats while the storm clears over two, then a storm of three bars with
  a second front on its beat 12, and every other lash crossed (`Boulders`, `BandageWhip2HitComboForward`: two lines 0.22 rad
  either side of the hero's mark, the second on the half beat; standing still between them is safe).

His XP bursts unravel in a line of seven gems behind him, away from the hero.

**The sight.** `BandagedKingModule.Storm` (0 to 1, moving on between beats) drives the world's `_RC_Storm`: the grid
floor and the arena's props, floor pieces and backdrop fade beyond 6.5 m of the hero (a 3 m feather, 6% left at full
storm). The walls (`_Storm` 0), the King, every telegraph, the hero and the effects draw through other materials and
always glow, so nothing that can hurt is ever hidden: no memory or tracking test. Sand bands (90 at full storm, three
brightness tiers of the world's Glow material in his cyan) stream on the wind, thinning away round the hero's feet.
No shader keywords: no variants for the budget.

**Fairness** (the framework's harness, `TheBandagedKingIsFair`): at 90 and 180 BPM (and 128 BPM on two more seeds),
act 2, a 135 s fight worn down evenly in its arena: no problems; at 90 BPM 93 hazards in 9 sequences, 8 dash waves, 5
needing the dash from where the hero stood; at 180 BPM 188 hazards, 21 sequences, 18 dash waves, 12 needing it. The
harness's screen circle is widened to the whole court because its camera holds it still and whole
(`TheCameraHoldsTheCourtWhole`). Beatless it plays its cycle on the 0.5 s fallback beat; a recorded fight replays to
its hash.

**Reference builds** (`reference-runs.tsv`, from the Explicit `MeasureTheReferenceBuildsAgainstTheBandagedKing`): the
scripted `positioning` player with the slot's reference builds at `--boss 7`, the slot's health (1930), the 128 BPM
playlist, the hero unkillable. The broad build kills him in about 96 s taking 10 to 45 damage; the focused build in
about 246 s taking about 75. Their mean (about 170 s) is above the slot's 135 s target, like the act 1 fights' focused
builds; the spread and the health are the pacing bead's (ps-8z4.60) to retune.

**Look and sound.** Faience cyan (the key family's lighter slot, `enemies.key.cyan`): the storm's sand is his colour,
and cyan keeps the orange/red telegraphs apart in it. A `BossLook` of scanlines climbing him like wrappings, slow edge
flow and a faint glitch;
the vendor mesh until its mesh bead (ps-8z4.20, landed alongside: its own recipe, cast row and clips) is baked in. His bank (`bosses.bandagedKing` in sounds.json): a hurricane gust for the entrance, whip whooshes ticking each
telegraph beat, whip cracks for the lashes' impacts, low wind gusts (the swell) for the front, a wind impact for his
death.

**Clips.** The mesh bead (ps-8z4.20) registered the Mummy first, with the Forward whip clips and its own impact
times; this fight uses its monsters.json entry and cast row (heightM 5, radius 1.4, 3 m/s) as they are: the lashes play
`BandageWhipAttack3Forward`, the front `BandageWhipAttack2Forward`, the combo the three-hit combo and the crossed lashes
the two-hit combo. Measured here in Blender for the plain clips (a bandage bone's tip reaching farthest from the root:
the whips stretch their bandages many metres at the strike), the plain clips strike at 0.433 s (Attack1), 0.4 s
(Attack2), 0.833 s (Attack3) and 0.933, 1.567, 2.033 s (the three-hit combo), close to the Forward clips' timings.
The Sarcophagus prefab is not used: his bier is the kit's fallen obelisk.
