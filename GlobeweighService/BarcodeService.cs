using System;
using System.Configuration;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace GlobeweighService
{
    public partial class BarcodeService : ServiceBase
    {
        private CancellationTokenSource _cts;
        private Task _runTask;

        public BarcodeService()
        {
            InitializeComponent();
            ServiceName = "GlobeweighService";
        }

        protected override void OnStart(string[] args)
        {
            _cts = new CancellationTokenSource();

            try
            {
                _runTask = ServiceHost.RunAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(ServiceName,
                    "Startup failure: " + ex,
                    EventLogEntryType.Error);
                throw;
            }
        }

        protected override void OnStop()
        {
            _cts.Cancel();

            try
            {
                if (_runTask != null && !_runTask.Wait(15000))
                {
                    EventLog.WriteEntry(ServiceName,
                        "Workers did not stop gracefully",
                        EventLogEntryType.Warning);
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(ServiceName,
                    "Error during stop: " + ex.Message,
                    EventLogEntryType.Error);
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }

    public static class ServiceHost
    {
        public static async Task RunAsync(CancellationToken stoppingToken)
        {
            var endpoint = await TryLoadWeighbridgeEndpoint();
            var endpoint2 = ReadWeighbridge2();
            var relay = await TryLoadRelayEndpoint();

            int httpPort = ReadHttpPort();

            var barcode = new BarcodeWorker();
            var weighbridge = new WeighbridgeWorker(endpoint.Ip, endpoint.Port, WeighbridgeState.Bridge1, "WB");
            var weighbridge2 = new WeighbridgeWorker(endpoint2.Ip, endpoint2.Port, WeighbridgeState.Bridge2, "WB2");
            var relayWorker = new RelayWorker(relay.Ip, relay.Port, relay.Enabled);
            var http = new WeighbridgeHttpServer(httpPort);
            var print = new PrintWorker();

            var t1 = barcode.RunAsync(stoppingToken);
            var t2 = weighbridge.RunAsync(stoppingToken);
            var t3 = http.RunAsync(stoppingToken);
            var t4 = relayWorker.RunAsync(stoppingToken);
            var t5 = weighbridge2.RunAsync(stoppingToken);
            var t6 = print.RunAsync(stoppingToken);

            await Task.WhenAll(t1, t2, t3, t4, t5, t6);
        }

        // Second weighbridge Moxa comes from App.config ("Weighbridge2" = ip[:port]) rather
        // than the DB Settings row, which only has columns for one bridge. Unset → worker idles.
        private static WeighbridgeEndpoint ReadWeighbridge2()
        {
            var raw = ConfigurationManager.AppSettings["Weighbridge2"];
            if (string.IsNullOrWhiteSpace(raw)) return new WeighbridgeEndpoint { Ip = null, Port = 0 };

            var ip = raw.Trim();
            int port = 4001;
            var colon = ip.LastIndexOf(':');
            if (colon > 0)
            {
                int.TryParse(ip.Substring(colon + 1), out port);
                ip = ip.Substring(0, colon);
            }
            return new WeighbridgeEndpoint { Ip = ip, Port = port };
        }

        private static int ReadHttpPort()
        {
            int port;
            var raw = ConfigurationManager.AppSettings["WeighbridgeListenPort"];
            if (int.TryParse(raw, out port) && port > 0 && port < 65536) return port;
            return 8765;
        }

        private static async Task<WeighbridgeEndpoint> TryLoadWeighbridgeEndpoint()
        {
            try
            {
                return await WeighbridgeSettingsLoader.LoadAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [WB] Failed to load endpoint from DB: {ex.Message}");
                return new WeighbridgeEndpoint { Ip = null, Port = 0 };
            }
        }

        private static async Task<RelayEndpoint> TryLoadRelayEndpoint()
        {
            try
            {
                return await WeighbridgeSettingsLoader.LoadRelayAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [RELAY] Failed to load endpoint from DB: {ex.Message}");
                return new RelayEndpoint { Ip = null, Port = 0, Enabled = false };
            }
        }
    }
}
