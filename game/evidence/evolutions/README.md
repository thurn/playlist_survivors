# Chests and the five evolutions (ps-msz.6.8)

The bosses' chests and their roulette (game/Assets/Game/Sim/Arsenal/Chests.cs, presented by
game/Assets/Game/Client/Pickups/ChestView.cs) and the evolutions' rules and layers (the weapons'
own files, game/Assets/Game/Client/Weapons/EvolutionLayers.cs); unity-game.md's Pickups, Level-ups
and Chests and The Other Weapons.

## Filmstrips

Windowless fixed-time stills of the Mac player (Mono) in the slice's level on the survivors test
playlist (house-128 first, 0.47 s a beat; the opening neon zone until the blossom zone at 6.6 s),
each hero holding only the weapon named, its levels opening no cards:

```sh
P="$PWD/native/playlists/survivors-30.m3u"
uv run --project demo3d/tools game look chest --build --backend mono --evidence evolutions \
  --at 4.45,4.9,5.3,5.65,6.3,7.1,7.75,7.9,8.3 --strip 3 --crop 360,60,1200,960 \
  --player-args "--file $P --arsenal magic-wand:8,empty-tome:1 --no-cards --debug chest@4.4 --walk down@5.3-5.8"
uv run --project demo3d/tools game look holy-wand --backend mono --evidence evolutions \
  --at 5.65,5.7675,5.885,6.0025,6.12,6.2375 --strip 3 --crop 560,280,900,800 \
  --player-args "--file $P --arsenal holy-wand:1 --no-cards"
uv run --project demo3d/tools game look unholy-vespers --backend mono --evidence evolutions \
  --at 5.65,5.885,6.12,6.355,6.59,6.825 --strip 3 --crop 460,140,1000,800 \
  --player-args "--file $P --arsenal unholy-vespers:1 --no-cards"
uv run --project demo3d/tools game look thunder-loop --backend mono --evidence evolutions \
  --at 13.16,13.28,13.40,14.69,14.81,14.93 --strip 3 --crop 360,60,1200,900 \
  --player-args "--file $P --arsenal thunder-loop:1 --no-cards"
uv run --project demo3d/tools game look soul-eater --backend mono --evidence evolutions \
  --at 11.2,11.43,11.67,11.9,12.14,12.37 --strip 3 --crop 460,140,1000,800 \
  --player-args "--file $P --arsenal soul-eater:1 --no-cards"
uv run --project demo3d/tools game look bloody-tear --backend mono --evidence evolutions \
  --at 4.95,5.0,5.18,5.42,5.65,5.7 --strip 3 --crop 460,140,1000,800 \
  --player-args "--file $P --arsenal bloody-tear:1 --no-cards"
```

- [chest.jpg](chest.jpg): `--debug chest@4.4` drops a tier-3 chest of minute 25's treasure two
  tiles below the knight, popping in with a gold burst (4.45 s): a gold crystal reliquary hovering
  over a glow, five shards (its five items) circling it, under a faint column of light (4.9, 5.3 s).
  The knight walks down onto it (`--walk`) and takes it (5.65 s): it rises over the knight's head,
  grown, and spins over a gold ring while the world is frozen, flashing on each of its eight eighths
  with its shards cycling the weapons' colours, under the HUD's TREASURE card (ps-msz.6.9, fed by the
  chest events), whose five gems cycle on the same ticks (6.3, 7.1 s). It lands on the downbeat after
  them (between 7.1 and 7.75 s): TREASURE FOUND, HOLY WAND (NEW!) and four ranks of Empty Tome; the
  chest bursts, its rewards fly into the knight, and Holy Wand's gold arrival rings sweep out with six
  rays of sparks (7.75, 7.9 s): the wand at rank 8 and its partner, Empty Tome, owned, so the first
  reward evolves it. Play resumes on that beat and Holy Wand's gold stream flies at the horde under
  the fading card (8.3 s). The evolutions' strips below predate the HUD's landing on staging and show
  no HUD.
