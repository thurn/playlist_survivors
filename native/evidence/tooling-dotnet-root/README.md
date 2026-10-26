# Extraction runtime discovery (hv-n7fn.35)

Observed once during SF1: `~/Documents/vs-extract/tools/ilspycmd -l c
'<game>/.../Managed/PhaserPort.dll'` reported "You must install .NET",
"App host version: 10.0.7", ".NET location: Not found" and both runtime-root
environment variables unset. Homebrew's `dotnet --list-runtimes` already
listed Microsoft.NETCore.App 10.0.7; no installation was needed.

Use the stable opt path:

```sh
DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec ~/Documents/vs-extract/tools/ilspycmd -l c '<game>/Vampire_Survivors.app/Contents/Resources/Data/Managed/PhaserPort.dll'
```

That exact invocation now exits 0 and lists Body. The private extraction's
README documents the root for listing classes and reading a type; its hash
and the checks are in `verified.txt`. No global environment settings, game
files, extracted code or raw class listings are committed. The repair is
invocation configuration, not a missing runtime.

No game behavior changes. Playable command from `native/` remains:
`cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 30 --delayed`.
