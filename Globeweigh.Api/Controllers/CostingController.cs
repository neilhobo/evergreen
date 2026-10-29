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
[Route("api/costing")]
public class CostingController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public CostingController(GbEvergreenContext db) => _db = db;

    // GET /api/costing/products — finished-product picker for the costing screen.
    // Mirrors the WPF CostingPriceListViewModel: finished products only,
    // ordered by category then id.
    [HttpGet("products")]
    public async Task<ActionResult<IEnumerable<ProductRow>>> Products(
        CancellationToken ct = default)
    {
        const int finishedProductTypeId = 1; // eProductType.FinishedProduct
        return await _db.Set<ProductRow>().AsNoTracking()
            .Where(p => p.DateDeleted == null && p.ProductTypeId == finishedProductTypeId)
            .OrderBy(p => p.CategoryId).ThenBy(p => p.Id)
            .ToListAsync(ct);
    }

    // GET /api/costing/products/{productId}/zones — zone prices for a product
    // (WPF order: by zone id).
    [HttpGet("products/{productId:int}/zones")]
    public async Task<ActionResult<IEnumerable<CostingZonePriceRow>>> Zones(
        int productId,
        CancellationToken ct = default)
    {
        return await _db.Set<CostingZonePriceRow>().AsNoTracking()
            .Where(z => z.ProductId == productId)
            .OrderBy(z => z.CostingZoneId)
            .ToListAsync(ct);
    }

    // GET /api/costing/prices — every zone price row (the "Costing Full" matrix
    // pivots these client-side into one column per zone).
    [HttpGet("prices")]
    public async Task<ActionResult<IEnumerable<CostingZonePriceRow>>> AllPrices(
        CancellationToken ct = default)
    {
        return await _db.Set<CostingZonePriceRow>().AsNoTracking()
            .ToListAsync(ct);
    }

    // PUT /api/costing/products/{productId}/zone-prices — bulk upsert of a product's
    // price in every zone (Costing matrix Edit dialog). Updates write a
    // CostingZonePriceHistory audit row when the price changes; zones the product
    // had no price row for get a new row.
    [HttpPut("products/{productId:int}/zone-prices")]
    public async Task<ActionResult> UpdateProductZonePrices(
        int productId,
        [FromBody] ProductZonePricesInput input,
        CancellationToken ct = default)
    {
        if (input.Items == null || input.Items.Count == 0)
            return BadRequest(new { message = "No prices supplied." });

        var existing = await _db.Set<CostingZonePriceEntity>()
            .Where(p => p.ProductId == productId)
            .ToListAsync(ct);
        var gtin = await _db.Set<ProductRow>().AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.Gtin)
            .FirstOrDefaultAsync(ct);
        var changedBy = User.FindFirst("name")?.Value ?? "admin";

        foreach (var item in input.Items)
        {
            var entity = existing.FirstOrDefault(p => p.CostingZoneId == item.CostingZoneId);
            if (entity == null)
            {
                _db.Add(new CostingZonePriceEntity
                {
                    CostingZoneId = item.CostingZoneId,
                    ProductId = productId,
                    Gtin = gtin,
                    Price = item.Price,
                    NA = item.NA,
                });
                continue;
            }

            if (entity.Price != item.Price)
            {
                _db.Add(new CostingZonePriceHistoryEntity
                {
                    ConstingZonePriceId = entity.Id,
                    ProductId = entity.ProductId,
                    ZoneId = entity.CostingZoneId,
                    OldPrice = entity.Price,
                    NewPrice = item.Price,
                    ChangedBy = changedBy,
                    DateCreated = DateTime.Now,
                });
            }
            entity.Price = item.Price;
            entity.NA = item.NA;
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/costing/zone-price/{id}/history — price-change audit trail for one
    // zone price, newest first (WPF "Pricing History" grid).
    [HttpGet("zone-price/{id:int}/history")]
    public async Task<ActionResult<IEnumerable<CostingZonePriceHistoryEntity>>> ZonePriceHistory(
        int id,
        CancellationToken ct = default)
    {
        return await _db.Set<CostingZonePriceHistoryEntity>().AsNoTracking()
            .Where(h => h.ConstingZonePriceId == id)
            .OrderByDescending(h => h.DateCreated)
            .ToListAsync(ct);
    }

    // GET /api/costing/zones/{zoneId}/prices — all product prices for a costing zone
    // (used by the order-item picker to pre-fill price from the customer's zone).
    [HttpGet("zones/{zoneId:int}/prices")]
    public async Task<ActionResult<IEnumerable<object>>> ZonePrices(
        int zoneId,
        CancellationToken ct = default)
    {
        var rows = await _db.Set<CostingZonePriceRow>().AsNoTracking()
            .Where(z => z.CostingZoneId == zoneId)
            .Select(z => new { z.ProductId, z.Price, z.NA })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // ----- Costing Zone management (Admin "Costing Zones") -----

    // GET /api/costing/zones — all costing zones (Admin list).
    [HttpGet("zones")]
    public async Task<ActionResult<IEnumerable<CostingZone>>> ListZones(CancellationToken ct = default)
    {
        return await _db.Set<CostingZone>().AsNoTracking()
            .Where(z => z.DateDeleted == null)
            .OrderBy(z => z.Id)
            .ToListAsync(ct);
    }

    // POST /api/costing/zones — create a zone.
    [HttpPost("zones")]
    public async Task<ActionResult<CostingZone>> CreateZone([FromBody] CostingZoneEditInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Description))
            return BadRequest(new { message = "Description is required." });

        var zone = new CostingZone
        {
            Description = input.Description,
            SecondaryDescription = input.SecondaryDescription,
            IsEuro = input.IsEuro,
            GroupId = 0,
        };
        _db.Add(zone);
        await _db.SaveChangesAsync(ct);
        return zone;
    }

    // PUT /api/costing/zones/{id} — update a zone.
    [HttpPut("zones/{id:int}")]
    public async Task<ActionResult<CostingZone>> UpdateZone(int id, [FromBody] CostingZoneEditInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Description))
            return BadRequest(new { message = "Description is required." });

        var zone = await _db.Set<CostingZone>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (zone == null) return NotFound();
        zone.Description = input.Description;
        zone.SecondaryDescription = input.SecondaryDescription;
        zone.IsEuro = input.IsEuro;
        await _db.SaveChangesAsync(ct);
        return zone;
    }

    // DELETE /api/costing/zones/{id} — soft delete (DateDeleted).
    [HttpDelete("zones/{id:int}")]
    public async Task<ActionResult> DeleteZone(int id, CancellationToken ct = default)
    {
        var zone = await _db.Set<CostingZone>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (zone == null) return NotFound();
        zone.DateDeleted = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // PUT /api/costing/zone-price/{id} — update a zone price, recording an audit
    // row in CostingZonePriceHistory when the price changes.
    [HttpPut("zone-price/{id:int}")]
    public async Task<ActionResult<CostingZonePriceEntity>> UpdateZonePrice(
        int id,
        [FromBody] ZonePriceInput input,
        CancellationToken ct = default)
    {
        var entity = await _db.Set<CostingZonePriceEntity>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity == null) return NotFound();

        if (entity.Price != input.Price)
        {
            _db.Add(new CostingZonePriceHistoryEntity
            {
                ConstingZonePriceId = entity.Id,
                ProductId = entity.ProductId,
                ZoneId = entity.CostingZoneId,
                OldPrice = entity.Price,
                NewPrice = input.Price,
                ChangedBy = User.FindFirst("name")?.Value ?? "admin",
                DateCreated = DateTime.Now,
            });
        }

        entity.Price = input.Price;
        entity.NA = input.NA;
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    // ── Customer-specific pricing (Product detail panel) ──

    // GET /api/costing/products/{productId}/customer-prices — every customer with a price of
    // their own for the product, with their costing zone so the panel can set the two side by side.
    [HttpGet("products/{productId:int}/customer-prices")]
    public async Task<ActionResult<IEnumerable<CustomerPriceRow>>> CustomerPrices(
        int productId,
        CancellationToken ct = default)
    {
        var rows = await (
            from p in _db.Set<CustomerProductPrice>().AsNoTracking()
            join c in _db.Set<CustomerSupplier>().AsNoTracking() on p.CustomerSupplierId equals c.Id
            join z in _db.Set<CostingZone>().AsNoTracking() on c.CostingZoneId equals z.Id into zj
            from z in zj.DefaultIfEmpty()
            where p.ProductId == productId
            orderby c.Name
            select new CustomerPriceRow
            {
                Id = p.Id,
                CustomerSupplierId = c.Id,
                CustomerCode = c.Code,
                CustomerName = c.Name,
                CustomerDeleted = c.DateDeleted != null,
                CostingZoneId = c.CostingZoneId,
                CostingZone = z == null ? null : z.Description,
                IsEuro = z == null ? null : z.IsEuro,
                Price = p.Price,
                DateCreated = p.DateCreated,
                CreatedBy = p.CreatedBy,
                DateModified = p.DateModified,
                ModifiedBy = p.ModifiedBy,
            }).ToListAsync(ct);
        return rows;
    }

    // GET /api/costing/customers/{customerId}/prices — the customer's own prices, every product
    // (Add Order Item offers each one in place of the zone price).
    [HttpGet("customers/{customerId:int}/prices")]
    public async Task<ActionResult<IEnumerable<object>>> PricesForCustomer(
        int customerId,
        CancellationToken ct = default)
    {
        var rows = await _db.Set<CustomerProductPrice>().AsNoTracking()
            .Where(p => p.CustomerSupplierId == customerId)
            .Select(p => new { p.ProductId, p.Price })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // POST /api/costing/products/{productId}/customer-prices — give a customer a price of their
    // own for the product. One per customer per product: a second is refused, not overwritten.
    [HttpPost("products/{productId:int}/customer-prices")]
    public async Task<ActionResult> AddCustomerPrice(
        int productId,
        [FromBody] CustomerPriceInput input,
        CancellationToken ct = default)
    {
        if (input.CustomerSupplierId <= 0) return BadRequest(new { message = "Pick a customer." });
        if (input.Price < 0) return BadRequest(new { message = "The price cannot be negative." });
        if (!await _db.Set<ProductRow>().AnyAsync(p => p.Id == productId, ct)) return NotFound();
        if (!await _db.Set<CustomerSupplier>().AnyAsync(c => c.Id == input.CustomerSupplierId, ct))
            return BadRequest(new { message = "That customer no longer exists." });
        if (await _db.Set<CustomerProductPrice>().AnyAsync(
                p => p.ProductId == productId && p.CustomerSupplierId == input.CustomerSupplierId, ct))
            return Conflict(new { message = "That customer already has a price for this product — edit it instead." });

        var by = User.FindFirst("name")?.Value ?? "admin";
        var now = DateTime.Now;
        _db.Add(new CustomerProductPrice
        {
            CustomerSupplierId = input.CustomerSupplierId,
            ProductId = productId,
            Price = input.Price,
            DateCreated = now,
            CreatedBy = by,
        });
        _db.Add(new CustomerProductPriceHistory
        {
            CustomerSupplierId = input.CustomerSupplierId,
            ProductId = productId,
            NewPrice = input.Price,
            ChangedBy = by,
            DateCreated = now,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // PUT /api/costing/customer-price/{id} — change a customer's price.
    [HttpPut("customer-price/{id:int}")]
    public async Task<ActionResult> UpdateCustomerPrice(
        int id,
        [FromBody] CustomerPriceInput input,
        CancellationToken ct = default)
    {
        if (input.Price < 0) return BadRequest(new { message = "The price cannot be negative." });
        var entity = await _db.Set<CustomerProductPrice>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity == null) return NotFound();
        if (entity.Price == input.Price) return NoContent();

        var by = User.FindFirst("name")?.Value ?? "admin";
        var now = DateTime.Now;
        _db.Add(new CustomerProductPriceHistory
        {
            CustomerSupplierId = entity.CustomerSupplierId,
            ProductId = entity.ProductId,
            OldPrice = entity.Price,
            NewPrice = input.Price,
            ChangedBy = by,
            DateCreated = now,
        });
        entity.Price = input.Price;
        entity.DateModified = now;
        entity.ModifiedBy = by;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/costing/customer-price/{id} — the customer goes back to their zone price.
    [HttpDelete("customer-price/{id:int}")]
    public async Task<ActionResult> DeleteCustomerPrice(int id, CancellationToken ct = default)
    {
        var entity = await _db.Set<CustomerProductPrice>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity == null) return NotFound();
        _db.Add(new CustomerProductPriceHistory
        {
            CustomerSupplierId = entity.CustomerSupplierId,
            ProductId = entity.ProductId,
            OldPrice = entity.Price,
            ChangedBy = User.FindFirst("name")?.Value ?? "admin",
            DateCreated = DateTime.Now,
        });
        _db.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/costing/products/{productId}/customer-price-history — every add, change and
    // removal of a customer price for the product, newest first.
    [HttpGet("products/{productId:int}/customer-price-history")]
    public async Task<ActionResult<IEnumerable<object>>> CustomerPriceHistory(
        int productId,
        CancellationToken ct = default)
    {
        var rows = await (
            from h in _db.Set<CustomerProductPriceHistory>().AsNoTracking()
            join c in _db.Set<CustomerSupplier>().AsNoTracking() on h.CustomerSupplierId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where h.ProductId == productId
            orderby h.DateCreated descending, h.Id descending
            select new
            {
                h.Id,
                h.CustomerSupplierId,
                CustomerName = c == null ? null : c.Name,
                h.OldPrice,
                h.NewPrice,
                h.ChangedBy,
                h.DateCreated,
            }).ToListAsync(ct);
        return Ok(rows);
    }
}

public class CustomerPriceInput
{
    // Only read by POST; a PUT changes the price of the row it names.
    public int CustomerSupplierId { get; set; }
    public decimal Price { get; set; }
}

public class CustomerPriceRow
{
    public int Id { get; set; }
    public int CustomerSupplierId { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerName { get; set; }
    public bool CustomerDeleted { get; set; }
    public int CostingZoneId { get; set; }
    public string? CostingZone { get; set; }
    public bool? IsEuro { get; set; }
    public decimal Price { get; set; }
    public DateTime DateCreated { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? DateModified { get; set; }
    public string? ModifiedBy { get; set; }
}

public class ZonePriceInput
{
    public decimal Price { get; set; }
    public bool NA { get; set; }
}

public class ProductZonePricesInput
{
    public List<ProductZonePriceItem> Items { get; set; } = new();
}

public class ProductZonePriceItem
{
    public int CostingZoneId { get; set; }
    public decimal Price { get; set; }
    public bool NA { get; set; }
}

public class CostingZoneEditInput
{
    public string? Description { get; set; }
    public string? SecondaryDescription { get; set; }
    // null = Both, true = Euro, false = Sterling.
    public bool? IsEuro { get; set; }
}
