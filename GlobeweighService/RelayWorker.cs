using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GlobeweighService
{
    // Owns the ONE persistent TCP connection to the gate/barrier relay card and exposes it
    // to the HTTP server. Same model as WeighbridgeWorker (one socket, auto-reconnect), so 10+
    // clients hitting the API → API proxies to this service → one connection to the card.
    //
    // Binary protocol ported from the desktop GateBarrierViewModel / API RelayService:
    //   open  = DIG_ACTIVE,  close = DIG_INACTIVE,  followed by channel byte then 0  (3 bytes, 1-byte ack)
    //   state = DIG_GET_OUTPUTS  ->  bit 0 = gate, bit 1 = barrier
    //   gate  = channel 1,  barrier = channel 2
    public class RelayWorker
    {
        private enum Commands : byte
        {
            GET_VER = 0x10,
            DIG_ACTIVE = 0x20, DIG_INACTIVE, DIG_DIRECTION, DIG_SET_OUTPUTS, DIG_GET_OUTPUTS, DIG_GET_INPUTS
        }

        public const int GateChannel = 1;
        public const int BarrierChannel = 2;

        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan ConnectionRecycleInterval = TimeSpan.FromHours(4);

        private readonly string _ip;
        private readonly int _port;
        private readonly bool _enabled;

        private readonly object _sync = new object();   // serialises all device exchanges
        private readonly byte[] _buf = new byte[70];
        private TcpClient _client;
        private NetworkStream _ns;
        private bool _deviceFound;

        // Single shared instance so the HTTP handler can issue commands.
        public static RelayWorker Instance { get; private set; }

        public RelayWorker(string ip, int port, bool enabled)
        {
            _ip = ip;
            _port = port;
            _enabled = enabled;
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            Instance = this;
            Log("Relay worker started");

            if (!_enabled || string.IsNullOrWhiteSpace(_ip) || _port <= 0)
            {
                Log($"Relay disabled or unconfigured (enabled={_enabled}, ip='{_ip}', port={_port}) - worker idle");
                try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); } catch { }
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Log($"Connecting to relay {_ip}:{_port}...");
                    if (!Connect())
                    {
                        RelayState.SetConnected(false);
                        await Task.Delay(ReconnectDelay, stoppingToken);
                        continue;
                    }

                    Log("Connected to relay");
                    RelayState.SetConnected(true);

                    var connectedAtUtc = DateTime.UtcNow;
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        if ((DateTime.UtcNow - connectedAtUtc) >= ConnectionRecycleInterval)
                        {
                            Log("Recycling relay connection after 4 hours");
                            break;
                        }

                        bool ok;
                        lock (_sync)
                        {
                            ok = PollOutputsLocked();
                        }
                        if (!ok)
                        {
                            Log("Relay poll failed - reconnecting");
                            break;
                        }

                        await Task.Delay(PollInterval, stoppingToken);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log("Relay error: " + ex.Message); }
                finally
                {
                    RelayState.SetConnected(false);
                    lock (_sync) { CloseLocked(); }
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        try { await Task.Delay(ReconnectDelay, stoppingToken); } catch { }
                    }
                }
            }

            Log("Relay worker stopped");
        }

        // Called from the HTTP handler thread. Opens/closes a relay and refreshes the state.
        // Returns false if the device isn't currently connected.
        public bool SendCommand(int channel, bool open)
        {
            lock (_sync)
            {
                if (!_deviceFound || _ns == null) return false;
                _buf[0] = (byte)(open ? Commands.DIG_ACTIVE : Commands.DIG_INACTIVE);
                _buf[1] = (byte)channel;
                _buf[2] = 0;
                if (!TxRx(3, 1, 1500)) { CloseLocked(); RelayState.SetConnected(false); return false; }
                PollOutputsLocked();
                return true;
            }
        }

        // ---- connection / protocol (callers other than Connect hold _sync) ----

        private bool Connect()
        {
            lock (_sync)
            {
                CloseLocked();
                try
                {
                    _client = new TcpClient();
                    var ar = _client.BeginConnect(_ip, _port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(3000))
                    {
                        Log("TCP connect timed out (no response from relay card / wrong host or port).");
                        CloseLocked();
                        return false;
                    }
                    _client.EndConnect(ar); // throws here if the card actively refused
                    _client.NoDelay = true;
                    _ns = _client.GetStream();
                    _ns.ReadTimeout = 5000;
                    _ns.WriteTimeout = 5000;

                    Thread.Sleep(300); // settle before first command

                    _buf[0] = (byte)Commands.GET_VER;
                    if (!TxRx(1, 3, 5000))
                    {
                        Log("TCP connected but no GET_VER reply (handshake failed - card busy/another client holding the connection?).");
                        CloseLocked();
                        return false;
                    }

                    _deviceFound = true;
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"TCP connect refused/failed ({_ip}:{_port}): {ex.Message}");
                    CloseLocked();
                    return false;
                }
            }
        }

        private bool PollOutputsLocked()
        {
            if (!_deviceFound || _ns == null) return false;

            _buf[0] = (byte)Commands.DIG_GET_OUTPUTS;
            int outputs;
            if (TxRx(1, 3, 1500))
            {
                outputs = (_buf[2] << 16) + (_buf[1] << 8) + _buf[0];
            }
            else
            {
                _buf[0] = (byte)Commands.DIG_GET_OUTPUTS;
                if (!TxRx(1, 2, 1500)) return false;
                outputs = (_buf[1] << 8) + _buf[0];
            }

            bool gate = (outputs & (1 << 0)) != 0;
            bool barrier = (outputs & (1 << 1)) != 0;
            RelayState.SetOutputs(gate, barrier);
            return true;
        }

        private bool TxRx(byte writeCount, int readCount, int totalTimeoutMs)
        {
            if (_ns == null) return false;
            try
            {
                _ns.Write(_buf, 0, writeCount);
                _ns.Flush();
                if (readCount <= 0) return true;

                int received = 0;
                var sw = Stopwatch.StartNew();
                while (received < readCount && sw.ElapsedMilliseconds < totalTimeoutMs)
                {
                    if (_ns.DataAvailable)
                    {
                        int chunk = _ns.Read(_buf, received, readCount - received);
                        if (chunk == 0) return false;
                        received += chunk;
                        continue;
                    }
                    Thread.Sleep(5);
                }
                return received == readCount;
            }
            catch
            {
                return false;
            }
        }

        private void CloseLocked()
        {
            try { if (_ns != null) _ns.Dispose(); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
            _ns = null;
            _client = null;
            _deviceFound = false;
        }

        private static void Log(string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [RELAY] {message}";
                Console.WriteLine(line);
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Weighbridge.log");
                var directory = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
