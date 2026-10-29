using System.Data;
using System.Text.Json;
using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// The Sage 50 sales-order export queue, dbo.SageExportJob (Data/Entities/SageExportJob.cs
// has the design). Two callers, two auth schemes:
//   • Admin, with the usual JWT: watch the queue, queue an order by hand, retry a failure,
//     preview what an order would send.
//   • Globeweigh.SageSidecar on the Sage machine, with the shared X-Sidecar-Key
//     (Auth/SidecarKeyAttribute): claim / complete / fail / release. Those actions are
//     [AllowAnonymous] to switch the JWT requirement off and [SidecarKey] to put the key
//     check in its place.
[ApiController]
[Authorize]
[Route("api/sage")]
public class SageController : ControllerBase
{
    // A job the sidecar claimed but never finished (it crashed, or lost the network) is
    // handed out again after this long. Generous on purpose: an SDO connect to a busy
    // Sage data set can take a while, and a premature re-hand-out is the one way this
    // design can double-post. (The sidecar's own done-markers cover the crash-AFTER-post
    // case; see ExportWorker there.)
    private const int StaleClaimMinutes = 15;

    private readonly GbEvergreenContext _db;
    private readonly IConfiguration _config;

    public SageController(GbEvergreenContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // ---- Admin ------------------------------------------------------------------------

    // GET /api/sage/status — is the integration on, is the sidecar key set, queue counts,
    // and when a sidecar last touched the queue. There is no heartbeat: a recent claim is
    // the evidence that one is alive.
    [HttpGet("status")]
    public async Task<ActionResult<SageStatusDto>> Status(CancellationToken ct = default)
    {
        var counts = await _db.Set<SageExportJob>().AsNoTracking()
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var lastClaim = await _db.Set<SageExportJob>().AsNoTracking()
            .Where(j => j.StartedUtc != null)
            .OrderByDescending(j => j.StartedUtc)
            .Select(j => new { j.StartedUtc, j.ClaimedBy })
            .FirstOrDefaultAsync(ct);
        var lastDone = await _db.Set<SageExportJob>().AsNoTracking()
            .Where(j => j.Status == SageExportJob.Done)
            .MaxAsync(j => j.CompletedUtc, ct);

        var key = _config["Sage:SidecarKey"];
        return new SageStatusDto(
            SageExport.AutoExportEnabled(_config),
            !string.IsNullOrWhiteSpace(key) && key.Length >= SidecarKeyAttribute.MinKeyLength,
            counts.GetValueOrDefault(SageExportJob.Queued),
            counts.GetValueOrDefault(SageExportJob.Exporting),
            counts.GetValueOrDefault(SageExportJob.Done),
            counts.GetValueOrDefault(SageExportJob.Failed),
            lastDone,
            lastClaim?.ClaimedBy,
            lastClaim?.StartedUtc);
    }

    // GET /api/sage/jobs?status=Failed&take=200 — newest first, without payloads.
    [HttpGet("jobs")]
    public async Task<ActionResult<IEnumerable<SageJobDto>>> Jobs(
        [FromQuery] string? status = null, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        var q = _db.Set<SageExportJob>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(j => j.Status == status);
        var rows = await q.OrderByDescending(j => j.Id).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    // GET /api/sage/jobs/{id} — one job, with the payload it carries.
    [HttpGet("jobs/{id:int}")]
    public async Task<ActionResult<SageJobDetailDto>> Job(int id, CancellationToken ct = default)
    {
        var job = await _db.Set<SageExportJob>().AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null) return NotFound();
        return new SageJobDetailDto(ToDto(job), ParsePayload(job.Payload));
    }

    // GET /api/sage/orders/{orderId}/payload — what an export of this order would send,
    // built fresh. For checking the mapping (account code, stock codes, prices) before an
    // order is queued; also the input for the sidecar's --post-file test mode.
    [HttpGet("orders/{orderId:int}/payload")]
    public async Task<ActionResult<SageExport.SalesOrderPayload>> Payload(int orderId, CancellationToken ct = default)
        => await SageExport.BuildPayloadAsync(_db, orderId, null, ct) is { } p ? p : NotFound();

    // POST /api/sage/orders/{orderId}/export — queue an order by hand. Works whether or not
    // automatic export is switched on. Refused for an order that has not been dispatched
    // (nothing has left, so there is nothing to invoice) and for one that already has a
    // live job (409, with that job).
    [HttpPost("orders/{orderId:int}/export")]
    public async Task<ActionResult<SageJobDto>> ExportOrder(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DateDeleted == null, ct);
        if (order == null) return NotFound();
        if (order.DateDispatched == null)
            return Conflict(new { message = $"Order #{order.OrderNo} has not been dispatched yet." });

        var existing = await _db.Set<SageExportJob>().AsNoTracking()
            .Where(j => j.DispatchOrderId == orderId
                && j.DocType == SageExportJob.SalesOrder
                && j.Status != SageExportJob.Failed)
            .OrderByDescending(j => j.Id)
            .FirstOrDefaultAsync(ct);
        if (existing != null)
            return Conflict(new
            {
                message = existing.Status == SageExportJob.Done
                    ? $"Order #{order.OrderNo} is already in Sage as order {existing.SageOrderNo}."
                    : $"Order #{order.OrderNo} is already queued (job {existing.Id}, {existing.Status}).",
                job = ToDto(existing),
            });

        var queued = await SageExport.EnqueueAsync(_db, [orderId], SageExportJob.Manual, null, ct);
        if (queued.Count == 0) return NotFound();
        await _db.SaveChangesAsync(ct);

        var job = await _db.Set<SageExportJob>().AsNoTracking()
            .Where(j => j.DispatchOrderId == orderId)
            .OrderByDescending(j => j.Id)
            .FirstAsync(ct);
        return ToDto(job);
    }

