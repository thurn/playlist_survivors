# SF1 — sourced VS feel numbers

Deliverable: `docs/survivors/appendix-feel.md`, linked from progression.md.
The tables are selected numerical facts, not copies of raw extracted assets.
No game text, runtime source or binary assets are committed.

- `source-audit.json` keeps the exact input SHA-256 hashes and counts: 45
  cumulative combat rows, 27 passive ranks and 21 pre-Reaper event entries.
- `table-audit.txt` records independent per-field sums of each rank's source
  deltas against the extraction loader's cumulative expansion.
- `runtime-audit.txt` records source checks for speed units/defaults, damage
  lockout, weapon defaults, healing rules, projectile hit memory and pickups.
- `check-fast.txt` records the required final aggregate validation.

The appendix distinguishes nominal weapon rows from projectile-specific
behavior, and VS rules from the demo's fixed adaptations (especially sword
geometry, Soul Eater healing, timed swarms and floor chickens). One scale
conversion example yields a BAT1 at 54.88 px/s for a 140 px/s hero; SF4 records
its chosen scale. This is source analysis, not an empirical gameplay balance
measurement or pacing gate.

Sources were read with the private `scripts/load.py` loader under
`uv run --with UnityPy python ...`; numeric ranks accumulate as in
DataHelper.UpgradeJsonData. The missing coordinate-conversion type was read
from the installed PhaserPort.dll using:

```sh
DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec ~/Documents/vs-extract/tools/ilspycmd -t Body '<installed-game>/Vampire_Survivors.app/Contents/Resources/Data/Managed/PhaserPort.dll'
```

The first ilspycmd invocation failed to locate .NET although Homebrew .NET
10.0.7 is installed; the explicit runtime root above succeeds. Tooling bead
`hv-n7fn.35` records the one failure and the follow-up for the local extraction
instructions. Only derived numerical observations and hashes are retained.

No app/sim behavior changed. The playable run command, from `native/`, remains:

```sh
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 30 --delayed
```

SF2's headless/replay evidence remains applicable; no new operator-only check
is introduced by this documentation bead. No per-bead review is required;
SF10 reviews the foundation milestone's recorded commits.
