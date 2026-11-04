# Boss Rush fight: moonhowl

Written by `champions boss review moonhowl` and `champions boss review moonhowl --motion` (boss-rush.md, Fights): the
fight in its arena's act kit, a hero with six weapons wandering the glade, at 1920 by 1080; the raw PNG stills stay out
of the tree, a few kept here as tagged JPEGs. For judging by eye; nothing here passes or fails.

## Stills

- [moonhowl-entrance.jpg](moonhowl-entrance.jpg): its arrival at the glade's boss start inside the ring of rune stones,
  the kit's pines and floor round it; the moon not yet risen.
- [moonhowl-howl.jpg](moonhowl-howl.jpg): the first howl's disc filling round it on its downbeat, the lesson's first
  lunge filling beside it.
- [moonhowl-lunges.jpg](moonhowl-lunges.jpg): the lesson's lunges chained one from the last's end, the full moon over
  the pines in the top right.
- [moonhowl-claws.jpg](moonhowl-claws.jpg): the stalk's claw combo filling at the hero; the moon waning.
- [moonhowl-howl-later.jpg](moonhowl-howl-later.jpg): the next cycle's howl and its first lunge, the moon full again.
- [moonhowl-stalk.jpg](moonhowl-stalk.jpg): 55 s in, a claw cone in the stalk under a crescent.
- [moonhowl-motion-sheet.jpg](moonhowl-motion-sheet.jpg): 12 of the motion frames (4 to 12 s), left to right, top to
  bottom.

## Notes

ps-8z4.14, Boss Rush boss 5 (act 1's finale, "The moon cycle"). The design as built is in
[boss-rush.md](../../../../docs/champions/boss-rush.md) (The roster, Moonhowl); the code is
`Assets/Champions/Sim/Rush/MoonhowlModule.cs`, its tests `dotnet/Champions.Sim.Tests/Rush/MoonhowlTests.cs`, its moon
`Client/Bosses/MoonhowlPresenter.cs` and its afterimages in `Client/Bosses/BossPresenter.cs`.

**The cycle.** Every move is planned on a beat and lands three beats later (act 1's least telegraph), on a cycle counted
from a downbeat: 32 beats (eight bars), 24 below half health. In its arena (`moonGlade` in Content/arenas.json: a ring
34 m across inside seven rune stones at 15 m, act 1's kit, held whole by a fixed camera):

- *The howl* (cycle beat 0, a downbeat; `Tail`, its `howl` clip): a 4.5 m disc round it. Its bank's signature (a wolf
  howl), so the howl you hear marks the turn of the cycle.
- *The frenzy* (beats 1 to 8; the signature): lunges (`Smash`, `clawsAttackRightForward`), each a 0.9 m slash line from
  where the last lunge left it through where the hero stood three beats before, 2.5 m past it (4 to 10 m, held 12 m
  from the centre), the body running it in 0.22 s on its beat. The first frenzy is the lesson: eight plain lunges on
  eight beats, nothing else. Later frenzies rest a beat either side of a *moon lunge* on the bar's downbeat (`Stomp`,
  `jumpBiteAttack`; two a frenzy below half): it bounds to the hero's side of the glade while a front the glade's width
  sweeps it from the far wall through the centre at 20 m/s, the dash wave (nowhere to walk to).
- *The stalk* (the rest of the cycle, until the howl's telegraph starts): it follows the hero at 3.5 m/s (slower than
  the walk, 0.8 m off), swiping a claw combo (`Fans`, `clawsAttack2HitComboForward`: 5 m cones, the second on the half
  beat, a third on the next beat below half) at a hero within 5.5 m, and leaping (`Boulders`, `jumpClawsAttack`: a
  2.6 m disc, airborne its last 0.45 s) onto one 9 m or more away, at most every eight beats.
- *The hunter's moon* below half: four untouchable beats, then the six-bar cycle with two moon lunges and three claws.

Its XP bursts fall in a crescent of seven gems behind it, away from the hero.

**The moon.** `MoonhowlModule.Moon` is the cycle's timer: full from the howl through the frenzy, waning to new halfway
through the stalk and waxing back to full at the next howl (it moves on between beats). `MoonhowlPresenter` draws it
beyond the glade's wall in the fixed camera's top right corner, over the pines, 2.8 m across, turned to the camera, in its blue: a dim rim, the lit part filled and outlined
along its limb and terminator, a halo ring breathing on the beat while it is full (two for the hunter's moon, which also
burns brighter). While the moon is full the boss presenter trails up to three afterimages of its body (its places 0.06 s
apart, its hue dimmed by each), wherever it lags enough to show: its lunges and bounds.

**Fairness** (the framework's harness, `MoonhowlIsFair`): at 90 and 180 BPM (and 128 BPM on two more seeds), act 1, a
130 s fight worn down evenly in its arena: no problems; at 90 BPM 82 hazards in 22 sequences, 10 dash waves, 6 needing
the dash from where the hero stood; at 180 BPM about 150 hazards, 39 sequences, 21 dash waves, 13 needing it. The
harness's screen circle is widened to the whole arena because its camera holds it still and whole
(`TheCameraHoldsTheGladeWhole`). Beatless it plays its cycle on the 0.5 s fallback beat; a recorded fight replays to
its hash. The harness now drops a hazard still filling at its boss's death (a dead fight lands nothing).

**Reference builds** (`reference-runs.tsv`, from the Explicit `MeasureTheReferenceBuildsAgainstMoonhowl`): the
scripted `positioning` player with the slot's reference builds at `--boss 5`, the slot's health (1664), the 128 BPM
playlist, the hero unkillable. The broad build kills it in about 98 s taking about 105 damage; the focused build (the
sword first) in about 200 s taking about 245. Their mean (about 150 s) is near the slot's 130 s target; the spread is
the pacing bead's to retune.

**Look and sound.** Cold blue (the key family, `enemies.key.blue`), with a `BossLook` of slow edge flow and a faint
ghost fade; the vendor mesh until its mesh bead (ps-8z4.15) designs one. Its bank (`bosses.moonhowl` in sounds.json):
an echoing howl for the entrance, a dog's growl ticking each telegraph beat, a dog's snapping attack for its impacts,
echoing howls for the howl (its signature) and a wolf's death cry. Its look and moon add no shader variants (BossEdge's
floats; the moon is the world's Glow material).

**Clips.** The impact times in monsters.json are read from the clips' lengths and their swings by eye (the claws strike
about 40% in, the jumps land about 60% in, the howl peaks at a second), not measured from a bone; the mesh bead's
review is the place to tighten them.
