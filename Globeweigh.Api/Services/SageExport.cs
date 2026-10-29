using System.Text.Json;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

// Builds and queues the sales-order exports that Globeweigh.SageSidecar writes into Sage 50
// (Data/Entities/SageExportJob.cs has the why). Static like StockDepletion: callers hand in
// their context and do the SaveChanges, so a queued export commits with — or rolls back
// with — whatever dispatched the order.
public static class SageExport
{
    // Sage:ExportEnabled — the master switch for AUTOMATIC exports on load completion. Off
    // by default: the queue must not start filling until the sidecar is installed on the
    // Sage machine and someone turns this on in the server's appsettings.json. Manual
    // exports from Admin (SageController.ExportOrder) ignore it — they are how the mapping
    // gets tested before go-live.
    public static bool AutoExportEnabled(IConfiguration config) =>
        config.GetValue<bool>("Sage:ExportEnabled");

    public static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    // ---- The contract with the sidecar -------------------------------------------------
    // Serialised camelCase into SageExportJob.Payload. Globeweigh.SageSidecar\SagePayload.cs
    // mirrors these two classes: change both together.

    public class SalesOrderPayload
    {
        public int OrderId { get; set; }
        public int OrderNo { get; set; }
        // "EVG-<order no>". Stamped into Sage (header ANALYSIS_1, item EXT_ORDER_REF) so an
        // order can be traced back here, and so the sidecar can recognise one it already
        // posted if the acknowledgement never made it back.
        public string Reference { get; set; } = "";
        public DateTime OrderDate { get; set; }          // when it was dispatched
        public DateTime? CreatedAt { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string? PoNumber { get; set; }
        public string? AccountRef { get; set; }          // CustomerSuppliers.Code = Sage A/C ref
        public string? CustomerName { get; set; }
        public string? ContactName { get; set; }
        public string? Telephone { get; set; }
        public string? DeliveryName { get; set; }
        public List<string> DeliveryAddress { get; set; } = [];
        public string? Comments { get; set; }
        public string? HaulierName { get; set; }
        public string? VehicleReg { get; set; }
        public string? TrailerNo { get; set; }
        public List<SalesOrderLine> Lines { get; set; } = [];
    }

    public class SalesOrderLine
    {
        public int ProductId { get; set; }
        public string? StockCode { get; set; }           // Product.SkuCode → Sage STOCK_CODE
        public string? Gtin { get; set; }
        public string? Description { get; set; }
        public int? Size { get; set; }
        public int Upp { get; set; }                     // bags per pallet
        public bool IsBulkBag { get; set; }
        public int PalletsOrdered { get; set; }
        public int PalletsPicked { get; set; }
        // What actually left, by the rule StockDepletion uses: picked pallets when the order
        // was picked, the ordered quantity when the whole order went out without picking.
        public int Pallets { get; set; }
        public int Units { get; set; }                   // Pallets × Upp
        public decimal WeightKg { get; set; }
        // Per UNIT (bag) — the trade price, e.g. £3.28 for a 60L compost — so it pairs with
        // Units, not Pallets (the sidecar's QuantityBasis). The order line's own price when
        // one was entered, else the customer's costing-zone price for the product; null when
        // neither exists (the sidecar posts the line at 0.00 and the price is completed in Sage).
        public decimal? UnitPrice { get; set; }
        public string? PriceSource { get; set; }         // "Order" | "CostingZone" | null
    }

    // The payload for an order, or null when there is no such (undeleted) order.
    // dispatchedAt lets a caller that has just set DateDispatched in memory pass it in
    // before it is saved.
    public static async Task<SalesOrderPayload?> BuildPayloadAsync(
        GbEvergreenContext db, int orderId, DateTime? dispatchedAt, CancellationToken ct)
    {
        var o = await db.Set<DispatchOrderEntity>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == orderId && x.DateDeleted == null, ct);
        if (o == null) return null;

