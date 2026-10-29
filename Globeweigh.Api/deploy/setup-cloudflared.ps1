<#
  ONE-TIME setup for reaching the Evergreen admin UI from OUTSIDE the LAN via a Cloudflare
  Tunnel. RUN THIS ON THE EVERGREEN SERVER (EVERGREEN-SQL01), in an elevated PowerShell.

  Installs cloudflared and registers it as a Windows service holding an OUTBOUND-only
  connection to Cloudflare. Cloudflare then forwards https://<hostname> to the IIS site on
  http://localhost:5099. Nothing inbound is opened on the firewall and no port is forwarded;
  users browse to https://<hostname>/admin/, pass the Cloudflare Access sign-in (one-time PIN
  emailed to an allowed address), then see the app's own login. LAN users, the touch kiosks
  and the picking devices keep using http://192.168.250.5:5099 unchanged.

  Verified 2026-09-16: cloudflared 2026.9.1 runs on this box despite it being Server 2012 R2
  (6.3.9600), and outbound TCP 443 + 7844 to the Cloudflare edge are open. If a future release
  stops running on 2012 R2 (Go dropped support for it in 1.21), pin the last known-good build
  with -Version 2023.10.0.

  Before running, in the Cloudflare Zero Trust dashboard (https://one.dash.cloudflare.com):
    1. Networks > Tunnels > Create a tunnel > Cloudflared. Name it "evergreen".
    2. On the "Install and run a connector" step choose Windows x86_64 and copy the token out
       of the "cloudflared.exe service install <TOKEN>" command. Pass it here as -Token.
    3. Public hostname tab: subdomain e.g. "evergreen", your domain, Service type HTTP,
       URL localhost:5099.
    4. Access > Applications > Add an application > Self-hosted: that same hostname,
       session duration 1 month, a policy allowing the permitted email addresses,
       login method One-time PIN.

  Step 4 is NOT optional here. Several Evergreen API controllers are [AllowAnonymous] by
  design (picking, touch, device enrolment, app install, labels, production) because they
  serve LAN kiosks and scanners that never log in. The tunnel publishes the whole origin, so
  Cloudflare Access is the only thing standing in front of those endpoints.

  Idempotent: re-running (e.g. with a new token) re-registers the service.

    .\setup-cloudflared.ps1 -Token <token from the dashboard>
#>
param(
    [Parameter(Mandatory = $true)][string]$Token,
    [int]$Port = 5099,
    # "latest", or a release tag such as 2023.10.0 to pin an older build.
    [string]$Version = 'latest',
    # Skip the JWT signing-key safety check. Do not use unless you have rotated the key.
    [switch]$SkipKeyCheck
)
$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Run this in an elevated (Administrator) PowerShell." }

# --- Safety gate: the JWT signing key must not still be the repo's dev placeholder ---
# The key is in appsettings.json, which the deploy excludes (/XF) so it survives releases.
# Shipping the repo default means anyone holding the source can mint a valid admin token;
# on the LAN that needs a foothold, on a public hostname it is a remote auth bypass.
$settings = 'C:\Globeweigh\EvergreenApi\appsettings.json'
if (-not $SkipKeyCheck) {
    if (-not (Test-Path $settings)) { throw "appsettings.json not found at $settings - is this the Evergreen server?" }
    $cfg = Get-Content $settings -Raw | ConvertFrom-Json
    $key = $cfg.Jwt.Key
    if (-not $key -or $key -like 'DEV-ONLY*' -or $key.Length -lt 32) {
        throw @"
Jwt:Key in $settings is still the development placeholder.
Rotate it BEFORE exposing this server to the internet - see the header of this script and
the rotate snippet in the walkthrough. Everyone is signed out once when it changes.
(Re-run with -SkipKeyCheck only if you have deliberately handled this.)
"@
    }
    Write-Host "JWT signing key looks rotated ($($key.Length) chars)." -ForegroundColor Green
}

# 2012 R2 negotiates TLS 1.0 by default, which github.com now refuses - force TLS 1.2 or the
# download fails with "The request was aborted: Could not create SSL/TLS secure channel".
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# The MSI installs to Program Files (x86)\cloudflared even on 64-bit; check both to be safe.
function Find-Cloudflared {
    $candidates = @()
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'cloudflared\cloudflared.exe' }
    $candidates += Join-Path $env:ProgramFiles 'cloudflared\cloudflared.exe'
    $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

# --- Install cloudflared (silent MSI) if it is not already present ---
$exe = Find-Cloudflared
if (-not $exe) {
    $url = if ($Version -eq 'latest') {
        'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.msi'
    } else {
        "https://github.com/cloudflare/cloudflared/releases/download/$Version/cloudflared-windows-amd64.msi"
    }
    $msi = Join-Path $env:TEMP 'cloudflared-windows-amd64.msi'
    Write-Host "Downloading cloudflared from $url ..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $url -OutFile $msi -UseBasicParsing
    Write-Host "Installing cloudflared..." -ForegroundColor Cyan
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart" -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "msiexec failed with exit code $($p.ExitCode)." }
    $exe = Find-Cloudflared
    if (-not $exe) { throw "cloudflared.exe not found after the MSI install." }
}
Write-Host "cloudflared: $exe" -ForegroundColor Green
& $exe --version
if ($LASTEXITCODE -ne 0) {
    throw "cloudflared.exe will not run on this OS. Re-run with -Version 2023.10.0 (the last build that supports Server 2012 R2)."
}

# --- (Re)register the Windows service with the tunnel token ---
$svc = Get-Service -Name 'cloudflared*' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($svc) {
    Write-Host "Removing the existing service $($svc.Name) so it can be re-registered..." -ForegroundColor Cyan
    if ($svc.Status -ne 'Stopped') { Stop-Service $svc.Name -Force }
    & $exe service uninstall
}
Write-Host "Registering the cloudflared service with the tunnel token..." -ForegroundColor Cyan
& $exe service install $Token
if ($LASTEXITCODE -ne 0) { throw "cloudflared service install failed (exit $LASTEXITCODE)." }

$svc = Get-Service -Name 'cloudflared*' | Select-Object -First 1
if (-not $svc) { throw "No cloudflared service found after install." }
Set-Service $svc.Name -StartupType Automatic
if ($svc.Status -ne 'Running') { Start-Service $svc.Name }
Start-Sleep -Seconds 5
$svc = Get-Service -Name $svc.Name
Write-Host "Service $($svc.Name): $($svc.Status)" -ForegroundColor Green

# --- Sanity: the tunnel's origin is the IIS site on localhost:$Port ---
# Check the UI roots the same way the deploy script does: a non-200 or any redirect on a UI
# root is a real outage, not "just the SPA auth redirect" (see deploy-to-evergreen.ps1).
foreach ($u in @("/admin/", "/admin/version.json")) {
    try {
        $r = Invoke-WebRequest "http://localhost:$Port$u" -UseBasicParsing -TimeoutSec 20
        Write-Host "Local check OK - $u returned $($r.StatusCode)" -ForegroundColor Green
    } catch {
        Write-Warning "http://localhost:$Port$u did not answer: $($_.Exception.Message). The tunnel will return 502 until IIS is up."
    }
}

Write-Host ""
Write-Host "Done. The tunnel should now show HEALTHY in the Zero Trust dashboard." -ForegroundColor Cyan
Write-Host "Browse to https://<hostname>/admin/ from off-site to test." -ForegroundColor Cyan
Write-Host "Connector log if it does not: Get-EventLog -LogName Application -Source cloudflared -Newest 40" -ForegroundColor DarkGray
