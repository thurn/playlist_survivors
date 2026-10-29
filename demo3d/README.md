# demo3d

The 3D tech demo's Unity 6.6 project (URP, Direct3D 12) and its tools. The design and plan are
in [docs/tech-demo-3d/](../docs/tech-demo-3d/README.md).

- `Assets/Demo3D/Choreography/`: the choreography layer, plain C# 9 with no UnityEngine
  dependency. `dotnet/` builds and tests the same sources without Unity.
- `Assets/Demo3D/Runtime/`, `Editor/`, `Tests/`: the client, the batch-mode task entry points
  (`Demo3D.EditorTools.Tasks`), the warm editor, and the tests.
- `Assets/Vendor/` and `Assets/Baked/`: gitignored; vendor files and derived files only.
- `manifest.json`: the vendor assets the demo uses (TD2 fills it).
- `tools/`: the `demo3d` command (Python under uv): setup, the vendor guard, the sync tool and
  the Unity task runner.
- `evidence/<key>/`: each task's measurements; stills carry the `ps-render` tag.
- `Assets/Demo3D/Bench/` and `Runtime/Bench/`: the benchmark harness (TD7). The pure logic (the
  fitted search, the pass rule, rows, specs, camera geometry) is engine-free; the player side
  (`--bench <spec> --out <dir>`) runs a sweep in one process per scene. Stress scenes implement
  `IBenchScene` and register with `BenchScenes.Register`; until TD4's exist, `synthetic` is the
  only one. `sweeps/` holds the sweep specs.

A sweep holds the machine lock (`bench.lock` in the per-machine cache), opens a 1920×1080 window
and needs the foreground: don't use the desktop while one runs. Never run a development player on
Cygnus outside the operator's supervision: its profiler socket raises a Windows Firewall prompt,
which only the operator may answer, and which holds the foreground until then.

## Commands

From the worktree's root (`D:\wt\<key>` on Cygnus):

```
uv run --project demo3d/tools demo3d setup          # prepare a fresh worktree, once
uv run --project demo3d/tools demo3d editor start   # optional: the warm editor
uv run --project demo3d/tools demo3d test           # EditMode tests (warm if running)
uv run --project demo3d/tools demo3d unity Configure    # any Tasks method, or a full name
uv run --project demo3d/tools demo3d build --backend il2cpp
uv run --project demo3d/tools demo3d check --player # every check, before a push
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/synthetic-frontier.json --out <dir>
uv run --project demo3d/tools demo3d report <dir>   # regenerate a sweep's report.md
uv run --project demo3d/tools demo3d pix <spec> --point n=2000,viewWidthM=40,load=demo,aa=msaa4 --out <dir>
dotnet test demo3d/dotnet/Demo3D.Choreography.Tests # the choreography layer alone
```

Setup reads the machine's sources from `roots.json` in the per-machine cache
(`D:\cache\playlist-survivors\demo3d\` on Cygnus, `$DEMO3D_CACHE` to override), creating it with
the defaults from windows.md, and installs the pre-push vendor guard for the worktree. Never
bypass the guard.
