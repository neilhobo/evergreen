using System;
using System.Threading;

namespace GlobeweighService
{
    // Latest gate/barrier relay state, written by RelayWorker, read by the HTTP server.
    // Mirrors WeighbridgeState (lock-free, thread-safe).
    public static class RelayState
    {
        private static int _isConnected;
        private static int _gateOpen;
        private static int _barrierOpen;
        private static long _lastUpdateTicks;

        public static bool IsConnected
        {
            get { return Volatile.Read(ref _isConnected) == 1; }
        }

        public static bool GateOpen
        {
            get { return Volatile.Read(ref _gateOpen) == 1; }
        }

        public static bool BarrierOpen
        {
            get { return Volatile.Read(ref _barrierOpen) == 1; }
        }

        public static DateTime LastUpdateUtc
        {
            get { return new DateTime(Interlocked.Read(ref _lastUpdateTicks), DateTimeKind.Utc); }
        }

        public static void SetConnected(bool connected)
        {
            Interlocked.Exchange(ref _isConnected, connected ? 1 : 0);
            if (!connected) Interlocked.Exchange(ref _lastUpdateTicks, DateTime.UtcNow.Ticks);
        }

        public static void SetOutputs(bool gateOpen, bool barrierOpen)
        {
            Interlocked.Exchange(ref _gateOpen, gateOpen ? 1 : 0);
            Interlocked.Exchange(ref _barrierOpen, barrierOpen ? 1 : 0);
            Interlocked.Exchange(ref _lastUpdateTicks, DateTime.UtcNow.Ticks);
        }
    }
}
