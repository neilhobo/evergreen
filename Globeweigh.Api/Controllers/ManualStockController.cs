using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Manual Stock (TauriTouch Factory module): batches of manually-created pallets. A batch is
// N pallets of one finished product against an order number; the pallets themselves are
// inserted into CodicoPallet (IsManual=1, PalletBatchId) in a later phase.
// Anonymous like TouchController: the Touch app's screens are passcode/login gated client-side.
[ApiController]
[AllowAnonymous]
[Route("api/manual-stock")]
public class ManualStockController : ControllerBase
{
    private const int FinishedProductTypeId = 1; // Globeweigh.Model eProductType.FinishedProduct

    private readonly GbEvergreenContext _db;

    public ManualStockController(GbEvergreenContext db) => _db = db;

    // GET /api/manual-stock/config — the standard bulk density (kg/m³) from dbo.Setting,
    // used by the weigh popup to show volume: m³ = kg / bulkDensity.
    [HttpGet("config")]
    public async Task<ActionResult<object>> Config(CancellationToken ct = default)
    {
        var density = await _db.Settings.AsNoTracking().Select(s => s.BulkDensity).FirstOrDefaultAsync(ct);
        return Ok(new { bulkDensity = density });
    }

    // PUT /api/manual-stock/config — update the standard bulk density (editable from the
    // weigh popup so operators can correct it without an admin round-trip).
    [HttpPut("config")]
    public async Task<ActionResult<object>> SetConfig([FromBody] ManualStockConfigInput input, CancellationToken ct = default)
    {
        if (input.BulkDensity <= 0 || input.BulkDensity > 9999)
            return BadRequest(new { message = "Bulk density must be between 0 and 9999 kg/m³." });

        var setting = await _db.Settings.FirstOrDefaultAsync(ct);
        if (setting == null) return NotFound(new { message = "Settings row not found." });

        setting.BulkDensity = decimal.Round(input.BulkDensity, 2);
        await _db.SaveChangesAsync(ct);
        return Ok(new { bulkDensity = setting.BulkDensity });
    }

