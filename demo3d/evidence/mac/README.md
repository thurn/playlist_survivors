# The demo on the Mac (2026-10-29)

The first setup, bake, build and windowless run of the demo on the operator's
Mac: Apple M5 Max, Metal, Unity 6000.6.7f1 with Mono. How-to:
[Running on the Mac](../../../docs/tech-demo-3d/mac.md).

- **Setup:** vendor files from the manifest (1,528 files), the song written as
  WAV (Homebrew's ffmpeg has no libvorbis), the first import and license check
  in 53 s, and a record of 403,879 vendor files for the guard.
- **`BakeAll`:** all six bakers ran in 151 s (the crowd, the arena's 12
  scenes with their lightmaps, the roster, the effects, playback, sounds).
- **Build:** `demo3d build` made `Builds/mac/PlaylistSurvivors3D.app` (Mono,
  Apple silicon, Metal) in 1,021 s cold, 645 MiB, with no errors.
- **Windowless run:** from 160 s of the song in fixed time to its last still
  at 175 s, in 17 s. The song's timeline built in 1.23 s (3,573 entities), and
  the run spawned 1,464 effects with no exception in the log.
  - It kept two hand-drawn stills in the final peak: [165 s](still-165.000.jpg)
    and 175 s (not kept).
  - Command: `"Builds/mac/PlaylistSurvivors3D.app/Contents/MacOS/Playlist
    Survivors 3D Demo" -batchmode -force-metal -logFile Logs/mac-smoke.log
    --fixed-time --start-at 160 --capture-stills ../captures/mac-smoke
    --stills-at 165,175 --seconds 18`.

## IL2CPP (2026-10-29, later)

With Mac Build Support (IL2CPP) installed, `demo3d build` builds IL2CPP on the
Mac too, into `Builds/mac/il2cpp/`.

- **Build:** 1,038 s cold (a warm Library, a fresh worktree), 1,827 MiB with
  IL2CPP's intermediate output beside the app, and no errors.
- **The same windowless run** (`Builds/mac/il2cpp/...`): the timeline built
  in 0.37 s (3,576 entities, against Mono's 3,573: Mono and IL2CPP diverged
  on Cygnus too), 1,394 effects spawned by 175 s, and no exception in the log.
  Its 165 s still showed the same climax in another layout (not kept).

What neither run shows:

- **Sound and live timing.** A batch-mode player plays no audio, and fixed
  time doesn't follow the song clock. The windowed check is the operator's
  (plan.md, Pending on the operator).
- **Video capture**, which needs a windowed player.
- **Frame times.** None were measured here, and the Mac's would say nothing
  about Cygnus's budget.
