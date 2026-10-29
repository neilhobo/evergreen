using System;
using System.IO;
using System.ServiceModel;
using Globeweigh.Model;

namespace Globeweigh.Model
{
    public static class GlobalVariables
    {
        public static EndpointAddress WcfEndpoint { get; set; }
        public static CustomerSupplier ClientPortalUser { get; set; }
        public static bool InTestMode { get; set; }
        public static GlobeweighUser CurrentGlobeweighUser { get; set; }
        public static string CurrentMsiVersion { get; set; }
        public static string OperaConnectionString { get; set; }
        public static string ConnectionString { get; set; }
        public static string ApiBaseUrl { get; set; }
        public static string WeighbridgeServiceUrl { get; set; }
        public static string SageODBCConnectionString { get; set; }
        public static Device CurrentDevice { get; set; }
        public static string ApplicationClickOnceProgramShortcut { get; set; }
        public static Setting GlobalDbSettings { get; set; }

        public static readonly string CommonApplicationDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Globeweigh\\");


        public static int DeviceId
        {
            get
            {
                if (CurrentDevice != null)
                {
                    return CurrentDevice.id;
                }
                return 0;
            }
        }

        public static int CurrentGlobeweighUserId
        {
            get
            {
                if (CurrentGlobeweighUser != null)
                {
                    return CurrentGlobeweighUser.id;
                }
                return 0;
            }
        }

        public static bool IsGlobeweighAdmin => CurrentGlobeweighUser?.RoleId == (int)eUserRole.GlobeweighAdmin;
        public static bool IsGlobeweighAdminOrAdmin => CurrentGlobeweighUser?.RoleId == (int)eUserRole.GlobeweighAdmin || CurrentGlobeweighUser?.RoleId == (int)eUserRole.OfficeAdmin;
    }

}
