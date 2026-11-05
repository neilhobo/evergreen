# Pushes a Shorebird over-the-air patch to Android users of the CURRENT release (the version
# in mobile/pubspec.yaml): no Play Store upload, no version bump. The app downloads it on its
# next launch or resume and offers a restart.
#
# Only Dart changes can be patched. Native plugins, assets/fonts or a Flutter upgrade need a
# full release (npm run mobile:release:android); Shorebird spots those and refuses the patch.
#
#   npm run mobile:patch:android                       # to everyone (stable)
#   npm run mobile:patch:android -- -Track staging     # staged; try it with: shorebird preview --track staging

param(
    [ValidateSet("stable", "staging")]
    [string]$Track = "stable"
)

. "$PSScriptRoot\common.ps1"
Assert-Shorebird

$v = Get-AppVersion
Write-Host "==> patching Android release $($v.Full) (track: $Track)" -ForegroundColor Cyan
Push-Location $MobileRoot
try {
    & $Shorebird patch android --release-version=$($v.Full) --track $Track --no-confirm
    if ($LASTEXITCODE -ne 0) { throw "shorebird patch android failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "================ DONE ================" -ForegroundColor Green
Write-Host "Release : $($v.Full)"
Write-Host "Track   : $Track"
if ($Track -eq "stable") {
    Write-Host "Android users on this release get it on their next launch, with a prompt to restart."
} else {
    Write-Host "Staged only. Promote it at console.shorebird.dev, or:"
    Write-Host "  shorebird patches set-track --release-version $($v.Full) --patch-number <n> --track stable"
}
