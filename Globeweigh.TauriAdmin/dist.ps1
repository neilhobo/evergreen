<#
  Ship the Globeweigh Admin web UI (and optionally the shared API) to the evergreen
  production server. Run from this folder via the `dist` shell command (or .\dist.ps1).

    dist        UI only : rebuild Admin UI + push to the server wwwroot\admin (no downtime, API untouched)
    dist -Api   UI + API: full publish via deploy-to-evergreen.ps1 (app_offline releases the API DLL lock)
#>
param([switch]$Api)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repo = Split-Path $root -Parent

if ($Api) {
    # deploy-to-evergreen.ps1 publishes the API AND rebuilds the Admin UI into wwwroot\admin.
    & (Join-Path $repo 'Globeweigh.Api\deploy\deploy-to-evergreen.ps1')
} else {
    & (Join-Path $root 'deploy-admin-ui.ps1') -Target '\\evergreen\Globeweigh\EvergreenApi\wwwroot\admin'
}
