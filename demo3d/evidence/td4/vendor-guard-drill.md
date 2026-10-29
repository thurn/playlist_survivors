# TD4: vendor guard drill (qa.md, Vendor guard drill)

Run on Cygnus on 2026-10-28 in a scratch worktree (`D:\wt\drill`, branch `td/drill`), pushing to
a local bare repository so nothing could leave the machine. The shared pre-push hook and the
machine's sync record (69,266 vendor files) were in place.

| Case | What | Result |
|---|---|---|
| 1 | Force-add a vendor PNG and its `.meta` into `demo3d/Assets/Vendor/Drill/`, commit, `git push drill` | **Refused.** The hook named `drill.png` (binary under demo3d/, SHA-256 of a vendor file) and `drill.png.meta` (inside the vendor folder, vendor GUID, vendor SHA-256); `failed to push some refs`; the remote got no branch |
| 2 | Copy a vendor PNG (Hovl `Circle17.png`) into `demo3d/Assets/Demo3D/Drill/`, `demo3d guard --index` | **Fails:** binary file under demo3d/, and its SHA-256 is a vendor file |
| 3 | Copy a vendor prefab (Hovl `Laser beam 10 fire.prefab`) with its `.meta` into a tracked folder | **Fails:** the prefab's SHA-256 and the `.meta`'s GUID and SHA-256 are vendor files |
| 4 | Commit a capture-tool still (`still-045.000.jpg`, tagged `ps-render`) under `demo3d/evidence/drill/` and push | **Passes**; the remote got the branch |

The Mac half of the drill (`scripts/check-fast` with a file from the Mac's sources) runs at
integration.
