using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Production Planning (Production > Production Planning, staged): the open order book
// totalled per product against what is on the floor, so the production manager can see
// what has to be made and for whom.
//
// Its own controller rather than an action on ProductionController: that one is
// [AllowAnonymous] for the factory touch screens, and the order book — customers, dates,
// quantities — is not something an unauthenticated device should be able to pull.
[ApiController]
[Authorize]
[Route("api/production/planning")]
public class PlanningController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public PlanningController(GbEvergreenContext db) => _db = db;

    // GET /api/production/planning — every product on an open order, with the order lines
    // behind it and its in-stock pallet count. The arithmetic (cover, when the stock runs
    // out, which orders count under a horizon) is the client's: it needs to re-cut the
    // same lines by horizon without a round trip, and every line is needed for the detail
    // anyway. Picked pallets are still on the floor until the load is dispatched, so
    // InStock includes them and Ordered is compared against it as-is.
    [HttpGet]
    public async Task<ActionResult<PlanningDto>> Get(CancellationToken ct = default)
    {
        // Every line on an open order, with the order's header. Both are views
        // (vwDispatchOrderItem carries the line's picked count).
        var lines = await (
            from i in _db.Set<DispatchOrderItemRow>().AsNoTracking()
            join o in _db.Set<DispatchOrderRow>().AsNoTracking() on i.OrderId equals o.Id
            where o.DateDeleted == null && o.DateDispatched == null && i.Quantity > 0
            select new
            {
                i.ProductId,
                OrderId = o.Id,
                o.OrderNo,
                o.CustomerName,
                o.DeliveryDate,
                o.DateCreated,
                Quantity = i.Quantity ?? 0,
                Picked = i.PickedQuantity ?? 0,
            })
            .ToListAsync(ct);

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking()
            // Cubic meter products carry no stock, so there is nothing to plan for them.
            .Where(p => productIds.Contains(p.Id) && p.SoldBy != ProductSoldBy.CubicMeter)
            .ToListAsync(ct);

        // In-stock pallets per product — the same Code13 -> Product.Gtin13 match the
        // Current Stock summary uses, so the two screens agree.
        var codes = products.Where(p => p.Gtin13 != null).Select(p => p.Gtin13!).Distinct().ToList();
        var stock = await _db.Set<CodicoPalletEntity>().AsNoTracking()
            .Where(p => p.DateOutOfStock == null && p.Code13 != null && codes.Contains(p.Code13))
            .GroupBy(p => p.Code13!)
            .Select(g => new { Code13 = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Code13, g => g.Count, ct);

        var rows = products
            .Select(p => new PlanningProductDto
            {
                ProductId = p.Id,
                Description = p.Description,
                Gtin = p.Gtin,
                Size = p.Size,
                Upp = p.Upp,
                InStock = p.Gtin13 != null ? stock.GetValueOrDefault(p.Gtin13) : 0,
                Lines = lines
                    .Where(l => l.ProductId == p.Id)
                    // ASAP (no date) first, then by delivery date — the order the stock is
                    // consumed in.
                    .OrderBy(l => l.DeliveryDate.HasValue)
                    .ThenBy(l => l.DeliveryDate)
                    .ThenBy(l => l.OrderNo)
                    .Select(l => new PlanningLineDto
                    {
                        OrderId = l.OrderId,
                        OrderNo = l.OrderNo,
                        CustomerName = l.CustomerName,
                        DeliveryDate = l.DeliveryDate,
                        DateCreated = l.DateCreated,
                        Quantity = l.Quantity,
                        Picked = l.Picked,
                    })
                    .ToList(),
            })
            .OrderBy(p => p.Description)
            .ThenBy(p => p.Size)
            .ToList();

        return new PlanningDto { Products = rows };
    }
}

public class PlanningDto
{
    public List<PlanningProductDto> Products { get; set; } = new();
}

public class PlanningProductDto
{
    public int ProductId { get; set; }
    public string? Description { get; set; }
    public string? Gtin { get; set; }
    public int? Size { get; set; }
    public int Upp { get; set; }
    public int InStock { get; set; }
    public List<PlanningLineDto> Lines { get; set; } = new();
}

public class PlanningLineDto
{
    public int OrderId { get; set; }
    public int OrderNo { get; set; }
    public string? CustomerName { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public DateTime? DateCreated { get; set; }
    public int Quantity { get; set; }
    public int Picked { get; set; }
}
