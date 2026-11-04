# Boss Rush boss framework (ps-8z4.3)

The foundation every Boss Rush fight bead builds on ([boss-rush.md](../../../../docs/champions/boss-rush.md), Fights and
Look). This page is the fight beads' guide: what exists, where, and how a boss uses it.

## What a fight bead writes

1. **A sim module**: `Assets/Champions/Sim/Rush/<Boss>Module.cs`, a `BossModule` subclass, plus one line in
   `BossModules`' table (`Sim/Rush/BossModule.cs`). Its `Id` is the boss's id and, by convention, its cast id in
   `Content/enemies.json` (the mesh bead's row; `Bodies` overrides it, for several bodies or other casts).
2. **Its tests** in `dotnet/Champions.Sim.Tests/Rush/<Boss>Tests.cs`, using the fairness harness (below).
3. **Its look** (optional): one line in `BossLook`'s table (`Render/Crowd/BossLook.cs`), keyed by its cast id.
4. **Its sounds**: a `bosses.<id>` bank in `Content/sounds.json` (entrance, tick, impact, signature, death).
5. **Its evidence**: `champions boss review <id>` into `evidence/boss-rush/<id>/`.

## The sim: `BossFight` and `BossModule`

`sim.Fight` (`Sim/Rush/BossFight.cs`) runs one module at a time, separately from the main game's `sim.Bosses` (the
main game's three bosses are untouched; every earlier sim test and the recorded sessions' replays still pass).

**In the run**: the mode shell (ps-8z4.1) counts a slot as landed once its id is a registered module with its cast row
in enemies.json (`BossRushBoss.Fight`); it then starts the slot's fight with `sim.Fight.Start(id, t, new FightSetup {
Act, Health = the slot's })` instead of calling the stand-in duel, and the fight's death calls the shell's `BossDied`
(rewards, the next arena). So registering the module and the cast row is all a fight bead does to put its boss in the
run. `FightSetup` has no arena field: the arena (ps-8z4.2) forms round the hero, which is the fight's default centre.

```csharp
sealed class BloomModule : BossModule
{
    public override string Id => "bloom";
    public override double[] PhasesBelow => new[] { 0.5 };        // optional phases
    public override int TransitionBeats => 4;                      // untouchable, at most 4 (Decision 8)

    protected internal override void Beat(BossFight f, double t, int beat, bool downbeat)
    {
        if (f.Stage != FightStage.Fighting || !downbeat) return;
        ref readonly var b = ref f.Bodies[0];
        int attack = f.NextAttack();
        for (int arm = 0; arm < 4; arm++)                           // four rotating arms of spores
            BossKit.Beam(f, 0, b.X, b.Y, arm * Math.PI / 2, 14, 0.6, spinRadPerS: 0.8, activeS: 2, damage: 12, beats: 3, attack: attack);
    }
}
```

- **Starting**: `sim.Fight.Start(id or module, t, new FightSetup { Act, Health, CentreX/Y })`. It emits `FightCall`
  (the client warms the boss's look), then the bodies arrive on the first downbeat at least `bosses.json`'s `waitS`
  later (each a `BossArrive`), then `EntranceBeats` before the module's first `Tick`. `SimConfig.Fight` /
  `RunInputs.Fight` start one with the run (recorded in sessions from v7); `champions play --fight <id>` plays one.
- **Hazards** (`Sim/Rush/Hazard.cs`): a shape, a telegraph from `f.Plan(beats, Landing.Beat|HalfBeat|Downbeat)`
  (never under the act's minimum: 3 beats in acts 1–2, 2 later, and never under 0.6 s), damage, and optionally
  `ActiveS` (lingers), `Vx/Vy` (travels: projectiles launch on the telegraph's beat), `GrowMps` (a ring grows),
  `SpinRadPerS` (a cone or line turns), `BodyMove` (a charge or dive moves its body), flags `DashWave`,
  `Projectile`, `Signature`. Each hits the hero at most once; dash i-frames work as in the main game. Give every
  hazard of one attack the same `f.NextAttack()` number: the harness checks attacks whole.
- **They draw** as the main game's boss marks (`BossTelegraph`/`BossMove` events, ids from `BossFight.FirstMarkId`):
  the danger red fill, held through `ActiveS`, reshaped as they move. The boss's hue goes on its body and effects.
- **Building blocks** (`Sim/Rush/BossKit.cs`): the main game's moves as pieces (`Smash`, `Stomp`, `Boulders`,
  `Charge`, `Dive`, `Breath` with a sweep, `Fans`, `Wave` with fronts spaced for the dash, `Wall`) and new ones
  (`Beam`, `Shot`, `Volley`). Their shapes are the main game's own functions (`SmashLine`, `ChargeLine`, `Boulder`,
  `Fan`), which `Bosses.Moves` now draws with too. Body helpers on `BossFight`: `Follow`, `MoveBody`, `Face`, `HeroAt`.
  `Beam`, `Wave`, `Shot` and `Volley` take an optional `move` (the clip it plays: the boss animator plays the clip role
  of a hazard's `BossMove` at its part 0's landing, e.g. `Breath` → `breath`, `Smash` → `smash`, `Stomp` → `stomp`,
  `Fireballs` → `fireball`; ps-8z4.10).
- **Phases**: below each `PhasesBelow` share the fight calls off its hazards, emits `BossPhase`, holds the boss
  untouchable for `TransitionBeats`, and calls `PhaseBegins` (lay a set-piece there).
- **Several bodies** (the Wraith Council): `Bodies` lists `BodySpec(cast, x, y, share)`; each has its own health
  (the fight's `Health` split by share); `BodyDied` tells the module one fell; the fight ends when all have.
- **XP bursts**: at each `1/(ShedThresholds+1)` of health lost (9 by default: 90%, 80% ... 10%) the fight raises
  `Shed(sim, fight, burst, spots)` on the next beat with the module's `ShedSpots` (a ring by default; author your own
  geometry). A kill sheds the rest at once. In the run, the progression (ps-8z4.4) drops an equal step of the slot's
  XP budget per burst at those spots by their shares (`BossRush.FightShed`), and the kill drops the rest; a stand-in
  duel still sheds by bossrush.json's shapes.
- **Death**: each body's `BossDeath`; then `Died(sim, fight)` (the mode shell's reward and next arena).
- Randomness only from `f.Random`; module state on the module instance (a fresh one per fight).

