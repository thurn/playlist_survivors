# 3D Tech Demo: Working on Cygnus

## How the Run Works

The demo is a standalone run on Cygnus. Agents work there directly, in
Claude Code sessions the operator starts, with no Mac orchestration, no beads
and no Tollgate. The demo touches nothing else in the repository, so it
doesn't need the repo's per-change gate until it comes back into remote
master at the end.

### The relay repository

Work lives on a **private** GitHub repository,
`thurn/playlist_survivors-relay` (the remote `relay` on both machines),
until the final [integration](#integration).

- **`demo3d`:** the run's branch. It starts from remote master (`origin`),
  including this design, and every task's commits go onto it. It may merge
  remote master in later; it is never rebased once pushed.
- **`td/<key>`:** optional, for a task that runs in parallel with another.
  It branches from `demo3d` and merges back into it when the task is done.

It is private because the demo's history hasn't passed the repo's checks,
and a vendor file that slipped past the guard would be published by a push
to the public repository. Nothing from the run is pushed to `origin`.

### Working on a task

1. **Pick the task.** The next task in [the plan](plan.md#order-and-parallelism)
   whose prerequisites are done; its status goes to "In progress" in its
   entry under [Task Notes](plan.md#task-notes).
2. **Work in a worktree on the Dev Drive:** `D:\wt\demo3d` on `demo3d`, or
   `D:\wt\<key>` on `td/<key>` for a parallel task. Never in the start-up
   checkout at `C:\src\playlist_survivors`, which only tracks master.
3. **Commit often,** with Conventional Commits. The task's notes, status and
   commit SHAs go in its entry under [Task Notes](plan.md#task-notes), in the
   same commits as the work.
4. **Check before every push** (see [Automated Checks](#automated-checks)),
   then push to the relay at every checkpoint: before a long sweep, at the end
   of a session, and when the task is done. No work lives only on Cygnus's
   disk.
5. **Finish.** The task's status becomes "Done", with its last commit and its
   run command. Anything only the operator can do goes to
   [Pending on the operator](plan.md#pending-on-the-operator); no task waits
   for it.

The rules for every session:

- **Only the relay.** Push only to `relay`, never to `origin`.
- **The vendor guard before every push,** with no exceptions (see
  [The vendor guard](vendor-assets.md#the-vendor-guard)).
- **No administrator rights.** A step that needs them goes to the operator
  (see [Lessons from setting up Cygnus](#lessons-from-setting-up-cygnus)).
- **Stop and record** when the base doesn't build or a prerequisite is
  missing, and fix that first: a broken `demo3d` blocks every task.

### Integration

When the demo is done (TD13), it comes back into remote master once, from the
Mac:

1. **Squash** `demo3d` into a few commits, one per milestone (see
   [Reviews](plan.md#reviews)), on a branch from the Mac's `staging`. The
   relay keeps the full history.
2. **Wire the Mac's checks:** the vendor guard in `scripts/check-fast`, the
   gate and the release stage, with `dotnet test` of the choreography layer
   in a new `ci-full-demo3d` release step. TD1 writes the guard and its
   tests to run on both machines, so this is wiring, not new code.
3. **Build the Mac's sync record** with a record-only run of the sync tool
   over the Mac's sources, so the Mac's guard can check GUIDs and hashes.
4. **Land each commit** through Tollgate as usual, after `scripts/ci-full`
   passes locally: one candidate at a time.

It was done on 2026-10-29, from relay `b07f0e61`, as four commits cut at the
`demo3d` branch's milestone points: foundations and the crowd (relay
`f1507a5b`, TD0 to TD7), the demo's systems (`d405cd8b`: TD8, TD10, TD11 and
TD9's arena), the scene (`b1b4d733`: TD14, TD15 and TD11's recalibration),
and the answer (`b07f0e61`: TD12, TD13 and the Mac's skeleton LOD
experiments). The first also wired the guard into the Mac's checks, and let
demo files reach the public repository (see
[The vendor guard](vendor-assets.md#the-vendor-guard)). Demo work since then
lands through Tollgate like any other change; the relay keeps the run's full
history.

The guard change had its cold review (no leak path found). Fixed: the hook
checks `git archive`'s status, the release step says when it can't check the
range against `origin/master`, and the derived and sync records are written
through per-process temporary files, since checks now run concurrently.
Recorded, not fixed:

- **Edited text copies.** Until integration, the public-origin rule refused
  every demo file, which also stopped a vendor shader or prefab copied under
  `Assets/Demo3D/` with an edit and a fresh GUID. Nothing recognizes that
  copy now ([What TD1 built](vendor-assets.md#the-vendor-guard)); review demo diffs for vendor-looking
  text.
- **The installed hook** changes only when `demo3d setup` runs; rerun it
  after changing `demo3d/tools/hooks/pre-push`.
- **Concurrent appends** to the derived record can lose entries, which only
  weakens it until the next check re-adds them.

## Setting up Cygnus

Cygnus was surveyed on 2026-10-27 and set up on 2026-10-28, partly by the
operator and partly by agent sessions in its Remote Control environment.

Where it stands:

- Windows 11 Home 25H2, build 26200. RTX 4070 Ti on driver 610.88; i7-12700F;
  32 GB of DDR4 at 2133 MHz. XMP stays off: Cygnus froze at startup with it
  on.
- **C:** NTFS, about 225 GB free. **D:** a 120 GB Dev Drive (ReFS, label
  `DevDrive`), a dynamically expanding VHDX at `C:\DevDrive\DevDrive.vhdx`,
  about 107 GB free with the vendor sources on it.
- One desktop display: a 3840×2160 BenQ PD3200U at 60 Hz and 200% scaling,
  without HDR.
- **Unity 6000.6.7f1** with Windows Build Support (IL2CPP and Mono),
  licensed: a batch probe exits 0. No other editor is installed.
- **Tools:** the .NET SDK 10.0.403, `uv` 0.13.6 (user scope), PIX 2603.25,
  git, git-lfs, ffmpeg 9.0.1, Rust, Visual Studio 2022 17.14, the Windows
  SDK, Windows Performance Toolkit, NVIDIA's FrameView SDK.
- **Win32 long paths** on, and `core.longpaths` set.
- **`gh`** signed in as `thurn`, and `gh auth setup-git` run, so HTTPS pushes
  use its credentials. The private relay exists, and a first handoff went
  through it (see [Lessons from setting up Cygnus](#lessons-from-setting-up-cygnus)).
- **Power:** the High performance plan, standby never, Game Bar background
  recording off, no screen-saver lock, Dynamic Lock off.
- **The checkout** at `C:\src\playlist_survivors`, used only to start
  sessions, tracks master. Agents never work in it (see
  [Working on a task](#working-on-a-task)).
- Cygnus can't resolve the Mac by name. The Mac is `JARVIS` at 192.168.68.66,
  and Cygnus is at 192.168.68.57. Neither runs an SSH server.
- G: is Google Drive for desktop, with about 150 Synty packs, some in
  different versions from the Mac's. The demo doesn't use them.

Still open, in [Pending on the operator](plan.md#pending-on-the-operator):
whether D: comes back after a reboot, and the "If you've been away" sign-in
setting (unreadable without admin).

The display turns off after 15 idle minutes, and that stays: the PC itself
never sleeps or locks, and a sweep wakes the display and holds it on for its
own duration (see [Running a sweep](benchmarks.md#running-a-sweep)).

### The operator's steps

For rebuilding the machine, or a second one. Every step that needs
administrator rights is the operator's: agent sessions run unelevated and
can't raise a UAC prompt (see
[Lessons from setting up Cygnus](#lessons-from-setting-up-cygnus)).

1. **Disk.** Free at least 150 GB on C:. Then create a 120 GB **Dev Drive**
   in Settings (System, Storage, Advanced storage settings, Disks & volumes,
   Create Dev Drive, Create new VHD), as a dynamically expanding VHDX,
   lettered D:.
   - A Dev Drive is ReFS, where Windows copies by cloning blocks. That
     replaces the Mac's APFS clones, so the setup command's copies of the
     vendor folder, baked folder and warm `Library` are nearly free.
   - Defender's performance mode on a Dev Drive also speeds up Unity's
     imports.
   - D: holds the vendor sources (about 11 GB), the per-machine cache (about
     10 to 15 GB), the task worktrees and captures. The Unity editor
     (about 10 GB) stays on C:.
   - Check that D: comes back after a reboot. If it doesn't, a startup task
     that attaches the VHDX (as SYSTEM) fixes it; creating that is the
     operator's too.
2. **Unity.** Sign in to Unity Hub and activate a license (Personal is
   enough). Install 6000.6.7f1 with Windows Build Support (IL2CPP).
3. **Tools,** from an administrator PowerShell, with `winget`: the .NET 10
   SDK (`Microsoft.DotNet.SDK.10`) and PIX on Windows (`Microsoft.PIX`).
   `uv` (`astral-sh.uv`) needs no admin; an agent can install it.
4. **Long paths.** Turn on Win32 long paths (an administrator setting), and
   `git config --global core.longpaths true`. Unity's `Library` nests deep.
5. **GitHub.** `gh auth login`, then `gh auth setup-git`, so git can push to
   the relay over HTTPS. The relay repository is created from the Mac.
6. **Power and display.** The High performance power plan; the PC never
   sleeps (`powercfg /change standby-timeout-ac 0`), though the display may;
   no automatic lock (Settings, Accounts,
   Sign-in options: Dynamic lock off and "If you've been away" set to Never;
   no secure screen saver). Turn off GeForce Experience's instant replay and
   the Xbox Game Bar's background recording, which use the GPU's encoder.
7. **Memory.** Leave XMP off: Cygnus froze at startup with it on. CPU
   numbers are pessimistic as a result (see
   [Where Cygnus flatters the target](performance.md#where-cygnus-flatters-the-target)).
8. **Assets.** Copy the zips and the song to Cygnus's Downloads (below).

### Moving the assets

Only the packs the demo uses are placed: about 11 GB. Vendor files and copies
of them never enter either repository, and the relay is no exception.

They went over on 2026-10-28 as two zips the operator made on the Mac and
copied to Cygnus's `Downloads`, plus the song:

- **`synty.zip`:** the Mac's whole `~/synty`, as `.unitypackage.gz` files.
  Placed in `D:\vendor\synty`, still gzipped: the POLYGON packs Fantasy
  Rivals, Knights, Fantasy Characters, Fantasy Kingdom, Adventure, Elven
  Realm and Dark Fantasy, and the ANIMATION packs Base Locomotion, Sword
  Combat, Idles and Goblin Locomotion.
- **`Assets.zip`:** most of the asset project's `Assets/` folder (18.5 GB
  unpacked, with many packs the demo doesn't use). Placed in
  `D:\vendor\unity_assets\UnityAssets\Assets`, each folder with its sibling
  `.meta`: `Hovl Studio`, `KriptoFX`, `Magic Pig Games (Infinity PBR)`,
  `AmplifyImpostors`, `MeshCombineStudio`, and the four sound and music packs
  named in [Sound](camera-and-sound.md#sound).
- **The song:** the *Action* MP3, in `D:\vendor\songs\action\`.

The asset project's `Packages/` and `ProjectSettings/` are not copied: they
are a stock template plus URP and Shader Graph, with default tags and
layers, and `demo3d/` has its own.

Every placed file was checked against the Mac: each Synty package's and the
song's SHA-256, and each asset folder's file count and total bytes. The
manifest's source hashes therefore match on both machines.

TD2 may want more packs later. `Assets.zip` already holds many; others come
over the same way (a zip from the Mac, checked against its hashes), and are
added to the list above.

### What TD1 verifies

TD1 checks the setup before anything else and records each result:
the Unity probe (with an explicit `-projectPath`) exits 0 with licensing
initialized; `dotnet --list-sdks` shows a 10.x SDK; `uv` and PIX are
present; D: is ReFS (`Get-Volume`; `fsutil devdrv query` needs admin); the
vendor sources are in place; a test branch pushes to and deletes from the
relay; the power plan is High performance; and the memory speed. Anything
missing goes to [Pending on the operator](plan.md#pending-on-the-operator),
and TD1 stops if it blocks the task.

### Lessons from setting up Cygnus

From the survey, the setup and the first relay handoff (the Windows Delayed
listening spike, landed as `73530c7`), 2026-10-27/28:

- **Agent sessions on Cygnus can't elevate.** They run unelevated, and auto
  mode refuses to launch an elevated script, register a startup task, or
  change lock or other security settings, so no UAC prompt ever appears.
  Everything that needs admin is the operator's, so no task step may need
  it: an agent that finds one stops and lists it.
- **PowerShell and .NET write CRLF,** sometimes mixed with LF in one file.
  The spike's handoff carried 96 such files. The repository's
  `.gitattributes` (`* text=auto eol=lf`) now stores every text file with
  LF whichever machine commits it.
- **Windows paths break `/` parsing.** The spike's step 0 found 15 test
  failures from one `rsplit('/')` on a path (`sim/src/content/load.rs`).
  Code that handles paths uses the path APIs, never string splits.
- **Floating point differs by an ulp or so.** The Windows CRT's `pow`,
  `sin`, `cos` and `exp` differ from macOS's, which failed two bit-exact
  tests. Determinism is per runtime and platform (the
  [Timeline contract](choreography.md#timeline-contract)), and no test compares floats bit
  for bit across machines.
- **Remotely started sessions can't count on messaging back.** In both
  Cygnus sessions the Mac started, the `send_message` tool disappeared
  partway through, and the Mac read their reports with `list_events`. One
  reason the run is now standalone; a session's state belongs in the plan's
  notes and its commits, not in messages.
- **Unity reopens its last project** when batch mode gets no
  `-projectPath`, re-creating it if it was deleted. Every Unity command
  names its project.
- **Zips made on the Mac carry `__MACOSX/` and `.DS_Store` entries** (half
  of `Assets.zip`'s 220,000). Extract entry by entry with .NET's `ZipFile`,
  skipping those, rather than with `Expand-Archive`, which can't filter and
  is slow at that size. Use `-LiteralPath` for names with brackets.
- **Untracked files in the start-up checkout block pulls** once the same
  paths land in git. Agents work only in `D:\wt\` worktrees, and the
  checkout at `C:\src\playlist_survivors` stays clean.
- **A new install isn't on the running shell's PATH.** After installing a
  tool, refresh it with
  `$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')`.
- **`robocopy` exits 0 to 7 on success;** only 8 and above are failures.
- **The gate doesn't see Windows-only code.** The spike is its own Cargo
  workspace inside `native/`, so the gate's check-fast found no crate
  changed. The release stage's `cargo machete`, which scans all of
  `native/`, then flagged `windows-core` as unused: the `windows` crate's
  `implement` macro needs it, which machete can't see (hv-50g9.17). Code
  that only builds on Windows is checked on Cygnus before its push, since
  the Mac can't build it.
- **Evidence written on Cygnus names its paths** (`C:\Users\dthurn\...`),
  as the Mac's does (`/Users/dthurn/...`). That's acceptable; credentials
  and vendor content are not, and the agent checks for both before
  pushing.

## Unity Work by Agents

Agents implement this demo, and they cannot use the Unity editor's interface.
Everything is therefore scripted, runs in batch mode on Cygnus, and fits the
worktree-per-change workflow.

- **Batch mode for everything:** baking, scene generation, lightmapping,
  tests and builds.

  ```
  Unity.exe -batchmode -projectPath "D:\wt\<key>\demo3d" ^
    -executeMethod Demo3D.Tasks.BakeAll -quit -logFile <log>
  Unity.exe -batchmode -projectPath "D:\wt\<key>\demo3d" ^
    -runTests -testPlatform EditMode -testResults results.xml -logFile <log>
  ```

  TD1 confirms whether `-logFile -` streams the log to the console on
  Windows; otherwise the task entry points write to a file and print it.
- **Scenes, prefabs and materials are generated** by editor scripts from
  tracked parameters. Agents never hand-edit vendor YAML, and edit our own
  Unity YAML only for small settings files.
- **The choreography layer also builds and tests with `dotnet test`** (SDK
  10, on both machines), through an SDK-style project over the same source
  files. That takes seconds, without Unity.
- **Tools run on both machines.** The setup command, the sync tool and the
  vendor guard run on Windows and macOS: Python under `uv`, or .NET console
  tools (TD1 picks). The repo's Bash scripts run on Cygnus under Git Bash
  where needed.
- **One setup command prepares a worktree.**
  - A per-machine cache holds the vendor folder, the baked folder and a warm
    `Library`, keyed by the manifest's hash and the generators' input hash.
    On Cygnus it lives on the Dev Drive, at
    `D:\cache\playlist-survivors\demo3d\`, beside the worktrees, since a
    block clone only works within one volume.
  - It clones all three into the worktree with ReFS block clones, which are
    nearly instant. Off a Dev Drive it falls back to plain copies, slowly,
    and says so.
  - It rebakes only what is stale, and verifies the Unity license.

  ```
  > demo3d setup        # in a fresh worktree; the command name is TD1's choice
  cloned Vendor (412 files), Baked (manifest 3f2a…), Library (warm)
  rebaking: bone textures for 1 changed clip annotation
  ```
- **A warm editor for the inner loop.** A cold batch-mode launch pays for the
  editor's startup and the asset database's check on every compile or test
  run, which adds up over the dozens of iterations a task makes.
  - A task command starts one long-running batch-mode editor per worktree.
    It takes commands over a named pipe (or localhost TCP with a per-worktree
    token, TD1 picks): refresh and compile, run EditMode tests, run a task
    method. Its listener restarts after each domain reload.
  - The task entry points use it when it is running and fall back to a cold
    launch otherwise. It exits after 30 idle minutes.
  - It holds the worktree's project lock, so every Unity step in that
    worktree goes through it while it runs.
  - **Before pushing,** an agent runs its Unity checks once cold, since a
    long-lived editor can carry stale static state across reloads.
  - TD1 spikes it and records the cold and warm loop times. If it isn't
    reliable, cold launches stay, and the notes say why.
- **Projects are locked per path.** The operator's editor may have a
  project open, and Unity locks a project per path. Agents only ever open
  their own worktree's project.
- **On-screen windows.** Only benchmark sweeps open a window. Everything else
  runs in batch mode and opens none.

## Automated Checks

Checks are path-gated on `demo3d/`, except the vendor guard, which runs on
every change.

- **On Cygnus, before every push to the relay:** the vendor guard,
  `dotnet test`, a cold Unity batch-mode compile and EditMode tests, and,
  for a commit that changes the player, an IL2CPP player build. A git
  pre-push hook in each worktree runs the guard, so a push can't skip it.
  The task's notes record each result. These are the run's only checks, so
  nothing is pushed without them.
- **On the Mac, at integration only:** the vendor guard in
  `scripts/check-fast`, the gate and the release stage, and `dotnet test` in
  a `ci-full-demo3d` release step (see [Integration](#integration)). The Mac
  has no Unity project to build, so no stage there runs Unity.
- **No benchmarks** run in any check. Benchmarks need a quiet machine and a
  window.

The choreography layer's tests:

- the same inputs give a byte-identical timeline dump;
- every impact lies within 1 ms of the grid (accents in free sections);
- every gait step starts on a beat, outside free sections;
- every hero stride outside an attack heads toward the section's goal until
  the hero reaches it;
- each section's measured deaths per second stay within 10% of its load
  preset;
- no two monsters overlap by more than a quarter of their radius;
- every hit and death has its clip and effect events;
- spawns appear outside the view at that section's view width;
- no monster or hero ever stands closer to a wall than its radius, beyond a
  small tolerance;
- on a test layout with a courtyard and gaps, every monster with a path
  reaches the hero, none stays stuck for more than 4 beats, and blocked
  monsters mark time in place instead of jittering;
- the flow field matches a reference shortest-path search on small test
  layouts;
- the song's timeline builds in under 3 s.
