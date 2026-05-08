# Manual release process

Compact checklist for publishing a new `braid` preview or stable version. NuGet package versions are immutable: a typo in a published version cannot be overwritten—publish a new version instead.

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

## Consumer smoke test

From an empty directory (uses .NET 10 xUnit template):

```powershell
mkdir braid-consume-test
cd braid-consume-test
dotnet new xunit -f net10.0
dotnet add package braid --version 0.1.0
```

Add a tiny test (for example `SmokeTest.cs`):

```csharp
using Braid;
using Xunit;

public sealed class SmokeTest
{
    [Fact]
    public async Task Package_resolves_and_braid_runs()
    {
        var done = false;
        await Braid.RunAsync(
            async context =>
            {
                context.Fork(async () =>
                {
                    await BraidProbe.HitAsync("x");
                    done = true;
                });

                await context.JoinAsync();
            },
            new BraidOptions { Iterations = 1, Schedule = BraidSchedule.Replay(new BraidStep("worker-1", "x")) });

        Assert.True(done);
    }
}
```

```powershell
dotnet test --configuration Release
```

## GitHub release

1. Create an annotated or lightweight tag for the version (for example `v0.1.0`) after validation.
2. Push the tag: `git push origin v0.1.0`.
3. Create a GitHub **Release** from that tag. Attach notes that match `CHANGELOG.md` for that version.

## NuGet.org

1. Push the `.nupkg`: `dotnet nuget push path\to\braid.0.1.0.nupkg --api-key <scoped-key> --source https://api.nuget.org/v3/index.json`
2. Push the `.snupkg` the same way (same API key is typical).
3. Open the package page on NuGet and verify metadata, README rendering, dependencies, and target framework.
4. Confirm **owners** on the package; use a **scoped** API key with push-only permissions where possible.
5. Never commit API keys or paste them into the repository.

## Related

- Shorter checklist: [release-checklist.md](release-checklist.md).
