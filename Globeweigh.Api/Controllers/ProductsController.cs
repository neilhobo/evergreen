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
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public ProductsController(GbEvergreenContext db) => _db = db;

    // GET /api/products — mirrors the WPF Products master list (read-only grid).
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductRow>>> List(
        [FromQuery] int productTypeId = 0,
        [FromQuery] bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var q = _db.Set<ProductRow>().AsNoTracking();
        if (!includeDeleted) q = q.Where(p => p.DateDeleted == null);
        if (productTypeId > 0) q = q.Where(p => p.ProductTypeId == productTypeId);
        return await q.OrderBy(p => p.Description).ToListAsync(ct);
    }

    // GET /api/products/product-types — lookup for the Product Type dropdown.
    [HttpGet("product-types")]
    public async Task<ActionResult<IEnumerable<object>>> ProductTypes(CancellationToken ct = default)
    {
        var rows = await _db.Set<ProductTypeRef>().AsNoTracking()
            .Where(t => t.DateDeleted == null)
            .OrderBy(t => t.TypeDescription)
            .Select(t => new { id = t.Id, typeDescription = t.TypeDescription })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // GET /api/products/categories — distinct categories from the view, for the Category dropdown.
    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<CategoryRef>>> Categories(CancellationToken ct = default)
    {
        var rows = await _db.Set<ProductRow>().AsNoTracking()
            .Where(p => p.CategoryName != null)
            .Select(p => new CategoryRef { Id = p.CategoryId, Name = p.CategoryName })
            .Distinct()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return Ok(rows);
    }

    // POST /api/products — create.
    [HttpPost]
    public async Task<ActionResult<Product>> Create([FromBody] ProductInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Description))
            return BadRequest(new { message = "Description is required." });

        var p = new Product
        {
            DateCreated = DateTime.Now,
            CreatedById = this.GetUserId(),
            DeletedById = 0,
        };
        Apply(p, input);
        _db.Add(p);
        await _db.SaveChangesAsync(ct);
        return p;
    }

    // PUT /api/products/{id} — update. When the save changes the Average Weight, every picked pallet of the
    // product still booked at 0 kg (a no-scan pick made while the product had no weighed
    // pallets and no Average Weight) takes that weight, flagged IsWeightEstimated, in the same
    // save. That includes dispatched orders — the client's call — so the response lists every
    // order touched, and whether it had gone, for the dialog to show.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductUpdateResult>> Update(int id, [FromBody] ProductInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Description))
            return BadRequest(new { message = "Description is required." });

        var p = await _db.Set<Product>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();
        var oldAverageWeight = p.AverageWeight;
        Apply(p, input);

        var zeroPicks = new List<DispatchOrderPickedEntity>();
        // Only an Average Weight edit re-books 0 kg picks: saving any other change must not
        // quietly re-weigh old (possibly dispatched) orders.
        if (p.AverageWeight is > 0 && p.AverageWeight != oldAverageWeight)
        {
            var liveOrderIds = _db.Set<DispatchOrderEntity>()
                .Where(o => o.DateDeleted == null)
                .Select(o => o.Id);
            zeroPicks = await _db.Set<DispatchOrderPickedEntity>()
                .Where(x => x.ProductId == id && x.DateDeleted == null && x.Weight <= 0
                    && liveOrderIds.Contains(x.DispatchOrderId))
                .ToListAsync(ct);
            foreach (var pick in zeroPicks)
            {
                pick.Weight = p.AverageWeight.Value;
                pick.IsWeightEstimated = true;
            }
        }
        await _db.SaveChangesAsync(ct);

        var updated = new List<ReweighedOrder>();
        if (zeroPicks.Count > 0)
        {
            var counts = zeroPicks.GroupBy(x => x.DispatchOrderId).ToDictionary(g => g.Key, g => g.Count());
            var ids = counts.Keys.ToList();
            updated = (await _db.Set<DispatchOrderRow>().AsNoTracking()
                    .Where(o => ids.Contains(o.Id))
                    .Select(o => new { o.Id, o.OrderNo, o.CustomerName, o.DateDispatched })
                    .ToListAsync(ct))
                .Select(o => new ReweighedOrder(o.Id, o.OrderNo, o.CustomerName, o.DateDispatched, counts[o.Id]))
                .OrderBy(o => o.OrderNo)
                .ToList();
        }
        return new ProductUpdateResult(p, updated);
    }

    // DELETE /api/products/{id} — soft delete (DateDeleted).
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        var p = await _db.Set<Product>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();
        p.DateDeleted = DateTime.Now;
        p.DeletedById = this.GetUserId();
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Raw Materials cascade (mirrors the WPF RawProductListView: Product -> Supplier -> Bog) ----

    // GET /api/products/{id}/suppliers — suppliers linked to a raw-material product.
    [HttpGet("{id:int}/suppliers")]
    public async Task<ActionResult<IEnumerable<object>>> ProductSuppliers(int id, CancellationToken ct = default)
    {
        var rows = await _db.Set<ProductSupplierRow>().AsNoTracking()
            .Where(s => s.ProductId == id)
            .OrderBy(s => s.Name)
            .Select(s => new { id = s.Id, name = s.Name, supplierId = s.SupplierId })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // POST /api/products/{id}/suppliers — link a supplier (mirrors AddProduct_Supplier).
    [HttpPost("{id:int}/suppliers")]
    public async Task<ActionResult> AddProductSupplier(int id, [FromBody] ProductSupplierInput input, CancellationToken ct = default)
    {
        if (input == null || input.SupplierId <= 0)
            return BadRequest(new { message = "Supplier is required." });

        var exists = await _db.Set<ProductSupplier>()
            .AnyAsync(x => x.ProductId == id && x.SupplierId == input.SupplierId, ct);
        if (exists) return BadRequest(new { message = "Supplier is already linked to this product." });

        _db.Add(new ProductSupplier { ProductId = id, SupplierId = input.SupplierId });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/products/suppliers/{id} — unlink (hard delete, mirrors RemoveProduct_Supplier).
    // {id} is the Product_Supplier row id, not the supplier id.
    [HttpDelete("suppliers/{id:int}")]
    public async Task<ActionResult> RemoveProductSupplier(int id, CancellationToken ct = default)
    {
        var ps = await _db.Set<ProductSupplier>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ps == null) return NotFound();
        _db.Remove(ps);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/products/suppliers/{supplierId}/bogs — a supplier's bogs (mirrors GetSupplier_BogList).
    [HttpGet("suppliers/{supplierId:int}/bogs")]
    public async Task<ActionResult<IEnumerable<object>>> SupplierBogs(int supplierId, CancellationToken ct = default)
    {
        var rows = await _db.Set<SupplierBog>().AsNoTracking()
            .Where(b => b.SupplierId == supplierId)
            .OrderBy(b => b.Name)
            .Select(b => new { id = b.Id, name = b.Name })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // POST /api/products/suppliers/{supplierId}/bogs — add a bog (mirrors AddUpdateSupplier_Bog).
    [HttpPost("suppliers/{supplierId:int}/bogs")]
    public async Task<ActionResult> CreateBog(int supplierId, [FromBody] BogInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input?.Name))
            return BadRequest(new { message = "Bog name is required." });

        _db.Add(new SupplierBog { SupplierId = supplierId, Name = input.Name.Trim() });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // PUT /api/products/bogs/{id} — rename a bog.
    [HttpPut("bogs/{id:int}")]
    public async Task<ActionResult> UpdateBog(int id, [FromBody] BogInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input?.Name))
            return BadRequest(new { message = "Bog name is required." });

        var bog = await _db.Set<SupplierBog>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (bog == null) return NotFound();
        bog.Name = input.Name.Trim();
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/products/bogs/{id} — hard delete (mirrors RemoveSupplier_Bog).
    [HttpDelete("bogs/{id:int}")]
    public async Task<ActionResult> DeleteBog(int id, CancellationToken ct = default)
    {
        var bog = await _db.Set<SupplierBog>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (bog == null) return NotFound();
        _db.Remove(bog);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(Product p, ProductInput i)
    {
        p.Gtin = i.Gtin;
        p.Description = i.Description;
        p.SkuCode = i.SkuCode;
        p.Size = i.Size;
        p.Upp = i.Upp;
        p.CommodityCode = i.CommodityCode;
        p.ProductTypeId = i.ProductTypeId;
        p.CategoryId = i.CategoryId;
        p.PosId = i.PosId;
        p.IsBulkBag = i.IsBulkBag;
        // Anything unrecognised or missing (e.g. an older client) is Pallet, the default.
        p.SoldBy = ProductSoldBy.IsValid(i.SoldBy) ? i.SoldBy! : ProductSoldBy.Pallet;
        // A product sold by the cubic meter has no Average Weight: it is never entered or kept.
        p.AverageWeight = p.SoldBy == ProductSoldBy.CubicMeter ? null
            : i.AverageWeight is > 0 ? Math.Round(i.AverageWeight.Value, 2) : null;
        // PalletWeight is not taken from the input: it is fixed at the column default (25 kg) for now.
        p.BagWeightGrams = i.BagWeightGrams is > 0 ? Math.Round(i.BagWeightGrams.Value, 2) : null;
    }
}

public class ProductInput
{
    public string? Gtin { get; set; }
    public string? Description { get; set; }
    public string? SkuCode { get; set; }
    public int? Size { get; set; }
    public int Upp { get; set; }
    public string? CommodityCode { get; set; }
    public int ProductTypeId { get; set; }
    public int CategoryId { get; set; }
    public int? PosId { get; set; }
    public bool IsBulkBag { get; set; }
    public decimal? AverageWeight { get; set; }
    public decimal? BagWeightGrams { get; set; }
    public string? SoldBy { get; set; }
}

// PUT /api/products/{id}: the saved product plus the orders whose 0 kg picks took its
// Average Weight (empty when there were none).
public record ProductUpdateResult(Product Product, List<ReweighedOrder> ReweighedOrders);
public record ReweighedOrder(int OrderId, int OrderNo, string? CustomerName, DateTime? DateDispatched, int Pallets);

public class ProductSupplierInput
{
    public int SupplierId { get; set; }
}

public class BogInput
{
    public string? Name { get; set; }
}

public class CategoryRef
{
    public int Id { get; set; }
    public string? Name { get; set; }
}
