# Builds Globeweigh.SageSidecar (Release, x86) and copies it to the Sage machine.
#
# The Sage machine is not evergreen, and which box it is (and how it is reached) is a
# per-site fact, so -RemotePath is required rather than defaulted:
#
#   .\deploy.ps1 -RemotePath \\SAGESERVER\c$\Globeweigh\SageSidecar
#   .\deploy.ps1 -RemotePath \\SAGESERVER\c$\Globeweigh\SageSidecar -ComputerName SAGESERVER
#        (also stops/starts the service there - needs admin rights on that box)
#
# The exe.config is NOT copied over an existing one: it carries that site's Sage login and
# the API key. Pass -IncludeConfig to overwrite it deliberately, or edit it in place.
# First-time install on the Sage machine: copy, fill in the config, run install-service.ps1
# there (see its header).

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RemotePath,
    [string]$ComputerName,
    [string]$ServiceName   = 'GlobeweighSageSidecar',
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$IncludeConfig
)

$ErrorActionPreference = 'Stop'

$projectDir  = $PSScriptRoot
$projectFile = Join-Path $projectDir 'Globeweigh.SageSidecar.csproj'
$buildOutput = Join-Path $projectDir "bin\$Configuration"

function Write-Step($msg) { Write-Host ">> $msg" -ForegroundColor Cyan }
function Write-Ok  ($msg) { Write-Host "   $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "   $msg" -ForegroundColor Yellow }

function Find-MSBuild {
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1
        if ($found) { return $found }
    }
    throw "MSBuild not found. Open 'Developer PowerShell for VS' or install VS Build Tools."
}

# 1. Build - x86, always (see the csproj for why).
if (-not $SkipBuild) {
    Write-Step "Building $Configuration|x86..."
    $msbuild = Find-MSBuild
    & $msbuild $projectFile /p:Configuration=$Configuration /p:Platform=x86 /verbosity:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }
    Write-Ok "build complete."
}
if (-not (Test-Path (Join-Path $buildOutput 'Globeweigh.SageSidecar.exe'))) { throw "Build output not found in $buildOutput" }

# 2. Remote reachable?
Write-Step "Checking $RemotePath..."
if (-not (Test-Path $RemotePath)) {
    New-Item -ItemType Directory -Path $RemotePath | Out-Null
    Write-Ok "created."
} else {
    Write-Ok "reachable."
}

# 3. Stop the service if we've been told where it is.
$svc = $null
if ($ComputerName) {
    Write-Step "Stopping $ServiceName on $ComputerName..."
    $svc = Get-Service -ComputerName $ComputerName -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $svc) {
        Write-Warn "not installed there yet - copying only (run install-service.ps1 on $ComputerName)."
    } elseif ($svc.Status -ne 'Stopped') {
        $svc | Stop-Service -Force
        $svc.WaitForStatus('Stopped', '00:00:30')
        Write-Ok "stopped."
    }
} else {
    Write-Warn "No -ComputerName: make sure $ServiceName is stopped on the target before the copy, or the exe will be in use."
}

# 4. Copy. Logs and state (the done-markers) belong to the target; the config is theirs too
#    unless -IncludeConfig.
Write-Step "Copying to $RemotePath..."
$robocopyArgs = @($buildOutput, $RemotePath, '/E', '/XD', 'Logs', 'state', '/R:3', '/W:2', '/NFL', '/NDL', '/NP', '/NJH', '/NJS')
if (-not $IncludeConfig -and (Test-Path (Join-Path $RemotePath 'Globeweigh.SageSidecar.exe.config'))) {
    $robocopyArgs += @('/XF', 'Globeweigh.SageSidecar.exe.config')
    Write-Warn "keeping the existing exe.config on the target (-IncludeConfig overwrites it)."
}
& robocopy @robocopyArgs | Out-Host
if ($LASTEXITCODE -ge 8) { throw "Robocopy failed (exit $LASTEXITCODE)" }
Write-Ok "copy complete (robocopy exit $LASTEXITCODE)."

# 5. Start it again.
if ($svc) {
    Write-Step "Starting $ServiceName on $ComputerName..."
    $svc = Get-Service -ComputerName $ComputerName -Name $ServiceName
    $svc | Start-Service
    $svc.WaitForStatus('Running', '00:00:30')
    Write-Ok "running - check $RemotePath\Logs for the first lines."
}

Write-Host ""
Write-Host "Deploy complete." -ForegroundColor Cyan
