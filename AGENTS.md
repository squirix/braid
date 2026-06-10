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

This repository is a single .NET 10 NuGet library with no long-running services, databases, or Docker dependencies. All development and testing runs in-process via the .NET CLI and xUnit.

### Prerequisites

- **.NET SDK 10.0.300+** (pinned in `global.json`). The SDK must be on `PATH`; if installed via `dotnet-install.sh`, set `DOTNET_ROOT="$HOME/.dotnet"` and prepend `$HOME/.dotnet` to `PATH`.

### Common commands (from repo root)

| Task | Command |
|------|---------|
| Restore | `dotnet restore` |
| Build | `dotnet build --configuration Release` |
| Test (full solution) | `dotnet test --configuration Release` |
| Test (library only) | `dotnet test tests/braid.tests/Braid.Tests.csproj` |
| Test (single example) | `dotnet test examples/lost-update/Braid.Examples.LostUpdate.csproj` |
| Pack | `dotnet pack --configuration Release --no-build` |

StyleCop analyzers and `TreatWarningsAsErrors` run during build; there is no separate lint command. CI workflow (`.github/workflows/ci.yml`) mirrors the restore → build → test → pack sequence above.

### Hello-world verification

Run the lost-update example to confirm braid's core replay-token workflow:

```bash
dotnet test examples/lost-update/Braid.Examples.LostUpdate.csproj
```

Expected: 1 passing test (`ReplayTokenCapturesLostUpdateInterleaving`).
