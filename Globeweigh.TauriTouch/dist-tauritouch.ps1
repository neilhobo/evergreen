<#
  "dist tauritouch" — build the Globeweigh Touch desktop installer and push it to the
  production Releases folder on the server.

  Like dist-tauriadmin.ps1, this is the only part of a TauriTouch release that can be pushed
  to evergreen from a dev box over Tailscale: the Releases folder is an SMB share
  (\\evergreen\Globeweigh\Releases), so building locally + copying the bundle is all it takes.
  The staff download page (api/touch-app) then serves the newest installer.

  NOTE: this ships the desktop SHELL only (loader + window chrome). The actual touch UI (React)
  is server-hosted and deploys separately via deploy-touch-ui.ps1 into the API's wwwroot\touch —
  that path is not reachable over SMB, so it must run on the server. Deploy the UI before widely
  distributing a new shell.

  Usage:
    .\dist-tauritouch.ps1                 # build + push to \\evergreen\Globeweigh\Releases
    .\dist-tauritouch.ps1 -SkipBuild      # push the already-built bundle as-is
    .\dist-tauritouch.ps1 -Releases X     # push to an explicit folder
#>
param(
    [string]$Releases = '\\evergreen\Globeweigh\Releases',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root   = $PSScriptRoot
$bundle = Join-Path $root 'src-tauri\target\release\bundle'

if (-not $SkipBuild) {
    Write-Host "Building installer (tauri build)..." -ForegroundColor Cyan
    & (Join-Path $root 'dev.ps1') build
    if ($LASTEXITCODE -ne 0) { throw "tauri build failed (exit $LASTEXITCODE)." }
}

# Ship the NSIS setup.exe only (+ its .sig, which the updater needs to verify the download).
# The MSI pair is deliberately not shipped.
$artifacts = @()
foreach ($pat in 'nsis\*-setup.exe', 'nsis\*-setup.exe.sig') {
    $artifacts += Get-ChildItem (Join-Path $bundle $pat) -ErrorAction SilentlyContinue
}
if (-not $artifacts) { throw "No setup.exe found under $bundle\nsis. Run without -SkipBuild." }

# Tauri leaves OLDER versioned bundles in place — ship only the highest version so a stale
# build can't be pushed alongside the new one.
$verRx = [regex]'_(\d+(?:\.\d+){1,3})_'
$top = $artifacts | Where-Object { $verRx.IsMatch($_.Name) } |
    Sort-Object { [version]$verRx.Match($_.Name).Groups[1].Value } -Descending |
    Select-Object -First 1
if ($top) {
    $tv = $verRx.Match($top.Name).Groups[1].Value
    $artifacts = $artifacts | Where-Object { $verRx.Match($_.Name).Groups[1].Value -eq $tv }
}

if (-not (Test-Path $Releases)) { throw "Releases folder not reachable: $Releases" }

Write-Host "Pushing $($artifacts.Count) file(s) to $Releases ..." -ForegroundColor Cyan
foreach ($f in $artifacts) {
    Copy-Item $f.FullName -Destination $Releases -Force
    $size = [math]::Round($f.Length / 1MB, 2)
    Write-Host ("  + {0}  ({1} MB)" -f $f.Name, $size) -ForegroundColor Green
}

# Show the version we just shipped (parsed from the setup.exe name).
$setup = $artifacts | Where-Object { $_.Name -like '*-setup.exe' } | Select-Object -First 1
if ($setup -and $setup.Name -match '_(\d+(?:\.\d+){1,3})_') {
    Write-Host "Shipped Globeweigh Touch v$($Matches[1]) to production Releases." -ForegroundColor Green
}
Write-Host "Staff download page will now serve it: http://192.168.250.5:5099/download/" -ForegroundColor Green
