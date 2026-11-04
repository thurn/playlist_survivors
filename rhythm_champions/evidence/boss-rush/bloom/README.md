# Boss Rush fight: bloom

Written by `champions boss review bloom` and `champions boss review bloom --motion` (boss-rush.md, Fights): the fight
in its arena's act kit, a hero with six weapons wandering round the root, at 1920 by 1080; the raw PNG stills stay out
of the tree, a few kept here as tagged JPEGs. For judging by eye; nothing here passes or fails.

## Stills

- [bloom-entrance.jpg](bloom-entrance.jpg): its arrival on a downbeat at the centre of its ring, the kit's ferns, pines
  and floor round it.
- [bloom-arms-lesson.jpg](bloom-arms-lesson.jpg): the lesson's first three arms filling, 60% of their telegraph.
- [bloom-arms-turning.jpg](bloom-arms-turning.jpg): 25 s in, three live arms turning (violet spores shed off their
  trailing edges) while the next bar's arms fill where they will have turned to.
- [bloom-bite.jpg](bloom-bite.jpg): the bite's cone filling at the hero beside its root.
- [bloom-pulse.jpg](bloom-pulse.jpg): the pulse filling at the root before it rings out.
- [bloom-later.jpg](bloom-later.jpg): 45 s in, live arms and the next set.
- [bloom-motion-sheet.jpg](bloom-motion-sheet.jpg): 12 of the motion frames (4 to 12 s), left to right, top to bottom.

## Notes

ps-8z4.10, Boss Rush boss 3 (act 1, "Rotating spirals"). The design as built is in
[boss-rush.md](../../../../docs/champions/boss-rush.md) (The roster, the Bloom); the code is
`Assets/Champions/Sim/Rush/BloomModule.cs`, its tests `dotnet/Champions.Sim.Tests/Rush/BloomTests.cs`.

**The attack set.** Rooted at the centre of its ring arena (`bloom` in Content/arenas.json: 34 m across, four tree
ferns by the wall on the diagonals, held whole by a fixed camera):

- *The arms* (signature, `Breath`, its `breath` clip): lines of spores from the root to the wall, 0.9 m wide. Each bar
  a new set lands on the downbeat (three beats' warning) where the last bar's have turned to, so they read as one set of
  arms that changes speed and direction on the bar. A speed-1 bar turns 0.4 rad, capped at 0.34 rad/s (its tip at the
  wall just under the walk). Phase 1 opens with three steady arms alone (the lesson: nothing else, not even the bite).
- *The bite* (`Smash`, `Bite`): a cone 6.5 m long at whoever stands within 5.5 m of the root, landing on the half beat,
  at most every two bars; never over the lesson or the pulse.
- *The spore spiral* (`Fireballs` then plain, `SpellCast`): rings of 8 (phase 2: 10) shots on 3 to 5 consecutive beats,
  each ring turned a quarter of its gap further.
- *The pulse* (the dash wave, `Stomp`, `BreathAttackSurround`): a ring from the root at 13 m/s on a downbeat, faster than
  any walk; phase 2 sends two fronts spaced for the dash.
- *The blossom* at half health: four untouchable beats, then four arms with speed changes and reversals every bar,
  denser spirals and the two-front pulse.

Its XP bursts fling six seeds along a spiral arm from near its root, turned further each burst.

**Fairness** (the framework's harness, `TheBloomIsFair`): at 90 and 180 BPM (and 128 BPM on two more seeds), act 1, a
125 s fight worn down evenly: no problems; at 90 BPM 218 hazards in 11 sequences, 2 dash waves both needing the dash;
at 180 BPM 399 hazards, 20 sequences, 6 dash waves all needing it. The harness's screen circle is widened to the whole
arena because its camera holds it still and whole (`TheCameraHoldsTheBloomsArenaWhole`). Beatless it plays on the
0.5 s fallback beat; a recorded fight replays to its hash.

**Reference builds** (`reference-runs.tsv`, from the Explicit `MeasureTheReferenceBuildsAgainstTheBloom`): the
scripted `positioning` player with the slot's reference builds at `--boss 3`, the slot's health (1262), the 128 BPM
playlist, the hero unkillable. The broad build kills it in about 87 s taking 50 to 75 damage; the focused build (the
sword first) in about 306 s taking about 250. Both deal the damage per second they dealt the stand-in (4.3 and 15.8 in
evidence/boss-rush/progression/), so the Bloom doesn't stall either build: the 125 s target is the health table's,
from the two builds' mean damage, and the spread between the builds is the pacing bead's to retune.

**Look and sound.** Violet (the accent family) with a `BossLook` of edge flow and an ultraviolet ember heart; the
vendor mesh until its mesh bead (ps-8z4.11) designs one. Its spores stream along its live arms in its violet
(`BloomSporePresenter`), the generic fight flavour sparks at each landing. Its bank (`bosses.bloom` in sounds.json):
a wind cast for the entrance, breathy wind whooshes ticking each telegraph beat, wet gore snaps for its impacts (the
bite), wind projectiles for the arms' landings (its signature) and a stretching snap at its death. Its look adds no
shader variants (BossEdge's floats).
