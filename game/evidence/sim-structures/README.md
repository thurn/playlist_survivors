# The sim's crystal structures and zones on a real dump (ps-msz.3.4)

What was run, on jarvis (.NET 10):

```bash
dotnet test game/dotnet/PS.Game.Sim.Tests --filter BreaksAndRegrowthsLandAtTheirTimesOnARealDump --logger "console;verbosity=normal"
```

The test feeds `native/fixtures/entries/accelerando-100-140-30s.jsonl` (the analyzer's first 30 s of
accelerando-100-140, every group stamped) to a sim the way the client does, one `StepTo` per 60 fps
frame. It plays the game's arena (`game/Content/arena.json`, 38 sites), the slice's horde and the sword at rank 1,
with the hero standing at its start tile (80, 78) and kept from falling. It checks that every zone change lands on the
first downbeat at or after its cause's time and known-at time, that every break lands on a heavy
impact or a cleave band, and that every regrowth lands on the downbeat after a zone change, for a
structure broken before it. Its printout:

```text
zone 0 -> 1 at 6.500 (Jump at 6.000, known 4.548)
zone 1 -> 2 at 19.450 (Change at 15.154, known 18.698)
break 1 at 8.300 by HeavyStrike, point (-3.00, -9.00)
break 4 at 8.300 by HeavyStrike, point (9.00, -2.25)
break 5 at 8.300 by HeavyStrike, point (9.00, -1.50)
break 7 at 8.300 by HeavyStrike, point (0.00, 6.00)
break 3 at 8.600 by Cleave, point (9.00, -7.50)
break 2 at 8.900 by Cleave, point (9.00, -10.50)
break 0 at 9.200 by Cleave, point (-10.50, -9.00)
break 6 at 14.545 by Cleave, point (-4.50, 6.00)
regrowth 0..7 at 20.580 to 21.150
break 3 at 21.420 by Cleave, point (9.00, -7.50)
break 2 at 21.700 by Cleave, point (9.00, -10.50)
break 0 at 21.980 by Cleave, point (-10.50, -9.00)
break 1 at 22.260 by HeavyStrike, point (-3.00, -9.00)
break 4 at 22.260 by HeavyStrike, point (9.00, -2.25)
break 5 at 22.260 by HeavyStrike, point (9.00, -1.50)
break 7 at 22.260 by HeavyStrike, point (0.00, 6.00)
break 6 at 23.660 by Cleave, point (-4.50, 6.00)
```

What it shows:

- **Zones.** The 7.3 dB jump at 6.0 s, known 1.45 s ahead, lands on the downbeat at 6.5 s. The
  changes at 10.45 s and 19.75 s are dropped: each falls within 8 s of the zone change before it. The
  change at 15.15 s is confirmed 3.5 s late, so it lands on the first downbeat after its known-at
  time, 19.45 s.
- **Breaks.** The first accent heavy strike, at 8.3 s, shatters the four small structures within its
  9.15 m on its impact frame. The wave's bands take the three farther ones 0.3 s apart as the ring
  passes them. Site 6, large and 9 tiles, needs three breaking hits. Two heavy strikes before it only
  glint (the first counts once for its strike and wave together), and it goes at 14.5 s.
- **Regrowth.** Everything broken at the 19.45 s zone change regrows on the next downbeat, 20.58 s,
  over one beat (to 21.15 s). The hero stands more than 3 m from every site. The heavy strikes after
  21.15 s break them again. They come so soon because the hero never moves and hits on every
  accent, which a real player's path won't do.
