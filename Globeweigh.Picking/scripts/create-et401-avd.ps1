<#
.SYNOPSIS
  (Re)creates the "ET401" Android emulator that mimics the 8" Zebra ET401 tablet.

.DESCRIPTION
  The dev/test emulator for Globeweigh.Picking. Run this after a PC rebuild or a fresh
  Android SDK install and `npm run dev:emu` works again.

  Mirrors the real hardware (zebra.com/us/en/products/spec-sheets/tablets/et401.html):
    display   8.0" WUXGA 1920x1200, ~283 real ppi -> 280 dpi bucket (686 dp wide)
    OS        Android 15 / API 35   (google_apis image: GMS but no Play Store, and
                                     root-able -- the closest match to a Zebra build)
    memory    6 GB RAM / 64 GB flash + microSD
    CPU       Qualcomm Dragonwing Q-6690, octa-core
    cameras   16 MP rear (flash, autofocus) + 8 MP front
  Boots landscape, because the app is android:screenOrientation="sensorLandscape".

  What it CANNOT mimic: the integrated barcode imager and DataWedge. Simulate a scan
  with the same broadcast the app listens for on the real device:
    adb shell am broadcast -a com.globeweigh.picking.SCANNER `
      --es com.symbol.datawedge.data_string "ProdCd:<code>Weight:<w>kgUniqueCode:<id>"

.PARAMETER Name
  AVD name. Defaults to ET401 -- the name scripts/dev-device.mjs boots for EMULATOR=1.

.PARAMETER Force
  Delete and recreate the AVD if it already exists.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/create-et401-avd.ps1 -Force
#>
[CmdletBinding()]
param(
    [string] $Name = 'ET401',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$Package  = 'system-images;android-35;google_apis;x86_64'
$DeviceId = 'zebra_et401_8'

# -- locate the SDK and a JDK (the SDK command-line tools are Java) -----------
$sdk = $env:ANDROID_HOME
if (-not $sdk) { $sdk = $env:ANDROID_SDK_ROOT }
if (-not $sdk) { $sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
if (-not (Test-Path $sdk)) { throw "Android SDK not found at '$sdk'. Set ANDROID_HOME." }

if (-not $env:JAVA_HOME) {
    $jbr = @(
        'C:\Program Files\Android\Android Studio\jbr',
        (Join-Path $env:LOCALAPPDATA 'Programs\Android Studio\jbr')
    ) | Where-Object { Test-Path (Join-Path $_ 'bin\java.exe') } | Select-Object -First 1
    if (-not $jbr) { throw 'No JDK found. Install Android Studio or set JAVA_HOME.' }
    $env:JAVA_HOME = $jbr
}

$androidCli = Join-Path $sdk 'cmdline-tools\latest\bin\android.exe'
$sdkmanager = Join-Path $sdk 'cmdline-tools\latest\bin\sdkmanager.bat'
$avdmanager = Join-Path $sdk 'cmdline-tools\latest\bin\avdmanager.bat'
$emulator   = Join-Path $sdk 'emulator\emulator.exe'
foreach ($t in @($avdmanager, $emulator)) {
    if (-not (Test-Path $t)) { throw "Missing SDK tool: $t (add it via Android Studio > SDK Manager)" }
}

Write-Host "[et401] SDK       $sdk"
Write-Host "[et401] JAVA_HOME $env:JAVA_HOME"

# -- 1. system image ---------------------------------------------------------
$imageDir = Join-Path $sdk 'system-images\android-35\google_apis\x86_64'
if (Test-Path (Join-Path $imageDir 'package.xml')) {
    Write-Host "[et401] system image already installed"
} else {
    Write-Host "[et401] downloading $Package (~1.6 GB, one time) ..."
    # `android sdk install` replaces the deprecated sdkmanager. The package id must be
    # ONE argument -- a bare cmd/bash shell splits it on the ';' separators.
    if (Test-Path $androidCli) { & $androidCli sdk install $Package } else { & $sdkmanager $Package }
    if (-not (Test-Path (Join-Path $imageDir 'package.xml'))) { throw 'System image install failed.' }
}

# -- 2. the AVD --------------------------------------------------------------
$avdDir = Join-Path $env:USERPROFILE ".android\avd\$Name.avd"
if ((Test-Path $avdDir) -and -not $Force) {
    throw "AVD '$Name' already exists. Re-run with -Force to recreate it."
}

# --device is passed only when the Zebra profile is registered in ~/.android/devices.xml.
# Every hardware value is written explicitly below anyway, so it is purely cosmetic: it is
# what makes Android Studio's Device Manager label the AVD "Zebra ET401 (8 in)".
$deviceArgs = @()
$devicesXml = Join-Path $env:USERPROFILE '.android\devices.xml'
if ((Test-Path $devicesXml) -and (Select-String -Path $devicesXml -Pattern $DeviceId -Quiet)) {
    $deviceArgs = @('--device', $DeviceId)
} else {
    Write-Host "[et401] no '$DeviceId' profile in devices.xml - creating a plain custom AVD"
}

Write-Host "[et401] creating AVD '$Name' ..."
& $avdmanager create avd --name $Name --package $Package @deviceArgs --force
if (-not (Test-Path $avdDir)) { throw "avdmanager did not create $avdDir" }

# -- 3. ET401 hardware profile ----------------------------------------------
$cfgPath = Join-Path $avdDir 'config.ini'
$cfg = [ordered]@{}
foreach ($line in Get-Content $cfgPath) {
    if ($line -match '^\s*([^#=]+?)\s*=\s*(.*)$') { $cfg[$Matches[1]] = $Matches[2] }
}

$et401 = [ordered]@{
    # display: 8.0" WUXGA. 1200 / (280/160) = 686 dp smallest width.
    'hw.lcd.width'              = '1920'
    'hw.lcd.height'             = '1200'
    'hw.lcd.density'            = '280'
    'hw.initialOrientation'     = 'landscape'   # the app is sensorLandscape
    'showDeviceFrame'           = 'no'          # no Zebra skin ships with the SDK
    # compute
    'hw.ramSize'                = '6144'        # 6 GB, as shipped
    'vm.heapSize'               = '512M'
    'hw.cpu.ncore'              = '8'           # the Q-6690 is octa-core
    'hw.gpu.enabled'            = 'yes'         # host GPU; without it Flutter crawls
    'hw.gpu.mode'               = 'auto'
    # storage
    'disk.dataPartition.size'   = '16G'
    'sdcard.size'               = '2G'          # the real tablet takes microSD
    # peripherals
    'hw.keyboard'               = 'yes'         # type with the host keyboard
    'hw.mainKeys'               = 'no'          # on-screen nav bar
    'hw.camera.back'            = 'virtualscene'
    'hw.camera.front'           = 'emulated'
    'hw.audioInput'             = 'yes'
    'hw.audioOutput'            = 'yes'
    # sensors the ET401 has (gyro, accel, light, magnetometer) -- and those it doesn't
    'hw.accelerometer'          = 'yes'
    'hw.gyroscope'              = 'yes'
    'hw.sensors.light'          = 'yes'
    'hw.sensors.magnetic_field' = 'yes'
    'hw.sensors.orientation'    = 'yes'
    'hw.sensors.proximity'      = 'no'
    'hw.sensors.humidity'       = 'no'
    'hw.sensors.temperature'    = 'no'
    'hw.sensors.pressure'       = 'no'
}
foreach ($k in $et401.Keys) { $cfg[$k] = $et401[$k] }

$out = foreach ($k in $cfg.Keys) { "$k=$($cfg[$k])" }
Set-Content -Path $cfgPath -Value ($out -join "`n") -Encoding ascii
Write-Host "[et401] wrote $cfgPath"

Write-Host ''
Write-Host "Done. AVD '$Name' created:" -ForegroundColor Green
Write-Host '  8" WUXGA 1920x1200 @ 280dpi (686dp) | Android 15 / API 35 | 6 GB RAM | 8 cores'
Write-Host ''
Write-Host "  Boot it:      `"$emulator`" -avd $Name"
Write-Host '  Run the app:  npm run dev:emu        (from Globeweigh.Picking)'
Write-Host '  Fake a scan:  adb shell am broadcast -a com.globeweigh.picking.SCANNER \'
Write-Host '                  --es com.symbol.datawedge.data_string "ProdCd:<code>Weight:<w>kgUniqueCode:<id>"'
