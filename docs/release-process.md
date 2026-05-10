# Manual release process

Compact checklist for publishing a new `braid` preview or stable version. NuGet package versions are immutable: a typo in a published version cannot be overwritten—publish a new version instead.

## Release variables (PowerShell)

Set the version once for all commands in a session. The value must match `<Version>` in `src/braid/Braid.csproj` and the section you are publishing in `CHANGELOG.md`.

```powershell
$Version = "<version>"
$Tag = "v$Version"
$Repo = "<absolute-path-to-this-repository>"
```

Example (illustration only—do not commit these lines with real numbers into the repo):

```powershell
$Version = "0.3.1"
$Tag = "v$Version"
$Repo = "C:\Source\braid"
```

## Preconditions

- Working tree clean (`git status`).
- Version in `src/braid/Braid.csproj` matches the intended NuGet version.
- No secrets committed; API keys stay in user or CI secret storage only.

## Build and pack

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet pack --configuration Release --no-build
```

Inspect the output `.nupkg` (and `.snupkg` when symbols are enabled):

- Package id is `braid`.
- README is at package root.
- XML documentation is present in the assembly.
- License expression matches repository intent (MIT).

Expected artifact names (substitute your `$Version` when checking paths):

```text
src/braid/bin/Release/braid.<Version>.nupkg
src/braid/bin/Release/braid.<Version>.snupkg
```

## Consumer smoke test

From an empty directory **outside** the repository (uses .NET 10 xUnit template when available):

```powershell
mkdir braid-consume-test
cd braid-consume-test
dotnet new xunit -f net10.0
dotnet add package braid --version $Version
```

If the installed xUnit template does not support `-f net10.0`, run `dotnet new xunit`, set `<TargetFramework>net10.0</TargetFramework>` in the generated project, and add a `global.json` that selects the .NET 10 SDK.

**Before NuGet publishing**, validate the locally packed package instead:

```powershell
dotnet add package braid --version $Version --source "$Repo\src\braid\bin\Release"
```

Add a tiny test (for example `SmokeTest.cs`) that runs one replay iteration and optionally checks `BraidSchedule.Parse` / `ToReplayText()` round-trip.

```powershell
dotnet test --configuration Release
```

After publishing to NuGet.org, you can repeat `dotnet add package braid --version $Version` **without** `--source` once the package is indexed, and run the same tests.

Optionally smoke-test **text replay** against the live package: parse a short schedule with `BraidSchedule.Parse(...)`, run one iteration under replay, and assert expected probe ordering. Use the same `$Version` as in `src/braid/Braid.csproj`.

## GitHub release

1. Create an annotated or lightweight tag for the version: `git tag $Tag` (after validation).
2. Push the tag: `git push origin $Tag`.
3. Create a GitHub **Release** from that tag. Attach notes that match `CHANGELOG.md` for that version.
4. Attach both `braid.$Version.nupkg` and `braid.$Version.snupkg` from `src/braid/bin/Release/` as release assets (same filenames as produced by `dotnet pack`).

## NuGet.org

1. Push the `.nupkg`: `dotnet nuget push path\to\braid.$Version.nupkg --api-key <scoped-key> --source https://api.nuget.org/v3/index.json`
2. Push the `.snupkg` the same way (same API key is typical).
3. Open the package page on NuGet and verify metadata, README rendering, dependencies, and target framework.
4. Confirm **owners** on the package; use a **scoped** API key with push-only permissions where possible.
5. Never commit API keys or paste them into the repository.

## Related

- Shorter checklist: [release-checklist.md](release-checklist.md).
