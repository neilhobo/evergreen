<#
.SYNOPSIS
    Fixes "can't drag-select text" in the PowerShell / cmd console window.

.DESCRIPTION
    The problem is the console HOST (conhost.exe), not the PowerShell version.
    When "Use legacy console" is on, or QuickEdit / line-wrapping selection are
    off, dragging gives you a rectangular block that chops wrapped lines in half,
    or does nothing at all.

    These are all per-user (HKCU) settings, so run this AS THE USER YOU LOG IN
    WITH over RDP. No admin rights needed. Close and reopen the console after.

.PARAMETER Report
    Only show the current settings, change nothing.

.EXAMPLE
    .\fix-console-selection.ps1 -Report
    .\fix-console-selection.ps1
#>
[CmdletBinding()]
param(
    [switch]$Report,

    # Pre-Windows-10 mitigation: widen the screen buffer so output stops
    # wrapping at 80 columns. Block-select then grabs whole lines.
    [int]$BufferWidth,
    [int]$WindowWidth,
    [int]$BufferHeight = 9999,
    [int]$WindowHeight = 50
)

$ErrorActionPreference = 'Stop'

# Value name -> @(wanted, meaning)
$Wanted = [ordered]@{
    ForceV2                  = @(1, 'Use the modern console (0 = "Use legacy console" ticked -> block-select only)')
    QuickEdit                = @(1, 'QuickEdit Mode - lets you select with the mouse at all')
    LineSelection            = @(1, 'Enable line wrapping selection - select flows like Notepad instead of a box')
    CtrlKeyShortcutsDisabled = @(0, 'Allow Ctrl+Shift+C / Ctrl+Shift+V and friends')
    FilterOnPaste            = @(1, 'Strip smart quotes etc. on paste')
    InterceptCopyPaste       = @(1, 'Ctrl+C / Ctrl+V act as copy/paste when text is selected')
}

$ConsoleKey = 'HKCU:\Console'

function Get-CurrentValue {
    param([string]$Key, [string]$Name)
    try { (Get-ItemProperty -Path $Key -Name $Name -ErrorAction Stop).$Name }
    catch { $null }
}

Write-Host ''
Write-Host "Console settings for user: $env:USERDOMAIN\$env:USERNAME" -ForegroundColor Cyan
Write-Host "Machine: $env:COMPUTERNAME   OS: $([Environment]::OSVersion.Version)" -ForegroundColor Cyan
Write-Host "PowerShell: $($PSVersionTable.PSVersion)  (note: irrelevant to selection - the host is what matters)" -ForegroundColor DarkGray

# The v2 console host (and everything except QuickEdit below) shipped with
# Windows 10 / Server 2016. On anything older conhost ignores these values.
$IsLegacyOnly = [Environment]::OSVersion.Version.Major -lt 10
if ($IsLegacyOnly) {
    Write-Host ''
    Write-Host '  !! This OS predates Windows 10 / Server 2016.' -ForegroundColor Red
    Write-Host '     It only has the old console host. ForceV2, LineSelection,' -ForegroundColor Red
    Write-Host '     CtrlKeyShortcutsDisabled, FilterOnPaste and InterceptCopyPaste' -ForegroundColor Red
    Write-Host '     are ignored here - only QuickEdit does anything.' -ForegroundColor Red
    Write-Host '     There is NO line-wrapping selection on this OS. Selection is' -ForegroundColor Red
    Write-Host '     always a rectangle. See the notes printed at the end.' -ForegroundColor Red
}
Write-Host ''
Write-Host 'Current HKCU\Console defaults:' -ForegroundColor Yellow

foreach ($name in $Wanted.Keys) {
    $want = $Wanted[$name][0]
    $desc = $Wanted[$name][1]
    $have = Get-CurrentValue -Key $ConsoleKey -Name $name
    $shown = if ($null -eq $have) { '(not set)' } else { $have }
    $inert = $IsLegacyOnly -and $name -ne 'QuickEdit'
    $ok = ($have -eq $want)
    $colour = if ($inert) { 'DarkGray' } elseif ($ok) { 'Green' } else { 'Red' }
    $tag = if ($inert) { '  <- ignored on this OS' } else { '' }
    Write-Host ("  {0,-26} {1,-10} want {2}{3}" -f $name, $shown, $want, $tag) -ForegroundColor $colour
    Write-Host ("  {0,-26} {1}" -f '', $desc) -ForegroundColor DarkGray
}

# Per-application subkeys override the defaults above. These are the usual culprits.
$AppKeys = Get-ChildItem -Path $ConsoleKey -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -match 'powershell|pwsh|cmd|conhost|windowsterminal' }

if ($AppKeys) {
    Write-Host ''
    Write-Host 'Per-application overrides found (these BEAT the defaults):' -ForegroundColor Yellow
    foreach ($k in $AppKeys) {
        Write-Host "  $($k.PSChildName)" -ForegroundColor Magenta
        foreach ($name in $Wanted.Keys) {
            $have = Get-CurrentValue -Key $k.PSPath -Name $name
            if ($null -ne $have) { Write-Host ("      {0,-26} {1}" -f $name, $have) -ForegroundColor DarkGray }
        }
    }
}

