# ps-6y2.17: bosses

Measured on jarvis (M5 Max), 2026-11-02. The duel lengths come from the sim's tuning tests; the stills are 1080p renders of
the duels' scripted scenes, shrunk to 960 px, from:

```sh
dotnet test rhythm_champions/dotnet --filter Name=PrintTheDuelLengths --logger "console;verbosity=detailed"
just champions-tool edgecut fatOgre && just champions-tool edgecut dragon
just champions-tool stills --scene duel-ogre --scene duel-dragon
just champions-tool look captures/stills --out rhythm_champions/evidence/ps-6y2.17/look --impact "*-entrance"
```

## Duel lengths

`BossTuningTests` (dotnet/Champions.Sim.Tests) plays each duel through the real sim on a steady fixture song. The
reference builds are what a good run holds by each boss (Decision 11's picks: the first offer and a level-up every two
minutes from 1:30, and the Ogre's treasure):

- **Ogre** (7:00): Sword Slash 2, Orbit Blades 1, Keen Edge 1, Blade Rush 1.
- **Dragon** (13:00): Sword Slash 3 (the treasure's raise), Orbit Blades 2, Keen Edge 1, Wide Arcs 1, Blade Rush 2.

The reference player keeps within Sword Slash's reach of the boss (walking to 3 m short of it whenever it is over 4.5 m
away) and dashes through it on every attack's landing beat that its cooldown allows: every dodge it makes is perfect.
"Chases only" is the same player never dashing, immortal, for comparison. Health: Ogre 375, dragon 1,000 (Sword Slash's
base hits; enemies.json).

| Boss | Song | Player | Duel | Hero's health left | Staggers |
| --- | --- | --- | ---: | ---: | ---: |
| fatOgre | beatless | dodges | 72.0 s | 100 | 9 |
| fatOgre | 80 BPM | dodges | 78.0 s | 100 | 6 |
| fatOgre | 100 BPM | dodges | 86.4 s | 100 | 8 |
| fatOgre | **128 BPM** | dodges | **71.2 s** | 100 | 8 |
| fatOgre | 160 BPM | dodges | 57.0 s | 100 | 8 |
| fatOgre | 174 BPM | dodges | 52.4 s | 100 | 8 |
| fatOgre | 128 BPM | chases only | 150.0 s | - | 0 |
| dragon | beatless | dodges | 136.9 s | 100 | 14 |
| dragon | 80 BPM | dodges | 174.0 s | 100 | 11 |
| dragon | 100 BPM | dodges | 184.8 s | 100 | 14 |
| dragon | **128 BPM** | dodges | **138.8 s** | 100 | 14 |
| dragon | 160 BPM | dodges | 111.2 s | 100 | 14 |
| dragon | 174 BPM | dodges | 100.0 s | 100 | 13 |
| dragon | 128 BPM | chases only | 281.2 s | - | 0 |

- The targets (Ogre 60-90 s, dragon 2-3 minutes) hold at 128 BPM and beatless, which the tests assert.
- **Bosses attack on the beat**, so a duel lasts about as many beats at any tempo: fast songs give shorter duels (the
  Ogre in 52 s at 174 BPM), slow ones longer. 80 BPM breaks the trend because Sword Slash then swings every beat (0.75 s)
  instead of every two.
- Most of the reference player's damage is Blade Rush's on-beat dashes through the boss and Break's double damage: a
  player who never dashes takes twice as long and is hit at nearly every landing.

## Telegraphs and the duels

`DuelScene` (Client/Scenes) calls each boss at the start of a scripted 120 BPM song whose every beat is strong, so the
boss lands on the first downbeat 4 s in; 24 skeletons stand round the hero for its entrance to shatter. The hero (a white
ring standing in, immortal) stands while the boss comes and takes each of its moves in turn. Each telegraph is kept three
fifths filled: a red outline from its start, a faint wash and a bright front growing from its origin to meet the outline
on the landing beat, then a flash.

| Still | What it shows |
| --- | --- |
| [duel-ogre-entrance](duel-ogre-entrance.jpg) | The Ogre lands on its downbeat (5.0 s); its shockwave breaks all 24 skeletons on that frame, thrown away from it |
| [duel-ogre-smash](duel-ogre-smash.jpg) | The overhead smash: a line in front of it, two beats |
| [duel-ogre-boulders](duel-ogre-boulders.jpg) | The boulders: a circle on the hero and two round it, two beats |
| [duel-ogre-stomp](duel-ogre-stomp.jpg) | The stomp: a ring round it, landing on a downbeat |
| [duel-ogre-charge](duel-ogre-charge.jpg) | The charge: a long line through the hero, three beats |
| [duel-ogre-duel](duel-ogre-duel.jpg) | The duel at 24 s |
| [duel-dragon-entrance](duel-dragon-entrance.jpg) | The dragon lands; the floor turns to the warm zone |
| [duel-dragon-breath](duel-dragon-breath.jpg) | The fire breath: a cone from its mouth, two beats (it burns a beat) |
| [duel-dragon-fireballs](duel-dragon-fireballs.jpg) | The fireballs: a circle marked on the hero on each of three beats |
| [duel-dragon-tail](duel-dragon-tail.jpg) | The tail sweep: a ring round it, landing on a downbeat |
| [duel-dragon-dive](duel-dragon-dive.jpg) | The dive: it takes off as the hero's spot is marked, landing four beats later |
| [duel-dragon-duel](duel-dragon-duel.jpg) | The duel at 24 s |

The models are ps-6y2.12's edge cuts, posed by `BossAnimator`: each attack's clip timed so its impact frame lands on
the move's beat, the Ogre's run along its charge, the dragon's takeoff, flight and landing round its dive.

## Look metrics

[look/metrics.md](look/metrics.md). Black share holds for both bosses (92-95%, 55% needed); readability, the floor,
palette conformance and the key hold (the skeletons show only on the entrance frames, which the steady rules don't judge). Recorded exceptions, for the look pass (ps-6y2.23):

- `background_light_max` (15%) breaks on every steady duel still (20-69%): the duel is on cleared ground, so one boss
  and its telegraphs are nearly all the frame's lit content and the grid holds the rest (the warm floor most). The rule
  judges a swarm; the world's own stills break it the same way (ps-6y2.14).
- `monster_form_min` breaks on the Ogre (0.5-0.7, 30 needed): its cut is a big dark body with few lines, so most of the
  tiles over it are flat dark. The rule's tiles are sized for fodder; the dragon passes.
- `halo_impact_min` breaks on the entrance frames (1.04, 15 needed): on the landing frame the shards and the entrance's
  ring have only just begun.
