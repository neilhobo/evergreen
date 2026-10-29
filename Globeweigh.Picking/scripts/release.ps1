# Build a new FULL release of the Picking app as a Shorebird-patchable APK.
#
# Bumps the build number in pubspec.yaml, runs `shorebird release android`
# (which wraps `flutter build` with Shorebird's pinned Flutter and registers
# the release on shorebird.dev), and drops a versioned APK ready to upload.
#
# A full release is needed for: first Shorebird rollout, native/plugin changes,
# asset or font changes, or a Flutter upgrade. Dart-only changes should use
# `npm run patch` instead — no APK download on the tablet.
#
# Usage from repo root:
#   npm run release                          # bump build number only (1.0.5+6 -> 1.0.5+7)
#   npm run release -- -Version 1.1.0        # also bump the semver
#
# Afterwards: TauriAdmin -> Devices tab -> upload the printed APK -> push to the ET40.

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

$shorebird = "$env:USERPROFILE\.shorebird\bin\shorebird.ps1"
if (-not (Test-Path $shorebird)) {
    throw "Shorebird CLI not found at $shorebird. Install: https://docs.shorebird.dev"
}

# ---- bump version in pubspec.yaml ----------------------------------------
$pubspec = Join-Path $repoRoot "pubspec.yaml"
$lines = Get-Content $pubspec
$idx = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^version:\s*(\d+\.\d+\.\d+)\+(\d+)\s*$') {
        $idx = $i
        $semver = $Matches[1]
        $build = [int]$Matches[2]
        break
    }
}
if ($idx -lt 0) { throw "Could not find 'version: x.y.z+n' line in pubspec.yaml." }

$nextSemver = if ($Version) { $Version } else { $semver }
$nextBuild = $build + 1
$lines[$idx] = "version: $nextSemver+$nextBuild"
Set-Content -Path $pubspec -Value $lines -Encoding utf8
Write-Host "==> bumped: $semver+$build  ->  $nextSemver+$nextBuild"

# ---- shorebird release (APK for sideload — no Play Store here) -----------
Push-Location $repoRoot
try {
    & $shorebird release android --artifact apk --no-confirm
    if ($LASTEXITCODE -ne 0) { throw "shorebird release android failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

# Versioned copy so the server's /app landing page picks the newest by filename.
$builtApk = Join-Path $repoRoot "build\app\outputs\flutter-apk\app-release.apk"
$namedApk = Join-Path $repoRoot "build\globeweigh-picking-$nextSemver.apk"
Copy-Item $builtApk $namedApk -Force

Write-Host ""
Write-Host "================ DONE ================" -ForegroundColor Green
Write-Host "Version : $nextSemver+$nextBuild"
Write-Host "APK     : $namedApk"
Write-Host ""
Write-Host "Next:"
Write-Host "  1. TauriAdmin -> Devices tab -> upload the APK above as a package."
Write-Host "  2. Push it to the ET40 (the tablet downloads + self-installs as before)."
Write-Host "  3. From then on, Dart-only changes ship instantly with:  npm run patch"