- [holy-wand.jpg](holy-wand.jpg): Holy Wand's bolts on the eighths and their hits on the sixteenths,
  gold crystals with a long streak of afterimages through a prism's six hues. Against the bloom the
  hues read as coloured fringes along a gold-white stream rather than distinct bands (an operator row
  asks whether that reads as a prism at full speed).
- [unholy-vespers.jpg](unholy-vespers.jpg): violet shards on the permanent ring, over a violet ring
  of light on the ground that kicks on each beat; each new ring takes over from the last with no gap.
- [thunder-loop.jpg](thunder-loop.jpg): the volley from the beat at 13.15 s (13.16 to 13.40) and its
  returns from 14.68 s (14.69 to 14.93), each in magenta with a magenta shock ring thrown out along the
  ground where it lands, in the blossom zone.
- [soul-eater.jpg](soul-eater.jpg): Soul Eater's teal aura, 5.5 m across, as the horde reaches it:
  each hit draws a small teal soul arcing out of its monster into the knight, who glows teal as they
  arrive (11.67, 12.14 s).
- [bloody-tear.jpg](bloody-tear.jpg): Bloody Tear's crimson slashes (two strikes a beat at its
  amount) where the sword's are white, in the opening zone before the horde arrives; its hits burst
  crimson, larger when critical.

## The tests

- game/Assets/Game/Tests/Sim/Arsenal/ChestTests.cs (.NET and EditMode):
  - **Chest timing**: a boss's chest falls on its tile's centre and the hero takes it only clear of
    the crowd, at the first tick's end after the last enemy leaves; the roulette ticks on eight
    eighths from the next downbeat and **lands on the downbeat after them** (checked against the beat
    events' downbeat flag), the world frozen from the take to the landing, the run clock still, and
    play resuming on the landing's own beat. A pause holds the roulette, which goes on from the first
    eighth after it, still landing on a downbeat. Levels reached during the spin open their cards at
    the landing.
  - **Rewards**: a tier-3 chest's five ranks in turn (native's chest_two_bars test); the
    lowest-ranked owned weapon, then passive, then the heal, never a new item.
  - **Evolution eligibility**: only an evolving minute's chest evolves, only a weapon at its top rank
    with its partner passive, in evolution order, into the base weapon's own slot, and never twice.
  - The debug chest: tier 3 of minute 25, two tiles ahead of the hero, lying there until taken.
- game/Assets/Game/Tests/Sim/Weapons/EvolutionTests.cs: Soul Eater's 1 + Recovery a hit and its
  power from health healed (capped at 6 more); Bloody Tear's crits at chance x Luck (every blow at
  Luck 10, none at Luck 0 or on the sword), double damage and the heal of 8 at most once a second;
  Unholy Vespers' rings born without an end and cut when the next is born; and an evolution's pulse on
  an accented beat breaking a structure where Garlic's, or Soul Eater's off the accent, doesn't.
- Mutation-checked: dropping the crowd rule, the first tick's downbeat, the pause's hold, the
  level-up's guard or the evolution gate on structure breaks each fails a test.
- game/Assets/Game/Tests/EditMode/HudTests.cs: the chest events fill the HUD's roulette, its ticks as
  they come, TREASURE FOUND on the landing's frame with each reward's name and rank, gone after.
- game/Assets/Game/Tests/EditMode/EffectsSoundTests.cs: the chest's coins as it is taken and lands,
  a ping on each of its eighths, and one transformation for a landing with two evolutions.

## A 30-minute run

A scratch harness (Run30's, game/evidence/schedule) on the survivors playlist's dump, the hero put on
each chest as it fell and cards auto-confirmed: all 15 chests taken, 0.01 to 3.1 s after they fell
(the hero clear of the crowd in 97% of frames), each roulette landing about two seconds later; the
hero evolved Bloody Tear at 15:00, Soul Eater at 20:00 and Holy Wand at 25:00 (minute 10's chest found
nothing at its top rank).
