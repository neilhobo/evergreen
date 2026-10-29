using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// The Sage customer mirror behind Admin > Sage Link > Customers (Data/Entities/
// SageCustomer.cs). Same split as SageController: admin reads with the JWT; the sidecar on
// the Sage machine fills the mirror with the shared X-Sidecar-Key. The API itself never
// talks to Sage — the sidecar is the only thing that can — so "Refresh from Sage" is a
// request flag the sidecar picks up on its next poll, not a synchronous call.
[ApiController]
[Authorize]
[Route("api/sage")]
public class SageCustomersController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public SageCustomersController(GbEvergreenContext db) => _db = db;

    // ---- Admin ------------------------------------------------------------------------

    // GET /api/sage/customers — every mirrored customer with its address counts, plus the
    // sync state so the page can say how fresh the list is, in one call.
    [HttpGet("customers")]
    public async Task<ActionResult<SageCustomersDto>> Customers(CancellationToken ct = default)
    {
        var counts = await _db.Set<SageCustomerAddress>().AsNoTracking()
            .GroupBy(a => a.AccountRef)
            .Select(g => new
            {
                Ref = g.Key,
                Total = g.Count(),
                Delivery = g.Count(a => a.AddressType == SageCustomerAddress.Delivery),
            })
            .ToDictionaryAsync(x => x.Ref, ct);

        var rows = await _db.Set<SageCustomer>().AsNoTracking()
            .OrderBy(c => c.AccountRef)
            .ToListAsync(ct);

        var customers = rows.Select(c =>
        {
            counts.TryGetValue(c.AccountRef, out var n);
            return new SageCustomerDto(
                c.Id, c.AccountRef, c.Name, c.Address1, c.Address2, c.Address3, c.Address4, c.Address5,
                c.Contact, c.Telephone, c.Email, c.DefTaxCode, c.Balance, c.OnHold, c.Inactive,
                n?.Total ?? 0, n?.Delivery ?? 0, Utc(c.LastSeenUtc));
        }).ToList();

        return new SageCustomersDto(ToDto(await State(ct)), customers);
    }

    // GET /api/sage/customers/{accountRef}/addresses — the address book for one account.
    [HttpGet("customers/{accountRef}/addresses")]
    public async Task<ActionResult<IEnumerable<SageAddressDto>>> Addresses(string accountRef, CancellationToken ct = default)
    {
        var key = accountRef.Trim().ToUpperInvariant();
        return await _db.Set<SageCustomerAddress>().AsNoTracking()
            .Where(a => a.AccountRef == key)
            .OrderBy(a => a.AddressNumber)
            .Select(a => new SageAddressDto(
                a.AddressNumber, a.Name, a.Description, a.AddressType,
                a.Address1, a.Address2, a.Address3, a.Address4, a.Address5,
                a.Telephone, a.Contact, a.Email))
            .ToListAsync(ct);
    }

    // GET /api/sage/sync — every mirrored entity's state (the page polls this after asking
    // for a refresh, until LastRunUtc moves).
    [HttpGet("sync")]
    public async Task<ActionResult<IEnumerable<SageSyncDto>>> Sync(CancellationToken ct = default) =>
        (await _db.Set<SageSyncState>().AsNoTracking().OrderBy(s => s.Entity).ToListAsync(ct))
            .Select(ToDto).ToList();

    // POST /api/sage/sync/customers/refresh — ask the sidecar for a fresh pull. It polls the
    // API every few seconds, so this lands within about half a minute while it is running;
    // if it is not running the request simply waits for it.
    [HttpPost("sync/customers/refresh")]
    public async Task<ActionResult<SageSyncDto>> RequestRefresh(CancellationToken ct = default)
    {
        var state = await State(ct);
        state.RequestedUtc = DateTime.UtcNow;
        state.RequestedBy = User.FindFirst("name")?.Value;
        await _db.SaveChangesAsync(ct);
        return ToDto(state);
    }

    // POST /api/sage/sync/customers/cancel — withdraw a refresh: a request the sidecar has not
    // picked up yet is dropped, and a pull in progress stops within a few seconds (the sidecar
    // checks CancelRequestedUtc as it reads). The mirror keeps what it had.
    [HttpPost("sync/customers/cancel")]
    public async Task<ActionResult<SageSyncDto>> CancelRefresh(CancellationToken ct = default)
    {
        var state = await State(ct);
        state.RequestedUtc = null;
        state.RequestedBy = null;
        state.CancelRequestedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(state);
    }

    // ---- Comparison (Sage as the source of truth) ---------------------------------------

    // GET /api/sage/comparison?includeBlanks=false — everything out of date on the Evergreen
    // side, one row per issue, with the sync state so the page can say how fresh the Sage
    // side is. See Services/SageComparison.cs for the rules.
    [HttpGet("comparison")]
    public async Task<ActionResult> Comparison([FromQuery] bool includeBlanks = false, CancellationToken ct = default)
    {
        var result = await SageComparison.BuildAsync(_db, includeBlanks, ct);
        return Ok(new { sync = ToDto(await State(ct)), result.Summary, result.Issues });
    }

    // GET /api/sage/comparison/missing — what "Copy to Evergreen" would create for the
    // "Missing in Evergreen" accounts: each customer with its addresses, and any account that
    // will be held back with the reason. Changes nothing; the confirm popup shows it.
    [HttpGet("comparison/missing")]
    public async Task<ActionResult<SageComparison.ImportPlan>> MissingPlan(CancellationToken ct = default)
        => await SageComparison.PlanMissingAsync(_db, ct);

    // POST /api/sage/comparison/missing/import { accountRefs } — create those customers. Only
    // the accounts the popup showed (and still missing) are created; see ImportMissingAsync.
    [HttpPost("comparison/missing/import")]
    public async Task<ActionResult<SageComparison.ImportResult>> ImportMissing(
        [FromBody] SageImportInput input, CancellationToken ct = default)
        => await SageComparison.ImportMissingAsync(_db, input.AccountRefs, this.GetUserId(), ct);

    // POST /api/sage/comparison/ignore { accountRefs } — accounts that will never be copied:
    // they leave Missing in Evergreen (and Copy to Evergreen) for the Ignored category.
    // POST /api/sage/comparison/unignore { accountRefs } — put them back.
    [HttpPost("comparison/ignore")]
    public async Task<ActionResult> Ignore([FromBody] SageImportInput input, CancellationToken ct = default)
        => Ok(new { changed = await SageComparison.SetIgnoredAsync(_db, input.AccountRefs, true, this.GetUserId(), ct) });

    [HttpPost("comparison/unignore")]
    public async Task<ActionResult> Unignore([FromBody] SageImportInput input, CancellationToken ct = default)
        => Ok(new { changed = await SageComparison.SetIgnoredAsync(_db, input.AccountRefs, false, this.GetUserId(), ct) });

    // GET /api/sage/comparison/{accountRef} — one account as both systems hold it.
    [HttpGet("comparison/{accountRef}")]
    public async Task<ActionResult<SageComparison.SideBySide>> ComparisonDetail(string accountRef, CancellationToken ct = default)
        => await SageComparison.DetailAsync(_db, accountRef, ct) is { } d ? d : NotFound();

    // GET /api/sage/comparison/export.csv?includeBlanks=&withOrders=&category= — the same
    // issues as a spreadsheet.
    [HttpGet("comparison/export.csv")]
    public async Task<IActionResult> ComparisonCsv(
        [FromQuery] bool includeBlanks = false, [FromQuery] bool withOrders = false, [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        var result = await SageComparison.BuildAsync(_db, includeBlanks, ct);
        IEnumerable<SageComparison.Issue> issues = result.Issues;
        if (withOrders) issues = issues.Where(i => i.Orders > 0);
        if (!string.IsNullOrWhiteSpace(category)) issues = issues.Where(i => i.Category == category);
        return File(SageComparison.ToCsv(issues), "text/csv; charset=utf-8",
            $"sage-comparison-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    // ---- Sidecar ----------------------------------------------------------------------

    // GET /api/sage/sidecar/sync — what the sidecar needs to decide whether a pull is due.
    [AllowAnonymous]
    [SidecarKey]
    [HttpGet("sidecar/sync")]
    public async Task<ActionResult> SidecarSync(CancellationToken ct = default) =>
        Ok(await _db.Set<SageSyncState>().AsNoTracking()
            .Select(s => new { s.Entity, s.RequestedUtc, s.LastSuccessUtc, s.CancelRequestedUtc })
            .ToListAsync(ct));

    // PUT /api/sage/sidecar/customers — the whole customer ledger and address book as the
    // sidecar just read them. Replaces the mirror and clears any pending request. A few MB
    // for ~3,000 customers, hence the raised body limit.
    [AllowAnonymous]
    [SidecarKey]
    [RequestSizeLimit(64_000_000)]
    [HttpPut("sidecar/customers")]
    public async Task<ActionResult> PutCustomers([FromBody] SageCustomerMirror.Snapshot snap, CancellationToken ct = default)
    {
        if (snap.Customers.Count == 0)
            return BadRequest(new { message = "Snapshot has no customers - refusing to empty the mirror." });

        var (customers, addresses) = await SageCustomerMirror.ReplaceAsync(_db, snap, ct);

        var state = await State(ct);
        var now = DateTime.UtcNow;
        state.LastRunUtc = now;
        state.LastSuccessUtc = now;
        state.LastError = null;
        state.RowCount = customers;
        state.DetailCount = addresses;
        state.DurationMs = snap.ReadMs;
        state.Machine = Truncate(snap.Machine, 100);
        state.RequestedUtc = null;
        state.RequestedBy = null;
        await _db.SaveChangesAsync(ct);

        return Ok(new { customers, addresses });
    }

    // POST /api/sage/sidecar/sync/{entity}/failed — the pull did not happen. The pending
    // request is cleared so a dead Sage does not make the sidecar retry every poll; the
    // error shows on the page and the person presses Refresh again when Sage is back.
    [AllowAnonymous]
    [SidecarKey]
    [HttpPost("sidecar/sync/{entity}/failed")]
    public async Task<ActionResult> SyncFailed(string entity, [FromBody] SyncFailedInput input, CancellationToken ct = default)
    {
        var state = await _db.Set<SageSyncState>().FirstOrDefaultAsync(s => s.Entity == entity, ct);
        if (state == null) return NotFound();
        state.LastRunUtc = DateTime.UtcNow;
        state.LastError = Truncate(input.Error, 2000);
        state.Machine = Truncate(input.Machine, 100) ?? state.Machine;
        state.RequestedUtc = null;
        state.RequestedBy = null;
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(state));
    }

    // ---- helpers ----------------------------------------------------------------------

    // The Customers state row — created on the fly if the migration's seed is missing.
    private async Task<SageSyncState> State(CancellationToken ct)
    {
        var state = await _db.Set<SageSyncState>().FirstOrDefaultAsync(s => s.Entity == SageSyncState.Customers, ct);
        if (state == null)
        {
            state = new SageSyncState { Entity = SageSyncState.Customers };
            _db.Add(state);
        }
        return state;
    }

    private static SageSyncDto ToDto(SageSyncState s) => new(
        s.Entity, Utc(s.LastRunUtc), Utc(s.LastSuccessUtc), s.LastError, s.RowCount, s.DetailCount,
        s.DurationMs, s.Machine, Utc(s.RequestedUtc), s.RequestedBy);

    // These columns hold UTC but come back from SQL with no Kind, which the JSON would emit
    // without a "Z" — and the page (and the sidecar) would then read them as local time.
    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? d) => d == null ? null : Utc(d.Value);

    private static string? Truncate(string? s, int max) =>
        s == null ? null : (s.Length <= max ? s : s[..max]);
}

