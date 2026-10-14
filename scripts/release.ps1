<#
.SYNOPSIS
Pack EdiFabric.Native.X12 with the four bundled libraries, smoke-test it, and push it to nuget.org.

.DESCRIPTION
Needs the .NET 10 SDK and the .NET 8 runtime (for the smoke test). Pushing needs a nuget.org API
key in NUGET_API_KEY. -DryRun packs and smoke-tests without pushing.

.EXAMPLE
./scripts/release.ps1 -DryRun
./scripts/release.ps1
#>
param(
    [switch]$DryRun,
    [switch]$SkipFetch
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$root = $PWD.Path

function Invoke-Checked {
    # Native tools write progress to stderr; only the exit code decides failure.
    $ErrorActionPreference = 'Continue'
    & $args[0] $args[1..($args.Count - 1)] 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "$($args -join ' ') failed with exit code $LASTEXITCODE" }
}

if (-not $DryRun -and -not $env:NUGET_API_KEY) { throw 'Set NUGET_API_KEY to a nuget.org API key, or use -DryRun.' }
$version = ([xml](Get-Content EdiFabric.Native.X12/EdiFabric.Native.X12.csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (-not $SkipFetch) { & "$PSScriptRoot/fetch-natives.ps1" -Destination natives }
if (Test-Path dist) { Get-ChildItem dist -File | Remove-Item }
Invoke-Checked dotnet pack EdiFabric.Native.X12 -c Release -o dist -p:ContinuousIntegrationBuild=true

$package = Get-Item "dist/EdiFabric.Native.X12.$version.nupkg"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try { $entries = $zip.Entries.FullName } finally { $zip.Dispose() }
foreach ($expected in 'runtimes/win-x64/native/edifabric-x12-tools.dll', 'runtimes/linux-x64/native/edifabric-x12-tools.so',
                      'runtimes/linux-arm64/native/edifabric-x12-tools.so', 'runtimes/osx-arm64/native/edifabric-x12-tools.dylib') {
    if ($entries -notcontains $expected) { throw "$($package.Name) is missing $expected" }
}
Write-Host "$($package.Name): $([math]::Round($package.Length / 1MB, 1)) MB, all four libraries bundled"

# A separate package folder, so the smoke test cannot pick up an older cached build of this version.
$smoke = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
New-Item -ItemType Directory $smoke | Out-Null
try {
    Copy-Item scripts/smoke-test/SmokeTest.csproj, scripts/smoke-test/Program.cs $smoke
    Push-Location $smoke
    try {
        Invoke-Checked dotnet restore --source "$root/dist" --packages "$smoke/packages" "-p:EdiFabricVersion=$version"
        Invoke-Checked dotnet run --no-restore -c Release "-p:EdiFabricVersion=$version"
    }
    finally { Pop-Location }
}
finally {
    Remove-Item $smoke -Recurse -Force -ErrorAction SilentlyContinue
}

if ($DryRun) {
    Write-Host "Dry run: packed and smoke-tested $($package.Name), nothing pushed."
    return
}
Invoke-Checked dotnet nuget push $package.FullName --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
