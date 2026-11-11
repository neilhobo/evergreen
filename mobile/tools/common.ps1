# Shared by the release and patch scripts (dot-sourced). Same pipeline as Specbooks
# (specbooks/mobile/tools, itself from SpecGauge): Shorebird builds the release so it can be patched later,
# fastlane uploads it, and iOS builds on the Mac over SSH.

$ErrorActionPreference = "Stop"
$MobileRoot = Split-Path -Parent $PSScriptRoot
$Pubspec = Join-Path $MobileRoot "pubspec.yaml"
$Shorebird = "$env:USERPROFILE\.shorebird\bin\shorebird.ps1"

# The Mac that builds iOS, and where the project is copied on it.
$Mac = "neilhobson@192.168.1.194"
$MacRoot = "~/Projects/evergreen-mobile"

function Get-AppVersion {
    $line = (Get-Content $Pubspec) | Where-Object { $_ -match '^version:\s*(\d+\.\d+\.\d+)\+(\d+)\s*$' } | Select-Object -First 1
    if (-not $line) { throw "No version line in $Pubspec" }
    [void]($line -match '^version:\s*(\d+\.\d+\.\d+)\+(\d+)\s*$')
    [pscustomobject]@{ Semver = $Matches[1]; Build = [int]$Matches[2]; Full = "$($Matches[1])+$($Matches[2])" }
}

# The store-version manifest on evergreen that GET /api/mobile/version serves (see
# Globeweigh.Api/Controllers/MobileAppController.cs). Read per request: no API deploy needed.
$VersionManifest = "\\evergreen\Globeweigh\EvergreenApi\mobile-version.json"

