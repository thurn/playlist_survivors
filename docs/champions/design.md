# Rhythm Champions: Game Design

## Introduction

Rhythm Champions is a horde survivor rhythm game. The hero fights a horde
of neon monsters to whatever music the player is listening to. Its attacks
land on the beat by themselves, the horde steps to the music, and one key,
Space, makes the hero dash: pressed on the beat, the dash hits harder and
wider. Its vision is "the game that doesn't need a trailer": every moment
of play is cinematic, and the aesthetic experience comes before everything
else.

This document is the locked design. It was settled with the operator (the
developer who plays and judges the game) on 2026-11-01 in a design
interview, after months of prototypes. [The Look](#the-look) was reopened
on 2026-11-02. Every earlier design is archived
under [docs/archive/](../archive/); none of them is authoritative. The
[implementation plan](plan.md) says how V1 gets built.

The project was called Playlist Survivors until this design. The game is
now Rhythm Champions; the repository, its caches and its bead prefix keep
the old name.

### The game, as the player sees it

- The player picks an app that is playing music (Spotify, Apple Music, a
  browser) or a local file or playlist. The game mutes the app and plays its
  audio itself, 3 s late, so it knows every beat before the player hears it.
- A white knight stands on an endless black grid. Neon monsters, low-poly
  wireframes glowing on black like synthwave art, close in from every side,
  stepping on the beat.
- WASD, the arrows or a click move the hero freely. The sword swings by
  itself on the beat, about once a second, at the thickest part of the
  crowd. Monsters burst into tumbling shards on the frame they're hit,
  thrown away from the blow.
- Space dashes. It always goes the same distance and is ready again in
  0.6 s. A dash on the beat hits twice as hard and half again as wide.
  Upgrades turn the dash into an attack: a leap that slams the ground, a
  rush through the crowd with the blade spinning, a shield charge, a blink.
- Every beat a ring of light leaves the hero's feet and sweeps across the
  grid, the monsters and the buildings, which shatter when the hero's
  attacks reach them.
- Every couple of minutes, in a quiet moment between songs, the hero picks
  one of three upgrades, each a dramatic jump in power.
- Two bosses interrupt the horde: an ogre at about minute 7 and a dragon at
  about minute 13. Each enters on a strong downbeat, shattering the whole
  horde, and fights a duel whose attacks land on the beat. Dodging them on
  the beat breaks the boss open.
- Killing the dragon wins level 1, the whole of V1.

### The short version

- **Every moment cinematic.** The look outranks everything. It aims for
  retro 3D neon wireframe in the synthwave style, starts from the neon edge
  trailer, which already looks good, and changes only what looks better
  changed, judged by eye.
- **The beat shapes everything; silence breaks nothing.** Every timing has a
  fallback in seconds. A song with no beat, a gap between songs or no music
  at all plays fine and costs the player nothing.
- **Nothing waits after a key.** A dash moves on the key-down frame.
- **Fewer choices that matter more.** A pick every couple of minutes, three
  cards, each one a "holy shit" jump.
- **Mobs fall, bosses fight.** A good build kills fodder in one hit; bosses
  have real, rhythmic mechanics.

## Decisions

Settled with the operator on 2026-11-01. Executors (the agents or people
implementing this design) treat them as fixed. Every number not listed here
is a recommended default: an executor may change it with a recorded
gameplay or measurement reason, without weakening a decision.

1. **A run is a chain of levels of about 15 minutes**, each with its own
   look and roster, ending in a boss. V1 ships level 1 only.
2. **Level 1 has two bosses**: a Fat Ogre around minute 7 and the dragon
   around minute 13-14. Each enters on the first strong downbeat after its
   minute (within 60 s), its entrance shatters every mob on screen, and the
   fight is a duel on the cleared ground.
3. **Bosses reward dodging on the beat.** Every boss attack is telegraphed
   over whole beats and lands on a beat. Dashing through it on that beat is a
   **perfect dodge** and fills the boss's **Break** bar; a full bar staggers
   the boss for a bar of music, taking extra damage. Hits never stagger or
   knock back a boss.
4. **Free movement** in 3D on WASD, the arrows or click-to-move, at a fixed
   6 m/s. Speeds never follow the tempo: the beat decides *when* monsters
   step, never how fast they travel.
5. **The dash**: Space, the hero's only key-driven ability. The starting
   Dash goes 4 m over 0.15 s with invulnerability (i-frames) for the dash and
   0.1 s after. Cooldown 0.6 s in seconds, the same in every song and in
   silence. A press in the last 0.15 s of cooldown fires the moment it is
   ready; earlier presses are dropped. It goes the way the hero is moving.
   Every dash type has its own fixed distance, whatever the timing.
6. **On-beat judgement is binary and forgiving**: within 80 ms of a beat,
   or 60 ms of a half beat or a strong onset the music actually plays. On
   the beat a dash deals 2x damage over 1.5x area. With no reliable beat
   every press counts as on the beat. Judged in heard time.
7. **The indicator above the hero**: a short white strip where the next two
   beats' ticks converge on a centre pip, the dash's cooldown. Under it, a
   thin white health bar. Nothing else about the hero is on the HUD.
8. **Terms**: **Dash** (the Space ability; exactly one), **Attack** (fires by
   itself on the beat), **Mod** (changes dashes and attacks by tag),
   **Upgrade** (any card picked). "Skill", "active", "passive" and "support"
   are not used.
9. **Sword Slash**, the starting attack, swings about once a second on the
   beat (1, 2 or 4 beats, whichever is nearest 1 s), aimed at the densest
   group in reach, and holds when nothing is in reach. The hero's body never
   animates faster than 1.15x: extra strikes from upgrades are spectral.
10. **Experience comes from kills**, with nothing to pick up. A level-up
    waits for the next quiet moment (a gap between songs or a stretch with no
    beat), at most 60 s.
11. **Level-ups offer three cards, no reroll or skip.** The first offer, at
    the top of a run, holds one new dash, one new attack and one mod. A build
    holds one dash, up to three attacks and up to four mods; attacks and
    dashes have three ranks, mods two, and every rank is a change in kind.
12. **While cards are up the world holds**: monsters keep bobbing to the
    music but don't move or attack. Cards appear at once, are pickable at
    once, and play resumes on the pick.
13. **A boss's treasure** offers the biggest jumps available: evolve a
    rank-3 attack or dash, or raise one straight to rank 3. It appears in
    under 0.3 s, with no roulette.
14. **Threats**: fodder hurts only by touch; elites add one telegraphed move
    each; a ranged mob joins in level 1's second half.
15. **Health** heals 25% on each level-up and fully on a boss kill; a mod
    can add healing. No pickups.
16. **Level 1's cast**: skeleton, goblin, slime, bat and demon (fodder),
    mushroom (ranged), big ork and troll (elites), Fat Ogre (mid-boss),
    dragon (boss).
17. **Density** ramps from 150 alive to 600 by minute 12; section changes
    bring formed waves and loud sections surge toward a cap of 1,000.
18. **The arena is infinite.** Level 1's structures are Synty buildings and
    trees in the edge look over a dim coloured fill. They block movement,
    shatter on any hero damage, never regrow, and give nothing but
    spectacle. Grey line art is reserved for unbreakable structures in
    later levels; level 1 has none.
19. **The trailer's edge look** (reopened 2026-11-02): monsters, the hero and
    structures are drawn as the neon edge trailer drew them, with
    `MonsterEdge.hlsl`'s 24-degree crease lines over a dark glass core,
    ported from the frozen look package. Each monster then gets a low-poly
    mesh designed as a retro 3D neon wireframe (the `monster-mesh-design`
    skill), and the
    trailer's drawing stays behind `--look trailer` for comparison. Look
    changes are judged by eye, never by thresholds. The "edge cuts" this
    replaced are deleted.
20. **The beat wave**: a ring of light leaves the hero's feet on every beat,
    brightening and slightly bending the grid (Geometry Wars' ripple),
    flaring monsters' lines with a bob, and flaring structures' edges.
21. **The palette** keeps the trailer's families (cyan-blue key fodder,
    magenta-violet accent, amber-orange elites, red boss) and gives the
    empty yellow-green arc to roles: electric yellow for warnings and enemy
    shots, plus a dim warm zone. White is only ever the hero, its effects
    and its summons.
22. **Weapon effects** use Hovl's slash shapes (local vendor imports, never
    committed) drawn through our own white shader.
