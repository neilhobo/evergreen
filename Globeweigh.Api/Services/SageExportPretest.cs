using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

// When a sidecar last asked for work, and which Sage companies it said it can post into.
// In memory only: the sidecar asks every few seconds, so the answer rebuilds itself within
// one poll of an API restart. This is what lets the pre-test say "nothing can be posted
// right now" BEFORE anyone queues orders that would then sit there.
public static class SageSidecarPresence
{
    public sealed record Seen(DateTime Utc, string? Machine, IReadOnlyList<string> Targets, string? TestCompany);

    private static volatile Seen? _last;
    public static Seen? Last => _last;

    public static void Record(string? machine, IEnumerable<string>? targets, string? testCompany) =>
        _last = new Seen(DateTime.UtcNow, machine, (targets ?? []).ToList(), testCompany);

    // Longer than the sidecar's poll and than one slow post; shorter than anyone's patience.
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);
}

// The check run before "Export to Sage" (Completed Orders): for each picked order, would the
// export work, and what would it do that somebody should know about first? Everything here
// is answered from Evergreen's own database — the Sage MIRRORS included (customers, and the
// stock codes seen on Sage's invoices) — because the API cannot ask Sage anything itself.
// So it is a strong prediction, not a guarantee: the sidecar still validates each order
// against the real company when it posts, and reports back per order.
public static class SageExportPretest
{
    public const string Ready = "Ready";
    public const string Warning = "Warning";
    public const string Blocked = "Blocked";

    public const string Block = "block";
    public const string Warn = "warn";

    public sealed record Issue(string Severity, string Text);

    public sealed record OrderCheck(
        int OrderId, int OrderNo, string? CustomerName, string? AccountRef, DateTime? Dispatched,
        int Lines, decimal NetTotal, string Status, List<Issue> Issues);

    public sealed record SidecarInfo(
        bool Online, DateTime? LastSeenUtc, string? Machine, bool SupportsTarget, string? TestCompany);

    public sealed record Result(
        string Target, SidecarInfo Sidecar, List<string> Blockers, List<OrderCheck> Orders,
        int Ready, int Warnings, int Blocked, bool CanExport);

