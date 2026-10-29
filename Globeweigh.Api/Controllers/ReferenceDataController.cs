using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reference-data")]
public class ReferenceDataController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public ReferenceDataController(GbEvergreenContext db) => _db = db;

    // GET /api/reference-data/types — entity types flagged as reference-data.
    [HttpGet("types")]
    public async Task<ActionResult<IEnumerable<EntityTypeRow>>> Types(CancellationToken ct = default)
    {
        return await _db.Set<EntityTypeRow>()
            .AsNoTracking()
            .Where(e => e.IsReferenceDataType)
            .OrderBy(e => e.DisplayName)
            .ToListAsync(ct);
    }

    // GET /api/reference-data?refTypeId= — reference-data items of a given type.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReferenceDataItem>>> List(
        [FromQuery] int refTypeId,
        CancellationToken ct = default)
    {
        return await _db.Set<ReferenceDataItem>()
            .AsNoTracking()
            .Where(r => r.RefTypeId == refTypeId && r.DateDeleted == null)
            .OrderBy(r => r.DisplayName)
            .ToListAsync(ct);
    }

    // POST /api/reference-data — create.
    [HttpPost]
    public async Task<ActionResult<ReferenceDataItem>> Create([FromBody] RefDataInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.DisplayName))
            return BadRequest(new { message = "Display Name is required." });

        // RefType is NOT NULL — reuse the RefType of any existing row of the same type.
        var refType = await _db.Set<ReferenceDataItem>()
            .Where(r => r.RefTypeId == input.RefTypeId)
            .Select(r => r.RefType)
            .FirstOrDefaultAsync(ct) ?? "";

        var item = new ReferenceDataItem
        {
            RefTypeId = input.RefTypeId,
            RefType = refType,
            Code = input.Code,
            DisplayName = input.DisplayName,
            AlternativeDisplayName = input.AlternativeDisplayName,
            DateCreated = DateTime.Now,
            DecimalData = 0,
            BoolData = false,
            IntData = 0,
            ForeignKey = 0,
        };
        _db.Add(item);
        await _db.SaveChangesAsync(ct);
        return item;
    }

    // PUT /api/reference-data/{id} — update.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ReferenceDataItem>> Update(int id, [FromBody] RefDataInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.DisplayName))
            return BadRequest(new { message = "Display Name is required." });

        var item = await _db.Set<ReferenceDataItem>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item == null) return NotFound();
        item.Code = input.Code;
        item.DisplayName = input.DisplayName;
        item.AlternativeDisplayName = input.AlternativeDisplayName;
        await _db.SaveChangesAsync(ct);
        return item;
    }

    // DELETE /api/reference-data/{id} — soft delete (DateDeleted).
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        var item = await _db.Set<ReferenceDataItem>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item == null) return NotFound();
        item.DateDeleted = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public class RefDataInput
{
    public int RefTypeId { get; set; }
    public string? Code { get; set; }
    public string? DisplayName { get; set; }
    public string? AlternativeDisplayName { get; set; }
}
