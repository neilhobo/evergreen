using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SageDataObject280;

namespace Globeweigh.SageSidecar
{
    // A Sage problem another attempt might get past (Sage busy, a record locked, ...).
    public class SageException : Exception
    {
        public SageException(string message) : base(message) { }
        public SageException(string message, Exception inner) : base(message, inner) { }
    }

    // Could not get into Sage at all — data path, login, SDO not installed. Not the job's
    // fault: the worker releases the job rather than spending one of its attempts.
    public class SageConnectException : SageException
    {
        public SageConnectException(string message) : base(message) { }
        public SageConnectException(string message, Exception inner) : base(message, inner) { }
    }

    // The order cannot be posted as it is (unknown customer, no lines, ...). Retrying cannot
    // fix data, so the worker fails the job finally and a person looks at it.
    public class SageValidationException : SageException
    {
        public SageValidationException(string message) : base(message) { }
    }

    // Everything that touches Sage, through Sage Data Objects v28. One instance = one SDO
    // workspace connection. Create it when there is work and Dispose it when the queue is
    // empty: a connected workspace holds a Sage user seat.
    //
    // Use from ONE STA thread only (ExportWorker runs on one). SDO is an apartment-threaded
    // COM server; calling it from the thread pool marshals every call and deadlocks the
    // moment the owning thread blocks.
    //
    // Field writes follow Sage's own advice from the SDO help ("we always typecast any value
    // passed to an SDO field"): strings as String, tax codes as Int16, quantities and money
    // as Double, dates as DateTime — see Sdo below. The field names and the posting sequence
    // are the ones in the help file's "Example - Posting a Sales Order" (SageDataObjectsv28_1
    // .chm in Documents\Sage 50cloud Accounts v28.1.404.0 Developer SDO).
    public sealed class SageSopWriter : IDisposable
    {
        private readonly SidecarConfig _cfg;
        private readonly Dictionary<short, double> _rateCache = new Dictionary<short, double>();
        private SDOEngine _engine;
        private WorkSpace _ws;
        private bool _connected;
        private bool _referenceScanBroken;

        public SageSopWriter(SidecarConfig cfg) { _cfg = cfg; }

        // Asked during the long mirror reads (customers, sales) whether to stop — Admin pressed
        // Cancel. Called at most every CancelCheckEvery, since each call is a trip to the API.
        // Null = never stop (the exports and the diagnostics).
        public Func<bool> CancelCheck { get; set; }
        private static readonly TimeSpan CancelCheckEvery = TimeSpan.FromSeconds(3);
        private DateTime _nextCancelCheck;

        private void ThrowIfCancelled()
        {
            if (CancelCheck == null || DateTime.UtcNow < _nextCancelCheck) return;
            _nextCancelCheck = DateTime.UtcNow + CancelCheckEvery;
            if (CancelCheck()) throw new OperationCanceledException("Cancelled from Admin");
        }

        public void Connect()
        {
            try
            {
                _engine = new SDOEngine();
            }
            catch (COMException ex)
            {
                // 0x80040154 REGDB_E_CLASSNOTREG: no SDO here, or we are somehow 64-bit.
                throw new SageConnectException(
                    "Sage Data Objects (SDO) are not available in this process — install the Sage 50 client "
                    + "or Sage50Accounts_SDO.msi on this machine, and make sure the sidecar is the 32-bit build. ("
                    + ex.Message + ")", ex);
            }

            _ws = (WorkSpace)_engine.Workspaces.Add(_cfg.SageWorkspaceName);

            // Never let SDO put a dialog up: there is nobody to click it in a service or a
            // scheduled task, and a hidden prompt looks exactly like a hang. (Late-bound: the
            // property is read-only on some engine builds, and that must not stop a connect.)
            try { ((dynamic)_ws).UI = false; } catch { /* read-only here */ }

            bool ok;
            try
            {
                ok = _ws.Connect(_cfg.SageDataPath, _cfg.SageUsername, _cfg.SagePassword ?? "", _cfg.SageWorkspaceName);
            }
            catch (Exception ex)
            {
                throw new SageConnectException("Could not connect to Sage data at '" + _cfg.SageDataPath + "': " + Describe(ex), ex);
            }
            if (!ok)
                throw new SageConnectException("Sage refused the connection to '" + _cfg.SageDataPath + "' (" + LastError()
                    + "). Check the data path, the Sage user and password, and that the user is not logged in elsewhere.");
            _connected = true;
        }

        public string Describe()
        {
            return "Sage v" + _ws.Version + " (" + _ws.ProgramType + ") at " + _ws.Path;
        }

        public int NextOrderNumber()
        {
            var post = (SopPost)_ws.CreateObject("SopPost");
            return post.GetNextNumber();
        }

        // For --test-sage: what Sage knows about a customer / a stock code.
        public string DescribeCustomer(string accountRef)
        {
            var c = FindCustomer(accountRef);
            if (c == null) return "Customer '" + accountRef + "': NOT FOUND in Sage";
            return "Customer " + Sdo.GetString(c.Fields, "ACCOUNT_REF") + ": " + Sdo.GetString(c.Fields, "NAME")
                + " | " + Sdo.GetString(c.Fields, "ADDRESS_1") + ", " + Sdo.GetString(c.Fields, "ADDRESS_2")
                + " | default tax T" + Sdo.GetInt16(c.Fields, "DEF_TAX_CODE")
                + " | balance " + Sdo.GetString(c.Fields, "BALANCE");
        }

        public string DescribeStock(string stockCode)
        {
            var s = (StockRecord)_ws.CreateObject("StockRecord");
            if (!FindStock(s, stockCode)) return "Stock code '" + stockCode + "': NOT FOUND in Sage"
                + (string.IsNullOrEmpty(_cfg.NonStockCode) ? " (and NonStockCode is blank, so such a line would FAIL)" : " (such a line would post as " + _cfg.NonStockCode + ")");
            return "Stock " + Sdo.GetString(s.Fields, "STOCK_CODE") + ": " + Sdo.GetString(s.Fields, "DESCRIPTION")
                + " | nominal " + Sdo.GetString(s.Fields, "NOMINAL_CODE")
                + " | tax T" + Sdo.GetInt16(s.Fields, "TAX_CODE")
                + " | sales price " + Sdo.GetString(s.Fields, "SALES_PRICE")
                + " | in stock " + Sdo.GetString(s.Fields, "QTY_IN_STOCK");
        }

        // ---- Reads: the diagnostics today, a customer import tomorrow ----------------------

        public sealed class CustomerRow
        {
            public string AccountRef, Name, Address1, Address2, Address3, Address4, Address5;
            public string Contact, Telephone, Telephone2, Email, VatNumber, DelName;
            public short DefTaxCode;
            public double Balance;
            public bool OnHold, Inactive;
        }

