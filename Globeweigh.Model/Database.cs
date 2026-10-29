using Globeweigh.Model.Custom;
using System;
using Globeweigh.Model.Helpers;
using System.IO;
using System.ServiceModel;

namespace Globeweigh.Model
{
    public static class Database
    {
        public static void SetDatabaseConnectionString()
        {
            GlobeweighConfig cirrusConfig = Utilities.GetConfigData();
            const string defaultConnectionString = "metadata=res://*/Model1.csdl|res://*/Model1.ssdl|res://*/Model1.msl;provider=System.Data.SqlClient;provider connection string=';data source={0};initial catalog={1};user id={2};password={3};multipleactiveresultsets=True;App=EntityFramework';";
            var connectionString = String.Format(defaultConnectionString, cirrusConfig.server, cirrusConfig.database, cirrusConfig.user, cirrusConfig.password);

            GlobalVariables.ConnectionString = connectionString;
            GlobalVariables.SageODBCConnectionString = @"Provider=MSDASQL.1;Persist Security Info=False;DSN=SageLine50v28;uid=globeweigh;pwd=globeweigh;";
            GlobalVariables.WcfEndpoint = new EndpointAddress(new Uri(@"http://EVERGREEN-SQL01/EvergreenWcf/Service1.svc"));

            // Globeweigh.Api the Admin app calls (migrating DB calls over one by one). Live by default.
            GlobalVariables.ApiBaseUrl = "http://192.168.250.5/EvergreenApi";

            if (Utilities.IsMyMachine)
            {
                 GlobalVariables.ConnectionString = GetLOCALConnectionString();
                 GlobalVariables.ApiBaseUrl = "http://localhost:5099";
                //GlobalVariables.ConnectionString = GetClientConnectionString();
                // GlobalVariables.ApiBaseUrl = "http://evergreen/EvergreenApi";
      
            }

            GlobalVariables.WeighbridgeServiceUrl = BuildWeighbridgeServiceUrl(GlobalVariables.ConnectionString);

            // On a dev machine the local DB host resolves to "localhost", where no
            // GlobeweighService runs, so live weight never appears. Pin it to the
            // production service (reachable here over Tailscale via MagicDNS name).
            if (Utilities.IsMyMachine)
            {
                GlobalVariables.WeighbridgeServiceUrl = string.Format("http://{0}:{1}/", DevWeighbridgeServiceHost, WeighbridgeServicePort);
            }
        }

        // Tailscale MagicDNS name of the production Evergreen box (resolves from dev machines).
        private const string DevWeighbridgeServiceHost = "evergreen";
        private const int WeighbridgeServicePort = 8765;

        private static string BuildWeighbridgeServiceUrl(string connectionString)
        {
            var host = ExtractSqlHost(connectionString);
            if (string.IsNullOrEmpty(host)) host = "localhost";
            return string.Format("http://{0}:{1}/", host, WeighbridgeServicePort);
        }

        private static string ExtractSqlHost(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return null;

            const string key = "data source=";
            int start = connectionString.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;
            start += key.Length;

            int end = connectionString.IndexOfAny(new[] { ';', '\'', '"' }, start);
            if (end < 0) end = connectionString.Length;

            var dataSource = connectionString.Substring(start, end - start).Trim();
            int slash = dataSource.IndexOf('\\');
            var host = slash > 0 ? dataSource.Substring(0, slash) : dataSource;

            int comma = host.IndexOf(',');
            if (comma > 0) host = host.Substring(0, comma);

            host = host.Trim();
            if (string.Equals(host, "(local)", StringComparison.OrdinalIgnoreCase)) host = "localhost";
            if (host == ".") host = "localhost";
            return host;
        }

        public static string GetLOCALConnectionString()
        {
            //            GlobalVariables.InTestMode = true;
            const string defaultLocalConnectionString = "metadata=res://*/Model1.csdl|res://*/Model1.ssdl|res://*/Model1.msl;provider=System.Data.SqlClient;provider connection string=';data source={0};initial catalog={1};integrated security=True;MultipleActiveResultSets=True;App=EntityFramework';";
            var cirrusConfig = new GlobeweighConfig();
            cirrusConfig.server = @"(local)\SQLEXPRESS";
            cirrusConfig.database = "GbEvergreen";
            var connectionString = String.Format(defaultLocalConnectionString, cirrusConfig.server, cirrusConfig.database);
            return connectionString;
        }


        public static string GetClientConnectionString()
        {
            const string defaultConnectionString = "metadata=res://*/Model1.csdl|res://*/Model1.ssdl|res://*/Model1.msl;provider=System.Data.SqlClient;provider connection string=';data source={0};initial catalog={1};user id={2};password={3};multipleactiveresultsets=True;App=EntityFramework';";
            var cirrusConfig = new GlobeweighConfig();
            cirrusConfig.server = @"evergreen\NTPRO";
            cirrusConfig.database = "GbEvergreen";
            cirrusConfig.user = "globeweigh";
            cirrusConfig.password = "ButternutSquash09";
            var connectionString = String.Format(defaultConnectionString, cirrusConfig.server, cirrusConfig.database, cirrusConfig.user, cirrusConfig.password);
            return connectionString;
        }

        public static bool SetAndTestDbConnection()
        {
            SetDatabaseConnectionString();

            using (var db = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var conn = db.Database.Connection;
                try
                {
                    conn.Open();   // check the database connection
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }
    }
}
