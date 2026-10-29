<#
  Install go2rtc (RTSP -> WebRTC/MSE gateway for the Gate/Barrier cameras) as an
  always-on Windows scheduled task that starts at boot.

  RUN AS ADMINISTRATOR, on a host that can reach the cameras (the prod box "evergreen").
  Clients then reach it over Tailscale at http://evergreen:1984 (= GateBarrier:VideoBaseUrl).

  What it does:
    1. Downloads go2rtc.exe (if missing) into C:\Globeweigh\go2rtc.
    2. Copies go2rtc.yaml (the camera config) next to it.
    3. Registers a scheduled task "Globeweigh-go2rtc" that runs it at startup (plus a
       5-minute self-heal re-trigger, no run-time limit), and starts it now.

  NOTE: this downloads the go2rtc binary from its official GitHub release.
#>

param(
    [string]$Dir       = 'C:\Globeweigh\go2rtc',
    [string]$ConfigSrc = (Join-Path $PSScriptRoot 'go2rtc.yaml'),
    [string]$TaskName  = 'Globeweigh-go2rtc'
)

$ErrorActionPreference = 'Stop'

$wi = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $wi).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    throw "This script must be run as Administrator."
}

New-Item -ItemType Directory -Force $Dir | Out-Null
$exe = Join-Path $Dir 'go2rtc.exe'

# 1. Download go2rtc if not present
if (-not (Test-Path $exe)) {
    # GitHub requires TLS 1.2; older Windows (Server 2012 R2 / PS 4) defaults to TLS 1.0.
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $url = 'https://github.com/AlexxIT/go2rtc/releases/latest/download/go2rtc_win64.zip'
    $zip = Join-Path $Dir 'go2rtc_win64.zip'
    Write-Host "Downloading go2rtc..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    # Expand-Archive needs PS 5+; use .NET so it works on PS 4 (Server 2012 R2) too.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $Dir)
    Remove-Item $zip -Force
}
if (-not (Test-Path $exe)) { throw "go2rtc.exe not found after download." }

# 2. Copy the camera config (skipped when running in-place from $Dir — the live
#    config IS the source, and Copy-Item refuses to overwrite a file with itself)
$cfg = Join-Path $Dir 'go2rtc.yaml'
if ((Resolve-Path $ConfigSrc).Path -ne $cfg) { Copy-Item $ConfigSrc $cfg -Force }
Write-Host "Config: $cfg" -ForegroundColor Green

# 2b. Firewall: allow inbound so remote clients (e.g. dev boxes over Tailscale) can reach it.
#     TCP 1984 = web UI + MSE/signalling (WebSocket); TCP+UDP 8555 = WebRTC candidates
#     (UDP is the low-latency path; without it the player falls back to MSE over 1984).
$fwRules = @(
    @('Globeweigh-go2rtc-1984-tcp', 'TCP', 1984),
    @('Globeweigh-go2rtc-8555-tcp', 'TCP', 8555),
    @('Globeweigh-go2rtc-8555-udp', 'UDP', 8555)
)
foreach ($fw in $fwRules) {
    if (-not (Get-NetFirewallRule -DisplayName $fw[0] -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $fw[0] -Direction Inbound -Action Allow `
            -Protocol $fw[1] -LocalPort $fw[2] -Profile Any | Out-Null
    }
}
Write-Host "Firewall rules added (TCP 1984, TCP+UDP 8555)." -ForegroundColor Green

# 3. Register + start the scheduled task (runs at boot as SYSTEM)
$action  = New-ScheduledTaskAction -Execute $exe -Argument "-config `"$cfg`"" -WorkingDirectory $Dir
# Two triggers: at boot, plus a 5-minute repeating self-heal — with MultipleInstances
# IgnoreNew it is a no-op while go2rtc is already running, and restarts it within 5
# minutes if it ever isn't (missed boot trigger, crash the RestartCount didn't catch).
# (New-TimeSpan -Days 9999 rather than [TimeSpan]::MaxValue: MaxValue errors on 2012 R2.)
$triggers = @(
    (New-ScheduledTaskTrigger -AtStartup),
    (New-ScheduledTaskTrigger -Once -At ([datetime]::Today) `
        -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 9999))
)
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
# ExecutionTimeLimit 0 = unlimited. Without it the cmdlet defaults to a 3-DAY limit and
# Task Scheduler silently kills go2rtc 72h after boot (a stop, not a failure, so the
# RestartCount/RestartInterval retry never fires).
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Seconds 0)

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Principal $principal -Settings $settings | Out-Null
Start-ScheduledTask -TaskName $TaskName

Start-Sleep -Seconds 3
Write-Host "`ngo2rtc running. Check:" -ForegroundColor Cyan
Write-Host "  http://localhost:1984/         (go2rtc dashboard - confirm streams are green)"
Write-Host "  http://localhost:1984/stream.html?src=gate   (test a camera)"
Write-Host "If a camera is blank, it's likely H.265 - see the fallback notes in go2rtc.yaml." -ForegroundColor Yellow
