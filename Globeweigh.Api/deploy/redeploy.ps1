<#
  Republish Globeweigh.Api into the IIS folder, with zero-downtime-ish handling of the
  file lock (app_offline.htm tells IIS/ANCM to release the app while we copy).

  Does NOT need administrator (assuming setup-iis.ps1 already granted the app-pool identity
  access to the folder). Run it any time you change the API.

  Usage:  powershell -ExecutionPolicy Bypass -File deploy\redeploy.ps1
#>

param(
    [string]$Pub      = 'C:\Globeweigh\EvergreenApi',
    [string]$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
$api = Join-Path $Repo 'Globeweigh.Api'

# --- 1. Take the IIS app offline to release the DLL lock ---
$offline = Join-Path $Pub 'app_offline.htm'
if (Test-Path $Pub) {
    Set-Content -Path $offline -Value '<html><body>Updating Globeweigh.Api...</body></html>' -Encoding utf8
    Start-Sleep -Seconds 2   # let ANCM shut the worker down and release the lock
}

# --- 2. Publish over the top ---
Write-Host "Publishing API to $Pub..." -ForegroundColor Cyan
Push-Location $api
try {
    dotnet publish -c Release -o $Pub --nologo
}
finally {
    Pop-Location
    # --- 3. Bring it back online (publish leaves app_offline.htm untouched) ---
    if (Test-Path $offline) { Remove-Item $offline -Force }
}

Write-Host "Redeploy complete. http://localhost:5099/" -ForegroundColor Green