$bufSize = Get-CurrentValue -Key $ConsoleKey -Name 'ScreenBufferSize'
$winSize = Get-CurrentValue -Key $ConsoleKey -Name 'WindowSize'
function Unpack { param($v) if ($null -eq $v) { '(not set)' } else { '{0} x {1}' -f ($v -band 0xFFFF), (($v -shr 16) -band 0xFFFF) } }
Write-Host ''
Write-Host 'Buffer / window size (cols x rows):' -ForegroundColor Yellow
Write-Host ("  ScreenBufferSize           {0}" -f (Unpack $bufSize)) -ForegroundColor DarkGray
Write-Host ("  WindowSize                 {0}" -f (Unpack $winSize)) -ForegroundColor DarkGray
Write-Host ("  this session               {0} x {1}" -f $Host.UI.RawUI.BufferSize.Width, $Host.UI.RawUI.BufferSize.Height) -ForegroundColor DarkGray

$wt = Get-Command wt.exe -ErrorAction SilentlyContinue
Write-Host ''
Write-Host ("Windows Terminal installed: {0}" -f $(if ($wt) { "yes -> $($wt.Source)" } else { 'no' })) -ForegroundColor Cyan

if ($Report) {
    Write-Host ''
    Write-Host 'Report only - nothing changed. Re-run without -Report to apply.' -ForegroundColor Yellow
    return
}

Write-Host ''
Write-Host 'Applying...' -ForegroundColor Yellow

if (-not (Test-Path $ConsoleKey)) { New-Item -Path $ConsoleKey -Force | Out-Null }

foreach ($name in $Wanted.Keys) {
    if ($IsLegacyOnly -and $name -ne 'QuickEdit') {
        Write-Host "  skipped $name (ignored on this OS)" -ForegroundColor DarkGray
        continue
    }
    New-ItemProperty -Path $ConsoleKey -Name $name -Value $Wanted[$name][0] -PropertyType DWord -Force | Out-Null
    Write-Host "  set HKCU\Console\$name = $($Wanted[$name][0])" -ForegroundColor Green
}

# Remove the same values from per-app subkeys so the defaults actually take effect.
foreach ($k in $AppKeys) {
    foreach ($name in $Wanted.Keys) {
        if ($null -ne (Get-CurrentValue -Key $k.PSPath -Name $name)) {
            Remove-ItemProperty -Path $k.PSPath -Name $name -Force
            Write-Host "  cleared override $($k.PSChildName)\$name" -ForegroundColor Green
        }
    }
}

if ($BufferWidth -gt 0) {
    if ($WindowWidth -le 0) { $WindowWidth = [Math]::Min($BufferWidth, 170) }
    if ($WindowWidth -gt $BufferWidth) { $WindowWidth = $BufferWidth }

    # Both values pack as (rows << 16) | cols.
    $buf = ($BufferHeight -shl 16) -bor $BufferWidth
    $win = ($WindowHeight -shl 16) -bor $WindowWidth
    New-ItemProperty -Path $ConsoleKey -Name 'ScreenBufferSize' -Value $buf -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $ConsoleKey -Name 'WindowSize'       -Value $win -PropertyType DWord -Force | Out-Null
    Write-Host "  set ScreenBufferSize = $BufferWidth x $BufferHeight" -ForegroundColor Green
    Write-Host "  set WindowSize       = $WindowWidth x $WindowHeight" -ForegroundColor Green

    foreach ($k in $AppKeys) {
        foreach ($name in 'ScreenBufferSize','WindowSize') {
            if ($null -ne (Get-CurrentValue -Key $k.PSPath -Name $name)) {
                Remove-ItemProperty -Path $k.PSPath -Name $name -Force
                Write-Host "  cleared override $($k.PSChildName)\$name" -ForegroundColor Green
            }
        }
    }
}

Write-Host ''
Write-Host 'Done. Close EVERY console window and open a new one - running consoles' -ForegroundColor Cyan
Write-Host 'keep the settings they started with.' -ForegroundColor Cyan

if ($IsLegacyOnly) {
    Write-Host ''
    Write-Host 'This OS cannot do Notepad-style selection in a console. Options:' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  1. Use the PowerShell ISE instead - powershell_ise.exe. Already' -ForegroundColor Yellow
    Write-Host '     installed. Its output pane is an ordinary text box, so drag-' -ForegroundColor Yellow
    Write-Host '     select and Ctrl+C behave exactly like Notepad.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  2. Pipe what you want to the clipboard and skip selecting:' -ForegroundColor Yellow
    Write-Host '         Get-Service | Out-String -Width 4000 | clip' -ForegroundColor Yellow
    Write-Host '     ...or to a file you open in Notepad:' -ForegroundColor Yellow
    Write-Host '         Get-Service | Out-File $env:TEMP\out.txt -Width 4000; notepad $env:TEMP\out.txt' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  3. Widen the buffer so lines stop wrapping - a rectangle then' -ForegroundColor Yellow
    Write-Host '     captures whole lines. Re-run with:  -BufferWidth 300' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  4. Install ConEmu (portable, no admin) for real line selection.' -ForegroundColor Yellow
} else {
    Write-Host ''
    Write-Host 'Then: drag to select (wraps properly), Ctrl+C or Enter to copy,' -ForegroundColor Cyan
    Write-Host 'right-click or Ctrl+V to paste.' -ForegroundColor Cyan
}

Write-Host ''
Write-Host 'If a pinned/Start-menu shortcut still misbehaves, its own console' -ForegroundColor DarkYellow
Write-Host 'properties are baked into the .lnk and beat the registry: right-click' -ForegroundColor DarkYellow
Write-Host 'it > Properties > Options, tick QuickEdit Mode.' -ForegroundColor DarkYellow
