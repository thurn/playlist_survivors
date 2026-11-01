# The schedule: a 30-minute .NET run on a dump (ps-msz.6.2)

The sim plays schedule.json's minutes (`HordeConfig.Schedule`, the player's `--run`) over a full
entry dump of the survivors test playlist, `native/playlists/survivors-30.m3u` (2294 s of house,
hip-hop, accelerando, drum and bass and a solo piano aria, with gaps), written by native's
`ps entries` into `~/Library/Caches/playlist-survivors/entries/708ac14e….jsonl`. The harness,
[Run30](Run30/Program.cs), drives the run's level (`RunLevel.Config`, its music-driven events off:
they have their own run, game/evidence/music-events) as the client does (60 fps frames, each entry arriving
once the frontier passes its stamp), in the game's arena, with the starting sword, a hero that
stands still and never falls, and the headless start (after 2 s of clear beat):

```sh
dotnet run --project game/evidence/schedule/Run30 -c Release -- DUMP.jsonl game/evidence/schedule
```

[summary.txt](summary.txt), from 2026-10-31:

```text
song 2172.2 s, run clock 1800.0 s, phase Ended, end Victory
spawned 10720, killed 10720, most alive 300 (cap 300), bosses 20, chests 14
boss telegraphs: 2 charges, 1 slams; 0 structures broken by slams, 3 charge or slam hits on the hero
```

- **Never above 300**: the most alive in any frame is the cap.
- **Every role on its minute**: [spawns.csv](spawns.csv) has each minute of run clock's spawns by
  role, with its seconds of reliable beat and its kills. Each minute's roster spawns in it, except
  minute 12's (werewolf, ghost, skeleton): minute 11 fills the cap with skeletons (minimum 300,
  every 0.1 s), and minute 12 falls in the piano aria, with no reliable beat (0.0 s): enemies hold,
  nothing reaches the sword and nothing dies, so no slot opens, as native's rule has it (waves
  only refill what dies). Its roles spawn from minute 13.
- **Every boss**: [bosses.csv](bosses.csv) has all 20 scheduled bosses, each on a downbeat after
  its minute starts. Minute 12's and minute 21's waited for room under the full cap (bat4 at
  13:08, xlflower at 21:11), the slot kept from the waves until it landed.
- **Chests**: [chests.csv](chests.csv): 14 dropped, by the first boss of each minute with
  treasure whose tier rolled (minutes 10, 15, 20 and 25 are scripted), the chests bead's to open.
- **The bosses' moves are rare here** because the hero stays at level 1 until the arsenal
  (ps-msz.6.4) gives it experience: a boss's health is its role's times the hero's level, so the
  four big bosses (15 to 25 health) fall to the sword within a few beats of landing, before most
  of their 8-beat cycles come round. The .NET tests (game/Assets/Game/Tests/Sim/Enemies/BossTests.cs)
  pin the moves themselves: the telegraph on a main beat, the charge as the delivery beat's step
  stopping at contact, the slam breaking a structure and hurting the hero on its beat, and a
  frozen delivery fizzling.

Per-minute counts against native's: native's survive report has no per-minute spawns, and its
kills (the positioning bot's full arsenal) differ from the sword alone's, so the counts can't be
compared on a dump. ScheduleTests pins the game to native's rule instead: with nothing killed,
minutes 0 to 3 spawn exactly native's 74, 121 and 105 and then hold at the cap with minute 3's
boss waiting; loudness scales minute 0 as native's `levelScale` (15, 74 and 77 for quiet, medium
and loud); and a killed horde refills to the minimum on the next wave. Since ps-w9p.4's denser
opening (game/evidence/opening) minutes 0 to 2 are the game's own numbers, and the same rule
gives 278 and 22 before the cap (40, 278 and 282 for minute 0 by loudness); this run predates it.
