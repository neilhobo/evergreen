using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Text;

namespace Globeweigh.SageSidecar
{
    public enum QuantityBasis { Pallets, Units }

    // Everything site-specific, read once from Globeweigh.SageSidecar.exe.config (App.config
    // in the source tree documents each key). Nothing is validated on load: Problems() says
    // what is missing, so the worker can refuse to start with a clear message rather than
    // fail on the first job.
    public sealed class SidecarConfig
    {
        public string ApiBaseUrl { get; set; }
        public string SidecarKey { get; set; }

        public string SageDataPath { get; set; }
        public string SageUsername { get; set; }
        public string SagePassword { get; set; }
        public string SageWorkspaceName { get; set; }

        // ---- Where exported orders go ----
        // Every export job names a target, Test or Live. SageDataPath above is the LIVE
        // company: the mirrors (customers, sales) are always read from it, but an order is
        // only ever WRITTEN to it when the job says Live AND LiveExportEnabled is true here.
        // Test jobs go to this second company - the demo data, or a copy of the live one.
        public string TestSageDataPath { get; set; }
        public string TestSageUsername { get; set; }
        public string TestSagePassword { get; set; }
        // Test only: an account to post to when the order's own customer is not in the test
        // company (the demo data knows none of Evergreen's). Blank = such an order fails.
        public string TestFallbackAccountRef { get; set; }
        public bool LiveExportEnabled { get; set; }

        public const string TargetTest = "Test";
        public const string TargetLive = "Live";

        // The test company is offered when it is set and is NOT the live one.
        public bool TestExportAvailable
        {
            get { return !string.IsNullOrWhiteSpace(TestSageDataPath) && !SamePath(TestSageDataPath, SageDataPath); }
        }

        // What this sidecar tells the API it can post into; the API hands out nothing else.
        public List<string> ExportTargets()
        {
            var t = new List<string>();
            if (TestExportAvailable) t.Add(TargetTest);
            if (LiveExportEnabled) t.Add(TargetLive);
            return t;
        }

        private static bool SamePath(string a, string b)
        {
            Func<string, string> norm = p => (p ?? "").Trim().TrimEnd('\\', '/').Replace('/', '\\').ToUpperInvariant();
            return norm(a) == norm(b);
        }

        public QuantityBasis QuantityBasis { get; set; }
        public string ReferenceField { get; set; }
        public string NonStockCode { get; set; }
        public string DefaultNominalCode { get; set; }
        public short DefaultTaxCode { get; set; }
        public Dictionary<short, double> TaxRates { get; set; }
        public string TakenBy { get; set; }
        public int DuplicateScanDepth { get; set; }

        public bool Enabled { get; set; }
        public TimeSpan Poll { get; set; }
        public TimeSpan ErrorBackoff { get; set; }
        // Re-read the Sage customer ledger for the API's mirror this often; 0 = only on request.
        public int CustomerSyncMinutes { get; set; }
        // Re-read the sales audit trail + invoice lines (the Reports pages) this often; 0 = on request.
        public int SalesSyncMinutes { get; set; }
        // How far back the sales mirror reaches: from 1 January, this many years ago (1 = last
        // year and this year, so a year-to-date report can be compared with the last one).
        public int SalesHistoryYears { get; set; }
        // ...but never later than this date, whatever the year: the Active Customers report
        // looks back to 1 Jan 2025 by default, so that year must survive New Year 2027.
        // Missing/empty in the config = 2025-01-01; "none" = no floor (the years window alone).
        public DateTime? SalesEarliestFrom { get; set; }
        // Reading the audit trail newest-first, stop after this many consecutive entries older
        // than the window; 0 = read every entry. (The invoice file is always read whole.)
        public int SalesScanStopRun { get; set; }

        public DateTime SalesHistoryFrom
        {
            get
            {
                var byYears = new DateTime(DateTime.Today.Year - Math.Max(0, SalesHistoryYears), 1, 1);
                return SalesEarliestFrom.HasValue && SalesEarliestFrom.Value.Date < byYears ? SalesEarliestFrom.Value.Date : byYears;
            }
        }

        public string BaseDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public string StateDir { get { return Path.Combine(BaseDir, "state"); } }
        public string LogDir { get { return Path.Combine(BaseDir, "Logs"); } }

        public static SidecarConfig Load()
        {
            var c = new SidecarConfig();
            c.ApiBaseUrl = Read("ApiBaseUrl", "http://evergreen:5099");
            c.SidecarKey = Read("SidecarKey", "");

            c.SageDataPath = Read("SageDataPath", "");
            c.SageUsername = Read("SageUsername", "manager");
            c.SagePassword = Read("SagePassword", "");
            c.SageWorkspaceName = Read("SageWorkspaceName", "Globeweigh");

            c.TestSageDataPath = Read("TestSageDataPath", "");
            c.TestSageUsername = Read("TestSageUsername", "manager");
            c.TestSagePassword = Read("TestSagePassword", "");
            c.TestFallbackAccountRef = Read("TestFallbackAccountRef", "").ToUpperInvariant();
            c.LiveExportEnabled = ReadBool("LiveExportEnabled", false);

            // Units by default: Evergreen's unit prices are per bag (see App.config).
            c.QuantityBasis = string.Equals(Read("QuantityBasis", "Units"), "Pallets", StringComparison.OrdinalIgnoreCase)
                ? QuantityBasis.Pallets : QuantityBasis.Units;
            c.ReferenceField = Read("ReferenceField", "ANALYSIS_1").ToUpperInvariant();
            c.NonStockCode = Read("NonStockCode", "S1");
            c.DefaultNominalCode = Read("DefaultNominalCode", "4000");
            c.DefaultTaxCode = (short)ReadInt("DefaultTaxCode", 1);
            c.TaxRates = ParseRates(Read("TaxRates", "0:0;1:20;2:5;9:0"));
            c.TakenBy = Read("TakenBy", "Globeweigh");
            c.DuplicateScanDepth = ReadInt("DuplicateScanDepth", 500);

            c.Enabled = ReadBool("Enabled", true);
            c.Poll = TimeSpan.FromSeconds(Math.Max(2, ReadInt("PollSeconds", 10)));
            c.ErrorBackoff = TimeSpan.FromSeconds(Math.Max(5, ReadInt("ErrorBackoffSeconds", 30)));
            c.CustomerSyncMinutes = Math.Max(0, ReadInt("CustomerSyncMinutes", 60));
            c.SalesSyncMinutes = Math.Max(0, ReadInt("SalesSyncMinutes", 180));
            c.SalesHistoryYears = Math.Max(0, ReadInt("SalesHistoryYears", 1));
            c.SalesScanStopRun = Math.Max(0, ReadInt("SalesScanStopRun", 3000));
            DateTime earliest;
            c.SalesEarliestFrom = DateTime.TryParseExact(Read("SalesEarliestFrom", "2025-01-01"), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out earliest) ? earliest : (DateTime?)null;
            return c;
        }