        var cust = await db.Set<CustomerSupplier>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == o.CustomerId, ct);
        // The order's chosen address, else the customer's first — as the delivery note does.
        var addr = await db.Set<CustomerSupplierAddress>().AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == o.AddressId, ct)
            ?? await db.Set<CustomerSupplierAddress>().AsNoTracking()
                .Where(a => a.CustomerSupplierId == o.CustomerId)
                .OrderBy(a => a.Id)
                .FirstOrDefaultAsync(ct);

        // Haulier and vehicle usually live on the load, not the order (as BuildDeliveryNote).
        var haulier = await db.Set<Haulier>().AsNoTracking()
            .Where(h => h.Id == o.HaulierId).Select(h => h.Name).FirstOrDefaultAsync(ct);
        var vehicleReg = o.VehicleReg;
        var trailerNo = o.TrailerNo;
        if ((string.IsNullOrWhiteSpace(haulier) || string.IsNullOrWhiteSpace(trailerNo)) && o.WeighbridgeRecordId > 0)
        {
            var rec = await db.Set<WeighbridgeRecord>().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == o.WeighbridgeRecordId, ct);
            if (rec != null)
            {
                if (string.IsNullOrWhiteSpace(haulier))
                    haulier = await db.Set<Haulier>().AsNoTracking()
                            .Where(h => h.Id == rec.HaulierId).Select(h => h.Name).FirstOrDefaultAsync(ct)
                        ?? rec.HaulierName;
                if (string.IsNullOrWhiteSpace(vehicleReg)) vehicleReg = rec.VehicleReg;
                if (string.IsNullOrWhiteSpace(trailerNo)) trailerNo = rec.TrailerNo;
            }
        }

        // Lines: the view for what was picked (the same figures the delivery note prints),
        // the table for the entered price, Product for the Sage stock code.
        var rows = await db.Set<DispatchOrderItemRow>().AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);
        var prices = await db.Set<DispatchOrderItemEntity>().AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .ToDictionaryAsync(i => i.Id, i => i.Price, ct);
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        // Order lines are normally entered without a price (the UI sends null); the price
        // list is the customer's costing zone.
        var zonePrices = new Dictionary<int, decimal>();
        if (cust != null && cust.CostingZoneId > 0)
        {
            var zone = await db.Set<CostingZonePriceEntity>().AsNoTracking()
                .Where(z => z.CostingZoneId == cust.CostingZoneId && !z.NA && productIds.Contains(z.ProductId))
                .ToListAsync(ct);
            foreach (var z in zone) zonePrices.TryAdd(z.ProductId, z.Price);
        }

        var anyPicked = rows.Any(r => (r.PickedQuantity ?? 0) > 0);
        var lines = new List<SalesOrderLine>();
        foreach (var r in rows)
        {
            products.TryGetValue(r.ProductId, out var product);
            var ordered = r.Quantity ?? 0;
            var picked = r.PickedQuantity ?? 0;
            var pallets = anyPicked ? picked : ordered;

            decimal? price = prices.TryGetValue(r.Id, out var linePrice) ? linePrice : null;
            string? priceSource = price != null ? "Order" : null;
            if (price == null && zonePrices.TryGetValue(r.ProductId, out var zonePrice))
            {
                price = zonePrice;
                priceSource = "CostingZone";
            }

            lines.Add(new SalesOrderLine
            {
                ProductId = r.ProductId,
                StockCode = product?.SkuCode?.Trim(),
                Gtin = r.Gtin ?? product?.Gtin,
                Description = r.ProductDescription ?? product?.Description,
                Size = r.Size,
                Upp = r.Upp,
                IsBulkBag = product?.IsBulkBag ?? false,
                PalletsOrdered = ordered,
                PalletsPicked = picked,
                Pallets = pallets,
                Units = pallets * r.Upp,
                WeightKg = r.PickedWeight,
                UnitPrice = price,
                PriceSource = priceSource,
            });
        }

        var addressLines = new[]
            { addr?.Address1, addr?.Address2, addr?.Address3, addr?.Address4, addr?.Address5 }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!.Trim())
            .ToList();
        // No address record any more (deleted since)? The order kept a flat copy.
        if (addressLines.Count == 0 && !string.IsNullOrWhiteSpace(o.AddressFull))
            addressLines = o.AddressFull
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

        return new SalesOrderPayload
        {
            OrderId = o.Id,
            OrderNo = o.OrderNo,
            Reference = $"EVG-{o.OrderNo}",
            OrderDate = dispatchedAt ?? o.DateDispatched ?? o.DateCreated ?? DateTime.Now,
            CreatedAt = o.DateCreated,
            DeliveryDate = o.DeliveryDate,
            PoNumber = o.PoNumber,
            AccountRef = cust?.Code?.Trim(),
            CustomerName = cust?.Name,
            ContactName = addr?.ContactName,
            Telephone = addr?.Telephone,
            DeliveryName = string.IsNullOrWhiteSpace(addr?.AddressName) ? cust?.Name : addr!.AddressName,
            DeliveryAddress = addressLines,
            Comments = o.Comments,
            HaulierName = haulier,
            VehicleReg = vehicleReg,
            TrailerNo = trailerNo,
            Lines = lines,
        };
    }

    // Queue an export for each order that does not already have a live (non-Failed) job.
    // Adds rows to the context — the CALLER saves — and returns the order ids actually
    // queued. Orders that have since been deleted are skipped.
    public static async Task<List<int>> EnqueueAsync(
        GbEvergreenContext db, IEnumerable<int> orderIds, string reason, DateTime? dispatchedAt,
        CancellationToken ct)
    {
        var ids = orderIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        // Belt to the filtered unique index's braces (UX_SageExportJob_LiveOrder): skip
        // orders already queued or done, so a re-completed load doesn't fail on the index.
        var live = await db.Set<SageExportJob>().AsNoTracking()
            .Where(j => j.DocType == SageExportJob.SalesOrder
                && ids.Contains(j.DispatchOrderId)
                && j.Status != SageExportJob.Failed)
            .Select(j => j.DispatchOrderId)
            .ToListAsync(ct);

        var createdUtc = DateTime.UtcNow;
        var queued = new List<int>();
        foreach (var orderId in ids.Except(live))
        {
            var payload = await BuildPayloadAsync(db, orderId, dispatchedAt, ct);
            if (payload == null) continue;

            db.Set<SageExportJob>().Add(new SageExportJob
            {
                DocType = SageExportJob.SalesOrder,
                DispatchOrderId = orderId,
                OrderNo = payload.OrderNo,
                Payload = JsonSerializer.Serialize(payload, PayloadJson),
                Status = SageExportJob.Queued,
                Reason = reason,
                CreatedUtc = createdUtc,
            });
            queued.Add(orderId);
        }
        return queued;
    }
}
