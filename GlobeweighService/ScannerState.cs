using System;
using System.Collections.Generic;
using System.Threading;

namespace GlobeweighService
{
    public enum ScanResult
    {
        Parsed,
        NoRead,
        UnknownCode,
        AlreadyScanned,
        Saved,
        ParseFailed,
        Error
    }

    public class ScanEvent
    {
        public DateTime TimestampUtc { get; set; }
        public string Raw { get; set; }
        public string ProdCd { get; set; }
        public string UniqueCode { get; set; }
        public string Weight { get; set; }
        public ScanResult Result { get; set; }
        public string Message { get; set; }
        public bool ScannerReportedError { get; set; }
    }

    public static class ScannerState
    {
        private const int MaxEvents = 50;

        private static readonly object _lock = new object();
        private static readonly LinkedList<ScanEvent> _events = new LinkedList<ScanEvent>();

        private static int _isConnected;
        private static long _lastConnectionChangeTicks;
        private static long _lastEventTicks;

        public static bool IsConnected
        {
            get { return Volatile.Read(ref _isConnected) == 1; }
        }

        public static DateTime LastConnectionChangeUtc
        {
            get { return new DateTime(Interlocked.Read(ref _lastConnectionChangeTicks), DateTimeKind.Utc); }
        }

        public static DateTime LastEventUtc
        {
            get { return new DateTime(Interlocked.Read(ref _lastEventTicks), DateTimeKind.Utc); }
        }

        public static void SetConnected(bool connected)
        {
            int newValue = connected ? 1 : 0;
            int previous = Interlocked.Exchange(ref _isConnected, newValue);
            if (previous != newValue)
                Interlocked.Exchange(ref _lastConnectionChangeTicks, DateTime.UtcNow.Ticks);
        }

        public static void AddEvent(ScanEvent evt)
        {
            if (evt == null) return;
            if (evt.TimestampUtc == default(DateTime))
                evt.TimestampUtc = DateTime.UtcNow;

            lock (_lock)
            {
                _events.AddFirst(evt);
                while (_events.Count > MaxEvents)
                    _events.RemoveLast();
            }
            Interlocked.Exchange(ref _lastEventTicks, evt.TimestampUtc.Ticks);
        }

        public static ScanEvent[] Snapshot()
        {
            lock (_lock)
            {
                var arr = new ScanEvent[_events.Count];
                int i = 0;
                foreach (var e in _events)
                    arr[i++] = e;
                return arr;
            }
        }
    }
}
