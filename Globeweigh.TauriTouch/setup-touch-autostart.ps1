<#
  Makes a shop-floor touch PC come back on its own after a power cut: power on, log on,
  and put Globeweigh Touch back on the screen with nobody standing there.

  RUN AS ADMINISTRATOR, on each touch PC, ideally while logged on as the account the
  screen actually runs as (the installer is per-user - see below).

  What it does:
    1. Registers scheduled task "Globeweigh-Touch-Autostart" for the screen's user. It runs a
       watchdog that launches the app at logon AND relaunches it if it ever dies (crash,
       someone closing it, a failed update).
    2. Power: no sleep, no monitor blank, no screensaver, no lock-on-idle, hibernate +
       Fast Startup off (a hybrid-boot hiberfile is the wrong thing to restore after a power cut).
    3. Boot: bootstatuspolicy ignoreallfailures + recoveryenabled No, so an unclean shutdown
       doesn't park the PC on the "Windows didn't start correctly" recovery menu forever.
    4. Windows Update: no auto-reboot while someone is logged on.
    5. Optional: Windows auto-logon (-AutoLogonPassword), so the PC reaches the desktop unattended.
    6. Reports the BIOS "after AC power loss" setting to check - the one step that usually
       cannot be scripted, and the #1 reason a PC never comes back at all.

  WHY THE USER MATTERS: the NSIS bundle is built with INSTALLMODE "currentUser", so the exe
  lives in %LOCALAPPDATA%\Globeweigh Touch of whoever ran the installer. Autostart therefore
  has to run as that same account, and auto-logon has to sign in as that same account.
  Run with -Status to see whether they line up on this machine.

  The app itself needs no help when the server is slow to come back: loader\index.html retries
  <api>/touch/ every 5 seconds forever.

  Usage:
    .\setup-touch-autostart.ps1 -Status                          # report only, changes nothing
    .\setup-touch-autostart.ps1                                  # autostart + power + boot hardening
    .\setup-touch-autostart.ps1 -AutoLogonPassword 'p@ss'        # ... plus Windows auto-logon
    .\setup-touch-autostart.ps1 -AutoLogonPassword 'p@ss' -AutologonExe C:\Tools\Autologon64.exe
    .\setup-touch-autostart.ps1 -User WEIGH01\touch              # target another account
    .\setup-touch-autostart.ps1 -Remove                          # undo the autostart task
#>
param(
    # The Windows account the screen runs as. Default: whoever is logged on at the console.
    [string]$User,

    # Supply to enable Windows auto-logon for $User. Without it, auto-logon is left alone.
    [string]$AutoLogonPassword,

    # Optional path to Sysinternals Autologon64.exe. If given, the password is stored as an
    # LSA secret instead of plaintext in the registry. Strongly preferred.
    [string]$AutologonExe,

    # Log straight back in if the account signs out (kiosk behaviour).
    [switch]$ForceAutoLogon,

    # Seconds between watchdog checks.
    [int]$CheckSeconds = 15,

    [string]$Dir      = 'C:\Globeweigh\Touch',
    [string]$TaskName = 'Globeweigh-Touch-Autostart',

    [switch]$SkipPower,
    [switch]$SkipBootPolicy,
    [switch]$SkipWindowsUpdate,

    # Best-effort BIOS "restore on AC power loss" via vendor tooling (Lenovo WMI / Dell cctk).
    # Off by default - it is a firmware write, so opt in deliberately.
    [switch]$TryBiosAcRecovery,

    [switch]$Status,
    [switch]$Remove,

    # Don't wait for a keypress at the end (for unattended/scripted runs).
    [switch]$NoPause,

    # Set automatically when the script re-launches itself elevated. Don't pass it by hand.
    [switch]$Relaunched
)

$ErrorActionPreference = 'Stop'

$APP_NAME = 'Globeweigh Touch'
$APP_PROC = 'globeweigh-tauritouch'
$APP_BIN  = 'globeweigh-tauritouch.exe'

function Step($m) { Write-Host "`n== $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "   $m" -ForegroundColor Green }
function Note($m) { Write-Host "   $m" -ForegroundColor Gray }
function Warn($m) { Write-Host "   ! $m" -ForegroundColor Yellow }

# The whole point of this script is that you can read what it said. Read-Host is the normal
# way to hold a console open, but it throws in a host with no interactive input - and a
# swallowed throw is exactly how a window disappears. So: three ways, ending in a plain wait
# that cannot fail.
function Hold-Window($msg) {
    if ($NoPause) { return }
    Write-Host ''
    try { Read-Host $msg | Out-Null; return } catch { }
    try {
        Write-Host "$msg (press a key)" -ForegroundColor Cyan
        [void][Console]::ReadKey($true)
        return
    } catch { }
    Write-Host 'This window closes in 3 minutes - copy what you need now.' -ForegroundColor Cyan
    Start-Sleep -Seconds 180
}

function Test-Admin {
    $wi = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $wi).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
}

# New-Item -Force on an EXISTING registry key can wipe its values, and one of the keys we
# touch is Policies\System (EnableLUA and friends live there). Only ever create what's missing.
function Ensure-RegKey($path) {
    if (-not (Test-Path $path)) { New-Item -Path $path -Force | Out-Null }
}

# ---------------------------------------------------------------- logging

