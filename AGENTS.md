# Repository Guidelines

## Coding Style

- Follow `.editorconfig` for formatting and analyzer severity.
- Use PascalCase for C# member names.
- Do not use underscores in xUnit test method names. Test methods marked with `[Fact]` or `[Theory]` should use descriptive PascalCase names, for example `TryGetReplayTextReturnsTrueForExportableTypedSchedule`.

## Verification

- After changing production or test code, run the smallest relevant `dotnet test` command that covers the change.

## Maintainer Private Docs

- Maintainer-only planning docs live in the private `squirix/braid-private-docs` repository. If you have access, keep it checked out next to this repository as `../braid-private-docs` and read `../braid-private-docs/agent-instructions.md` before planning roadmap or design changes.
