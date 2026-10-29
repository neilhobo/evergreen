using System;
using System.ServiceProcess;
using System.Threading;

namespace Globeweigh.SageSidecar
{
    // Runs as a Windows service (install-service.ps1) or, started from a console, in the
    // foreground — the same worker either way — plus one-shot checks for the install:
    //
    //   --ping                                  can we reach the API with this key?
    //   --test-sage [--account X] [--stock Y]   connect to Sage, report the company, look up
    //                                           a customer / a stock code (no writes)
    //   --post-file <payload.json>              post ONE order from a file — a real post.
    //                                           Save GET /api/sage/orders/{id}/payload to get one.
    //
    // --test-sage and --post-file accept --data <ACCDATA path> --user <u> --password <p> to
    // try settings before putting them in the config.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var cfg = SidecarConfig.Load();
            Log.Init(cfg.LogDir);

            // Switches win over the service entry point. A scheduled task or a remote shell is
            // not "interactive" either, and those are exactly where the diagnostics get run on
            // the Sage machine; only a bare, non-interactive start is the Windows service.
            if (Has(args, "--help") || Has(args, "-?") || Has(args, "/?"))
            {
                Usage();
                return 0;
            }
            if (Has(args, "--ping"))
                return Diagnostics.Ping(cfg);
            if (Has(args, "--test-sage"))
                return Diagnostics.TestSage(cfg, Arg(args, "--data"), Arg(args, "--user"), Arg(args, "--password"),
                    Arg(args, "--account"), Arg(args, "--stock"),
                    ArgInt(args, "--list-customers", 0), ArgInt(args, "--list-stock", 0), Has(args, "--list-tax"), Arg(args, "--out"));
            if (Has(args, "--post-file"))
                return Diagnostics.PostFile(cfg, Arg(args, "--post-file"), Arg(args, "--data"), Arg(args, "--user"),
                    Arg(args, "--password"), Has(args, "--yes"));
            if (Has(args, "--sales-snapshot"))
            {
                var fromArg = Arg(args, "--from");
                DateTime fromDate;
                DateTime? from = fromArg != null && DateTime.TryParseExact(fromArg, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out fromDate) ? fromDate : (DateTime?)null;
                return Diagnostics.SalesSnapshot(cfg, Arg(args, "--data"), Arg(args, "--user"), Arg(args, "--password"),
                    from, ArgInt(args, "--stop-run", cfg.SalesScanStopRun), Arg(args, "--out"), Has(args, "--push"));
            }
            if (Has(args, "--probe-invoice"))
                return Diagnostics.ProbeInvoices(cfg, Arg(args, "--data"), Arg(args, "--user"), Arg(args, "--password"), Arg(args, "--probe-invoice"));
            if (Has(args, "--probe-ledger"))
                return Diagnostics.ProbeLedger(cfg, Arg(args, "--data"), Arg(args, "--user"), Arg(args, "--password"), Arg(args, "--probe-ledger"));
            if (Has(args, "--report-sales"))
            {
                var from = ArgDate(args, "--from", new DateTime(DateTime.Today.Year, 1, 1));
                var to = ArgDate(args, "--to", DateTime.Today);
                var accounts = (Arg(args, "--accounts") ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                return Diagnostics.ReportSales(cfg, Arg(args, "--data"), Arg(args, "--user"), Arg(args, "--password"),
                    from, to, accounts, Has(args, "--all-lines"), Has(args, "--scan-all"), Arg(args, "--out"));
            }

            if (!Environment.UserInteractive && !Has(args, "--console"))
            {
                ServiceBase.Run(new SidecarService(cfg));
                return 0;
            }

            Console.Title = "Globeweigh Sage Sidecar";
            Console.WriteLine("Console mode - Ctrl+C to stop. (--help lists the diagnostic switches)");
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            var crashed = false;
            SidecarHost.Start(cfg, cts.Token, ex => { crashed = true; }).Join();
            return crashed ? 1 : 0;
        }

        private static bool Has(string[] args, string flag)
        {
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The token after a flag, or null when the flag is absent or is followed by another
        // flag (PowerShell drops empty-string arguments, so `--password ""` arrives as a bare
        // `--password`; it must not swallow the next switch as the password).
        private static string Arg(string[] args, string flag)
        {
            for (var i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1].StartsWith("--", StringComparison.Ordinal) ? null : args[i + 1];
            return null;
        }

        private static DateTime ArgDate(string[] args, string flag, DateTime fallback)
        {
            var v = Arg(args, flag);
            DateTime d;
            return v != null && DateTime.TryParseExact(v, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out d) ? d : fallback;
        }

        private static int ArgInt(string[] args, string flag, int fallback)
        {
            if (!Has(args, flag)) return fallback;
            int n;
            var v = Arg(args, flag);
            if (v == null) return 25;                      // flag given without a number
            return int.TryParse(v, out n) ? n : 25;
        }

        private static void Usage()
        {
            Console.WriteLine("Globeweigh.SageSidecar - posts Evergreen sales orders into Sage 50 Accounts.");
            Console.WriteLine();
            Console.WriteLine("  (no switches)              run the worker in this console (Ctrl+C stops it)");
            Console.WriteLine("  --console                  same, forced (e.g. from a scheduled task)");
            Console.WriteLine("  --ping                     check the API URL and key in the config");
            Console.WriteLine("  --test-sage                connect to Sage and report the company; add");
            Console.WriteLine("        --account <ref>      ... a customer to look up");
            Console.WriteLine("        --stock <code>       ... a stock code to look up");
            Console.WriteLine("        --list-customers [N] ... the first N customers (-1 = all) and their");
            Console.WriteLine("                                 delivery addresses, plus every field of one record");
            Console.WriteLine("        --list-stock [N]     ... the first N stock records (-1 = all)");
            Console.WriteLine("        --list-tax           ... every tax code with the rate Sage reports for it");
            Console.WriteLine("        --out <file.json>    ... write what was read to a JSON file");
            Console.WriteLine("  --post-file <json>         post one order from a saved payload (a REAL post)");
            Console.WriteLine("        --yes                ... skip the confirmation (non-interactive runs)");
            Console.WriteLine("  --sales-snapshot [--out <json>] [--push] [--from yyyy-MM-dd] [--stop-run N]");
            Console.WriteLine("                             the sales mirror (audit trail SI/SC + invoice lines from");
            Console.WriteLine("                             SalesHistoryYears) to a file and/or to the API now (read-only)");
            Console.WriteLine("  --probe-invoice N[,N]      one invoice by number with its lines (or the order behind it)");
            Console.WriteLine("  --probe-ledger <ref>       an account's audit trail, newest invoices and orders");
            Console.WriteLine("  --report-sales --out <json> [--from yyyy-MM-dd] [--to yyyy-MM-dd]");
            Console.WriteLine("        --accounts A,B,C     invoices/credits in the window (lines for these accounts),");
            Console.WriteLine("        [--all-lines]        plus their ledger entries, to a JSON file (read-only)");
            Console.WriteLine();
            Console.WriteLine("  --test-sage / --post-file also take --data <ACCDATA> --user <u> --password <p>");
            Console.WriteLine("  to try Sage settings before putting them in Globeweigh.SageSidecar.exe.config.");
        }
    }
}
