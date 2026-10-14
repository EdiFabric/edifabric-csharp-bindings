#!/usr/bin/env bash
# Pack EdiFabric.Native.X12 with the four bundled libraries, smoke-test it, and push it to nuget.org.
#
# Needs the .NET 10 SDK and the .NET 8 runtime (for the smoke test). Pushing needs a nuget.org API
# key in NUGET_API_KEY.
#
#   scripts/release.sh --dry-run   pack and smoke-test without pushing
#   scripts/release.sh             push
set -euo pipefail
cd "$(dirname "$0")/.."
root=$PWD

dry_run=false skip_fetch=false
for arg in "$@"; do
    case "$arg" in
        --dry-run) dry_run=true ;;
        --skip-fetch) skip_fetch=true ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done
if ! $dry_run && [ -z "${NUGET_API_KEY:-}" ]; then
    echo "Set NUGET_API_KEY to a nuget.org API key, or use --dry-run." >&2
    exit 1
fi
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' EdiFabric.Native.X12/EdiFabric.Native.X12.csproj | head -n 1)

$skip_fetch || bash scripts/fetch-natives.sh natives
rm -f dist/*
dotnet pack EdiFabric.Native.X12 -c Release -o dist -p:ContinuousIntegrationBuild=true

package="dist/EdiFabric.Native.X12.$version.nupkg"
entries=$(unzip -Z1 "$package")
for expected in runtimes/win-x64/native/edifabric-x12-tools.dll runtimes/linux-x64/native/edifabric-x12-tools.so \
                runtimes/linux-arm64/native/edifabric-x12-tools.so runtimes/osx-arm64/native/edifabric-x12-tools.dylib; do
    grep -qx "$expected" <<< "$entries" || { echo "$package is missing $expected" >&2; exit 1; }
done
echo "$package: all four libraries bundled"

# A separate package folder, so the smoke test cannot pick up an older cached build of this version.
smoke=$(mktemp -d)
trap 'rm -rf "$smoke"' EXIT
cp scripts/smoke-test/SmokeTest.csproj scripts/smoke-test/Program.cs "$smoke"
(
    cd "$smoke"
    dotnet restore --source "$root/dist" --packages "$smoke/packages" -p:EdiFabricVersion="$version"
    dotnet run --no-restore -c Release -p:EdiFabricVersion="$version"
)

if $dry_run; then
    echo "Dry run: packed and smoke-tested $package, nothing pushed."
    exit 0
fi
dotnet nuget push "$package" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
