using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Model;

namespace GlobeweighService
{
    public class WeighbridgeEndpoint
    {
        public string Ip { get; set; }
        public int Port { get; set; }
    }

    public class RelayEndpoint
    {
        public string Ip { get; set; }
        public int Port { get; set; }
        public bool Enabled { get; set; }
    }

    public static class WeighbridgeSettingsLoader
    {
        public static async Task<WeighbridgeEndpoint> LoadAsync()
        {
            using (var db = new GlobeweighEntities(AppConfig.ConnectionString))
            {
                var row = await db.Settings
                    .Where(s => s.id == 2)
                    .Select(s => new { s.WeighbridgeMoxaIpAddress, s.WeighbridgeMoxaPortNo })
                    .FirstOrDefaultAsync();

                if (row == null) return new WeighbridgeEndpoint { Ip = null, Port = 0 };

                return new WeighbridgeEndpoint
                {
                    Ip = row.WeighbridgeMoxaIpAddress,
                    Port = row.WeighbridgeMoxaPortNo
                };
            }
        }

        public static async Task<RelayEndpoint> LoadRelayAsync()
        {
            using (var db = new GlobeweighEntities(AppConfig.ConnectionString))
            {
                var row = await db.Settings
                    .Where(s => s.id == 2)
                    .Select(s => new { s.GateRelayIpAddress, s.GateRelayPortNo, s.GateEnabled })
                    .FirstOrDefaultAsync();

                if (row == null) return new RelayEndpoint { Ip = null, Port = 0, Enabled = false };

                return new RelayEndpoint
                {
                    Ip = row.GateRelayIpAddress,
                    Port = row.GateRelayPortNo,
                    Enabled = row.GateEnabled
                };
            }
        }
    }
}
