<#
  One-time IIS setup for Globeweigh.Api on a local dev/host machine.
  RUN AS ADMINISTRATOR (right-click > Run with PowerShell as admin, or from an elevated shell).

  Result: the API + web UI run permanently under IIS at http://localhost:5099 — no Visual
  Studio / dotnet run needed. ApiBaseUrl stays http://localhost:5099, so no code changes.

  What it does:
    1. Ensures the ASP.NET Core 8 Hosting Bundle (IIS module) is installed — downloads & installs it if missing.
    2. Creates app pool "EvergreenApi" (No Managed Code) and an IIS site bound to port 5099.
    3. Grants the app-pool identity read access to the published folder + write to its logs.
    4. Grants the app-pool identity SQL access to GbEvergreen (integrated security).
#>

param(
    [string]$SitePath = 'C:\Globeweigh\EvergreenApi',
    [int]$Port = 5099,
    [string]$AppPool = 'EvergreenApi',
    [string]$SiteName = 'EvergreenApi',
    [string]$SqlInstance = '(local)\SQLEXPRESS',
    [string]$Database = 'GbEvergreen',
    [string]$ReleasesPath = 'C:\Globeweigh\Releases',
    [string]$FriendlyHost = 'evergreen-sql01'
)

$ErrorActionPreference = 'Stop'

# --- must be admin ---
$wi = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $wi).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    throw "This script must be run as Administrator."
}

if (-not (Test-Path "$SitePath\Globeweigh.Api.dll")) {
    throw "Published API not found at $SitePath. Publish first (see redeploy.ps1) then re-run."
}

# --- 1. ASP.NET Core Hosting Bundle (ANCM) ---
if (-not (Test-Path "$env:windir\system32\inetsrv\aspnetcorev2.dll")) {
    # Prefer a bundle staged next to this script — Server 2012's PowerShell often can't
    # negotiate TLS 1.2 to aka.ms, so the installer is shipped alongside this script.
    $local = Join-Path $PSScriptRoot 'dotnet-hosting-win.exe'
    $installer = if (Test-Path $local) { $local } else { Join-Path $env:TEMP 'dotnet-hosting-8-win.exe' }
    if (-not (Test-Path $installer)) {
        Write-Host "ASP.NET Core Hosting Bundle not found - downloading..." -ForegroundColor Yellow
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri 'https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe' -OutFile $installer
    }
    Write-Host "Installing ASP.NET Core Hosting Bundle from $installer ..." -ForegroundColor Yellow
    Start-Process -FilePath $installer -ArgumentList '/install','/quiet','/norestart' -Wait
    Write-Host "Hosting Bundle installed. Restarting IIS..." -ForegroundColor Green
    net stop was /y | Out-Null
    net start w3svc | Out-Null
} else {
    Write-Host "ASP.NET Core Hosting Bundle already present." -ForegroundColor Green
}

Import-Module WebAdministration

# --- 2. App pool (No Managed Code) ---
if (Test-Path "IIS:\AppPools\$AppPool") { Remove-WebAppPool -Name $AppPool }
New-WebAppPool -Name $AppPool | Out-Null
Set-ItemProperty "IIS:\AppPools\$AppPool" -Name managedRuntimeVersion -Value ''        # 'No Managed Code'
Set-ItemProperty "IIS:\AppPools\$AppPool" -Name startMode -Value 'AlwaysRunning'         # warm on boot
Set-ItemProperty "IIS:\AppPools\$AppPool" -Name autoStart -Value $true

# --- 3. Site bound to the port (free it from any kestrel first) ---
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like '*Globeweigh.Api*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $AppPool -Port $Port -Force | Out-Null
Set-ItemProperty "IIS:\Sites\$SiteName" -Name serverAutoStart -Value $true

# Open the Windows Firewall for the port — IIS binds all interfaces, but without an
# inbound rule only localhost works (loopback bypasses the firewall); LAN clients on
# 192.168.250.x get their SYN dropped. This is the usual "localhost works, clients can't connect".
$ruleName = "Globeweigh API ($Port)"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Any | Out-Null
    Write-Host "Opened firewall for inbound TCP $Port." -ForegroundColor Green
} else {
    Write-Host "Firewall rule '$ruleName' already present." -ForegroundColor Green
}

# Friendly no-port URL: a host-header binding on :80 so staff can use
# http://$FriendlyHost/download/ instead of :$Port. Host header lets it share :80 with
# any Default Web Site. The :$Port binding stays (the installed app uses it).
if ($FriendlyHost) {
    if (-not (Get-WebBinding -Name $SiteName -Port 80 -Protocol http -HostHeader $FriendlyHost -ErrorAction SilentlyContinue)) {
        New-WebBinding -Name $SiteName -Protocol http -Port 80 -HostHeader $FriendlyHost
        Write-Host "Added :80 binding for host '$FriendlyHost'." -ForegroundColor Green
    }
    if (-not (Get-NetFirewallRule -DisplayName "Globeweigh API (80)" -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName "Globeweigh API (80)" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow -Profile Any | Out-Null
        Write-Host "Opened firewall for inbound TCP 80." -ForegroundColor Green
    }
}

# --- 4. Filesystem permissions for the app-pool virtual account ---
$poolId = "IIS AppPool\$AppPool"
icacls $SitePath /grant "${poolId}:(OI)(CI)RX" /T | Out-Null
$logs = Join-Path $SitePath 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
icacls $logs /grant "${poolId}:(OI)(CI)M" /T | Out-Null

# Read access to the installer Releases folder — the staff download page + updater
# (api/admin-app) stream installers from here.
if (Test-Path $ReleasesPath) {
    icacls $ReleasesPath /grant "${poolId}:(OI)(CI)RX" /T | Out-Null
    Write-Host "Granted $poolId read on $ReleasesPath." -ForegroundColor Green
}

# --- 5. SQL access for the app-pool identity (integrated security) ---
$sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$poolId')
    CREATE LOGIN [$poolId] FROM WINDOWS;
USE [$Database];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$poolId')
    CREATE USER [$poolId] FOR LOGIN [$poolId];
ALTER ROLE db_datareader ADD MEMBER [$poolId];
ALTER ROLE db_datawriter ADD MEMBER [$poolId];
"@
try {
    $cs = "Server=$SqlInstance;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
    $cn = New-Object System.Data.SqlClient.SqlConnection $cs
    $cn.Open()
    $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; $cmd.ExecuteNonQuery() | Out-Null
    $cn.Close()
    Write-Host "Granted SQL access to [$poolId] on $Database." -ForegroundColor Green
} catch {
    Write-Host "WARNING: could not grant SQL access automatically: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "Run the GRANT statements above manually in SSMS as a sysadmin, or set a SQL login in appsettings.json." -ForegroundColor Yellow
}

Start-Website -Name $SiteName
Write-Host "`nDone. Verify: http://localhost:$Port/gate-web/  and  http://localhost:$Port/swagger" -ForegroundColor Cyan
