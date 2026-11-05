# Builds the iOS release on the Mac with Shorebird (so it can be patched later) and uploads
# it with fastlane. (No store-version prompt yet: the Evergreen API has no /api/app/version.)
#
#   npm run mobile:release:ios                         # TestFlight
#   npm run mobile:release:ios:prod                    # TestFlight, then App Store review (auto-release)
#   npm run mobile:release:ios -- -Version 1.1.0
#   $env:CHANGELOG="First build"; npm run mobile:release:ios

param(
    [string]$Version = "",
    [ValidateSet("beta", "production")]
    [string]$Track = "beta"
)

. "$PSScriptRoot\common.ps1"

$next = Step-AppVersion $Version
Sync-ToMac

Write-Host "==> building on the Mac and uploading ($Track) ..." -ForegroundColor Cyan
$changelog = if ($env:CHANGELOG) { $env:CHANGELOG } else { "Build $next" }
Invoke-OnMac -Vars @{ CHANGELOG = $changelog; TRACK = $Track } @"
# Shorebird wraps flutter build ipa with its pinned Flutter and registers the release so it
# can be patched; the IPA lands in build/ios/ipa/ where fastlane looks for it.
shorebird release ios --export-options-plist=ios/ExportOptions.plist --no-confirm
cd ios
CHANGELOG="`$CHANGELOG" fastlane ios "`$TRACK"
"@

Write-Host ""
Write-Host "================ DONE ================" -ForegroundColor Green
Write-Host "Version : $next"
Write-Host "Track   : $Track"
if ($Track -eq "production") {
    Write-Host "Submitted for App Store review; it's released by itself once approved (typically 1-3 days)."
} else {
    Write-Host "Processing in TestFlight (about 10 minutes): App Store Connect -> TestFlight."
}
