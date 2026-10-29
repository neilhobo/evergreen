# Deploys the Globeweigh Evergreen admin web UI to the API's wwwroot\admin. This IS
# the release process: the installed Tauri shell loads the UI from <api>/admin/ at
# launch, so copying the new build to the server's wwwroot\admin is all a UI release
# takes - users just restart (or reload) the app. No installer, no signing, no updater.
#
#   .\deploy-admin-ui.ps1            # build + deploy to the IIS-published API (C:\Globeweigh\EvergreenApi)
#   .\deploy-admin-ui.ps1 -Local     # build + deploy to the source-tree API wwwroot (dotnet run / IIS Express)
#   .\deploy-admin-ui.ps1 -Target X  # build + deploy to an explicit wwwroot\admin path
#   .\deploy-admin-ui.ps1 -SkipBuild # redeploy the existing dist\ as-is
#   .\deploy-admin-ui.ps1 -AllowOlder # override the no-going-backwards guard (deliberate rollback)
#
# Static files only - the API keeps running; no app_offline needed.
param(
    [switch]$Local,
    [string]$Target,
    [switch]$SkipBuild,
    [switch]$AllowOlder
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
. (Join-Path (Split-Path $root -Parent) 'deploy-ui-common.ps1')

if ($Target) {
    $target = $Target
} elseif ($Local) {
    # This checkout's API wwwroot, wherever the repo happens to live.
    $target = Join-Path (Split-Path $root -Parent) 'Globeweigh.Api\wwwroot\admin'
} else {
    # The server folder over SMB. This script runs from the DEV BOX (the verification below
    # hits http://evergreen:5099), so it must be the share, not the server's own C:\ path —
    # that just creates an empty folder on the dev box and deploys nothing.
    $target = '\\evergreen\Globeweigh\EvergreenApi\wwwroot\admin'
}

if (-not $SkipBuild) {
    Write-Host "Building UI (npm run build)..." -ForegroundColor Cyan
    Push-Location $root
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed." }
    } finally { Pop-Location }
}
if (-not (Test-Path (Join-Path $dist 'index.html'))) { throw "No build output at $dist" }

if (-not (Test-Path $target)) { New-Item -ItemType Directory -Force -Path $target | Out-Null }

# Never move the live UI backwards in time - see deploy-ui-common.ps1 for why.
$build = Assert-UiNotDowngrade -Dist $dist -Target $target -Name 'Admin UI' -AllowOlder:$AllowOlder

Write-Host "Deploying $($build.Version) to $target ..." -ForegroundColor Cyan
# Copy WITHOUT purging: previous hashed bundles must stay so a client holding a
# stale index.html (refresh racing a deploy, cached page) can still load its
# assets - the in-app version check then brings it forward. Old bundles are
# pruned only after 14 days.
robocopy $dist $target /E /NJH /NJS /NDL /NFL | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
$global:LASTEXITCODE = 0
$assetDir = Join-Path $target 'assets'
if (Test-Path $assetDir) {
    Get-ChildItem $assetDir -File | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-14) } |
        Remove-Item -Force
}

$url = if ($Local) { 'http://localhost:5099/admin/' } else { 'http://evergreen:5099/admin/' }
# Prove the release landed: the folder AND the URL must report the build we just shipped.
Assert-UiDeployed -Target $target -Expected $build -Name 'Admin UI' -Url $url
