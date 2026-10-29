# Diagnoses why the Globeweigh Touch app lists no COM ports / printers on this PC.
# Run in a normal PowerShell window (no admin needed):
#   powershell -ExecutionPolicy Bypass -File .\diagnose-ports-printers.ps1
# Results print to screen AND save to diagnose-result.txt next to the script - send that back.
$ErrorActionPreference = 'Continue'
$log = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'diagnose-result.txt'
Start-Transcript -Path $log -Force | Out-Null

"=== Globeweigh port/printer diagnostics - $(Get-Date) ==="
"Machine: $env:COMPUTERNAME   User: $env:USERNAME"
try { $os = Get-CimInstance Win32_OperatingSystem; "OS: $($os.Caption) ($($os.Version))" } catch { "OS query FAILED: $_" }

"`n--- 1. SERIALCOMM registry (what Touch v0.1.5 reads for COM ports) ---"
cmd /c "reg query HKLM\HARDWARE\DEVICEMAP\SERIALCOMM" 2>&1

"`n--- 2. .NET SerialPort.GetPortNames() ---"
try { $p = [System.IO.Ports.SerialPort]::GetPortNames(); if ($p) { $p -join ', ' } else { '(none)' } } catch { "FAILED: $_" }

"`n--- 3. WMI Win32_SerialPort ---"
try { Get-CimInstance Win32_SerialPort | Select-Object DeviceID, Name | Format-Table -AutoSize | Out-String } catch { "FAILED: $_" }

"`n--- 4. PnP devices named (COMx) - should match Device Manager's Ports section ---"
try {
    Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match '\(COM\d+\)' } |
        Select-Object Name, Status, DeviceID | Format-Table -AutoSize | Out-String
} catch { "FAILED: $_" }

"`n--- 5. mode.com ---"
cmd /c mode 2>&1

"`n--- 6. Printers via EnumPrinters (what Touch v0.1.5 uses) ---"
try {
    Add-Type -AssemblyName System.Drawing
    $pr = [System.Drawing.Printing.PrinterSettings]::InstalledPrinters
    if ($pr.Count) { $pr | ForEach-Object { "  $_" } } else { '(none)' }
} catch { "FAILED: $_" }

"`n--- 7. Get-Printer ---"
try { Get-Printer | Select-Object Name, DriverName, PortName | Format-Table -AutoSize | Out-String } catch { "FAILED: $_" }

"`n--- 8. Printer registry keys ---"
"[local printers]"
cmd /c "reg query HKLM\SYSTEM\CurrentControlSet\Control\Print\Printers" 2>&1
"[per-user network connections]"
cmd /c "reg query HKCU\Printers\Connections" 2>&1

"`n--- 9. Print Spooler service (EnumPrinters returns NOTHING if this is stopped) ---"
try { Get-Service Spooler | Select-Object Status, StartType | Format-Table -AutoSize | Out-String } catch { "FAILED: $_" }

"`n--- 10. Installed Globeweigh Touch version ---"
try {
    Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
                  'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
                  'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue } |
        Where-Object { $_.DisplayName -like '*Globeweigh Touch*' } |
        Select-Object DisplayName, DisplayVersion, InstallLocation | Format-Table -AutoSize | Out-String
} catch { "FAILED: $_" }

Stop-Transcript | Out-Null
""
"Done. Send back: $log"
