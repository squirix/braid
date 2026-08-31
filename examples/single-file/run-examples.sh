#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
configuration="${1:-Release}"

for dir in "$root"/*/; do
    name="$(basename "$dir")"
    file="$dir$name.cs"

    if [[ -f "$file" ]]; then
        echo "Running $file"
        dotnet run --file "$file" --configuration "$configuration"
    fi
done
