<#
  Web deploy from the DEV BOX to the evergreen production server over Tailscale/SMB.

  Publishes Globeweigh.Api (Release) + rebuilds the Admin UI into wwwroot\admin, then
  robocopies the whole app to the server folder (\\evergreen\Globeweigh\EvergreenApi,
  i.e. C:\Globeweigh\EvergreenApi on the server). After the one-time setup-iis.ps1 on the
  server, this is the entire web release — IIS serves the new files (static UI changes
  immediately; an app_offline toggle releases the DLL lock for API changes).

  MUST be run as a script file (powershell -File ...). Do NOT inline the UNC path in a
  `powershell -Command "..."` invoked from bash — bash collapses the `\\` and the copy
  silently lands in a local .\evergreen folder instead of the share.

  Outstanding Data\Migrations scripts are applied to the server's database first (it
  prompts), so the new code never starts against a schema that hasn't caught up.

  Usage:
    powershell -ExecutionPolicy Bypass -File Globeweigh.Api\deploy\deploy-to-evergreen.ps1
    ... -SkipUi           # API only (no npm build)
    ... -AllowOlder       # let the Admin UI go backwards (deliberate rollback)
    ... -Yes              # don't prompt before applying migrations
    ... -SkipMigrations   # deploy code only
#>
param(
    [string]$Server = '\\evergreen\Globeweigh\EvergreenApi',
    [string]$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [switch]$SkipUi,
    [switch]$AllowOlder,
    [ValidateSet('local', 'prod')]
    [string]$MigrateTarget = 'prod',
    [switch]$SkipMigrations,
    [switch]$Yes,
    # Where to confirm the Admin UI afterwards. Pass '' when deploying somewhere other
    # than production, so the check isn't aimed at a server this run never touched.
    [string]$VerifyUrl = 'http://evergreen:5099/admin/'
)
$ErrorActionPreference = 'Stop'
$api   = Join-Path $Repo 'Globeweigh.Api'
$ui    = Join-Path $Repo 'Globeweigh.TauriAdmin'
$stage = Join-Path $env:TEMP 'gwapi_publish'
. (Join-Path $Repo 'deploy-ui-common.ps1')

$srvTouch = Join-Path $Server 'wwwroot\touch'
$srvAdmin = Join-Path $Server 'wwwroot\admin'

if (-not (Test-Path $Server)) { throw "Server folder not reachable: $Server" }

# --- 1. Admin UI -> source-tree wwwroot\admin (so publish carries it) ---
if (-not $SkipUi) {
    Write-Host "Building + staging Admin UI..." -ForegroundColor Cyan
    & (Join-Path $ui 'deploy-admin-ui.ps1') -Local
}

# --- 2. Publish the API (Release) to a staging folder ---
Write-Host "Publishing API..." -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Push-Location $api
try {
    dotnet publish -c Release -o $stage --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
} finally { Pop-Location }

# --- 2b. Drop UI folders the publish must NOT ship ---
# dotnet publish sweeps up the whole source-tree wwwroot, including whatever stale build
# `deploy-touch-ui.ps1 -Local` / `deploy-admin-ui.ps1 -Local` last left there. Copying that
# over the server silently rolls the live UI back to that snapshot (the old index.html points
# at old bundles, which robocopy never purged, so it loads fine and looks current).
# wwwroot\touch is ALWAYS server-owned - it is released only by TauriTouch\deploy-touch-ui.ps1.
# wwwroot\admin is server-owned too when -SkipUi skipped the rebuild above.
$dropFromStage = @('touch')
if ($SkipUi) { $dropFromStage += 'admin' }
foreach ($d in $dropFromStage) {
    $p = Join-Path $stage "wwwroot\$d"
    if (Test-Path $p) {
        Remove-Item $p -Recurse -Force
        Write-Host "Excluded wwwroot\$d from the publish (server keeps its own copy)." -ForegroundColor DarkGray
    }
}

# What the server is running BEFORE we touch it, so we can prove afterwards that this
# deploy changed only what it was supposed to.
$touchBefore = Get-UiBuild $srvTouch
$adminBefore = Get-UiBuild $srvAdmin
if (-not $SkipUi) {
    # The Admin UI ships inside this publish, so the no-going-backwards check belongs
    # here - deploy-admin-ui.ps1 -Local only ever compared against the source tree.
    $adminBuild = Assert-UiNotDowngrade -Dist (Join-Path $stage 'wwwroot\admin') `
        -Target $srvAdmin -Name 'Admin UI' -AllowOlder:$AllowOlder
}

# --- 3. Schema before code, while the server is still untouched ---
# A migration failure aborts the deploy with production still up and running the
# old build, rather than leaving new code against a schema that hasn't caught up.
# Several sessions add scripts to Data\Migrations, so this is what stops a schema
# change being left behind by whoever deploys next. Data backfills and one-off
# corrections marked "-- @manual" are reported as outstanding, never auto-run.
if ($SkipMigrations) {
    Write-Host "Skipping migrations (-SkipMigrations)." -ForegroundColor Yellow
} else {
    Write-Host "Applying outstanding DB migrations to '$MigrateTarget'..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'migrate-db.ps1') -Target $MigrateTarget -ServerRoot $Server -Yes:$Yes
    if ($LASTEXITCODE) { throw "Migrations failed - nothing was deployed. Fix them, or re-run with -SkipMigrations." }
}

# --- 4. Push to the server (app_offline releases the ANCM/IIS DLL lock) ---
$offline = Join-Path $Server 'app_offline.htm'
Set-Content -Path $offline -Value '<html><body>Updating Globeweigh.Api...</body></html>' -Encoding utf8
Start-Sleep -Seconds 2
Write-Host "Copying to $Server ..." -ForegroundColor Cyan
# /XF preserves server-only files: app_offline marker, the setup script, and the prod
# config (appsettings*.json carry the prod SQL connection string — the repo copy is dev).
robocopy $stage $Server /E /XF app_offline.htm setup-iis.ps1 appsettings.json appsettings.Development.json /NFL /NDL /NJH /R:2 /W:1 | Out-Null
$rc = $LASTEXITCODE
Remove-Item $offline -Force -ErrorAction SilentlyContinue
if ($rc -ge 8) { throw "robocopy failed ($rc)." }
# robocopy uses 0-7 for success (1=copied, 2=extra, 3=both, ...). Reset so the
# script doesn't exit with robocopy's non-zero success code and look like a failure.
$global:LASTEXITCODE = 0

# --- 5. Prove this deploy changed only what it owns ---
# wwwroot\touch is never shipped from here; if it moved, something is writing a UI folder
# it does not own and every Touch screen is about to load it. Fail loudly, immediately.
Assert-UiUnchanged -Target $srvTouch -Name 'Touch UI' -Before $touchBefore
if ($SkipUi) {
    Assert-UiUnchanged -Target $srvAdmin -Name 'Admin UI' -Before $adminBefore
} else {
    Assert-UiDeployed -Target $srvAdmin -Expected $adminBuild -Name 'Admin UI' -Url $VerifyUrl
}
$liveTouch = Get-UiBuild $srvTouch
if ($liveTouch) { Write-Host "Touch UI untouched, still $($liveTouch.Version)." -ForegroundColor DarkGray }

$count = (Get-ChildItem $Server -Recurse -File).Count
Write-Host "Deployed $count files to $Server (robocopy rc=$rc)." -ForegroundColor Green
Write-Host "Verify on server: http://localhost:5099/download/  and  http://192.168.250.5:5099/admin/" -ForegroundColor Green
