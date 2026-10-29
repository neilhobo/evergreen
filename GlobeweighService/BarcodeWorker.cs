using Globeweigh.Model;
using GlobeweighService;
using System;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class BarcodeWorker
{
    private readonly string _ip = "192.168.250.183";
    private readonly int _port = 4001;

    // ERRORd: appeared after the scanner was swapped on 2026-04-29 — it replaces
    // ProdCd: at the head of the frame. Both are accepted as the product-code key.
    private static readonly string[] _prodCdKeys = { "ProdCd:", "ERRORd:" };
    private static readonly string[] _knownKeys = { "ProdCd:", "ERRORd:", "Weight:", "UniqueCode:" };

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ConnectionRecycleInterval = TimeSpan.FromHours(4);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        Log("Service started");

        bool dbOk = await TestDatabaseConnectionAsync();
        if (!dbOk)
            Log("WARNING - Starting without confirmed database connection. Will retry on each scan.");

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    Log($"Connecting to {_ip}:{_port}...");
                    await client.ConnectAsync(_ip, _port);

                    EnableKeepAlive(client.Client, 60000, 10000);

                    Log("Connected to scanner");
                    ScannerState.SetConnected(true);

                    using (var stream = client.GetStream())
                    {
                        var buffer = new byte[1024];
                        var messageBuffer = new StringBuilder();

                        var connectedAtUtc = DateTime.UtcNow;
                        var lastDataAtUtc = DateTime.UtcNow;
                        var lastHeartbeatAtUtc = DateTime.UtcNow;

                        while (!stoppingToken.IsCancellationRequested)
                        {
                            if ((DateTime.UtcNow - lastHeartbeatAtUtc) >= HeartbeatInterval)
                            {
                                Log(
                                    $"Heartbeat - connected to scanner, " +
                                    $"last data {(DateTime.UtcNow - lastDataAtUtc).TotalMinutes:0.0} minute(s) ago");
                                lastHeartbeatAtUtc = DateTime.UtcNow;
                            }

                            if ((DateTime.UtcNow - connectedAtUtc) >= ConnectionRecycleInterval)
                            {
                                Log($"Recycling scanner connection after {ConnectionRecycleInterval.TotalHours:0} hours");
                                break;
                            }

                            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);

                            if (bytesRead == 0)
                            {
                                Log("Scanner connection closed by remote host");
                                break;
                            }

                            lastDataAtUtc = DateTime.UtcNow;

                            string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            messageBuffer.Append(chunk);

                            await ProcessBuffer(messageBuffer);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    Log("I/O error: " + ex.Message);
                }
                catch (SocketException ex)
                {
                    Log($"Socket error ({ex.SocketErrorCode}): {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log("Connection error: " + ex);
                }
                finally
                {
                    ScannerState.SetConnected(false);

                    try
                    {
                        if (client.Connected)
                            client.Close();
                    }
                    catch
                    {
                    }

                    if (!stoppingToken.IsCancellationRequested)
                    {
                        Log($"Disconnected. Retrying in {ReconnectDelay.TotalSeconds:0} seconds...");
                        try
                        {
                            await Task.Delay(ReconnectDelay, stoppingToken);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        ScannerState.SetConnected(false);
        Log("Service stopped");
    }

    // Accepts either STX/ETX framing or CR/LF framing. The new scanner (post 2026-04-29)
    // emits without STX/ETX, so we look for whichever delimiter arrives first.
    private async Task ProcessBuffer(StringBuilder messageBuffer)
    {
        var data = messageBuffer.ToString();

        while (true)
        {
            int stxIndex = data.IndexOf('');
            int etxIndex = stxIndex >= 0 ? data.IndexOf('', stxIndex + 1) : -1;
            int nlIndex = IndexOfAny(data, '\r', '\n');

            bool haveStxFrame = stxIndex >= 0 && etxIndex > stxIndex;
            bool haveNlFrame = nlIndex >= 0;

            if (!haveStxFrame && !haveNlFrame)
                break;

            string raw;
            int consumed;

            if (haveStxFrame && (!haveNlFrame || stxIndex < nlIndex))
            {
                raw = data.Substring(stxIndex + 1, etxIndex - stxIndex - 1);
                consumed = etxIndex + 1;
            }
            else
            {
                raw = data.Substring(0, nlIndex);
                consumed = nlIndex + 1;
            }

            data = data.Substring(consumed);

            raw = (raw ?? string.Empty).Trim('', '', '\r', '\n', ' ', '\t');
            if (raw.Length > 0)
                await HandleMessage(raw);
        }

        // Don't let a never-terminating stream balloon the buffer.
        if (data.Length > 8192)
            data = string.Empty;

        messageBuffer.Clear();
        messageBuffer.Append(data);
    }

    private static int IndexOfAny(string s, char a, char b)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == a || c == b) return i;
        }
        return -1;
    }

    private static bool RawIndicatesScannerError(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return false;
        return raw.TrimStart().StartsWith("ERROR", StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleMessage(string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            bool scannerError = RawIndicatesScannerError(message);

            if (message.Equals("No Read", StringComparison.OrdinalIgnoreCase))
            {
                Log("No Read");
                ScannerState.AddEvent(new ScanEvent
                {
                    Raw = message,
                    Result = ScanResult.NoRead,
                    Message = "No Read",
                    ScannerReportedError = scannerError
                });
                return;
            }

            Log("Received: " + message);

            var scan = ParseMessage(message);

            if (scan == null)
            {
                Log("Ignored - could not parse message");
                ScannerState.AddEvent(new ScanEvent
                {
                    Raw = message,
                    Result = ScanResult.ParseFailed,
                    Message = "Could not parse - no UniqueCode found",
                    ScannerReportedError = scannerError
                });
                return;
            }

            Log($"Parsed  - ProdCd: {scan.ProdCd} | UniqueCode: {scan.UniqueCode} | Weight: {scan.Weight}" +
                (scannerError ? " | (scanner flagged ERROR)" : ""));

            await ProcessBarcode(scan, message, scannerError);
        }
        catch (Exception ex)
        {
            Log("HandleMessage error: " + ex);
            ScannerState.AddEvent(new ScanEvent
            {
                Raw = message,
                Result = ScanResult.Error,
                Message = ex.Message,
                ScannerReportedError = RawIndicatesScannerError(message)
            });
        }
    }

    private class ScanMessage
    {
        public string ProdCd { get; set; }
        public string UniqueCode { get; set; }
        public string Weight { get; set; }
    }

    private ScanMessage ParseMessage(string message)
    {
        var uniqueCode = ExtractValue(message, "UniqueCode:");

        if (string.IsNullOrWhiteSpace(uniqueCode))
            return null;

        string prodCd = null;
        foreach (var key in _prodCdKeys)
        {
            prodCd = ExtractValue(message, key);
            if (!string.IsNullOrWhiteSpace(prodCd)) break;
        }

        return new ScanMessage
        {
            ProdCd = prodCd,
            Weight = ExtractValue(message, "Weight:"),
            UniqueCode = uniqueCode
        };
    }

    private string ExtractValue(string input, string key)
    {
        int start = input.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (start == -1)
            return null;

        start += key.Length;

        int end = input.Length;
        foreach (var otherKey in _knownKeys)
        {
            if (otherKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            int keyPos = input.IndexOf(otherKey, start, StringComparison.OrdinalIgnoreCase);
            if (keyPos != -1 && keyPos < end)
                end = keyPos;
        }

        return input.Substring(start, end - start).Trim();
    }

    private async Task ProcessBarcode(ScanMessage scan, string raw, bool scannerError)
    {
        try
        {
            using (var db = CreateDbContext())
            {
                var pallet = await db.CodicoPallets
                    .Where(x => x.UniqueCode == scan.UniqueCode)
                    .FirstOrDefaultAsync();

                if (pallet == null)
                {
                    Log($"Unknown UniqueCode: {scan.UniqueCode}");
                    ScannerState.AddEvent(new ScanEvent
                    {
                        Raw = raw,
                        ProdCd = scan.ProdCd,
                        UniqueCode = scan.UniqueCode,
                        Weight = scan.Weight,
                        Result = ScanResult.UnknownCode,
                        Message = "No pallet matches this UniqueCode",
                        ScannerReportedError = scannerError
                    });
                    return;
                }

                if (pallet.IsScanned == true)
                {
                    Log($"Already scanned - ignoring (UniqueCode: {scan.UniqueCode} | Pallet: {pallet.id})");
                    ScannerState.AddEvent(new ScanEvent
                    {
                        Raw = raw,
                        ProdCd = scan.ProdCd,
                        UniqueCode = scan.UniqueCode,
                        Weight = scan.Weight,
                        Result = ScanResult.AlreadyScanned,
                        Message = $"Pallet {pallet.id} already scanned",
                        ScannerReportedError = scannerError
                    });
                    return;
                }

                pallet.IsScanned = true;
                pallet.DateScanned = DateTime.Now;

                await db.SaveChangesAsync();

                Log($"Pallet {pallet.id} scanned (UniqueCode: {scan.UniqueCode} | Code: {pallet.Code})");
                ScannerState.AddEvent(new ScanEvent
                {
                    Raw = raw,
                    ProdCd = scan.ProdCd,
                    UniqueCode = scan.UniqueCode,
                    Weight = scan.Weight,
                    Result = ScanResult.Saved,
                    Message = $"Pallet {pallet.id} marked scanned ({pallet.Code})",
                    ScannerReportedError = scannerError
                });
            }
        }
        catch (Exception ex)
        {
            Log("DB error: " + ex);
            ScannerState.AddEvent(new ScanEvent
            {
                Raw = raw,
                ProdCd = scan.ProdCd,
                UniqueCode = scan.UniqueCode,
                Weight = scan.Weight,
                Result = ScanResult.Error,
                Message = "DB error: " + ex.Message,
                ScannerReportedError = scannerError
            });
        }
    }

    public async Task<bool> TestDatabaseConnectionAsync()
    {
        try
        {
            Log("Testing database connection...");

            using (var db = CreateDbContext())
            {
                await db.Database.Connection.OpenAsync();
                Log($"Database connection OK - Server: {db.Database.Connection.DataSource} | DB: {db.Database.Connection.Database}");

                var count = await db.CodicoPallets.CountAsync();
                Log($"CodicoPallets table OK - {count} total records");
            }

            return true;
        }
        catch (System.Data.Entity.Core.MetadataException ex)
        {
            Log("METADATA ERROR - EF cannot find .edmx resources: " + ex.Message);

            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var resources = a.GetManifestResourceNames();
                if (resources.Length > 0)
                    Log($"  [{a.GetName().Name}] resources: {string.Join(", ", resources)}");
            }

            return false;
        }
        catch (System.Data.SqlClient.SqlException ex)
        {
            Log("SQL CONNECTION ERROR: " + ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Log($"Database connection FAILED: {ex}");
            return false;
        }
    }

    private void EnableKeepAlive(Socket socket, uint keepAliveTimeMs, uint keepAliveIntervalMs)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            var inOptionValues = new byte[12];
            BitConverter.GetBytes((uint)1).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes(keepAliveTimeMs).CopyTo(inOptionValues, 4);
            BitConverter.GetBytes(keepAliveIntervalMs).CopyTo(inOptionValues, 8);

            socket.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);

            Log($"TCP keepalive enabled (time={keepAliveTimeMs}ms, interval={keepAliveIntervalMs}ms)");
        }
        catch (Exception ex)
        {
            Log("Failed to enable TCP keepalive: " + ex.Message);
        }
    }

    private void Log(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";

            Console.WriteLine(line);

            var logPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Logs",
                "MoxaBarcodeService.log");

            var directory = Path.GetDirectoryName(logPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch
        {
        }
    }

    private GlobeweighEntities CreateDbContext()
    {
        return new GlobeweighEntities(AppConfig.ConnectionString);
    }
}