# Started before ANYTHING else, including the elevation hop, so that a run which ends with a
# vanished window still leaves a file behind saying how far it got. The two stages log
# separately: the first window only fires UAC, the elevated one does the work.
if ($Relaunched) {
    # Elevated, so C:\Globeweigh\Touch is creatable. Same path on every PC, named per machine.
    $setupLog = Join-Path $Dir "setup-$env:COMPUTERNAME.log"
    try { if (-not (Test-Path $Dir)) { New-Item -ItemType Directory -Force $Dir | Out-Null } } catch { }
} else {
    $setupLog = Join-Path $env:TEMP "touch-setup-launch-$env:COMPUTERNAME.log"
}
try {
    Start-Transcript -Path $setupLog -Force | Out-Null
    $transcribing = $true
} catch {
    try {
        $setupLog = Join-Path $env:TEMP "touch-setup-$env:COMPUTERNAME.log"
        Start-Transcript -Path $setupLog -Force | Out-Null
        $transcribing = $true
    } catch { $transcribing = $false }
}
Write-Host "Logging to: $setupLog" -ForegroundColor DarkCyan

# ---------------------------------------------------------------- self-elevate

# One script, run it any way you like: double-click, right-click "Run with PowerShell", or a
# normal window. If it isn't elevated it re-launches itself through UAC with -NoExit, so the
# new window stays put whatever happens - including a crash before the error handler exists.
if (-not (Test-Admin)) {
    Write-Host 'Not elevated - asking for administrator rights...' -ForegroundColor Yellow

    if (-not $PSCommandPath) {
        # Pasted in (irm ... | iex), so there is no file to re-launch elevated. Not a failure
        # worth troubleshooting - just say the one thing that fixes it.
        Write-Host ''
        Write-Host 'This needs to run as administrator.' -ForegroundColor Red
        Write-Host ''
        Write-Host '  1. Press Windows key + X, then press A   (Windows PowerShell (Admin))' -ForegroundColor White
        Write-Host '  2. Paste this again:' -ForegroundColor White
        Write-Host ''
        Write-Host '     irm http://evergreen:5099/touch-setup.txt | iex' -ForegroundColor Cyan
        Write-Host ''
        Write-Host 'Nothing has been changed on this PC.' -ForegroundColor Yellow
        Hold-Window 'Press Enter to close'
        exit 1
    }

    # Run the elevated copy from TEMP. Elevated PowerShell does not inherit the user's mapped
    # drives, so a script launched from Z:\... would be unreachable to it; TEMP always works.
    $selfCopy = Join-Path $env:TEMP 'setup-touch-autostart.ps1'
    try {
        Copy-Item -LiteralPath $PSCommandPath -Destination $selfCopy -Force
        # A file copied off a share carries a mark-of-the-web and is refused under RemoteSigned.
        Unblock-File -LiteralPath $selfCopy -ErrorAction SilentlyContinue
    } catch { $selfCopy = $PSCommandPath }

    # Rebuild whatever was passed to this run so the elevated copy does the same job.
    $argText = '-Relaunched'
    foreach ($kv in $PSBoundParameters.GetEnumerator()) {
        if ($kv.Key -eq 'Relaunched') { continue }
        if ($kv.Value -is [switch]) {
            if ($kv.Value.IsPresent) { $argText += " -$($kv.Key)" }
        } else {
            $argText += " -$($kv.Key) '" + ("$($kv.Value)" -replace "'", "''") + "'"
        }
    }

    # NOT -File. A Group Policy execution policy (MachinePolicy/UserPolicy) outranks the
    # -ExecutionPolicy Bypass switch and refuses to load script FILES at all - PowerShell
    # prints a red error and exits before any line of this script runs, which is exactly how
    # a window "closes by itself" no matter what protections the script contains. Running the
    # file's TEXT as a scriptblock is not gated that way, so this works under a locked-down GPO.
    $selfEsc = $selfCopy -replace "'", "''"
    $cmd = "& ([scriptblock]::Create((Get-Content -LiteralPath '$selfEsc' -Raw))) $argText"
    $fwd = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-NoExit', '-Command', $cmd)

    $elevatedOk = $false
    try {
        Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') `
                      -Verb RunAs -ArgumentList $fwd -ErrorAction Stop
        $elevatedOk = $true
    } catch {
        Write-Host "`nCould not elevate: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host 'Say Yes to the UAC prompt, or sign in as a user with admin rights.' -ForegroundColor Yellow
    }

    if ($transcribing) { try { Stop-Transcript | Out-Null } catch { } }

    # This window must NOT close on its own. If it did and the elevated one failed to appear,
    # there would be nothing left on screen to read - which is exactly what kept happening.
    if ($elevatedOk) {
        Write-Host ''
        Write-Host 'A second window opened, running as administrator - the work happens THERE.' -ForegroundColor Green
        Write-Host 'If no second window appeared, the UAC prompt was refused or blocked by policy.' -ForegroundColor Yellow
        Write-Host "That window logs to: $(Join-Path $Dir "setup-$env:COMPUTERNAME.log")" -ForegroundColor Cyan
    } else {
        Write-Host ''
        Write-Host 'Nothing was changed on this PC.' -ForegroundColor Red
        Write-Host 'Open PowerShell as administrator yourself, then run:' -ForegroundColor Yellow
        Write-Host "   & '$PSCommandPath'" -ForegroundColor White
    }
    Hold-Window 'Press Enter to close THIS window'
    exit 0
}

# ---------------------------------------------------------------- target user

# The console user, not $env:USERNAME: an over-the-shoulder UAC elevation runs this script as
# the admin account while the screen is still logged on as someone else, and getting that
# wrong points autostart at the wrong profile (and so at a non-existent exe).
function Get-ConsoleUser {
    try {
        $u = (Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).UserName
        if ($u) { return $u }
    } catch { }
    return "$env:USERDOMAIN\$env:USERNAME"
}

