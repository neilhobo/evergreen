using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// The Sage sales mirror (audit trail + invoice lines) behind the Reports pages. Same split
// as SageCustomersController: the sidecar fills it with X-Sidecar-Key, admins ask for a
// refresh with the JWT, and the API itself never talks to Sage. The reports built on it are
// in ReportsController.
[ApiController]
[Authorize]
[Route("api/sage")]
public class SageSalesController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public SageSalesController(GbEvergreenContext db) => _db = db;

    // POST /api/sage/sync/sales/refresh — ask the sidecar for a fresh pull of the sales
    // mirror. A read takes a few minutes; the page polls /api/sage/sync until LastRunUtc moves.
    [HttpPost("sync/sales/refresh")]
    public async Task<ActionResult<SageSyncDto>> RequestRefresh(CancellationToken ct = default)
    {
        var state = await State(ct);
        state.RequestedUtc = DateTime.UtcNow;
        state.RequestedBy = User.FindFirst("name")?.Value;
        await _db.SaveChangesAsync(ct);
        return ToDto(state);
    }

    // POST /api/sage/sync/sales/cancel — withdraw a refresh (see the customers one): a pending
    // request is dropped and a pull in progress stops within seconds. The mirror keeps what it had.
    [HttpPost("sync/sales/cancel")]
    public async Task<ActionResult<SageSyncDto>> CancelRefresh(CancellationToken ct = default)
    {
        var state = await State(ct);
        state.RequestedUtc = null;
        state.RequestedBy = null;
        state.CancelRequestedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(state);
    }

    // PUT /api/sage/sidecar/sales — the window's audit trail and invoice lines as the sidecar
    // just read them. Replaces the mirror and clears any pending request. Two years of
    // Evergreen's sales is several MB, hence the raised body limit.
    [AllowAnonymous]
    [SidecarKey]
    [RequestSizeLimit(256_000_000)]
    [HttpPut("sidecar/sales")]
    public async Task<ActionResult> PutSales([FromBody] SageSalesMirror.Snapshot snap, CancellationToken ct = default)
    {
        if (snap.Transactions.Count == 0)
            return BadRequest(new { message = "Snapshot has no transactions - refusing to empty the mirror." });

        var (transactions, invoices, lines) = await SageSalesMirror.ReplaceAsync(_db, snap, ct);

        var state = await State(ct);
        var now = DateTime.UtcNow;
        state.LastRunUtc = now;
        state.LastSuccessUtc = now;
        state.LastError = null;
        state.RowCount = transactions;
        state.DetailCount = lines;
        state.DurationMs = snap.ReadMs;
        state.Machine = Truncate(snap.Machine, 100);
        state.WindowFrom = snap.From.Date;
        state.RequestedUtc = null;
        state.RequestedBy = null;
        await _db.SaveChangesAsync(ct);

        return Ok(new { transactions, invoices, lines });
    }

    private async Task<SageSyncState> State(CancellationToken ct)
    {
        var state = await _db.Set<SageSyncState>().FirstOrDefaultAsync(s => s.Entity == SageSyncState.Sales, ct);
        if (state == null)
        {
            state = new SageSyncState { Entity = SageSyncState.Sales };
            _db.Add(state);
        }
        return state;
    }

    private static SageSyncDto ToDto(SageSyncState s) => new(
        s.Entity, Utc(s.LastRunUtc), Utc(s.LastSuccessUtc), s.LastError, s.RowCount, s.DetailCount,
        s.DurationMs, s.Machine, Utc(s.RequestedUtc), s.RequestedBy);

    private static DateTime? Utc(DateTime? d) => d == null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc);

    private static string? Truncate(string? s, int max) =>
        s == null ? null : (s.Length <= max ? s : s[..max]);
}
