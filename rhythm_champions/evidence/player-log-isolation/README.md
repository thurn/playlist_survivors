# Player log isolation — 2026-11-03

ps-oli.7 validated the launch/log routing change using **existing independent
builds** from ps-oli.3 and ps-6y2.43, copied with APFS cloning into temporary
paths containing spaces. The source apps/worktrees were read only. No Unity
build was needed. Validation began after the unscripted operator player exited;
other workers' scripted captures and CI were separate processes.

Both copies were packaged with the new launcher and passed macOS deep signature
verification. Temporary Content links supplied the game's existing external
Content requirement. The first attempt without those links exited 2 and still
preserved both complete engine startup/failure/shutdown logs independently.

Successful checks:

- Both native players launched concurrently via `Contents/MacOS/champions-launch`
  at 320×180, Low tier, fixed time, one simulated second; both exited 0.
- Distinct checkout identities and launch UUIDs produced separate engine logs.
  Each contained `Initialize engine version` and `CodeReloadManager destroyed`.
- Repeating one app and launching the other via macOS `open -n -W ... --args`
  created further logs. SHA-256 checks proved the earlier successful logs remained
  byte-for-byte unchanged. The wrapper recorded engine exit status in completion
  markers; startup logging was routed before the native engine initialized.
- The public headless helper's `Logs/player.log` link was readable; deleting the
  temporary checkout/apps left its persistent engine log readable.
- A separate headless capture kept one JPEG and used the capture category;
  `Logs/stills.log` supported the existing CI exception reader.
- A fixture-walk replay exited 0 and retained `Replay:` output with no exception
  through `Logs/player.log`, in its own replay category.
- Explicit file logging wrote the caller's destination. A direct wrapped native
  launch with `-logFile -` emitted engine startup to stdout.
- `champions logs` printed the persistent root. CLI and packaged app routing,
  raw-binary/editor-build exceptions, retention and caller overrides are described
  in [the player log documentation](../../README.md#player-logs).

Local validation reports are `log-isolation-validation.json` and
`log-isolation-consumers.json` beside the cache's `player-logs` directory.
Engine logs, screenshots, audio paths and machine-specific absolute paths stay
local and are not committed. Automated tests also exercise concurrent allocation
and standalone app subprocesses, explicit overrides, paths with spaces, symlink
aliases, repeated launches, CI links, cleanup after removed checkouts and
preservation of active/interrupted runs. No timing thresholds or sleeps are used.