    // POST /api/sage/jobs/{id}/retry — put a Failed job back on the queue with a fresh set
    // of attempts. The payload is NOT rebuilt: it is the order as dispatched and that is
    // still what should reach Sage. If the fix was to the order's data (a customer code, a
    // SKU), queue a new export with POST orders/{id}/export instead — the filtered unique
    // index lets a Failed job be superseded — and this one stays Failed as the record.
    [HttpPost("jobs/{id:int}/retry")]
    public async Task<ActionResult<SageJobDto>> Retry(int id, CancellationToken ct = default)
    {
        var job = await _db.Set<SageExportJob>().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null) return NotFound();
        if (job.Status != SageExportJob.Failed)
            return Conflict(new { message = $"Job {id} is {job.Status}, not Failed." });

        var superseded = await _db.Set<SageExportJob>().AsNoTracking()
            .AnyAsync(j => j.Id != id
                && j.DispatchOrderId == job.DispatchOrderId
                && j.DocType == job.DocType
                && j.Status != SageExportJob.Failed, ct);
        if (superseded)
            return Conflict(new { message = $"Order #{job.OrderNo} already has a newer export job." });

        job.Status = SageExportJob.Queued;
        job.Attempts = 0;
        job.StartedUtc = null;
        job.CompletedUtc = null;
        job.ClaimedBy = null;
        await _db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    // ---- Sidecar ----------------------------------------------------------------------

    // GET /api/sage/sidecar/ping — connectivity and key check for the install.
    [AllowAnonymous]
    [SidecarKey]
    [HttpGet("sidecar/ping")]
    public ActionResult Ping() => Ok(new
    {
        ok = true,
        utc = DateTime.UtcNow,
        autoExportEnabled = SageExport.AutoExportEnabled(_config),
    });

    // POST /api/sage/sidecar/claim — hand the sidecar the next job. ONE atomic UPDATE, so
    // two sidecars (or one restarted mid-job) can never take the same row; the outer
    // predicate re-checks the status under the lock so two simultaneous claims cannot both
    // win the same id. A job left in Exporting past StaleClaimMinutes is handed out again.
    [AllowAnonymous]
    [SidecarKey]
    [HttpPost("sidecar/claim")]
    public async Task<ActionResult> Claim([FromBody] ClaimInput? input, CancellationToken ct = default)
    {
        var conn = _db.Database.GetDbConnection();
        int? id;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = ClaimSql;
            var machine = cmd.CreateParameter();
            machine.ParameterName = "@machine";
            machine.Value = (object?)Truncate(input?.Machine, 100) ?? DBNull.Value;
            cmd.Parameters.Add(machine);
            var stale = cmd.CreateParameter();
            stale.ParameterName = "@stale";
            stale.Value = StaleClaimMinutes;
            cmd.Parameters.Add(stale);

            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
            id = await cmd.ExecuteScalarAsync(ct) is int i ? i : null;
        }
        if (id == null) return NoContent();

        var job = await _db.Set<SageExportJob>().AsNoTracking().FirstAsync(j => j.Id == id, ct);
        return Ok(new
        {
            job.Id,
            job.DispatchOrderId,
            job.OrderNo,
            job.Attempts,
            job.Reason,
            Payload = ParsePayload(job.Payload),
        });
    }

    private const string ClaimSql = @"
UPDATE j
   SET Status     = 'Exporting',
       StartedUtc = GETUTCDATE(),
       Attempts   = j.Attempts + 1,
       ClaimedBy  = @machine
