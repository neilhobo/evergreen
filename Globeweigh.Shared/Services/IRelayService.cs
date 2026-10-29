using Globeweigh.Model;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Globeweigh.Shared.Services
{
    public interface IRelayService : IDisposable
    {
        bool IsConnected { get; }
        bool BarrierOpen { get; } // true => relay 2 ON (barrier up/open)

        event EventHandler<bool> BarrierOpenChanged;

        Task EnsureConnectedAsync();              // idempotent
        Task SetBarrierAsync(bool open);          // open=true => relay 2 ON
        Task RefreshBarrierStateAsync();          // read DIG_GET_OUTPUTS and update BarrierOpen
    }

    public sealed class RelayService : IRelayService
    {
        private enum Commands
        {
            GET_VER = 0x10,
            DIG_ACTIVE = 0x20, DIG_INACTIVE, DIG_DIRECTION, DIG_SET_OUTPUTS, DIG_GET_OUTPUTS, DIG_GET_INPUTS,
            VAR_SET_OUTPUT = 0x30, VAR_GET_OUTPUT, VAR_GET_INPUT,
            GET_SER_NUM = 0x77, GET_VOLTS, PASSWORD_ENTRY, GET_UNLOCK_TIME, LOG_OUT,
        }

        private TcpClient _client;
        private NetworkStream _ns;
        private readonly object _sync = new object();
        private volatile bool _deviceFound;
        private readonly byte[] _buf = new byte[70];

        private bool _barrierOpen;

        public bool IsConnected { get { return _deviceFound; } }

        public bool BarrierOpen
        {
            get { return _barrierOpen; }
            private set
            {
                if (_barrierOpen != value)
                {
                    _barrierOpen = value;
                    var h = BarrierOpenChanged;
                    if (h != null) h(this, value);
                }
            }
        }

        public event EventHandler<bool> BarrierOpenChanged;

        public async Task EnsureConnectedAsync()
        {
            if (_deviceFound) return;

            string ip = GlobalVariables.GlobalDbSettings.GateRelayIpAddress;
            int port = GlobalVariables.GlobalDbSettings.GateRelayPortNo;

            lock (_sync)
            {
                if (_client != null) SafeClose();
                _client = new TcpClient();
            }

            try
            {
                IAsyncResult ar = _client.BeginConnect(ip, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(2000))
                {
                    SafeClose();
                    return;
                }
                try { _client.EndConnect(ar); } catch { }

                _client.NoDelay = true;
                _ns = _client.GetStream();
                _ns.ReadTimeout = 3000;
                _ns.WriteTimeout = 3000;

                Thread.Sleep(300); // small settle

                // GET_VER (usually 3 bytes)
                _buf[0] = (byte)Commands.GET_VER;
                if (!TxRx(1, 3, 2000))
                {
                    SafeClose();
                    return;
                }

                _deviceFound = true;

                // Seed from hardware (with adaptive read)
                await RefreshBarrierStateAsync().ConfigureAwait(false);
            }
            catch
            {
                SafeClose();
            }
        }

        public async Task SetBarrierAsync(bool open)
        {
            if (!_deviceFound) throw new InvalidOperationException("Relay not connected.");

            byte channel = 2; // relay 2 controls barrier
            _buf[0] = (byte)(open ? Commands.DIG_ACTIVE : Commands.DIG_INACTIVE);
            _buf[1] = channel;
            _buf[2] = 0;

            if (!TxRx(3, 1, 1500))
            {
                // Try to re-sync once
                await RefreshBarrierStateAsync().ConfigureAwait(false);
                return;
            }

            await RefreshBarrierStateAsync().ConfigureAwait(false);
        }

        public Task RefreshBarrierStateAsync()
        {
            if (!_deviceFound) return Task.FromResult(0);

            // Try 3 bytes first (some variants return 3), then fall back to 2
            _buf[0] = (byte)Commands.DIG_GET_OUTPUTS;

            bool ok = TxRx(1, 3, 1500);
            int outputs = 0;

            if (ok)
            {
                outputs = (_buf[2] << 16) + (_buf[1] << 8) + _buf[0];
            }
            else
            {
                _buf[0] = (byte)Commands.DIG_GET_OUTPUTS;
                ok = TxRx(1, 2, 1500);
                if (!ok)
                {
                    SafeClose();
                    return Task.FromResult(0);
                }
                outputs = (_buf[1] << 8) + _buf[0];
            }

            // Bit 1 => relay 2 (0-based bit index)
            bool barrierIsOpen = (outputs & (1 << 1)) != 0;
            BarrierOpen = barrierIsOpen;

            return Task.FromResult(0);
        }

        private bool TxRx(byte writeCount, int readCount, int totalTimeoutMs)
        {
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

        private void SafeClose()
        {
            try { _ns?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            _ns = null;
            _client = null;
            _deviceFound = false;
        }

        public void Dispose()
        {
            SafeClose();
        }
    }
}