        public sealed class AddressRow
        {
            public string AccountRef;
            public int AddressNumber;
            public string Name, Description, Type, Address1, Address2, Address3, Address4, Address5;
            public string Telephone, Contact, Email;
        }

        public int CountCustomers()
        {
            var rec = (SalesRecord)_ws.CreateObject("SalesRecord");
            return rec.Count;
        }

        // The customer ledger in account order. max <= 0 = all of it.
        public List<CustomerRow> ListCustomers(int max)
        {
            var list = new List<CustomerRow>();
            var rec = (SalesRecord)_ws.CreateObject("SalesRecord");
            if (!rec.MoveFirst()) return list;
            do
            {
                ThrowIfCancelled();
                var f = rec.Fields;
                list.Add(new CustomerRow
                {
                    AccountRef = Sdo.GetString(f, "ACCOUNT_REF"),
                    Name = Sdo.GetString(f, "NAME"),
                    Address1 = Sdo.GetString(f, "ADDRESS_1"),
                    Address2 = Sdo.GetString(f, "ADDRESS_2"),
                    Address3 = Sdo.GetString(f, "ADDRESS_3"),
                    Address4 = Sdo.GetString(f, "ADDRESS_4"),
                    Address5 = Sdo.GetString(f, "ADDRESS_5"),
                    Contact = Sdo.TryGetString(f, "CONTACT_NAME"),
                    Telephone = Sdo.TryGetString(f, "TELEPHONE"),
                    Telephone2 = Sdo.TryGetString(f, "TELEPHONE_2"),
                    Email = Sdo.TryGetString(f, "E_MAIL"),
                    VatNumber = Sdo.TryGetString(f, "VAT_REG_NUMBER"),
                    DelName = Sdo.TryGetString(f, "DEL_NAME"),
                    DefTaxCode = Sdo.TryGetInt16(f, "DEF_TAX_CODE"),
                    Balance = Sdo.TryGetDouble(f, "BALANCE"),
                    OnHold = Sdo.TryGetDouble(f, "ACCOUNT_ON_HOLD") != 0,
                    Inactive = Sdo.TryGetDouble(f, "INACTIVE_FLAG") != 0,
                });
                if (max > 0 && list.Count >= max) break;
            } while (rec.MoveNext());
            return list;
        }

        // Every delivery address in the company (the CustomerAddresses collection the old
        // Admin import walked), each tagged with its customer's account ref. max <= 0 = all.
        public List<AddressRow> ListAddresses(int max)
        {
            var list = new List<AddressRow>();
            var addrs = (CustomerAddresses)_ws.CreateCustomerAddresses();
            foreach (CustomerAddress a in addrs)
            {
                ThrowIfCancelled();
                var cust = a.Customer as Customer;
                list.Add(new AddressRow
                {
                    AccountRef = cust == null ? "" : (cust.Reference ?? "").Trim(),
                    AddressNumber = a.AddressNumber,
                    Name = (a.Name ?? "").Trim(),
                    Description = (a.Description ?? "").Trim(),
                    Type = a.AddressType.ToString(),
                    Address1 = (a.AddressLine1 ?? "").Trim(),
                    Address2 = (a.AddressLine2 ?? "").Trim(),
                    Address3 = (a.AddressLine3 ?? "").Trim(),
                    Address4 = (a.AddressLine4 ?? "").Trim(),
                    Address5 = (a.AddressLine5 ?? "").Trim(),
                    Telephone = (a.Telephone ?? "").Trim(),
                    Contact = (a.Contact ?? "").Trim(),
                    Email = (a.EMail ?? "").Trim(),
                });
                if (max > 0 && list.Count >= max) break;
            }
            return list;
        }

        public sealed class StockRow
        {
            public string StockCode, Description, NominalCode, UnitOfSale, Category;
            public short TaxCode;
            public double SalesPrice, QtyInStock;
            public bool Inactive;
        }

        public int CountStock()
        {
            var rec = (StockRecord)_ws.CreateObject("StockRecord");
            return rec.Count;
        }

        // The product list in stock-code order. max <= 0 = all of it.
        public List<StockRow> ListStock(int max)
        {
            var list = new List<StockRow>();
            var rec = (StockRecord)_ws.CreateObject("StockRecord");
            if (!rec.MoveFirst()) return list;
            do
            {
                var f = rec.Fields;
                list.Add(new StockRow
                {
                    StockCode = Sdo.GetString(f, "STOCK_CODE"),
                    Description = Sdo.TryGetString(f, "DESCRIPTION"),
                    NominalCode = Sdo.TryGetString(f, "NOMINAL_CODE"),
                    TaxCode = Sdo.TryGetInt16(f, "TAX_CODE"),
                    SalesPrice = Sdo.TryGetDouble(f, "SALES_PRICE"),
                    UnitOfSale = Sdo.TryGetString(f, "UNIT_OF_SALE"),
                    QtyInStock = Sdo.TryGetDouble(f, "QTY_IN_STOCK"),
                    Category = Sdo.TryGetString(f, "STOCK_CAT"),
                    Inactive = Sdo.TryGetDouble(f, "INACTIVE_FLAG") != 0,
                });
                if (max > 0 && list.Count >= max) break;
            } while (rec.MoveNext());
            return list;
        }

        public sealed class TaxRow
        {
            public short Code;
            public int TaxCodeId;
            public string Name;
            public double Rate;
            public string LoadedBy;
        }

        // Every tax code Sage will Load(), T0..T99. The help file does not say what key
        // Load() wants, so it is asked by number and then by "Tn"; LoadedBy records which
        // worked. Needed because the client's scheme (T20 as the standard rate) makes
        // guessing a rate from the code number unsafe.
        public List<TaxRow> ListTaxCodes()
        {
            var list = new List<TaxRow>();
            for (short code = 0; code < 100; code++)
            {
                TaxRow row = null;
                foreach (var key in new object[] { code, (int)code, "T" + code })
                {
                    try
                    {
                        var tc = (TaxCode)_ws.CreateTaxCode();
                        object k = key;
                        if (tc.Load(ref k))
                        {
                            row = new TaxRow { Code = code, TaxCodeId = tc.TaxCodeID, Name = (tc.Name ?? "").Trim(), Rate = tc.Rate, LoadedBy = key.GetType().Name };
                            break;
                        }
                    }
                    catch { /* try the next key form */ }
                }
                if (row != null) list.Add(row);
            }
            return list;
        }

        // ---- Sales extract (read-only), for one-off reporting ------------------------------

        public sealed class InvoiceRow
        {
            public int Number;
            public DateTime Date;
            public short TypeCode;              // 0 product inv, 1 SOP inv, 2 service inv, 3 product credit, 4 service credit
            public bool IsCredit;
            public string AccountRef, Name, OrderNumber, CustOrderNumber;
            public short Currency, CurrencyUsed;
            public double ForeignRate;
            public double ItemsNet, ItemsTax;              // document currency, goods only
            public double BaseTotNet, BaseTotTax;          // base currency, goods only
            public double CarrNet, BaseCarrNet;            // carriage
            public double NetValueDiscount, BaseNetValueDiscount;
            public bool Posted;
            public List<InvoiceLine> Lines;                // null = not read (account not selected)
        }