OUTPUT inserted.id
  FROM dbo.SageExportJob j
 WHERE j.id = (SELECT TOP 1 id
                 FROM dbo.SageExportJob WITH (UPDLOCK, READPAST)
                WHERE Status = 'Queued'
                   OR (Status = 'Exporting' AND StartedUtc < DATEADD(minute, -@stale, GETUTCDATE()))
                ORDER BY id)
   AND (j.Status = 'Queued'
        OR (j.Status = 'Exporting' AND j.StartedUtc < DATEADD(minute, -@stale, GETUTCDATE())));";

    // POST /api/sage/sidecar/jobs/{id}/complete — the order is in Sage. Idempotent: the
    // sidecar retries this call, and re-hands a done-marker after a crash.
    [AllowAnonymous]
    [SidecarKey]
    [HttpPost("sidecar/jobs/{id:int}/complete")]
    public async Task<ActionResult<SageJobDto>> Complete(int id, [FromBody] CompleteInput input, CancellationToken ct = default)
    {
        if (input.SageOrderNo <= 0) return BadRequest(new { message = "sageOrderNo is required." });
        var job = await _db.Set<SageExportJob>().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null) return NotFound();

        if (job.Status == SageExportJob.Done)
            return job.SageOrderNo == input.SageOrderNo
                ? ToDto(job)
                : Conflict(new { message = $"Job {id} is already Done as Sage order {job.SageOrderNo}, not {input.SageOrderNo}." });

        job.Status = SageExportJob.Done;
        job.SageOrderNo = input.SageOrderNo;
        job.CompletedUtc = DateTime.UtcNow;
        job.LastError = null;
        await _db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    // POST /api/sage/sidecar/jobs/{id}/fail — this attempt failed. Back to Queued for
    // another go, or parked as Failed once the attempts are spent or the sidecar says the
    // failure is final (bad data: unknown customer, no lines — retrying cannot fix it).
    [AllowAnonymous]
    [SidecarKey]
    [HttpPost("sidecar/jobs/{id:int}/fail")]
    public async Task<ActionResult> Fail(int id, [FromBody] FailInput input, CancellationToken ct = default)
    {
        var job = await _db.Set<SageExportJob>().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null) return NotFound();
        if (job.Status != SageExportJob.Exporting)
            return Conflict(new { message = $"Job {id} is {job.Status}, not Exporting." });

        var final = input.Final || job.Attempts >= SageExportJob.MaxAttempts;
        job.LastError = Truncate(input.Error, 2000);
        if (final)
        {
            job.Status = SageExportJob.Failed;
            job.CompletedUtc = DateTime.UtcNow;
        }
        else
        {
            job.Status = SageExportJob.Queued;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { job.Id, job.Status, job.Attempts, Final = final });
    }

    // POST /api/sage/sidecar/jobs/{id}/release — the sidecar could not even try (Sage data
    // unreachable, Sage refused the login). Back to Queued WITHOUT spending an attempt:
    // that is an environment problem, not a job problem, and burning attempts on it would
    // park good orders as Failed while someone reboots the Sage server.
    [AllowAnonymous]
    [SidecarKey]
    [HttpPost("sidecar/jobs/{id:int}/release")]
    public async Task<ActionResult> Release(int id, [FromBody] ReleaseInput? input, CancellationToken ct = default)
    {
        var job = await _db.Set<SageExportJob>().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null) return NotFound();
        if (job.Status != SageExportJob.Exporting)
            return Conflict(new { message = $"Job {id} is {job.Status}, not Exporting." });

        job.Status = SageExportJob.Queued;
        job.Attempts = Math.Max(0, job.Attempts - 1);
        job.LastError = Truncate(input?.Reason, 2000);
        job.StartedUtc = null;
        job.ClaimedBy = null;
        await _db.SaveChangesAsync(ct);
        return Ok(new { job.Id, job.Status, job.Attempts });
    }

    // ---- helpers ----------------------------------------------------------------------

    private static SageJobDto ToDto(SageExportJob j) => new(
        j.Id, j.DocType, j.DispatchOrderId, j.OrderNo, j.Status, j.Attempts, j.LastError, j.Reason,
        j.SageOrderNo, j.ClaimedBy, j.CreatedUtc, j.StartedUtc, j.CompletedUtc);

    private static JsonElement ParsePayload(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string? Truncate(string? s, int max) =>
        s == null ? null : (s.Length <= max ? s : s[..max]);
}

public record SageStatusDto(
    bool AutoExportEnabled,
    bool SidecarKeyConfigured,
    int Queued,
    int Exporting,
    int Done,
    int Failed,
    DateTime? LastCompletedUtc,
    string? LastClaimedBy,
    DateTime? LastClaimedUtc);

public record SageJobDto(
    int Id,
    string DocType,
    int DispatchOrderId,
    int OrderNo,
    string Status,
    int Attempts,
    string? LastError,
    string Reason,
    int? SageOrderNo,
    string? ClaimedBy,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? CompletedUtc);

public record SageJobDetailDto(SageJobDto Job, JsonElement Payload);

public class ClaimInput
{
    public string? Machine { get; set; }
}

public class CompleteInput
{
    public int SageOrderNo { get; set; }
}

public class FailInput
{
    public string? Error { get; set; }
    public bool Final { get; set; }
}

public class ReleaseInput
{
    public string? Reason { get; set; }
}
