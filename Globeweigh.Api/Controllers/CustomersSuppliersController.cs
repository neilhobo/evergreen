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
[Route("api/customers-suppliers")]
public class CustomersSuppliersController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public CustomersSuppliersController(GbEvergreenContext db) => _db = db;

    // GET /api/customers-suppliers — mirrors the WPF CustomerSupplierListView.
    // typeId 1 = Customers, 2 = Suppliers; 0 = all. Reads the vwCustomerSupplier view,
    // plus each record's address count and its email addresses (so the Emails panel
    // beside the list and the Edit > Emails tab need no second call).
    [HttpGet]
    public async Task<ActionResult> List(
        [FromQuery] int typeId = 0,
        CancellationToken ct = default)
    {
        var q = _db.Set<CustomerSupplierRow>().AsNoTracking();
        if (typeId > 0) q = q.Where(c => c.TypeId == typeId);
        var rows = await q.OrderBy(c => c.Name).ToListAsync(ct);

        // Address count per record, for the grid's "Address Count" column.
        var counts = await _db.Set<CustomerSupplierAddress>().AsNoTracking()
            .GroupBy(a => a.CustomerSupplierId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var emails = await EmailsByRecord(rows.Select(r => r.Id).ToList(), ct);

        // Each customer's sales rep, from the table (the view does not carry it).
        var reps = await _db.Set<CustomerSupplier>().AsNoTracking()
            .Where(c => c.SalesRepId != null)
            .ToDictionaryAsync(c => c.Id, c => c.SalesRepId, ct);

        return Ok(rows.Select(r => new
        {
            r.Id,
            r.TypeId,
            r.Code,
            r.Name,
            r.CostingZoneId,
            r.CostingZone,
            SalesRepId = reps.GetValueOrDefault(r.Id),
            AddressCount = counts.TryGetValue(r.Id, out var n) ? n : 0,
            Emails = emails.TryGetValue(r.Id, out var list) ? list : new List<string>(),
        }));
    }

    // GET /api/customers-suppliers/costing-zones — for the editor dropdown.
    [HttpGet("costing-zones")]
    public async Task<ActionResult<IEnumerable<object>>> CostingZones(CancellationToken ct = default)
    {
        var zones = await _db.Set<Globeweigh.Api.Data.Entities.CostingZone>()
            .AsNoTracking()
            .Where(z => z.DateDeleted == null)
            .OrderBy(z => z.Description)
            .ToListAsync(ct);
        return Ok(zones.Select(z => new { id = z.Id, description = z.Description }));
    }

    // GET /api/customers-suppliers/{id}/addresses — addresses for a customer/supplier.
    [HttpGet("{id:int}/addresses")]
    public async Task<ActionResult<IEnumerable<CustomerSupplierAddress>>> Addresses(
        int id,
        CancellationToken ct = default)
    {
        return await _db.Set<CustomerSupplierAddress>()
            .AsNoTracking()
            .Where(a => a.CustomerSupplierId == id)
            .OrderBy(a => a.AddressName)
            .ToListAsync(ct);
    }

    // POST /api/customers-suppliers — create, with its email addresses.
    [HttpPost]
    public async Task<ActionResult<CustomerSupplier>> Create(
        [FromBody] CustomerSupplierInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { message = "Name is required." });
        var emails = NormaliseEmails(input.Emails);
        if (emails.Any(e => e.Length > 200))
            return BadRequest(new { message = "Email addresses must be 200 characters or fewer." });

        var c = new CustomerSupplier
        {
            DateCreated = DateTime.Now,
            CreatedById = this.GetUserId(),
            DeletedById = 0,
        };
        Apply(c, input);

        // The email rows need the new id, so two saves — in one transaction, so a failure
        // on the second never leaves a record behind without its addresses.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Add(c);
        await _db.SaveChangesAsync(ct);
        foreach (var e in emails)
            _db.Add(new CustomerSupplierEmail { CustomerSupplierId = c.Id, Email = e, DateCreated = DateTime.Now });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return c;
    }

    // PUT /api/customers-suppliers/{id} — update. The email list given is the record's
    // whole list: addresses left out are removed, new ones added.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<CustomerSupplier>> Update(
        int id,
        [FromBody] CustomerSupplierInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { message = "Name is required." });
        var emails = NormaliseEmails(input.Emails);
        if (emails.Any(e => e.Length > 200))
            return BadRequest(new { message = "Email addresses must be 200 characters or fewer." });

        var c = await _db.Set<CustomerSupplier>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return NotFound();
        Apply(c, input);
        await SyncEmails(c.Id, emails, ct);
        await _db.SaveChangesAsync(ct);
        return c;
    }

    // PUT /api/customers-suppliers/{id}/costing-zone — set only the costing zone.
    // Used by the dispatch "customer has no costing zone" prompt; leaves every other
    // customer field untouched (so it can't clobber IsBoth/Inactive etc.).
    [HttpPut("{id:int}/costing-zone")]
    public async Task<ActionResult> SetCostingZone(
        int id,
        [FromBody] CostingZoneInput input,
        CancellationToken ct = default)
    {
        var c = await _db.Set<CustomerSupplier>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return NotFound();
        c.CostingZoneId = input.CostingZoneId;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // eUserRole.Sales — the users a customer's sales rep is chosen from.
    private const int RoleSales = 6;

    // GET /api/customers-suppliers/sales-reps — the users with the Sales role, for the Sales Rep
    // dropdown. Disabled ones are included (flagged) so a customer still shows its rep's name.
    [HttpGet("sales-reps")]
    public async Task<ActionResult<IEnumerable<object>>> SalesReps(CancellationToken ct = default)
    {
        var ids = await SalesUserIds(ct);
        var users = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.DateDeleted == null)
            .OrderBy(u => u.FirstName).ThenBy(u => u.Surname)
            .Select(u => new { u.Id, Name = ((u.FirstName ?? "") + " " + (u.Surname ?? "")).Trim(), u.Disabled })
            .ToListAsync(ct);
        return Ok(users);
    }

    // PUT /api/customers-suppliers/{id}/sales-rep — set only the sales rep (null = none), as
    // the Sales Rep column's dropdown does on change. Leaves every other field untouched.
    [HttpPut("{id:int}/sales-rep")]
    public async Task<ActionResult> SetSalesRep(
        int id,
        [FromBody] SalesRepInput input,
        CancellationToken ct = default)
    {
        var c = await _db.Set<CustomerSupplier>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return NotFound();
        if (input.SalesRepId is { } rep && !(await SalesUserIds(ct)).Contains(rep))
            return BadRequest(new { message = "That user does not have the Sales role." });
        c.SalesRepId = input.SalesRepId;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Users holding the Sales role: the Globeweigh_Role junction, or the legacy single RoleId.
    private async Task<List<int>> SalesUserIds(CancellationToken ct)
    {
        var fromRoles = await _db.Set<UserRoleEntity>().AsNoTracking()
            .Where(r => r.RoleId == RoleSales).Select(r => r.UserId).ToListAsync(ct);
        var fromColumn = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.RoleId == RoleSales).Select(u => u.Id).ToListAsync(ct);
        return fromRoles.Concat(fromColumn).Distinct().ToList();
    }

    // DELETE /api/customers-suppliers/{id} — soft delete (DateDeleted).
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        var c = await _db.Set<CustomerSupplier>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return NotFound();
        c.DateDeleted = DateTime.Now;
        c.DeletedById = this.GetUserId();
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/customers-suppliers/{id}/addresses — create an address.
    [HttpPost("{id:int}/addresses")]
    public async Task<ActionResult<CustomerSupplierAddress>> CreateAddress(
        int id,
        [FromBody] AddressInput input,
        CancellationToken ct = default)
    {
        var a = new CustomerSupplierAddress
        {
            CustomerSupplierId = id,
            AddressSageRef = 0,
            DateCreated = DateTime.Now,
            DateLastModified = DateTime.Now,
        };
        ApplyAddress(a, input);
        _db.Add(a);
        await _db.SaveChangesAsync(ct);
        return a;
    }

    // PUT /api/customers-suppliers/addresses/{addrId} — update an address.
    [HttpPut("addresses/{addrId:int}")]
    public async Task<ActionResult<CustomerSupplierAddress>> UpdateAddress(
        int addrId,
        [FromBody] AddressInput input,
        CancellationToken ct = default)
    {
        var a = await _db.Set<CustomerSupplierAddress>().FirstOrDefaultAsync(x => x.Id == addrId, ct);
        if (a == null) return NotFound();
        ApplyAddress(a, input);
        a.DateLastModified = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return a;
    }

    // DELETE /api/customers-suppliers/addresses/{addrId} — hard delete (no soft-delete column).
    [HttpDelete("addresses/{addrId:int}")]
    public async Task<ActionResult> DeleteAddress(int addrId, CancellationToken ct = default)
    {
        var a = await _db.Set<CustomerSupplierAddress>().FirstOrDefaultAsync(x => x.Id == addrId, ct);
        if (a == null) return NotFound();
        _db.Remove(a);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // PUT /api/customers-suppliers/addresses/{addrId}/delivery-note — the address's
    // reusable Delivery Note Info text (Edit Customer > Delivery Note Info tab). Kept
    // apart from AddressInput so an ordinary address edit never touches it.
    [HttpPut("addresses/{addrId:int}/delivery-note")]
    public async Task<ActionResult> SetDeliveryNote(
        int addrId,
        [FromBody] DeliveryNoteInput input,
        CancellationToken ct = default)
    {
        var a = await _db.Set<CustomerSupplierAddress>().FirstOrDefaultAsync(x => x.Id == addrId, ct);
        if (a == null) return NotFound();
        var text = input.Text?.Trim();
        a.DeliveryNote = string.IsNullOrEmpty(text) ? null : text;
        a.DateLastModified = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/customers-suppliers/{id}/label-config — the customer's label overrides.
    // Logo travels as base64 JSON (bearer-token auth rules out plain <img src> URLs).
    [HttpGet("{id:int}/label-config")]
    public async Task<ActionResult<object>> GetLabelConfig(int id, CancellationToken ct = default)
    {
        var cfg = await _db.Set<CustomerLabelConfig>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CustomerSupplierId == id, ct);
        if (cfg?.LogoImage == null || cfg.LogoImage.Length == 0)
            return Ok(new { hasCustomLogo = false, logoFileName = (string?)null, logoContentType = (string?)null, logoBase64 = (string?)null });
        return Ok(new
        {
            hasCustomLogo = true,
            logoFileName = cfg.LogoFileName,
            logoContentType = cfg.LogoContentType ?? "image/png",
            logoBase64 = Convert.ToBase64String(cfg.LogoImage),
        });
    }

    // PUT /api/customers-suppliers/{id}/label-config/logo — set the customer's logo override.
    [HttpPut("{id:int}/label-config/logo")]
    public async Task<ActionResult> SetLabelLogo(
        int id,
        [FromBody] LabelLogoInput input,
        CancellationToken ct = default)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(input.Base64 ?? ""); }
        catch (FormatException) { return BadRequest(new { message = "Invalid image data." }); }
        if (bytes.Length == 0) return BadRequest(new { message = "Image is empty." });
        if (bytes.Length > 2 * 1024 * 1024) return BadRequest(new { message = "Image is too large (max 2 MB)." });

        var cfg = await _db.Set<CustomerLabelConfig>().FirstOrDefaultAsync(x => x.CustomerSupplierId == id, ct);
        if (cfg == null)
        {
            cfg = new CustomerLabelConfig { CustomerSupplierId = id };
            _db.Add(cfg);
        }
        cfg.LogoImage = bytes;
        cfg.LogoContentType = input.ContentType;
        cfg.LogoFileName = input.FileName;
        cfg.DateLastModified = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/customers-suppliers/{id}/label-config/logo — back to the default logo.
    [HttpDelete("{id:int}/label-config/logo")]
    public async Task<ActionResult> DeleteLabelLogo(int id, CancellationToken ct = default)
    {
        var cfg = await _db.Set<CustomerLabelConfig>().FirstOrDefaultAsync(x => x.CustomerSupplierId == id, ct);
        if (cfg == null) return NoContent();
        cfg.LogoImage = null;
        cfg.LogoContentType = null;
        cfg.LogoFileName = null;
        cfg.DateLastModified = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // The CustomerSupplierEmail rows of these records, keyed by record, in the order
    // they were added.
    private async Task<Dictionary<int, List<string>>> EmailsByRecord(List<int> ids, CancellationToken ct)
    {
        var rows = await _db.Set<CustomerSupplierEmail>().AsNoTracking()
            .Where(e => ids.Contains(e.CustomerSupplierId))
            .OrderBy(e => e.Id)
            .Select(e => new { e.CustomerSupplierId, e.Email })
            .ToListAsync(ct);
        return rows
            .GroupBy(e => e.CustomerSupplierId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Email).ToList());
    }

    // Makes the record's CustomerSupplierEmail rows match `wanted`: rows no longer wanted
    // go, new ones are added, and an exact match stays as it is (keeping its id and
    // DateCreated). Queued on the context — the caller saves.
    private async Task SyncEmails(int csId, List<string> wanted, CancellationToken ct)
    {
        var existing = await _db.Set<CustomerSupplierEmail>()
            .Where(e => e.CustomerSupplierId == csId).ToListAsync(ct);
        foreach (var e in existing)
            if (!wanted.Contains(e.Email, StringComparer.Ordinal)) _db.Remove(e);
        foreach (var w in wanted)
            if (!existing.Any(e => e.Email == w))
                _db.Add(new CustomerSupplierEmail { CustomerSupplierId = csId, Email = w, DateCreated = DateTime.Now });
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

    private static void Apply(CustomerSupplier c, CustomerSupplierInput i)
    {
        c.TypeId = i.TypeId;
        c.Code = i.Code;
        c.Name = i.Name;
        c.CostingZoneId = i.CostingZoneId;
        c.IsBoth = i.IsBoth;
        c.Inactive = i.Inactive;
    }

    private static void ApplyAddress(CustomerSupplierAddress a, AddressInput i)
    {
        a.AddressName = i.AddressName;
        a.Address1 = i.Address1;
        a.Address2 = i.Address2;
        a.Address3 = i.Address3;
        a.Address4 = i.Address4;
        a.Address5 = i.Address5;
        a.Telephone = i.Telephone;
        a.Telephone2 = i.Telephone2;
        a.Email = i.Email;
        a.ContactName = i.ContactName;
    }
}

public class CustomerSupplierInput
{
    public int TypeId { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public int CostingZoneId { get; set; }
    public bool IsBoth { get; set; }
    public bool Inactive { get; set; }
    // The record's whole email list (see Update).
    public List<string?>? Emails { get; set; }
}

public class CostingZoneInput
{
    public int CostingZoneId { get; set; }
}

public class SalesRepInput
{
    public int? SalesRepId { get; set; } // null = no rep
}

public class LabelLogoInput
{
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public string? Base64 { get; set; }
}

public class AddressInput
{
    public string? AddressName { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? Address3 { get; set; }
    public string? Address4 { get; set; }
    public string? Address5 { get; set; }
    public string? Telephone { get; set; }
    public string? Telephone2 { get; set; }
    public string? Email { get; set; }
    public string? ContactName { get; set; }
}

public class DeliveryNoteInput
{
    public string? Text { get; set; }
}