    public static async Task<Result> RunAsync(
        GbEvergreenContext db, IConfiguration config, IEnumerable<int> orderIds, CancellationToken ct)
    {
        var target = SageExport.ExportTarget(config);
        var isLive = target == SageExportJob.TargetLive;
        var ids = orderIds.Distinct().ToList();

        // ---- the things that stop the whole batch ----
        var seen = SageSidecarPresence.Last;
        var online = seen != null && DateTime.UtcNow - seen.Utc < SageSidecarPresence.OnlineWindow;
        var supports = seen != null && seen.Targets.Contains(target, StringComparer.OrdinalIgnoreCase);
        var sidecar = new SidecarInfo(online, seen?.Utc, seen?.Machine, supports, seen?.TestCompany);

        var blockers = new List<string>();
        var key = config["Sage:SidecarKey"];
        if (string.IsNullOrWhiteSpace(key))
            blockers.Add("The Sage link is not set up on this server (no Sage:SidecarKey).");
        else if (seen == null)
            blockers.Add("The Sage sidecar has not asked for work since the server last started. "
                + "It asks every few seconds when it is running, so either it is stopped or it is a version "
                + "from before test exports. Nothing can be posted until it is back.");
        else if (!online)
            blockers.Add($"The Sage sidecar on {seen.Machine ?? "the Sage machine"} has gone quiet "
                + "(it last asked for work over two minutes ago). Nothing can be posted until it is back.");
        else if (!supports)
            blockers.Add(isLive
                ? $"The Sage sidecar on {seen.Machine} is not allowed to post to the live company (LiveExportEnabled is off in its config)."
                : $"The Sage sidecar on {seen.Machine} has no test company set up (TestSageDataPath in its config).");

        // ---- what the checks read ----
        var orders = await db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderNo, o.DateDispatched, o.DateDeleted })
            .ToDictionaryAsync(o => o.Id, ct);

        var jobs = (await db.Set<SageExportJob>().AsNoTracking()
                .Where(j => j.DocType == SageExportJob.SalesOrder
                    && j.Target == target
                    && ids.Contains(j.DispatchOrderId)
                    && j.Status != SageExportJob.Failed)
                .ToListAsync(ct))
            .GroupBy(j => j.DispatchOrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(j => j.Id).First());

        var payloads = new Dictionary<int, SageExport.SalesOrderPayload>();
        foreach (var id in ids)
            if (orders.TryGetValue(id, out var o) && o.DateDeleted == null
                && await SageExport.BuildPayloadAsync(db, id, null, ct) is { } p)
                payloads[id] = p;

        // The customer mirror is the LIVE company's ledger. For a test export it is still the
        // best evidence there is, but the test company may not hold the same accounts.
        var refs = payloads.Values
            .Select(p => (p.AccountRef ?? "").Trim().ToUpperInvariant())
            .Where(r => r.Length > 0).Distinct().ToList();
        var mirrorSynced = await db.Set<SageCustomer>().AsNoTracking().AnyAsync(ct);
        var sageCustomers = await db.Set<SageCustomer>().AsNoTracking()
            .Where(c => refs.Contains(c.AccountRef))
            .Select(c => new { c.AccountRef, c.OnHold, c.Inactive })
            .ToListAsync(ct);
        var customerByRef = sageCustomers
            .GroupBy(c => c.AccountRef.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        // Sage's stock list is not mirrored; the codes its invoices carry are. A SKU that no
        // Sage invoice has ever used is, to a good approximation, not a Sage product.
        var stockCodes = (await db.Set<SageInvoiceLine>().AsNoTracking()
                .Where(l => l.StockCode != null && l.StockCode != "")
                .Select(l => l.StockCode!)
                .Distinct()
                .ToListAsync(ct))
            .Select(c => c.Trim().ToUpperInvariant())
            .ToHashSet();

        // ---- per order ----
        var checks = new List<OrderCheck>();
        foreach (var id in ids)
        {
            var issues = new List<Issue>();
            if (!orders.TryGetValue(id, out var o) || o.DateDeleted != null || !payloads.TryGetValue(id, out var p))
            {
                issues.Add(new Issue(Block, "This order no longer exists (deleted since the list was loaded)."));
                checks.Add(new OrderCheck(id, o?.OrderNo ?? 0, null, null, null, 0, 0, Blocked, issues));
                continue;
            }

            if (o.DateDispatched == null)
                issues.Add(new Issue(Block, "Not dispatched yet — nothing has left, so there is nothing to invoice."));

            if (jobs.TryGetValue(id, out var job))
                issues.Add(new Issue(Block, job.Status == SageExportJob.Done
                    ? $"Already exported: Sage order {job.SageOrderNo} in the {target.ToLowerInvariant()} company"
                        + (job.CompletedUtc is { } done ? $" on {done.ToLocalTime():d MMM yyyy}." : ".")
                    : $"Already on its way to Sage (export job {job.Id}, {job.Status.ToLowerInvariant()})."));

            // Customer
            var accountRef = (p.AccountRef ?? "").Trim().ToUpperInvariant();
            if (accountRef.Length == 0)
                issues.Add(new Issue(Block, $"{p.CustomerName ?? "The customer"} has no account code (Customers > Code), so there is no Sage account to post to."));
            else if (!mirrorSynced)
                issues.Add(new Issue(Warn, "Sage's customer list has not been synced, so the account could not be checked."));
            else if (!customerByRef.TryGetValue(accountRef, out var sc))
                issues.Add(new Issue(isLive ? Block : Warn, isLive
                    ? $"Account {accountRef} is not in Sage."
                    : $"Account {accountRef} is not in live Sage — it will only post if the test company has it."));
            else
            {
                if (sc.OnHold) issues.Add(new Issue(Warn, $"Account {accountRef} is ON HOLD in Sage."));
                if (sc.Inactive) issues.Add(new Issue(Warn, $"Account {accountRef} is marked inactive in Sage."));
            }

            // Lines
            var shipped = p.Lines.Where(l => l.Units > 0).ToList();
            if (p.Lines.Count == 0)
                issues.Add(new Issue(Block, "The order has no lines."));
            else if (shipped.Count == 0)
                issues.Add(new Issue(Block, "Every line shipped a quantity of 0, so there is nothing to post."));
            else
            {
                if (shipped.Count < p.Lines.Count)
                    issues.Add(new Issue(Warn, Count(p.Lines.Count - shipped.Count, "line") + " with nothing shipped will be left off."));

                var noPrice = shipped.Where(l => l.UnitPrice == null).ToList();
                if (noPrice.Count > 0)
                    issues.Add(new Issue(Warn, Count(noPrice.Count, "line") + " with no price will post at 0.00: "
                        + Names(noPrice) + "."));

                if (stockCodes.Count > 0)
                {
                    var nonStock = shipped
                        .Where(l => string.IsNullOrWhiteSpace(l.StockCode) || !stockCodes.Contains(l.StockCode.Trim().ToUpperInvariant()))
                        .ToList();
                    if (nonStock.Count > 0)
                        issues.Add(new Issue(Warn, Count(nonStock.Count, "line") + " of " + shipped.Count
                            + " will post as a special item (S1) rather than a Sage product, because the SKU is not a Sage stock code: "
                            + Names(nonStock) + "."));
                }
            }

            var net = shipped.Sum(l => Math.Round(l.Units * (l.UnitPrice ?? 0m), 2, MidpointRounding.AwayFromZero));
            var status = issues.Any(i => i.Severity == Block) ? Blocked : issues.Count > 0 ? Warning : Ready;
            checks.Add(new OrderCheck(id, p.OrderNo, p.CustomerName, accountRef.Length > 0 ? accountRef : null,
                o.DateDispatched, shipped.Count, net, status, issues));
        }

        var ready = checks.Count(c => c.Status == Ready);
        var warnings = checks.Count(c => c.Status == Warning);
        var blocked = checks.Count(c => c.Status == Blocked);
        return new Result(target, sidecar, blockers, checks, ready, warnings, blocked,
            blockers.Count == 0 && ready + warnings > 0);
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string Names(List<SageExport.SalesOrderLine> lines)
    {
        var names = lines
            .Select(l => string.IsNullOrWhiteSpace(l.Description) ? (l.StockCode ?? $"product {l.ProductId}") : l.Description!.Trim())
            .Distinct().ToList();
        return names.Count <= 3 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + $" and {names.Count - 3} more";
    }
}
