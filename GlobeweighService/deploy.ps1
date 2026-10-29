# Deploys GlobeweighService to \\evergreen\Globeweigh\ScannerService.
#
# Usage (from a regular PowerShell window on your dev machine):
#   .\deploy.ps1
#
# What it does:
#   1. Builds the project in Release using MSBuild
#   2. Stops the GlobeweighService on \\evergreen
#   3. Mirrors the build output to the share (excluding Logs)
#   4. Starts the service back up
#   5. Hits http://evergreen:8765/health to confirm it's serving
#
# First-time install (run once, on \\evergreen, as Administrator):
#   sc create GlobeweighService binPath= "C:\Globeweigh\ScannerService\GlobeweighService.exe" start= auto DisplayName= "Globeweigh Service"
#   sc failure GlobeweighService reset= 86400 actions= restart/5000/restart/5000/restart/5000
#   sc failureflag GlobeweighService 1
#   netsh http add urlacl url=http://+:8765/ user=Everyone
#   netsh advfirewall firewall add rule name="Globeweigh Weighbridge HTTP" dir=in action=allow protocol=TCP localport=8765
#   (then run this script to deliver the binaries)
#
# Migrating an existing 'GlobeweighScannerService' install to 'GlobeweighService'
# (run once on \\evergreen as Administrator):
#   sc stop   GlobeweighScannerService
#   sc delete GlobeweighScannerService
#   sc create GlobeweighService binPath= "C:\Globeweigh\ScannerService\GlobeweighService.exe" start= auto DisplayName= "Globeweigh Service"
#   sc failure GlobeweighService reset= 86400 actions= restart/5000/restart/5000/restart/5000
#   sc failureflag GlobeweighService 1
#   (then run this script - it will deliver the new binaries and start the new service)

[CmdletBinding()]
param(
    [string]$RemoteServer  = 'evergreen',
    [string]$RemotePath    = '\\evergreen\Globeweigh\ScannerService',
    [string]$ServiceName   = 'GlobeweighService',
    [string]$Configuration = 'Release',
    [int]   $HealthPort    = 8765,
    [switch]$SkipBuild,
    [switch]$SkipHealthCheck,
    # -CopyOnly: skip Stop-Service / Start-Service / health-check.
    # Use this when the dev machine doesn't have rights to manage the
    # service on the remote server - you stop & start it yourself.
    [switch]$CopyOnly
)

$ErrorActionPreference = 'Stop'

$projectDir  = $PSScriptRoot
$projectFile = Join-Path $projectDir 'GlobeweighService.csproj'
$buildOutput = Join-Path $projectDir "bin\$Configuration"

function Write-Step($msg) { Write-Host ">> $msg" -ForegroundColor Cyan }
function Write-Ok  ($msg) { Write-Host "   $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "   $msg" -ForegroundColor Yellow }

function Find-MSBuild {
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -prerelease -products * `
            -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null |
            Select-Object -First 1
        if ($found) { return $found }
    }

    throw "MSBuild not found. Open 'Developer PowerShell for VS' or install VS Build Tools."
}

# 1. Build
if (-not $SkipBuild) {
    Write-Step "Building $Configuration..."
    $msbuild = Find-MSBuild
    & $msbuild $projectFile `
        /p:Configuration=$Configuration `
        /p:Platform=AnyCPU `
        /verbosity:minimal `
        /nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }
    Write-Ok "build complete."
} else {
    Write-Step "Skipping build (-SkipBuild)."
}

if (-not (Test-Path $buildOutput)) {
    throw "Build output not found: $buildOutput"
}

# 2. Verify share is reachable
Write-Step "Checking remote path $RemotePath..."
if (-not (Test-Path $RemotePath)) {
    throw "Remote path unreachable: $RemotePath"
}
Write-Ok "share reachable."

# 3. Stop service on remote (if it exists)
$serviceWasInstalled = $false
if ($CopyOnly) {
    Write-Step "Skipping service stop (-CopyOnly). Make sure you've stopped $ServiceName on $RemoteServer before continuing."
} else {
    Write-Step "Stopping $ServiceName on $RemoteServer..."
    $svc = Get-Service -ComputerName $RemoteServer -Name $ServiceName -ErrorAction SilentlyContinue
    $serviceWasInstalled = $null -ne $svc
    if (-not $serviceWasInstalled) {
        Write-Warn "Service not installed on $RemoteServer. (See first-time install steps in script header.)"
        Write-Warn "Will copy files only - service won't be started."
    } elseif ($svc.Status -ne 'Stopped') {
        $svc | Stop-Service -Force
        $svc.WaitForStatus('Stopped', '00:00:30')
        Write-Ok "stopped."
    } else {
        Write-Ok "already stopped."
    }
}

# 4. Copy via robocopy. /E mirrors subdirs, excludes Logs and existing config we don't want to replace.
#    We DO copy the .exe.config so new appSettings (like WeighbridgeListenPort) get delivered.
Write-Step "Copying to $RemotePath..."
$robocopyArgs = @(
    $buildOutput,
    $RemotePath,
    '/E',
    '/XD', 'Logs',
    '/R:3', '/W:2',
    '/NFL', '/NDL', '/NP',
    '/NJH', '/NJS'
)
& robocopy @robocopyArgs | Out-Host
# robocopy: 0=no change, 1=copied, 2-3=mismatch ok, 4-7=ok+xtras, 8+=error
if ($LASTEXITCODE -ge 8) { throw "Robocopy failed (exit $LASTEXITCODE)" }
Write-Ok "copy complete (robocopy exit $LASTEXITCODE)."

# 5. Start service
if ($CopyOnly) {
    Write-Step "Skipping service start (-CopyOnly). Start $ServiceName on $RemoteServer when ready."
} elseif ($serviceWasInstalled) {
    Write-Step "Starting $ServiceName on $RemoteServer..."
    $svc = Get-Service -ComputerName $RemoteServer -Name $ServiceName
    $svc | Start-Service
    $svc.WaitForStatus('Running', '00:00:30')
    Write-Ok "running."
}

# 6. Health check
if (-not $SkipHealthCheck -and -not $CopyOnly -and $serviceWasInstalled) {
    $healthUrl = "http://${RemoteServer}:${HealthPort}/health"
    Write-Step "Health check: $healthUrl"
    Start-Sleep -Seconds 2
    try {
        $resp = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
        if ($resp.StatusCode -eq 200) {
            Write-Ok "OK: $($resp.Content)"
        } else {
            Write-Warn "Unexpected status: $($resp.StatusCode)"
        }
    } catch {
        Write-Warn "Health check failed: $($_.Exception.Message)"
        Write-Warn "Check the service log on the server: <install>\Logs\Weighbridge.log"
    }
}

Write-Host ""
Write-Host "Deploy complete." -ForegroundColor Cyan
