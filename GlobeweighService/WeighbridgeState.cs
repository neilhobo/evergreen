using System;
using System.Threading;

namespace GlobeweighService
{
    // One instance per physical weighbridge. Bridge1 is the original (DB-configured Moxa),
    // Bridge2 is the second bridge (App.config "Weighbridge2").
    public class WeighbridgeState
    {
        public static readonly WeighbridgeState Bridge1 = new WeighbridgeState();
        public static readonly WeighbridgeState Bridge2 = new WeighbridgeState();

        private string _weight;
        private long _lastUpdateTicks;
        private int _isConnected;
        private long _lastConnectionChangeTicks;
        private long _dataLostSinceTicks;

        public string Weight
        {
            get { return Volatile.Read(ref _weight); }
        }

        public DateTime LastUpdateUtc
        {
            get { return new DateTime(Interlocked.Read(ref _lastUpdateTicks), DateTimeKind.Utc); }
        }

        public bool IsConnected
        {
            get { return Volatile.Read(ref _isConnected) == 1; }
        }

        public DateTime LastConnectionChangeUtc
        {
            get { return new DateTime(Interlocked.Read(ref _lastConnectionChangeTicks), DateTimeKind.Utc); }
        }

        // When the feed went silent, or null while weights are arriving. Lets callers
        // (and the Gate/Barrier screen) say "no data for 4m" instead of just showing a dash.
        public DateTime? DataLostSinceUtc
        {
            get
            {
                var ticks = Interlocked.Read(ref _dataLostSinceTicks);
                return ticks == 0 ? (DateTime?)null : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        public void UpdateWeight(string weight)
        {
            Volatile.Write(ref _weight, weight);
            Interlocked.Exchange(ref _lastUpdateTicks, DateTime.UtcNow.Ticks);
            Interlocked.Exchange(ref _dataLostSinceTicks, 0);
        }

        // Records when data actually stopped, not when we noticed. CompareExchange so the
        // start time is pinned by the first call and doesn't creep forward on every retry.
        public void MarkDataLost(DateTime lostAtUtc)
        {
            Interlocked.CompareExchange(ref _dataLostSinceTicks, lostAtUtc.Ticks, 0);
        }

        public void SetConnected(bool connected)
        {
            int newValue = connected ? 1 : 0;
            int previous = Interlocked.Exchange(ref _isConnected, newValue);
            if (previous != newValue)
                Interlocked.Exchange(ref _lastConnectionChangeTicks, DateTime.UtcNow.Ticks);
        }
    }
}
