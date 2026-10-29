# Allows network (SMB) sign-in to accounts with a BLANK password on THIS machine, so a dev box
# can reach \\<this-pc> shares as the auto-logon user. Windows blocks that by default
# (Lsa\LimitBlankPasswordUse = 1) — run this ON the client PC (e.g. via TeamViewer), as admin.
# SECURITY: this weakens the machine — anyone on its network can log in as the blank-password
# account. Meant as a temporary measure; undo when done with:  .\allow-blank-password-smb.ps1 -Revert
param([switch]$Revert)

# Self-elevate so it can be run from a normal double-click / prompt in the remote session.
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    if ($Revert) { $args += "-Revert" }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $args
    return
}

$key = "HKLM:\SYSTEM\CurrentControlSet\Control\Lsa"
$name = "LimitBlankPasswordUse"
$value = if ($Revert) { 1 } else { 0 }

$before = (Get-ItemProperty -Path $key -Name $name -ErrorAction SilentlyContinue).$name
Set-ItemProperty -Path $key -Name $name -Value $value -Type DWord
$after = (Get-ItemProperty -Path $key -Name $name).$name

Write-Host "$name : $before -> $after"
if ($Revert) {
    Write-Host "Blank-password network sign-in is BLOCKED again (Windows default)." -ForegroundColor Green
} else {
    Write-Host "Blank-password network sign-in is now ALLOWED on this machine." -ForegroundColor Yellow
    Write-Host "Connect from the dev box as  <this-pc-ip>\<username>  with an empty password."
    Write-Host "Remember to revert when finished:  .\allow-blank-password-smb.ps1 -Revert"
}
# Takes effect for new SMB sessions immediately — no reboot needed. If Explorer already cached a
# failed session, clear it first on the dev box:  net use \\<ip> /delete
Read-Host "Press Enter to close"
