using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GlobeweighService
{
    public class WeighbridgeWorker
    {
        private readonly string _ip;
        private readonly int _port;
        private readonly WeighbridgeState _state;
        private readonly string _logTag;

        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ConnectionRecycleInterval = TimeSpan.FromHours(4);
        private static readonly TimeSpan StaleDataTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan OutageReminderInterval = TimeSpan.FromMinutes(5);

        // While the feed is down the connect/recycle cycle repeats every ~20s and buries the
        // one line that matters. During an outage we log a single DATA LOST line, then only a
        // reminder every 5 minutes, then DATA RESTORED with the duration.
        private bool _inOutage;
        private DateTime _outageStartedUtc;
        private DateTime _lastOutageNoticeUtc;
        private string _lastSuppressedError;

        public WeighbridgeWorker(string ip, int port, WeighbridgeState state, string logTag)
        {
            _ip = ip;
            _port = port;
            _state = state;
            _logTag = logTag;
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            Log("Weighbridge worker started");

            if (string.IsNullOrWhiteSpace(_ip) || _port <= 0)
            {
                Log($"Weighbridge endpoint not configured (ip='{_ip}', port={_port}) - worker idle");
                try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); } catch { }
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                using (var client = new TcpClient())
                {
                    try
                    {
                        LogRoutine($"Connecting to weighbridge {_ip}:{_port}...");
                        await client.ConnectAsync(_ip, _port);

                        EnableKeepAlive(client.Client, 60000, 10000);

                        LogRoutine("Connected to weighbridge");
                        _state.SetConnected(true);

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
                                        $"Heartbeat - connected, last data {(DateTime.UtcNow - lastDataAtUtc).TotalSeconds:0} second(s) ago");
                                    lastHeartbeatAtUtc = DateTime.UtcNow;
                                }

                                if ((DateTime.UtcNow - connectedAtUtc) >= ConnectionRecycleInterval)
                                {
                                    Log($"Recycling weighbridge connection after {ConnectionRecycleInterval.TotalHours:0} hours");
                                    break;
                                }

                                var readTask = stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                                var completed = await Task.WhenAny(readTask, Task.Delay(StaleDataTimeout, stoppingToken));
                                if (completed != readTask)
                                {
                                    // Shutdown also trips this branch - don't report it as an outage.
                                    if (stoppingToken.IsCancellationRequested) break;
                                    BeginOrContinueOutage(lastDataAtUtc);
                                    LogRoutine($"No data for {StaleDataTimeout.TotalSeconds:0}s - recycling");
                                    break;
                                }

                                int bytesRead = await readTask;
                                if (bytesRead == 0)
                                {
                                    LogRoutine("Weighbridge connection closed by remote host");
                                    break;
                                }

                                lastDataAtUtc = DateTime.UtcNow;

                                string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                                messageBuffer.Append(chunk);

                                ProcessBuffer(messageBuffer);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (IOException ex)
                    {
                        BeginOrContinueOutage(DateTime.UtcNow);
                        LogError("I/O error: " + ex.Message);
                    }
                    catch (SocketException ex)
                    {
                        BeginOrContinueOutage(DateTime.UtcNow);
                        LogError($"Socket error ({ex.SocketErrorCode}): {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        BeginOrContinueOutage(DateTime.UtcNow);
                        LogError("Connection error: " + ex);
                    }
                    finally
                    {
                        _state.SetConnected(false);
                        try { if (client.Connected) client.Close(); } catch { }

                        if (!stoppingToken.IsCancellationRequested)
                        {
                            LogRoutine($"Disconnected. Retrying in {ReconnectDelay.TotalSeconds:0} seconds...");
                            try { await Task.Delay(ReconnectDelay, stoppingToken); } catch { }
                        }
                    }
                }
            }

            Log("Weighbridge worker stopped");
        }

        private void ProcessBuffer(StringBuilder messageBuffer)
        {
            // The Moxa typically delivers fixed-length frames at ~10 Hz. The original VM
            // logic took the full buffer and read Substring(1, 10). We do the same per
            // chunk here, but only when we have at least 23 chars (guard from VM code).
            var data = messageBuffer.ToString();

            int newlineIndex;
            while ((newlineIndex = IndexOfFrameTerminator(data)) >= 0)
            {
                var frame = data.Substring(0, newlineIndex);
                data = data.Substring(newlineIndex + 1);
                TryUpdateFromFrame(frame);
            }

            if (data.Length >= 23)
            {
                TryUpdateFromFrame(data);
                data = string.Empty;
            }

            if (data.Length > 4096) data = string.Empty;

            messageBuffer.Clear();
            messageBuffer.Append(data);
        }

        // Called every time we give up on a silent connection. Only the first call in an
        // outage logs; later ones just tick the 5-minute reminder.
        private void BeginOrContinueOutage(DateTime lastDataAtUtc)
        {
            var now = DateTime.UtcNow;

            if (!_inOutage)
            {
                _inOutage = true;
                _outageStartedUtc = lastDataAtUtc;
                _lastOutageNoticeUtc = now;
                _lastSuppressedError = null;
                _state.MarkDataLost(lastDataAtUtc);
                Log($"*** DATA LOST - no weight data since {lastDataAtUtc.ToLocalTime():HH:mm:ss}. " +
                    "Reconnect logging suppressed until restored. ***");
                return;
            }

            if ((now - _lastOutageNoticeUtc) >= OutageReminderInterval)
            {
                _lastOutageNoticeUtc = now;
                Log($"*** STILL NO DATA after {FormatDuration(now - _outageStartedUtc)} " +
                    $"(reconnecting to {_ip}:{_port} every {ReconnectDelay.TotalSeconds:0}s) ***");
            }
        }

        private void EndOutage()
        {
            if (!_inOutage) return;
            _inOutage = false;
            _lastSuppressedError = null;
            Log($"*** DATA RESTORED after {FormatDuration(DateTime.UtcNow - _outageStartedUtc)} ***");
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s";
            if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m {span.Seconds}s";
            return $"{span.Seconds}s";
        }

        private static int IndexOfFrameTerminator(string data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == '\n' || data[i] == '\r') return i;
            }
            return -1;
        }

        private void TryUpdateFromFrame(string frame)
        {
            if (string.IsNullOrEmpty(frame) || frame.Length < 23) return;
            try
            {
                string weight = frame.Substring(1, 10).Trim();
                _state.UpdateWeight(weight);
                EndOutage();
            }
            catch (Exception ex)
            {
                Log("Frame parse error: " + ex.Message);
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
            }
            catch (Exception ex)
            {
                Log("Failed to enable TCP keepalive: " + ex.Message);
            }
        }

        // Per-cycle chatter: useful normally, pure noise once we know the feed is down.
        private void LogRoutine(string message)
        {
            if (_inOutage) return;
            Log(message);
        }

        // Errors repeat every retry while a bridge is down - log each distinct one once.
        private void LogError(string message)
        {
            if (_inOutage && message == _lastSuppressedError) return;
            _lastSuppressedError = message;
            Log(message);
        }

        private void Log(string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{_logTag}] {message}";
                Console.WriteLine(line);

                var logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Logs",
                    "Weighbridge.log");

                var directory = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
