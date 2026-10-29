using System.Text;
using System.Text.RegularExpressions;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

// Sage is the source of truth for customers. This compares what Evergreen holds
// (CustomerSuppliers + CustomerSupplier_Address) with the Sage mirror (SageCustomer +
// SageCustomerAddress) and lists everything out of date on the Evergreen side — one row per
// issue — for Admin > Sage Link > Comparison and its CSV export. The one thing it changes is
// "Copy to Evergreen": creating the customers Sage has and Evergreen does not (see below).
//
// Evergreen's addresses carry no link to Sage's (the old import never filled SageAccountRef /
// AddressSageRef), so addresses are matched by content: exact on all five lines, else on the
// first line + postcode and reported as "changed" with the lines that differ. Sage's address
// book repeats the same lines under several types (registered, trade contact), so both sides
// are compared as sets of DISTINCT addresses; copies on the Evergreen side are a hygiene issue
// of their own.
public static class SageComparison
{
    // Category names double as the CSV's first column, so they read as English.
    public const string MissingInEvergreen = "Missing in Evergreen";
    public const string NotInSage = "Not in Sage";
    public const string DuplicateCustomer = "Duplicate code in Evergreen";
    public const string NameDiffers = "Name differs";
    public const string InactiveDiffers = "Inactive flag differs";
    public const string OnHoldInSage = "On hold in Sage";
    public const string AddressMissing = "Address missing in Evergreen";
    public const string AddressNotInSage = "Address not in Sage";
    public const string AddressChanged = "Address changed";
    public const string AddressDetailDiffers = "Address detail differs";
    public const string AddressDetailBlank = "Address detail blank in Evergreen";
    public const string DuplicateAddress = "Duplicate address in Evergreen";
    // Not an issue: Missing in Evergreen accounts the office has chosen never to copy. Listed
    // so they can be found and un-ignored; left out of every issue count.
    public const string Ignored = "Ignored";

    // The order the categories are listed and sorted in: the ones that break an export or a
    // delivery first, hygiene last.
    public static readonly string[] Categories =
    [
        MissingInEvergreen, NotInSage, OnHoldInSage, NameDiffers, InactiveDiffers,
        AddressMissing, AddressChanged, AddressNotInSage, AddressDetailDiffers,
        AddressDetailBlank, DuplicateAddress, DuplicateCustomer, Ignored,
    ];

    public record Issue(
        string Category,
        string AccountRef,
        string? Name,                   // the Evergreen name when there is one, else Sage's
        string Detail,                  // which field / which address
        string? Evergreen,              // Evergreen's value
        string? Sage,                   // Sage's value
        string? SageAddressType,
        int? EvergreenCustomerId,
        int Orders,
        DateTime? LastOrder);

    public record Summary(
        int SageCustomers,
        int EvergreenCustomers,
        int Matched,
        int CustomersWithIssues,
        int CustomersWithOrdersWithIssues,
        Dictionary<string, int> ByCategory);

    public record Result(Summary Summary, List<Issue> Issues);

    // One account as both systems hold it, for the side-by-side panel.
    public record SideBySide(string AccountRef, CustomerSide? Evergreen, CustomerSide? Sage);
    public record CustomerSide(
        int? Id, string? Name, bool Inactive, bool? OnHold, short? TaxCode, decimal? Balance,
        int Orders, DateTime? LastOrder, List<AddressSide> Addresses);
    public record AddressSide(
        int? Id, int? Number, string? Type, string? Name, string? Description, List<string> Lines,
        string? Telephone, string? Contact, string? Email, DateTime? Modified);

    // ---- build ----------------------------------------------------------------------------

