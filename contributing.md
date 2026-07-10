# Contributing to braid

Thanks for checking out **braid** — a .NET library for deterministic concurrency testing with explicit async probe
points and replay schedules.

Report bugs and suggest improvements via [GitHub Issues](https://github.com/squirix/braid/issues).

## Guidelines

- Follow `.editorconfig` for formatting and analyzer severity.
- Use PascalCase for C# member names.
- Do not use underscores in xUnit test method names. Test methods marked with `[Fact]` or `[Theory]` should use
  descriptive PascalCase names, for example `TryGetReplayTextReturnsTrueForExportableTypedSchedule`.
- Keep changes focused; add or update tests when behavior changes.
- After changing production or test code, run the smallest relevant `dotnet test` command that covers the change.

## Development

This repository is a single .NET 10 NuGet library. All development and testing runs in-process via the .NET CLI and
xUnit — no databases, Docker, or long-running services.

### Prerequisites

- **.NET SDK 10.0.300+** (pinned in `global.json`).

### Common commands (from repository root)

| Task | Command |
|------|---------|
| Restore | `dotnet restore` |
| Build | `dotnet build --configuration Release` |
| Test (full solution) | `dotnet test --configuration Release` |
| Test (library only) | `dotnet test tests/braid.tests/Braid.Tests.csproj` |
| Run (single example) | `dotnet run examples/single-file/lost-update/lost-update.cs` |
| Run (all examples) | `powershell -File examples/single-file/run-examples.ps1` (Windows) or `bash examples/single-file/run-examples.sh` (Linux/macOS) |
| Pack | `dotnet pack --configuration Release --no-build` |

StyleCop analyzers and `TreatWarningsAsErrors` run during build; there is no separate lint command. CI (`.github/workflows/ci.yml`)
mirrors restore → build → test → file-based examples → pack.

### Hello-world verification

Run the lost-update example to confirm braid's core replay-token workflow:

```bash
dotnet run examples/single-file/lost-update/lost-update.cs
```

Expected: 1 passing test (`ReplayTokenCapturesLostUpdateInterleaving`).

## Submit changes

1. Fork the repository, branch from `main`, and make your change.
2. Run `dotnet build --configuration Release` and `dotnet test --configuration Release` (or a narrower test command when
   appropriate).
3. Open a pull request targeting `main` with a short description. Link related issues with `Fixes #123` in the PR body when
   applicable.

## License

By contributing, you agree your code is under the [MIT License](./LICENSE).