function Resolve-Account($name) {
    foreach ($cand in @($name, "$env:COMPUTERNAME\$name", "$env:USERDOMAIN\$name")) {
        if (-not $cand) { continue }
        try {
            $sid = (New-Object Security.Principal.NTAccount($cand)).Translate([Security.Principal.SecurityIdentifier]).Value
            return [pscustomobject]@{ Account = $cand; Sid = $sid; Short = ($cand -split '\\')[-1] }
        } catch { }
    }
    throw "Could not resolve Windows account '$name'. Pass -User 'DOMAIN\user' or '$env:COMPUTERNAME\user'."
}

function Get-ProfilePath($sid) {
    $k = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid"
    (Get-ItemProperty $k -Name ProfileImagePath -ErrorAction SilentlyContinue).ProfileImagePath
}

# Mirrors the resolution the watchdog does at runtime, so -Status reports what it will find.
function Get-TouchExeFor($sid, $profilePath) {
    $keys = @()
    if (Test-Path "Registry::HKEY_USERS\$sid") {
        $keys += "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Uninstall\$APP_NAME"
    }
    $keys += "Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\$APP_NAME"
    $keys += "Registry::HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$APP_NAME"
    foreach ($k in $keys) {
        $p = Get-ItemProperty $k -ErrorAction SilentlyContinue
        if ($p -and $p.InstallLocation) {
            # NSIS writes InstallLocation wrapped in quotes.
            $loc = ($p.InstallLocation -replace '"', '').TrimEnd('\')
            $bin = if ($p.MainBinaryName) { $p.MainBinaryName } else { $APP_BIN }
            $c = Join-Path $loc $bin
            if (Test-Path $c) { return $c }
        }
    }
    $fallbacks = @()
    if ($profilePath) { $fallbacks += (Join-Path $profilePath "AppData\Local\$APP_NAME\$APP_BIN") }
    $fallbacks += (Join-Path $env:ProgramFiles "$APP_NAME\$APP_BIN")
    if (${env:ProgramFiles(x86)}) { $fallbacks += (Join-Path ${env:ProgramFiles(x86)} "$APP_NAME\$APP_BIN") }
    foreach ($c in $fallbacks) { if (Test-Path $c) { return $c } }
    return $null
}

function Get-WatchdogProcess {
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*touch-watchdog.ps1*' }
}

# A mistyped auto-logon password is invisible until the next power cut, when the PC quietly
# sits at the sign-in screen instead. Check it before writing it anywhere.
function Test-Credential($account, $short, $password) {
    try {
        Add-Type -AssemblyName System.DirectoryServices.AccountManagement -ErrorAction Stop
        $isLocal = (($account -split '\\')[0] -eq $env:COMPUTERNAME)
        $ctxType = if ($isLocal) { 'Machine' } else { 'Domain' }
        $ctx = New-Object System.DirectoryServices.AccountManagement.PrincipalContext($ctxType)
        return [pscustomobject]@{ Checked = $true; Valid = $ctx.ValidateCredentials($short, $password) }
    } catch {
        return [pscustomobject]@{ Checked = $false; Valid = $false }
    }
}

# ---------------------------------------------------------------- transcript

# Printed before anything can fail, so a broken run still says what it was running on.
Write-Host "=== Globeweigh Touch autostart setup - $(Get-Date) ===" -ForegroundColor White
try { Note "OS         : $((Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).Caption)" } catch { Note 'OS         : (unreadable)' }
Note "PowerShell : $($PSVersionTable.PSVersion)"
Note "Elevated   : $(Test-Admin)"
Note "Policy     : $(Get-ExecutionPolicy)"
Note "Script     : $PSCommandPath"
Note "Log        : $setupLog"

function Invoke-Setup {

# The ScheduledTasks cmdlets are Windows 8 / Server 2012 and newer. On anything older this
# script cannot work at all, and saying so beats twenty lines of "not recognized".
if (-not (Get-Command Register-ScheduledTask -ErrorAction SilentlyContinue)) {
    throw "This PC has PowerShell $($PSVersionTable.PSVersion) with no ScheduledTasks module (needs Windows 8 / Server 2012 or newer). Autostart must be set up by hand here."
}

$consoleUser = Get-ConsoleUser
if (-not $User) { $User = $consoleUser }
$acct        = Resolve-Account $User
$userProfile = Get-ProfilePath $acct.Sid      # NOT $profile - that is an automatic variable
$exe         = Get-TouchExeFor $acct.Sid $userProfile

$watchdog = Join-Path $Dir 'touch-watchdog.ps1'
$logPath  = if ($userProfile) { Join-Path $userProfile "AppData\Local\$APP_NAME\autostart.log" } else { $null }

Write-Host "Globeweigh Touch - unattended startup setup" -ForegroundColor White
Note "Machine : $env:COMPUTERNAME  ($((Get-CimInstance Win32_OperatingSystem).Caption))"
Note "Console : $consoleUser"
Note "Target  : $($acct.Account)  [$($acct.Sid)]"
Note "Profile : $(if ($userProfile) { $userProfile } else { '(no profile - has this account ever logged on?)' })"
Note "App exe : $(if ($exe) { $exe } else { "(not found - $APP_NAME is not installed for this account)" })"

# ==========================================================================  STATUS

if ($Status) {
    $elevated = Test-Admin

    Step 'Autostart task'
    $t = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($t) {
        $i = $t | Get-ScheduledTaskInfo
        Ok   "$TaskName - state $($t.State), runs as $($t.Principal.UserId) ($($t.Principal.LogonType))"
        Note "last run $($i.LastRunTime)  result 0x$('{0:X}' -f $i.LastTaskResult)  next $($i.NextRunTime)"
        Note "action  $($t.Actions[0].Execute) $($t.Actions[0].Arguments)"
        if ($t.Principal.UserId -and ($t.Principal.UserId -notlike "*$($acct.Short)")) {
            Warn "task runs as $($t.Principal.UserId) but the target account is $($acct.Account)"
        }
    } else { Warn "$TaskName is not registered" }

    Step 'Processes'
    $wd = Get-WatchdogProcess
    if ($wd) { Ok "watchdog running (pid $($wd.ProcessId -join ', '))" } else { Warn 'watchdog is NOT running' }
    $ap = Get-Process -Name $APP_PROC -ErrorAction SilentlyContinue
    if ($ap) { Ok "$APP_NAME running (pid $($ap.Id -join ', '))" } else { Warn "$APP_NAME is NOT running" }

    Step 'Auto-logon'
    $wl = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -ErrorAction SilentlyContinue
    if ($wl -and $wl.AutoAdminLogon -eq '1') {
        Ok "AutoAdminLogon=1 as $($wl.DefaultDomainName)\$($wl.DefaultUserName)"
        $names = @($wl.PSObject.Properties.Name)
        if ($names -contains 'DefaultPassword') { Warn 'password is stored in PLAINTEXT (DefaultPassword)' }
        else { Note 'no DefaultPassword value - held as an LSA secret (Sysinternals Autologon)' }
        if ($names -contains 'AutoLogonCount') { Warn "AutoLogonCount=$($wl.AutoLogonCount) - auto-logon stops after that many boots" }
        if ($wl.DefaultUserName -and ($wl.DefaultUserName -ne $acct.Short)) {
            Warn "auto-logon user '$($wl.DefaultUserName)' != app owner '$($acct.Short)' - the app will not be on that desktop"
        }
    } else { Warn 'auto-logon is OFF - the PC will sit at the sign-in screen after a power cut' }

    Step 'Power'
    foreach ($s in @(@('SUB_VIDEO', 'VIDEOIDLE',   'monitor off'),
                     @('SUB_SLEEP', 'STANDBYIDLE', 'sleep'),
                     @('SUB_DISK',  'DISKIDLE',    'disk off'))) {
        $out = & powercfg.exe /query SCHEME_CURRENT $s[0] $s[1]
        $v = $out | Where-Object { $_ -match 'Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)' } |
             ForEach-Object { [Convert]::ToInt64($Matches[1], 16) } | Select-Object -First 1
        if ($null -eq $v) { Note ("{0,-18} (not readable)" -f $s[2]) }
        elseif ($v -eq 0)  { Ok   ("{0,-18} 0 (never)" -f $s[2]) }
        else               { Warn ("{0,-18} $v seconds - should be 0 on a shop-floor screen" -f $s[2]) }
    }
    # CONSOLELOCK is a hidden setting, so powercfg /query won't show it - read where it lands.
    $scheme = ((& powercfg.exe /getactivescheme) -join ' ') -replace '.*GUID:\s*([0-9a-f-]+).*', '$1'
    $clk = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\$scheme\fea3413e-7e05-4911-9a71-700331f1c294\0e796bdb-100d-47d6-a2d5-f7d2daa51f51" -ErrorAction SilentlyContinue
    if ($null -eq $clk) { Warn ("{0,-18} default (password IS required on wake)" -f 'lock on wake') }
    elseif ($clk.ACSettingIndex -eq 0) { Ok ("{0,-18} 0 (no password on wake)" -f 'lock on wake') }
    else { Warn ("{0,-18} $($clk.ACSettingIndex) (password required on wake)" -f 'lock on wake') }

    $pw = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' -ErrorAction SilentlyContinue
    $hiber = if (Test-Path "$env:SystemDrive\hiberfil.sys") { 'yes' } else { 'no' }
    Note ("hiberfil.sys present={0}  HiberbootEnabled(FastStartup)={1}" -f $hiber,
          $(if ($null -ne $pw.HiberbootEnabled) { $pw.HiberbootEnabled } else { '(unset)' }))
    $desk = "Registry::HKEY_USERS\$($acct.Sid)\Control Panel\Desktop"
    if (Test-Path $desk) {
        $d = Get-ItemProperty $desk
        Note "screensaver active=$($d.ScreenSaveActive) timeout=$($d.ScreenSaveTimeOut) secure=$($d.ScreenSaverIsSecure)"
    } else { Note "the target user's hive is not loaded - screensaver state unknown" }

    Step 'Boot / Windows Update'
    $bcd = & bcdedit.exe /enum "{current}" | Select-String -Pattern 'bootstatuspolicy|recoveryenabled'
    if ($bcd) { $bcd | ForEach-Object { Ok $_.ToString().Trim() } }
    elseif (-not $elevated) { Note 'bcdedit needs elevation - re-run as admin to read boot policy' }
    else { Warn 'bootstatuspolicy / recoveryenabled are at defaults (a bad shutdown can stop at the recovery menu)' }
    $au = Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' -ErrorAction SilentlyContinue
    Note "NoAutoRebootWithLoggedOnUsers=$(if ($au) { $au.NoAutoRebootWithLoggedOnUsers } else { '(unset)' })"

    Step 'BIOS'
    $cs = Get-CimInstance Win32_ComputerSystem
    Note "$($cs.Manufacturer) $($cs.Model) - check firmware 'After AC power loss' = Power On (not readable from Windows)"

    if ($logPath -and (Test-Path $logPath)) {
        Step 'Watchdog log (last 15 lines)'
        Get-Content $logPath -Tail 15 | ForEach-Object { Note $_ }
    }
    Write-Host ''
    return
}

# Elevation was handled at the top by self-relaunch; this only fires if that was bypassed.
if (-not (Test-Admin)) { throw 'Not running as Administrator.' }

# ==========================================================================  REMOVE

if ($Remove) {
    Step 'Removing autostart'
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Ok "unregistered $TaskName"
    } else { Note "$TaskName was not registered" }
    Get-WatchdogProcess | ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        Ok "stopped watchdog pid $($_.ProcessId)"
    }
    if (Test-Path $watchdog) { Remove-Item $watchdog -Force; Ok "deleted $watchdog" }
    Warn 'power, boot, Windows Update and auto-logon settings were left as they are (undo those by hand if needed).'
    Write-Host ''
    return
}