    // includeBlanks: also report a telephone / contact / email that Sage has and Evergreen has
    // left empty. Off by default — there are thousands, and they drown the conflicts.
    public static async Task<Result> BuildAsync(GbEvergreenContext db, bool includeBlanks, CancellationToken ct)
    {
        var data = await Load(db, ct);
        var issues = new List<Issue>();

        // No mirror yet (the sidecar has never delivered): there is nothing to compare
        // against, and listing every Evergreen customer as "Not in Sage" would be exactly
        // the wrong report. Empty, with the counts the page needs to say so.
        if (data.SageCustomers.Count == 0)
            return new Result(
                new Summary(0, data.EvCustomers.Count, 0, 0, 0, Categories.ToDictionary(c => c, _ => 0)),
                issues);

        // Evergreen customers by code (first wins; the rest are an issue of their own).
        var evByCode = new Dictionary<string, CustomerSupplier>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in data.EvCustomers.OrderBy(c => c.Id))
        {
            var code = Key(c.Code);
            if (code.Length == 0) continue;
            if (evByCode.TryGetValue(code, out var first))
            {
                issues.Add(Row(DuplicateCustomer, code, c.Name, $"Evergreen ids {first.Id} and {c.Id}", c.Name, first.Name, null, c.Id, data.OrdersOf(c.Id)));
                continue;
            }
            evByCode[code] = c;
        }
        var sageByCode = data.SageCustomers.ToDictionary(s => s.AccountRef, StringComparer.OrdinalIgnoreCase);
        var evAddrByCust = data.EvAddresses.ToLookup(a => a.CustomerSupplierId);
        var sageAddrByCode = data.SageAddresses.ToLookup(a => a.AccountRef, StringComparer.OrdinalIgnoreCase);

        var matched = 0;
        foreach (var (code, ev) in evByCode)
        {
            var (orders, last) = data.OrdersOf(ev.Id);
            if (!sageByCode.TryGetValue(code, out var sage))
            {
                issues.Add(Row(NotInSage, code, ev.Name, "No Sage account with this code", ev.Name, null, null, ev.Id, (orders, last)));
                continue;
            }
            matched++;

            if (NormName(ev.Name) != NormName(sage.Name))
                issues.Add(Row(NameDiffers, code, ev.Name, "Customer name", ev.Name, sage.Name, null, ev.Id, (orders, last)));
            if (ev.Inactive != sage.Inactive)
                issues.Add(Row(InactiveDiffers, code, ev.Name, "Inactive flag", Flag(ev.Inactive), Flag(sage.Inactive), null, ev.Id, (orders, last)));
            if (sage.OnHold)
                issues.Add(Row(OnHoldInSage, code, ev.Name, "Account is on hold in Sage", ev.Inactive ? "Inactive" : "Active", "On hold", null, ev.Id, (orders, last)));

            CompareAddresses(issues, code, ev, evAddrByCust[ev.Id].ToList(), sageAddrByCode[code].ToList(), includeBlanks, (orders, last));
        }

        foreach (var sage in data.SageCustomers)
        {
            if (evByCode.ContainsKey(sage.AccountRef)) continue;
            if (data.Ignored.TryGetValue(Key(sage.AccountRef), out var ign))
            {
                issues.Add(Row(Ignored, sage.AccountRef, sage.Name,
                    $"Ignored by {data.UserNames.GetValueOrDefault(ign.IgnoredById) ?? "—"} on {ign.DateIgnored:dd/MM/yyyy}",
                    null, Join(" · ", sage.Name, sage.Telephone), null, null, (0, null)));
                continue;
            }
            var where = Join(", ", sage.Address3, sage.Address5);
            issues.Add(Row(MissingInEvergreen, sage.AccountRef, sage.Name, where.Length > 0 ? where : "No Evergreen customer with this code",
                null, Join(" · ", sage.Name, sage.Telephone), null, null, (0, null)));
        }

        var order = Categories.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        issues = issues
            .OrderBy(i => order.GetValueOrDefault(i.Category, 99))
            .ThenByDescending(i => i.Orders)
            .ThenBy(i => i.AccountRef, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byCategory = Categories.ToDictionary(c => c, c => 0);
        foreach (var i in issues) byCategory[i.Category] = byCategory.GetValueOrDefault(i.Category) + 1;
        var withIssues = issues.Where(i => i.Category != Ignored).Select(i => i.AccountRef).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var withOrders = issues.Where(i => i.Orders > 0 && i.Category != Ignored).Select(i => i.AccountRef).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return new Result(
            new Summary(data.SageCustomers.Count, evByCode.Count, matched, withIssues, withOrders, byCategory),
            issues);
    }