# Tell installed apps a new store build exists: they show "Update available" with an
# Update button. "minimum" (force-update) is kept as is: it's set by hand on the server.
# $DelayMinutes holds the prompt back while the store processes the upload.
function Publish-StoreVersion([string]$Platform, [string]$Version, [string]$Url, [int]$DelayMinutes = 15) {
    Write-Host "==> telling installed apps about $Version ($Platform) ..." -ForegroundColor Cyan
    try {
        $manifest = if (Test-Path $VersionManifest) { Get-Content $VersionManifest -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
        $old = $manifest.$Platform
        $entry = [ordered]@{
            latest           = $Version
            previous         = if ($old -and $old.latest -ne $Version) { $old.latest } elseif ($old) { $old.previous } else { "" }
            availableFromUtc = (Get-Date).ToUniversalTime().AddMinutes($DelayMinutes).ToString("yyyy-MM-ddTHH:mm:ssZ")
            minimum          = if ($old -and $old.minimum) { $old.minimum } else { "" }
            url              = $Url
        }
        $manifest | Add-Member -NotePropertyName $Platform -NotePropertyValue ([pscustomobject]$entry) -Force
        [IO.File]::WriteAllText($VersionManifest, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
        Write-Host "    $Platform latest -> $Version (prompt from $($entry.availableFromUtc))" -ForegroundColor Green
    } catch {
        # The build is uploaded either way; only the in-app prompt is missing.
        Write-Warning "Could not update $VersionManifest ($_). Installed apps won't be prompted until it's set."
    }
}

# Next build number (and a new semver if given), written back to pubspec.yaml.
function Step-AppVersion([string]$NewSemver) {
    $v = Get-AppVersion
    $semver = if ($NewSemver) { $NewSemver } else { $v.Semver }
    $next = "$semver+$($v.Build + 1)"
    $text = [IO.File]::ReadAllText($Pubspec) -replace '(?m)^version:[ \t]*\S+[ \t]*\r?$', "version: $next"
    [IO.File]::WriteAllText($Pubspec, $text, [Text.UTF8Encoding]::new($false))
    Write-Host "==> version: $($v.Full)  ->  $next" -ForegroundColor Cyan
    $next
}

# Ruby/fastlane on PATH, and the UTF-8 locale fastlane needs on Windows.
function Initialize-Fastlane {
    $rubyBin = "C:\Ruby34-x64\bin"
    if ((Test-Path $rubyBin) -and ($env:Path -notlike "*$rubyBin*")) { $env:Path = "$rubyBin;$env:Path" }
    if (-not $env:LANG) { $env:LANG = "en_US.UTF-8" }
    if (-not $env:LC_ALL) { $env:LC_ALL = "en_US.UTF-8" }
    if (-not (Get-Command fastlane -ErrorAction SilentlyContinue)) {
        throw "fastlane is not on PATH. Run: gem install fastlane (after installing Ruby via winget install RubyInstallerTeam.RubyWithDevKit.3.4)."
    }
}

function Assert-Shorebird {
    if (-not (Test-Path $Shorebird)) { throw "Shorebird CLI not found at $Shorebird. Install: https://docs.shorebird.dev" }
}

# The Mac login password from mobile/.env.mac (gitignored): unlocks the keychain over SSH so
# codesign can reach the Apple Distribution key.
function Get-MacPassword {
    $envMac = Join-Path $MobileRoot ".env.mac"
    if (-not (Test-Path $envMac)) {
        throw "Missing mobile/.env.mac. Copy .env.mac.example to .env.mac and set MAC_PASSWORD=<your Mac login password>."
    }
    foreach ($line in Get-Content $envMac) {
        if ($line -match '^\s*MAC_PASSWORD\s*=\s*(.+?)\s*$') { return $Matches[1].Trim('"').Trim("'") }
    }
    throw "MAC_PASSWORD not set in mobile/.env.mac."
}

# Copies mobile/ to the Mac (tar over SSH), leaving out build output and the files flutter
# generates with Windows paths in them (the Mac makes its own on pub get).
function Sync-ToMac {
    Write-Host "==> syncing mobile/ to $Mac ..." -ForegroundColor Cyan
    $excludes = @(
        './build', './.dart_tool', './ios/Pods', './ios/.symlinks',
        './ios/Flutter/Flutter.framework', './ios/Flutter/Generated.xcconfig',
        './ios/Flutter/flutter_export_environment.sh', './ios/Flutter/ephemeral',
        './android/.gradle', './android/build', './android/app/build', './android/key.properties',
        './.env.mac'
    ) | ForEach-Object { "--exclude=$_" }
    Push-Location $MobileRoot
    try {
        cmd /c "tar $($excludes -join ' ') -czf - . | ssh $Mac `"mkdir -p $MacRoot && tar -xzf - -C $MacRoot`""
        if ($LASTEXITCODE -ne 0) { throw "Sync to the Mac failed (exit $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
}

# Runs a bash script on the Mac with the keychain unlocked. The script goes through a temp
# file (LF line endings, no BOM) fed to `bash -s`, so the password never appears on the SSH
# command line and PowerShell can't turn the line endings into CRLF.
function Invoke-OnMac([string]$Script, [hashtable]$Vars = @{}) {
    $password = (Get-MacPassword) -replace "'", "'\''"
    $assignments = "MAC_PASSWORD='$password'`n"
    foreach ($k in $Vars.Keys) { $assignments += "$k='$($Vars[$k] -replace "'", "'\''")'`n" }
    $prelude = @"
eval "`$(/opt/homebrew/bin/brew shellenv)"
export LANG=en_US.UTF-8
export PATH="`$HOME/.shorebird/bin:`$PATH"
set -e
security unlock-keychain -p "`$MAC_PASSWORD" "`$HOME/Library/Keychains/login.keychain-db"
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "`$MAC_PASSWORD" "`$HOME/Library/Keychains/login.keychain-db" >/dev/null
cd $MacRoot
rm -f ios/Flutter/Generated.xcconfig ios/Flutter/flutter_export_environment.sh
# Shorebird's own Flutter (the one that builds the release), not the Mac's system
# one: it's newer, and packages such as pdfrx need what it pins (meta 1.18).
SB_FLUTTER="`$(ls -td "`$HOME"/.shorebird/bin/cache/flutter/*/bin/flutter | head -1)"
"`$SB_FLUTTER" pub get
(cd ios && pod install --repo-update)
# The App Store profile the Release build signs with, from the API key.
(cd ios && fastlane ios profiles)

"@
    $temp = [IO.Path]::GetTempFileName()
    try {
        $body = ($assignments + $prelude + $Script) -replace "`r`n", "`n"
        [IO.File]::WriteAllText($temp, $body, [Text.UTF8Encoding]::new($false))
        cmd /c "ssh $Mac `"bash -s`" < `"$temp`""
        if ($LASTEXITCODE -ne 0) { throw "The Mac build failed (exit $LASTEXITCODE)" }
    } finally {
        Remove-Item $temp -ErrorAction SilentlyContinue
    }
}
