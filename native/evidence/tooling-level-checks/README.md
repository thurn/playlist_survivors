# Tooling — level-only native validation (`hv-n7fn.36`)

SF6 found that a `levels/survivors.yaml`-only diff selected neither the native
section nor its affected crates. Both selectors named only `reactive.yaml`.

Both now match `.yaml` and `.yml` files anywhere below `levels/`: these are
custom level inputs accepted by the app as well as the shipped Survivors and
reactive levels. A deleted or renamed level must still select validation; the
selectors deliberately do not require a file to exist. README files, backup
extensions and paths outside `levels/` do not match as level data. Changes to
`changes.sh` itself select the native section and these regression tests,
without selecting Rust crates.

`selector-tests.log`: all 17 tests pass, including the actual POSIX gate
selector, missing paths, both rename directions, unknown-base conservatism,
crate dependency propagation and purity. `real-selector-probe.log` invokes
`native/scripts/crates affected` with real Cargo metadata: Survivors,
reactive and custom YAML all select every crate; unrelated files select none.

Commands:

```sh
uv run --no-project python -m unittest -v native/scripts/test_crates.py
scripts/check-fast
scripts/ci-full
```

The final aggregate and required full local CI outputs are kept alongside this
file. The source commit SHA, delivery candidate and measured durations are in
this bead's native notes. This changes check selection, not game behavior.
The app remains playable; its run command is recorded with delivery. No cold
review is required for this small tooling diff by the project's review policy.

The first full `scripts/ci-full` passed all sections. A final selector-test
trigger was then added; the native full section was rerun on that final tree.
Baker and Python analysis inputs did not change. Both raw logs are kept.
