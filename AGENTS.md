# Repository Guidelines

## Coding Style

- Follow `.editorconfig` for formatting and analyzer severity.
- Use PascalCase for C# member names.
- Do not use underscores in xUnit test method names. Test methods marked with `[Fact]` or `[Theory]` should use descriptive PascalCase names, for example `TryGetReplayTextReturnsTrueForExportableTypedSchedule`.

## Verification

- After changing production or test code, run the smallest relevant `dotnet test` command that covers the change.

## Maintainer Private Docs

- Maintainer-only planning docs live in the private `squirix/braid-private-docs` repository. If you have access, keep it checked out next to this repository as `../braid-private-docs` and read `../braid-private-docs/agent-instructions.md` before planning roadmap or design changes.

## Cursor Cloud specific instructions

This repository is a **.NET 10 library** (no web server, database, or Docker). Local development is build-and-test only.

### SDK

- SDK version is pinned in `global.json` (currently **10.0.300**, `rollForward: latestFeature`).
- The SDK is installed under `~/.dotnet`; ensure `DOTNET_ROOT="$HOME/.dotnet"` and `PATH` includes `$DOTNET_ROOT` (added to `~/.bashrc` during environment setup).

### Common commands

Match CI (`.github/workflows/ci.yml`):

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

For a focused change, run the smallest relevant test project, for example:

```bash
dotnet test tests/braid.tests/Braid.Tests.csproj --filter FullyQualifiedName~YourTestName
```

Featured race-reproduction examples live under `examples/` (for example `examples/lost-update`).

### Linting

There is no separate lint CLI. **StyleCop.Analyzers** and nullable/reference-type checks run during `dotnet build` (warnings are treated as errors via `Directory.Build.props`).

### Packaging

`dotnet pack --configuration Release --no-build` produces `braid.*.nupkg` under `src/braid/bin/Release/`.
