# The crystal HUD (ps-msz.6.9)

Windowless fixed-time stills of the Mac player (`game look`, IL2CPP, Mono for the exploratory
runs) on the survivors test playlist (`native/playlists/survivors-30.m3u`, house-128 first), the
slice's level unless a set says `--run`. Every command adds
`--player-args "--file $PWD/native/playlists/survivors-30.m3u --no-session ..."` with the flags
shown. A HUD panel's still is one frame behind the camera's: UI Toolkit draws its target after
the frame's still is taken, so the gem (in world space) is frame-exact and the panel trails it by
1/60 s.

## In play

`game look hud-play --evidence hud/play --at 5.0,9.5` with `--arsenal sword:3,garlic:2,fire-trail:1`:

- [play/still-005.000.jpg](play/still-005.000.jpg): the opening, in the neon zone. Experience along
  the top with LV 1, health top left with DELAYED 3 S and the source tag under it, three weapon
  slots (gem, mark, rank pips) and nine empty ones as faint dots, kills top right, the run clock
  bottom left. The beat track: the committed beat bright, the beats its period leads us to expect
  dimmer, sliding in to the heart. Over the knight the beat gem with its ring; the life ring under it.
- [play/still-009.500.jpg](play/still-009.500.jpg): after house-128's opening jump moved the zone
  to blossom's night at 6.6 s: damage numbers over the kills (15: a sword hit of 1.5, times 10), and
  the HUD's words keep a soft shadow and outline for the brighter grounds.

## The beat gem lands on the beat

`game look hud-gem --evidence hud/gem --at 37.433333 --frames 12 --strip 6 --crop 760,200,400,340`
and `game look hud-gem-beat ... --at 37.10,37.20,37.30,37.40,37.516667,37.533333,37.60,37.75 --strip 4`:

- [gem/hud-gem.jpg](gem/hud-gem.jpg): 12 consecutive frames across the downbeat at 37.530 s (the
  run's `Beat` event, downbeat bit set). The gold ring (a downbeat comes) closes over frames +0 to
  +5, and on the beat's own frame, +6 (37.533 s, the first frame at or after it), it lands on the
  gem's rim as the gem flashes white; the gold burst throws out from +7.
- [gem/hud-gem-beat.jpg](gem/hud-gem-beat.jpg): a whole beat: the ring far and faint at 37.20,
  closing through 37.30 and 37.40, on the gem a frame before the beat (37.517), landed on the beat
  frame (37.533), the burst at 37.60, and at 37.75 the next ring, white (the next beat isn't a
  downbeat).

HudTests (EditMode) pin the timing: the gem's next beat is the beat itself on the frame before
it, phase 0 on its own frame, and two-beat bars alternate; only beats known by the frame show.

## Waiting, music stopped and pause

- [waiting/still-001.500.jpg](waiting/still-001.500.jpg) (`--at 1.5`): before the run starts
  (headless and fixed-time runs start after 2 s of clear beat): THE RUN STARTS ON THE BEAT, the
  track empty under WAITING FOR THE BEAT with a grey heart, no gem yet (it comes in over the run's
  first two seconds).
- [music-stopped/still-066.000.jpg](music-stopped/still-066.000.jpg) (`--at 66`): house-128 fell
  silent and the world froze at 61.675 s: the MUSIC STOPPED banner, the track waiting, the gem a
  breathing pale disc.
- [pause/still-010.600.jpg](pause/still-010.600.jpg) (`--pause-at 10 --at 10.6`): the pause menu
  over the HUD (its PAUSED heading is the banner while it is up).

## The level-up's cards

`game look hud-cards --evidence hud/cards --at 13.08,13.52,14.2,14.75`. Level 2 opened at 13.033 s;
its cards flip in on the beats at 13.15, 13.62 and 14.09 s; the selected card is taken at 14.60 s.

- [cards/still-013.080.jpg](cards/still-013.080.jpg): the first card opening out 0.07 s before
  its beat, the HUD dimmed.
- [cards/still-013.520.jpg](cards/still-013.520.jpg): the first whole (MAGIC WAND, NEW!, NEW ATTACK,
  lit as selected), the second edge-on mid-flip, the third not yet.
- [cards/still-014.200.jpg](cards/still-014.200.jpg): all three (MAGIC WAND, GARLIC, SWORD SPIN
  LV 1 > 2, +1 SPIN), the hint now how to choose.
- [cards/still-014.750.jpg](cards/still-014.750.jpg): 0.15 s after the pick: the chosen card lifts
  and swells, the others fall away turning, and MAGIC WAND is in the slots.

## A chest

The sim's chests come with ps-msz.6.8; until then `--chest-preview 20:3` opens a client-only
preview of a tier-3 chest at 20 s (no freeze): native's eight eighth-note ticks from the next
downbeat, landing on the downbeat after them. `game look hud-chest --evidence hud/chest
--at 20.6,21.5,22.6,23.6` with `--chest-preview 20:3 --arsenal sword:4,garlic:3,fire-trail:2`:
[20.6](chest/still-020.600.jpg) and [21.5](chest/still-021.500.jpg) TREASURE, five gold-lit gems
cycling through the arsenal on each tick; [22.6](chest/still-022.600.jpg) TREASURE FOUND, landed
and swelling with the rewards' names and ranks; [23.6](chest/still-023.600.jpg) the card fading
out of the way.

## The end cards

- [victory/still-048.000.jpg](victory/still-048.000.jpg): `--debug finale@44 --arsenal
  sword:4,garlic:3,fire-trail:2 --at 48`. The sim's new `finale` command brought the finale's blast
  on the downbeat at 44.09 s and the victory card at 47.09 s: VICTORY, TIME 0:40 · KILLS 1305 ·
  LEVEL 1, and each weapon's damage, time held and DPS times 10 (the sword killed everything before
  it reached the garlic's aura or the trail).
- [fell/still-151.000.jpg](fell/still-151.000.jpg): `--run --arsenal garlic:1,fire-trail:1 --at
  151`. The run's level lets the hero fall: it fell at 147.48 s. YOU FELL, TIME 1:52 · KILLS 695
  · LEVEL 2, and the Magic Wand taken at level 2 did most of the damage (7.0K, 68 a second, held
  1:42.5), the garlic 105 and the trail none (standing still, the hero leaves no wake).

Not checked here, on the operator's list: the HUD at ordinary window sizes and with the beats heard.