        public sealed class InvoiceLine
        {
            public short ItemNumber;
            public string StockCode, Description, UnitOfSale, NominalCode;
            public double Qty, UnitPrice, NetAmount, BaseNet, FullNetAmount, TaxAmount;
            public short TaxCode;
            public bool Service;
        }

        public sealed class LedgerRow
        {
            public string AccountRef;
            public short Type;                  // 1 = SI, 2 = SC
            public DateTime Date;
            public double NetAmount, TaxAmount, ForeignNetAmount;
            public short Currency;
            public string InvRef, Details;
        }

        public sealed class AccountInfo
        {
            public string AccountRef, Name;
            public short Currency;
            public double Balance;
            public bool Found;
        }

        public sealed class SalesReport
        {
            public DateTime From, To;
            public DateTime ReadAt;
            public string Company;
            public List<string> Accounts = new List<string>();
            public List<AccountInfo> AccountInfos = new List<AccountInfo>();
            public List<InvoiceRow> Invoices = new List<InvoiceRow>();   // every account in range; Lines only for the selected ones
            public List<LedgerRow> Ledger = new List<LedgerRow>();       // SI/SC for the selected accounts
            public int Scanned;
            public DateTime? OldestScanned;
            public List<string> Notes = new List<string>();
        }

        // Invoices and credits dated from..to (quotes and pro-formas left out), newest first
        // off the end of the invoice file, with item lines for the selected accounts; plus
        // the selected accounts' SI/SC audit-trail entries for the same window, which is the
        // ledger truth to reconcile the invoice figures against.
        public SalesReport ReadSales(DateTime from, DateTime to, ICollection<string> accounts, bool linesForAll, bool scanAll)
        {
            var wanted = new HashSet<string>(accounts.Select(a => (a ?? "").Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
            var report = new SalesReport { From = from, To = to, ReadAt = DateTime.Now, Company = Describe(), Accounts = wanted.ToList() };

            // Invoices are numbered in the order they were raised, so walking back from the
            // newest and stopping once a long run of them pre-dates the window finds the lot
            // without reading years of history — IF the file is in number order. Evergreen's
            // is not: old invoices have been purged and Sage reuses the slots, so there
            // scanAll reads the whole (small) file instead.
            ScanInvoices(from, to, wanted, linesForAll, scanAll ? int.MaxValue : 600, report);

            foreach (var acc in wanted)
            {
                var sr = FindCustomer(acc);
                if (sr == null)
                {
                    report.AccountInfos.Add(new AccountInfo { AccountRef = acc, Found = false });
                    report.Notes.Add("Account " + acc + " not found in Sage.");
                    continue;
                }
                report.AccountInfos.Add(new AccountInfo
                {
                    AccountRef = acc,
                    Found = true,
                    Name = Sdo.GetString(sr.Fields, "NAME"),
                    Currency = Sdo.TryGetInt16(sr.Fields, "CURRENCY"),
                    Balance = Sdo.TryGetDouble(sr.Fields, "BALANCE"),
                });
                var hd = sr.Link as HeaderData;
                if (hd == null || !hd.MoveFirst()) continue;
                do
                {
                    var hf = hd.Fields;
                    var type = Sdo.TryGetInt16(hf, "TYPE");
                    if (type != 1 && type != 2) continue;
                    var date = Sdo.TryGetDate(hf, "DATE");
                    if (date < from || date > to) continue;
                    // DELETED_FLAG is the deletion marker. RECORD_DELETED is NOT a flag — live
                    // records carry internal values like 13056 in it — and testing it threw
                    // away almost everything on the first run.
                    if (Sdo.TryGetDouble(hf, "DELETED_FLAG") != 0) continue;
                    report.Ledger.Add(new LedgerRow
                    {
                        AccountRef = acc,
                        Type = type,
                        Date = date,
                        NetAmount = Sdo.TryGetDouble(hf, "NET_AMOUNT"),
                        TaxAmount = Sdo.TryGetDouble(hf, "TAX_AMOUNT"),
                        ForeignNetAmount = Sdo.TryGetDouble(hf, "FOREIGN_NET_AMOUNT"),
                        Currency = Sdo.TryGetInt16(hf, "CURRENCY"),
                        InvRef = Sdo.TryGetString(hf, "INV_REF"),
                        Details = Sdo.TryGetString(hf, "DETAILS"),
                    });
                } while (hd.MoveNext());
            }
            return report;
        }

        // Newest-first over the invoice file, collecting what is dated in the window. Stops
        // once olderRunToStop consecutive records pre-date the window (int.MaxValue = read
        // the whole file, ~2.5 min for Evergreen's 30k records back to 2006). An early stop
        // is only safe when the caller knows every wanted record is near the end — see
        // ReadSalesMirror for why the mirror cannot assume that.
        private void ScanInvoices(DateTime from, DateTime to, HashSet<string> wanted, bool linesForAll, int olderRunToStop, SalesReport report)
        {
            var inv = (InvoiceRecord)_ws.CreateObject("InvoiceRecord");
            if (!inv.MoveLast()) return;
            var olderRun = 0;
            while (true)
            {
                ThrowIfCancelled();
                report.Scanned++;
                var f = inv.Fields;
                var date = Sdo.TryGetDate(f, "INVOICE_DATE");
                if (report.OldestScanned == null || date < report.OldestScanned) report.OldestScanned = date;
                if (date < from)
                {
                    if (++olderRun >= olderRunToStop) break;
                }
                else
                {
                    olderRun = 0;
                    if (date <= to) ReadInvoice(inv, f, date, wanted, linesForAll, report);
                }
                if (report.Scanned >= 200000) { report.Notes.Add("Stopped at 200,000 invoices scanned."); break; }
                if (!inv.MovePrev()) break;
            }
        }

        // ---- Sales mirror (read-only), for the API's Reports pages --------------------------

        // One audit-trail header: a posted sales invoice (TYPE 1, SI) or credit (TYPE 2, SC).
        // Field names match the API's SageSalesMirror.TransactionIn — change both together.
        public sealed class TransactionRow
        {
            public int UniqueRef;               // Sage's UNIQUE_REF on the header
            public string AccountRef;
            public short Type;                  // 1 = SI, 2 = SC
            public DateTime Date;
            public string InvRef, Details;
            public double NetAmount, TaxAmount, ForeignNetAmount;
            public short Currency;
            public string UserName;
        }

        // What PUT /api/sage/sidecar/sales takes: every SI/SC on the audit trail dated from
        // From, and every invoice/credit record dated from From with its item lines.
        public sealed class SalesSnapshot
        {
            public string Machine, Company;
            public DateTime ReadAt;
            public int ReadMs;
            public DateTime From, To;
            public List<TransactionRow> Transactions = new List<TransactionRow>();
            public List<InvoiceRow> Invoices = new List<InvoiceRow>();
            public int HeadersScanned, InvoicesScanned;
            public List<string> Notes = new List<string>();
        }

        // The audit trail is read straight off HEADER.DTA (CreateObject("HeaderData")) rather
        // than account by account through SalesRecord.Link — one pass instead of ~3,000. It
        // is in posting order, so walking back from the newest and stopping after stopRun
        // consecutive entries older than From is safe: back-dated postings exist, but not
        // thousands in a row. (A credit note posted this year has this year's transaction
        // number whatever its document number, so the invoice-file problem below does not
        // arise here.)
        public SalesSnapshot ReadSalesMirror(DateTime from, int stopRun)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var to = DateTime.Today.AddYears(1);        // anything post-dated comes too
            var snap = new SalesSnapshot { Company = Describe(), From = from, To = to, Machine = Environment.MachineName, ReadAt = DateTime.Now };
            if (stopRun <= 0) stopRun = int.MaxValue;

            var hd = (HeaderData)_ws.CreateObject("HeaderData");
            if (hd.MoveLast())
            {
                var olderRun = 0;
                do
                {
                    ThrowIfCancelled();
                    snap.HeadersScanned++;
                    var hf = hd.Fields;
                    var date = Sdo.TryGetDate(hf, "DATE");
                    if (date < from)
                    {
                        if (++olderRun >= stopRun) break;
                        continue;
                    }
                    olderRun = 0;
                    var type = Sdo.TryGetInt16(hf, "TYPE");
                    if (type != 1 && type != 2) continue;
                    if (Sdo.TryGetDouble(hf, "DELETED_FLAG") != 0) continue;   // DELETED_FLAG, never RECORD_DELETED
                    snap.Transactions.Add(new TransactionRow
                    {
                        UniqueRef = (int)Sdo.TryGetDouble(hf, "UNIQUE_REF"),
                        AccountRef = (Sdo.TryGetString(hf, "ACCOUNT_REF") ?? "").Trim().ToUpperInvariant(),
                        Type = type,
                        Date = date,
                        InvRef = Sdo.TryGetString(hf, "INV_REF"),
                        Details = Sdo.TryGetString(hf, "DETAILS"),
                        NetAmount = Sdo.TryGetDouble(hf, "NET_AMOUNT"),
                        TaxAmount = Sdo.TryGetDouble(hf, "TAX_AMOUNT"),
                        ForeignNetAmount = Sdo.TryGetDouble(hf, "FOREIGN_NET_AMOUNT"),
                        Currency = Sdo.TryGetInt16(hf, "CURRENCY"),
                        UserName = Sdo.TryGetString(hf, "USER_NAME"),
                    });
                    if (snap.HeadersScanned >= 2000000) { snap.Notes.Add("Stopped at 2,000,000 audit-trail entries scanned."); break; }
                } while (hd.MovePrev());
            }

            // The invoice file is read WHOLE, whatever stopRun says. It is in INVOICE_NUMBER
            // order, and Evergreen numbers credit notes in their own low sequence (527, 528,
            // ...), so this year's credits sit at the very start of the file behind 25,000
            // older invoices; an early stop from the newest end lost 92 of them on the first
            // run. ~2.5 min for the 30,000 records.
            var report = new SalesReport();
            ScanInvoices(from, to, new HashSet<string>(StringComparer.OrdinalIgnoreCase), true, int.MaxValue, report);
            snap.Invoices = report.Invoices;
            snap.InvoicesScanned = report.Scanned;
            snap.Notes.AddRange(report.Notes);
            snap.ReadMs = (int)sw.ElapsedMilliseconds;
            return snap;
        }

        // Unfiltered look at where sales actually live in this company: the account's audit
        // trail (first/last entries, type mix), the newest invoices, and the newest sales
        // orders — for deciding which file a turnover report has to be built from.
        public List<string> ProbeLedger(string accountRef)
        {
            var o = new List<string>();
            var sr = FindCustomer(accountRef);
            if (sr == null) { o.Add("account " + accountRef + " not found"); return o; }
            o.Add("account " + accountRef + " '" + Sdo.GetString(sr.Fields, "NAME") + "' currency=" + Sdo.TryGetInt16(sr.Fields, "CURRENCY")
                + " balance=" + Sdo.TryGetDouble(sr.Fields, "BALANCE") + " turnoverYTD=" + Sdo.TryGetDouble(sr.Fields, "TURNOVER_YTD")
                + " lastInv=" + Sdo.TryGetDate(sr.Fields, "LAST_INV_DATE").ToString("yyyy-MM-dd") + " noOfHeaders=" + Sdo.TryGetDouble(sr.Fields, "NO_OF_HEADER"));
            var link = sr.Link;
            var hd = link as HeaderData;
            o.Add("Link: " + (link == null ? "null" : "object") + ", as HeaderData: " + (hd == null ? "NULL" : "ok") + (hd != null ? ", Count=" + hd.Count : ""));
            if (hd != null && hd.MoveFirst())
            {
                var n = 0; var types = new Dictionary<int, int>(); DateTime min = DateTime.MaxValue, max = DateTime.MinValue; var inWindow = 0; double si2026 = 0, sc2026 = 0;
                var first = new List<string>(); var last = new List<string>();
                do
                {
                    var hf = hd.Fields; n++;
                    var t = Sdo.TryGetInt16(hf, "TYPE"); var d = Sdo.TryGetDate(hf, "DATE"); var net = Sdo.TryGetDouble(hf, "NET_AMOUNT");
                    types[t] = types.TryGetValue(t, out var c) ? c + 1 : 1;
                    if (d < min) min = d; if (d > max) max = d;
                    var line = "    " + d.ToString("yyyy-MM-dd") + " type " + t + " net " + net.ToString("N2", CultureInfo.InvariantCulture) + " ref " + Sdo.TryGetString(hf, "INV_REF") + " '" + Sdo.TryGetString(hf, "DETAILS") + "'";
                    if (first.Count < 5) first.Add(line);
                    last.Add(line); if (last.Count > 5) last.RemoveAt(0);
                    if (d.Year == 2026) { inWindow++; if (t == 1) si2026 += net; if (t == 2) sc2026 += net; }
                } while (hd.MoveNext());
                o.Add("  entries=" + n + " dates " + min.ToString("yyyy-MM-dd") + ".." + max.ToString("yyyy-MM-dd") + " types " + string.Join(" ", types.OrderBy(k => k.Key).Select(k => "T" + k.Key + "=" + k.Value))
                    + " | 2026: " + inWindow + " entries, SI " + si2026.ToString("N2", CultureInfo.InvariantCulture) + " SC " + sc2026.ToString("N2", CultureInfo.InvariantCulture));
                o.Add("  first:"); o.AddRange(first); o.Add("  last:"); o.AddRange(last);
            }
            else o.Add("  (no audit-trail entries via Link)");

            var inv = (InvoiceRecord)_ws.CreateObject("InvoiceRecord");
            o.Add("invoices: Count=" + inv.Count);
            if (inv.MoveLast())
            {
                var k = 0;
                do
                {
                    var f = inv.Fields;
                    o.Add("    #" + Sdo.TryGetDouble(f, "INVOICE_NUMBER") + " " + Sdo.TryGetDate(f, "INVOICE_DATE").ToString("yyyy-MM-dd") + " type " + Sdo.TryGetInt16(f, "INVOICE_TYPE_CODE")
                        + " " + Sdo.GetString(f, "ACCOUNT_REF") + " itemsNet " + Sdo.TryGetDouble(f, "ITEMS_NET").ToString("N2", CultureInfo.InvariantCulture) + " posted=" + Sdo.TryGetDouble(f, "POSTED_CODE") + " del=" + Sdo.TryGetDouble(f, "DELETED_FLAG"));
                } while (++k < 8 && inv.MovePrev());
            }

            var sop = (SopRecord)_ws.CreateObject("SopRecord");
            o.Add("sales orders: Count=" + sop.Count);
            if (sop.MoveLast())
            {
                var k = 0;
                do
                {
                    var f = sop.Fields;
                    o.Add("    SOP " + Sdo.TryGetDouble(f, "ORDER_NUMBER") + " " + Sdo.TryGetDate(f, "ORDER_DATE").ToString("yyyy-MM-dd") + " " + Sdo.GetString(f, "ACCOUNT_REF")
                        + " status=" + Sdo.TryGetDouble(f, "STATUS") + " invNo=" + Sdo.TryGetString(f, "INVOICE_NUMBER") + " despatch=" + Sdo.TryGetDate(f, "DESPATCH_DATE").ToString("yyyy-MM-dd")
                        + " itemsNet " + Sdo.TryGetDouble(f, "ITEMS_NET").ToString("N2", CultureInfo.InvariantCulture));
                } while (++k < 8 && sop.MovePrev());
            }
            return o;
        }

        // One invoice by number, whatever its date or deleted state, with its lines — for
        // chasing a ledger entry whose invoice the windowed extract did not pick up.
        public List<string> ProbeInvoice(int number)
        {
            var o = new List<string>();
            var inv = (InvoiceRecord)_ws.CreateObject("InvoiceRecord");
            Sdo.Set(inv.Fields, "INVOICE_NUMBER", (double)number);
            bool found;
            try { found = inv.Find(false); }
            catch (Exception ex) { o.Add("invoice " + number + ": Find failed - " + ex.Message); return o; }
            if (!found)
            {
                o.Add("invoice " + number + ": NOT FOUND in the invoice file - looking for the sales order it was raised from");
                ProbeSopByInvoice(number, o);
                return o;
            }
            var f = inv.Fields;
            o.Add("invoice " + number + ": date " + Sdo.TryGetDate(f, "INVOICE_DATE").ToString("yyyy-MM-dd") + " type " + Sdo.TryGetInt16(f, "INVOICE_TYPE_CODE")
                + " account " + Sdo.GetString(f, "ACCOUNT_REF") + " '" + Sdo.TryGetString(f, "NAME") + "' itemsNet " + Sdo.TryGetDouble(f, "ITEMS_NET").ToString("N2", CultureInfo.InvariantCulture)
                + " baseNet " + Sdo.TryGetDouble(f, "BASE_TOT_NET").ToString("N2", CultureInfo.InvariantCulture) + " posted=" + Sdo.TryGetDouble(f, "POSTED_CODE")
                + " deleted=" + Sdo.TryGetDouble(f, "DELETED_FLAG") + " recordDeleted=" + Sdo.TryGetDouble(f, "RECORD_DELETED") + " order " + Sdo.TryGetString(f, "ORDER_NUMBER"));
            var item = inv.Link as InvoiceItem;
            if (item != null && item.MoveFirst())
            {
                do
                {
                    var lf = item.Fields;
                    o.Add("    " + Sdo.TryGetString(lf, "STOCK_CODE") + " | " + Sdo.TryGetString(lf, "DESCRIPTION") + " | qty " + Sdo.TryGetDouble(lf, "QTY_ORDER")
                        + " @ " + Sdo.TryGetDouble(lf, "UNIT_PRICE").ToString("N2", CultureInfo.InvariantCulture) + " net " + Sdo.TryGetDouble(lf, "NET_AMOUNT").ToString("N2", CultureInfo.InvariantCulture)
                        + " deleted=" + Sdo.TryGetDouble(lf, "DELETED_FLAG"));
                } while (item.MoveNext());
            }
            else o.Add("    (no lines)");
            return o;
        }

        // A deleted invoice leaves its sales order behind (STATUS/INVOICE_NUMBER on the SOP
        // header); walk the order file newest-first looking for one that produced this invoice.
        private void ProbeSopByInvoice(int number, List<string> o)
        {
            var sop = (SopRecord)_ws.CreateObject("SopRecord");
            if (!sop.MoveLast()) { o.Add("    (no sales orders)"); return; }
            var want = number.ToString(CultureInfo.InvariantCulture);
            var scanned = 0;
            do
            {
                scanned++;
                var f = sop.Fields;
                var invNo = (Sdo.TryGetString(f, "INVOICE_NUMBER") ?? "").Trim();
                var invNum = Sdo.TryGetDouble(f, "INVOICE_NUMBER_NUMERIC");
                if (invNo != want && (int)invNum != number) continue;
                o.Add("    SOP " + Sdo.TryGetDouble(f, "ORDER_NUMBER") + " " + Sdo.TryGetDate(f, "ORDER_DATE").ToString("yyyy-MM-dd") + " " + Sdo.GetString(f, "ACCOUNT_REF")
                    + " '" + Sdo.TryGetString(f, "NAME") + "' status=" + Sdo.TryGetDouble(f, "STATUS") + " invNo=" + invNo + " itemsNet " + Sdo.TryGetDouble(f, "ITEMS_NET").ToString("N2", CultureInfo.InvariantCulture)
                    + " deleted=" + Sdo.TryGetDouble(f, "DELETED_FLAG") + " custOrder '" + Sdo.TryGetString(f, "CUST_ORDER_NUMBER") + "'");
                var item = sop.Link as SopItem;
                if (item != null && item.MoveFirst())
                {
                    do
                    {
                        var lf = item.Fields;
                        o.Add("        " + Sdo.TryGetString(lf, "STOCK_CODE") + " | " + Sdo.TryGetString(lf, "DESCRIPTION") + " | qty " + Sdo.TryGetDouble(lf, "QTY_ORDER")
                            + " @ " + Sdo.TryGetDouble(lf, "UNIT_PRICE").ToString("N2", CultureInfo.InvariantCulture) + " net " + Sdo.TryGetDouble(lf, "NET_AMOUNT").ToString("N2", CultureInfo.InvariantCulture)
                            + " deleted=" + Sdo.TryGetDouble(lf, "DELETED_FLAG"));
                    } while (item.MoveNext());
                }
                else o.Add("        (no lines)");
                return;
            } while (scanned < 50000 && sop.MovePrev());
            o.Add("    no sales order carries invoice " + number + " (scanned " + scanned + " orders)");
        }

        private static void ReadInvoice(InvoiceRecord inv, Fields f, DateTime date, HashSet<string> wanted, bool linesForAll, SalesReport report)
        {
            if (Sdo.TryGetDouble(f, "DELETED_FLAG") != 0) return;     // RECORD_DELETED is not a flag (see above)
            var type = Sdo.TryGetInt16(f, "INVOICE_TYPE_CODE");
            if (type > 4) return;                                   // pro-formas and quotations are not sales
            var acc = Sdo.GetString(f, "ACCOUNT_REF");
            var row = new InvoiceRow
            {
                Number = (int)Sdo.TryGetDouble(f, "INVOICE_NUMBER"),
                Date = date,
                TypeCode = type,
                IsCredit = type == 3 || type == 4,
                AccountRef = acc,
                Name = Sdo.TryGetString(f, "NAME"),
                OrderNumber = Sdo.TryGetString(f, "ORDER_NUMBER"),
                CustOrderNumber = Sdo.TryGetString(f, "CUST_ORDER_NUMBER"),
                Currency = Sdo.TryGetInt16(f, "CURRENCY"),
                CurrencyUsed = Sdo.TryGetInt16(f, "CURRENCY_USED"),
                ForeignRate = Sdo.TryGetDouble(f, "FOREIGN_RATE"),
                ItemsNet = Sdo.TryGetDouble(f, "ITEMS_NET"),
                ItemsTax = Sdo.TryGetDouble(f, "ITEMS_TAX"),
                BaseTotNet = Sdo.TryGetDouble(f, "BASE_TOT_NET"),
                BaseTotTax = Sdo.TryGetDouble(f, "BASE_TOT_TAX"),
                CarrNet = Sdo.TryGetDouble(f, "CARR_NET"),
                BaseCarrNet = Sdo.TryGetDouble(f, "BASE_CARR_NET"),
                NetValueDiscount = Sdo.TryGetDouble(f, "NETVALUE_DISCOUNT"),
                BaseNetValueDiscount = Sdo.TryGetDouble(f, "BASE_NETVALUE_DISCOUNT"),
                Posted = Sdo.TryGetDouble(f, "POSTED_CODE") != 0,
            };
            if (linesForAll || wanted.Contains(acc))
            {
                row.Lines = new List<InvoiceLine>();
                var item = inv.Link as InvoiceItem;
                if (item != null && item.MoveFirst())
                {
                    do
                    {
                        var lf = item.Fields;
                        row.Lines.Add(new InvoiceLine
                        {
                            ItemNumber = Sdo.TryGetInt16(lf, "ITEM_NUMBER"),
                            StockCode = Sdo.TryGetString(lf, "STOCK_CODE"),
                            Description = Sdo.TryGetString(lf, "DESCRIPTION"),
                            UnitOfSale = Sdo.TryGetString(lf, "UNIT_OF_SALE"),
                            NominalCode = Sdo.TryGetString(lf, "NOMINAL_CODE"),
                            Qty = Sdo.TryGetDouble(lf, "QTY_ORDER"),
                            UnitPrice = Sdo.TryGetDouble(lf, "UNIT_PRICE"),
                            NetAmount = Sdo.TryGetDouble(lf, "NET_AMOUNT"),
                            BaseNet = Sdo.TryGetDouble(lf, "BASE_NET"),
                            FullNetAmount = Sdo.TryGetDouble(lf, "FULL_NET_AMOUNT"),
                            TaxAmount = Sdo.TryGetDouble(lf, "TAX_AMOUNT"),
                            TaxCode = Sdo.TryGetInt16(lf, "TAX_CODE"),
                            Service = Sdo.TryGetDouble(lf, "SERVICE_FLAG") != 0,
                        });
                    } while (item.MoveNext());
                }
            }
            report.Invoices.Add(row);
        }

        // Name = value for every field Sage holds on the first customer record — the way to
        // see what is there before deciding what an import should take. By ordinal rather
        // than the collection's enumerator: after a full MoveNext() sweep the enumerator came
        // back with two fields, the ordinal walk does not.
        public List<KeyValuePair<string, string>> FirstCustomerFields()
        {
            var list = new List<KeyValuePair<string, string>>();
            var rec = (SalesRecord)_ws.CreateObject("SalesRecord");
            if (!rec.MoveFirst()) return list;
            var fields = rec.Fields;
            int count;
            try { count = fields.Count; } catch { count = 0; }
            // SDO collections are 1-based; fall back to 0-based if ordinal 1 is refused.
            var first = 1;
            try { object probe = 1; fields.Item(ref probe); } catch { first = 0; }
            for (var i = first; i < first + count; i++)
            {
                try
                {
                    object idx = i;
                    var f = fields.Item(ref idx);
                    string v;
                    try { v = Convert.ToString(f.Value, CultureInfo.InvariantCulture); } catch { v = "<unreadable>"; }
                    list.Add(new KeyValuePair<string, string>(f.Name, (v ?? "").Trim()));
                }
                catch { /* skip an ordinal the engine will not hand over */ }
            }
            return list;
        }

        // Is there already an order in Sage carrying this reference? Walks the newest
        // DuplicateScanDepth orders. The second line of defence behind ExportWorker's local
        // done-markers, for when a marker was lost (box rebuilt) after a post succeeded but
        // before the API heard about it.
        public int? FindExistingOrder(string reference)
        {
            if (_referenceScanBroken || _cfg.DuplicateScanDepth <= 0
                || string.IsNullOrEmpty(_cfg.ReferenceField) || string.IsNullOrEmpty(reference))
                return null;

            try
            {
                var rec = (SopRecord)_ws.CreateObject("SopRecord");
                if (!rec.MoveLast()) return null;
                for (var i = 0; i < _cfg.DuplicateScanDepth; i++)
                {
                    var val = Sdo.GetString(rec.Fields, _cfg.ReferenceField);
                    if (string.Equals(val, reference, StringComparison.OrdinalIgnoreCase))
                        return Convert.ToInt32(Sdo.Get(rec.Fields, "ORDER_NUMBER"), CultureInfo.InvariantCulture);
                    if (!rec.MovePrev()) break;
                }
                return null;
            }
            catch (Exception ex)
            {
                // Don't let a scan problem stop the posting; do stop repeating it.
                _referenceScanBroken = true;
                Log.Warn("Duplicate scan of " + _cfg.ReferenceField + " failed and is switched off for this session: " + ex.Message);
                return null;
            }
        }

        // Posts the order and returns Sage's ORDER_NUMBER for it.
        public int PostSalesOrder(SalesOrderPayload p)
        {
            if (p == null) throw new SageValidationException("Empty payload.");
            if (string.IsNullOrWhiteSpace(p.AccountRef))
                throw new SageValidationException("Order " + p.OrderNo + " has no customer account code (Customers > Code), so there is no Sage account to post it to.");
            if (p.Lines == null || p.Lines.Count == 0)
                throw new SageValidationException("Order " + p.OrderNo + " has no lines.");

            var customer = FindCustomer(p.AccountRef);
            if (customer == null)
                throw new SageValidationException("Customer account '" + p.AccountRef.Trim().ToUpperInvariant()
                    + "' does not exist in Sage (order " + p.OrderNo + ", " + p.CustomerName + ").");

            var stock = (StockRecord)_ws.CreateObject("StockRecord");
            var post = (SopPost)_ws.CreateObject("SopPost");
            var hdr = post.Header;

            // ---- Header: invoice address from the Sage account, delivery address from us ----
            var custName = Sdo.GetString(customer.Fields, "NAME");
            Sdo.Set(hdr, "ACCOUNT_REF", Sdo.GetString(customer.Fields, "ACCOUNT_REF"));
            Sdo.Set(hdr, "NAME", custName);
            for (var i = 1; i <= 5; i++)
                Sdo.Set(hdr, "ADDRESS_" + i, Sdo.GetString(customer.Fields, "ADDRESS_" + i));

            var del = p.DeliveryAddress ?? new List<string>();
            Sdo.Set(hdr, "DELIVERY_NAME", FirstNonEmpty(p.DeliveryName, p.CustomerName, custName));
            for (var i = 1; i <= 5; i++)
                Sdo.Set(hdr, "DEL_ADDRESS_" + i, i <= del.Count ? del[i - 1] : "");

            Sdo.Set(hdr, "CUST_TEL_NUMBER", FirstNonEmpty(p.Telephone, Sdo.GetString(customer.Fields, "TELEPHONE")));
            Sdo.Set(hdr, "CONTACT_NAME", FirstNonEmpty(p.ContactName, Sdo.GetString(customer.Fields, "CONTACT_NAME")));
            Sdo.Set(hdr, "GLOBAL_TAX_CODE", Sdo.GetInt16(customer.Fields, "DEF_TAX_CODE"));
            Sdo.Set(hdr, "ORDER_DATE", p.OrderDate.Date);
            Sdo.Set(hdr, "CUST_ORDER_NUMBER", p.PoNumber ?? "");
            Sdo.Set(hdr, "NOTES_1", "Evergreen order " + p.OrderNo + (string.IsNullOrWhiteSpace(p.HaulierName) ? "" : " - " + p.HaulierName.Trim()));
            Sdo.Set(hdr, "NOTES_2", Join(" ", p.VehicleReg, p.TrailerNo));
            Sdo.Set(hdr, "NOTES_3", p.Comments ?? "");
            Sdo.Set(hdr, "TAKEN_BY", _cfg.TakenBy);
            Sdo.Set(hdr, "GLOBAL_NOM_CODE", "");
            Sdo.Set(hdr, "GLOBAL_DETAILS", "");
            if (_cfg.ReferenceField.Length > 0)
                Sdo.Set(hdr, _cfg.ReferenceField, p.Reference ?? "");

            // ---- Lines ----
            dynamic items = post.Items;
            var added = 0;
            var skipped = new List<string>();
            foreach (var line in p.Lines)
            {
                var qty = _cfg.QuantityBasis == QuantityBasis.Units ? line.Units : line.Pallets;
                if (qty <= 0)
                {
                    skipped.Add(FirstNonEmpty(line.Description, line.StockCode, "product " + line.ProductId));
                    continue;
                }

                var res = ResolveStock(stock, line);
                var unitPrice = (double)(line.UnitPrice ?? 0m);
                var net = Math.Round(qty * unitPrice, 2, MidpointRounding.AwayFromZero);

                // From Sage 50 2015 on, Update() works the VAT out from NET_AMOUNT and
                // TAX_CODE itself (TAX_FLAG left at 0), so no TAX_AMOUNT here.
                var item = (SopItem)items.Add();
                Sdo.Set(item.Fields, "STOCK_CODE", res.Code);
                Sdo.Set(item.Fields, "DESCRIPTION", res.Description);
                Sdo.Set(item.Fields, "NOMINAL_CODE", res.Nominal);
                Sdo.Set(item.Fields, "TAX_CODE", res.TaxCode);
                Sdo.Set(item.Fields, "QTY_ORDER", (double)qty);
                Sdo.Set(item.Fields, "UNIT_PRICE", unitPrice);
                Sdo.Set(item.Fields, "NET_AMOUNT", net);
                Sdo.Set(item.Fields, "FULL_NET_AMOUNT", net);
                Sdo.Set(item.Fields, "TAX_RATE", TaxRate(res.TaxCode));
                Sdo.Set(item.Fields, "COMMENT_1", LineComment(line));
                Sdo.Set(item.Fields, "COMMENT_2", "");
                Sdo.Set(item.Fields, "UNIT_OF_SALE", _cfg.QuantityBasis == QuantityBasis.Units ? "Bag" : "Pallet");
                Sdo.Set(item.Fields, "EXT_ORDER_REF", p.Reference ?? "");
                added++;
            }
            if (added == 0)
                throw new SageValidationException("Order " + p.OrderNo + ": every line shipped a quantity of 0 ("
                    + string.Join(", ", skipped) + "), so there is nothing to post.");
            if (skipped.Count > 0)
                Log.Warn("Order " + p.OrderNo + ": " + skipped.Count + " line(s) with nothing shipped left off the Sage order: " + string.Join(", ", skipped));

            // The number Sage is about to hand out — the fallback if the header doesn't
            // report it after the post.
            var expected = 0;
            try { expected = post.GetNextNumber(); } catch { /* informational */ }

            bool ok;
            try
            {
                ok = post.Update();
            }
            catch (Exception ex)
            {
                throw new SageException("Sage rejected order " + p.OrderNo + ": " + Describe(ex), ex);
            }
            if (!ok)
                throw new SageException("Sage did not create order " + p.OrderNo + " (" + LastError() + ").");

            var no = 0;
            try { no = Convert.ToInt32(Sdo.Get(hdr, "ORDER_NUMBER"), CultureInfo.InvariantCulture); } catch { /* fall back */ }
            if (no <= 0) no = expected;
            if (no <= 0)
                throw new SageException("Order " + p.OrderNo + " was posted but Sage did not report its order number - check Sage before retrying.");
            return no;
        }

        // ---- lookups --------------------------------------------------------------------

        private SalesRecord FindCustomer(string accountRef)
        {
            var rec = (SalesRecord)_ws.CreateObject("SalesRecord");
            Sdo.Set(rec.Fields, "ACCOUNT_REF", (accountRef ?? "").Trim().ToUpperInvariant());
            return rec.Find(false) ? rec : null;
        }

        private static bool FindStock(StockRecord rec, string code)
        {
            Sdo.Set(rec.Fields, "STOCK_CODE", (code ?? "").Trim().ToUpperInvariant());
            return rec.Find(false);
        }

        private struct StockInfo
        {
            public string Code;
            public string Nominal;
            public string Description;
            public short TaxCode;
        }

        // A real Sage product takes its nominal and tax code from its stock record; a SKU
        // Sage has never heard of goes on as the configured special product code (S1) with
        // the SKU kept in the description — or fails the order, if NonStockCode is blank.
        private StockInfo ResolveStock(StockRecord stock, SalesOrderLine line)
        {
            var code = (line.StockCode ?? "").Trim().ToUpperInvariant();
            var desc = (line.Description ?? "").Trim();

            if (code.Length > 0 && FindStock(stock, code))
            {
                return new StockInfo
                {
                    Code = code,
                    Nominal = Sdo.GetString(stock.Fields, "NOMINAL_CODE"),
                    TaxCode = Sdo.GetInt16(stock.Fields, "TAX_CODE"),
                    Description = desc.Length > 0 ? desc : Sdo.GetString(stock.Fields, "DESCRIPTION"),
                };
            }

            if (string.IsNullOrWhiteSpace(_cfg.NonStockCode))
                throw new SageValidationException(code.Length == 0
                    ? "Product '" + desc + "' (id " + line.ProductId + ") has no SKU code, and NonStockCode is blank, so it cannot be posted."
                    : "Stock code '" + code + "' (" + desc + ") does not exist in Sage, and NonStockCode is blank, so it cannot be posted.");

            return new StockInfo
            {
                Code = _cfg.NonStockCode,
                Nominal = _cfg.DefaultNominalCode,
                TaxCode = _cfg.DefaultTaxCode,
                Description = code.Length > 0 ? desc + " [" + code + "]" : desc,
            };
        }

        // Sage's own rate for a tax code if it will tell us, else the configured table.
        private double TaxRate(short code)
        {
            double rate;
            if (_rateCache.TryGetValue(code, out rate)) return rate;

            var found = false;
            try
            {
                var tc = (TaxCode)_ws.CreateTaxCode();
                object key = code;
                if (tc.Load(ref key))
                {
                    rate = tc.Rate;
                    found = true;
                }
            }
            catch
            {
                // fall through to the table
            }
            if (!found && !_cfg.TaxRates.TryGetValue(code, out rate))
            {
                Log.Warn("No rate known for tax code T" + code + " - posting it as 0%. Add it to TaxRates in the config.");
                rate = 0;
            }
            _rateCache[code] = rate;
            return rate;
        }

        // ---- helpers --------------------------------------------------------------------

        private static string LineComment(SalesOrderLine l)
        {
            var kg = l.WeightKg > 0 ? ", " + l.WeightKg.ToString("N0", CultureInfo.InvariantCulture) + " kg" : "";
            if (l.IsBulkBag)
                return l.Pallets + " bulk bag" + (l.Pallets == 1 ? "" : "s") + kg;
            return l.Pallets + " pallet" + (l.Pallets == 1 ? "" : "s") + " x " + l.Upp + " = " + l.Units + " bags" + kg;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }

        private static string Join(string sep, params string[] values)
        {
            var parts = new List<string>();
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) parts.Add(v.Trim());
            return string.Join(sep, parts);
        }

