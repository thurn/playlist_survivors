# The cast: every role and boss in its crystal (ps-msz.6.2)

Windowless stills of the Mac player (Mono), one per role of game/Content/cast.json, each standing
alone 5 m ahead of the hero (`--cast ROLE`: `RunLevel.Lineup`, no music, so it idles), cropped
around it:

```sh
uv run --project demo3d/tools game look ROLE --backend mono --at 2 --frames 1 --strip 1 \
  --crop 560,0,800,500 --evidence cast --player-args "--cast ROLE"
```

[cast.jpg](cast.jpg) is all 26 on one sheet, in cast.json's order; ROLE.jpg is each at full
crop. Every role shows its model's gem colour, and the nine models' colours are distinct across
the spectrum: violet bats, pink slime (zombie), red demon (ghost, the ghost swarm, the bee), cyan
skeleton, green goblin (mudman 1, the stampede's goblins), teal big orc (mudman 2, the XL mantis,
the mummy boss), blue troll (werewolf, its boss, the minotaur), orange mushroom (the flowers and
their boss) and gold dragon (the mantis boss). Two roles keep their own colour against a model
mate: the XL bat's dark violet (#5a3dcc) and the XL mummy's pale blue (#b8c8ff, beside the blue
werewolf boss). Sizes follow the cast's scales: the XL bat and bats 4 and 5 at 2x, the brutes at
2x, the bosses at 2x to 3x (the dragon at 1x is already 4.5 m) and the minotaur at 2.5x.

**The flowers stay on the mushroom.** None of the vendor packs has a thorn-plant monster, and in
crystal the mushroom's cap over its stalk reads at once as a growth from the ground, apart from
every walker; it is also the slowest role, which keeps it planted.

## Telegraphs, charges and slams

Two bosses (the werewolf boss, blue, and the mummy boss, teal) lined up before the hero over
house-128 (`--cast boss-werewolf,boss-xlmummy --file public/reactive/house-128/house-128.ogg`),
the run starting after 2 s of clear beat at 3.78 s:

- [boss-charge.jpg](boss-charge.jpg), 4.20 to 5.40 s: both telegraph on the beat at 4.24 s (an
  `Attack` of kind Charge, the delivery two beats later at 5.18 s), turning to the hero and
  holding the humanoids' flourish wind-up in place; at 5.088 s each sets off on its charge, the
  delivery beat's step (the Synty run), and plants on the hero at 5.18 s, where the sim hits it
  for the boss's power (`Hit` of kind Charge).
- [boss-slam.jpg](boss-slam.jpg), 8.50 to 9.60 s: within 4 m of the hero they telegraph slams at
  8.47 s; the wind-up holds, then the attack clip's overhead blow lands its contact on the
  delivery beat (9.41 s).

The dragon telegraphs with "Fire Head 1" and charges with its "Run"; the mushroom with its
fourth attack's start and its spinning loop (Editor/Crowd/roster.json). Models without their own
clips would use the attack and the walk. The sim's rules are pinned by BossTests (.NET) and the
clips' timing by CrowdTests (EditMode).
