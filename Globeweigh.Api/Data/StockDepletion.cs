using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Data;

// Dispatch-time stock depletion. Stock is counts-only: which physical pallets left
// doesn't matter — dispatching an order marks the OLDEST in-stock pallets of each
// product out of stock (method "Dispatch"), one per dispatched pallet, and writes the
// StockHistory audit rows. Make Current Stock remains the periodic correction.
public static class StockDepletion
{
    // Call at weigh-out for each order being dispatched (both the Touch/Picking
    // complete-load path and the admin weigh-out path). Adds StockHistory rows to the
    // context (caller saves) and runs the pallet updates immediately.
    public static async Task DispatchOrderAsync(
        GbEvergreenContext db, int orderId, DateTime now, CancellationToken ct)
    {
        var items = await (
            from i in db.Set<DispatchOrderItemEntity>().AsNoTracking()
            where i.OrderId == orderId
            join pr in db.Set<Product>().AsNoTracking() on i.ProductId equals pr.Id into pj
            from pr in pj.DefaultIfEmpty()
            select new
            {
                i.ProductId,
                Ordered = i.Quantity ?? 0,
                Gtin13 = pr != null ? pr.Gtin13 : null,
                SoldBy = pr != null ? pr.SoldBy : null,
            }).ToListAsync(ct);
        if (items.Count == 0) return;

        // What actually left: picked pallets per product; ordered quantity as the
        // fallback when a line was dispatched without picking.
        // Deliberately keyed off the ORDER LINES: a pick with no surviving line was
        // orphaned by an order edit (the line was removed after picking) and never
        // shipped — depleting it here would write off pallets still sitting in the yard.
        var picked = await db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            .Where(p => p.DispatchOrderId == orderId && p.DateDeleted == null)
            .GroupBy(p => p.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ProductId, g => g.Count, ct);

        foreach (var it in items)
        {
            // Sold by the cubic meter (raw peat): never stocked, so nothing to deplete or record.
            if (it.SoldBy == ProductSoldBy.CubicMeter) continue;

            // Picked is the truth (what physically left). A line with no picks depletes
            // nothing; the ordered quantity is only used when the WHOLE order was
            // dispatched without picking (manual/legacy path).
            var qty = picked.Count > 0 ? picked.GetValueOrDefault(it.ProductId) : it.Ordered;
            db.Add(new StockHistory
            {
                EntityId = orderId,
                ProductId = it.ProductId,
                TransactionType = "Dispatch",
                Quantity = qty,
                DateCreated = now,
            });

            // Deplete oldest-first (FIFO) among the product's in-stock pallets.
            if (qty > 0 && it.Gtin13 != null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE cp SET DateOutOfStock = {now}, OutOfStockMethod = 'Dispatch'
FROM dbo.CodicoPallet cp
JOIN (SELECT id, ROW_NUMBER() OVER (ORDER BY DateCreated ASC, id ASC) AS rn
      FROM dbo.CodicoPallet
      WHERE Code13 = {it.Gtin13} AND DateOutOfStock IS NULL) r ON r.id = cp.id
WHERE r.rn <= {qty}", ct);
            }
        }
    }
}