public record SageCustomersDto(SageSyncDto Sync, List<SageCustomerDto> Customers);

public record SageCustomerDto(
    int Id,
    string AccountRef,
    string? Name,
    string? Address1,
    string? Address2,
    string? Address3,
    string? Address4,
    string? Address5,
    string? Contact,
    string? Telephone,
    string? Email,
    short DefTaxCode,
    decimal Balance,
    bool OnHold,
    bool Inactive,
    int AddressCount,
    int DeliveryAddressCount,
    DateTime LastSeenUtc);

public record SageAddressDto(
    int AddressNumber,
    string? Name,
    string? Description,
    string? Type,
    string? Address1,
    string? Address2,
    string? Address3,
    string? Address4,
    string? Address5,
    string? Telephone,
    string? Contact,
    string? Email);

public record SageSyncDto(
    string Entity,
    DateTime? LastRunUtc,
    DateTime? LastSuccessUtc,
    string? LastError,
    int RowCount,
    int DetailCount,
    int DurationMs,
    string? Machine,
    DateTime? RequestedUtc,
    string? RequestedBy);

public class SyncFailedInput
{
    public string? Error { get; set; }
    public string? Machine { get; set; }
}

public class SageImportInput
{
    public List<string> AccountRefs { get; set; } = new();
}