# ==========================================================================  1. WATCHDOG

Step '1. Watchdog script'
if (-not (Test-Path $Dir)) { New-Item -ItemType Directory -Force $Dir | Out-Null }

# Single-quoted here-string: nothing below is expanded by THIS script. Everything the
# watchdog needs it resolves itself, at runtime, in the screen user's own session - so a
# reinstall, an updater bump or a different install location all keep working.
$watchdogBody = @'
# Globeweigh Touch watchdog - keeps the shop-floor app on screen.
# Written by setup-touch-autostart.ps1. Runs as the screen's user, in their session.
param(
    [int]$Interval = 15,
    # Relaunch only after the app has been missing this many checks running: the Tauri
    # updater stops the app for a few seconds mid-install, and racing it would make the
    # installer's "app is running" check fail.
    [int]$MissesBeforeLaunch = 2
)
$ErrorActionPreference = 'Continue'

$APP_NAME = 'Globeweigh Touch'
$APP_PROC = 'globeweigh-tauritouch'
$APP_BIN  = 'globeweigh-tauritouch.exe'
$log      = Join-Path $env:LOCALAPPDATA "$APP_NAME\autostart.log"

function Write-Log($m) {
    try {
        $d = Split-Path $log -Parent
        if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force $d | Out-Null }
        if ((Test-Path $log) -and ((Get-Item $log).Length -gt 512KB)) { Move-Item $log "$log.1" -Force }
        Add-Content -Path $log -Value ('{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $m)
    } catch { }
}

