using System.Diagnostics;
using System.Net.Sockets;
using Globeweigh.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

/// <summary>
/// Talks to the gate/barrier relay device over TCP. Ports the binary protocol from the
/// desktop app (Globeweigh.Shared RelayService / GateBarrierViewModel), extended to drive
/// BOTH relays: channel 1 = gate (output bit 0), channel 2 = barrier (output bit 1).
///
/// Singleton: holds one TCP connection and serialises all exchanges with a semaphore, since
/// the device handles one request/response at a time. Relay IP/port/enabled come from the
/// Setting table (read fresh on each connect attempt).
/// </summary>
public sealed class RelayService : IDisposable
{
    private enum Commands : byte
    {
        GET_VER = 0x10,
        DIG_ACTIVE = 0x20, DIG_INACTIVE, DIG_DIRECTION, DIG_SET_OUTPUTS, DIG_GET_OUTPUTS, DIG_GET_INPUTS,
        VAR_SET_OUTPUT = 0x30, VAR_GET_OUTPUT, VAR_GET_INPUT,
        GET_SER_NUM = 0x77, GET_VOLTS, PASSWORD_ENTRY, GET_UNLOCK_TIME, LOG_OUT,
    }

    private const int GateChannel = 1;     // relay 1 => output bit 0
    private const int BarrierChannel = 2;  // relay 2 => output bit 1

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RelayService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly byte[] _buf = new byte[70];

    private TcpClient? _client;
    private NetworkStream? _ns;
    private volatile bool _deviceFound;

    // State cache so that many clients polling /state (10+ admin apps every ~1.5s) don't each
    // become a device round-trip. The relay is read at most once per StateCacheMs; everyone
    // else is served the last reading. Commands (open/close) always run live and refresh it.
    private const int StateCacheMs = 1000;
    private readonly object _cacheLock = new();
    private RelayState? _cachedState;
    private long _cachedAtTick;

    public RelayService(IServiceScopeFactory scopeFactory, ILogger<RelayService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public bool IsConnected => _deviceFound;

    public sealed record RelayState(bool Connected, bool GateOpen, bool BarrierOpen);

    /// <summary>Read the current gate/barrier output states (connecting first if needed).
    /// Coalesced: returns a recent cached reading so concurrent pollers don't each hit the
    /// device. Only one caller per StateCacheMs window actually talks to the relay.</summary>
    public async Task<RelayState> GetStateAsync(CancellationToken ct = default)
    {
        if (TryGetCached(out var cached)) return cached;

        await _gate.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed it while we waited for the semaphore.
            if (TryGetCached(out cached)) return cached;

            RelayState state;
            if (EnsureConnected())
            {
                var (gate, barrier) = ReadOutputs();
                state = new RelayState(_deviceFound, gate, barrier);
            }
            else
            {
                state = new RelayState(false, false, false);
            }
            CacheState(state);
            return state;
        }
        finally { _gate.Release(); }
    }

    private bool TryGetCached(out RelayState state)
    {
        lock (_cacheLock)
        {
            if (_cachedState != null && Environment.TickCount64 - _cachedAtTick < StateCacheMs)
            {
                state = _cachedState;
                return true;
            }
        }
        state = null!;
        return false;
    }

    private void CacheState(RelayState state)
    {
        lock (_cacheLock)
        {
            _cachedState = state;
            _cachedAtTick = Environment.TickCount64;
        }
    }

    /// <summary>Open/close the gate (relay 1).</summary>
    public Task<RelayState> SetGateAsync(bool open, CancellationToken ct = default)
        => SetRelayAsync(GateChannel, open, ct);

    /// <summary>Open/close the barrier (relay 2).</summary>
    public Task<RelayState> SetBarrierAsync(bool open, CancellationToken ct = default)
        => SetRelayAsync(BarrierChannel, open, ct);

    private async Task<RelayState> SetRelayAsync(int channel, bool open, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!EnsureConnected())
                throw new InvalidOperationException("Relay not connected.");

            _buf[0] = (byte)(open ? Commands.DIG_ACTIVE : Commands.DIG_INACTIVE);
            _buf[1] = (byte)channel;
            _buf[2] = 0;
            TxRx(3, 1, 1500); // device acks with 1 byte

            var (gate, barrier) = ReadOutputs();
            var state = new RelayState(_deviceFound, gate, barrier);
            CacheState(state); // make the post-command state immediately visible to pollers
            return state;
        }
        finally { _gate.Release(); }
    }

    // ---- protocol helpers (caller holds _gate) ----

    private bool EnsureConnected()
    {
        if (_deviceFound) return true;

        var (enabled, ip, port) = ReadRelaySettings();
        if (!enabled || string.IsNullOrWhiteSpace(ip) || port <= 0)
        {
            _log.LogWarning("Relay disabled or unconfigured (enabled={Enabled}, ip={Ip}, port={Port}).", enabled, ip, port);
            return false;
        }

        SafeClose();
        try
        {
            _client = new TcpClient();
            var ar = _client.BeginConnect(ip, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(2000))
            {
                SafeClose();
                return false;
            }
            _client.EndConnect(ar);
            _client.NoDelay = true;
            _ns = _client.GetStream();
            _ns.ReadTimeout = 3000;
            _ns.WriteTimeout = 3000;

            Thread.Sleep(300); // settle before first command

            _buf[0] = (byte)Commands.GET_VER;
            if (!TxRx(1, 3, 2000))
            {
                SafeClose();
                return false;
            }

            _deviceFound = true;
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Relay connect failed ({Ip}:{Port}).", ip, port);
            SafeClose();
            return false;
        }
    }

    private (bool gate, bool barrier) ReadOutputs()
    {
        if (!_deviceFound) return (false, false);

        // Some firmware variants return 3 bytes, others 2 — try 3 then fall back.
        _buf[0] = (byte)Commands.DIG_GET_OUTPUTS;
        int outputs;
        if (TxRx(1, 3, 1500))
        {
            outputs = (_buf[2] << 16) + (_buf[1] << 8) + _buf[0];
        }
        else
        {
            _buf[0] = (byte)Commands.DIG_GET_OUTPUTS;
            if (!TxRx(1, 2, 1500))
            {
                SafeClose();
                return (false, false);
            }
            outputs = (_buf[1] << 8) + _buf[0];
        }

        bool gate = (outputs & (1 << 0)) != 0;    // relay 1
        bool barrier = (outputs & (1 << 1)) != 0; // relay 2
        return (gate, barrier);
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

    private (bool enabled, string? ip, int port) ReadRelaySettings()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GbEvergreenContext>();
        var s = db.Settings.AsNoTracking().FirstOrDefault();
        if (s == null) return (false, null, 0);
        return (s.GateEnabled, s.GateRelayIpAddress, s.GateRelayPortNo);
    }

    private void SafeClose()
    {
        try { _ns?.Dispose(); } catch { }
        try { _client?.Close(); } catch { }
        _ns = null;
        _client = null;
        _deviceFound = false;
    }

    public void Dispose() => SafeClose();
}
