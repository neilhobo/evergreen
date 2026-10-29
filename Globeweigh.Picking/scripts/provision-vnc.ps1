# One-time remote-support (VNC) provisioning for a Picking tablet.
#
# Installs droidVNC-NG and configures it fully headless: fixed port + password,
# auto-start on boot, no screen-capture consent dialogs, remote input enabled.
# After this, TauriAdmin -> Devices -> select device -> Remote View just works
# (the API bridges browser-WebSocket -> tablet:5900; see DeviceVncProxy).
#
# Needs adb access to the tablet ONCE — either:
#   USB:      plug it in, accept the debugging prompt        -> provision-vnc.ps1
#   Wireless: Settings > Developer options > Wireless debugging > Pair device with code,
#             then: provision-vnc.ps1 -DeviceHost 192.168.x.x -PairPort NNNNN -PairCode 123456
#             (connect port is auto-discovered from the pairing; pass -ConnectPort if known)
#
# The accessKey/password here MUST match VncCompanion.kt in the app (the app nudges the
# server started on each launch using the same key).
#
# Safe to re-run: it resets droidVNC-NG's data first so the preseeded defaults apply.

param(
    [string]$DeviceHost,        # wireless: tablet IP; omit for USB
    [int]$PairPort,             # wireless pairing port (from the pair dialog)
    [string]$PairCode,          # wireless 6-digit pairing code
    [int]$ConnectPort,          # wireless debugging connect port (shown on the main page)
    [string]$Apk                # optional local path to droidvnc-ng .apk
)

$ErrorActionPreference = "Stop"

# ---- shared secrets (keep in sync with VncCompanion.kt) -------------------
$VNC_PORT = 5900
$VNC_PASSWORD = "2wb7wkrb"
$ACCESS_KEY = "ec275e75ab76cb8708f4993aa4c57b47"
$PKG = "net.christianbeier.droidvnc_ng"

$adb = "adb"
$repoRoot = Split-Path -Parent $PSScriptRoot

# ---- locate the APK -------------------------------------------------------
if (-not $Apk) {
    $candidates = @(
        (Join-Path (Split-Path -Parent $repoRoot) "Globeweigh.Api\_companions"),
        "\\evergreen\Globeweigh\EvergreenApi\_companions"
    )
    foreach ($dir in $candidates) {
        if (Test-Path $dir) {
            $found = Get-ChildItem $dir -Filter "droidvnc*.apk" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($found) { $Apk = $found.FullName; break }
        }
    }
}
if (-not $Apk -or -not (Test-Path $Apk)) {
    throw "droidVNC-NG apk not found — pass -Apk or stage it in Globeweigh.Api\_companions."
}

# ---- connect (wireless) ---------------------------------------------------
$serialArgs = @()
if ($DeviceHost) {
    if ($PairPort -and $PairCode) {
        & $adb pair "${DeviceHost}:${PairPort}" $PairCode
        if ($LASTEXITCODE -ne 0) { throw "adb pair failed" }
    }
    if ($ConnectPort) {
        & $adb connect "${DeviceHost}:${ConnectPort}"
        if ($LASTEXITCODE -ne 0) { throw "adb connect failed" }
        $serialArgs = @("-s", "${DeviceHost}:${ConnectPort}")
    }
}
& $adb @serialArgs wait-for-device

# ---- install + reset ------------------------------------------------------
Write-Host "==> installing $([IO.Path]::GetFileName($Apk))"
& $adb @serialArgs install -r -g $Apk
if ($LASTEXITCODE -ne 0) { throw "adb install failed" }

# Reset app data so the preseeded defaults.json is authoritative (defaults are
# ignored once the app has generated its own prefs).
& $adb @serialArgs shell pm clear $PKG | Out-Null

# ---- preseed defaults.json (fixed port/password/accessKey, start on boot) --
$defaults = @"
{
    "port": $VNC_PORT,
    "password": "$VNC_PASSWORD",
    "accessKey": "$ACCESS_KEY",
    "startOnBoot": true,
    "startOnBootDelay": 0,
    "viewOnly": false
}
"@
$tmp = Join-Path $env:TEMP "droidvnc-defaults.json"
Set-Content -Path $tmp -Value $defaults -Encoding ascii
& $adb @serialArgs shell mkdir -p "/sdcard/Android/data/$PKG/files"
& $adb @serialArgs push $tmp "/sdcard/Android/data/$PKG/files/defaults.json"
if ($LASTEXITCODE -ne 0) { throw "adb push defaults.json failed" }

# ---- permissions: headless capture + remote input -------------------------
# Skip the MediaProjection consent dialog forever:
& $adb @serialArgs shell cmd appops set $PKG PROJECT_MEDIA allow
# Enable droidVNC-NG's accessibility (input) service alongside whatever is enabled:
$existing = (& $adb @serialArgs shell settings get secure enabled_accessibility_services).Trim()
$svc = "$PKG/.InputService"
if ($existing -and $existing -ne "null" -and $existing -notlike "*$svc*") {
    & $adb @serialArgs shell settings put secure enabled_accessibility_services "${existing}:${svc}"
} elseif (-not $existing -or $existing -eq "null") {
    & $adb @serialArgs shell settings put secure enabled_accessibility_services $svc
}
& $adb @serialArgs shell settings put secure accessibility_enabled 1

# ---- start it now (headless thanks to the appop) --------------------------
& $adb @serialArgs shell am start-foreground-service `
    -n "$PKG/.MainService" `
    -a "$PKG.ACTION_START" `
    --es "$PKG.EXTRA_ACCESS_KEY" $ACCESS_KEY `
    --ei "$PKG.EXTRA_PORT" $VNC_PORT `
    --es "$PKG.EXTRA_PASSWORD" $VNC_PASSWORD

Write-Host ""
Write-Host "================ DONE ================" -ForegroundColor Green
Write-Host "VNC server: port $VNC_PORT, password $VNC_PASSWORD, auto-starts on boot."
Write-Host "Test: TauriAdmin -> Devices -> select the device -> Remote View."
Write-Host "(The Picking app also re-nudges the server on every launch.)"
