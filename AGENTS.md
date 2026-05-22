# Repository Guidelines

## Coding Style

- Follow `.editorconfig` for formatting and analyzer severity.
- Use PascalCase for C# member names.
- Do not use underscores in xUnit test method names. Test methods marked with `[Fact]` or `[Theory]` should use descriptive PascalCase names, for example `TryGetReplayTextReturnsTrueForExportableTypedSchedule`.

## Verification

- After changing production or test code, run the smallest relevant `dotnet test` command that covers the change.
