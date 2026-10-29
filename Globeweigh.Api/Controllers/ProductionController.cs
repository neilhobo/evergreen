using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

[ApiController]
// Anonymous: read-only, and the TauriTouch Production / Prod-Summary screens must work on
// Factory devices with LoginEnabled off (no JWT) — matching the unauthenticated WPF Touch.
[AllowAnonymous]
[Route("api/production")]
public class ProductionController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public ProductionController(GbEvergreenContext db) => _db = db;

    // GET /api/production/pallets — mirrors the WPF ProductionPalletListView (read-only).
    // When sinceId is supplied the call becomes an incremental "live" fetch: it returns
    // only pallets newer than the client's cursor (oldest-first so the client can prepend
    // them newest-first), which the Production screen polls to show pallets as they scan.
    //
    // Queries the CodicoPallet TABLE, not vwCodicoPallet: the view's
    // COUNT(*) OVER (PARTITION BY UniqueCode) must be computed over the whole table
    // before any date filter can apply (~5s per call at 100k rows). Filtering first and
    // probing duplicates through IX_CodicoPallet_UniqueCode takes milliseconds.
    [HttpGet("pallets")]
    public async Task<ActionResult<IEnumerable<CodicoPalletDto>>> Pallets(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? sinceId = null,
        CancellationToken ct = default)
    {
        var pallets = _db.Set<CodicoPalletEntity>().AsNoTracking();
        if (from != null) pallets = pallets.Where(p => p.DateCreated >= from);
        if (to != null) pallets = pallets.Where(p => p.DateCreated < to.Value.AddDays(1));
        if (sinceId != null) pallets = pallets.Where(p => p.Id > sinceId);

        var query =
            from cp in pallets
            join pr in _db.Set<Product>().AsNoTracking() on cp.Code13 equals pr.Gtin13 into gj
            from pr in gj.DefaultIfEmpty()
            select new CodicoPalletDto
            {
                Id = cp.Id,
                UniqueCode = cp.UniqueCode,
                CodicoCode = cp.Code,
                Gtin = pr != null ? pr.Gtin : null,
                Weight = cp.Weight,
                Description = pr != null ? pr.Description : null,
                Size = pr != null ? pr.Size : null,
                Upp = pr != null ? (int?)pr.Upp : null,
                DateCreated = cp.DateCreated,
                IsScanned = cp.IsScanned,
                DateScanned = cp.DateScanned,
                ScanSource = cp.ScanSource,
                IsManual = cp.IsManual,
                IsDuplicate = _db.Set<CodicoPalletEntity>()
                    .Any(o => o.UniqueCode == cp.UniqueCode && o.Id != cp.Id),
                // The missed-scan alarm for this pallet has been silenced (see the alarm
                // endpoints below) — the UI dims its red row.
                AlarmAcked = _db.Set<ProductionAlarmAck>().Any(a => a.PalletId == cp.Id),
            };

        var rows = sinceId != null
            ? await query.OrderBy(r => r.Id).ToListAsync(ct)
            : await query.OrderByDescending(r => r.DateCreated).ToListAsync(ct);

        // LineNumber = 4th char from the end of UniqueCode (as vwCodicoPallet computed it),
        // done here rather than in SQL so a malformed code yields null instead of an error.
        foreach (var r in rows)
        {
            var u = r.UniqueCode?.TrimEnd();
            if (u != null && u.Length >= 4 && char.IsAsciiDigit(u[u.Length - 4]))
                r.LineNumber = u[u.Length - 4] - '0';
        }

        return rows;
    }

    // GET /api/production/summary — production totals per product over a date range.
    // Mirrors the WPF ProductionSummaryView: byLine=true groups per production line
    // (adds LineNumber). Backed by the GetProductionSummary[ByLine] stored procs.
    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<ProductionSummaryDto>>> Summary(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool byLine = false,
        CancellationToken ct = default)
    {
        // Match the WPF normalisation: whole days, from 00:00 to 23:59:59.999.
        var f = (from ?? DateTime.Today.AddDays(-5)).Date;
        var t = (to ?? DateTime.Today).Date.AddDays(1).AddMilliseconds(-1);

        if (byLine)
        {
            var rows = await _db.Set<ProductionSummaryLineRow>()
                .FromSqlInterpolated($"EXEC GetProductionSummaryByLine @DateFrom={f}, @DateTo={t}")
                .ToListAsync(ct);
            return rows.Select(r => new ProductionSummaryDto
            {
                LineNumber = r.LineNumber,
                Id = r.Id,
                Gtin = r.Gtin,
                Description = r.Description,
                Size = r.Size,
                Upp = r.Upp,
                RecordCount = r.RecordCount ?? 0,
                BagCount = r.BagCount ?? 0,
            }).ToList();
        }

        var summary = await _db.Set<ProductionSummaryRow>()
            .FromSqlInterpolated($"EXEC GetProductionSummary @DateFrom={f}, @DateTo={t}")
            .ToListAsync(ct);
        return summary.Select(r => new ProductionSummaryDto
        {
            Id = r.Id,
            Gtin = r.Gtin,
            Description = r.Description,
            Size = r.Size,
            Upp = r.Upp,
            RecordCount = r.RecordCount ?? 0,
            BagCount = r.BagCount ?? 0,
        }).ToList();
    }

    // ---- Missed-scan alarm --------------------------------------------------------
    // Shared alarm state for all touch PCs. A pallet is "missed" by the same rule as the
    // Production screen's red rows — unscanned, created today, non-manual, and EITHER:
    //   * OVERTAKEN — the scanner has read a LATER pallet (higher id), so it went past this
    //     one. Catches a pallet skipped between two good reads.
    //   * BLIND SCANNER — it sits in a long enough run of unread pallets with nothing read
    //     after it, and has waited long enough. Catches the scanner failing outright, which
    //     the overtaken rule alone cannot see (see UnackedMissedTodayAsync).
    // The alarm is ACTIVE while any missed pallet has no ProductionAlarmAck row. Suppress
    // acks everything currently missed, so the alarm stays quiet until a NEW pallet is
    // missed; a missed pallet that later gets scanned clears itself.
    //
    // Only pallets created TODAY are considered, so a fault running past midnight goes quiet
    // until the next morning's pallets rebuild the run (they do, within ~20 min of the line
    // restarting). The site runs ~07:00–17:30, so this has never bitten in practice.
    //
    // The PC with the relay card polls GET alarm and drives the siren to match; every PC
    // with the ProductionAlarm device flag shows the popup and can POST suppress.

    public record AlarmPallet(int Id, string? UniqueCode);
    public record AlarmState(bool Active, List<AlarmPallet> Pallets);
    public record SuppressInput(string? By);

    // GET /api/production/alarm — polled every few seconds by the touch PCs.
    [HttpGet("alarm")]
    public async Task<ActionResult<AlarmState>> Alarm(CancellationToken ct = default)
    {
        var missed = await UnackedMissedTodayAsync(ct);
        return new AlarmState(missed.Count > 0, missed);
    }

    // POST /api/production/alarm/suppress — silence the current alarm (from ANY touch PC).
    [HttpPost("alarm/suppress")]
    public async Task<ActionResult<AlarmState>> SuppressAlarm(
        [FromBody] SuppressInput? input, CancellationToken ct = default)
    {
        var missed = await UnackedMissedTodayAsync(ct);
        foreach (var m in missed)
            _db.Add(new ProductionAlarmAck { PalletId = m.Id, AckedAtUtc = DateTime.UtcNow, AckedBy = input?.By });
        try
        {
            if (missed.Count > 0) await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two PCs pressed Silence at once — the other insert won; the alarm is off either way.
        }
        return new AlarmState(false, new List<AlarmPallet>());
    }

    // TEMPORARY — testing only: simulate a missed scan by un-scanning a pallet (and clearing
    // any earlier alarm ack for it, so the alarm re-fires even after a suppress). The pallet
    // only counts as missed once a LATER pallet has been scanned, same as the real rule.
    // Remove together with the "Mark as Missed Scan" button on the Production screen.
    [HttpPost("pallets/{id:int}/mark-missed")]
    public async Task<IActionResult> MarkMissed(int id, CancellationToken ct = default)
    {
        var pallet = await _db.Set<CodicoPalletEntity>().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pallet == null) return NotFound();

        pallet.IsScanned = false;
        pallet.DateScanned = null;
        pallet.ScanSource = null;
        var ack = await _db.Set<ProductionAlarmAck>().FirstOrDefaultAsync(a => a.PalletId == id, ct);
        if (ack != null) _db.Remove(ack);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/production/pallets/{id}/mark-passed — backs the Production screen's
    // "Mark As Scanned" button: clear a missed pallet without printing a label (the other way
    // to clear one is a label reprint, see LabelsController.Reprint).
    //
    // Recorded as ScanSource "Manual", so the row still shows that no scanner ever read this
    // pallet — the clear is visible in the data rather than silently indistinguishable from a
    // real read. It does NOT count as evidence about the scanner in the alarm rule.
    [HttpPost("pallets/{id:int}/mark-passed")]
    public async Task<IActionResult> MarkPassed(int id, CancellationToken ct = default)
    {
        var pallet = await _db.Set<CodicoPalletEntity>().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pallet == null) return NotFound();

        pallet.IsScanned = true;
        pallet.DateScanned = DateTime.Now;
        // Recorded as an operator clear, NOT a scanner read — so it cannot make earlier
        // unread pallets look overtaken, nor mask a blind scanner.
        pallet.ScanSource = CodicoPalletEntity.ScannedByOperator;
        var ack = await _db.Set<ProductionAlarmAck>().FirstOrDefaultAsync(a => a.PalletId == id, ct);
        if (ack != null) _db.Remove(ack);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // A blind scanner — one that is triggering but decoding nothing — produces an unbroken
    // run of unread pallets at the END of the id sequence, where the OVERTAKEN rule can never
    // see them: no later pallet is ever read, so there is no evidence the scanner moved on.
    // On 11 Sep 2026 the scanner stopped decoding at 13:05 and 39 pallets went past unread
    // over 2.5 hours without the alarm making a sound. These two thresholds catch that case.
    //
    // Calibrated against 1,225 real scanner reads in the log over 1–11 Sep 2026. A pallet is
    // normally read ~1 min after its label prints, but the tail is long and legitimate — it
    // waits for the pallet in front, so 22% of reads land 5+ min later and one sat parked for
    // 13 h. Time alone therefore cannot tell a blind scanner from a slow queue: a 5-minute
    // "unscanned for too long" rule would have cried wolf ~300 times in those 10 days.
    // What is distinctive is the RUN. Genuine misses came in runs of 1–2 (84 of 94 runs) and
    // never exceeded 10; the runs of 5+ that did occur were all real misses the operators
    // cleared by hand, so firing on them is correct. The longest run with production still
    // going but the scanner legitimately quiet — the last pallets of a shift, labelled and
    // left overnight — was 3. Requiring 5 clears that by a margin while still firing ~40 min
    // into a total outage instead of never.
    private const int BlindScannerMinRun = 5;
    private const int BlindScannerStaleMinutes = 20;

    private async Task<List<AlarmPallet>> UnackedMissedTodayAsync(CancellationToken ct)
    {
        var today = DateTime.Today;
        // Manual pallets (Touch Manual Stock, Admin manual stock add, stock-take
        // placeholders) are invisible to this rule on BOTH sides: they never pass the
        // conveyor scanner, so they can neither be missed themselves nor act as the
        // "a later pallet was scanned" evidence that makes an earlier pallet missed.
        var todays = _db.Set<CodicoPalletEntity>().AsNoTracking()
            .Where(p => p.DateCreated >= today && !p.IsManual);

        // Only a genuine SCANNER read counts as evidence about the scanner. A row cleared by
        // an operator — Mark As Scanned, or a label reprint — must never stand in for one, or
        // clearing a single row by hand would turn every earlier unread pallet red at once,
        // and clearing the run of a blind scanner would hide the fault instead of showing it.
        var maxScannerReadId = await todays
            .Where(p => p.IsScanned &&
                        (p.ScanSource == null || p.ScanSource == CodicoPalletEntity.ScannedByScanner))
            .MaxAsync(p => (int?)p.Id, ct) ?? 0;

        // Every unscanned pallet today, with whether its alarm has already been silenced.
        // Tens of rows at most, so both rules are then applied in memory — one query, and no
        // list of ids handed back to SQL.
        var unscanned = await todays
            .Where(p => !p.IsScanned)
            .OrderBy(p => p.Id)
            .Select(p => new
            {
                p.Id,
                p.UniqueCode,
                p.DateCreated,
                Acked = _db.Set<ProductionAlarmAck>().Any(a => a.PalletId == p.Id),
            })
            .ToListAsync(ct);

        // BLIND SCANNER: the unread run after the last scanner read. Everything in it is still
        // "not reached yet" as far as the overtaken rule is concerned, so it is judged as a
        // group — a long enough run whose oldest pallet has waited long enough means the
        // scanner has stopped seeing rather than the line being slow.
        var tail = unscanned.Where(p => p.Id > maxScannerReadId).ToList();
        var stale = DateTime.Now.AddMinutes(-BlindScannerStaleMinutes);
        var blind = tail.Count >= BlindScannerMinRun && tail[0].DateCreated <= stale;

        return unscanned
            .Where(p => !p.Acked)
            .Where(p => p.Id < maxScannerReadId || (blind && p.Id > maxScannerReadId))
            .Select(p => new AlarmPallet(p.Id, p.UniqueCode?.Trim()))
            .ToList();
    }
}