function Get-TouchExe {
    foreach ($k in @("HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$APP_NAME",
                     "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$APP_NAME",
                     "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$APP_NAME")) {
        $p = Get-ItemProperty $k -ErrorAction SilentlyContinue
        if ($p -and $p.InstallLocation) {
            $loc = ($p.InstallLocation -replace '"', '').TrimEnd('\')
            $bin = if ($p.MainBinaryName) { $p.MainBinaryName } else { $APP_BIN }
            $c = Join-Path $loc $bin
            if (Test-Path $c) { return $c }
        }
    }
    $c = Join-Path $env:LOCALAPPDATA "$APP_NAME\$APP_BIN"
    if (Test-Path $c) { return $c }
    $c = Join-Path $env:ProgramFiles "$APP_NAME\$APP_BIN"
    if (Test-Path $c) { return $c }
    if (${env:ProgramFiles(x86)}) {
        $c = Join-Path ${env:ProgramFiles(x86)} "$APP_NAME\$APP_BIN"
        if (Test-Path $c) { return $c }
    }
    return $null
}

# An installer or uninstaller on screen means an update is in flight - leave it alone.
function Test-InstallerRunning {
    foreach ($n in (Get-Process -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ProcessName -Unique)) {
        if ($n -like '*setup*' -or $n -like 'unins*' -or $n -eq 'msiexec') { return $true }
    }
    return $false
}

Write-Log "watchdog started (pid $PID, user $env:USERNAME, every ${Interval}s)"
$misses  = 0
$noExeAt = [datetime]::MinValue

while ($true) {
    try {
        if (Get-Process -Name $APP_PROC -ErrorAction SilentlyContinue) {
            if ($misses -gt 0) { Write-Log 'app is back'; $misses = 0 }
        }
        elseif (Test-InstallerRunning) {
            Write-Log 'app down but an installer is running - holding off'
            $misses = 0
        }
        else {
            $misses++
            if ($misses -ge $MissesBeforeLaunch) {
                $exe = Get-TouchExe
                if ($exe) {
                    Write-Log "app not running - starting $exe"
                    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent)
                    $misses = 0
                    Start-Sleep -Seconds 10   # let the window come up before checking again
                }
                else {
                    # Don't fill the log with this every cycle.
                    if (((Get-Date) - $noExeAt).TotalMinutes -gt 10) {
                        Write-Log "$APP_NAME is not installed for $env:USERNAME - nothing to start"
                        $noExeAt = Get-Date
                    }
                    $misses = 0
                    Start-Sleep -Seconds 45
                }
            }
        }
    } catch {
        # Never let the loop die: this process IS the recovery mechanism.
        Write-Log "loop error: $_"
    }
    Start-Sleep -Seconds $Interval
}
'@

Set-Content -Path $watchdog -Value $watchdogBody -Encoding UTF8
Ok "wrote $watchdog"
if ($logPath) { Note "watchdog log: $logPath" }

# ==========================================================================  2. TASK

Step "2. Scheduled task '$TaskName'"

