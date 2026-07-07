param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Get-ChildItem -Path $root -Directory | ForEach-Object {
    $file = Join-Path $_.FullName "$($_.Name).cs"
    if (Test-Path $file) {
        Write-Host "Running $file"
        dotnet run --configuration $Configuration --file $file
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}