// Same JSON shape as the old vwCodicoPallet read-model so TauriTouch/TauriAdmin need no changes.
public class CodicoPalletDto
{
    public int Id { get; set; }
    public string? UniqueCode { get; set; }
    public string? CodicoCode { get; set; }
    public string? Gtin { get; set; }
    public decimal Weight { get; set; }
    public string? Description { get; set; }
    public int? Size { get; set; }
    public int? Upp { get; set; }
    public DateTime? DateCreated { get; set; }
    public bool IsScanned { get; set; }
    public DateTime? DateScanned { get; set; }
    // NULL or "Scanner" = the conveyor scanner read it; "Reprint"/"Manual" = an operator
    // cleared the row. The grid needs the difference to colour missed scans the same way the
    // alarm judges them (see CodicoPalletEntity.ScanSource).
    public string? ScanSource { get; set; }
    public int? LineNumber { get; set; }
    public bool? IsDuplicate { get; set; }
    public bool AlarmAcked { get; set; }
    // Created by hand (Manual Stock / manual stock add / stock take) rather than pushed by
    // the 3rd party — the grid excludes these from its missed-scan colouring.
    public bool IsManual { get; set; }
}

public class ProductionSummaryDto
{
    public int? LineNumber { get; set; }
    public int? Id { get; set; }
    public string? Gtin { get; set; }
    public string? Description { get; set; }
    public int? Size { get; set; }
    public int? Upp { get; set; }
    public int RecordCount { get; set; }
    public int BagCount { get; set; }
}
