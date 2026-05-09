# Release checklist

Use this checklist for each manual braid release.

For NuGet push, symbols, API keys, GitHub Release creation, and consumer smoke-test details, see [release-process.md](release-process.md).

## Release variables

Set the intended release version once before running the checklist.

PowerShell:

    $Version = "<version>"
    $Tag = "v$Version"

Example:

    $Version = "0.3.0"
    $Tag = "v$Version"

Do not commit these values into this checklist.

## Preconditions

1. Ensure the working tree is clean:

       git status --short

2. Ensure the current branch is the intended release branch:

       git branch --show-current

3. Ensure the remote points to the expected repository:

       git remote -v

4. Ensure `src/braid/Braid.csproj` contains the intended package version.

5. Ensure `CHANGELOG.md` contains release notes for the intended version.

6. Ensure README does not contain hardcoded package-version drift.

7. Ensure no secrets, API keys, local paths, or machine-specific values are committed.

## Build and test

Run:

    dotnet restore
    dotnet build --configuration Release
    dotnet test --configuration Release

Stop if any command fails.

## Pack

Run:

    dotnet pack --configuration Release --no-build

Expected package artifacts:

    src/braid/bin/Release/braid.$Version.nupkg
    src/braid/bin/Release/braid.$Version.snupkg

If using a shell that does not expand `$Version`, substitute the intended version manually in the command line only. Do not hardcode it in this document.

## Package inspection

Inspect the generated package before publishing.

Confirm:

1. Package id is `braid`.
2. Package version matches the intended version.
3. README is included.
4. XML documentation is included.
5. License metadata is present.
6. Target framework is correct.
7. No secrets, local paths, or machine-specific files are included.

## Local consumer smoke test

Create a temporary directory outside the repository.

PowerShell example:

    mkdir braid-consume-test
    cd braid-consume-test
    dotnet new xunit -f net10.0
    dotnet add package braid --version $Version --source <absolute-path-to-repo>/src/braid/bin/Release
    dotnet test --configuration Release

If the installed xUnit template does not support `-f net10.0`, run `dotnet new xunit`, then set the generated project to `net10.0`.

## Tag

Create the release tag only after build, tests, package inspection, and local smoke test pass.

PowerShell:

    git tag $Tag
    git push origin $Tag

If the tag already exists locally or remotely, stop and inspect. Do not force-update release tags unless explicitly approved.

## Publish

Publish manually only after final review.

Do not publish packages from CI until the release workflow and secrets policy are intentionally designed.

See [release-process.md](release-process.md) for:

- NuGet.org push commands;
- symbols package push;
- GitHub Release creation;
- attaching `.nupkg` and `.snupkg` release assets;
- post-publish verification.

## Final verification

Confirm:

1. Working tree is clean.
2. Release tag exists locally and remotely.
3. NuGet.org shows the intended package version after indexing.
4. GitHub Release exists for the tag.
5. GitHub Release contains both `.nupkg` and `.snupkg` assets.
6. Consumer smoke test works against NuGet.org after indexing.

## Notes

- NuGet package versions are immutable. If a version was published with a mistake, publish a new version.
- Keep README evergreen. Do not add pinned package versions to README.
- Keep exact version references in release notes, tags, package metadata, and release validation commands only.
