# Boss Rush fight: sandwyrm

Written by `champions boss review sandwyrm` and `champions boss review sandwyrm --motion` (boss-rush.md, Fights): the
fight in its arena, dressed with the act 2 kit, a hero with six weapons wandering the basin, at 1920 by 1080. The raw
PNG stills stay out of the tree; a few are kept here as tagged JPEGs. They are for judging by eye; nothing here passes or
fails.

## Stills

- [sandwyrm-entrance.jpg](sandwyrm-entrance.jpg): it rises at the basin's boss start as rings of ripples close in on
  it. Round the basin stand the act 2 kit's obelisk, lotus columns, date palms and rock spires, with pyramids beyond.
- [sandwyrm-hunt.jpg](sandwyrm-hunt.jpg): the first taught hunt at 60% filled. Its ripple runs under the sand toward the
  hero's spot: chevrons over its head, ridges in its wake and the dashed heading line.
- [sandwyrm-hunt-heading.jpg](sandwyrm-hunt-heading.jpg): the second hunt, its ripple about to reach the eruption.
- [sandwyrm-lash.jpg](sandwyrm-lash.jpg): surfaced, it lashes its body down in a line at the hero.
- [sandwyrm-tunnel.jpg](sandwyrm-tunnel.jpg): the tunnel through the hero's spot to the far side. Every eruption of its
  wake fills from the dive, the far ones landing later, a half beat apart.
- [sandwyrm-spit.jpg](sandwyrm-spit.jpg): clods of sand lobbed round the hero's spot.
- [sandwyrm-breach.jpg](sandwyrm-breach.jpg): it heads under the sand to the rim across from the hero to breach. The arc
  of sand rolls out from there on the downbeat.
- [sandwyrm-motion-sheet.jpg](sandwyrm-motion-sheet.jpg): 12 of the motion frames (4 to 12 s), left to right and top
  to bottom. It rises, the first hunts fill, it sinks and erupts with a crown of shards, and it surfaces again.

## Notes

ps-8z4.17, Boss Rush boss 6, act 2's opener ("Ripples"). The design as built is in
[boss-rush.md](../../../../docs/champions/boss-rush.md) (The roster, The Sandwyrm). The code:

- the module: `Assets/Champions/Sim/Rush/SandwyrmModule.cs`;
- its tests: `dotnet/Champions.Sim.Tests/Rush/SandwyrmTests.cs`;
- its effects: `Client/Bosses/SandwyrmPresenter.cs`.

**The fight.** Its arena is `duneSea` in Content/arenas.json: a circle 34 m across, dressed with the `sunkenSands` kit,
held whole by a fixed camera. Its middle is open sand; its ruins stand at 15 m, out past its 13.5 m of travel. Every
move is planned on a beat (the song's, or the 0.5 s fallback):

- *The hunt* (`Dive`; the signature, taught alone three times first). It sinks where it stands and travels under the
  sand to the hero's spot as it dived (the dive's body move). It bursts up there on the next downbeat at least three
  beats on: a 3 m disc, 16 damage, lingering half a beat.
- *The lash* (`Smash`, Attack01): a 10 m line at the hero from where it surfaced, 1.2 m either side, three beats.
- *The spit* (`Boulders`, Cast1): five clods of 1.8 m round the hero's spot (seven in phase two), three beats.
- *The tunnel*: a straight run through the hero's spot to where its line meets 13.5 m from the centre (through the
  centre when that run would be under 8 m). It plans 1.4 m eruptions every 2 m along the line, the first three beats on
  and each next a half beat later. It bursts up at the far end on the slot after the last. The wake lands behind its
  head (the last few as it passes).
- *The breach* (the dash wave): a dive to the rim point across the centre from the hero, landing on a downbeat. A
  0.8 m ring of sand rolls out from there at 16 m/s across the whole basin. In phase two two rings roll, a dash
  cooldown and a beat apart (`BossKit.Wave`'s spacing).
- *The deep hunt* below half health. It dives to the basin's heart for four untouchable beats. Then hunts chain three
  in a row, one a bar (each planned on the beat after the last erupts, landing on the next downbeat), the tunnel comes
  back over itself, and it rests one beat between moves instead of two.

Its XP bursts lie in a zigzag of six gems along its last run under the sand.

**Fairness** (the framework's harness, `TheSandwyrmIsFairInItsBasin`): at 90 and 180 BPM, act 2 (three-beat
telegraphs), a 130 s fight worn down evenly in its arena, the slot's health. There were no problems:

| BPM | Hazards | Sequences | Dash waves | Needing the dash |
| --- | --- | --- | --- | --- |
| 90 | 101 | 29 | 2 | 2 |
| 180 | 193 | 60 | 4 | 4 |

The harness's screen circle is the whole arena, because its camera holds the basin still and whole
(`TheCameraHoldsItsBasinWhole`). Beatless, it fights on the fallback beat, and a recorded fight replays to its hash.

**Reference builds** ([reference-runs.tsv](reference-runs.tsv), from the Explicit
`MeasureTheReferenceBuildsAgainstTheSandwyrm`). These runs pair the scripted `positioning` player with the slot's
reference builds at `--boss 6`, at the slot's health (1776), on the 128 BPM playlist, with the hero unkillable:

| Build | Kill time | Damage taken |
| --- | --- | --- |
| Broad | about 93 s | about 97 |
| Focused (the sword first) | about 230 s | about 325 |

Their mean, about 160 s, is near the slot's 132.5 s target, with the same spread as act 1's fights. Retuning it is the
pacing bead's job.

**Look and sound.**

- Colour: hot magenta (the accent family, `enemies.accent.magenta`) over act 2's dim amber.
- Its `BossLook`: crystal glints on its plates, light flowing down its segments and a hot pink ember core.
- Mesh: the mesh bead's (ps-8z4.18) designed worm, which landed while this fight was in review; the stills show it.
- `SandwyrmPresenter` draws everything else in its hue, on the world's Glow material: the ripple (chevrons, wake ridges,
  heading line), shard crowns at each eruption, a crest riding the breach's ring, the entrance's closing ripples and its
  death's ring of shards.
- The floor's own grid doesn't displace. The roster's "floor grid displaces along its path" is the ridged wake, which
  needs no change to the floor's shader.
- No shader variants are added.
- Its bank (`bosses.sandwyrm`), all from the Universal Sound FX:
  - entrance: a deep monster growl;
  - ticks: stone-slide rumbles, climbing 12% a beat to each burst (`TickRise`);
  - impacts: stone demolitions and falling pebbles;
  - signature: a deep thud and a rock burst;
  - death: a long groan.

**Clips and size.** The mesh bead (ps-8z4.18) registered the type: `heightM` 14 (the worm rears about 4.8 m), its
measured impacts and its own roles. This fight adds the roles the boss animator plays (the Giant Worm's FBX holds every
clip, at 30 fps):

| Clip | Role | Timing |
| --- | --- | --- |
| Disappear | takeoff | under by 1.5 s |
| Hidden | fly | wholly underground |
| Appear | land, and the entrance's roar | impact 1.6 s, as it breaks out to full height |
| Cast1 | throw | the mesh bead's 1.167 s impact |

Disappear and Appear were measured in Blender from the mesh's top, frame by frame. So the dive's clips play its
burrowing, and `SandwyrmModule.Sunk` lowers the body while it travels. The roar is Appear rather than the mesh bead's
IdleBreak, so it rises out of the sand as it arrives.