    // The two sides of one account, every address included, for the detail panel.
    public static async Task<SideBySide?> DetailAsync(GbEvergreenContext db, string accountRef, CancellationToken ct)
    {
        var code = Key(accountRef);
        var ev = await db.Set<CustomerSupplier>().AsNoTracking()
            .Where(c => c.DateDeleted == null && c.TypeId == 1 && c.Code != null && c.Code.Trim().ToUpper() == code)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(ct);
        var sage = await db.Set<SageCustomer>().AsNoTracking().FirstOrDefaultAsync(s => s.AccountRef == code, ct);
        if (ev == null && sage == null) return null;

        CustomerSide? evSide = null;
        if (ev != null)
        {
            var addrs = await db.Set<CustomerSupplierAddress>().AsNoTracking()
                .Where(a => a.CustomerSupplierId == ev.Id).OrderBy(a => a.Id).ToListAsync(ct);
            var orders = await db.Set<DispatchOrderEntity>().AsNoTracking()
                .Where(o => o.CustomerId == ev.Id && o.DateDeleted == null)
                .GroupBy(o => 1)
                .Select(g => new { Count = g.Count(), Last = g.Max(o => o.DateCreated) })
                .FirstOrDefaultAsync(ct);
            evSide = new CustomerSide(ev.Id, ev.Name, ev.Inactive, null, null, null, orders?.Count ?? 0, orders?.Last,
                addrs.Select(a => new AddressSide(a.Id, null, null, a.AddressName, null,
                    Lines(a.Address1, a.Address2, a.Address3, a.Address4, a.Address5),
                    a.Telephone, a.ContactName, a.Email, a.DateLastModified)).ToList());
        }

        CustomerSide? sageSide = null;
        if (sage != null)
        {
            var addrs = await db.Set<SageCustomerAddress>().AsNoTracking()
                .Where(a => a.AccountRef == code).OrderBy(a => a.AddressNumber).ToListAsync(ct);
            sageSide = new CustomerSide(null, sage.Name, sage.Inactive, sage.OnHold, sage.DefTaxCode, sage.Balance, 0, null,
                addrs.Select(a => new AddressSide(null, a.AddressNumber, a.AddressType, a.Name, a.Description,
                    Lines(a.Address1, a.Address2, a.Address3, a.Address4, a.Address5),
                    a.Telephone, a.Contact, a.Email, null)).ToList());
        }

        return new SideBySide(code, evSide, sageSide);
    }

    // The issues as a CSV Excel opens cleanly (UTF-8 with BOM, CRLF, everything quoted).
    public static byte[] ToCsv(IEnumerable<Issue> issues)
    {
        var sb = new StringBuilder();
        sb.Append("Category,A/C,Name,Detail,Evergreen,Sage,Sage address type,Orders,Last order\r\n");
        foreach (var i in issues)
        {
            sb.Append(string.Join(",",
                Q(i.Category), Q(i.AccountRef), Q(i.Name), Q(i.Detail), Q(i.Evergreen), Q(i.Sage),
                Q(i.SageAddressType), i.Orders.ToString(), Q(i.LastOrder?.ToString("yyyy-MM-dd"))));
            sb.Append("\r\n");
        }
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return [.. Encoding.UTF8.GetPreamble(), .. body];
    }

    // ---- copy "Missing in Evergreen" across ------------------------------------------------

    // What "Copy to Evergreen" would create: each Sage account Evergreen has no customer for,
    // with the addresses it would get. The Sage address book is taken as the comparison reads
    // it — one address per distinct set of lines, the delivery flavour first — and an account
    // with an empty book gets its main (registered) address instead. Accounts that cannot be
    // copied are listed with the reason, never created.
    public record ImportAddress(int? SageNumber, string? Type, string? Name, string? Description, List<string> Lines,
        string? Telephone, string? Contact, string? Email);
    public record ImportCustomer(string AccountRef, string Name, bool Inactive, bool OnHold, List<ImportAddress> Addresses);
    public record ImportSkip(string AccountRef, string? Name, string Reason);
    public record ImportPlan(List<ImportCustomer> Customers, List<ImportSkip> Skipped);
    public record ImportResult(int Customers, int Addresses, int NoLongerMissing);

