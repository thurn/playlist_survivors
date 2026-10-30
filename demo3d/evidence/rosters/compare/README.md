# Roster sets: default, forest and heroic side by side (ps-f8a.4)

Each set in its final look (crowd.md, Roster sets), from the same song
moments and beauty shots, a column each:

- [song.jpg](song.jpg): the song stills at 25, 60, 100, 145 and 175 s
  (groove, drop-1, groove-2, drop-2, the final peak). The default plays its
  level script (425 at 30 m at the widest); each set plays its own level
  variant (150 on screen at 18 m), so its camera is closer and its crowd
  smaller.
- [beauty.jpg](beauty.jpg): TD15's six beauty shots with the horde. These
  are fixed cameras over the arena-wide horde, so the sets' monsters are as
  small as the default's there.

The columns are the committed look sets: default is
[evidence/look/final](../../look/final/), forest is
[evidence/rosters/forest](../forest/README.md) and heroic is
[evidence/rosters/heroic/look](../heroic/README.md).

What the sheet shows:

- **default:** the dyed bone-texture horde, each type in its signature hue,
  the most saturated and the most uniform per type.
- **forest:** the packs' painted colourways, chunky silhouettes and glowing
  eyes and spikes, graded less saturated than the default (14 against 30) so
  the paint doesn't scream, with bloom kept to the glow and the effects.
- **heroic:** realistic creatures, cooler and less saturated (14, a cooler
  moon, temperature -10), so their leather, bone and metal keep their
  detail on the warm ground; it reads by silhouette and value more than by
  colour.

The effects are the brightest pixels in every song still of all three
(each look.md, Effects), and every beauty shot passes readability.
Without `--roster` the look matches demo3d/evidence/look/final/: a
`demo3d look` of the default after this change differed by at most 0.005
L* on all 79 L* statistics.

Commands, after the set commands in crowd.md, Roster sets:

```
uv run --project demo3d/tools demo3d look rosters-forest --player-args "--roster forest" --baseline final
uv run --project demo3d/tools demo3d look rosters-heroic --player-args "--roster heroic" --baseline final
uv run --project demo3d/tools demo3d look-sheet ../rosters/compare/song.jpg final ../rosters/forest ../rosters/heroic/look \
  --labels default,forest,heroic --stills song-025,song-060,song-100,song-145,song-175 --scale 2
uv run --project demo3d/tools demo3d look-sheet ../rosters/compare/beauty.jpg final ../rosters/forest ../rosters/heroic/look \
  --labels default,forest,heroic --stills opening-horde,path-horde,glowing-tree-horde,graveyard-horde,courtyard-horde,forest-edge-horde --scale 3
```

`look` writes to demo3d/evidence/look/SLUG; the set folders here are those
outputs moved into place.
