using Globeweigh.Model;
using Globeweigh.Shared.Helpers;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Globeweigh.Shared.Services
{
    public interface IScaleService : IDisposable
    {
        event EventHandler<string> WeightChanged;

        string CurrentWeight { get; }
        bool IsConnected { get; }
        DateTime LastUpdateUtc { get; }

        // Establishes or re-establishes the connection if needed
        Task EnsureConnectedAsync();

        // Legacy entry point. Parameters are ignored - the live weight comes
        // from the GlobeweighService HTTP endpoint, discovered from
        // GlobalVariables.WeighbridgeServiceUrl.
        bool Connect(string ipAddress, int port);

        void Disconnect();
    }

    public class ScaleService : IScaleService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

        private readonly object _sync = new object();
        private readonly HttpClient _http;

        private CancellationTokenSource _cts;
        private Task _pollTask;

        public string CurrentWeight { get; private set; }
        public bool IsConnected { get; private set; }
        public DateTime LastUpdateUtc { get; private set; }

        public event EventHandler<string> WeightChanged;

        public ScaleService()
        {
            _http = new HttpClient { Timeout = RequestTimeout };
        }

        public bool Connect(string ipAddress, int port)
        {
            EnsureRunning();
            return true;
        }

        public Task EnsureConnectedAsync()
        {
            EnsureRunning();
            return Task.FromResult(0);
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                if (_cts != null)
                {
                    try { _cts.Cancel(); } catch { }
                    _cts = null;
                }
                IsConnected = false;
            }
        }

        public void Dispose()
        {
            Disconnect();
            try { _http.Dispose(); } catch { }
        }

        private void EnsureRunning()
        {
            lock (_sync)
            {
                if (_cts != null && !_cts.IsCancellationRequested) return;
                _cts = new CancellationTokenSource();
                _pollTask = Task.Run(() => PollLoopAsync(_cts.Token));
            }
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await PollOnceAsync(ct);
                try { await Task.Delay(PollInterval, ct); } catch { }
            }
        }

        private async Task PollOnceAsync(CancellationToken ct)
        {
            string baseUrl = GlobalVariables.WeighbridgeServiceUrl;
            if (string.IsNullOrEmpty(baseUrl))
            {
                IsConnected = false;
                return;
            }

            try
            {
                var url = baseUrl.TrimEnd('/') + "/weighbridge/weight";
                using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        IsConnected = false;
                        return;
                    }

                    var body = await resp.Content.ReadAsStringAsync();
                    var weight = ExtractStringField(body, "weight");
                    var serviceConnected = string.Equals(ExtractRawField(body, "isConnected"), "true",
                        StringComparison.OrdinalIgnoreCase);
                    var ageMsRaw = ExtractRawField(body, "ageMs");

                    long ageMs;
                    if (!long.TryParse(ageMsRaw, out ageMs)) ageMs = -1;

                    var dataFresh = ageMs >= 0 && ageMs <= StaleThreshold.TotalMilliseconds;
                    IsConnected = serviceConnected && dataFresh;

                    if (weight != null && !string.Equals(weight, CurrentWeight, StringComparison.Ordinal))
                    {
                        CurrentWeight = weight;
                        LastUpdateUtc = DateTime.UtcNow;
                        var h = WeightChanged;
                        if (h != null) h(this, weight);
                    }
                    else if (weight != null)
                    {
                        LastUpdateUtc = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                IsConnected = false;
                ErrorLogging.LogError(ex, "ScaleService.PollOnce");
            }
        }

        private static string ExtractStringField(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var pattern = "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            var m = Regex.Match(json, pattern);
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        private static string ExtractRawField(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var pattern = "\"" + Regex.Escape(field) + "\"\\s*:\\s*([^,}\\s]+)";
            var m = Regex.Match(json, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