    public static async Task<ImportPlan> PlanMissingAsync(GbEvergreenContext db, CancellationToken ct)
    {
        var plan = new ImportPlan([], []);
        var sage = await db.Set<SageCustomer>().AsNoTracking().OrderBy(s => s.AccountRef).ToListAsync(ct);
        if (sage.Count == 0) return plan;

        // Every live code in Evergreen. A customer with the code means the account is not
        // missing (the comparison's own test); a supplier with it would make a second record
        // under one code, so that account is held back for someone to look at.
        var held = await db.Set<CustomerSupplier>().AsNoTracking()
            .Where(c => c.DateDeleted == null && c.Code != null)
            .Select(c => new { c.Code, c.TypeId })
            .ToListAsync(ct);
        var customerCodes = held.Where(c => c.TypeId == 1).Select(c => Key(c.Code)).ToHashSet();
        var supplierCodes = held.Where(c => c.TypeId != 1).Select(c => Key(c.Code)).ToHashSet();
        // Accounts the office has said never to copy are not offered at all.
        var ignored = (await db.Set<SageIgnoredAccount>().AsNoTracking().Select(i => i.AccountRef).ToListAsync(ct))
            .Select(Key).ToHashSet();
        var book = (await db.Set<SageCustomerAddress>().AsNoTracking().ToListAsync(ct))
            .ToLookup(a => a.AccountRef, StringComparer.OrdinalIgnoreCase);

        foreach (var s in sage)
        {
            var code = Key(s.AccountRef);
            if (customerCodes.Contains(code) || ignored.Contains(code)) continue;
            if (supplierCodes.Contains(code))
            {
                plan.Skipped.Add(new ImportSkip(code, s.Name, "A supplier in Evergreen already uses this code"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(s.Name))
            {
                plan.Skipped.Add(new ImportSkip(code, null, "No name in Sage"));
                continue;
            }

            var addresses = book[code]
                .OrderBy(a => TypeRank(a.AddressType)).ThenBy(a => a.AddressNumber)
                .Select(a => (a, lines: Lines(a.Address1, a.Address2, a.Address3, a.Address4, a.Address5)))
                .Where(x => x.lines.Count > 0)
                .GroupBy(x => Norm(string.Concat(x.lines))).Select(g => g.First())
                .Select(x => new ImportAddress(x.a.AddressNumber, x.a.AddressType, x.a.Name, x.a.Description, x.lines,
                    x.a.Telephone, x.a.Contact, x.a.Email))
                .ToList();
            if (addresses.Count == 0)
            {
                var main = Lines(s.Address1, s.Address2, s.Address3, s.Address4, s.Address5);
                if (main.Count > 0)
                    addresses.Add(new ImportAddress(null, "Registered", s.DelName, null, main, s.Telephone, s.Contact, s.Email));
            }
            plan.Customers.Add(new ImportCustomer(code, s.Name.Trim(), s.Inactive, s.OnHold, addresses));
        }
        return plan;
    }

    // Create the customers (and their addresses) for the given accounts — the ones the person
    // saw in the summary. The plan is worked out again here, so an account someone added by
    // hand in the meantime is left alone rather than duplicated. One transaction: all or none.
    public static async Task<ImportResult> ImportMissingAsync(
        GbEvergreenContext db, IEnumerable<string> accountRefs, int userId, CancellationToken ct)
    {
        var wanted = accountRefs.Select(Key).Where(k => k.Length > 0).ToHashSet();
        var plan = await PlanMissingAsync(db, ct);
        var todo = plan.Customers.Where(c => wanted.Contains(c.AccountRef)).ToList();
        if (todo.Count == 0) return new ImportResult(0, 0, wanted.Count);

        var now = DateTime.Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var made = todo.Select(c => (plan: c, row: new CustomerSupplier
        {
            TypeId = 1,
            Code = c.AccountRef,
            Name = c.Name,
            Inactive = c.Inactive,
            CostingZoneId = 0,
            DateCreated = now,
            CreatedById = userId,
            DeletedById = 0,
        })).ToList();
        db.AddRange(made.Select(m => m.row));
        await db.SaveChangesAsync(ct);

        var addresses = 0;
        foreach (var (c, row) in made)
            foreach (var a in c.Addresses)
            {
                string? Line(int i) => i < a.Lines.Count ? Cap(a.Lines[i]) : null;
                db.Add(new CustomerSupplierAddress
                {
                    CustomerSupplierId = row.Id,
                    AddressName = Cap(a.Name),
                    Address1 = Line(0),
                    Address2 = Line(1),
                    Address3 = Line(2),
                    Address4 = Line(3),
                    Address5 = Line(4),
                    Telephone = Cap(a.Telephone),
                    ContactName = Cap(a.Contact),
                    Email = Cap(a.Email),
                    // The link the old import never made: these addresses know where they came from.
                    SageAccountRef = c.AccountRef,
                    AddressSageRef = a.SageNumber ?? 0,
                    SageDescription = Cap(a.Description),
                    DateCreated = now,
                    DateLastModified = now,
                });
                addresses++;
            }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ImportResult(made.Count, addresses, wanted.Count - todo.Count);

        // The address columns are nvarchar(100); Sage's are shorter, but never trust that.
        static string? Cap(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > 100 ? s.Trim()[..100] : s.Trim());
    }

    // Ignore / stop ignoring Sage accounts (Missing in Evergreen <-> Ignored). Idempotent: an
    // account already in the wanted state is left as it is. Returns how many changed.
    public static async Task<int> SetIgnoredAsync(
        GbEvergreenContext db, IEnumerable<string> accountRefs, bool ignore, int userId, CancellationToken ct)
    {
        var refs = accountRefs.Select(Key).Where(k => k.Length is > 0 and <= 20).Distinct().ToList();
        var existing = await db.Set<SageIgnoredAccount>().Where(i => refs.Contains(i.AccountRef)).ToListAsync(ct);
        int changed;
        if (ignore)
        {
            var have = existing.Select(i => Key(i.AccountRef)).ToHashSet();
            var add = refs.Where(r => !have.Contains(r))
                .Select(r => new SageIgnoredAccount { AccountRef = r, IgnoredById = userId, DateIgnored = DateTime.Now })
                .ToList();
            db.AddRange(add);
            changed = add.Count;
        }
        else
        {
            db.RemoveRange(existing);
            changed = existing.Count;
        }
        await db.SaveChangesAsync(ct);
        return changed;
    }

    // ---- addresses ------------------------------------------------------------------------

    private sealed record Addr(int Id, int Number, string? Type, string? Name, List<string> Lines, string Key, string Key1,
        string? Telephone, string? Contact, string? Email);

    private static void CompareAddresses(List<Issue> issues, string code, CustomerSupplier ev,
        List<CustomerSupplierAddress> evAddrs, List<SageCustomerAddress> sageAddrs, bool includeBlanks,
        (int Orders, DateTime? Last) orders)
    {
        var e = evAddrs
            .Select(a => Make(a.Id, 0, null, a.AddressName, a.Address1, a.Address2, a.Address3, a.Address4, a.Address5, a.Telephone, a.ContactName, a.Email))
            .Where(a => a.Lines.Count > 0)
            .ToList();
        // Sage's book, one entry per distinct set of lines; the delivery flavour wins where the
        // same lines appear under several types.
        var s = sageAddrs
            .OrderBy(a => TypeRank(a.AddressType)).ThenBy(a => a.AddressNumber)
            .Select(a => Make(0, a.AddressNumber, a.AddressType, a.Name, a.Address1, a.Address2, a.Address3, a.Address4, a.Address5, a.Telephone, a.Contact, a.Email))
            .Where(a => a.Lines.Count > 0)
            .GroupBy(a => a.Key).Select(g => g.First())
            .ToList();

        // Copies on the Evergreen side.
        foreach (var g in e.GroupBy(a => a.Key).Where(g => g.Count() > 1))
            issues.Add(Row(DuplicateAddress, code, ev.Name, string.Join(", ", g.First().Lines), $"{g.Count()} copies (ids {string.Join(", ", g.Select(a => a.Id))})", null, null, ev.Id, orders));
        var eDistinct = e.GroupBy(a => a.Key).Select(g => g.First()).ToList();

        var used = new HashSet<int>();
        foreach (var sa in s)
        {
            var exact = eDistinct.FirstOrDefault(a => a.Key == sa.Key && !used.Contains(a.Id));
            if (exact != null)
            {
                used.Add(exact.Id);
                CompareDetails(issues, code, ev, exact, sa, includeBlanks, orders);
                continue;
            }
            var near = eDistinct.FirstOrDefault(a => a.Key1 == sa.Key1 && !used.Contains(a.Id));
            if (near != null)
            {
                used.Add(near.Id);
                issues.Add(Row(AddressChanged, code, ev.Name, LineDiff(near.Lines, sa.Lines),
                    string.Join(", ", near.Lines), string.Join(", ", sa.Lines), sa.Type, ev.Id, orders));
                CompareDetails(issues, code, ev, near, sa, includeBlanks, orders);
                continue;
            }
            issues.Add(Row(AddressMissing, code, ev.Name, string.Join(", ", sa.Lines), null,
                Join(" · ", sa.Name, string.Join(", ", sa.Lines), sa.Telephone), sa.Type, ev.Id, orders));
        }
        foreach (var ea in eDistinct.Where(a => !used.Contains(a.Id)))
            issues.Add(Row(AddressNotInSage, code, ev.Name, string.Join(", ", ea.Lines),
                Join(" · ", ea.Name, string.Join(", ", ea.Lines), ea.Telephone), null, null, ev.Id, orders));
    }

    // Telephone / contact / email / name on a matched address. A value on both sides that
    // differs is a conflict; a value Sage has that Evergreen left blank is a gap (only when
    // asked for, and never for the address name — Evergreen rarely fills that in).
    private static void CompareDetails(List<Issue> issues, string code, CustomerSupplier ev, Addr e, Addr s, bool includeBlanks,
        (int Orders, DateTime? Last) orders)
    {
        var where = string.Join(", ", s.Lines);
        Check("Telephone", e.Telephone, s.Telephone, Digits, true);
        Check("Contact", e.Contact, s.Contact, NormName, true);
        Check("Email", e.Email, s.Email, v => (v ?? "").Trim().ToLowerInvariant(), true);
        Check("Address name", e.Name, s.Name, NormName, false);

        void Check(string field, string? evValue, string? sageValue, Func<string?, string> norm, bool gapCounts)
        {
            var evBlank = string.IsNullOrWhiteSpace(evValue);
            var sageBlank = string.IsNullOrWhiteSpace(sageValue);
            if (sageBlank) return;
            if (evBlank)
            {
                if (includeBlanks && gapCounts)
                    issues.Add(Row(AddressDetailBlank, code, ev.Name, $"{field} — {where}", null, sageValue, s.Type, ev.Id, orders));
                return;
            }
            if (norm(evValue) != norm(sageValue))
                issues.Add(Row(AddressDetailDiffers, code, ev.Name, $"{field} — {where}", evValue, sageValue, s.Type, ev.Id, orders));
        }
    }

    private static Addr Make(int id, int number, string? type, string? name, string? l1, string? l2, string? l3, string? l4, string? l5,
        string? tel, string? contact, string? email)
    {
        var lines = Lines(l1, l2, l3, l4, l5);
        var key = Norm(string.Concat(lines));
        var key1 = lines.Count == 0 ? "" : Norm(lines[0]) + "|" + PostcodeOf(lines);
        return new Addr(id, number, type, name, lines, key, key1, tel, contact, email);
    }

    private static string LineDiff(List<string> ev, List<string> sage)
    {
        var parts = new List<string>();
        for (var i = 0; i < Math.Max(ev.Count, sage.Count); i++)
        {
            var a = i < ev.Count ? ev[i] : "";
            var b = i < sage.Count ? sage[i] : "";
            if (Norm(a) != Norm(b)) parts.Add($"line {i + 1}: '{a}' → '{b}'");
        }
        return parts.Count == 0 ? "Lines differ" : string.Join("; ", parts);
    }

    private static int TypeRank(string? type) => type switch
    {
        SageCustomerAddress.Delivery => 0,
        "Registered" => 1,
        _ => 2,
    };

    // ---- data -----------------------------------------------------------------------------

    private sealed class Loaded
    {
        public List<CustomerSupplier> EvCustomers = [];
        public List<CustomerSupplierAddress> EvAddresses = [];
        public Dictionary<int, (int Count, DateTime? Last)> Orders = [];
        public List<SageCustomer> SageCustomers = [];
        public List<SageCustomerAddress> SageAddresses = [];
        public Dictionary<string, SageIgnoredAccount> Ignored = [];
        public Dictionary<int, string> UserNames = [];
        public (int, DateTime?) OrdersOf(int customerId) => Orders.TryGetValue(customerId, out var o) ? o : (0, null);
    }

    private static async Task<Loaded> Load(GbEvergreenContext db, CancellationToken ct)
    {
        var d = new Loaded
        {
            EvCustomers = await db.Set<CustomerSupplier>().AsNoTracking()
                .Where(c => c.DateDeleted == null && c.TypeId == 1)
                .ToListAsync(ct),
            EvAddresses = await (
                from a in db.Set<CustomerSupplierAddress>().AsNoTracking()
                join c in db.Set<CustomerSupplier>().AsNoTracking() on a.CustomerSupplierId equals c.Id
                where c.DateDeleted == null && c.TypeId == 1
                select a).ToListAsync(ct),
            SageCustomers = await db.Set<SageCustomer>().AsNoTracking().ToListAsync(ct),
            SageAddresses = await db.Set<SageCustomerAddress>().AsNoTracking().ToListAsync(ct),
        };
        d.Orders = await db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => o.DateDeleted == null)
            .GroupBy(o => o.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count(), Last = g.Max(o => o.DateCreated) })
            .ToDictionaryAsync(x => x.CustomerId, x => (x.Count, x.Last), ct);
        d.Ignored = (await db.Set<SageIgnoredAccount>().AsNoTracking().ToListAsync(ct))
            .ToDictionary(i => Key(i.AccountRef));
        var who = d.Ignored.Values.Select(i => i.IgnoredById).Distinct().ToList();
        d.UserNames = await db.GlobeweighUsers.AsNoTracking()
            .Where(u => who.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => ((u.FirstName ?? "") + " " + (u.Surname ?? "")).Trim(), ct);
        return d;
    }

