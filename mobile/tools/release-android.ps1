# Builds a signed AAB with Shorebird (so it can be patched later) and uploads it to the Play
# Store with fastlane, all on Windows. (No store-version prompt yet: the Evergreen API has no /api/app/version.)
#
#   npm run mobile:release:android                     # Internal testing track
#   npm run mobile:release:android:prod                # PUBLIC RELEASE - see below
#   npm run mobile:release:android -- -Version 1.1.0   # new semver as well as the build number
#
# :prod is not a draft: the production lane submits at 100% rollout, so it goes to Google
# review and then live to everyone on approval. Halting it means cancelling in the Play
# Console while review is pending.

param(
    [string]$Version = "",
    [ValidateSet("internal", "production")]
    [string]$Track = "internal"
)

. "$PSScriptRoot\common.ps1"
Assert-Shorebird
Initialize-Fastlane

$next = Step-AppVersion $Version

Push-Location $MobileRoot
try {
    Write-Host "==> shorebird release android" -ForegroundColor Cyan
    & $Shorebird release android --no-confirm
    if ($LASTEXITCODE -ne 0) { throw "shorebird release android failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

$aab = Join-Path $MobileRoot "build\app\outputs\bundle\release\app-release.aab"
if (-not (Test-Path $aab)) { throw "AAB not found at $aab" }

Write-Host "==> uploading to the Play Store ($Track track) ..." -ForegroundColor Cyan
Push-Location (Join-Path $MobileRoot "android")
try {
    & fastlane android $Track
    if ($LASTEXITCODE -ne 0) { throw "Play Store upload failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "================ DONE ================" -ForegroundColor Green
Write-Host "Version : $next"
Write-Host "Track   : $Track"
if ($Track -eq "internal") {
    Write-Host "Internal testers can install it in about 5 minutes (Play Store -> Internal)."
} else {
    Write-Host "Submitted for Google review at 100% rollout; it goes live by itself once approved."
}
