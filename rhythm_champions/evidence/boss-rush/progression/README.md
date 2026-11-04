# Boss Rush progression (ps-8z4.4)

The model is in [boss-rush.md, Progression, as built](../../../../docs/champions/boss-rush.md#progression-as-built-ps-8z44).
The tables are in [Content/bossrush.json](../../../Content/bossrush.json). Everything here is deterministic: it comes
from the .NET sim tests (`BossRushProgressionTests`) driving scripted players, not from hand-play.

## Shedding (sim tests)

- Each 10% of health lost bursts a tenth of the budget on the next beat, at that beat's own time, and never before it.
  Each burst is the slot's shape's cluster of gems. Nine bursts shed 90%, the kill drops the rest, and the drops total
  exactly the budget. Once every gem is collected, boss 1 gives its 3 levels.
- One blow through 90/80/70% gives three bursts on the same beat. However long a fight runs, it sheds only what its
  health lost.
- With no beat, a threshold bursts one fallback beat (0.5 s) after it was passed.
- Adds draw from the same budget, and the kill drops what is left.
- Ring gems lie on the radius, spray gems inside the fan ahead of the boss, lane gems on the line ahead of it. No two
  gems in a burst share a pickup cell. A slot's authored `shed` replaces the default, and the default fills its
  missing fields.

## The curve

50 XP to level 2, then each level 5% more. Act 1's bosses give 3 levels each and the rest 2 each, so each boss's
budget is its levels' XP. Steps after each boss count its level-ups plus its reward:

| After boss | 5 | 10 | 15 | 20 | 23 | 24 | 25 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Level | 16 | 26 | 36 | 46 | 52 | 54 | 56 |
| Steps | 20 | 35 | 50 | 65 | 74 | **77** (full) | 77 + 2 fallbacks |

Budgets run from 158 XP (boss 1) to 1,361 XP (boss 25), 13,636 XP for the whole run.

## Health

[sustained-damage.tsv](sustained-damage.tsv) comes from `MeasureTheReferenceBuildsAndPrintTheHealthTable` (Explicit;
it runs in about a second). For each slot it records the boss health per second the scripted reference player takes
over 60 s of the duel with each reference build. The player walks `positioning` and dodges telegraphs. The fighter
is the slot's current one in its arena (the main game's stand-ins in the `standin` arena until the fights land), made unkillable, with no XP, at 128 BPM,
seed 31. Each slot's health is `targetS` (120 s rising to 180 s) times the mean of the two builds. It runs from 204
(boss 1, Sword 1 alone at 1.7/s) to 8,748 (boss 25, full builds at about 49/s).

A landed fight's row is measured against its own fight (the player then walks round the arena's props, chases the
fight's body and dashes its dash waves). Boss 1, the Treasure Goblin, was re-measured when its fight landed (ps-8z4.6):
1.73/s against the Ogre stand-in's 1.7, the same to a tenth, so its health stays 204; the other rows are unchanged.

The focused build stays melee for a long time, and the scripted player keeps its distance from bosses, so it trails
the broad build until the late acts. The real fights' movement and uptime will differ from the stand-ins', so the
pacing bead (ps-8z4.60) re-measures and re-tunes once they exist. Hand-play difficulty is an
[operator-pending](../../operator-pending.md) row.
