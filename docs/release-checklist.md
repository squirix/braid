# Release checklist

For NuGet push, symbols, API keys, and a consumer smoke test, see [release-process.md](release-process.md).

## v0.2.1 patch

1. Ensure working tree is clean.
2. Run `dotnet restore`.
3. Run `dotnet build --configuration Release`.
4. Run `dotnet test --configuration Release`.
5. Run `dotnet pack --configuration Release --no-build`.
6. Inspect generated `.nupkg`.
7. Confirm package id is `braid`.
8. Confirm version is `0.2.1`.
9. Confirm README is included.
10. Confirm XML docs are included.
11. Confirm license metadata is present.
12. Create tag only after validation:
    `git tag v0.2.1`
13. Push tag manually:
    `git push origin v0.2.1`
14. Publish manually only after final review.

Do not publish packages from CI until release workflow and secrets policy are intentionally designed.
