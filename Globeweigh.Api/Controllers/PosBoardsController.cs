using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// POS Boards, the mobile app's screen for whoever holds the "POS creator" tag (StaffTags.PosCreator,
// set in Admin > Staff > Edit > Tags). They see the OPEN orders that carry a POS board
// (DispatchOrder.HasPos) and, order by order, tick the products they have a board made up for
// (DispatchOrderPosMade: one row per order + product). An order leaves the list once every
// product is ticked, or when they press Complete (DispatchOrderPosComplete): not every product
// gets a board. Lines of the same product on one order count as one board.
[ApiController]
[Authorize]
[Route("api/mobile/pos-boards")]
public class PosBoardsController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public PosBoardsController(GbEvergreenContext db) => _db = db;

    // GET api/mobile/pos-boards — the orders still owed a board, soonest delivery first.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PosOrderDto>>> List(CancellationToken ct = default)
    {
        if (!await this.HasTagAsync(_db, StaffTags.PosCreator, ct)) return Forbid();
        var orders = await LoadAsync(null, ct);
        return orders
            .Where(o => !o.Completed && o.Made < o.Total)
            .OrderBy(o => o.DeliveryDate ?? DateTime.MaxValue).ThenBy(o => o.OrderNo)
            .ToList();
    }

    // POST api/mobile/pos-boards/{orderId}/made — POS made up for these products of the order.
    // Returns the order as it now stands (also once complete, so the app can say so).
    [HttpPost("{orderId:int}/made")]
    public async Task<ActionResult<PosOrderDto>> Made(
        int orderId, [FromBody] PosMadeInput input, CancellationToken ct = default)
    {
        if (!await this.HasTagAsync(_db, StaffTags.PosCreator, ct)) return Forbid();
        var order = (await LoadAsync(orderId, ct)).FirstOrDefault();
        if (order == null) return NotFound(new { message = "That order is no longer open." });

        if (AddMade(order, input) > 0)
        {
            await _db.SaveChangesAsync(ct);
            order = (await LoadAsync(orderId, ct)).First();
        }
        return order;
    }

    // POST api/mobile/pos-boards/{orderId}/complete — finish the order as it stands: not every
    // product gets a board, so this takes it off the list with whatever is ticked. Any products
    // sent (ticked on screen but not yet saved) are marked made up first.
    [HttpPost("{orderId:int}/complete")]
    public async Task<ActionResult<PosOrderDto>> Complete(
        int orderId, [FromBody] PosMadeInput input, CancellationToken ct = default)
    {
        if (!await this.HasTagAsync(_db, StaffTags.PosCreator, ct)) return Forbid();
        var order = (await LoadAsync(orderId, ct)).FirstOrDefault();
        if (order == null) return NotFound(new { message = "That order is no longer open." });

        AddMade(order, input);
        if (!order.Completed)
            _db.Add(new DispatchOrderPosCompleteEntity
            {
                OrderId = orderId, CompletedById = this.GetUserId(), DateCompleted = DateTime.Now,
            });
        await _db.SaveChangesAsync(ct);
        return (await LoadAsync(orderId, ct)).First();
    }

    // Queues a made-up row for each product asked for that is on the order and not yet made.
    private int AddMade(PosOrderDto order, PosMadeInput? input)
    {
        var wanted = (input?.ProductIds ?? Array.Empty<int>()).Distinct().ToList();
        var owed = order.Lines.Where(l => l.MadeAt == null).Select(l => l.ProductId).ToHashSet();
        var add = wanted.Where(owed.Contains).ToList();
        var me = this.GetUserId();
        foreach (var productId in add)
            _db.Add(new DispatchOrderPosMadeEntity
            {
                OrderId = order.Id, ProductId = productId, MadeById = me, DateMade = DateTime.Now,
            });
        return add.Count;
    }

    // Open orders with a POS board (one, or all), each with its products and which are made up.
    private async Task<List<PosOrderDto>> LoadAsync(int? onlyId, CancellationToken ct)
    {
        var orders = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => o.HasPos && o.DateDeleted == null && o.DateDispatched == null
                && (onlyId == null || o.Id == onlyId))
            .Select(o => new { o.Id, o.OrderNo, o.DeliveryDate, o.WeighbridgeRecordId })
            .ToListAsync(ct);
        if (orders.Count == 0) return new();
        var ids = orders.Select(o => o.Id).ToList();

        // The load each order is on, if any: still in Loads (not weighed in) or on Pending
        // (weighed in: the lorry is on site being loaded, so its boards are wanted now).
        var loadIds = orders.Select(o => o.WeighbridgeRecordId).Where(l => l > 0).Distinct().ToList();
        var loads = await _db.Set<WeighbridgeRecord>().AsNoTracking()
            .Where(w => loadIds.Contains(w.Id) && w.DateDeleted == null)
            .Select(w => new { w.Id, w.DateWeightIn, w.CollectionDate, w.HaulierName })
            .ToDictionaryAsync(w => w.Id, ct);

        var customers = await _db.Set<DispatchOrderRow>().AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.CustomerName })
            .ToDictionaryAsync(r => r.Id, r => r.CustomerName, ct);

        var items = await (
            from i in _db.Set<DispatchOrderItemEntity>().AsNoTracking()
            join p in _db.Set<Product>().AsNoTracking() on i.ProductId equals p.Id
            where ids.Contains(i.OrderId)
            orderby i.Id
            select new { i.OrderId, i.ProductId, i.Quantity, p.Description, p.Size, p.Upp, p.SoldBy }
        ).ToListAsync(ct);

        var made = await _db.Set<DispatchOrderPosMadeEntity>().AsNoTracking()
            .Where(m => ids.Contains(m.OrderId))
            .ToListAsync(ct);
        var completed = (await _db.Set<DispatchOrderPosCompleteEntity>().AsNoTracking()
                .Where(c => ids.Contains(c.OrderId))
                .Select(c => c.OrderId)
                .ToListAsync(ct))
            .ToHashSet();
        var makerIds = made.Select(m => m.MadeById).Distinct().ToList();
        var makers = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => makerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.Surname })
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.Surname}".Trim(), ct);

        return orders.Select(o =>
        {
            var lines = items.Where(i => i.OrderId == o.Id)
                .GroupBy(i => i.ProductId)
                .Select(g =>
                {
                    var first = g.First();
                    var m = made.FirstOrDefault(x => x.OrderId == o.Id && x.ProductId == g.Key);
                    return new PosLineDto
                    {
                        ProductId = g.Key,
                        Name = first.Description ?? $"#{g.Key}",
                        Size = first.Size,
                        Upp = first.Upp,
                        SoldBy = first.SoldBy,
                        Quantity = g.Sum(x => x.Quantity ?? 0),
                        MadeAt = m?.DateMade,
                        MadeBy = m != null && makers.TryGetValue(m.MadeById, out var who) ? who : null,
                    };
                })
                .ToList();
            loads.TryGetValue(o.WeighbridgeRecordId, out var load);
            return new PosOrderDto
            {
                LoadState = load == null ? null : load.DateWeightIn != null ? "Pending" : "Load",
                LoadDate = load?.CollectionDate,
                LoadHaulier = load?.HaulierName,
                Id = o.Id,
                OrderNo = o.OrderNo,
                CustomerName = customers.TryGetValue(o.Id, out var c) ? c : null,
                DeliveryDate = o.DeliveryDate,
                Total = lines.Count,
                Made = lines.Count(l => l.MadeAt != null),
                Completed = completed.Contains(o.Id),
                Lines = lines,
            };
        }).ToList();
    }
}

public class PosOrderDto
{
    public int Id { get; set; }
    public int OrderNo { get; set; }
    public string? CustomerName { get; set; }
    public DateTime? DeliveryDate { get; set; }
    // null = not on a load; "Load" = on a load not yet weighed in; "Pending" = on a load that is
    // weighed in (on site, being loaded).
    public string? LoadState { get; set; }
    public DateTime? LoadDate { get; set; }
    public string? LoadHaulier { get; set; }
    public int Total { get; set; } // products on the order = boards wanted
    public int Made { get; set; }
    // Finished by hand (Complete), whatever was ticked: off the list.
    public bool Completed { get; set; }
    public List<PosLineDto> Lines { get; set; } = new();
}

public class PosLineDto
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public int? Size { get; set; }
    public int Upp { get; set; }
    public string? SoldBy { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? MadeAt { get; set; }
    public string? MadeBy { get; set; }
}

public class PosMadeInput
{
    public int[]? ProductIds { get; set; }
}
