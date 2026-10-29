using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

namespace Globeweigh.SageSidecar
{
    // The one-shot switches in Program: proving each half of the setup on the Sage machine
    // before the service is left to run, and reading what Sage holds (customers, delivery
    // addresses, stock) without writing anything.
    internal static class Diagnostics
    {
        // When listing "all", the console table stops here; the JSON (--out) has everything.
        private const int ConsoleRows = 40;

        public static int Ping(SidecarConfig cfg)
        {
            try
            {
                using (var api = new ApiClient(cfg))
                {
                    var r = api.Ping();
                    Console.WriteLine("OK - " + cfg.ApiBaseUrl + " answered (server time " + r.Utc.ToLocalTime() + ")");
                    Console.WriteLine("     automatic export on the server is " + (r.AutoExportEnabled ? "ON" : "OFF (only manual exports will be queued)"));
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED - " + ex.Message);
                return 1;
            }
        }

        public static int TestSage(SidecarConfig cfg, string data, string user, string password,
            string account, string stock, int listCustomers, int listStock, bool listTax, string outFile)
        {
            Override(cfg, data, user, password);
            foreach (var p in cfg.SageProblems()) Console.WriteLine("  ! " + p);

            return RunSta(() =>
            {
                using (var w = new SageSopWriter(cfg))
                {
                    Console.WriteLine("Connecting to " + cfg.SageDataPath + " as '" + cfg.SageUsername + "' ...");
                    w.Connect();
                    Console.WriteLine("OK - " + w.Describe());
                    Console.WriteLine("     next sales order number would be " + w.NextOrderNumber());
                    if (!string.IsNullOrEmpty(account)) Console.WriteLine("     " + w.DescribeCustomer(account));
                    if (!string.IsNullOrEmpty(stock)) Console.WriteLine("     " + w.DescribeStock(stock));

                    var doc = new Dictionary<string, object>();
                    doc["company"] = w.Describe();
                    doc["readAt"] = DateTime.Now;
                    if (listTax)
                    {
                        var taxes = w.ListTaxCodes();
                        Console.WriteLine();
                        Console.WriteLine("Tax codes Sage will Load(): " + taxes.Count);
                        Console.WriteLine("{0,-5} {1,6} {2,8} {3,-40} {4}", "Code", "Id", "Rate %", "Name", "loaded by");
                        foreach (var t in taxes)
                            Console.WriteLine("T{0,-4} {1,6} {2,8:N2} {3,-40} {4}", t.Code, t.TaxCodeId, t.Rate, Cut(t.Name, 40), t.LoadedBy);
                        doc["taxCodes"] = taxes;
                    }
                    if (listCustomers != 0) ListCustomers(w, listCustomers, doc);
                    if (listStock != 0) ListStock(w, listStock, doc);

                    if (!string.IsNullOrEmpty(outFile) && (listCustomers != 0 || listStock != 0 || listTax))
                    {
                        File.WriteAllText(outFile, JsonConvert.SerializeObject(doc, Formatting.Indented));
                        Console.WriteLine();
                        Console.WriteLine("Written: " + outFile);
                    }
                }
            });
        }

        // Read-only: the customer ledger, the delivery-address book, and one record in full.
        private static void ListCustomers(SageSopWriter w, int max, Dictionary<string, object> doc)
        {
            var total = w.CountCustomers();
            var customers = w.ListCustomers(max);
            var show = max > 0 ? customers.Count : Math.Min(customers.Count, ConsoleRows);
            Console.WriteLine();
            Console.WriteLine("Customers: " + total + " in Sage, " + customers.Count + " read" + (show < customers.Count ? ", first " + show + " shown" : ""));
            Console.WriteLine("{0,-8} {1,-30} {2,-26} {3,-16} {4,-13} {5,-4} {6,10}", "A/C", "Name", "Address 1", "Town", "Telephone", "Tax", "Balance");
            for (var i = 0; i < show; i++)
            {
                var c = customers[i];
                Console.WriteLine("{0,-8} {1,-30} {2,-26} {3,-16} {4,-13} T{5,-3} {6,10:N2}{7}",
                    c.AccountRef, Cut(c.Name, 30), Cut(c.Address1, 26), Cut(c.Address3, 16), Cut(c.Telephone, 13),
                    c.DefTaxCode, c.Balance, c.OnHold ? "  ON HOLD" : "");
            }

            var addresses = w.ListAddresses(max > 0 ? max * 4 : 0);
            show = max > 0 ? addresses.Count : Math.Min(addresses.Count, ConsoleRows);
            Console.WriteLine();
            Console.WriteLine("Delivery addresses (CustomerAddresses): " + addresses.Count + " read" + (show < addresses.Count ? ", first " + show + " shown" : ""));
            Console.WriteLine("{0,-8} {1,3} {2,-24} {3,-9} {4,-26} {5,-16} {6,-13}", "A/C", "#", "Name", "Type", "Address 1", "Town", "Telephone");
            for (var i = 0; i < show; i++)
            {
                var a = addresses[i];
                Console.WriteLine("{0,-8} {1,3} {2,-24} {3,-9} {4,-26} {5,-16} {6,-13}",
                    a.AccountRef, a.AddressNumber, Cut(a.Name, 24), Cut(a.Type.Replace("sdo", ""), 9), Cut(a.Address1, 26), Cut(a.Address3, 16), Cut(a.Telephone, 13));
            }

            var fields = w.FirstCustomerFields();
            Console.WriteLine();
            Console.WriteLine("Every field on the first customer record (" + fields.Count + "):");
            foreach (var kv in fields)
                if (kv.Value.Length > 0) Console.WriteLine("  {0,-26} = {1}", kv.Key, Cut(kv.Value, 60));

            var fieldMap = new Dictionary<string, string>();
            foreach (var kv in fields) fieldMap[kv.Key] = kv.Value;
            doc["totalCustomers"] = total;
            doc["customers"] = customers;
            doc["addresses"] = addresses;
            doc["firstCustomerFields"] = fieldMap;
        }

        // Read-only: the product list.
        private static void ListStock(SageSopWriter w, int max, Dictionary<string, object> doc)
        {
            var total = w.CountStock();
            var stock = w.ListStock(max);
            var show = max > 0 ? stock.Count : Math.Min(stock.Count, ConsoleRows);
            Console.WriteLine();
            Console.WriteLine("Stock: " + total + " in Sage, " + stock.Count + " read" + (show < stock.Count ? ", first " + show + " shown" : ""));
            Console.WriteLine("{0,-18} {1,-34} {2,-8} {3,-4} {4,9} {5,-8} {6,9}", "Code", "Description", "Nominal", "Tax", "Price", "Unit", "In stock");
            for (var i = 0; i < show; i++)
            {
                var s = stock[i];
                Console.WriteLine("{0,-18} {1,-34} {2,-8} T{3,-3} {4,9:N2} {5,-8} {6,9:N0}{7}",
                    Cut(s.StockCode, 18), Cut(s.Description, 34), s.NominalCode, s.TaxCode, s.SalesPrice, Cut(s.UnitOfSale, 8), s.QtyInStock,
                    s.Inactive ? "  INACTIVE" : "");
            }
            doc["totalStock"] = total;
            doc["stock"] = stock;
        }

        // Read-only: where one account's sales actually live (audit trail / invoices / orders).
        public static int ProbeLedger(SidecarConfig cfg, string data, string user, string password, string account)
        {
            Override(cfg, data, user, password);
            return RunSta(() =>
            {
                using (var w = new SageSopWriter(cfg))
                {
                    w.Connect();
                    Console.WriteLine("OK - " + w.Describe());
                    foreach (var line in w.ProbeLedger(account ?? "")) Console.WriteLine(line);
                }
            });
        }

        // Read-only: the sales mirror as the service would send it — to a JSON file, and/or
        // straight to the configured API (--push), which is how a fresh install gets its
        // Reports data without waiting for the timer.
        public static int SalesSnapshot(SidecarConfig cfg, string data, string user, string password,
            DateTime? from, int stopRun, string outFile, bool push)
        {
            if (string.IsNullOrEmpty(outFile) && !push)
            {
                Console.WriteLine("--sales-snapshot needs --out <file.json> and/or --push.");
                return 2;
            }
            Override(cfg, data, user, password);
            foreach (var p in cfg.SageProblems()) Console.WriteLine("  ! " + p);
            var since = from ?? cfg.SalesHistoryFrom;

            return RunSta(() =>
            {
                using (var w = new SageSopWriter(cfg))
                {
                    Console.WriteLine("Connecting to " + cfg.SageDataPath + " as '" + cfg.SageUsername + "' ...");
                    w.Connect();
                    Console.WriteLine("OK - " + w.Describe());
                    Console.WriteLine("Reading the audit trail and invoices from " + since.ToString("yyyy-MM-dd")
                        + (stopRun > 0 ? " (audit trail stops after " + stopRun + " older entries; invoice file read whole)" : " (full scans)") + " ...");
                    var snap = w.ReadSalesMirror(since, stopRun);
                    var lines = snap.Invoices.Sum(i => i.Lines == null ? 0 : i.Lines.Count);
                    Console.WriteLine("Scanned " + snap.HeadersScanned + " audit entries and " + snap.InvoicesScanned + " invoices in "
                        + (snap.ReadMs / 1000.0).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + "s: "
                        + snap.Transactions.Count + " SI/SC transactions, " + snap.Invoices.Count + " invoice records, " + lines + " lines.");
                    foreach (var n in snap.Notes) Console.WriteLine("  note: " + n);
                    if (!string.IsNullOrEmpty(outFile))
                    {
                        File.WriteAllText(outFile, JsonConvert.SerializeObject(snap));
                        Console.WriteLine("Written: " + outFile + " (" + (new FileInfo(outFile).Length / 1024) + " KB)");
                    }
                    if (push)
                    {
                        using (var api = new ApiClient(cfg))
                        {
                            var r = api.PutSales(snap);
                            Console.WriteLine("Pushed to " + cfg.ApiBaseUrl + ": " + r.Transactions + " transactions, " + r.Invoices + " invoices, " + r.Lines + " lines now in the mirror.");
                        }
                    }
                }
            });
        }

        // Read-only: specific invoices by number, with their lines.
        public static int ProbeInvoices(SidecarConfig cfg, string data, string user, string password, string numbers)
        {
            Override(cfg, data, user, password);
            return RunSta(() =>
            {
                using (var w = new SageSopWriter(cfg))
                {
                    w.Connect();
                    Console.WriteLine("OK - " + w.Describe());
                    foreach (var n in (numbers ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int no;
                        if (!int.TryParse(n, out no)) { Console.WriteLine("not a number: " + n); continue; }
                        foreach (var line in w.ProbeInvoice(no)) Console.WriteLine(line);
                    }
                }
            });
        }

        // Read-only: every invoice/credit in the window with item lines for the given
        // accounts, and those accounts' ledger, to a JSON file for reporting off-box.
        public static int ReportSales(SidecarConfig cfg, string data, string user, string password,
            DateTime from, DateTime to, string[] accounts, bool linesForAll, bool scanAll, string outFile)
        {
            if (string.IsNullOrEmpty(outFile))
            {
                Console.WriteLine("--report-sales needs --out <file.json>.");
                return 2;
            }
            Override(cfg, data, user, password);
            foreach (var p in cfg.SageProblems()) Console.WriteLine("  ! " + p);

            return RunSta(() =>
            {
                using (var w = new SageSopWriter(cfg))
                {
                    Console.WriteLine("Connecting to " + cfg.SageDataPath + " as '" + cfg.SageUsername + "' ...");
                    w.Connect();
                    Console.WriteLine("OK - " + w.Describe());
                    Console.WriteLine("Reading invoices " + from.ToString("yyyy-MM-dd") + " to " + to.ToString("yyyy-MM-dd")
                        + " with lines for " + (linesForAll ? "every account" : accounts.Length + " account(s)") + " ...");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var r = w.ReadSales(from, to, accounts, linesForAll, scanAll);
                    Console.WriteLine("Scanned " + r.Scanned + " invoices back to " + (r.OldestScanned.HasValue ? r.OldestScanned.Value.ToString("yyyy-MM-dd") : "?")
                        + "; " + r.Invoices.Count + " in the window; " + r.Ledger.Count + " ledger entries; "
                        + sw.Elapsed.TotalSeconds.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + "s");
                    foreach (var a in r.AccountInfos)
                    {
                        var invs = r.Invoices.Where(i => string.Equals(i.AccountRef, a.AccountRef, StringComparison.OrdinalIgnoreCase)).ToList();
                        var net = invs.Sum(i => (i.IsCredit ? -1 : 1) * i.BaseTotNet);
                        Console.WriteLine("  {0,-9} {1,-36} {2,4} inv/cr  base net {3,12:N2}  currency {4}{5}",
                            a.AccountRef, Cut(a.Name ?? "", 36), invs.Count, net, a.Currency, a.Found ? "" : "  NOT FOUND");
                    }
                    foreach (var n in r.Notes) Console.WriteLine("  note: " + n);
                    File.WriteAllText(outFile, JsonConvert.SerializeObject(r, Formatting.Indented));
                    Console.WriteLine("Written: " + outFile);
                }
            });
        }

        public static int PostFile(SidecarConfig cfg, string path, string data, string user, string password, bool yes)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Console.WriteLine("--post-file needs the path of a payload JSON file (save GET /api/sage/orders/{id}/payload from the API).");
                return 2;
            }
            var payload = JsonConvert.DeserializeObject<SalesOrderPayload>(File.ReadAllText(path));
            if (payload == null || payload.OrderNo == 0)
            {
                Console.WriteLine("That file does not look like a sales-order payload.");
                return 2;
            }
            Override(cfg, data, user, password);

            Console.WriteLine("This will CREATE sales order " + payload.Reference + " (" + (payload.Lines == null ? 0 : payload.Lines.Count)
                + " lines) for account '" + payload.AccountRef + "' in " + cfg.SageDataPath + ".");
            if (!yes)
            {
                if (!Environment.UserInteractive)
                {
                    Console.WriteLine("Not interactive - add --yes to confirm the post.");
                    return 3;
                }
                Console.Write("Type YES to continue: ");
                if (Console.ReadLine() != "YES")
                {
                    Console.WriteLine("Not posted.");
                    return 3;
                }
            }

            return RunSta(() =>
            {
                using (var w = new SageSopWriter(cfg))
                {
                    w.Connect();
                    Console.WriteLine("Connected: " + w.Describe());
                    var existing = w.FindExistingOrder(payload.Reference);
                    if (existing.HasValue)
                    {
                        Console.WriteLine(payload.Reference + " is already in Sage as order " + existing.Value + " - not posting again.");
                        return;
                    }
                    var no = w.PostSalesOrder(payload);
                    Console.WriteLine("Posted: Sage sales order " + no);
                }
            });
        }

        private static void Override(SidecarConfig cfg, string data, string user, string password)
        {
            if (!string.IsNullOrEmpty(data)) cfg.SageDataPath = data;
            if (!string.IsNullOrEmpty(user)) cfg.SageUsername = user;
            if (password != null) cfg.SagePassword = password;
        }

        private static string Cut(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + ".";
        }

        // SDO wants an STA thread; a console app's main thread is not one.
        private static int RunSta(Action action)
        {
            var rc = 0;
            var t = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex)
                {
                    Console.WriteLine("FAILED - " + ex.Message);
                    rc = 1;
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return rc;
        }
    }
}