    // ---- helpers --------------------------------------------------------------------------

    private static Issue Row(string category, string code, string? name, string detail, string? ev, string? sage,
        string? sageType, int? evId, (int Orders, DateTime? Last) orders) =>
        new(category, code, name, detail, ev, sage, sageType, evId, orders.Orders, orders.Last);

    private static List<string> Lines(params string?[] lines) =>
        lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim()).ToList();

    private static string Key(string? code) => (code ?? "").Trim().ToUpperInvariant();

    // Letters and digits only, upper-cased: "01329-842225" and "01329 842225" are one value.
    private static string Norm(string? s) => s == null ? "" : NonAlnum.Replace(s.ToUpperInvariant(), "");
    private static string Digits(string? s) => s == null ? "" : NonDigit.Replace(s, "");
    private static string NormName(string? s) => s == null ? "" : Spaces.Replace(s.Trim().ToUpperInvariant(), " ");
    private static string Flag(bool inactive) => inactive ? "Inactive" : "Active";
    private static string Join(string sep, params string?[] parts) =>
        string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    // The postcode is the last line that looks like one; else the last line.
    private static string PostcodeOf(List<string> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
            if (Postcode.IsMatch(lines[i].Trim())) return Norm(lines[i]);
        return lines.Count == 0 ? "" : Norm(lines[^1]);
    }

    private static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    private static readonly Regex NonAlnum = new("[^A-Z0-9]", RegexOptions.Compiled);
    private static readonly Regex NonDigit = new("[^0-9]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Postcode = new(@"^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
