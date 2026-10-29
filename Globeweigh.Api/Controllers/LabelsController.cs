using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Label reprint audit. Pallet labels are printed entirely client-side by the TauriTouch app
// (rendered to a canvas, spooled to the printer attached to that PC), so the API never sees
// the print itself — the app posts here afterwards to record that it happened.
//
// Anonymous, like ProductionController and ManualStockController: a Factory device can run
// with LoginEnabled off and hold no JWT, and a reprint on such a PC must still be logged.
[ApiController]
[AllowAnonymous]
[Route("api/labels")]
public class LabelsController : ControllerBase
{
    private readonly GbEvergreenContext _db;
    private readonly ILogger<LabelsController> _log;

    public LabelsController(GbEvergreenContext db, ILogger<LabelsController> log)
    {
        _db = db;
        _log = log;
    }

    // POST /api/labels/reprint — record that a label was reprinted on a factory PC.
    //
    // Called AFTER the label has already gone to the printer, so this must never be able to
    // undo a successful print: the caller fires it and ignores the outcome, and anything
    // unexpected here is logged and answered 204 rather than thrown back at the app.
    [HttpPost("reprint")]
    public async Task<IActionResult> Reprint([FromBody] LabelReprintRequest req, CancellationToken ct = default)
    {
        if (req.PalletId <= 0) return BadRequest("palletId is required.");

        // Anything but the two known screens is recorded as Production rather than rejected —
        // losing the audit row would be worse than mislabelling where it came from.
        var source = req.Source == LabelReprint.ManualStock
            ? LabelReprint.ManualStock
            : LabelReprint.Production;

        try
        {
            _db.Add(new LabelReprint
            {
                PalletId = req.PalletId,
                UniqueCode = Clip(req.UniqueCode, 50),
                Source = source,
                DeviceName = Clip(req.DeviceName, 100),
                PrinterName = Clip(req.PrinterName, 200),
                UserName = Clip(req.UserName, 100),
                PrintedAtUtc = DateTime.UtcNow,
            });

            // Reprinting the label IS how the factory tracks that a missed pallet has been
            // dealt with, so an unscanned pallet is marked scanned here. Recorded as a
            // Reprint, not a scanner read: the conveyor scanner still has not seen this
            // pallet, so it must not count as evidence that the scanner is working or that
            // it moved past earlier pallets (see ProductionController's alarm rule).
            var pallet = await _db.Set<CodicoPalletEntity>()
                .FirstOrDefaultAsync(p => p.Id == req.PalletId, ct);
            if (pallet != null && !pallet.IsScanned)
            {
                pallet.IsScanned = true;
                pallet.DateScanned = DateTime.Now;
                pallet.ScanSource = CodicoPalletEntity.ScannedByReprint;
                // The row is no longer missed, so its alarm ack is spent — drop it so a
                // genuine later miss on the same pallet can alarm again.
                var ack = await _db.Set<ProductionAlarmAck>()
                    .FirstOrDefaultAsync(a => a.PalletId == req.PalletId, ct);
                if (ack != null) _db.Remove(ack);
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // The label is already out of the printer — never surface this to the operator.
            _log.LogError(ex, "Failed to log label reprint for pallet {PalletId}", req.PalletId);
        }

        return NoContent();
    }

    // GET /api/labels/reprints — the log, newest first. Filter by date (whole local days)
    // and/or a single pallet.
    [HttpGet("reprints")]
    public async Task<ActionResult<IEnumerable<LabelReprintDto>>> Reprints(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? palletId = null,
        CancellationToken ct = default)
    {
        var q = _db.Set<LabelReprint>().AsNoTracking();
        // PrintedAtUtc is UTC; the from/to the caller sends are local days, so convert the
        // bounds rather than the column (which would defeat IX_LabelReprint_PrintedAtUtc).
        if (from != null) q = q.Where(r => r.PrintedAtUtc >= from.Value.Date.ToUniversalTime());
        if (to != null) q = q.Where(r => r.PrintedAtUtc < to.Value.Date.AddDays(1).ToUniversalTime());
        if (palletId != null) q = q.Where(r => r.PalletId == palletId);

        return await q
            .OrderByDescending(r => r.PrintedAtUtc)
            .Select(r => new LabelReprintDto
            {
                Id = r.Id,
                PalletId = r.PalletId,
                UniqueCode = r.UniqueCode,
                Source = r.Source,
                DeviceName = r.DeviceName,
                PrinterName = r.PrinterName,
                UserName = r.UserName,
                PrintedAtUtc = r.PrintedAtUtc,
            })
            .ToListAsync(ct);
    }

    private static string? Clip(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }
}

public class LabelReprintRequest
{
    public int PalletId { get; set; }
    public string? UniqueCode { get; set; }
    public string? Source { get; set; }
    public string? DeviceName { get; set; }
    public string? PrinterName { get; set; }
    public string? UserName { get; set; }
}

public class LabelReprintDto
{
    public int Id { get; set; }
    public int PalletId { get; set; }
    public string? UniqueCode { get; set; }
    public string Source { get; set; } = "";
    public string? DeviceName { get; set; }
    public string? PrinterName { get; set; }
    public string? UserName { get; set; }
    public DateTime PrintedAtUtc { get; set; }
}
