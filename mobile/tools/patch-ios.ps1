# Pushes a Shorebird over-the-air patch to iOS users of the CURRENT release (the version in
# mobile/pubspec.yaml): no TestFlight, no App Store review, no version bump. Builds on the Mac.
#
# Only Dart changes can be patched; native, asset, entitlement or Flutter changes need a full
# release (npm run mobile:release:ios).
#
#   npm run mobile:patch:ios                           # to everyone (stable)
#   npm run mobile:patch:ios -- -Track staging
#   npm run mobile:patch:ios -- -AllowAssetDiffs       # see below

param(
    [ValidateSet("stable", "staging")]
    [string]$Track = "stable",
    # Shorebird refuses a patch whose assets differ from the release; adding a pub package
    # always trips it (the NOTICES licence file changes). Pass this only once the failed run's
    # "Changed files" are all inert: the patch ships with the RELEASE's assets.
    [switch]$AllowAssetDiffs
)

. "$PSScriptRoot\common.ps1"

$v = Get-AppVersion
Write-Host "==> patching iOS release $($v.Full) (track: $Track)" -ForegroundColor Cyan
if ($AllowAssetDiffs) { Write-Host "==> --allow-asset-diffs: asset changes will NOT reach users" -ForegroundColor Yellow }
Sync-ToMac

$assetFlag = if ($AllowAssetDiffs) { "--allow-asset-diffs" } else { "" }
# --release-version is required: over SSH there's no tty for shorebird to ask which release.
Invoke-OnMac @"
shorebird patch ios --release-version=$($v.Full) --track $Track --export-options-plist=ios/ExportOptions.plist --no-confirm $assetFlag
"@

Write-Host ""
Write-Host "================ DONE ================" -ForegroundColor Green
Write-Host "Release : $($v.Full)"
Write-Host "Track   : $Track"
if ($Track -eq "stable") {
    Write-Host "iOS users on this release get it on their next launch, with a prompt to restart."
} else {
    Write-Host "Staged only. Promote it at console.shorebird.dev, or:"
    Write-Host "  shorebird patches set-track --release-version $($v.Full) --patch-number <n> --track stable"
}
