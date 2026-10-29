# Runs Tauri dev/build inside an MSVC x64 build environment.
# rustc links against the full desktop CRT, so a VS install that carries only the onecore
# libs (all the managed/MAUI workloads pull in) fails with LNK1104 'cannot open msvcrt.lib'.
# Rather than pinning one VS version, pick the newest install that actually ships
# VC\Tools\MSVC\<ver>\lib\x64 — works with 2022 Build Tools, 2026, or both side by side.
param([ValidateSet('dev', 'build')][string]$Mode = 'dev')

$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
$vswhere = Join-Path $vsInstaller 'vswhere.exe'

$vcvars = $null
if (Test-Path $vswhere) {
    $installs = & $vswhere -products * -prerelease -format json | Out-String | ConvertFrom-Json
    foreach ($inst in ($installs | Sort-Object { [version]$_.installationVersion } -Descending)) {
        $bat = Join-Path $inst.installationPath 'VC\Auxiliary\Build\vcvars64.bat'
        if (-not (Test-Path $bat)) { continue }
        $desktopCrt = Get-ChildItem (Join-Path $inst.installationPath 'VC\Tools\MSVC') -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName 'lib\x64\msvcrt.lib') }
        if ($desktopCrt) { $vcvars = $bat; break }
    }
}
if (-not $vcvars) {
    throw @"
No MSVC x64 desktop build environment found (vcvars64.bat + VC\Tools\MSVC\<ver>\lib\x64).
Tauri needs the C++ desktop toolchain. Either add the 'Desktop development with C++'
workload to your existing Visual Studio via the VS Installer, or install the standalone
build tools:
  winget install --id Microsoft.VisualStudio.2022.BuildTools --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
"@
}

# vcvars64.bat shells out to vswhere.exe by bare name; ensure the VS Installer dir
# (where vswhere lives) is on PATH so the build-environment setup doesn't fail.
if (Test-Path $vswhere) { $env:PATH = "$vsInstaller;$env:PATH" }

# A shell (or VS Code) launched before rustup was installed inherits a PATH without
# %USERPROFILE%\.cargo\bin, and tauri then dies with 'cargo metadata ... program not found'.
# Resolve cargo ourselves so the build doesn't depend on when the terminal was opened.
$cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
$cargoBin = Join-Path $cargoHome 'bin'
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    if (Test-Path (Join-Path $cargoBin 'cargo.exe')) { $env:PATH = "$cargoBin;$env:PATH" }
    else { throw "cargo not found on PATH or at $cargoBin. Install Rust: winget install --id Rustlang.Rustup" }
}

Set-Location $PSScriptRoot
# Updater artifact signing. Required for release builds (bundle.createUpdaterArtifacts).
# Pass the key CONTENT (the _PATH variant is unreliable) and the password — Windows can't
# express an empty env var, so the key is generated with a real password.
$env:TAURI_SIGNING_PRIVATE_KEY = (Get-Content (Join-Path $PSScriptRoot '_signing\updater.key') -Raw).Trim()
$env:TAURI_SIGNING_PRIVATE_KEY_PASSWORD = 'GwUpdater2026'
cmd /s /c " `"$vcvars`" >nul && npm run tauri $Mode "
