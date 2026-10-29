using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/hauliers")]
public class HauliersController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public HauliersController(GbEvergreenContext db) => _db = db;

    // GET /api/hauliers — mirrors IHaulierRepository.GetHaulierList(). CreatedBy is
    // resolved from GlobeweighUser for the super-admin-only audit columns on the grid.
    // Each row carries its email addresses (HaulierEmail rows), so the Emails panel beside
    // the list and the Edit Haulier > Emails tab need no second call.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<HaulierRow>>> List(
        [FromQuery] bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var q = _db.Set<Haulier>().AsNoTracking();
        if (!includeDeleted) q = q.Where(h => h.DateDeleted == null);

        var rows = await (
            from h in q
            join u in _db.GlobeweighUsers.AsNoTracking() on h.CreatedById equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            orderby h.Name
            select new HaulierRow
            {
                Id = h.Id,
                Code = h.Code,
                Name = h.Name,
                ContactName = h.ContactName,
                Tel = h.Tel,
                AddressLine1 = h.AddressLine1,
                AddressLine2 = h.AddressLine2,
                AddressLine3 = h.AddressLine3,
                AddressLine4 = h.AddressLine4,
                AddressLine5 = h.AddressLine5,
                VatNumber = h.VatNumber,
                NominalCode = h.NominalCode,
                VatCode = h.VatCode,
                IntakeHaulier = h.IntakeHaulier,
                DateCreated = h.DateCreated,
                DateDeleted = h.DateDeleted,
                CreatedById = h.CreatedById,
                DeletedById = h.DeletedById,
                CreatedBy = u == null ? null : (u.FirstName + " " + u.Surname).Trim(),
            }).ToListAsync(ct);

        var emails = await EmailsByHaulier(rows.Select(r => r.Id).ToList(), ct);
        foreach (var r in rows)
            if (emails.TryGetValue(r.Id, out var list)) r.Emails = list;
        return rows;
    }

    // POST /api/hauliers — create, with its email addresses.
    [HttpPost]
    public async Task<ActionResult<HaulierRow>> Create([FromBody] HaulierInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { message = "Name is required." });
        var emails = NormaliseEmails(input.Emails);
        if (emails.Any(e => e.Length > 200))
            return BadRequest(new { message = "Email addresses must be 200 characters or fewer." });

        var h = new Haulier
        {
            DateCreated = DateTime.Now,
            CreatedById = this.GetUserId(),
            DeletedById = 0,
        };
        Apply(h, input);

        // The email rows need the new id, so two saves — in one transaction, so a failure
        // on the second never leaves a haulier behind without its addresses.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Add(h);
        await _db.SaveChangesAsync(ct);
        foreach (var e in emails)
            _db.Add(new HaulierEmail { HaulierId = h.Id, Email = e, DateCreated = DateTime.Now });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToRow(h, emails);
    }

    // PUT /api/hauliers/{id} — update. The email list given is the haulier's whole list:
    // addresses left out are removed, new ones added.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<HaulierRow>> Update(int id, [FromBody] HaulierInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { message = "Name is required." });
        var emails = NormaliseEmails(input.Emails);
        if (emails.Any(e => e.Length > 200))
            return BadRequest(new { message = "Email addresses must be 200 characters or fewer." });

        var h = await _db.Set<Haulier>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h == null) return NotFound();
        Apply(h, input);
        await SyncEmails(h.Id, emails, ct);
        await _db.SaveChangesAsync(ct);
        return ToRow(h, emails);
    }

    // DELETE /api/hauliers/{id} — soft delete (DateDeleted), mirrors MarkAsDeletedAsync.
    // The HaulierEmail rows stay with the record.
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        var h = await _db.Set<Haulier>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h == null) return NotFound();
        h.DateDeleted = DateTime.Now;
        h.DeletedById = this.GetUserId();
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(Haulier h, HaulierInput i)
    {
        h.Code = i.Code;
        h.Name = i.Name;
        h.ContactName = i.ContactName;
        h.Tel = i.Tel;
        h.AddressLine1 = i.AddressLine1;
        h.AddressLine2 = i.AddressLine2;
        h.AddressLine3 = i.AddressLine3;
        h.AddressLine4 = i.AddressLine4;
        h.AddressLine5 = i.AddressLine5;
        h.VatNumber = i.VatNumber;
        h.NominalCode = i.NominalCode;
        h.VatCode = i.VatCode;
        h.IntakeHaulier = i.IntakeHaulier;
    }

    // The HaulierEmail rows of these hauliers, keyed by haulier, in the order they were added.
    private async Task<Dictionary<int, List<string>>> EmailsByHaulier(List<int> ids, CancellationToken ct)
    {
        var rows = await _db.Set<HaulierEmail>().AsNoTracking()
            .Where(e => ids.Contains(e.HaulierId))
            .OrderBy(e => e.Id)
            .Select(e => new { e.HaulierId, e.Email })
            .ToListAsync(ct);
        return rows
            .GroupBy(e => e.HaulierId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Email).ToList());
    }

    // Makes the haulier's HaulierEmail rows match `wanted`: rows no longer wanted go, new
    // ones are added, and an exact match stays as it is (keeping its id and DateCreated).
    // Queued on the context — the caller saves.
    private async Task SyncEmails(int haulierId, List<string> wanted, CancellationToken ct)
    {
        var existing = await _db.Set<HaulierEmail>().Where(e => e.HaulierId == haulierId).ToListAsync(ct);
        foreach (var e in existing)
            if (!wanted.Contains(e.Email, StringComparer.Ordinal)) _db.Remove(e);
        foreach (var w in wanted)
            if (!existing.Any(e => e.Email == w))
                _db.Add(new HaulierEmail { HaulierId = haulierId, Email = w, DateCreated = DateTime.Now });
    }

    // Trimmed, blanks dropped, case-insensitive duplicates dropped, order kept.
    private static List<string> NormaliseEmails(IEnumerable<string?>? emails)
    {
        var result = new List<string>();
        foreach (var raw in emails ?? Enumerable.Empty<string?>())
        {
            var e = (raw ?? "").Trim();
            if (e.Length == 0 || result.Contains(e, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(e);
        }
        return result;
    }

    // The create/update echo: the entity's columns plus the emails just saved. No CreatedBy —
    // the grid refreshes after a save anyway.
    private static HaulierRow ToRow(Haulier h, List<string> emails) => new()
    {
        Id = h.Id,
        Code = h.Code,
        Name = h.Name,
        ContactName = h.ContactName,
        Tel = h.Tel,
        Emails = emails,
        AddressLine1 = h.AddressLine1,
        AddressLine2 = h.AddressLine2,
        AddressLine3 = h.AddressLine3,
        AddressLine4 = h.AddressLine4,
        AddressLine5 = h.AddressLine5,
        VatNumber = h.VatNumber,
        NominalCode = h.NominalCode,
        VatCode = h.VatCode,
        IntakeHaulier = h.IntakeHaulier,
        DateCreated = h.DateCreated,
        DateDeleted = h.DateDeleted,
        CreatedById = h.CreatedById,
        DeletedById = h.DeletedById,
    };
}

// List shape: every Haulier column plus its email addresses and the resolved creator name.
public class HaulierRow
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? ContactName { get; set; }
    public string? Tel { get; set; }
    // Every address on the haulier (HaulierEmail rows), in the order they were added.
    public List<string> Emails { get; set; } = new();
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AddressLine3 { get; set; }
    public string? AddressLine4 { get; set; }
    public string? AddressLine5 { get; set; }
    public string? VatNumber { get; set; }
    public string? NominalCode { get; set; }
    public string? VatCode { get; set; }
    public bool IntakeHaulier { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }
    public int CreatedById { get; set; }
    public int DeletedById { get; set; }
    public string? CreatedBy { get; set; }
}

public class HaulierInput
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? ContactName { get; set; }
    public string? Tel { get; set; }
    // The haulier's whole email list (see Update).
    public List<string?>? Emails { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AddressLine3 { get; set; }
    public string? AddressLine4 { get; set; }
    public string? AddressLine5 { get; set; }
    public string? VatNumber { get; set; }
    public string? NominalCode { get; set; }
    public string? VatCode { get; set; }
    public bool IntakeHaulier { get; set; }
}
