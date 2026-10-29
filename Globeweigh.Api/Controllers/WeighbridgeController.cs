using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/weighbridge")]
public class WeighbridgeController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public WeighbridgeController(GbEvergreenContext db) => _db = db;

    // GET /api/weighbridge/complete — completed weighbridge records, optionally date-filtered.
    // Deliveries/Incoming only shows incoming deliveries; completed outgoing
    // Collections belong on the Orders/Dispatch page (completed-weighbridge).
    [HttpGet("complete")]
    public async Task<ActionResult<IEnumerable<WeighbridgeCompleteRow>>> Complete(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var q = _db.Set<WeighbridgeCompleteRow>().AsNoTracking().Where(r => !r.IsOutgoing);
        if (from != null) q = q.Where(r => r.DateWeightIn >= from);
        if (to != null) q = q.Where(r => r.DateWeightIn < to.Value.AddDays(1));
        return await q.OrderByDescending(r => r.DateWeightIn).ToListAsync(ct);
    }

    // GET /api/weighbridge/pending — pending (weighed-in, not yet out) records.
    // Exclude records that have not been weighed in yet (DateWeightIn null) —
    // those are still pre-loads and must not appear in a pending list.
    // Deliveries/Incoming only shows incoming deliveries (Touch weigh-in), so
    // this list excludes IsOutgoing records; outgoing Collections belong on the
    // Orders/Dispatch page.
    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<WeighbridgePendingRow>>> Pending(
        CancellationToken ct = default)
    {
        return await _db.Set<WeighbridgePendingRow>()
            .AsNoTracking()
            .Where(r => r.DateWeightIn != null && !r.IsOutgoing)
            .OrderByDescending(r => r.PendingWeighDate)
            .ToListAsync(ct);
    }

    // GET /api/weighbridge/{id} — single record, used to prefill the edit form
    // (the grids read the vwWeighbridge* views which omit write-only fields like
    // HaulierId, Comments and SampleBoxWeight).
    [HttpGet("{id:int}")]
    public async Task<ActionResult<WeighbridgeRecord>> Get(int id, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        return rec;
    }

    // POST /api/weighbridge — create a record.
    [HttpPost]
    public async Task<ActionResult<WeighbridgeRecord>> Create([FromBody] WeighbridgeRecordInput input, CancellationToken ct = default)
    {
        if (input.HaulierId <= 0) return BadRequest(new { message = "Haulier is required." });
        var rec = new WeighbridgeRecord { DateCreated = DateTime.Now };
        await ApplyAsync(rec, input, ct);
        _db.Add(rec);
        await _db.SaveChangesAsync(ct);
        return rec;
    }

    // PUT /api/weighbridge/{id} — update the editable fields of a record.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<WeighbridgeRecord>> Update(int id, [FromBody] WeighbridgeRecordInput input, CancellationToken ct = default)
    {
        if (input.HaulierId <= 0) return BadRequest(new { message = "Haulier is required." });
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        await ApplyAsync(rec, input, ct);
        await _db.SaveChangesAsync(ct);
        return rec;
    }

    // DELETE /api/weighbridge/{id} — soft delete (matches the WPF MarkAsDeletedAsync).
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        rec.DateDeleted = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Maps the input onto the entity. HaulierName is denormalised on the record
    // (same as the WPF/dispatch writes), so resolve it from the selected haulier.
    private async Task ApplyAsync(WeighbridgeRecord rec, WeighbridgeRecordInput i, CancellationToken ct)
    {
        rec.IsOutgoing = i.IsOutgoing;
        rec.HaulierId = i.HaulierId;
        rec.HaulierName = await _db.Set<Haulier>()
            .Where(h => h.Id == i.HaulierId).Select(h => h.Name).FirstOrDefaultAsync(ct);
        rec.VehicleReg = i.VehicleReg;
        rec.TrailerNo = i.TrailerNo;
        rec.DocketNumber = i.DocketNumber;
        rec.Product = i.Product;
        rec.Supplier = i.Supplier;
        rec.BogName = i.BogName;
        rec.WeightIn = i.WeightIn;
        rec.DateWeightIn = i.DateWeightIn;
        rec.WeightOut = i.WeightOut;
        rec.DateWeightOut = i.DateWeightOut;
        rec.SampleBoxWeight = i.SampleBoxWeight;
        rec.Comments = i.Comments;
    }
}

// Writable fields of a weighbridge record (audit/state dates are managed server-side
// or supplied directly). Mirrors the WPF EditWeighbrideRecordView form.
public class WeighbridgeRecordInput
{
    public bool IsOutgoing { get; set; }
    public int HaulierId { get; set; }
    public string? VehicleReg { get; set; }
    public string? TrailerNo { get; set; }
    public string? DocketNumber { get; set; }
    public string? Product { get; set; }
    public string? Supplier { get; set; }
    public string? BogName { get; set; }
    public int WeightIn { get; set; }
    public DateTime? DateWeightIn { get; set; }
    public int WeightOut { get; set; }
    public DateTime? DateWeightOut { get; set; }
    public decimal? SampleBoxWeight { get; set; }
    public string? Comments { get; set; }
}
