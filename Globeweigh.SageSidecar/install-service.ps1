# Installs Globeweigh.SageSidecar as a Windows service on THIS machine - the one that has
# Sage 50 (or the Sage SDO) installed and can see the company's ACCDATA folder. Run once,
# elevated, from the folder the binaries were copied to.
#
#   .\install-service.ps1                       run as LocalSystem (Sage data on this box)
#   .\install-service.ps1 -Credential (Get-Credential DOMAIN\svc_sage)
#                                               run as a domain user (Sage data on a share:
#                                               LocalSystem cannot reach network paths)
#
# Before starting the service, fill in Globeweigh.SageSidecar.exe.config and prove it:
#   .\Globeweigh.SageSidecar.exe --ping
#   .\Globeweigh.SageSidecar.exe --test-sage --account <a Sage A/C ref> --stock <a Sage stock code>
#
# Removing:  sc stop GlobeweighSageSidecar; sc delete GlobeweighSageSidecar

[CmdletBinding()]
param(
    [string]$ServiceName = 'GlobeweighSageSidecar',
    [string]$DisplayName = 'Globeweigh Sage Sidecar',
    # Defaults to the folder this script sits in. Resolved in the body, not here: on the
    # server's older PowerShell $PSScriptRoot is empty inside a param() default.
    [string]$InstallDir,
    [pscredential]$Credential
)

$ErrorActionPreference = 'Stop'
if (-not $InstallDir) { $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Definition }

$exe = Join-Path $InstallDir 'Globeweigh.SageSidecar.exe'
if (-not (Test-Path $exe)) { throw "Not found: $exe - copy the Release build here first (deploy.ps1)." }

# The config must have been filled in; an empty key or data path means the service would
# start, log a configuration error and stop again.
[xml]$cfg = Get-Content (Join-Path $InstallDir 'Globeweigh.SageSidecar.exe.config')
$settings = @{}
foreach ($n in $cfg.configuration.appSettings.add) { $settings[$n.key] = $n.value }
$missing = @('SidecarKey', 'SageDataPath') | Where-Object { [string]::IsNullOrWhiteSpace($settings[$_]) }
if ($missing) { throw "Fill in $($missing -join ', ') in Globeweigh.SageSidecar.exe.config before installing." }

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service $ServiceName already exists ($($existing.Status)). Stopping it to update the binary path." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') { Stop-Service $ServiceName -Force; $existing.WaitForStatus('Stopped', '00:00:30') }
    & sc.exe config $ServiceName binPath= "`"$exe`"" | Out-Null
} else {
    $args = @('create', $ServiceName, 'binPath=', "`"$exe`"", 'start=', 'auto', 'DisplayName=', "`"$DisplayName`"")
    if ($Credential) {
        $args += @('obj=', $Credential.UserName, 'password=', $Credential.GetNetworkCredential().Password)
    }
    & sc.exe @args | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc create failed (exit $LASTEXITCODE)" }
}

& sc.exe description $ServiceName "Posts Evergreen sales orders into Sage 50 Accounts via SDO." | Out-Null
# Restart on failure: the worker exits with code 1 on a crash or a configuration problem.
& sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

if ($Credential) {
    Write-Host "Note: $($Credential.UserName) needs 'Log on as a service' and read/write on $($settings['SageDataPath'])." -ForegroundColor Yellow
}

Start-Service $ServiceName
(Get-Service $ServiceName).WaitForStatus('Running', '00:00:30')
Write-Host "$ServiceName is running. Log: $(Join-Path $InstallDir 'Logs')" -ForegroundColor Green