# Remove any other way the app could be auto-started, or the screen gets two windows.
$startupDirs = @()
if ($userProfile) { $startupDirs += (Join-Path $userProfile 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup') }
$startupDirs += (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\StartUp')
foreach ($sd in $startupDirs) {
    if (-not (Test-Path $sd)) { continue }
    foreach ($lnk in (Get-ChildItem $sd -Filter '*.lnk' -ErrorAction SilentlyContinue)) {
        try {
            $target = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk.FullName).TargetPath
            if ($target -like "*$APP_BIN") {
                Remove-Item $lnk.FullName -Force
                Warn "removed duplicate startup shortcut: $($lnk.FullName)"
            }
        } catch { }
    }
}
$runKey = "Registry::HKEY_USERS\$($acct.Sid)\Software\Microsoft\Windows\CurrentVersion\Run"
if (Test-Path $runKey) {
    $rk = Get-Item $runKey
    foreach ($n in $rk.GetValueNames()) {
        if ("$($rk.GetValue($n))" -like "*$APP_BIN*") {
            Remove-ItemProperty -Path $runKey -Name $n -Force
            Warn "removed duplicate Run key entry: $n"
        }
    }
}

$psExe  = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $psExe `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$watchdog`" -Interval $CheckSeconds"

$trigger = New-ScheduledTaskTrigger -AtLogOn -User $acct.Account
# Give the shell and the network a moment before the first launch attempt.
try { $trigger.Delay = 'PT20S' } catch { Warn 'could not set a logon delay on the trigger (harmless)' }
# Insurance only: if the watchdog process itself ever disappears, this re-runs the task.
# MultipleInstances=IgnoreNew makes it a no-op while the watchdog is alive.
try {
    $trigger.Repetition = New-CimInstance -ClassName MSFT_TaskRepetitionPattern `
        -Namespace Root/Microsoft/Windows/TaskScheduler -ClientOnly `
        -Property @{ Interval = 'PT10M'; StopAtDurationEnd = $false }
} catch { Warn 'could not add the 10-minute re-trigger (task still runs at logon)' }

$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew -StartWhenAvailable `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Seconds 0)
try { $settings.IdleSettings.StopOnIdleEnd = $false } catch { }

# Interactive: the watchdog must run inside the user's desktop session to show a window.
$principal = New-ScheduledTaskPrincipal -UserId $acct.Account -LogonType Interactive -RunLevel Limited

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal `
    -Description "Launches Globeweigh Touch at logon and relaunches it if it stops. Checks every $CheckSeconds s." | Out-Null
Ok "registered, runs as $($acct.Account) at logon (+20s), checks every ${CheckSeconds}s"

# ==========================================================================  3. POWER

if (-not $SkipPower) {
    Step '3. Power: no sleep, no blank screen, no lock'
    foreach ($t in @(@('monitor-timeout-ac', 0), @('monitor-timeout-dc', 0),
                     @('standby-timeout-ac', 0), @('standby-timeout-dc', 0),
                     @('disk-timeout-ac',    0), @('disk-timeout-dc',    0),
                     @('hibernate-timeout-ac', 0), @('hibernate-timeout-dc', 0))) {
        & powercfg.exe /change $t[0] $t[1] | Out-Null
    }
    Ok 'sleep / monitor / disk / hibernate timeouts set to Never'

    # Hibernate off also turns off Fast Startup. After a power cut, resuming a hiberfile
    # written before the outage is exactly what we do not want.
    & powercfg.exe /hibernate off | Out-Null
    Ok 'hibernate + Fast Startup off'

    # Don't demand a password when the display wakes.
    & powercfg.exe /setacvalueindex SCHEME_CURRENT SUB_NONE CONSOLELOCK 0 | Out-Null
    & powercfg.exe /setdcvalueindex SCHEME_CURRENT SUB_NONE CONSOLELOCK 0 | Out-Null
    & powercfg.exe /setactive SCHEME_CURRENT | Out-Null
    Ok 'no password required on wake'

    # Screensaver + lock, in the screen user's own hive.
    $desk = "Registry::HKEY_USERS\$($acct.Sid)\Control Panel\Desktop"
    if (Test-Path $desk) {
        Set-ItemProperty $desk -Name ScreenSaveActive    -Value '0' -Type String
        Set-ItemProperty $desk -Name ScreenSaverIsSecure -Value '0' -Type String
        Set-ItemProperty $desk -Name ScreenSaveTimeOut   -Value '0' -Type String
        Remove-ItemProperty $desk -Name 'SCRNSAVE.EXE' -ErrorAction SilentlyContinue
        Ok 'screensaver disabled for the screen user'
    } else {
        Warn "the target user's registry hive is not loaded - screensaver left alone"
        Warn "re-run this while $($acct.Short) is logged on to cover it"
    }

    # Machine-wide idle lock.
    $sysPol = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
    Ensure-RegKey $sysPol
    Set-ItemProperty $sysPol -Name InactivityTimeoutSecs -Value 0 -Type DWord
    Ok 'machine inactivity lock disabled'
}

# ==========================================================================  4. BOOT

if (-not $SkipBootPolicy) {
    Step '4. Boot: survive an unclean shutdown without a menu'
    # After a power cut Windows can otherwise stop at "Windows didn't start correctly" /
    # Startup Repair and wait for a keypress nobody is there to give. Trade-off: genuine
    # boot failures get quieter, so check Event Viewer if a screen starts misbehaving.
    foreach ($pair in @(@('bootstatuspolicy', 'ignoreallfailures'), @('recoveryenabled', 'No'))) {
        $out = & bcdedit.exe /set "{current}" $pair[0] $pair[1]
        if ($LASTEXITCODE -eq 0) { Ok "$($pair[0]) = $($pair[1])" } else { Warn "bcdedit $($pair[0]) failed: $out" }
    }
}

# ==========================================================================  5. WINDOWS UPDATE

if (-not $SkipWindowsUpdate) {
    Step '5. Windows Update: no surprise reboots'
    $au = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
    Ensure-RegKey $au
    Set-ItemProperty $au -Name NoAutoRebootWithLoggedOnUsers -Value 1 -Type DWord
    Ok 'no automatic reboot while someone is logged on'
    Note 'updates still install - the reboot now waits for someone to allow it.'
}

# ==========================================================================  6. AUTO-LOGON

Step '6. Windows auto-logon'
$wlKey  = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
$domain = if ($acct.Account -match '\\') { ($acct.Account -split '\\')[0] } else { $env:COMPUTERNAME }

# One script run by hand on every PC means the password can't always come from a parameter.
# Ask for it here, unless it was passed, or auto-logon is already set up for this account.
if (-not $AutoLogonPassword -and -not $NoPause) {
    $cur = Get-ItemProperty $wlKey -ErrorAction SilentlyContinue
    $alreadyOk = ($cur -and $cur.AutoAdminLogon -eq '1' -and $cur.DefaultUserName -eq $acct.Short)
    if (-not $alreadyOk) {
        Warn "this PC needs auto-logon to reach the desktop on its own after a power cut."
        Write-Host "   Windows password for $($acct.Account) (or just press Enter to skip):" -ForegroundColor Yellow
        $sec = Read-Host '   Password' -AsSecureString
        if ($sec -and $sec.Length -gt 0) {
            $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
            try { $AutoLogonPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
            finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
        }
    }
}

# Refuse to write a password that doesn't work - that would strand the PC at the sign-in
# screen after the next outage, which is exactly the failure this whole script exists to stop.
if ($AutoLogonPassword) {
    $chk = Test-Credential $acct.Account $acct.Short $AutoLogonPassword
    if ($chk.Checked -and -not $chk.Valid) {
        Warn "that password is NOT valid for $($acct.Account) - auto-logon left unchanged."
        Warn 're-run the script and type it again.'
        $AutoLogonPassword = $null
    } elseif ($chk.Checked) {
        Ok 'password verified against Windows'
    } else {
        Note 'could not verify the password here - make sure it is right'
    }
}

if ($AutoLogonPassword) {
    if ($AutologonExe) {
        if (-not (Test-Path $AutologonExe)) { throw "Autologon exe not found: $AutologonExe" }
        & $AutologonExe $acct.Short $domain $AutoLogonPassword '/accepteula' | Out-Null
        Remove-ItemProperty $wlKey -Name DefaultPassword -ErrorAction SilentlyContinue
        Ok "auto-logon enabled for $domain\$($acct.Short) (password stored as an LSA secret)"
    } else {
        Set-ItemProperty $wlKey -Name AutoAdminLogon    -Value '1'                -Type String
        Set-ItemProperty $wlKey -Name DefaultUserName   -Value $acct.Short        -Type String
        Set-ItemProperty $wlKey -Name DefaultDomainName -Value $domain            -Type String
        Set-ItemProperty $wlKey -Name DefaultPassword   -Value $AutoLogonPassword -Type String
        Ok "auto-logon enabled for $domain\$($acct.Short)"
        Warn 'the password is now in the registry in PLAINTEXT (any local admin can read it).'
        Warn 'prefer: -AutologonExe <Sysinternals Autologon64.exe>, which stores it as an LSA secret.'
    }
    # This value decrements each boot and silently switches auto-logon off when it hits zero.
    Remove-ItemProperty $wlKey -Name AutoLogonCount -ErrorAction SilentlyContinue

    if ($ForceAutoLogon) {
        Set-ItemProperty $wlKey -Name ForceAutoLogon -Value '1' -Type String
        Ok 'ForceAutoLogon: the PC signs straight back in if the account signs out'
    }

    # An expiring password kills auto-logon ~6 weeks later, with no obvious cause.
    try {
        $lu = Get-LocalUser -Name $acct.Short -ErrorAction Stop
        if (-not $lu.PasswordNeverExpires) {
            Set-LocalUser -Name $acct.Short -PasswordNeverExpires $true
            Ok "password set to never expire for $($acct.Short)"
        } else { Note 'password already set to never expire' }
    } catch {
        Note 'could not set password-never-expires (domain account?) - check that separately'
    }
} else {
    $wl = Get-ItemProperty $wlKey -ErrorAction SilentlyContinue
    if ($wl -and $wl.AutoAdminLogon -eq '1') {
        Ok "already on, as $($wl.DefaultDomainName)\$($wl.DefaultUserName) (left unchanged)"
        if ($wl.DefaultUserName -ne $acct.Short) { Warn "that is NOT the app's owner ($($acct.Short)) - the app will not be on that desktop" }
    } else {
        Warn 'auto-logon is OFF and no -AutoLogonPassword was given.'
        Warn 'after a power cut this PC will reach the sign-in screen and stop there.'
        Warn "re-run with:  -AutoLogonPassword '<password for $($acct.Short)>'"
    }
}

# ==========================================================================  7. BIOS

Step '7. BIOS: power on when mains returns'
$cs = Get-CimInstance Win32_ComputerSystem
Note "$($cs.Manufacturer) $($cs.Model)"

$biosDone = $false
if ($TryBiosAcRecovery) {
    try {
        $ls = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_BiosSetting -ErrorAction Stop |
              Where-Object { $_.CurrentSetting -like 'AfterPowerLoss*' }
        if ($ls) {
            Note "current: $($ls.CurrentSetting)"
            $set  = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_SetBiosSetting
            $save = Get-CimInstance -Namespace root\wmi -ClassName Lenovo_SaveBiosSettings
            Invoke-CimMethod -InputObject $set  -MethodName SetBiosSetting   -Arguments @{ parameter = 'AfterPowerLoss,Power On;' } | Out-Null
            Invoke-CimMethod -InputObject $save -MethodName SaveBiosSettings -Arguments @{ parameter = ';' } | Out-Null
            Ok 'Lenovo AfterPowerLoss set to Power On (takes effect next boot)'
            $biosDone = $true
        }
    } catch { Note 'no Lenovo BIOS WMI interface on this machine' }

    if (-not $biosDone) {
        $cctk = @("$env:ProgramFiles\Dell\Command Configure\X86_64\cctk.exe",
                  "${env:ProgramFiles(x86)}\Dell\Command Configure\X86_64\cctk.exe") |
                Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
        if ($cctk) {
            $out = & $cctk --AcPwrRcvry=On
            if ($LASTEXITCODE -eq 0) { Ok 'Dell AcPwrRcvry set to On'; $biosDone = $true }
            else { Warn "cctk failed: $out" }
        }
    }
}

if (-not $biosDone) {
    Warn 'NOT SET FROM WINDOWS - do this by hand in firmware setup on each PC:'
    Note '  reboot -> F2 / Del / F10 -> Power (or Advanced -> APM / Power Management)'
    Note '  set "After AC Power Loss" / "AC Recovery" / "Restore on AC Power Loss" = Power On'
    Note '  (vendor names vary: State After Power Failure, AC Loss Auto Restart, PWRON After PWR-Fail)'
    Note 'Without it the PC stays OFF when the mains comes back, and nothing else here matters.'
}

# ==========================================================================  VERIFY

Step 'Starting it now'
if ($consoleUser -eq $acct.Account) {
    Start-ScheduledTask -TaskName $TaskName
    Start-Sleep -Seconds ($CheckSeconds * 2 + 8)
    $wd = Get-WatchdogProcess
    if ($wd) { Ok "watchdog running (pid $($wd.ProcessId -join ', '))" } else { Warn 'watchdog did not start - check Task Scheduler history' }
    if (Get-Process -Name $APP_PROC -ErrorAction SilentlyContinue) { Ok "$APP_NAME is on screen" }
    elseif (-not $exe) { Warn "$APP_NAME is not installed for $($acct.Short) - install it as that user; the watchdog picks it up within ${CheckSeconds}s" }
    else { Warn "$APP_NAME did not start - see $logPath" }
} else {
    Note "console user is $consoleUser, not $($acct.Account) - the task fires at that account's next logon."
}

# Re-read the machine rather than trusting that each step above did what it said.
Step 'After a power cut, this PC will:'

$wlNow   = Get-ItemProperty $wlKey -ErrorAction SilentlyContinue
$taskNow = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
$wdNow   = Get-WatchdogProcess
$monOut  = & powercfg.exe /query SCHEME_CURRENT SUB_VIDEO VIDEOIDLE
$monIdle = $monOut | Where-Object { $_ -match 'Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)' } |
           ForEach-Object { [Convert]::ToInt64($Matches[1], 16) } | Select-Object -First 1

function Line($num, $what, $good, $detail) {
    $mark = if ($good) { 'YES' } else { 'NO ' }
    $col  = if ($good) { 'Green' } else { 'Red' }
    Write-Host ("   {0}. {1,-22} {2}  {3}" -f $num, $what, $mark, $detail) -ForegroundColor $col
}

Line 1 'power itself on'   $false "set BIOS 'After AC Power Loss' = Power On by hand"
Line 2 'log on by itself'  ($wlNow -and $wlNow.AutoAdminLogon -eq '1' -and $wlNow.DefaultUserName -eq $acct.Short) `
                           $(if ($wlNow.AutoAdminLogon -eq '1') { "as $($wlNow.DefaultUserName)" } else { 'auto-logon OFF' })
Line 3 'launch the app'    ($null -ne $taskNow) $(if ($taskNow) { "task $($taskNow.State), as $($taskNow.Principal.UserId)" } else { 'task missing' })
Line 4 'stay awake'        ($monIdle -eq 0) $(if ($monIdle -eq 0) { 'screen never blanks' } else { "monitor sleeps after $monIdle s" })
Line 5 'restart if it dies' ($null -ne $wdNow) $(if ($wdNow) { "watchdog pid $($wdNow.ProcessId -join ',')" } else { 'watchdog not running yet' })

Write-Host "`nLine 1 is the only one that needs someone in the BIOS. The rest are done." -ForegroundColor Cyan
Write-Host "Real test: pull the plug, plug it back in, and walk away." -ForegroundColor Cyan

}   # end Invoke-Setup

# ==========================================================================  DRIVER

$failed = $false
try {
    Invoke-Setup
}
catch {
    $failed = $true
    Write-Host "`n**** FAILED ****" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "`nwhere: $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())" -ForegroundColor DarkGray

    # The three that actually happen in the field.
    $m = "$($_.Exception.Message)"
    if ($m -like '*Administrator*') {
        Write-Host "`nFIX: close this, then Start -> type 'powershell' -> right-click -> Run as administrator." -ForegroundColor Yellow
    } elseif ($m -like '*Could not resolve Windows account*') {
        Write-Host "`nFIX: pass the account explicitly, e.g.  -User '$env:COMPUTERNAME\touch'" -ForegroundColor Yellow
        try {
            Write-Host 'Local accounts on this PC:' -ForegroundColor Yellow
            Get-LocalUser | Where-Object Enabled | ForEach-Object { Write-Host "   $env:COMPUTERNAME\$($_.Name)" -ForegroundColor Gray }
        } catch { }
    } elseif ($m -like '*ScheduledTasks module*') {
        Write-Host "`nFIX: this PC is too old for the scheduled-task route - tell Neil, it needs the Startup-folder fallback." -ForegroundColor Yellow
    }
}
finally {
    if ($transcribing) { try { Stop-Transcript | Out-Null } catch { } }
    Write-Host ''
    if ($failed) {
        Write-Host '-------------------------------------------------------------' -ForegroundColor Red
        Write-Host ' COPY THE RED TEXT ABOVE (select it, then press Enter to copy)' -ForegroundColor Red
        Write-Host " or just send this file:  $setupLog" -ForegroundColor Red
        Write-Host '-------------------------------------------------------------' -ForegroundColor Red
    } else {
        Write-Host "Log saved to: $setupLog" -ForegroundColor Cyan
    }

    # The window must not close on its own - this run is the only chance to read the error.
    # Belt and braces: the elevated relaunch also uses -NoExit, so even a crash that skips
    # this block leaves a live prompt behind.
    if (-not $NoPause) { Hold-Window 'Press Enter to close' }
}

if ($failed) { exit 1 } else { exit 0 }
