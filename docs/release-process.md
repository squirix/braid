# Release process

Release changes go through a `develop` → `main` pull request. A `v*` tag on `main` starts the
[Release workflow](../.github/workflows/release.yml). It builds and tests both targets, publishes the package and symbols
to NuGet.org via trusted publishing (OIDC), and creates a draft GitHub Release.

## Prepare and validate the release

1. Set `<Version>` in `Directory.Build.props` and add the matching release notes to `CHANGELOG.md`.
2. From a clean checkout of the release candidate, install the .NET 11 SDK and .NET 10 runtime, then run:

   ```powershell
   dotnet build --configuration Release
   dotnet test --configuration Release --no-build
   dotnet pack --configuration Release --no-build
   ```

3. Inspect `src/braid/bin/Release/braid.<Version>.nupkg` and `braid.<Version>.snupkg`. Check the package ID, version,
   README, and Apache-2.0 license. Verify `lib/net10.0/` and `lib/net11.0/` each contain `Braid.dll` and `Braid.xml`,
   and check the corresponding PDBs in the symbol package.
4. Run the [consumer smoke test](#consumer-smoke-test) against the local package.

## Consumer smoke test

Set `$Version` to the release version and `$Repo` to the absolute path to the braid checkout. From an empty directory
outside the checkout, create a test project and install the local package:

```powershell
dotnet new xunit -f net10.0
dotnet add package braid --version $Version --source "$Repo\src\braid\bin\Release"
```

If the template does not offer the target, create the project with `dotnet new xunit` and set its `TargetFramework`.
Add a test that parses a short schedule and runs one replay iteration with an assertion on probe order.
Run `dotnet test --configuration Release`, then repeat in a separate directory targeting `net11.0`.

## Tag and publish

1. After the release PR is merged, check out the release commit on `main` and confirm the tag is unused locally and remotely.
2. Confirm the GitHub `release` environment allows the tag and `NUGET_USER` is configured. Create and push the tag:

   ```powershell
   $Tag = "v$Version"
   git tag $Tag
   git push origin $Tag
   ```

3. When the workflow completes, review the draft GitHub Release, update its notes from `CHANGELOG.md`, and publish it.
4. Verify the two GitHub release assets and the version, README, dependencies, and target frameworks on NuGet.org.
   After indexing, repeat the smoke test with a fresh consumer project and install from NuGet.org by omitting `--source`.

## Manual fallback

Use this only when the Release workflow is unavailable. Check which publication steps have already succeeded before
resuming. NuGet package versions are immutable.

1. Use the same validated artifacts and tag. If the package has yet to be published, push it with a scoped API key:

   ```powershell
   dotnet nuget push "$Repo\src\braid\bin\Release\braid.$Version.nupkg" --api-key <scoped-key> --source https://api.nuget.org/v3/index.json
   ```

   Keep the key in secret storage. Leave the `.snupkg` next to the `.nupkg` so the same push sends the symbols.
2. Create or finish the GitHub Release for the tag using the matching notes and attach both package files.
3. Run the same post-publish verification as for a workflow release.