    // GET /api/manual-stock/products — finished products for the batch product picker.
    [HttpGet("products")]
    public async Task<ActionResult<IEnumerable<object>>> Products(CancellationToken ct = default)
    {
        var rows = await _db.Set<ProductRow>().AsNoTracking()
            .Where(p => p.DateDeleted == null && p.ProductTypeId == FinishedProductTypeId
                && p.SoldBy != ProductSoldBy.CubicMeter) // cubic meter products carry no stock
            .OrderBy(p => p.Description)
            .Select(p => new { id = p.Id, description = p.Description, gtin = p.Gtin, size = p.Size, upp = p.Upp, averageWeight = p.AverageWeight })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // GET /api/manual-stock/batches — newest first, with the product description and the
    // weighed-so-far count joined in.
    [HttpGet("batches")]
    public async Task<ActionResult<IEnumerable<PalletBatchDto>>> Batches(CancellationToken ct = default)
    {
        var rows = await (
            from b in _db.Set<PalletBatch>().AsNoTracking()
            join p in _db.Set<ProductRow>().AsNoTracking() on b.ProductId equals p.Id into pj
            from p in pj.DefaultIfEmpty()
            orderby b.Id descending
            select new PalletBatchDto
            {
                Id = b.Id,
                OrderNumber = b.OrderNumber,
                ProductId = b.ProductId,
                Product = p != null ? p.Description : null,
                Gtin = p != null ? p.Gtin : null,
                Size = p != null ? p.Size : null,
                Upp = p != null ? (int?)p.Upp : null,
                PalletCount = b.PalletCount,
                PalletsWeighed = _db.Set<CodicoPalletEntity>().Count(cp => cp.PalletBatchId == b.Id),
                CreatedBy = b.CreatedBy,
                DateCreated = b.DateCreated,
                RequireIndividualWeight = b.RequireIndividualWeight,
                PalletWeight = b.PalletWeight,
            }
        ).Take(200).ToListAsync(ct);
        return Ok(rows);
    }

    // GET /api/manual-stock/batches/{id}/pallets — the batch's weighed pallets, oldest first.
    [HttpGet("batches/{id:int}/pallets")]
    public async Task<ActionResult<IEnumerable<object>>> Pallets(int id, CancellationToken ct = default)
    {
        var rows = await _db.Set<CodicoPalletEntity>().AsNoTracking()
            .Where(p => p.PalletBatchId == id)
            .OrderBy(p => p.Id)
            .Select(p => new { id = p.Id, uniqueCode = p.UniqueCode, weight = p.Weight, dateCreated = p.DateCreated, printed = p.DateLabelPrinted != null })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // POST /api/manual-stock/batches/{id}/pallets — capture one weighed pallet into
    // CodicoPallet (IsManual=1). UniqueCode uses a manual series "M{batch}-{seq}-{ddMMyyyy}" so it can
    // never collide with the 3rd-party feed's numeric serials.
    [HttpPost("batches/{id:int}/pallets")]
    public async Task<ActionResult<object>> AddPallet(int id, [FromBody] ManualPalletInput input, CancellationToken ct = default)
    {
        if (input.Weight <= 0) return BadRequest(new { message = "Weight must be greater than zero." });

        var batch = await _db.Set<PalletBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound();

        var existing = await _db.Set<CodicoPalletEntity>().AsNoTracking()
            .Where(p => p.PalletBatchId == id)
            .Select(p => p.UniqueCode)
            .ToListAsync(ct);
        if (existing.Count >= batch.PalletCount)
            return BadRequest(new { message = $"This batch already has all {batch.PalletCount} pallets weighed." });

        var product = await _db.Set<ProductRow>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == batch.ProductId, ct);
        var gtin = product?.Gtin ?? "";

        var seq = existing.Count + 1;
        // Gap-proof if pallets get deleted. Matches on M{batch}-{seq} alone — the date suffix
        // differs per pallet, and pre-date codes have none.
        while (existing.Any(c => c == $"M{id}-{seq:D3}" || c.StartsWith($"M{id}-{seq:D3}-"))) seq++;

        var pallet = NewPallet(id, seq, gtin, input.Weight, DateTime.Now);
        _db.Add(pallet);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = pallet.Id, uniqueCode = pallet.UniqueCode, weight = pallet.Weight, dateCreated = pallet.DateCreated, printed = false });
    }

    private static CodicoPalletEntity NewPallet(int batchId, int seq, string gtin, decimal weight, DateTime now) => new()
    {
        // Creation date appended (client request): M12-003-01102026.
        UniqueCode = $"M{batchId}-{seq:D3}-{now:ddMMyyyy}",
        // Code13 is a computed column — the DB derives it from Code, don't set it here.
        Code = string.IsNullOrEmpty(gtin) ? null : gtin,
        Weight = weight,
        // Manually-created pallets never pass the conveyor scanner, so they are born
        // scanned: left unscanned they would show as red "missed scan" rows and fire
        // the production alarm siren, which they can never clear.
        IsScanned = true,
        DateScanned = now,
        DateCreated = now,
        IsManual = true,
        PalletBatchId = batchId,
    };

    // POST /api/manual-stock/pallets/printed — stamp DateLabelPrinted on pallets whose label
    // was just sent to a printer (the tick on the batch's pallet list). Manual pallets only.
    [HttpPost("pallets/printed")]
    public async Task<ActionResult> MarkPrinted([FromBody] MarkPrintedInput input, CancellationToken ct = default)
    {
        var ids = input.PalletIds ?? new List<int>();
        if (ids.Count == 0) return NoContent();
        var pallets = await _db.Set<CodicoPalletEntity>()
            .Where(p => ids.Contains(p.Id) && p.IsManual)
            .ToListAsync(ct);
        var now = DateTime.Now;
        foreach (var p in pallets) p.DateLabelPrinted = now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/manual-stock/pallets/{palletId} — remove a manually-created pallet
    // (mis-weigh). Manual pallets only, and never once the pallet has been used — picked
    // onto a dispatch order, or taken out of stock. (IsScanned can't gate this: manual
    // pallets are created already-scanned, see AddPallet.)
    [HttpDelete("pallets/{palletId:int}")]
    public async Task<ActionResult> DeletePallet(int palletId, CancellationToken ct = default)
    {
        var pallet = await _db.Set<CodicoPalletEntity>().FirstOrDefaultAsync(p => p.Id == palletId, ct);
        if (pallet == null) return NotFound();
        if (!pallet.IsManual)
            return BadRequest(new { message = "Only manually-created pallets can be deleted." });
        if (pallet.PickedOrderId != null || pallet.DateOutOfStock != null)
            return BadRequest(new { message = "This pallet has already been dispatched and cannot be deleted." });
        _db.Remove(pallet);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/manual-stock/batches/{id} — remove a batch AND its weighed pallets.
    // Blocked if any pallet has already been used (picked or taken out of stock).
    [HttpDelete("batches/{id:int}")]
    public async Task<ActionResult> DeleteBatch(int id, CancellationToken ct = default)
    {
        var batch = await _db.Set<PalletBatch>().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound();

        var pallets = await _db.Set<CodicoPalletEntity>()
            .Where(p => p.PalletBatchId == id)
            .ToListAsync(ct);
        if (pallets.Any(p => p.PickedOrderId != null || p.DateOutOfStock != null))
            return BadRequest(new { message = "This batch has pallets that were already dispatched and cannot be deleted." });

        _db.RemoveRange(pallets);
        _db.Remove(batch);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/manual-stock/batches — create a batch.
    [HttpPost("batches")]
    public async Task<ActionResult<PalletBatchDto>> Create([FromBody] PalletBatchInput input, CancellationToken ct = default)
    {
        if (input.ProductId <= 0) return BadRequest(new { message = "A product is required." });
        if (input.PalletCount < 1 || input.PalletCount > 100)
            return BadRequest(new { message = "Number of pallets must be between 1 and 100." });

        var product = await _db.Set<ProductRow>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == input.ProductId && p.DateDeleted == null, ct);
        if (product == null) return BadRequest(new { message = "Product not found." });
        if (product.SoldBy == ProductSoldBy.CubicMeter)
            return BadRequest(new { message = "This product is sold by the cubic meter and carries no stock." });

        // Average-weight batch: no weighing, every pallet is created now at the one weight.
        decimal? palletWeight = null;
        if (!input.RequireIndividualWeight)
        {
            if (input.PalletWeight is not > 0 || input.PalletWeight > 99999)
                return BadRequest(new { message = "Enter a pallet weight greater than zero." });
            palletWeight = decimal.Round(input.PalletWeight.Value, 2);
        }

        var batch = new PalletBatch
        {
            OrderNumber = string.IsNullOrWhiteSpace(input.OrderNumber) ? null : input.OrderNumber.Trim(),
            ProductId = input.ProductId,
            PalletCount = input.PalletCount,
            CreatedBy = string.IsNullOrWhiteSpace(input.CreatedBy) ? null : input.CreatedBy.Trim(),
            DateCreated = DateTime.Now,
            RequireIndividualWeight = input.RequireIndividualWeight,
            PalletWeight = palletWeight,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Add(batch);
        await _db.SaveChangesAsync(ct);
        if (palletWeight is decimal w)
        {
            for (var seq = 1; seq <= batch.PalletCount; seq++)
                _db.Add(NewPallet(batch.Id, seq, product.Gtin ?? "", w, batch.DateCreated));
            await _db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);

        return new PalletBatchDto
        {
            Id = batch.Id,
            OrderNumber = batch.OrderNumber,
            ProductId = batch.ProductId,
            Product = product.Description,
            Gtin = product.Gtin,
            Size = product.Size,
            Upp = product.Upp,
            PalletCount = batch.PalletCount,
            CreatedBy = batch.CreatedBy,
            DateCreated = batch.DateCreated,
            PalletsWeighed = palletWeight is null ? 0 : batch.PalletCount,
            RequireIndividualWeight = batch.RequireIndividualWeight,
            PalletWeight = batch.PalletWeight,
        };
    }
}

public class PalletBatchDto
{
    public int Id { get; set; }
    public string? OrderNumber { get; set; }
    public int ProductId { get; set; }
    public string? Product { get; set; }
    public string? Gtin { get; set; }
    // Bag size + units per pallet — the Manual label prints them in its description line.
    public int? Size { get; set; }
    public int? Upp { get; set; }
    public int PalletCount { get; set; }
    public int PalletsWeighed { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime DateCreated { get; set; }
    public bool RequireIndividualWeight { get; set; }
    public decimal? PalletWeight { get; set; }
}

public class ManualPalletInput
{
    public decimal Weight { get; set; }
}

public class ManualStockConfigInput
{
    public decimal BulkDensity { get; set; }
}

public class PalletBatchInput
{
    public string? OrderNumber { get; set; }
    public int ProductId { get; set; }
    public int PalletCount { get; set; }
    public string? CreatedBy { get; set; }
    // Defaults true so an older Touch build (no field) keeps the weigh-each-pallet flow.
    public bool RequireIndividualWeight { get; set; } = true;
    public decimal? PalletWeight { get; set; }
}

public class MarkPrintedInput
{
    public List<int>? PalletIds { get; set; }
}
