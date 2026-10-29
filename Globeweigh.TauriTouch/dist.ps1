<#
  Ship the Globeweigh Touch web UI (and optionally the shared API) to the evergreen
  production server. Run from this folder via the `dist` shell command (or .\dist.ps1).

    dist        UI only : rebuild Touch UI + push to the server wwwroot\touch (no downtime, API untouched)
    dist -Api   UI + API: publish the shared API (app_offline) THEN push the Touch UI

  The API is shared with TauriAdmin; -Api publishes it without rebuilding the Admin UI.
#>
param([switch]$Api)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repo = Split-Path $root -Parent

if ($Api) {
    # Publish the shared API only (no Admin UI rebuild), then push the Touch UI below.
    & (Join-Path $repo 'Globeweigh.Api\deploy\deploy-to-evergreen.ps1') -SkipUi
}

& (Join-Path $root 'deploy-touch-ui.ps1') -Target '\\evergreen\Globeweigh\EvergreenApi\wwwroot\touch'
