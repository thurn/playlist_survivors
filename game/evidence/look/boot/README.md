# The boot scene, windowless (ps-msz.1.3)

Proof that the new `game/` project builds, runs and draws on the Mac without a window. The scene
is empty: a camera clearing to the night's near-black violet, through URP Forward+ at MSAA 4x.

```sh
uv run --project demo3d/tools game setup                      # 44 s with a warm cache
uv run --project demo3d/tools game test --no-skips            # 4/4 EditMode tests, 8 s warm
uv run --project demo3d/tools game build --backend il2cpp     # 77 s cold (1291 shader variants)
uv run --project demo3d/tools game build --backend mono       # 18 s
uv run --project demo3d/tools game look boot --at 1           # this still, 3.3 s
```

- [still-001.000.jpg](still-001.000.jpg): the IL2CPP player at t = 1 s in fixed time, batch mode,
  Metal. The Mono player gives the same frame, and runs to `--seconds 3` and quits with exit 0.
- The EditMode tests check that every quality level draws with the game's own URP asset (MSAA 4x,
  a Forward+ renderer with no missing feature), that the boot scene is the first build scene with
  a camera, that its camera draws through URP in the editor, and the `ps-render` tag.