        private string Describe(Exception ex)
        {
            var s = ex.Message;
            var com = ex as COMException;
            if (com != null) s += " [HRESULT 0x" + com.ErrorCode.ToString("X8") + "]";
            var last = LastError();
            if (last.Length > 0 && s.IndexOf(last, StringComparison.Ordinal) < 0) s += " (" + last + ")";
            return s;
        }

        private string LastError()
        {
            try
            {
                var e = _engine == null ? null : _engine.LastError;
                if (e == null) return "";
                var text = (e.Text ?? "").Trim();
                return text.Length == 0 ? e.Code.ToString() : e.Code + ": " + text;
            }
            catch { return ""; }
        }

        public void Dispose()
        {
            try { if (_connected && _ws != null) _ws.Disconnect(); } catch { /* going away anyway */ }
            _connected = false;
            Release(ref _ws);
            Release(ref _engine);
        }

        private static void Release<T>(ref T o) where T : class
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { }
            o = null;
        }
    }

    // Typed access to SDO fields. SDO is strict about variant types (a tax code sent as
    // Int32 instead of Int16 is rejected), and silently truncates nothing — an over-long
    // string fails the whole post — so strings are cut to the field's own Length.
    internal static class Sdo
    {
        // A field by name, from a Fields collection or anything that behaves like one:
        // SopPost.Header is typed object but is a Fields collection underneath.
        public static Field FieldOf(object fieldsLike, string name)
        {
            var fields = fieldsLike as Fields;
            if (fields != null)
            {
                object key = name;
                return fields.Item(ref key);
            }
            dynamic d = fieldsLike;
            return (Field)d.Item(name);
        }

        public static void Set(object f, string name, string value)
        {
            var fld = FieldOf(f, name);
            var v = value ?? "";
            int max = fld.Length;
            if (max > 0 && v.Length > max) v = v.Substring(0, max);
            fld.Value = v;
        }

        public static void Set(object f, string name, double value) { FieldOf(f, name).Value = value; }
        public static void Set(object f, string name, short value) { FieldOf(f, name).Value = value; }
        public static void Set(object f, string name, DateTime value) { FieldOf(f, name).Value = value; }

        public static object Get(object f, string name) { return FieldOf(f, name).Value; }

        public static string GetString(object f, string name)
        {
            var v = Get(f, name);
            return v == null ? "" : (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Trim();
        }

        public static short GetInt16(object f, string name)
        {
            var v = Get(f, name);
            return v == null ? (short)0 : Convert.ToInt16(v, CultureInfo.InvariantCulture);
        }

        // Tolerant variants for the read paths: a field name that this Sage version does not
        // have, or a value that will not convert, gives the default instead of an exception.
        public static string TryGetString(object f, string name)
        {
            try { return GetString(f, name); } catch { return ""; }
        }

        public static short TryGetInt16(object f, string name)
        {
            try { return GetInt16(f, name); } catch { return 0; }
        }

        public static double TryGetDouble(object f, string name)
        {
            try
            {
                var v = Get(f, name);
                return v == null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }

        public static DateTime TryGetDate(object f, string name)
        {
            try
            {
                var v = Get(f, name);
                return v == null ? DateTime.MinValue : Convert.ToDateTime(v, CultureInfo.InvariantCulture);
            }
            catch { return DateTime.MinValue; }
        }
    }
}
