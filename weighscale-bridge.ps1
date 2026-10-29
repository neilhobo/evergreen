# Bridges a serial weighscale indicator to a raw TCP port, so a remote dev box can read the
# scale over the VPN — the software equivalent of a Moxa NPort in TCP-server mode. Run this ON
# the PC the scale is plugged into:
#   .\weighscale-bridge.ps1 -ComPort COM5 -Baud 9600 -TcpPort 4001
# First run: add -OpenFirewall (needs admin) to allow the inbound TCP port once.
# One TCP client at a time; when a client drops, the next connect is accepted. Ctrl+C to stop.
# NOTE: the COM port must not be in use — close any scale software on this PC first.
param(
    [string]$ComPort = "COM5",
    [int]$Baud = 9600,
    [ValidateSet("None", "Odd", "Even", "Mark", "Space")][string]$Parity = "None",
    [int]$DataBits = 8,
    [ValidateSet("One", "OnePointFive", "Two")][string]$StopBits = "One",
    [int]$TcpPort = 4001,
    [switch]$OpenFirewall
)

if ($OpenFirewall) {
    netsh advfirewall firewall add rule name="Weighscale bridge $TcpPort" dir=in action=allow protocol=TCP localport=$TcpPort | Out-Null
    Write-Host "Firewall: inbound TCP $TcpPort allowed." -ForegroundColor Yellow
}

$sp = New-Object System.IO.Ports.SerialPort $ComPort, $Baud, $Parity, $DataBits, $StopBits
$sp.Open()
Write-Host "Opened $ComPort @ $Baud $($Parity.Substring(0,1))$DataBits$(if ($StopBits -eq 'One') {1} elseif ($StopBits -eq 'Two') {2} else {1.5})"

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $TcpPort)
$listener.Start()

$buf = New-Object byte[] 4096
try {
    while ($true) {
        Write-Host "Waiting for a client on TCP $TcpPort ..."
        $client = $listener.AcceptTcpClient()
        $client.NoDelay = $true
        $stream = $client.GetStream()
        Write-Host "Client connected: $($client.Client.RemoteEndPoint)" -ForegroundColor Green
        $sp.DiscardInBuffer() # start the client on fresh data, not a stale backlog
        $bytes = 0
        try {
            while ($true) {
                $moved = $false
                $n = $sp.BytesToRead
                if ($n -gt 0) {
                    $n = $sp.Read($buf, 0, [Math]::Min($buf.Length, $n))
                    $stream.Write($buf, 0, $n)
                    $bytes += $n
                    $moved = $true
                }
                if ($client.Available -gt 0) {
                    $n = $stream.Read($buf, 0, [Math]::Min($buf.Length, $client.Available))
                    if ($n -le 0) { break }
                    $sp.Write($buf, 0, $n) # scale-bound (poll/tare commands), pass through
                    $moved = $true
                }
                # Read-ready with zero available = the client closed the connection.
                if ($client.Client.Poll(0, [System.Net.Sockets.SelectMode]::SelectRead) -and $client.Available -eq 0) { break }
                if (-not $moved) { Start-Sleep -Milliseconds 15 }
            }
        } catch { Write-Host "Client error: $_" -ForegroundColor Red }
        $client.Close()
        Write-Host "Client disconnected ($bytes bytes relayed)."
    }
} finally {
    $sp.Close()
    $listener.Stop()
}