23. **The zone key follows the music**: four floor keys (two cool, a violet,
    a dim warm) crossfading at strong section changes.
24. **Hero**: the Synty knight with sword and shield, in white. Sword Slash
    plays the reference trailer's opening clips at natural speed.
25. **Camera**: a fixed high angle following the hero with a small lead,
    easing out for boss fights, moving only on special moments.
26. **UI**: card = icon, name and one line of at most six words. The HUD is
    a hairline experience bar and, in boss fights, a boss bar with its Break
    bar. No timer, kill count, damage numbers or banners.
27. **Music**: Delayed listening and local files, through the existing Rust
    analysis plugin, with video and input calibration. The game never pauses
    for silence.
28. **Difficulty**: beatable in a few tries. A first run usually reaches the
    Ogre; the dragon falls after two to four runs.
29. **V1 runs on Apple Silicon Macs** with keyboard and mouse. An M1 Air
    class machine holds 1,000 monsters at 60 fps through quality tiers; the
    look is designed at full quality and never cut down for the low end.
30. **Technology** is in the [implementation plan](plan.md)'s Decisions.

## Terms

| Term | Meaning |
| --- | --- |
| Run | One playthrough. In the full game a chain of levels; in V1, level 1. |
| Level | About 15 minutes of play with its own look and roster, ending in a boss. |
| Run clock | Playing time. Stops only while cards are up or the game is paused, never for the music. |
| Dash | The Space ability. The hero has exactly one; a new one replaces it. |
| Attack | An ability that fires by itself on the beat grid. |
| Mod | A change to dashes and attacks, applied by tag to everything it fits. |
| Upgrade | Any card picked: a new dash, a new attack, a new mod, or a rank of one owned. |
| Rank | An upgrade's level: 1-3 for attacks and dashes, 1-2 for mods. |
| Evolution | An attack's or dash's fourth form, given only by treasure, at rank 3. |
| Treasure | A boss's reward: a three-card offer of evolutions and raises to rank 3. |
| Beat, downbeat | The analyzer's committed beats; a downbeat is a bar's first beat. |
| Half beat | The midpoint between two beats. |
| Reliable beat | A beat the analyzer is confident in. Without one the game plays "beatless". |
| Strong onset | A drum hit or accent the analyzer marks above its accent threshold. |
| On the beat | A press inside the judgement windows (Decision 6), or any press with no reliable beat. |
| Heard time | When the player heard the music, as opposed to when the sim processes a key. |
| Quiet moment | A gap between songs or a stretch with no reliable beat, when held cards show. |
| Fodder, elite, boss | Common monsters; tougher monsters with one telegraphed move; duel bosses. |
| Mob | Fodder and elites together: everything a boss's entrance shatters. |
| Edge look | The trailer's drawing: a mesh's creases sharper than 24 degrees lit in its colour over a dark glass core (`MonsterEdge.hlsl`). |
| Monster mesh | A monster's model reworked as a low-poly neon wireframe by the `monster-mesh-design` skill. |
| Zone key | The floor's colour family at the moment. |
| Special moment | The few events allowed to flash and shake: see [Restraint](#restraint). |
| Break | A boss's stagger bar, filled by perfect dodges. |

## Related Information

- [The implementation plan](plan.md): architecture, build policy, CI and the
  Enact epic's beads.
- The palette's starting point: [The ion palette's gaps](../archive/design/ion-palette-gaps.md).
- The rules the look keeps from the neon look's study, with Geometry Wars 3
  measured: [the archived style guide](../archive/design/neon-look-style.md).
  The palette bead writes its successor, `docs/champions/style.md`.
