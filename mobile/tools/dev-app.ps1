# Runs the app on an Android emulator through flutter_dev.dart, which hot reloads by itself
# whenever a .dart file under lib/ changes (an edit from the editor or from Claude), and
# takes reload / restart requests at http://127.0.0.1:8991. Ported from SpecGauge
# (specgauge-mobile-new/tools/dev-app.ps1).
#
#   npm run mobile           # against the live API (evergreen, via the tunnel)
#   npm run mobile:local     # against the API on this PC (:5099, through adb reverse)
#
# Start an emulator first (Android Studio's Device Manager); with several attached it
# takes the first, or pass -Device <emulator-id or AVD name>.

param(
    [string]$Device = "",
    [string]$BaseUrl = "http://localhost:5099",
    [int]$Port = 8991
)

$ErrorActionPreference = "Continue"

$adb      = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$emulator = "$env:LOCALAPPDATA\Android\Sdk\emulator\emulator.exe"

# A stopped session can leave its dart child holding the control port; free it by the
# actual TCP listener, never by process name.
function Stop-OrphanOnPort($port) {
    $listeners = @(netstat -ano | Select-String -Pattern "TCP\s+127\.0\.0\.1:$port\s+0\.0\.0\.0:0\s+LISTENING\s+(\d+)" | ForEach-Object { [int]$_.Matches[0].Groups[1].Value })
    foreach ($holderPid in $listeners) {
        $proc = Get-Process -Id $holderPid -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "==> killing orphan $($proc.ProcessName) (PID $holderPid) holding port $port" -ForegroundColor DarkYellow
            Stop-Process -Id $holderPid -Force -ErrorAction SilentlyContinue
        }
    }
    if ($listeners.Count -gt 0) { Start-Sleep -Seconds 2 }
}

function Get-EmulatorIdForAvd($avdName) {
    foreach ($line in (& $adb devices 2>$null)) {
        if ($line -match '^(emulator-\d+)\s+device\s*$') {
            $id = $Matches[1]
            $name = (& $adb -s $id emu avd name 2>$null | Select-Object -First 1)
            if ($name -and $name.Trim() -eq $avdName) { return $id }
        }
    }
    return $null
}

function Get-AttachedEmulators {
    @(& $adb devices 2>$null | ForEach-Object {
        if ($_ -match '^(emulator-\d+)\s+device\s*$') { $Matches[1] }
    })
}

function Resolve-Device($requested) {
    # A cold adb server answers before a running emulator has re-registered.
    & $adb start-server 2>$null | Out-Null
    $emulators = @(Get-AttachedEmulators)
    if ($emulators.Count -eq 0) {
        Write-Host "==> waiting for an attached emulator ..." -ForegroundColor DarkGray
        $deadline = (Get-Date).AddSeconds(30)
        while ($emulators.Count -eq 0 -and (Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 1
            $emulators = @(Get-AttachedEmulators)
        }
    }

    if ([string]::IsNullOrWhiteSpace($requested)) {
        if ($emulators.Count -eq 0) {
            throw "No emulator attached. Start one in Android Studio (or pass -Device <avd-name>) and retry."
        }
        Write-Host "==> using attached emulator $($emulators[0])" -ForegroundColor DarkGray
        return $emulators[0]
    }

    foreach ($line in (& $adb devices 2>$null)) {
        if ($line -match "^$([regex]::Escape($requested))\s+device\s*$") { return $requested }
    }

    # Otherwise an AVD name: reuse it if running, else launch it and wait for boot.
    $avds = (& $emulator -list-avds 2>$null) | ForEach-Object { $_.Trim() }
    if ($avds -notcontains $requested) {
        throw "'$requested' is neither an attached device nor a known AVD ($($avds -join ', '))"
    }
    $id = Get-EmulatorIdForAvd $requested
    if ($id) { return $id }

    Write-Host "==> launching emulator $requested ..." -ForegroundColor Cyan
    Start-Process -FilePath $emulator -ArgumentList @('-avd', $requested) -WindowStyle Minimized | Out-Null
    $deadline = (Get-Date).AddMinutes(3)
    while (-not $id -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $id = Get-EmulatorIdForAvd $requested
    }
    if (-not $id) { throw "Emulator $requested did not appear within 3 minutes" }
    & $adb -s $id wait-for-device | Out-Null
    $deadline = (Get-Date).AddMinutes(3)
    while ((Get-Date) -lt $deadline) {
        if (((& $adb -s $id shell getprop sys.boot_completed 2>$null) -join '').Trim() -eq '1') { break }
        Start-Sleep -Seconds 2
    }
    return $id
}

Stop-OrphanOnPort $Port

$resolved = Resolve-Device $Device
& $adb -s $resolved shell am force-stop com.specdigital.evergreen | Out-Null
# Orphaned VM-service forwards from killed sessions can wedge `flutter run --machine`.
& $adb -s $resolved forward --remove-all | Out-Null
& $adb -s $resolved reverse --remove-all | Out-Null
# The emulator's localhost:5099 is this PC's, so the local API needs no 10.0.2.2.
& $adb -s $resolved reverse tcp:5099 tcp:5099 | Out-Null

Push-Location "$PSScriptRoot\.."
try {
    dart run tools/flutter_dev.dart --device $resolved --port $Port -- --dart-define=EVERGREEN_API_BASE_URL=$BaseUrl
} finally {
    Pop-Location
    # Windows otherwise leaves the dart child bound to $Port.
    Stop-OrphanOnPort $Port
}
