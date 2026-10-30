# Monster style: mix (ps-a54.4)

`--monster-style mix`: stylelab's "Showcase mix", a style per type (`MonsterStyle.StyleOf`, after
the lab's `Treatments.StyleFor`). Painted skeletons, goblins and mushrooms; crystal slimes,
spectrum bats and fire demons; chrome orks, void trolls and a fire dragon. Each type gets its
style's materials, shell, mesh (the slimes and bats draw the unwelded style mesh) and tuned grade,
and the camera copies its opaque texture for the slimes' refraction. The post look is the lab's
mix grade (bloom 1.1, chroma 0.05) on demo3d's tuned base
([all/README.md](../all/README.md) has the tuning).

How the set was made, on the Mac, windowless, IL2CPP player:

```
uv run --project demo3d/tools demo3d look ../styles/mix --baseline final --player-args "--monster-style mix"
```

- [look.md](look.md) and [look.json](look.json) hold the look harness's numbers against
  `look/final`; [contact-sheet.jpg](contact-sheet.jpg) shows `final` on the left and mix on the
  right.
- Every player log says `DemoMain: --monster-style mix's post look on the night volume`.

## What the numbers say

- **The effects are the brightest values in all 11 stills**, by 5.9 to 25.1 L* (final: 2.6 to
  23.2).
- **Readability passes in every shot**: dL* 21.6 to 32.1, against final's 20.0 to 24.3.
- **Near-black is 3 to 16% of the song stills**, the same as final's.
- **At gameplay distance it reads mostly as painted**, since the fodder is painted and
  outnumbers the rest. The crystal slimes are the clearest accent (the pale violet packs in the
  graveyard and courtyard shots); the elites are few and small in these stills, so whether they
  stand out is for the operator to judge in motion.
