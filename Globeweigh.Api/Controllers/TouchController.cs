using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Endpoints for the shop-floor Touch screens (Globeweigh.TauriTouch). Kept separate from the
// office Dispatch flows. Reuses the keyed WeighbridgeRecord and the vwWeighbridge* read-models.
// Weigh semantics mirror the WPF WeighbridgeWeighViewModel: a weigh sets WeightIn when
// DateWeightIn is null, otherwise WeightOut.
// Anonymous: the Weigh-In/Weigh-Out kiosk screens never log a user in (matching the
// unauthenticated WPF Touch app, which talked straight to the DB).
[ApiController]
[AllowAnonymous]
[Route("api/touch")]
public class TouchController : ControllerBase
{
    private const int RawMaterialTypeId = 4; // Globeweigh.Model eProductType.RawMaterial

    private readonly GbEvergreenContext _db;

    public TouchController(GbEvergreenContext db) => _db = db;

    // ---- Delivery lookups (Haulier -> Product -> Supplier -> Bog) ----

    // GET /api/touch/hauliers — intake hauliers (mirrors GetIntakeHaulierList).
    [HttpGet("hauliers")]
    public async Task<ActionResult<IEnumerable<object>>> Hauliers(CancellationToken ct = default)
    {
        var rows = await _db.Set<Haulier>().AsNoTracking()
            .Where(h => h.DateDeleted == null && h.IntakeHaulier)
            .OrderBy(h => h.Name)
            .Select(h => new { id = h.Id, name = h.Name })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // GET /api/touch/products — raw-material products (mirrors GetProductViewsAsync(RawMaterial)).
    [HttpGet("products")]
    public async Task<ActionResult<IEnumerable<object>>> Products(CancellationToken ct = default)
    {
        var rows = await _db.Set<ProductRow>().AsNoTracking()
            .Where(p => p.DateDeleted == null && p.ProductTypeId == RawMaterialTypeId)
            .OrderBy(p => p.Description)
            .Select(p => new { id = p.Id, description = p.Description })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // GET /api/touch/products/{id}/suppliers — suppliers of a product (mirrors GetProductSupplierList).
    [HttpGet("products/{id:int}/suppliers")]
    public async Task<ActionResult<IEnumerable<object>>> ProductSuppliers(int id, CancellationToken ct = default)
    {
        var rows = await _db.Set<ProductSupplierRow>().AsNoTracking()
            .Where(s => s.ProductId == id)
            .OrderBy(s => s.Name)
            .Select(s => new { id = s.Id, name = s.Name, supplierId = s.SupplierId })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // GET /api/touch/suppliers/{id}/bogs — a supplier's bogs (mirrors GetSupplier_BogList).
    [HttpGet("suppliers/{id:int}/bogs")]
    public async Task<ActionResult<IEnumerable<object>>> SupplierBogs(int id, CancellationToken ct = default)
    {
        var rows = await _db.Set<SupplierBog>().AsNoTracking()
            .Where(b => b.SupplierId == id)
            .OrderBy(b => b.Name)
            .Select(b => new { id = b.Id, name = b.Name })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // ---- Weigh-In (Delivery): create a new incoming record already weighed in ----

    // POST /api/touch/weigh-in — mirrors WeighbridgeWeighViewModel saving a fresh delivery record.
    [HttpPost("weigh-in")]
    public async Task<ActionResult> WeighInDelivery([FromBody] DeliveryWeighInInput input, CancellationToken ct = default)
    {
        if (input == null) return BadRequest(new { message = "Missing body." });
        if (input.HaulierId <= 0) return BadRequest(new { message = "Haulier is required." });
        if (string.IsNullOrWhiteSpace(input.VehicleReg)) return BadRequest(new { message = "Vehicle reg is required." });

        var rec = new WeighbridgeRecord
        {
            IsOutgoing = false,
            HaulierId = input.HaulierId,
            HaulierName = input.HaulierName,
            Product = input.Product,
            Supplier = input.Supplier,
            BogName = input.BogName,
            VehicleReg = input.VehicleReg,
            DocketNumber = input.DocketNumber,
            WeightIn = input.Weight,
            DateWeightIn = DateTime.Now,
            WeightOut = 0,
            DateCreated = DateTime.Now,
        };
        _db.Add(rec);
        await _db.SaveChangesAsync(ct);
        // The last 3 digits of the id are the collection reference the haulier is given.
        return Ok(new { id = rec.Id, idRef = (rec.Id % 1000).ToString("D3") });
    }

    // ---- Weigh-In (Collection): look up a pre-load by its 3-digit ref, then weigh ----

    // GET /api/touch/collection/{ref3} — find the pre-load load by its zero-padded 3-digit ref.
    [HttpGet("collection/{idRef}")]
    public async Task<ActionResult> Collection(string idRef, CancellationToken ct = default)
    {
        var refId = (idRef ?? "").Trim().PadLeft(3, '0');
        var load = await _db.Set<WeighbridgePreLoadRow>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdRef == refId, ct);
        if (load == null) return NotFound(new { message = $"No load found for reference '{refId}'." });

        var orders = await _db.Set<DispatchOrderRow>().AsNoTracking()
            .Where(o => o.WeighbridgeRecordId == load.Id)
            // Delivery order (1 = dropped first); unsequenced orders go last, not first as a
            // bare OrderBy on a nullable would put them in SQL.
            .OrderBy(o => o.SortOrder == null)
            .ThenBy(o => o.SortOrder)
            .ThenBy(o => o.Id)
            .ToListAsync(ct);

        return Ok(new { id = load.Id, idRef = load.IdRef, haulierName = load.HaulierName, orders });
    }

    // POST /api/touch/collection/{id}/weigh — set trailer no and record the weight (toggles in/out).
    [HttpPost("collection/{id:int}/weigh")]
    public async Task<ActionResult> CollectionWeigh(int id, [FromBody] CollectionWeighInput input, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.TrailerNo)) return BadRequest(new { message = "Trailer no is required." });

        rec.TrailerNo = input.TrailerNo;
        ApplyWeigh(rec, input.Weight);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Sample Weight: records weighed in on a day -> record a sample box weight ----

    // GET /api/touch/to-be-sampled?date=yyyy-MM-dd — records weighed in on the given day
    // (mirrors GetWeighbridgeRecordsToBeSampled, filtered on DateWeightIn). Defaults to today.
    [HttpGet("to-be-sampled")]
    public async Task<ActionResult<IEnumerable<WeighbridgeToSampleRow>>> ToBeSampled(
        [FromQuery] DateTime? date = null, CancellationToken ct = default)
    {
        var start = (date ?? DateTime.Today).Date;
        var end = start.AddDays(1);
        return await _db.Set<WeighbridgeToSampleRow>().AsNoTracking()
            .Where(r => r.DateWeightIn >= start && r.DateWeightIn < end && !r.IsOutgoing)
            .OrderBy(r => r.DateWeightIn)
            .ToListAsync(ct);
    }

    // POST /api/touch/{id}/sample-weight — record the sample box weight (mirrors AddSampleWeight).
    [HttpPost("{id:int}/sample-weight")]
    public async Task<ActionResult> AddSampleWeight(int id, [FromBody] SampleWeightInput input, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        rec.SampleBoxWeight = input.Weight;
        rec.DateSampleWeightAdded = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Weigh-Out: pending list -> confirm reg -> record out-weight ----

    // GET /api/touch/pending — records weighed in but not out (mirrors GetWeighbridgeRecordsWeighedIn).
    // Skip pre-loads that have not been weighed in yet (DateWeightIn null), and outgoing
    // Collections (IsOutgoing) — those are weighed out through Dispatch/Picking, not here.
    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<WeighbridgePendingRow>>> Pending(CancellationToken ct = default)
    {
        return await _db.Set<WeighbridgePendingRow>().AsNoTracking()
            .Where(r => r.DateWeightIn != null && !r.IsOutgoing)
            .OrderByDescending(r => r.PendingWeighDate)
            .ToListAsync(ct);
    }

    // POST /api/touch/pending/{id}/weigh-out — confirm the vehicle reg then record the out-weight.
    [HttpPost("pending/{id:int}/weigh-out")]
    public async Task<ActionResult> WeighOut(int id, [FromBody] WeighOutInput input, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id && !r.IsOutgoing, ct);
        if (rec == null) return NotFound();

        // Confirm the entered reg matches the load (whitespace-insensitive, case-insensitive) —
        // the same guard the WPF WeighbridgeOutViewModel applies before weighing out.
        if (!RegEquals(input.VehicleReg, rec.VehicleReg))
            return BadRequest(new { message = "Vehicle Reg entered does not match" });

        rec.WeightOut = input.Weight;
        rec.DateWeightOut = DateTime.Now;
        if (rec.DateWeightIn == null) rec.DateWeightIn = DateTime.Now; // safety: never complete without an in-weight
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // A weigh records WeightIn the first time, WeightOut thereafter (WPF toggle on DateWeightIn).
    private static void ApplyWeigh(WeighbridgeRecord rec, int weight)
    {
        if (rec.DateWeightIn == null)
        {
            rec.WeightIn = weight;
            rec.DateWeightIn = DateTime.Now;
        }
        else
        {
            rec.WeightOut = weight;
            rec.DateWeightOut = DateTime.Now;
        }
    }

    private static bool RegEquals(string? a, string? b) =>
        string.Equals(Strip(a), Strip(b), StringComparison.OrdinalIgnoreCase);

    private static string Strip(string? s) =>
        new string((s ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
}

public class DeliveryWeighInInput
{
    public int HaulierId { get; set; }
    public string? HaulierName { get; set; }
    public string? Product { get; set; }
    public string? Supplier { get; set; }
    public string? BogName { get; set; }
    public string? VehicleReg { get; set; }
    public string? DocketNumber { get; set; }
    public int Weight { get; set; }
}

public class CollectionWeighInput
{
    public string? TrailerNo { get; set; }
    public int Weight { get; set; }
}

public class WeighOutInput
{
    public string? VehicleReg { get; set; }
    public int Weight { get; set; }
}

public class SampleWeightInput
{
    public decimal Weight { get; set; }
}
