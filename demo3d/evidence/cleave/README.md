# The drops cleave (ps-ffx)

The normal song's drops after ps-ffx (choreography.md, The drops cleave): each
still is 0.07 s after a downbeat's heavy cleave, the light combo's
finisher with a 360-degree ring hit shape, drawn with the spin's circular sword
slash (Hovl's Sword Slash 11) at the reach it cuts.

| Still | Song time | Section |
|---|---|---|
| drop-1-63.56.jpg | 63.564 | drop-1 |
| drop-2-150.27.jpg | 150.273 | drop-2 |
| final-peak-176.60.jpg | 176.596 | final peak |

The IL2CPP player's timeline diverges from the dotnet CLI's over the song, so
the cleave times come from the player's own `--dump-sounds` (the heavy cleave
carries the ring's boom), not from the CLI's dump. Rendered windowless on the
Mac:

```
uv run --project demo3d/tools demo3d build
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal --fixed-time --start-at 173.53 \
  --capture-stills captures/cleave --stills-at 176.596 --seconds 176.9
```

The numbers (dotnet CLI, seed 1; `song` report's carnage table): in drop-1,
groove-2, the build, drop-2 and the final peak the hero takes 84-92% of the
kills (5-29% before) and swings 1.2-1.5 times a second (the build about 0.9;
0.5-1.1 before). A heavy cleave kills 40-60 in the drops and about 100 at the
final peak. Every song check passes on seeds 1-5, normal and overdrive (on seed 6 the
final peak reaches 0.85 of its rate).
