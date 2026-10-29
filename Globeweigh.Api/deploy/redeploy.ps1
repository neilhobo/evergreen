<#
  Rebuild the web UI + republish Globeweigh.Api into the IIS folder, with zero-downtime-ish
  handling of the file lock (app_offline.htm tells IIS/ANCM to release the app while we copy).

  Does NOT need administrator (assuming setup-iis.ps1 already granted the app-pool identity
  access to the folder). Run it any time you change the API or the web UI.

  Usage:  powershell -ExecutionPolicy Bypass -File deploy\redeploy.ps1
#>

param(
    [string]$Pub      = 'C:\Globeweigh\EvergreenApi',
    [string]$Repo     = 'D:\Git-Globeweigh\Evergreen',
    [switch]$SkipWeb                      # pass -SkipWeb to redeploy API only (no npm build)
)

$ErrorActionPreference = 'Stop'
$api = Join-Path $Repo 'Globeweigh.Api'
$web = Join-Path $Repo 'Globeweigh.WebAdmin'

# --- 1. Build the web UI and stage it into the API's wwwroot (publish copies wwwroot) ---
if (-not $SkipWeb) {
    Write-Host "Building web UI..." -ForegroundColor Cyan
    Push-Location $web
    npm run build
    Pop-Location
    $gateWeb = Join-Path $api 'wwwroot\gate-web'
    New-Item -ItemType Directory -Force $gateWeb | Out-Null
    Copy-Item (Join-Path $web 'dist\*') $gateWeb -Recurse -Force
}

# --- 2. Take the IIS app offline to release the DLL lock ---
$offline = Join-Path $Pub 'app_offline.htm'
if (Test-Path $Pub) {
    Set-Content -Path $offline -Value '<html><body>Updating Globeweigh.Api...</body></html>' -Encoding utf8
    Start-Sleep -Seconds 2   # let ANCM shut the worker down and release the lock
}

# --- 3. Publish over the top ---
Write-Host "Publishing API to $Pub..." -ForegroundColor Cyan
Push-Location $api
try {
    dotnet publish -c Release -o $Pub --nologo
}
finally {
    Pop-Location
    # --- 4. Bring it back online (publish leaves app_offline.htm untouched) ---
    if (Test-Path $offline) { Remove-Item $offline -Force }
}

Write-Host "Redeploy complete. http://localhost:5099/gate-web/" -ForegroundColor Green
