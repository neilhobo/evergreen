# Runs Tauri dev/build inside an MSVC x64 build environment.
# rustc links against the full desktop CRT, so a VS install that carries only the onecore
# libs (all the managed/MAUI workloads pull in) fails with LNK1104 'cannot open msvcrt.lib'.
# Rather than pinning one VS version, pick the newest install that actually ships
# VC\Tools\MSVC\<ver>\lib\x64 — works with 2022 Build Tools, 2026, or both side by side.
#
# -ExternalVite: dev only — skip tauri's beforeDevCommand because the caller (npm run dev:full)
# has already started Vite in parallel, so it is warming up while this script and cargo run.
param([ValidateSet('dev', 'build')][string]$Mode = 'dev', [switch]$ExternalVite)

# The Tauri/cargo/vite toolchain writes Info + warning lines to stderr. If a caller has
# $ErrorActionPreference='Stop' (e.g. dist-tauriadmin.ps1), PowerShell 5.1 turns the first
# such stderr line into a terminating NativeCommandError and aborts the build before it even
# compiles. Force Continue here; a real build failure is still caught via $LASTEXITCODE.
$ErrorActionPreference = 'Continue'

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

# vcvars64.bat costs ~3s on every start, and its output only changes when VS / the MSVC
# toolset does. Cache the variables it adds or changes (keyed on the .bat and the MSVC tools
# dir timestamps) and re-apply them here. PATH is cached as just the entries vcvars prepends,
# so changes to the rest of PATH still come through. Delete the cache file to force a rerun.
$vcCache = Join-Path $PSScriptRoot 'src-tauri\target\.vcvars-env.json'
$msvcDir = Join-Path (Split-Path $vcvars) '..\..\Tools\MSVC'
$vcKey = "$vcvars|$((Get-Item $vcvars).LastWriteTimeUtc.Ticks)|$((Get-Item $msvcDir).LastWriteTimeUtc.Ticks)"
$vcEnv = $null
if (Test-Path $vcCache) {
    try { $c = Get-Content $vcCache -Raw | ConvertFrom-Json; if ($c.key -eq $vcKey) { $vcEnv = $c } } catch { }
}
if (-not $vcEnv) {
    $before = @{}
    Get-ChildItem env: | ForEach-Object { $before[$_.Name] = $_.Value }
    $vars = @{}
    $pathPrefix = @()
    foreach ($line in (cmd /s /c " `"$vcvars`" >nul && set ")) {
        $i = $line.IndexOf('=')
        if ($i -le 0) { continue }
        $name = $line.Substring(0, $i); $value = $line.Substring($i + 1)
        if ($name -eq 'Path') {
            $old = $env:PATH -split ';'
            $pathPrefix = @($value -split ';' | Where-Object { $_ -and $old -notcontains $_ })
        } elseif ($before[$name] -ne $value) {
            $vars[$name] = $value
        }
    }
    if (-not $vars['INCLUDE'] -or -not $pathPrefix) { throw "vcvars64.bat produced no build environment: $vcvars" }
    $vcEnv = [pscustomobject]@{ key = $vcKey; pathPrefix = $pathPrefix; vars = [pscustomobject]$vars }
    New-Item -ItemType Directory -Force (Split-Path $vcCache) | Out-Null
    $vcEnv | ConvertTo-Json -Depth 3 | Set-Content $vcCache -Encoding UTF8
}
foreach ($p in $vcEnv.vars.PSObject.Properties) { Set-Item "env:$($p.Name)" $p.Value }
$env:PATH = (@($vcEnv.pathPrefix) + $env:PATH) -join ';'

# Not `tauri dev --config ...`: that sets TAURI_CONFIG, which the build script watches, so
# every switch between dev and dev:full would recompile the crate. dev-vite.mjs (tauri's
# beforeDevCommand) reads this and idles instead of starting a second Vite.
if ($ExternalVite) { $env:GW_EXTERNAL_VITE = '1' }
# Call the CLI directly rather than through `npm run tauri` (one less npm start-up).
& node (Join-Path $PSScriptRoot 'node_modules\@tauri-apps\cli\tauri.js') $Mode