        // What stops the worker from starting. Empty = good to go.
        public IEnumerable<string> Problems()
        {
            if (string.IsNullOrWhiteSpace(ApiBaseUrl) || !Uri.IsWellFormedUriString(ApiBaseUrl, UriKind.Absolute))
                yield return "ApiBaseUrl is not an absolute URL.";
            if (string.IsNullOrWhiteSpace(SidecarKey))
                yield return "SidecarKey is empty - copy Sage:SidecarKey from the API's appsettings.json on the server.";
            foreach (var p in SageProblems()) yield return p;
            if (!string.IsNullOrWhiteSpace(TestSageDataPath) && SamePath(TestSageDataPath, SageDataPath))
                yield return "TestSageDataPath is the same folder as SageDataPath - the test company must be a different one from the live company.";
            if (ReferenceField.Length > 0 && ReferenceField != "ANALYSIS_1" && ReferenceField != "ANALYSIS_2" && ReferenceField != "ANALYSIS_3")
                yield return "ReferenceField must be ANALYSIS_1, ANALYSIS_2, ANALYSIS_3 or blank.";
        }

        public IEnumerable<string> SageProblems()
        {
            if (string.IsNullOrWhiteSpace(SageDataPath))
                yield return "SageDataPath is empty - it must be the company's ACCDATA folder.";
            else if (!Directory.Exists(SageDataPath))
                yield return "SageDataPath '" + SageDataPath + "' cannot be seen from this account (does it exist? does the service account have rights to it?).";
            if (string.IsNullOrWhiteSpace(SageUsername))
                yield return "SageUsername is empty.";
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("  api        : " + ApiBaseUrl + (string.IsNullOrEmpty(SidecarKey) ? "   *** NO KEY ***" : ""));
            sb.AppendLine("  sage data  : " + (string.IsNullOrEmpty(SageDataPath) ? "*** NOT SET ***" : SageDataPath));
            sb.AppendLine("  sage user  : " + SageUsername + (string.IsNullOrEmpty(SagePassword) ? " (no password)" : ""));
            sb.AppendLine("  exports    : LIVE company " + (LiveExportEnabled ? "*** ENABLED ***" : "off (LiveExportEnabled=false)")
                + "; TEST company " + (TestExportAvailable
                    ? TestSageDataPath + " as " + TestSageUsername
                        + (TestFallbackAccountRef.Length > 0 ? ", unknown customers -> " + TestFallbackAccountRef : "")
                    : "not set (TestSageDataPath)"));
            sb.AppendLine("  quantity   : " + QuantityBasis + ", reference in " + (ReferenceField.Length == 0 ? "(none)" : ReferenceField)
                + ", non-stock as " + (string.IsNullOrEmpty(NonStockCode) ? "(fail)" : NonStockCode)
                + " / N" + DefaultNominalCode + " / T" + DefaultTaxCode);
            sb.AppendLine("  poll       : every " + Poll.TotalSeconds + "s, back off " + ErrorBackoff.TotalSeconds + "s, scan depth " + DuplicateScanDepth);
            sb.AppendLine("  customers  : mirror to the API " + (CustomerSyncMinutes > 0 ? "every " + CustomerSyncMinutes + " min and on request" : "on request only"));
            sb.Append("  sales      : mirror from " + SalesHistoryFrom.ToString("yyyy-MM-dd") + " to the API "
                + (SalesSyncMinutes > 0 ? "every " + SalesSyncMinutes + " min and on request" : "on request only")
                + (SalesScanStopRun > 0 ? ", audit trail stops after " + SalesScanStopRun + " older entries" : ", full audit-trail scans"));
            return sb.ToString();
        }

        private static string Read(string key, string fallback)
        {
            var v = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }

        private static int ReadInt(string key, int fallback)
        {
            int i;
            return int.TryParse(ConfigurationManager.AppSettings[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : fallback;
        }

        private static bool ReadBool(string key, bool fallback)
        {
            bool b;
            return bool.TryParse(ConfigurationManager.AppSettings[key], out b) ? b : fallback;
        }

        // "0:0;1:20;2:5" -> {0:0, 1:20, 2:5}
        private static Dictionary<short, double> ParseRates(string raw)
        {
            var d = new Dictionary<short, double>();
            foreach (var pair in raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split(':');
                short code; double rate;
                if (kv.Length == 2
                    && short.TryParse(kv[0].Trim(), out code)
                    && double.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out rate))
                    d[code] = rate;
            }
            return d;
        }
    }
}
