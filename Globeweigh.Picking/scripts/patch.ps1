# Push a Shorebird over-the-air patch to the CURRENT release version in
# pubspec.yaml — no APK download, no version bump, no Devices-tab push.
# The tablet picks the patch up on next app launch (applied the launch after).
#
# Only Dart code changes can be patched. Native plugin changes, asset/font
# changes, or a Flutter upgrade need a full release (npm run release).
# Shorebird detects such diffs and refuses the patch.
#
# Usage from repo root:
#   npm run patch                            # publish to the tablet (stable)
#   npm run patch -- -Track staging          # stage for testing first
#
# A staged patch is invisible to devices; test it with:
#   shorebird preview --track staging
# then promote it from console.shorebird.dev (or shorebird patches set-track).

param(
    [ValidateSet("stable", "staging")]
    [string]$Track = "stable"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

$shorebird = "$env:USERPROFILE\.shorebird\bin\shorebird.ps1"
if (-not (Test-Path $shorebird)) {
    throw "Shorebird CLI not found at $shorebird. Install: https://docs.shorebird.dev"
}

# Which release to patch. Without this the CLI asks "Which release would you like to
# patch?" and, because --no-confirm makes the run non-interactive, exits 64 instead.
# pubspec.yaml is the right answer by definition: it is the version the current source
# was released as, and the one the tablets are running.
$pubspec = Join-Path $repoRoot "pubspec.yaml"
$line = Get-Content $pubspec | Where-Object { $_ -match '^version:\s*(\d+\.\d+\.\d+)\+(\d+)\s*$' } | Select-Object -First 1
if (-not $line) { throw "Could not find 'version: x.y.z+n' line in pubspec.yaml." }
$null = $line -match '^version:\s*(\d+\.\d+\.\d+)\+(\d+)\s*$'
$releaseVersion = "$($Matches[1])+$($Matches[2])"
Write-Host "Patching release $releaseVersion (track: $Track)" -ForegroundColor Cyan

Push-Location $repoRoot
try {
    & $shorebird patch android --release-version=$releaseVersion --track $Track --no-confirm
    if ($LASTEXITCODE -ne 0) { throw "shorebird patch android failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "================ DONE ================" -ForegroundColor Green
Write-Host "Track : $Track"
if ($Track -eq "stable") {
    Write-Host "The tablet gets the patch on its next app launch (active the launch after"
    Write-Host "the download). No APK download or install prompt."
} else {
    # ASCII only inside strings: Windows PowerShell reads this UTF-8 file as ANSI, and an
    # em dash decodes to a smart quote that terminates the string mid-line.
    Write-Host "Staged only - no devices affected. Test with:"
    Write-Host "  shorebird preview --track staging"
    Write-Host "then promote to stable via console.shorebird.dev or:"
    Write-Host "  shorebird patches set-track --release-version <x.y.z+n> --patch-number <n> --track stable"
}
