# The UI: the indicator, the HUD, cards, menus and calibration (ps-6y2.19)

Stills of `champions stills --scene ui` (Client/Scenes/UiScene.cs), rendered
headless at fixed time on the M5 Max at 1920x1080 and kept here at half size
(the indicator's sheet cropped at three times game scale). The scene runs the
real sim on a scripted song (120 BPM to 20 s, then no beat) with the hero at
the game camera's distance, a white ring standing in for the hero until
ps-6y2.20 draws it. Reproduce:

```sh
just champions-build
just champions-tool stills --scene ui
```

## The indicator ([indicator-states.jpg](indicator-states.jpg))

Drawn in the world over the hero's head (Render/UI/HeroIndicator.cs and
Indicator.shader), from the sim's events, snapshot and beats
(Client/UI/Indicator.cs, `IndicatorModel`):

| State | What shows |
| --- | --- |
| ready | the next two beats' ticks sliding in from the strip's ends (0.2 and 0.7 of the way), the solid pip, the full health bar |
| dash-on-beat | 0.06 s after a dash pressed on the beat: the pip's flash halo, the pip cooling; the world's on-beat ring bends the grid |
| dropped-press | a press 0.3 s into the 0.6 s cooldown, dropped: the pip blinks dim |
| cooling | the dim ring filling clockwise with the cooldown |
| hurt | 0.05 s after a hit: the bar at 65%, its single red flash (no blinking) |
| level-held | a level held for its quiet moment: the dot beside the pip |
| beatless | no reliable beat: no ticks, only the pip |

The tests in `IndicatorTests` check each of these against the sim's events.
At game scale the strip is about 80 px wide: [indicator-ready.jpg](indicator-ready.jpg)
is the whole frame.

## The screens

| Still | What it shows |
| --- | --- |
| [title.jpg](title.jpg) | the name, Play, Calibrate, Quit |
| [picker.jpg](picker.jpg) | the apps playing audio (a still's own list), then File... (the Mac's open panel) |
| [pause.jpg](pause.jpg) | Resume, Calibrate, Quality (Left and Right step the tiers), End run, over the held world |
| [cards-first-offer.jpg](cards-first-offer.jpg) | the first offer: a dash (slanted), an attack (cut corners, focused by the arrows) and a mod (rounded), each an icon, a name, a line and its rank's pips; the experience hairline under them |
| [cards-treasure.jpg](cards-treasure.jpg) | a treasure: an evolution drawn larger with its diamond pip, and two raises to rank 3 |
| [hud-duel.jpg](hud-duel.jpg) | the HUD in a duel: the boss bar in the Fat Ogre's colour with the white-gold Break bar under it, the hairline |
| [victory.jpg](victory.jpg), [death.jpg](death.jpg) | the end card, lit and unlit: the run's time and kills (icons, not words), the build's icons, Again |
| [calibration-video.jpg](calibration-video.jpg), [calibration-input.jpg](calibration-input.jpg) | the calibration screens: the flash to line up with the click and its offset; the tap-along's eight clicks and the error scale |

## Checks

- `UiTests` (EditMode): a pointer's press on a card in the run's panel picks it
  in the real sim at its first offer; the keys 1-3, and the arrows with Enter,
  pick through `KeyInput` (the keyboard's events handed in directly: a
  batch-mode editor runs no native input); every menu row and card takes the
  pointer (`IPanel.Pick` finds it at its centre); every screen keeps to the
  design's word budget (menu rows of one or two words, a card's name of at
  most three and its line of at most six, no words on the end card but Again,
  none at all during play); every card the content can offer has a face
  within its budget; the icons' SVG subset; calibration's store and tap-along.
- The variant budget: 265 of 300 after this bead (the indicator's shader 2;
  UI Toolkit's filter shaders, which the panel settings reference and the UI
  never uses, stripped).
- The `ui` scene's screen stills are not meant for `champions look`: a menu on
  the void or a card moment breaks the frame rules (a near-black frame with
  white text), as the gallery's and the palette's stills do.