- The prototypes this design learned from, all archived and frozen:
  - [The Unity game](../archive/design/unity-game.md) and its
    [modes](../archive/design/game-modes.md) (`game/`): the sim, the plugin,
    the clocks, heard-time presses, the zen mode and the dash tried before.
  - [The neon look](../archive/design/neon-look.md): the look package, the
    impulse field, the edge look's crease lines.
  - [The 3D tech demo](../archive/tech-demo-3d/README.md) (`demo3d/`): the
    crowd renderer, compute effects, choreography and trailer mode.
  - [The native game](../archive/design/native-app.md) (`native/`): the
    belief stream, Delayed listening and the analyzer.
- The reference look: the operator's capture of the neon action trailer in
  the edge preset (`captures/trailer-action-edge.mp4`, made by
  `just trailer-looks action` in the frozen `demo3d/`). Stills from it are in
  [reference/](reference/), discussed in
  [The reference: the trailer](#the-reference-the-trailer).
- [The monster-mesh-design skill](../../.agents/skills/monster-mesh-design/SKILL.md):
  how each monster's mesh is designed.

## The Run

### Level 1, minute by minute

The times are run clock and recommended defaults.

| When | What happens |
| --- | --- |
| Start | The music starts and the first offer appears: one new dash, one new attack, one mod. Play starts on the first downbeat after the pick, or at once with no beat. |
| 0:00 | Skeletons and slimes, 150 alive, ramping. |
| 1:30 | Bats join, swarming. |
| 3:00 | Goblins join. |
| 4:00 | The first big orks (elite). |
| 4:30 | Demons join. |
| 5:30 | The first trolls (elite). |
| 7:00 | Spawning stops. The Fat Ogre enters on the first strong downbeat 4 s or more later (by 8:00 at the latest, on a downbeat), shattering every mob on screen. Duel, 60-90 s for a good build. |
| Ogre dies | Treasure. Spawning resumes with the second-half mix: denser, more elites, mushrooms (ranged) join. |
| 13:00 | Spawning stops. The dragon enters on the first strong downbeat 4 s or more later (by 14:00 at the latest), shattering the horde; the floor turns to the warm zone. Duel, 2-3 minutes. |
| Dragon dies | The finale: a full-screen shatter of anything left, then the victory card. |

The level lasts about 15-17 minutes. A good build reaches 6 level-ups, so
V1's run holds about 8 picks: the first offer, the level-ups and the Ogre's
treasure. The dragon's treasure will open level 2.

### Starting

- Title: **Play**, **Calibrate**, **Quit**. Play opens the music picker:
  apps currently playing audio, then **File...** (a file or playlist).
- The first offer appears while the music's first bars play; the world is
  empty and held until the pick.

### Pauses: only the player's

- The world never pauses for the music. A song ending, a gap, a beatless
  intro or the music stopping outright are all play as usual: monsters glide
  instead of stepping, judgement counts every press on the beat, and held
  cards get their quiet moment.
- Esc or losing focus pauses: **Resume**, **Calibrate**, **Quality**, **End
  run**. The music plays on (it is the source app's).
- Cards hold the world (Decision 12). Nothing else does.

### Ending

- **Death**: the hero shatters into white shards on the killing frame. The
  camera holds; 1.5 s later the card shows, unlit: time, kills, the build's
  icons, **Again**.
- **Victory**: the dragon's shatter, then the same card, lit.
- **Again** (Enter or a click) starts a new run on the same music source,
  which keeps playing. Esc goes to the title.

### Difficulty

Beatable in a few tries (Decision 28). Targets for tuning:

- An idle hero dies within 90 s.
- A hero who never upgrades its dash struggles past minute 8.
- A first run usually reaches the Ogre; the dragon falls after 2-4 runs of
  learning the dash and the patterns.

Balance is tuned by hand-play: each tuning bead records what it changed and
puts a play check on the operator-pending list.

## Controls

| Input | Action |
| --- | --- |
| WASD or arrows | Move, 8 directions; diagonals as fast as the rest; opposite keys cancel. |
| Left click | Walk straight to the clicked ground point; holding the button follows the cursor. A movement key drops the target. |
| Space | Dash. |
| Cards | Click a card, press 1-3, or arrows and Enter (or Space). |
| Esc | Pause. |

- No gamepad in V1.
- No path finding: a click target behind a structure leaves the hero
  against it.
- A dash that passes its click target clears the target, so the hero never
  walks back to it.

## The Hero

- **Model**: the Synty knight with sword and shield, in the edge look in
  white, as in the trailer, with the blade's bright core.
- **Movement**: 6 m/s, always. Enemies never block the hero; touching them
  hurts.
- **Health**: 100. A hit gives 0.5 s of invulnerability. Level-ups heal 25%,
  a boss kill heals fully. Being hurt flashes the health bar and the hero
  once (no blinking).
- **Animation**:
  - Sword Slash alternates the reference trailer's clips, `LightCombo01B` and
    `LightCombo01A`, with `HeavyFlourish01` for spins. Each impact frame lands
    on its beat.
  - One clip per swing, never cut short, never faster than 1.15x.
  - The legs keep the walk under an upper-body layer; the torso twists to the
    aim. The aim is chosen at the wind-up and held through the swing: no
    re-aiming mid-swing.
  - Each dash type has its own pose or clip (see [Dashes](#dashes)).

## The Dash

### Rules every dash keeps

- **Space only.** Exactly one dash is equipped; a new one replaces it.
- **On the key-down frame.** The drawn hero leaves its spot on the frame
  Space goes down. The sim resolves the dash from when the key was heard,
  so the sim's lead over the speakers never delays it.
- **Fixed distance and duration per dash type**, the same on the beat or
  off it, at every tempo.
- **Cooldown 0.6 s** from the press, for every dash type (Quickstep lowers
  it). A press in the last 0.15 s fires the moment the dash is ready, judged
  by when it was heard; an earlier press is dropped, and the pip blinks.
- **I-frames** for the dash and 0.1 s after, for every dash type.
- **Direction**: the held movement keys (8 ways), else toward the click
  target, else the way the hero faces.
- **Structures**: an intact structure stops a dash, which slides along it.
  A damaging dash hits it first, and if that breaks it, carries on through.
- **Enemies** never stop a dash.

### On the beat

- Binary: on the beat or not, no grades.
- Windows, in heard time: 80 ms either side of a beat; 60 ms either side of
  a half beat or a strong onset that the music actually plays there.
- No reliable beat: every press is on the beat.
- **Payoff**: 2x damage over 1.5x area for that dash's hits; a bigger,
  brighter effect, a stronger beat-wave ring from the hero, and an accent
  sound layered on the dash's own.
- The plain Dash has no damage, so on the beat it only looks and sounds
  bigger, until a mod gives it damage.
- Nothing punishes pressing off the beat or not dashing.

### The indicator

- A white strip about 1.2 m wide, floating over the hero's head, drawn in
  the world.
- The next two beats' ticks slide in from both ends to the centre, arriving
  on their beats. Half beats are not drawn.
- The centre pip is the dash: a dim ring filling while it cools down, solid
  and bright when ready, a flash on an on-beat dash.
- A small dot beside the pip lights while a level-up is held for its quiet
  moment.
- Under the strip, the thin white health bar.
- With no reliable beat there are no ticks, only the pip.

## Attacks

### Rules every attack keeps

- **On the beat grid.** Every attack fires on beats, half beats or downbeats
  of the committed beats. With no reliable beat it fires on a fixed pulse in
  seconds.
- **Sword Slash's period** is 1, 2 or 4 beats, whichever is nearest 1 s
  (ties go to the longer). It is chosen when the beat locks and kept while
  the tempo stays within 8% of that, changing only on a downbeat, so a song
  near a boundary never flickers. 128 BPM: every 2 beats (0.94 s). 174 BPM:
  every 2 beats (0.69 s). 80 BPM: every beat (0.75 s). No beat: every 1 s.
- **Aim**: the densest group of enemies in reach, else the nearest in reach.
  With nothing in reach plus 1 m, the swing is skipped and the hero holds a
  ready stance; the beat count carries on.
- **Spectral, not faster.** When an upgrade adds strikes (Tempo, Twin
  Strike, rank 3s), the extra ones are drawn as white spectral slashes or
  phantoms. The hero's body plays one clip per swing at natural speed.
- Bosses are aimed at like any enemy, never weighted.

### The attacks

All damage is relative to Sword Slash's base hit (1x). Ranks change kind;
evolutions come only from treasure.

| Attack | Rank 1 | Rank 2 | Rank 3 | Evolution |
| --- | --- | --- | --- | --- |
| **Sword Slash** (start) | Arc of 150 degrees, 3 m reach, every period | 210 degrees, 3.8 m | Every fourth slash is a spin: 360 degrees, 1.5x | **Tempest**: every slash throws a crescent wave 12 m |
| **Orbit Blades** | 2 white spectral swords circle at 2.5 m, one lap per bar, 0.6x | 3 swords | 4 swords; the ring pushes out to 4 m on downbeats | **Blade Halo**: 6 swords, the ring breathing 2-5 m with every beat |
| **Blade Rain** | Every 2 beats a sword drops on the densest spot within 10 m, 1.5 m impact, 1.2x | Every beat | 2 swords at a time | **Sword Storm**: 8 swords burst down on each downbeat |
| **Crescent** | Each downbeat a wave rolls 10 m the way the hero moves, 4 m wide, 1x | Front and back | Four ways | **Full Moon**: a ring wave to 14 m each downbeat |
| **Phantom** | A white echo knight beside the hero repeats each Sword Slash a beat later, 0.7x | It also repeats the hero's dash | Two phantoms | **Legion**: three phantoms around the hero |

- Spectral swords, phantoms and crescents are the hero's: white, but dimmer
  than the hero (they sit low on the hero light rung).

## Dashes

| Dash | Move | Rank 2 | Rank 3 | Evolution |
| --- | --- | --- | --- | --- |
| **Dash** (start) | 4 m in 0.15 s, no damage | - | - | - |
| **Blade Rush** | 5 m in 0.22 s through the crowd, three fast slashes hitting everything within 1.5 m of the path, 1.5x | Path 2.5 m wide, a fourth slash | The path leaves a cut that strikes again on the next beat | **Thousand Cuts**: afterimage blades slash along the path on each of the next two beats |
| **Leap Slam** | 5 m leap in 0.35 s (i-frames throughout), landing shockwave 3 m, 2x | 4 m shockwave, and a ring rolls on to 7 m on the next beat | An aftershock on the next downbeat | **Skyfall**: a higher leap; the landing shatters everything within 6 m and sends a ring to 12 m |
| **Shield Charge** | 6 m in 0.3 s, shield forward; enemies in a 2 m path struck (1.2x) and thrown to its sides | Thrown enemies damage what they land on | Ends in a shield bash cone | **Juggernaut**: 8 m, and everything thrown shatters on landing |
| **Blink Strike** | 6 m at once (0.05 s), a slash burst of 2.5 m at both ends, 1.5x | A third burst at the midpoint | The arrival burst repeats on the next beat | **Phase Walk**: a white phantom at the origin repeats the burst on every beat for a bar |

- Poses: Blade Rush uses `HeavyStab01` into spectral slashes, Leap Slam
  `LightLeaping01`, Shield Charge the forward parry pose held, Blink Strike
  a vanish and reappear with a burst. The plain Dash holds a long low stride.
- Distances and durations are per dash type and fixed; an evolution is a new
  form and may change them.

## Mods

A mod applies to everything carrying its tag. The tags:

| Tag | Carried by |
| --- | --- |
| Strike | Sword Slash, phantom slashes, Blade Rush's slashes, Blink Strike's bursts |
| Area | Anything with a radius or an arc: Sword Slash, Leap Slam, Crescent, Blade Rain, bursts, shockwaves |
| Dash | Every dash |
| Summon | Orbit Blades, Phantom, Blade Rain's swords |

| Mod | Tag | Rank 1 | Rank 2 |
| --- | --- | --- | --- |
| **Wide Arcs** | Area | +40% area | +80% |
| **Twin Strike** | Strike | Every strike echoes half a beat later at 50%, spectral | 75% |
| **Tempo** | Attacks | Sword Slash and Crescent fire one step faster on the beat grid (the extra strikes spectral) | Every attack does |
| **Keen Edge** | Strike | +50% damage; hits on a downbeat deal 2x | +100% |
| **Quickstep** | Dash | Cooldown 0.6 to 0.45 s | 0.35 s |
| **Momentum** | Dash | Dash damage 2x, and every dash, the plain one too, leaves a slash trail along its path that hits once, 0.8x | The trail strikes again on the next beat |
| **Afterimage** | Dash | A white afterimage repeats the dash, path and damage, a beat later | Two, a beat and two beats later |
| **Shatterburst** | Kill | A kill's shards cut its neighbours for 30% of the killing hit | 60% |
| **Lifeblood** | Kill | Every 25 kills heal 2% | 4% |
| **Encore** | Dash | An on-beat dash makes the next strike within a beat deal 3x | The next two strikes |

"One step faster on the beat grid" halves an attack's period: 4 beats to 2,
2 to 1, 1 to a half beat.

## Level-ups and Treasure

### Experience

- Every kill adds experience on its impact frame: fodder 1, bats 0.5, elites
  10, the boss entrance's mass kill counts in full. Nothing drops.
- The curve is tuned so a good build levels about every 2 minutes, starting
  near 1:30; killing faster levels faster.
- The hairline bar along the screen's bottom edge fills with it.

### Offers

- Three different cards, pick one. No reroll, skip or banish.
- **The first offer**: one new dash, one new attack, one new mod.
- **Later offers** draw from what the build can take: a new attack while an
  attack slot is free, a damaging dash the hero doesn't have, a new mod while
  a mod slot is free, and a rank of anything owned below its top. At least
  one card builds on something owned whenever possible.
- **Slots**: one dash, three attacks (Sword Slash plus two), four mods. A new
  dash replaces the current one.

### Held for a quiet moment

A level reached is held, with the indicator's dot lit, until the first of:

- a gap: loudness under the silence threshold for 0.75 s (a song change);
- a beatless stretch: no reliable beat for 2 s;
- 60 s after it was reached, then on the next downbeat (at once with no
  beat).

During a boss duel held levels wait for the boss's death and show after its
treasure. Several held levels show one after another.

### The card moment

- The world holds: monsters keep bobbing to the music, the beat wave keeps
  sweeping, nothing moves or attacks, the run clock stops.
- All three cards appear together in under 0.2 s and can be picked at once.
- Play resumes on the frame of the pick.
- Keys held when the cards opened are ignored until released, so the key
  that picks never walks the hero afterwards.

### Treasure

- Dropped by the Ogre (the dragon's waits for level 2) and opened as the
  boss shatters, in under 0.3 s, with no roulette.
- Three cards: the evolution of every owned rank-3 attack or dash, then
  "raise to rank 3" for the highest-ranked owned attacks and dash below it.
  If fewer than three qualify, mod ranks and new mods fill the rest.

### Cards

- A white line-art icon, the name, one line of at most six words ("Hits echo
  half a beat later").
- The card's outline shape tells its kind: dash, attack or mod. Pips show
  the rank it gives.
- No heading, no hint line, no "NEW!". Treasure cards are the same, with an
  evolution's card drawn larger.

## The Horde

### The cast

The family slots are the palette's ([The Palette](#the-palette)). Speeds are
averages; the beat sets when each step happens.

| Type | Role | Slot | Size | Steps | Speed | Threat |
| --- | --- | --- | --- | --- | --- | --- |
| Skeleton | fodder | key, light | 1.8 m walker | every beat | 1.5 m/s | touch |
| Slime | fodder | key, light | 0.9 m blob | hops every beat | 1.2 m/s | touch |
| Demon | fodder | key, deep | 1.9 m walker | every beat | 1.8 m/s | touch |
| Bat | fodder, swarmer | key, deep | 0.3 m flyer | half beats | 2.6 m/s | touch, low health |
| Goblin | fodder | accent | 1.2 m walker | every beat | 2.0 m/s | touch |
| Mushroom | ranged (second half) | accent | 1.0 m | every 2 beats | 0.9 m/s | spore shots |
| Big ork | elite | counter | 2.4 m | every 2 beats, stomps | 1.4 m/s | charge |
| Troll | elite | counter | 2.6 m | every 2 beats, stomps | 1.2 m/s | ground slam |
| Fat Ogre | mid-boss | counter | 5 m | - | - | duel |
| Dragon | boss | boss | 12 m wingspan | - | - | duel |

### Moving to the beat

- A monster steps on its beats: the step's motion starts on the beat with a
  sharp ease-out and settles within 40% of the interval. Its length is its
  speed times the interval, so its average speed never depends on the tempo.
- Steps start on the beat everywhere, at every distance from the hero. The
  beat wave's light and bob ride the ring outward, but steps never wait for
  it.
- **No reliable beat**: monsters glide continuously at the same average
  speed. They never stop.
- **Dark gaps**: monsters keep apart, a little more than their bodies'
  radii, so a crowd reads as many lit shapes with black between them, never
  one coloured mass.
- Monsters steer around structures. They never break them.

### Threats

- **Touch**: a monster whose step lands it against the hero bites on that
  beat, for 5. With no beat, a touching monster bites every 0.5 s.
- **Big ork's charge**: a yellow warning line toward the hero grows over two
  beats; on the beat after, the ork charges 8 m along it. 20 damage.
- **Troll's slam**: a yellow ring of 3 m fills around the troll over two
  beats and slams on the next. 20 damage.
- **Mushroom's spores** (after the Ogre): its cap flashes yellow a beat
  before; on the beat it fires a yellow spore at the hero, 7 m/s, 14 m
  range, every 2 bars. 10 damage. Walk aside or dash through it.
- With no beat, telegraphs take 0.5 s per beat.

### Toughness

- Fodder health ramps over the level so that unupgraded Sword Slash kills
  fodder in one hit at the start and needs three or four by minute 12. A good
  build one-shots fodder all level.
- Elites take about 8x fodder health.

### Spawning

- **Ramp**: 150 alive at the start, 600 by minute 12, from just off-screen
  in a ring around the hero.
- **Formed waves** on strong section changes, entering on the downbeat:
  rings closing on the hero, lines sweeping across, a swarm of bats.
- **Surges**: loud sections push toward 1,000 alive; quiet ones thin out.
- **No music**: the ramp alone.
- **Bosses**: spawning stops 4 s before an entrance and resumes after the
  boss dies.

## Bosses

### Every boss

- **Arrival**: on the first strong downbeat 4 s or more after spawning stops
  (Level 1's table), within 60 s; with no reliable beat, 4 s after spawning
  stops.
- **Entrance**: the boss lands on that downbeat and its shockwave shatters
  every mob and structure on screen outward from it, the level's biggest
  moment. The kills count for experience.
- **The duel**: no spawns. No new structures within 40 m. The boss follows
  the hero across the endless grid.
- **Telegraphs** fill over whole beats and the attack lands on a beat, in
  `boss.telegraph` red. With no beat, 0.5 s per beat.
- **No stagger, no knockback**: hits never interrupt a boss. Its attack
  clips always play.
- **Perfect dodge**: a dash whose i-frames cover the moment an attack lands
  on the hero, pressed on that beat. It fills Break by a quarter.
- **Break**: a full bar staggers the boss for one bar of music (2 s with no
  beat), taking 2x damage. The bar empties.
- **Phase two** below 50% health: telegraphs shrink to one beat and the
  moves change as listed.
- **The boss bar**: a slim health bar at the top of the screen in the boss's
  colour, with the Break bar in white under it.

### Fat Ogre

| Move | Telegraph | What lands |
| --- | --- | --- |
| Overhead smash | A line in front of it, 2 beats | The club along the line |
| Stomp | A ring around it, lands on the downbeat | A shockwave ring |
| Charge | A long line across the ground, 3 beats | It charges the length of the line |
| Boulder throw | Marked circles near the hero, 2 beats | Boulders on the circles |

Phase two: one-beat telegraphs; smash then stomp chained. Target duel
length 60-90 s for a good build.

### Dragon

| Move | Telegraph | What lands |
| --- | --- | --- |
| Fire breath | A red cone, 2 beats | Burns for one beat |
| Tail sweep | A ring around it, lands on a downbeat | A ring strike |
| Dive | Takes off; marks the hero's spot | Lands on beat 4 as concentric rings |
| Fireballs | Three marked circles | One per beat |

Phase two: one-beat telegraphs, the breath sweeps 180 degrees, the dive
chains twice. Target duel length 2-3 minutes. Its death is the finale.

## The World

### The infinite arena

- A black void with a quiet grid, extending forever. The world is generated
  in chunks around the hero from the run's seed, so a run replays exactly.
- Structures stand in loose clusters (a hamlet, a grove, a lone tower) with
  open lanes between them and empty chunks around them. Level 1 has only
  breakable structures.
- A broken structure stays broken, even if the hero comes back.

### Structures

- 6-8 kinds from the local Synty packs: cottages, a tower, wall and fence
  runs, two kinds of tree, rocks.
- **Look**: the edge look over a dim fill in the zone's key colour, so they
  read as solid shapes, clearly apart from the monsters' dark bodies.
- **Play**: they block the hero and the monsters. Any hero damage that
  reaches one breaks it: a small one in one hit, a building in two or three.
  Boss attacks and entrances break them too.
- **Shatter** on the impact frame, in their fill colour, thrown away from
  the blow.
- **Grey line art** means unbreakable. It is reserved for later levels.

### Zones

- Level 1 has four zone keys: two cool, a violet and a dim warm. The palette
  bead picks their hues.
- At each strong section change the floor's key crossfades to the next zone
  over a beat, swept outward from the hero by the beat wave. Structures take
  the new key; monsters keep their colours.
- The warm zone is the dragon's: the floor turns warm at its entrance.
- With no section changes detected, the key moves on every 3 minutes.

## Music

### Sources

- **Delayed listening**: the player picks an app that is playing audio. The
  game taps it, mutes it and plays its audio 3 s late, so the analyzer hears
  every beat before the player does.
- **File**: a local file or playlist, played through the same delay and the
  same analyzer, so file runs test the live path faithfully.
- No Direct listening (hearing the app live).
- Calibration: a video screen (align a flash with a click) and an input
  screen (tap along), stored per output device, reachable from the title and
  the pause menu.

### What the music drives

| Drives | How | Without a beat |
| --- | --- | --- |
| Attacks | Fire on the beat grid | A fixed pulse in seconds |
| Dash payoff | On the beat, 2x and 1.5x | Every press pays |
| Enemy steps | Step on their beats | Glide at the same speed |
| The beat wave | A ring per beat | None, only event rings |
| Telegraphs | Whole beats, landing on a beat | 0.5 s per beat |
| Spawn surges | Loudness, formed waves on section changes | The ramp alone |
| Zone changes | Strong section changes | Every 3 minutes |
| Boss arrival | The first strong downbeat in its window | 4 s after spawning stops |
| Card timing | Gaps and beatless stretches | Gaps, then 60 s |

## The Look

Reopened on 2026-11-02. The first version of this section misread the
reference trailer as broken and replaced its drawing with "edge cuts": a few
chosen lines over a dark body, gated by a black-share threshold. They passed
every number and were a severe visual regression, so they were deleted. This
section starts again from the trailer.

### The aim: retro 3D neon wireframe

The look is **retro 3D neon wireframe**, grounded in synthwave: low-poly 3D
models drawn as glowing lines along their edges on black, like the wireframe
mountains, palm trees and cities of 80s synthwave art. The operator's
references are in `captures/style-references/` in the main checkout. They're
third-party images, so they stay outside the tree:

- `synthwave-mountains-palm.png`: low-poly mountains and a palm tree with
  every facet outlined, over a neon grid floor and a striped sun.
- `wireframe-mountains-grid.png`: triangulated mountains either side of a
  grid road.
- `wireframe-objects-elephant.png`: a low-poly elephant head, a sphere, a
  torus and a terrain patch, every edge drawn.
- `neon-wireframe-plane-tunnel.png`: a paper plane of a dozen glowing edges
  in a neon tunnel.
- `synthwave-city.png`: towers drawn as neon edges of simple faceted solids.

What they share, and what the monsters aim for:

- **A 3D model, not a flat drawing.** Each object is a model with a
  deliberately simple, low-poly topology, and its edges are the lines. The
  lines describe volume: facets, planes, bands around a form.
- **Built from simple shapes.** A mountain is a few big triangles, a palm
  frond a strip of quads, a building a box. A monster is usually best built
  the same way: a few simple faceted volumes (head, torso, limbs, weapon)
  joined together. It's a guideline, and some monsters need more.
- **Every line connects.** Each facet is a closed polygon and lines meet at
  vertices, so the silhouette is closed too.
- **Hidden lines are removed**: a dark body hides the lines behind it, and
  the inside of every facet stays dark.
- **One neon hue per object**, an even line weight and a soft glow.

What misses it:

- lit facet patches or glassy fills (the first port's stills);
- broken or blotchy scratches (the deleted edge cuts);
- so many tiny facets that the lines merge into a fill (the trailer's
  goblins).

### The reference: the trailer

The closest the game has come is the neon action trailer in the frozen
demo3d's edge preset, the operator's capture
`captures/trailer-action-edge.mp4` (outside the tree; made by
`just trailer-looks action`). **It looks good.** It is the renderer's
baseline: Rhythm Champions starts from exactly that look and changes it only
where a change looks better. Where it falls short of the aim, the cause is
mostly the vendor meshes. Their dense, uneven topology was made for textured
rendering, so some types scribble or fill in. The monster meshes fix that.

What the trailer does, described rather than prescribed:

- **Monsters** are their vendor meshes drawn by the look package's
  `MonsterEdge.hlsl`. Every crease sharper than 24 degrees is a thin line in
  the type's colour, a little brighter toward the silhouette. Underneath is a
  dark glass core whose facets are barely visible, so the lines carry the
  figure. A fresnel rim traces the outline, and a small inner glow flares on
  the beat.
- **Each type has one hue**, levelled to one luminance. A crowd reads as
  fields of colour, and each monster still reads as a figure: helmets,
  shields, weapons, limbs.
- **The hero** is drawn the same way in white, the brightest thing on screen.
- **The floor** is void under a quiet grid. Light comes from things.
- **Shards** keep the look: each piece is outlined along its crack, hottest
  on the break's frame.

Stills from the capture, in [reference/](reference/):

- [opening-duel.jpg](reference/opening-duel.jpg): three brutes and the hero
  up close, every plate of their armour outlined. The look at its best.
- [dragon-live.jpg](reference/dragon-live.jpg): the dragon in its duel (the
  capture at 0:58), long clean strokes for the wings and body.
- [shield-circles.jpg](reference/shield-circles.jpg) and
  [dense-swarm.jpg](reference/dense-swarm.jpg): the crowd. The skeletons
  read by their shields, and the orks by their bulk. The magenta goblins
  read as filled pink shapes rather than line art (see below).
- [dragon-shatter.jpg](reference/dragon-shatter.jpg): the dragon
  shattering. A reference for the shatter, not for a living monster.

### Changing the look

Changes from the trailer are targeted, and they are judged by looking:
side by side with the trailer's drawing of the same thing, at game scale and
in a crowd. The trailer's own drawing stays one flag away
(`--look trailer`), so any change can be compared directly.

Where to start (things to try, not orders):

- **The goblins** read as filled magenta objects, not retro vector art. Their
  small dense facets light so many creases that at crowd scale the lines
  merge into a fill.
- **Each monster gets a mesh designed for the look** ([Monster
  meshes](#monster-meshes)).
- Detail that turns to noise at game scale can go. Detail that names the
  type stays.

### Monster meshes

Each of the ten monsters gets a low-poly mesh designed as a retro 3D neon
wireframe ([the aim](#the-aim-retro-3d-neon-wireframe)), made from its
vendor model with the project's `monster-mesh-design` skill, one bead per
monster. The goal is a model whose edges draw a coherent, handsome wireframe
figure at game scale, in a crowd and up close, and that still moves with the
vendor rig and clips.

- **Simple faceted shapes joined together** are the usual way, as the
  references are built. It's a guideline, not a rule: some monsters need
  detail kept.
- **The topology is the drawing.** A designed mesh may draw every one of its
  edges (a true wireframe, as the references do) or only its creases over 24
  degrees, as the trailer does. The recipe says which, judged by eye.
- A committed recipe builds each mesh locally from the vendor files, and its
  output is never committed, like every vendor-derived file.
- Until the operator prefers a new mesh, the vendor mesh as the trailer drew
  it stays one flag away.

### Judgment over rules

- Everything in The Look is guidance under aesthetic judgment. When a
  guideline and a better-looking frame disagree, the frame wins.
- Numbers (luminance, lit share, line counts, separation) are diagnostics
  for finding problems. They never accept or reject art.
- Art is accepted by looking:
  - a blind critique of its stills by an agent that hasn't read the docs;
  - side-by-sides with the trailer's drawing of the same thing;
  - in the end, the operator's eye.

Habits the trailer keeps, worth keeping unless a frame says otherwise:

- **Light comes from things, not from the world.** Edges, rims, trails,
  sparks and the grid emit; the floor is void.
- **White is the hero's**: the hero, its effects and its summons. **Grey
  means a wall.**
- **A swarm reads as many figures with dark gaps between them**, not one
  coloured mass. This is what the goblins miss.
- **No monster is brighter than the hero.**
- **A frame tends to have a colour key**, one family holding most of it.
- **Point lights sparingly**, for effect: a slam, a boss entrance.

The archived style guide's value ladder is a rough guide: void for the floor
and the cores, ground light for the grid and fills, enemy light for monsters'
lines, hero light above it, and clipped white only for impact frames and the
blade's core.

### The palette

The trailer's families, with the empty arc given to roles (Decision 21):

- **Key** (the most numerous fodder: skeleton, slime, demon, bat): cyan to
  blue, in the floor's first zone family.
- **Accent** (goblin, mushroom): magenta to violet.
- **Counter** (big ork, troll, Fat Ogre): amber to orange.
- **Boss** (dragon): red, inside the counter family's reach.
- **Warning**: electric yellow, for elite telegraphs, spores and any hazard
  short of a boss. Boss telegraphs stay red.
- **Zones**: two cool, a violet, a dim warm.
- **Hero**: white; its trail pure white (no longer in the key family).
- **Wall**: grey, reserved.
- New roles where the old palette had none: warning, enemy shot, hero hurt,
  structure fill per zone, Break.

The palette bead fits values to the style rules and writes the new style
guide.

### The beat wave

- Every beat a ring leaves the hero's feet and travels about 11 m per beat.
- As it passes: grid lines brighten and bend slightly (Geometry Wars'
  ripple), monsters' lines flare and they bob, structures' edges flare.
- Downbeats push harder. An on-beat dash, a slam, a boss entrance and a mass
  kill send stronger rings of their own.
- The grid settles back to ground light within half a beat. Even a
  downbeat's ring never lifts the floor above ground light.
- With no reliable beat, there are no beat rings; event rings still go out.

### Shatter

- **Monsters** break on the impact frame into pieces of their mesh, each
  outlined along its crack as in the trailer, thrown forcefully away from
  the blow and up, tumbling, then sinking. Weapons break and fly with the
  body. No cracks first, no delay, no twinkling.
- **Structures** break the same way in their fill colour.
- **Boss entrances** shatter the whole screen outward from the boss.
- **The hero's death**: white shards from the hero.

### Weapon effects

- Hovl's slash shapes (the crescent mesh's taper, its noise streaks, sparks,
  fast-in slow-out timing), imported locally and drawn through our own
  shader: a white core, a faint white-blue edge.
- On the beat: bigger and brighter. Special moments: the bright versions.
- Every effect of the hero's is white; every enemy effect takes its owner's
  colour or the warning yellow.

### Camera

- The reference trailer's high angle, about 60 degrees down, following the
  hero with a slight lead the way it moves. It never rotates.
- It eases out about 20% for boss duels.
- Punches and shakes only on special moments.

### Restraint

The look is calm by default. **Special moments**, the only events that may
flash the frame, shake the camera or bend the whole grid: boss entrances,
boss deaths, Break, slams and evolved hits, on-beat damaging dashes, mass
kills, structure breaks. Even these never white out the frame, and their
flash is gone within a quarter second.

### Quality tiers

- **Progressive enhancement**: the look is designed at full quality; tiers
  shed extras, never the core look.
- The core, kept on every tier: the edge look, the beat wave, the grid,
  shatter (with fewer pieces on the low tier), bloom.
- The extras, high tier only or scaled: full-resolution bloom, distortion in
  slashes, glow halos on lines, more shatter pieces, chromatic flashes on
  special moments.
- **Low tier**: an M1 Air class Mac holds 1,000 monsters at 60 fps at 1080p.

## Sound

- The hero's sounds: swings, impacts, dash whooshes, shatters, an accent
  layered under on-beat dashes, and the bosses' telegraphs and hits.
- Sounds start the moment the sim knows them, in heard time; they never wait
  for the beat grid.
- They sit under the music: the music is the star, never ducked except under
  the pause menu and calibration.

## UI

- **In the world**: the hero's indicator (beat ticks, dash pip, held-level
  dot, health bar). Telegraphs are drawn on the ground.
- **HUD**: the hairline experience bar along the bottom edge; in a duel the
  boss bar and its Break bar at the top. Nothing else: no timer, kills, slot
  rows, damage numbers or banners ("MUSIC STOPPED" is gone).
- **Cards**: [Cards](#cards). Clicks always work.
- **Menus**: one or two words a row. Title (Play, Calibrate, Quit), music
  picker, pause (Resume, Calibrate, Quality, End run), the end card.
- **Text budget**: during play no text at all, except the cards' names and
  lines.

## Future Work

Out of V1, recorded so the design leaves room:

- **Later levels**, each with its own monster look: crystal, fire, void and
  more from the earlier style lab, and death animations beyond the shatter
  for them.
- **Status and ground effects**: freezing, burning, poison, burning ground,
  each with its own shader design. "Teleport and leave a trail of fire"
  waits for these.
- **Other weapons**: bows, spears, hammers, axes, shotguns, assault rifles,
  grenades, spells, each with its own attacks, dashes and mods.
- **Environmental variety**: more structure sets, terrain, grey
  unbreakables, landmarks. The edge look's risk is monotony; test variety
  after V1.
- **Level 2 onward**, and the dragon's treasure that opens it.
- **Windows** (process loopback capture, already spiked), gamepads.
- **A bot** for balance sweeps and a **staged trailer mode** like the
  frozen demo3d's.
- **Meta-progression** between runs: not designed.

## Open Questions for Play-testing

None of these gates anything; tuning beads answer them with recorded
reasons.

- Is 0.6 s the right cooldown, and does the 0.15 s buffer feel like a dash
  that never drops a press?
- Are 80 ms and 60 ms generous enough with live latency, and does the strong
  onset window feel fair on syncopated music?
- Does Sword Slash at about once a second read as rhythmic, or as too slow
  next to the dash?
- Does the boss entrance's mass shatter land as the level's biggest moment,
  and is a quarter Break per perfect dodge the right pace?
- Do four zone keys give level 1 enough variety, or does it still feel
  monotonous?
- Is 1,000 alive the right cap for the screen's readability, as well as for
  the frame budget?