The proof: `OgreModule` (`fatOgre`) is the main game's Fat Ogre on the building blocks, with its bosses.json moves
(smash, boulders, stomp, charge, the dash-through wave as its signature) and a phase at half health with a two-beat
transition. It isn't one of the 25 (Decision 10); it anchors the tests and can stand in for unlanded fights.

## The fairness harness

`dotnet/Champions.Sim.Tests/Rush/Fairness.cs` (boss-rush.md, Fair fights). Every fight bead's tests use it:

```csharp
[TestCase(90)] [TestCase(180)]                       // the slowest and fastest tempo it supports
public void TheBloomIsFair(double bpm)
{
    var report = Fairness.Check(() => new BloomModule(), new FairnessOptions { Bpm = bpm, Act = 1 });
    Fairness.AssertFair(report);
    Assert.That(report.DashWavesNeedingDash, Is.GreaterThan(0), "a real dash wave");
}
```

It plays the fight in a real sim (hero immortal and wandering, the boss worn down evenly over `FightS`, so every
phase plays), records every hazard that landed, and checks:

- each telegraph at least the act's beats and 0.6 s; each impact on a beat or half beat;
- no off-screen hits (the hazard's ground or its body within 14 m of the hero when it starts filling);
- dash waves (every front) at least the dash cooldown plus a beat apart;
- **a way out**: for each sequence of time-overlapping attacks, from *every* spot in the arena, a hero at the base
  6 m/s with one baseline dash (4 m, 0.15 s plus 0.1 s of i-frames, 0.6 s cooldown, still cooling if the previous
  sequence's last impact was that recent) has a hit-free path: a backward reachability over a grid, each hazard swept
  across the half step either side so nothing tunnels (a little stricter than the game).

It also counts the dash waves walking alone can't escape from where the hero stood (`DashWavesNeedingDash`). The
arena: `ArenaRadiusM` (20 m) or the arena bead's outline through `Inside` and `ExtentM`. The harness's own tests prove
it catches a burning disc with no way out, short, off-grid and off-screen telegraphs and crowded dash waves.

## Look: the shared boss-look shader

`Render/Resources/Crowd/BossEdge.shader`: MonsterEdge's glass, rim, feature lines and core exactly, plus six
variations by float, each off at 0: `_Scan` (hologram scanlines), `_Crystal` (facet glints), `_Flow` (light flowing
along the edges), `_Glitch` (slices jumping sideways), `_Ghost` (a breathing dither dissolve) and `_Ember` (a hot core
and silhouette in `_EmberTint`, the boss hue's hot end). A boss opts in with a `BossLook` entry; its bodies then draw
with BossEdge instead of MonsterEdge. References for the frozen demo3d's crystal/spectrum/hologram styles (ps-a54.3)
informed the variations; nothing was copied.

**Variant count**: BossEdge has no keywords (`shader_feature` was allowed, but floats do the job and one boss is on
screen at a time), so the player gains one shader with MonsterEdge's single variant per pass, about **1 variant** of
the 1,000 budget, and no fight's look adds any. A fight that writes its own shader records its count in its evidence.

**No arrival hitch**: on `FightCall` the client's boss presenter draws the first body's type (its BossEdge or
MonsterEdge material) as a speck below the floor until the body arrives, so its pipeline state is built while the
arena forms. Other bodies' types and the shards' material are not warmed yet (see the operator check below).

## Sound banks

`Content/sounds.json`'s optional `bosses` holds each boss's bank by id: `entrance`, `tick`, `impact`, `signature`,
`death`, each `{ gain, clips }` by vendor file name (the manifest's sound entries; nothing vendor is committed).
`SoundBaker` bakes them as `boss/ID/ROLE/variant`; a fight's call binds its bank. The client plays: the entrance at
each body's arrival, a tick on each beat of each attack's telegraph (teaching the rhythm), the impact at each landing
(the signature for `HazardFlags.Signature` hazards) and the death with the last body's shatter. A role a bank lacks,
or a boss without one, plays the generic boss sounds. `fatOgre`'s bank is the proof, from clips the categories use.

## Review and launch

- `champions boss review <id> [--motion]` plays the still scene `fight-<id>` headlessly (the fight alone, six weapons,
  a wandering hero): a still every 5 s, the entrance, the first twelve attacks' telegraphs at 60% filled, each phase
  and the death, into `evidence/boss-rush/<id>/` with a README (its Notes kept). `--motion` adds 30 fps frames from
  4 to 12 s for a clip.
- `champions play --fight <id> [--fight-act N]` plays a fight by hand, no level. The mode shell (ps-8z4.1) adds
  `--boss N` (the boss by its place in the run).

## Added by the first fight (ps-8z4.6, the Treasure Goblin)

- **A fight's own arena under `--fight`**: `RunInputs.Build` finds the fight's Boss Rush slot. When that slot names
  an arena (bossrush.json `arena`), it turns the arenas on (`SimConfig.FightArena`), and the sim forms the arena round
  the hero before the fight starts. `champions play --fight ID` and `champions boss review ID` then play the fight in
  its arena. The review's hero wanders round the arena's centre.
- **The harness in the arena**: `FairnessOptions.Arena` forms the arena round the hero as the run does. The walls
  and props then block the hero, and the walkable floor is where it may stand, round the arena's centre.
  `ChaseShare` takes a share of the wander's spots beside the boss, so close-range moves play.
- **The reference player in fights** (`ScriptedPlayer`, `--walk positioning`):
  - It walks to a fight's lead body as it does to a duel's boss, steering round the arena's props (`Steer`).
  - It dodges a fight's landing only where the landing's ground reaches it.
  - It dashes a fight's dash waves as their front reaches it.
  - `BossRushProgressionTests.SustainedDamage` measures a landed fight against the fight itself.
- **Test kit** (`dotnet/Champions.Sim.Tests/Rush/RushTestKit.cs`):
  - `StandIns` unlands every fight, for the tests of the shell's and progression's own stand-in rules.
  - `Fighting`, `Boss` and `Strike` work whichever kind of fight a slot has.
- **Building blocks**: `BossKit.Wave(..., move:)` presents a wave as a move (its body's clip).
- **Clips**: `BossAnimator` falls back to a Boss Rush rig's own roles (smash→stab, attack→slice, a wave's
  stomp→cast). A body with a `run` clip runs above 3 m/s.
- **Flavour** (`Client/Bosses/FightFlavour.cs`): every fight's hazard landings throw sparks in the boss's palette
  colour, never the danger red. A shot's launch glints along its way, a signature landing bursts, and each XP burst
  sprays behind the boss. Its death bursts wide.
- **A module's beats when there is no beat**: the framework calls `Beat` only on real beats. A module that schedules
  from `Beat` keeps its own fallback beat in `Tick` (see `TreasureGoblinModule`).

## Added by Moonhowl (ps-8z4.14)

- **The harness at a death**: a hazard still filling when its fight dies never lands (a dead fight lands nothing), so
  `Fairness.Check` drops it from the report as it drops called-off ones; before, a claw planned just before the kill was
  checked against beats the run never recorded and read as off the grid.
- **Afterimages**: `BossPresenter` draws Moonhowl's body again at its places a few hundredths of a second back, dimmed,
  while its moon is full (`MoonhowlModule.FullMoon`); a fight wanting the same writes more instances into its type's
  crowd slots the same way.

## Verification

- `dotnet test rhythm_champions/dotnet`: all pass, rebased on the shell and arenas (15 new in `Rush/BossFightTests.cs`,
  among them the Ogre's fairness at 90 and 180 BPM).
- Unity EditMode (`champions test`): `BossRushTests` (the fight's bank ticking on the beats, the BossEdge material
  taking a look with no keywords, `--fight` and the review scenes' names) pass; BossEdge compiled for Metal with no
  keywords (vertex and fragment, one variant). The only failures were MusicTests needing the audio plugin's dylib,
  which this worktree didn't have.
- Not run here: a player build and `champions boss review fatOgre` (a build takes the CI's slot; the first fight bead's
  review is the first real render), and the variant count from `champions check --variants` (slow CI builds it).
