# Oracle fixtures

The TS reactive game's runs at the tag **`ts-freeze`** (`e97ef4a`), for the
Rust sim, recorder and evaluator to match bit for bit. The format, and the
procedure the Rust harness must follow, is in [FORMAT.md](FORMAT.md); the
plan is [docs/design/native-app.md](../../docs/design/native-app.md#oracle-fixtures).

## Regenerating

The fixtures are generated once. Regenerate them only from `ts-freeze` plus the
exporter's commit (the exporter is instrumentation only; it changes no game
code), and only in a bead that is allowed to change the oracle's inputs:

```bash
git worktree add /tmp/oracle ts-freeze
git -C /tmp/oracle cherry-pick <the commit that added scripts/oracle-export.ts>
cd /tmp/oracle && npm ci
npm run oracle:export                    # every run, the math vectors and the content hash (~15 min on 10 jobs)
npm run oracle:export -- truncation      # truncation.json
npm run oracle:export -- stills          # stills/
npm run oracle:export -- sizes
```

Then copy `native/oracle/` back. The reference stills come from the TS
renderer: `npm run oracle:export -- stills` runs `npm run capture` (a dev
server on the checkout's own port, headless Chrome, 1920 × 1080) for each
moment listed in the exporter, and `stills/index.json` records each still's
track, profile, song time and capture query.

## The inputs are pinned

Every fixture's `meta.json` names the SHA-256 of its track's `stream.json`,
`levels/reactive.yaml`, and the tree hashes of `content/` and
`public/assets/`. Until the TS game is removed (D6), those inputs don't change,
streams aren't regenerated with the Python tool, and content isn't re-baked,
except in a bead that also regenerates these fixtures from `ts-freeze`.

## The truncation check

`truncation.json` holds the TS game's result on the truncation check, run once
(the [plan](../../docs/design/native-app.md#causality-checks)). It runs as
`check:reactive`'s perturbation check does (the same check level and options,
`reactive-moderate` and `mash4`, the default cuts just before every entry's
`knownAt` and after every level-up, wave and set piece), but every stream entry
known after the cut is deleted (the song's length held, since TS can't run
without it): on `causality-mix` with the check level and on the Galop with the
reactive level. A negative control (the sim reading the evidence a second
ahead) must fail it. Each failing cut is a read of the stream's end that the
perturbation check never exposed; the design's Live Exceptions lists them.

## Sizes and findings

Measured at export: [native/evidence/f2/](../evidence/f2/README.md).
