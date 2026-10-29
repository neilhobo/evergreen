using System.Net;
using System.Text;
using System.Text.Json;
using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dispatch")]
public class DispatchController : ControllerBase
{
    private readonly GbEvergreenContext _db;
    private readonly IConfiguration _config;

    public DispatchController(GbEvergreenContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // GET /api/dispatch/orders — open (not dispatched, not deleted) orders, each with the
    // latest order confirmation emailed for it (the grid's Emailed column).
    [HttpGet("orders")]
    public async Task<ActionResult<IEnumerable<DispatchOrderRow>>> Orders(
        CancellationToken ct = default)
    {
        await ReleaseOrphanedOrders(ct);
        var rows = await _db.Set<DispatchOrderRow>().AsNoTracking()
            .Where(o => o.DateDeleted == null && o.DateDispatched == null)
            .OrderBy(o => o.DeliveryDate)
            .ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var latest = (await _db.Set<DispatchOrderEmail>().AsNoTracking()
                .Where(e => ids.Contains(e.DispatchOrderId) && e.Kind == DispatchOrderEmail.KindConfirmation)
                .OrderByDescending(e => e.DateSent).ThenByDescending(e => e.Id)
                .ToListAsync(ct))
            .GroupBy(e => e.DispatchOrderId)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var r in rows)
            if (latest.TryGetValue(r.Id, out var e))
            {
                r.ConfirmationSentAt = e.DateSent;
                r.ConfirmationSentTo = e.SentTo;
            }
        return rows;
    }

    // GET /api/dispatch/completed?from=&to= — dispatched (not deleted) orders.
    // ?mine=true: only the orders whose Sales Rep is the caller (My Orders > Completed). A
    // Sales-role caller always gets just their own, whatever they ask for.
    [HttpGet("completed")]
    public async Task<ActionResult<IEnumerable<DispatchOrderRow>>> Completed(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool mine = false,
        CancellationToken ct = default)
    {
        var q = _db.Set<DispatchOrderRow>().AsNoTracking()
            .Where(o => o.DateDeleted == null && o.DateDispatched != null);
        if (mine || await IsSalesUser(ct))
        {
            var me = this.GetUserId();
            var myIds = _db.Set<DispatchOrderEntity>().Where(d => d.SalesRepId == me).Select(d => d.Id);
            q = q.Where(o => myIds.Contains(o.Id));
        }
        if (from != null) q = q.Where(o => o.DateDispatched >= from);
        if (to != null) q = q.Where(o => o.DateDispatched < to.Value.AddDays(1));
        var rows = await q.OrderByDescending(o => o.DateDispatched).ToListAsync(ct);

        // Orders usually carry no haulier/vehicle of their own — those details are
        // captured on the load (WeighbridgeRecord) they were dispatched on, so
        // backfill blanks from there for display.
        var wbIds = rows
            .Where(r => r.WeighbridgeRecordId > 0 &&
                (string.IsNullOrWhiteSpace(r.HaulierName) || string.IsNullOrWhiteSpace(r.VehicleReg)))
            .Select(r => r.WeighbridgeRecordId).Distinct().ToList();
        if (wbIds.Count > 0)
        {
            var recs = await _db.Set<WeighbridgeRecord>().AsNoTracking()
                .Where(w => wbIds.Contains(w.Id))
                .Select(w => new { w.Id, w.HaulierId, w.HaulierName, w.VehicleReg, w.TrailerNo })
                .ToListAsync(ct);
            var haulierIds = recs.Select(r => r.HaulierId).Where(h => h > 0).Distinct().ToList();
            var haulierNames = await _db.Set<Haulier>().AsNoTracking()
                .Where(h => haulierIds.Contains(h.Id))
                .ToDictionaryAsync(h => h.Id, h => h.Name, ct);
            var byId = recs.ToDictionary(r => r.Id);
            foreach (var r in rows)
            {
                if (!byId.TryGetValue(r.WeighbridgeRecordId, out var w)) continue;
                if (string.IsNullOrWhiteSpace(r.HaulierName))
                    r.HaulierName = (w.HaulierId > 0 && haulierNames.TryGetValue(w.HaulierId, out var n) ? n : null)
                        ?? w.HaulierName;
                if (string.IsNullOrWhiteSpace(r.VehicleReg))
                    r.VehicleReg = !string.IsNullOrWhiteSpace(w.VehicleReg) ? w.VehicleReg : w.TrailerNo;
            }
        }
        return rows;
    }

    // GET /api/dispatch/orders/{id}/items — line items for an order.
    [HttpGet("orders/{id:int}/items")]
    public async Task<ActionResult<IEnumerable<DispatchOrderItemRow>>> Items(
        int id,
        CancellationToken ct = default)
    {
        var rows = await _db.Set<DispatchOrderItemRow>().AsNoTracking()
            .Where(i => i.OrderId == id)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);
        rows.AddRange(await PosRowsAsync(o => o.Id == id, ct));
        return rows;
    }

    // An order's POS board (DispatchOrder.HasPos) is not an order line, but the order and load
    // panels list it as one so the office can see whether the picker picked it (Picking app:
    // tapped, recorded as DatePosPicked). Synthetic rows: negative id, ProductId 0, SoldBy "Pos",
    // Quantity 1, PickedQuantity 1 once picked.
    private async Task<List<DispatchOrderItemRow>> PosRowsAsync(
        System.Linq.Expressions.Expression<Func<DispatchOrderEntity, bool>> which, CancellationToken ct) =>
        (await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => o.DateDeleted == null && o.HasPos)
            .Where(which)
            .Select(o => new { o.Id, o.OrderNo, o.WeighbridgeRecordId, o.DatePosPicked })
            .ToListAsync(ct))
        .Select(o => new DispatchOrderItemRow
        {
            Id = -1_000_000 - o.Id,
            OrderId = o.Id,
            ProductId = 0,
            ProductDescription = "POS Board",
            Quantity = 1,
            PickedQuantity = o.DatePosPicked != null ? 1 : 0,
            OrderNo = o.OrderNo,
            WeighbridgeRecordId = o.WeighbridgeRecordId,
            SoldBy = "Pos",
            PickedAt = o.DatePosPicked,
        })
        .ToList();

    // GET /api/dispatch/orders/{id}/picked — the individual pallets picked onto this
    // order, newest first: the per-pallet detail behind the item grid's Picked totals.
    // Same shape as the picked array on loads/{id}, so the two grids can share columns.
    [HttpGet("orders/{id:int}/picked")]
    public async Task<ActionResult> OrderPicked(int id, CancellationToken ct = default)
    {
        var picked = await (
            from p in _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            join o in _db.Set<DispatchOrderEntity>().AsNoTracking() on p.DispatchOrderId equals o.Id
            join pr in _db.Set<Product>().AsNoTracking() on p.ProductId equals pr.Id into prs
            from pr in prs.DefaultIfEmpty()
            where p.DateDeleted == null && p.DispatchOrderId == id
            // Grouped by Evergreen code, then in the order they were scanned — the
            // product's pallets read as one block, in picking sequence.
            orderby pr.Gtin, p.DateCreated, p.Id
            select new
            {
                p.Id,
                OrderId = p.DispatchOrderId,
                o.OrderNo,
                p.ProductId,
                ProductDescription = pr != null ? pr.Description : null,
                Gtin = pr != null ? pr.Gtin : null,
                Size = pr != null ? pr.Size : null,
                Upp = pr != null ? (int?)pr.Upp : null,
                p.PalletId,
                p.Weight,
                p.DateCreated,
                p.IsNoScan,
                p.IsWeightEstimated,
            }
        ).ToListAsync(ct);
        return Ok(picked);
    }

    // GET /api/dispatch/orders/{id}/edit-items — raw line items (incl. Price) plus the
    // header fields the grid rows omit (Comments), for prefilling the edit form.
    [HttpGet("orders/{id:int}/edit-items")]
    public async Task<ActionResult<object>> EditItems(int id, CancellationToken ct = default)
    {
        var order = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order == null) return NotFound();
        var rows = await _db.Set<DispatchOrderItemEntity>().AsNoTracking()
            .Where(i => i.OrderId == id)
            .Select(i => new { i.ProductId, i.Quantity, i.Price })
            .ToListAsync(ct);
        return Ok(new
        {
            orderNo = order.OrderNo,
            orderNoEditable = await IsOrderNoEditable(order, ct),
            comments = order.Comments,
            pickingComments = order.PickingComments,
            hasPos = order.HasPos,
            addressId = order.AddressId,
            salesRepId = order.SalesRepId,
            salesRepName = order.SalesRepId is { } rep
                ? await _db.GlobeweighUsers.AsNoTracking().Where(u => u.Id == rep)
                    .Select(u => ((u.FirstName ?? "") + " " + (u.Surname ?? "")).Trim()).FirstOrDefaultAsync(ct)
                : null,
            // Approved from My Orders: the rep who sent it, shown but not changeable.
            salesRepFixed = await IsFromSales(id, ct),
            items = rows,
        });
    }

    // A Sales-role user (eUserRole 6): sees only the orders credited to them.
    private async Task<bool> IsSalesUser(CancellationToken ct)
    {
        var me = this.GetUserId();
        return await _db.Set<UserRoleEntity>().AsNoTracking().AnyAsync(r => r.UserId == me && r.RoleId == 6, ct)
            || await _db.GlobeweighUsers.AsNoTracking().AnyAsync(u => u.Id == me && u.RoleId == 6, ct);
    }

    // An order a sales rep sent from My Orders and the office approved: its rep is fixed.
    private Task<bool> IsFromSales(int orderId, CancellationToken ct) =>
        _db.Set<SalesOrderEntity>().AsNoTracking().AnyAsync(s => s.DispatchOrderId == orderId, ct);

    // The order number can be corrected only while the order is still OPEN (not dispatched,
    // not deleted) AND predates the switch to the specified order-number series - i.e. it is
    // one of the ad-hoc numbers keyed during the side-by-side period, not one the system
    // issued from the continued legacy series. Until the switch has ever been flipped on
    // (NextOrderNoEnabledDate is null) every open order counts as pre-hand-over.
    private async Task<bool> IsOrderNoEditable(DispatchOrderEntity order, CancellationToken ct)
    {
        if (order.DateDispatched != null || order.DateDeleted != null) return false;
        var flip = await _db.Settings.AsNoTracking()
            .Select(s => s.NextOrderNoEnabledDate).FirstOrDefaultAsync(ct);
        return flip == null || (order.DateCreated ?? DateTime.MinValue) < flip;
    }

    // GET /api/dispatch/orders/{id}/delivery-note — everything the printed Evergreen
    // delivery note shows: customer + address, delivery instructions, haulier, and the
    // order lines with pallet/bag quantities. The note number is the order number.
    [HttpGet("orders/{id:int}/delivery-note")]
    public async Task<ActionResult<DeliveryNoteDto>> DeliveryNote(int id, CancellationToken ct = default)
        => await BuildDeliveryNote(id, ct) is { } dto ? dto : NotFound();

    // The note's data, shared by the JSON endpoint above and the printable page below, so
    // an auto-printed docket can never carry different figures from the one a user
    // previews in Completed Orders. Null = no such (undeleted) order.
    //
    // confirmation: the ORDER CONFIRMATION instead — the same note, titled Order Confirmation,
    // with the lines as ORDERED (it goes to the customer from Open Orders, before anything is
    // picked) and dated the day the order was taken.
    private async Task<DeliveryNoteDto?> BuildDeliveryNote(int id, CancellationToken ct, bool confirmation = false)
    {
        var o = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (o == null) return null;

        var cust = await _db.Set<CustomerSupplier>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == o.CustomerId, ct);
        // Prefer the order's chosen address; fall back to the customer's first address.
        var addr = await _db.Set<CustomerSupplierAddress>().AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == o.AddressId, ct)
            ?? await _db.Set<CustomerSupplierAddress>().AsNoTracking()
                .Where(a => a.CustomerSupplierId == o.CustomerId)
                .OrderBy(a => a.Id)
                .FirstOrDefaultAsync(ct);
        var haulier = await _db.Set<Haulier>().AsNoTracking()
            .Where(h => h.Id == o.HaulierId).Select(h => h.Name).FirstOrDefaultAsync(ct);
        // Orders usually carry no haulier/vehicle of their own — those details are
        // captured on the load (WeighbridgeRecord) they were dispatched on.
        var vehicleReg = o.VehicleReg;
        var trailerNo = o.TrailerNo;
        if ((string.IsNullOrWhiteSpace(haulier) || string.IsNullOrWhiteSpace(trailerNo)) && o.WeighbridgeRecordId > 0)
        {
            var rec = await _db.Set<WeighbridgeRecord>().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == o.WeighbridgeRecordId, ct);
            if (rec != null)
            {
                if (string.IsNullOrWhiteSpace(haulier))
                    haulier = await _db.Set<Haulier>().AsNoTracking()
                            .Where(h => h.Id == rec.HaulierId).Select(h => h.Name).FirstOrDefaultAsync(ct)
                        ?? rec.HaulierName;
                if (string.IsNullOrWhiteSpace(vehicleReg)) vehicleReg = rec.VehicleReg;
                if (string.IsNullOrWhiteSpace(trailerNo)) trailerNo = rec.TrailerNo;
            }
        }

        // The note shows what was actually PICKED, not what was ordered (the confirmation: ordered).
        var items = await _db.Set<DispatchOrderItemRow>().AsNoTracking()
            .Where(i => i.OrderId == id)
            .OrderBy(i => i.Id)
            .Select(i => new DeliveryNoteItemDto
            {
                Description = i.ProductDescription,
                Size = i.Size,
                Upp = i.Upp,
                Pallets = (confirmation ? i.Quantity : i.PickedQuantity) ?? 0,
                TotalBags = ((confirmation ? i.Quantity : i.PickedQuantity) ?? 0) * i.Upp,
                // Raw peat is never picked: its line goes out as ordered, in m³.
                SoldBy = i.SoldBy,
                CubicMeters = i.SoldBy == ProductSoldBy.CubicMeter ? i.Quantity ?? 0 : 0,
            }).ToListAsync(ct);

        var addressLines = new[]
            { addr?.Address1, addr?.Address2, addr?.Address3, addr?.Address4, addr?.Address5 }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!.Trim())
            .ToList();

        return new DeliveryNoteDto
        {
            Template = await NoteTemplate(ct),
            Confirmation = confirmation,
            OrderId = o.Id,
            NoteNo = o.OrderNo,
            Date = confirmation ? o.DateCreated : o.DateDispatched ?? o.DateCreated,
            PoNumber = o.PoNumber,
            AccountRef = cust?.Code,
            CustomerName = cust?.Name,
            AddressLines = addressLines,
            AddressFull = o.AddressFull,
            Telephone = addr?.Telephone,
            Comments = o.Comments,
            HaulierName = haulier,
            VehicleReg = vehicleReg,
            TrailerNo = trailerNo,
            Items = items,
        };
    }

    // The delivery-note design chosen on Admin > Delivery Note (the commercial invoice follows
    // it too): two FeatureFlag rows
    // (TauriAdmin api/features.ts STYLE.deliveryNote*), neither ON = the original. Stamped on
    // the note here rather than read by the UI, so the Completed Orders preview, the
    // auto-print and the emailed PDF (both rendered from the print page, which has no login
    // to read flags with) always draw the same design. Being part of the note's data, it is
    // also part of NoteCacheKey, so a design change re-renders cached PDFs.
    private const string FlagNoteLedger = "Style.DeliveryNoteLedger";
    private const string FlagNoteGreenBand = "Style.DeliveryNoteGreenBand";

    private async Task<string> NoteTemplate(CancellationToken ct)
    {
        var on = await _db.Set<FeatureFlagEntity>().AsNoTracking()
            .Where(f => f.Enabled && (f.Name == FlagNoteLedger || f.Name == FlagNoteGreenBand))
            .Select(f => f.Name).ToListAsync(ct);
        return on.Contains(FlagNoteLedger) ? "ledger"
            : on.Contains(FlagNoteGreenBand) ? "greenband"
            : "classic";
    }

    // GET /api/dispatch/orders/{id}/commercial-invoice — the Evergreen commercial invoice
    // for one order, printed from the same A4 template as the delivery note. The invoice
    // number is the LOAD number (the WeighbridgeRecord the order went out on); the nett
    // weight is the order's calculated weight and gross is the load's weigh-in + that. Lines bill what was PICKED, like the
    // note, at the order line's price per bag; the HS tariff code is the product's
    // commodity code.
    //
    // ?estimate=true is the PRE-commercial invoice (Load detail opened from the Loads grid,
    // before the order is picked): every line bills its ORDERED pallets, each at the product's
    // average pallet weight. A rough figure by design — nothing is weighed yet.
    [HttpGet("orders/{id:int}/commercial-invoice")]
    public async Task<ActionResult<CommercialInvoiceDto>> CommercialInvoice(
        int id, [FromQuery] bool estimate = false, CancellationToken ct = default) =>
        await BuildCommercialInvoice(id, ct, estimate) is { } dto ? dto : NotFound();

    // The invoice's data: what the endpoint above returns and what its print page inlines.
    private async Task<CommercialInvoiceDto?> BuildCommercialInvoice(int id, CancellationToken ct, bool estimate = false)
    {
        var o = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (o == null) return null;

        var cust = await _db.Set<CustomerSupplier>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == o.CustomerId, ct);
        var custAddresses = await _db.Set<CustomerSupplierAddress>().AsNoTracking()
            .Where(a => a.CustomerSupplierId == o.CustomerId)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        // Invoice to: the customer's first (main) address. Deliver to: the order's chosen one.
        var invoiceAddr = custAddresses.FirstOrDefault();
        var deliverAddr = custAddresses.FirstOrDefault(a => a.Id == o.AddressId)
            ?? await _db.Set<CustomerSupplierAddress>().AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == o.AddressId, ct)
            ?? invoiceAddr;

        static List<string> Lines(CustomerSupplierAddress? a) =>
            new[] { a?.Address1, a?.Address2, a?.Address3, a?.Address4, a?.Address5 }
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l!.Trim())
                .ToList();

        var rec = o.WeighbridgeRecordId > 0
            ? await _db.Set<WeighbridgeRecord>().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == o.WeighbridgeRecordId, ct)
            : null;
        // The weights are THIS order's product weights, never the lorry's (a load can carry
        // several orders, and the client wants the goods only on this document):
        //  - gross = the order's calculated weight: its picked pallets' weights (estimates
        //    included, as the picking app totals them), so packaging and all;
        //  - nett  = gross less the packaging: per picked pallet, the empty pallet
        //    (Product.PalletWeight) and its empty bags (Product.BagWeightGrams x the line's UPP).
        // Per line (the Weight column is the line's gross), per HS tariff code (the two
        // Combined Weight tables), and for the order (Total gross / Total nett).
        var linePicks = await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            .Where(p => p.DateDeleted == null && p.DispatchOrderId == id)
            .GroupBy(p => p.DispatchOrderItemId)
            .Select(g => new { ItemId = g.Key, Kg = g.Sum(p => p.Weight), Pallets = g.Count() })
            .ToDictionaryAsync(x => x.ItemId, ct);

        var rows = await _db.Set<DispatchOrderItemRow>().AsNoTracking()
            .Where(i => i.OrderId == id)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);
        var itemIds = rows.Select(r => r.Id).ToList();
        var prices = await _db.Set<DispatchOrderItemEntity>().AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Price, ct);
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.CommodityCode, p.PalletWeight, p.BagWeightGrams, p.AverageWeight }, ct);

        // Estimate only: one pallet's weight per product — the Average Weight set on the
        // Products list, else the median of its weighed pallets still in stock.
        var avgPallet = new Dictionary<int, decimal>();
        if (estimate)
        {
            foreach (var g in rows.GroupBy(r => r.ProductId))
            {
                var avg = products.GetValueOrDefault(g.Key)?.AverageWeight ?? 0m;
                var gtin = (g.First().Gtin ?? "").Trim();
                if (avg <= 0 && gtin.Length > 0)
                {
                    var inStock = await _db.Set<CodicoPalletEntity>().AsNoTracking()
                        .Where(c => c.DateOutOfStock == null && c.PickedOrderId == null && c.Weight > 0
                            && (c.Code13 == gtin || c.Code == gtin))
                        .OrderByDescending(c => c.Id)
                        .Take(200)
                        .Select(c => c.Weight)
                        .ToListAsync(ct);
                    if (inStock.Count > 0)
                    {
                        inStock.Sort();
                        avg = inStock[inStock.Count / 2];
                    }
                }
                avgPallet[g.Key] = avg;
            }
        }

        var items = rows.Select(r =>
        {
            var pallets = estimate ? r.Quantity ?? 0 : r.PickedQuantity ?? 0;
            var units = pallets * r.Upp;
            var rate = prices.GetValueOrDefault(r.Id);
            var product = products.GetValueOrDefault(r.ProductId);
            int? weight = null;
            decimal? nettWeight = null;
            // Gross kg and pallet count to weigh: the picks, or (estimate) ordered x average.
            var (kg, palletCount) = estimate
                ? (pallets * avgPallet.GetValueOrDefault(r.ProductId), pallets)
                : linePicks.TryGetValue(r.Id, out var picks) ? (picks.Kg, picks.Pallets) : (0m, 0);
            if (kg > 0)
            {
                weight = (int)Math.Round(kg, MidpointRounding.AwayFromZero);
                // A product with no bag weight typed in yet loses only its pallet.
                var packaging = (product?.PalletWeight ?? 0m)
                    + (product?.BagWeightGrams ?? 0m) / 1000m * r.Upp;
                nettWeight = Math.Max(0m, Math.Round(weight.Value - palletCount * packaging, 2,
                    MidpointRounding.AwayFromZero));
            }
            return new CommercialInvoiceItemDto
            {
                Description = r.ProductDescription,
                Size = r.Size,
                Upp = r.Upp,
                Pallets = pallets,
                HsCode = product?.CommodityCode,
                Units = units,
                Weight = weight,
                NettWeight = nettWeight,
                Rate = rate,
                Nett = rate is { } p ? Math.Round(units * p, 2, MidpointRounding.AwayFromZero) : 0m,
            };
        }).ToList();

        // Combined by HS tariff code, in the order the codes first appear in the lines; lines
        // with nothing picked are left out.
        var weighed = items.Where(i => i.Weight != null).ToList();
        var byHs = weighed
            .GroupBy(i => i.HsCode?.Trim() ?? "")
            .Select(g => new CommercialInvoiceWeightDto
            {
                HsCode = g.Key,
                Gross = g.Sum(i => i.Weight!.Value),
                Nett = g.Sum(i => i.NettWeight ?? 0m),
            })
            .ToList();
        // The order's products whose packaging weights are not set up, so the nett above is
        // short of them: the preview warns, the printed invoice does not show it.
        var missingWeights = rows
            .GroupBy(r => r.ProductId)
            .Select(g =>
            {
                var product = products.GetValueOrDefault(g.Key);
                return new CommercialInvoiceMissingWeightDto
                {
                    ProductId = g.Key,
                    Description = g.First().ProductDescription,
                    Size = g.First().Size,
                    NoPalletWeight = (product?.PalletWeight ?? 0m) <= 0,
                    NoBagWeight = (product?.BagWeightGrams ?? 0m) <= 0,
                };
            })
            .Where(m => m.NoPalletWeight || m.NoBagWeight)
            .ToList();
        int? gross = weighed.Count > 0 ? weighed.Sum(i => i.Weight!.Value) : null;
        decimal? net = weighed.Count > 0 ? weighed.Sum(i => i.NettWeight ?? 0m) : null;

        var nettTotal = items.Sum(i => i.Nett);
        var vat = Math.Round(nettTotal * CommercialInvoiceDto.VatRate, 2, MidpointRounding.AwayFromZero);

        return new CommercialInvoiceDto
        {
            Template = await NoteTemplate(ct),
            Estimate = estimate,
            OrderId = o.Id,
            InvoiceNo = o.WeighbridgeRecordId,
            OrderNo = o.OrderNo,
            Date = rec?.DateWeightOut ?? o.DateDispatched ?? o.DateCreated,
            AccountRef = cust?.Code,
            InvoiceName = cust?.Name,
            InvoiceLines = Lines(invoiceAddr),
            DeliverName = string.IsNullOrWhiteSpace(deliverAddr?.AddressName)
                ? cust?.Name
                : deliverAddr!.AddressName!.Trim(),
            DeliverLines = Lines(deliverAddr),
            DeliverFull = o.AddressFull,
            TotalGross = gross,
            TotalNet = net,
            Items = items,
            WeightsByHsCode = byHs,
            MissingWeights = missingWeights,
            Nett = nettTotal,
            Vat = vat,
            Total = nettTotal + vat,
        };
    }

    // Inlined note data is written into a <script> tag, so the encoder must keep escaping
    // HTML-significant characters — the Web defaults do (< > & become \uXXXX). Do NOT
    // swap in UnsafeRelaxedJsonEscaping: a customer name or comment containing "</script>"
    // would then break out of the tag.
    private static readonly JsonSerializerOptions PrintJson = new(JsonSerializerDefaults.Web);

    // GET /api/dispatch/orders/{id}/delivery-note/print — the delivery note as a
    // standalone HTML page, for the server-side auto-print path (headless Chrome → PDF →
    // Weighbridge Printer). Serves the Admin UI's built print.html with this note's data
    // inlined as window.__note, so the page renders the very same DeliveryNoteSheet
    // component the Completed Orders preview uses.
    //
    // Anonymous, but LOOPBACK ONLY: the rest of this controller is [Authorize] and this
    // page carries customer data, so it must not be reachable off the box. Inlining the
    // data rather than having the page fetch it is what lets it stay anonymous — there is
    // no token to put in a URL — and it removes a network round-trip that could still be
    // in flight when Chrome's virtual-time budget expires, which would print a blank note.
    [AllowAnonymous]
    [HttpGet("orders/{id:int}/delivery-note/print")]
    public async Task<IActionResult> DeliveryNotePrintPage(
        [FromServices] IWebHostEnvironment env,
        [FromServices] IConfiguration config,
        int id, CancellationToken ct = default)
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote == null || !IPAddress.IsLoopback(remote)) return NotFound();

        var dto = await BuildDeliveryNote(id, ct);
        if (dto == null) return NotFound();
        return await PrintPage(env, config, $"<script>window.__note = {JsonSerializer.Serialize(dto, PrintJson)};</script>", ct);
    }

    // GET /api/dispatch/orders/{id}/order-confirmation/print — the order confirmation, likewise
    // (the delivery note inlined with Confirmation set), for the PDF the order confirmation email
    // attaches. Loopback only, as above.
    [AllowAnonymous]
    [HttpGet("orders/{id:int}/order-confirmation/print")]
    public async Task<IActionResult> OrderConfirmationPrintPage(
        [FromServices] IWebHostEnvironment env,
        [FromServices] IConfiguration config,
        int id, CancellationToken ct = default)
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote == null || !IPAddress.IsLoopback(remote)) return NotFound();

        var dto = await BuildDeliveryNote(id, ct, confirmation: true);
        if (dto == null) return NotFound();
        return await PrintPage(env, config, $"<script>window.__note = {JsonSerializer.Serialize(dto, PrintJson)};</script>", ct);
    }

    // GET /api/dispatch/orders/{id}/commercial-invoice/print — the commercial invoice, likewise
    // (the same print.html, with the invoice inlined as window.__invoice), for the PDF the
    // dispatch confirmation's commercial copy attaches. Loopback only, as above.
    [AllowAnonymous]
    [HttpGet("orders/{id:int}/commercial-invoice/print")]
    public async Task<IActionResult> CommercialInvoicePrintPage(
        [FromServices] IWebHostEnvironment env,
        [FromServices] IConfiguration config,
        int id, CancellationToken ct = default)
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote == null || !IPAddress.IsLoopback(remote)) return NotFound();

        var dto = await BuildCommercialInvoice(id, ct);
        if (dto == null) return NotFound();
        return await PrintPage(env, config, $"<script>window.__invoice = {JsonSerializer.Serialize(dto, PrintJson)};</script>", ct);
    }

    // GET /api/dispatch/orders/{id}/pre-commercial-invoice/print — the pre-commercial invoice
    // (the estimate), likewise, for the PDF the pre-commercial email attaches. Loopback only.
    [AllowAnonymous]
    [HttpGet("orders/{id:int}/pre-commercial-invoice/print")]
    public async Task<IActionResult> PreCommercialInvoicePrintPage(
        [FromServices] IWebHostEnvironment env,
        [FromServices] IConfiguration config,
        int id, CancellationToken ct = default)
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote == null || !IPAddress.IsLoopback(remote)) return NotFound();

        var dto = await BuildCommercialInvoice(id, ct, estimate: true);
        if (dto == null) return NotFound();
        return await PrintPage(env, config, $"<script>window.__invoice = {JsonSerializer.Serialize(dto, PrintJson)};</script>", ct);
    }

    // Default is the deployed Admin UI. AdminUi:PrintPagePath lets a dev point at a
    // local TauriAdmin build via appsettings.Local.json, without staging a build into
    // the source tree's wwwroot (which is how a UI release once got rolled back).
    private static string PrintPagePath(IWebHostEnvironment env, IConfiguration config)
    {
        var configured = config["AdminUi:PrintPagePath"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.WebRootPath ?? "", "admin", "print.html")
            : configured;
    }

    // The print page's build, as its last write time: part of every PDF cache key, so a UI
    // release (which can change how a document renders, or teach the page a new one) never
    // leaves an earlier render in the cache. Empty when there is no page yet.
    private string PrintPageStamp()
    {
        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var path = PrintPagePath(env, _config);
        return System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path).Ticks.ToString() : "";
    }

    // The Admin UI's print.html with a document's data inlined in place of its marker.
    private async Task<IActionResult> PrintPage(
        IWebHostEnvironment env, IConfiguration config, string dataScript, CancellationToken ct)
    {
        var path = PrintPagePath(env, config);
        if (!System.IO.File.Exists(path))
            return Problem($"Delivery-note print page not found at '{path}'. "
                + "Deploy the Admin UI, or set AdminUi:PrintPagePath.");

        var html = await System.IO.File.ReadAllTextAsync(path, ct);

        // print.html ships with <base href="./"> so it also works opened directly at
        // /admin/print.html. Served from this route the document URL is an /api/... path,
        // against which the hashed assets and the logos (images/*.png) would 404 — so
        // point the base at the UI folder instead.
        html = html.Replace("""<base href="./" />""", """<base href="/admin/" />""");
        html = html.Replace("<!--GW_NOTE_DATA-->", dataScript);

        return Content(html, "text/html; charset=utf-8");
    }

    // GET /api/dispatch/loads — the "Loads" (pre-load building) grid.
    [HttpGet("loads")]
    public async Task<ActionResult> Loads(CancellationToken ct = default)
    {
        var rows = await _db.Set<WeighbridgePreLoadRow>().AsNoTracking()
            .OrderByDescending(r => r.CollectionDate)
            .ToListAsync(ct);
        // The latest "ready for collection" email sent for each load, for the grid's Emailed
        // column (WeighbridgeRecordEmail, written by LoadEmailSend).
        var ids = rows.Select(r => r.Id).ToList();
        var latestEmail = await LatestEmails(ids, WeighbridgeRecordEmail.KindReady, ct);
        return Ok(rows.Select(r => new
        {
            r.Id,
            r.IdRef,
            r.CollectionDate,
            r.HaulierId,
            r.HaulierName,
            r.VehicleReg,
            r.TrailerNo,
            r.OrderCount,
            EmailSentAt = latestEmail.TryGetValue(r.Id, out var e) ? e.DateSent : (DateTime?)null,
            EmailSentTo = latestEmail.TryGetValue(r.Id, out var e2) ? e2.SentTo : null,
        }));
    }

    // GET /api/dispatch/pending — pending weighbridge records. A record only
    // becomes "pending" once it has been weighed in (DateWeightIn set); before
    // that it is still a pre-load and belongs in the Loads grid.
    // Dispatch only handles outgoing loads (Collection on TauriTouch), so this
    // list is restricted to IsOutgoing records; incoming deliveries belong on
    // the Deliveries/Incoming (weighbridge) page.
    [HttpGet("pending")]
    public async Task<ActionResult> Pending(CancellationToken ct = default)
    {
        var rows = await _db.Set<WeighbridgePendingRow>().AsNoTracking()
            .Where(r => r.DateWeightIn != null && r.IsOutgoing)
            .OrderByDescending(r => r.PendingWeighDate)
            .ToListAsync(ct);

        // Picking progress per record: picked vs ordered pallets across its orders.
        var ids = rows.Select(r => r.Id).ToList();
        var totals = await _db.Set<DispatchOrderRow>().AsNoTracking()
            .Where(o => ids.Contains(o.WeighbridgeRecordId) && o.DateDeleted == null)
            .GroupBy(o => o.WeighbridgeRecordId)
            .Select(g => new
            {
                Id = g.Key,
                Picked = g.Sum(x => x.PickedQuantity),
                Ordered = g.Sum(x => x.TotalQuantityOrdered),
            })
            .ToListAsync(ct);
        var byId = totals.ToDictionary(t => t.Id);

        return Ok(rows.Select(r => new
        {
            r.Id,
            IdRef = (r.Id % 1000).ToString("D3"), // same 3-digit ref as the Loads grid (vwWeighbridgePreLoad.IdRef)
            r.HaulierId,
            r.HaulierName,
            r.VehicleReg,
            r.TrailerNo,
            r.PendingWeight,
            r.PendingWeighDate,
            r.OrderCount,
            r.IsOutgoing,
            r.Product,
            r.Supplier,
            r.BogName,
            r.WeightIn,
            r.DateWeightIn,
            r.DocketNumber,
            PickedQuantity = byId.TryGetValue(r.Id, out var t) ? t.Picked : 0,
            TotalQuantityOrdered = byId.TryGetValue(r.Id, out var t2) ? t2.Ordered : 0,
        }));
    }

    // GET /api/dispatch/completed-weighbridge?from=&to= — completed deliveries: weighed-out,
    // outgoing (IsOutgoing) weighbridge records only. With no range, the latest 500 (the
    // Completed Loads panel on the Dispatch page); with one, every load weighed out in it
    // (the Completed Loads tab).
    [HttpGet("completed-weighbridge")]
    public async Task<ActionResult> CompletedWeighbridge(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var q = _db.Set<WeighbridgeCompleteRow>().AsNoTracking()
            .Where(r => r.DateWeightOut != null && r.IsOutgoing);
        if (from != null) q = q.Where(r => r.DateWeightOut >= from);
        if (to != null) q = q.Where(r => r.DateWeightOut < to.Value.AddDays(1));
        q = q.OrderByDescending(r => r.DateWeightOut);
        if (from == null && to == null) q = q.Take(500);
        var rows = await q.ToListAsync(ct);

        // Pallets on each load: the picked totals of its orders, counted as the Pending endpoint
        // counts them. The view carries no pallet count of its own.
        var ids = rows.Select(r => r.Id).ToList();
        var pallets = await _db.Set<DispatchOrderRow>().AsNoTracking()
            .Where(o => ids.Contains(o.WeighbridgeRecordId) && o.DateDeleted == null)
            .GroupBy(o => o.WeighbridgeRecordId)
            .Select(g => new { Id = g.Key, Picked = g.Sum(x => x.PickedQuantity) })
            .ToDictionaryAsync(g => g.Id, g => g.Picked, ct);
        // Both emails a load gets: the "ready for collection" one it had on the Loads grid, and
        // the dispatch confirmation sent from this grid once it has weighed out. The Completed
        // Loads grids' Emailed column shows the second.
        var latestEmail = await LatestEmails(ids, WeighbridgeRecordEmail.KindReady, ct);
        var latestDispatchEmail = await LatestEmails(ids, WeighbridgeRecordEmail.KindDispatched, ct);

        return Ok(rows.Select(r => new
        {
            r.Id,
            r.HaulierName,
            r.VehicleReg,
            r.TrailerNo,
            r.DateWeightIn,
            r.DateWeightOut,
            r.WeightIn,
            r.WeightOut,
            r.NetWeight,
            r.OrderCount,
            r.TotalWeightPicked,
            r.Product,
            r.Supplier,
            r.BogName,
            r.IsOutgoing,
            r.DocketNumber,
            r.SampleBoxWeight,
            r.CubicM3,
            PickedQuantity = pallets.TryGetValue(r.Id, out var p) ? p : 0,
            EmailSentAt = latestEmail.TryGetValue(r.Id, out var e) ? e.DateSent : (DateTime?)null,
            EmailSentTo = latestEmail.TryGetValue(r.Id, out var e2) ? e2.SentTo : null,
            DispatchEmailSentAt = latestDispatchEmail.TryGetValue(r.Id, out var d) ? d.DateSent : (DateTime?)null,
            DispatchEmailSentTo = latestDispatchEmail.TryGetValue(r.Id, out var d2) ? d2.SentTo : null,
        }));
    }

    // The latest email of one kind sent for each of the loads, keyed by load, for a grid's
    // Emailed column.
    private async Task<Dictionary<int, WeighbridgeRecordEmail>> LatestEmails(
        List<int> ids, string kind, CancellationToken ct) =>
        (await _db.Set<WeighbridgeRecordEmail>().AsNoTracking()
                .Where(e => ids.Contains(e.WeighbridgeRecordId) && e.Kind == kind)
                .OrderByDescending(e => e.DateSent).ThenByDescending(e => e.Id)
                .ToListAsync(ct))
            .GroupBy(e => e.WeighbridgeRecordId)
            .ToDictionary(g => g.Key, g => g.First());

    // ── Emails to the haulier ────────────────────────────────────────────────────────────
    // Two messages, each composed in one place (Services/LoadEmailComposer.cs, in the wording
    // set in Admin > Automatic Emails) so the Send Email dialog's preview is exactly what goes
    // out, and each recorded as a WeighbridgeRecordEmail of its own Kind:
    //  - "ready for collection", from the Loads grid. The 3-digit ref is the key line: it is
    //    what the driver keys into the weighbridge app on arrival to book in, be weighed and be
    //    sent round to be loaded. The orders and their delivery addresses follow, in drop order.
    //  - the dispatch confirmation, from the Completed Loads grid once the load has weighed
    //    out: it has been loaded and has left, with the weights and what is on board.

    // GET /api/dispatch/loads/{id}/email — the "ready for collection" message as it will go
    // out, the sender it goes from, and the haulier's addresses to offer as recipients.
    [HttpGet("loads/{id:int}/email")]
    public async Task<ActionResult> LoadEmailPreview(int id, CancellationToken ct = default) =>
        await EmailPreview(await ComposeLoadEmail(id, ct), ct);

    // POST /api/dispatch/loads/{id}/email — send it to the given addresses.
    [HttpPost("loads/{id:int}/email")]
    public async Task<ActionResult> LoadEmailSend(
        int id,
        [FromBody] LoadEmailSendInput input,
        [FromServices] EmailSender email,
        [FromServices] DeliveryNotePdfRenderer notes,
        CancellationToken ct = default) =>
        await EmailSend(id, await ComposeLoadEmail(id, ct), WeighbridgeRecordEmail.KindReady, "haulier's", input, email, notes, ct);

    // GET /api/dispatch/loads/{id}/dispatch-email — the dispatch confirmation, likewise, and
    // with it each customer's copy (their orders, their delivery notes, their addresses).
    [HttpGet("loads/{id:int}/dispatch-email")]
    public async Task<ActionResult> DispatchEmailPreview(int id, CancellationToken ct = default)
    {
        var d = await LoadMailDataAsync(AutomaticEmails.Dispatched, id, ct);
        if (d == null) return NotFound();
        var mail = LoadEmailComposer.Compose(AutomaticEmails.Dispatched, d,
            await AutomaticEmails.WordingAsync(_db, AutomaticEmails.Dispatched, ct));
        var customers = (await CustomerEmailsAsync(d, null, ct))
            .Select(c => new
            {
                c.CustomerId,
                c.CustomerName,
                c.Mail.Recipients,
                c.Mail.Subject,
                c.Mail.Html,
                c.Mail.Text,
                c.Mail.Attachments,
            })
            .ToList();
        return await EmailPreview(mail, customers, ct, CommercialInvoiceAttachments(d));
    }

    // GET /api/dispatch/loads/{id}/pre-commercial-email — the pre-commercial invoices email, to the
    // haulier only, each order's pre-commercial invoice attached.
    [HttpGet("loads/{id:int}/pre-commercial-email")]
    public async Task<ActionResult> PreCommercialEmailPreview(int id, CancellationToken ct = default) =>
        await EmailPreview(await ComposeEmail(AutomaticEmails.PreCommercial, id, ct), ct);

    // POST /api/dispatch/loads/{id}/pre-commercial-email — send it to the given addresses.
    [HttpPost("loads/{id:int}/pre-commercial-email")]
    public async Task<ActionResult> PreCommercialEmailSend(
        int id,
        [FromBody] LoadEmailSendInput input,
        [FromServices] EmailSender email,
        [FromServices] DeliveryNotePdfRenderer notes,
        CancellationToken ct = default) =>
        await EmailSend(id, await ComposeEmail(AutomaticEmails.PreCommercial, id, ct),
            WeighbridgeRecordEmail.KindPreCommercial, "haulier's", input, email, notes, ct);

    // Each order's commercial invoice, as the haulier's dispatch confirmation carries them (after
    // its delivery notes) when the dialog's Commercial Invoice addresses are ticked.
    private static List<LoadEmailAttachment> CommercialInvoiceAttachments(LoadMailData d) =>
        d.Orders
            .Select(o => new LoadEmailAttachment(o.Id, o.OrderNo,
                CommercialInvoiceFileName(d.Rec.Id, o.OrderNo), LoadEmailAttachment.Invoice))
            .ToList();

    // POST /api/dispatch/loads/{id}/dispatch-email — send it: the haulier's, or with a
    // CustomerId, that customer's copy.
    [HttpPost("loads/{id:int}/dispatch-email")]
    public async Task<ActionResult> DispatchEmailSend(
        int id,
        [FromBody] LoadEmailSendInput input,
        [FromServices] EmailSender email,
        [FromServices] DeliveryNotePdfRenderer notes,
        CancellationToken ct = default)
    {
        if (input.CustomerId is not int customerId)
        {
            var load = await LoadMailDataAsync(AutomaticEmails.Dispatched, id, ct);
            if (load == null) return NotFound();
            var haulier = LoadEmailComposer.Compose(AutomaticEmails.Dispatched, load,
                await AutomaticEmails.WordingAsync(_db, AutomaticEmails.Dispatched, ct));
            // Commercial: the commercial invoices go too, after the delivery notes.
            if (input.Commercial)
                haulier = haulier with
                {
                    Attachments = haulier.Attachments.Concat(CommercialInvoiceAttachments(load)).ToList(),
                };
            return await EmailSend(id, haulier, WeighbridgeRecordEmail.KindDispatched,
                "haulier's", input, email, notes, ct);
        }
        var d = await LoadMailDataAsync(AutomaticEmails.Dispatched, id, ct);
        if (d == null) return NotFound();
        var c = (await CustomerEmailsAsync(d, customerId, ct)).FirstOrDefault();
        if (c == null) return NotFound(new { message = "That customer has no orders on this load." });
        return await EmailSend(id, c.Mail, WeighbridgeRecordEmail.KindDispatchedCustomer,
            "customer's", input, email, notes, ct);
    }

    private async Task<ActionResult> EmailPreview(LoadEmail? mail, CancellationToken ct) =>
        await EmailPreview(mail, Array.Empty<object>(), ct);

    private async Task<ActionResult> EmailPreview(
        LoadEmail? mail, IEnumerable<object> customers, CancellationToken ct,
        List<LoadEmailAttachment>? commercialInvoices = null, string? customerName = null)
    {
        if (mail == null) return NotFound();
        var setting = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        return Ok(new
        {
            mail.Ref,
            mail.HaulierName,
            // The order confirmation's: who the main message goes to (its recipients are theirs).
            CustomerName = customerName,
            mail.Recipients,
            mail.Subject,
            mail.Html,
            mail.Text,
            mail.Attachments,
            Customers = customers,
            CommercialInvoices = commercialInvoices ?? new List<LoadEmailAttachment>(),
            FromAddress = setting?.EmailFromAddress,
            FromName = setting?.EmailFromName,
            EmailReady = setting != null && EmailSender.ConfigFrom(setting) != null,
        });
    }

    // CustomerId: on the dispatch confirmation, send that customer's copy instead of the haulier's.
    // Commercial: on the haulier's dispatch confirmation, attach each order's commercial invoice too.
    public record LoadEmailSendInput(List<string>? To, string? OverrideTo, int? CustomerId = null, bool Commercial = false);

    public record CustomerLoadEmail(int CustomerId, string CustomerName, LoadEmail Mail);

    // Each customer's copy of the dispatch confirmation (or just the one asked for), in the
    // order of their first drop: their orders, their delivery notes, and their own addresses
    // (CustomerSupplierEmail, as the Emails tab of Edit Customer keeps them).
    private async Task<List<CustomerLoadEmail>> CustomerEmailsAsync(LoadMailData d, int? only, CancellationToken ct)
    {
        var ids = d.Orders.Select(o => o.CustomerId).Distinct()
            .Where(c => only == null || c == only).ToList();
        if (ids.Count == 0) return new List<CustomerLoadEmail>();
        var names = await _db.Set<CustomerSupplier>().AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var addresses = await _db.Set<CustomerSupplierEmail>().AsNoTracking()
            .Where(e => ids.Contains(e.CustomerSupplierId))
            .OrderBy(e => e.Id)
            .Select(e => new { e.CustomerSupplierId, e.Email })
            .ToListAsync(ct);
        var wording = await AutomaticEmails.WordingAsync(_db, AutomaticEmails.DispatchedCustomer, ct);
        return ids.Select(cid =>
        {
            var name = (names.GetValueOrDefault(cid)
                ?? d.Orders.First(o => o.CustomerId == cid).CustomerName ?? "").Trim();
            var to = addresses.Where(a => a.CustomerSupplierId == cid)
                .Select(a => a.Email.Trim()).Where(a => a.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var mine = LoadEmailComposer.ForCustomer(d, cid, name, to);
            return new CustomerLoadEmail(cid, name,
                LoadEmailComposer.Compose(AutomaticEmails.DispatchedCustomer, mine, wording));
        }).ToList();
    }

    // Send the message to the given addresses, all in the one email's To. The dialog calls
    // this once per address to send individually, or once with every address to send one
    // email. Only the recipient's own addresses go (whose: "haulier's" / "customer's").
    private async Task<ActionResult> EmailSend(
        int id, LoadEmail? mail, string kind, string whose, LoadEmailSendInput input, EmailSender email,
        DeliveryNotePdfRenderer notes, CancellationToken ct)
    {
        if (mail == null) return NotFound();
        var (error, to) = await DeliverAsync(mail, whose, input, email, notes, ct);
        if (error != null) return error;
        // On the record: the grid shows the load as emailed from this.
        _db.Add(new WeighbridgeRecordEmail
        {
            WeighbridgeRecordId = id,
            Kind = kind,
            SentTo = string.Join(", ", to),
            Subject = mail.Subject,
            SentById = this.GetUserId(),
            DateSent = DateTime.Now,
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new { sent = to });
    }

    // The send itself, for a load's emails and an order's alike: the addresses checked against
    // the message's own, the attachments rendered, the email sent. Error is what to return when
    // it did not go; otherwise To is who it went to, for the caller to record.
    private async Task<(ActionResult? Error, List<string> To)> DeliverAsync(
        LoadEmail mail, string whose, LoadEmailSendInput input, EmailSender email,
        DeliveryNotePdfRenderer notes, CancellationToken ct)
    {
        // A test address typed in the dialog takes the whole send: the recipient gets nothing.
        var overrideTo = input.OverrideTo?.Trim();
        List<string> to;
        if (!string.IsNullOrEmpty(overrideTo))
        {
            if (!System.Net.Mail.MailAddress.TryCreate(overrideTo, out _))
                return (BadRequest(new { message = $"{overrideTo} is not an email address." }), new());
            to = new List<string> { overrideTo };
        }
        else
        {
            to = (input.To ?? new List<string>())
                .Select(a => a.Trim()).Where(a => a.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (to.Count == 0) return (BadRequest(new { message = "Choose at least one address." }), new());
            var allowed = new HashSet<string>(mail.Recipients, StringComparer.OrdinalIgnoreCase);
            var stranger = to.FirstOrDefault(a => !allowed.Contains(a));
            if (stranger != null)
                return (BadRequest(new { message = $"{stranger} is not one of the {whose} addresses." }), new());
        }
        // The attachments first, rendered on this box or taken from the renderer's cache: a note
        // that will not render stops the send, rather than the email going out without it.
        var files = new List<EmailSender.Attachment>(mail.Attachments.Count);
        foreach (var a in mail.Attachments)
        {
            try
            {
                var pdf = a.Kind == LoadEmailAttachment.Invoice
                    ? await RenderCommercialInvoicePdfAsync(a.OrderId, notes, ct)
                    : a.Kind == LoadEmailAttachment.PreInvoice
                        ? await RenderCommercialInvoicePdfAsync(a.OrderId, notes, ct, estimate: true)
                        : a.Kind == LoadEmailAttachment.Confirmation
                            ? await RenderDeliveryNotePdfAsync(a.OrderId, notes, ct, confirmation: true)
                            : await RenderDeliveryNotePdfAsync(a.OrderId, notes, ct);
                files.Add(new EmailSender.Attachment(a.FileName, pdf, "application/pdf"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var what = a.Kind == LoadEmailAttachment.Invoice ? "commercial invoice"
                    : a.Kind == LoadEmailAttachment.PreInvoice ? "pre-commercial invoice"
                    : a.Kind == LoadEmailAttachment.Confirmation ? "order confirmation"
                    : "delivery note";
                return (StatusCode(502, new { message = $"Could not render the {what} for order {a.OrderNo}: {ex.Message}" }), new());
            }
        }
        try
        {
            await email.SendHtmlAsync(to, mail.Subject, mail.Html, mail.Text, files, ct);
        }
        catch (InvalidOperationException ex)
        {
            return (BadRequest(new { message = ex.Message }), new());
        }
        catch (Exception ex)
        {
            return (StatusCode(502, new { message = EmailSender.Describe(ex) }), new());
        }
        return (null, to);
    }

    // ── The order confirmation, to the customer ──────────────────────────────────────────
    // Sent from Open Orders (the grid's envelope button): the order as taken, so the customer can
    // check it before it is picked, with the order confirmation PDF attached (the delivery note,
    // titled Order Confirmation, lines as ordered). Composed by LoadEmailComposer in the wording
    // set in Admin > Automatic Emails, and recorded as a DispatchOrderEmail.

    // GET /api/dispatch/orders/{id}/confirmation-email — the message as it will go out, the sender
    // it goes from, and the customer's addresses to offer as recipients.
    [HttpGet("orders/{id:int}/confirmation-email")]
    public async Task<ActionResult> OrderConfirmationEmailPreview(int id, CancellationToken ct = default)
    {
        var d = await OrderMailDataAsync(id, ct);
        if (d == null) return NotFound();
        var mail = LoadEmailComposer.Compose(AutomaticEmails.OrderConfirmation, d,
            await AutomaticEmails.WordingAsync(_db, AutomaticEmails.OrderConfirmation, ct));
        return await EmailPreview(mail, Array.Empty<object>(), ct, customerName: d.CustomerName);
    }

    // POST /api/dispatch/orders/{id}/confirmation-email — send it to the given addresses.
    [HttpPost("orders/{id:int}/confirmation-email")]
    public async Task<ActionResult> OrderConfirmationEmailSend(
        int id,
        [FromBody] LoadEmailSendInput input,
        [FromServices] EmailSender email,
        [FromServices] DeliveryNotePdfRenderer notes,
        CancellationToken ct = default)
    {
        var d = await OrderMailDataAsync(id, ct);
        if (d == null) return NotFound();
        var mail = LoadEmailComposer.Compose(AutomaticEmails.OrderConfirmation, d,
            await AutomaticEmails.WordingAsync(_db, AutomaticEmails.OrderConfirmation, ct));
        var (error, to) = await DeliverAsync(mail, "customer's", input, email, notes, ct);
        if (error != null) return error;
        // On the record: the Open Orders grid shows the order as emailed from this.
        _db.Add(new DispatchOrderEmail
        {
            DispatchOrderId = id,
            Kind = DispatchOrderEmail.KindConfirmation,
            SentTo = string.Join(", ", to),
            Subject = mail.Subject,
            SentById = this.GetUserId(),
            DateSent = DateTime.Now,
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new { sent = to });
    }

    // What the order confirmation is built from, in the shape the load's emails use: the one
    // order, its customer's name and addresses (CustomerSupplierEmail, as the Emails tab of Edit
    // Customer keeps them), its delivery-note comments and its order confirmation PDF. The load
    // it is on, if it is on one yet; a blank record if not (nothing of the load is shown).
    private async Task<LoadMailData?> OrderMailDataAsync(int id, CancellationToken ct)
    {
        var order = await _db.Set<DispatchOrderRow>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.DateDeleted == null, ct);
        if (order == null) return null;
        var rec = order.WeighbridgeRecordId > 0
            ? await _db.Set<WeighbridgeRecord>().AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == order.WeighbridgeRecordId, ct)
            : null;
        var customerName = (await _db.Set<CustomerSupplier>().AsNoTracking()
                .Where(c => c.Id == order.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct)
            ?? order.CustomerName ?? "").Trim();
        var recipients = (await _db.Set<CustomerSupplierEmail>().AsNoTracking()
                .Where(e => e.CustomerSupplierId == order.CustomerId)
                .OrderBy(e => e.Id)
                .Select(e => e.Email)
                .ToListAsync(ct))
            .Select(e => e.Trim()).Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var comments = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => o.Id == id).Select(o => o.Comments).FirstOrDefaultAsync(ct);
        var attachments = new List<LoadEmailAttachment>
        {
            new(order.Id, order.OrderNo, OrderConfirmationFileName(order.OrderNo), LoadEmailAttachment.Confirmation),
        };
        return new LoadMailData(rec ?? new WeighbridgeRecord(), order.OrderNo.ToString(),
            (order.HaulierName ?? "").Trim(), recipients, new List<DispatchOrderRow> { order },
            new Dictionary<int, string?> { [order.Id] = comments }, attachments, CustomerName: customerName);
    }

    // The two messages, composed by Services/LoadEmailComposer.cs from the load and the wording
    // set in Admin > Automatic Emails (Services/AutomaticEmails.cs).
    private async Task<LoadEmail?> ComposeLoadEmail(int id, CancellationToken ct) =>
        await ComposeEmail(AutomaticEmails.Ready, id, ct);

    private async Task<LoadEmail?> ComposeEmail(string kind, int id, CancellationToken ct)
    {
        var d = await LoadMailDataAsync(kind, id, ct);
        if (d == null) return null;
        return LoadEmailComposer.Compose(kind, d, await AutomaticEmails.WordingAsync(_db, kind, ct));
    }

    // What the message is built from: the load, its 3-digit ref, its haulier's name,
    // telephone and addresses, its orders in drop order, and the orders' delivery-note comments. Each order's
    // delivery note goes with the dispatch confirmation (the PDF the auto-print prints);
    // nothing goes with the "ready" email.
    private async Task<LoadMailData?> LoadMailDataAsync(string kind, int id, CancellationToken ct)
    {
        var rec = await _db.Set<WeighbridgeRecord>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.DateDeleted == null, ct);
        if (rec == null) return null;
        var haulier = await _db.Set<Haulier>().AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == rec.HaulierId, ct);
        var addresses = await _db.Set<HaulierEmail>().AsNoTracking()
            .Where(e => e.HaulierId == rec.HaulierId)
            .OrderBy(e => e.Id)
            .Select(e => e.Email)
            .ToListAsync(ct);
        var recipients = addresses.Select(e => e.Trim()).Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var orders = await _db.Set<DispatchOrderRow>().AsNoTracking()
            .Where(o => o.WeighbridgeRecordId == id && o.DateDeleted == null)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.OrderNo)
            .ToListAsync(ct);
        // The delivery-note comments live on the order itself, not on the grid's view of it.
        var noteById = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => o.WeighbridgeRecordId == id && o.DateDeleted == null)
            .Select(o => new { o.Id, o.Comments })
            .ToDictionaryAsync(o => o.Id, o => o.Comments, ct);
        var reference = (rec.Id % 1000).ToString("D3");
        var haulierName = (haulier?.Name ?? rec.HaulierName ?? "").Trim();
        var attachments = kind == AutomaticEmails.Dispatched
            ? orders.Select(o => new LoadEmailAttachment(o.Id, o.OrderNo, DeliveryNoteFileName(o.OrderNo))).ToList()
            : kind == AutomaticEmails.PreCommercial
                ? orders.Select(o => new LoadEmailAttachment(o.Id, o.OrderNo,
                    CommercialInvoiceFileName(rec.Id, o.OrderNo, estimate: true), LoadEmailAttachment.PreInvoice)).ToList()
                : new List<LoadEmailAttachment>();
        return new LoadMailData(rec, reference, haulierName, recipients, orders, noteById, attachments,
            HaulierTelephone: (haulier?.Tel ?? "").Trim());
    }

    // ── Delivery notes as PDF ────────────────────────────────────────────────────────────
    // The attachment the dispatch confirmation carries for each order, rendered by headless
    // Chrome from the print page below (DeliveryNotePdfRenderer). Also served on its own here,
    // which is how the Send Email dialog shows one and warms the renderer before it sends.

    // GET /api/dispatch/orders/{id}/delivery-note/pdf
    [HttpGet("orders/{id:int}/delivery-note/pdf")]
    public async Task<IActionResult> DeliveryNotePdf(
        int id, [FromServices] DeliveryNotePdfRenderer notes, CancellationToken ct = default)
    {
        var dto = await BuildDeliveryNote(id, ct);
        if (dto == null) return NotFound();
        byte[] pdf;
        try
        {
            pdf = await notes.RenderAsync(id, NoteCacheKey(dto), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(502, new { message = $"Could not render the delivery note for order {dto.NoteNo}: {ex.Message}" });
        }
        // Inline, so a browser shows it rather than saving it; the name is there for a save.
        Response.Headers.ContentDisposition =
            new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline") { FileName = DeliveryNoteFileName(dto.NoteNo) }.ToString();
        return File(pdf, "application/pdf");
    }

    private async Task<byte[]> RenderDeliveryNotePdfAsync(
        int orderId, DeliveryNotePdfRenderer notes, CancellationToken ct, bool confirmation = false)
    {
        var dto = await BuildDeliveryNote(orderId, ct, confirmation)
            ?? throw new InvalidOperationException("The order no longer exists.");
        return confirmation
            ? await notes.RenderAsync(orderId, ConfirmationCacheKey(dto), ct, DeliveryNotePdfRenderer.OrderConfirmation)
            : await notes.RenderAsync(orderId, NoteCacheKey(dto), ct);
    }

    // GET /api/dispatch/orders/{id}/order-confirmation/pdf — the order confirmation as PDF, the
    // attachment the order confirmation email carries; how the Send Email dialog shows it and
    // warms the renderer before it sends.
    [HttpGet("orders/{id:int}/order-confirmation/pdf")]
    public async Task<IActionResult> OrderConfirmationPdf(
        int id, [FromServices] DeliveryNotePdfRenderer notes, CancellationToken ct = default)
    {
        var dto = await BuildDeliveryNote(id, ct, confirmation: true);
        if (dto == null) return NotFound();
        byte[] pdf;
        try
        {
            pdf = await notes.RenderAsync(id, ConfirmationCacheKey(dto), ct, DeliveryNotePdfRenderer.OrderConfirmation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(502, new { message = $"Could not render the order confirmation for order {dto.NoteNo}: {ex.Message}" });
        }
        Response.Headers.ContentDisposition =
            new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline") { FileName = OrderConfirmationFileName(dto.NoteNo) }.ToString();
        return File(pdf, "application/pdf");
    }

    // Prefixed, so a confirmation can never be served from a note's cache entry or the other way round.
    private string ConfirmationCacheKey(DeliveryNoteDto dto) => "confirmation:" + NoteCacheKey(dto);

    private static string OrderConfirmationFileName(int orderNo) => $"Order Confirmation {orderNo}.pdf";

    // What the renderer caches a PDF under: the note's data, hashed, and the print page's
    // build. An order edited since the last render, or a new print page, gets a fresh PDF; an
    // unchanged one is not rendered twice.
    private string NoteCacheKey(DeliveryNoteDto dto) =>
        PrintPageStamp() + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(dto, PrintJson)));

    private static string DeliveryNoteFileName(int noteNo) => $"Delivery Note {noteNo}.pdf";

    // GET /api/dispatch/orders/{id}/commercial-invoice/pdf — the commercial invoice as PDF, the
    // attachment the dispatch confirmation's commercial copy carries; how the Send Email dialog
    // shows one and warms the renderer before it sends.
    // ?estimate=true: the pre-commercial invoice instead.
    [HttpGet("orders/{id:int}/commercial-invoice/pdf")]
    public async Task<IActionResult> CommercialInvoicePdf(
        int id, [FromServices] DeliveryNotePdfRenderer notes, [FromQuery] bool estimate = false,
        CancellationToken ct = default)
    {
        var dto = await BuildCommercialInvoice(id, ct, estimate);
        if (dto == null) return NotFound();
        byte[] pdf;
        try
        {
            pdf = await notes.RenderAsync(id, InvoiceCacheKey(dto), ct, InvoiceDocument(estimate));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var what = estimate ? "pre-commercial invoice" : "commercial invoice";
            return StatusCode(502, new { message = $"Could not render the {what} for order {dto.OrderNo}: {ex.Message}" });
        }
        Response.Headers.ContentDisposition =
            new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline")
            { FileName = CommercialInvoiceFileName(dto.InvoiceNo, dto.OrderNo, estimate) }.ToString();
        return File(pdf, "application/pdf");
    }

    private async Task<byte[]> RenderCommercialInvoicePdfAsync(
        int orderId, DeliveryNotePdfRenderer notes, CancellationToken ct, bool estimate = false)
    {
        var dto = await BuildCommercialInvoice(orderId, ct, estimate)
            ?? throw new InvalidOperationException("The order no longer exists.");
        return await notes.RenderAsync(orderId, InvoiceCacheKey(dto), ct, InvoiceDocument(estimate));
    }

    private static string InvoiceDocument(bool estimate) =>
        estimate ? DeliveryNotePdfRenderer.PreCommercialInvoice : DeliveryNotePdfRenderer.CommercialInvoice;

    // Prefixed, so an invoice can never be served from a note's cache entry or the other way round.
    private string InvoiceCacheKey(CommercialInvoiceDto dto) =>
        "invoice:" + PrintPageStamp() + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(dto, PrintJson)));

    // The invoice number as printed: LoadNo-OrderNo (just the order number off a load).
    private static string CommercialInvoiceFileName(int loadNo, int orderNo, bool estimate = false)
    {
        var name = estimate ? "Pre-Commercial Invoice" : "Commercial Invoice";
        return loadNo > 0 ? $"{name} {loadNo}-{orderNo}.pdf" : $"{name} {orderNo}.pdf";
    }

    // The legacy schema denormalises the chosen delivery address onto the order
    // (AddressFull, CRLF-joined lines) — vwDispatchOrder and the grids read it.
    private Task<string?> BuildAddressFull(int addressId, CancellationToken ct) => BuildAddressFull(_db, addressId, ct);

    internal static async Task<string?> BuildAddressFull(GbEvergreenContext db, int addressId, CancellationToken ct)
    {
        if (addressId <= 0) return null;
        var a = await db.Set<CustomerSupplierAddress>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == addressId, ct);
        if (a == null) return null;
        var lines = new[] { a.Address1, a.Address2, a.Address3, a.Address4, a.Address5 }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!.Trim());
        var full = string.Join("\r\n", lines);
        return string.IsNullOrWhiteSpace(full) ? null : full;
    }

    // The next order number: the counter continuing the legacy system's series once that
    // has been switched on in Admin > Settings, and MAX(OrderNo)+1 until then.
    //
    // The UPDATE is a single statement holding an exclusive lock on the settings row,
    // so two orders created at the same moment cannot be handed the same number — which
    // MAX+1 could, and did nothing to prevent. With the counter off the UPDATE matches
    // nothing, returns nothing, and we fall through to the old behaviour.
    private static async Task<int> AllocateOrderNo(GbEvergreenContext db, CancellationToken ct)
    {
        var seeded = await db.Database.SqlQueryRaw<int>(@"
            UPDATE dbo.Setting SET NextOrderNo = NextOrderNo + 1
            OUTPUT deleted.NextOrderNo AS Value
            WHERE NextOrderNoEnabled = 1 AND NextOrderNo IS NOT NULL
              AND id = (SELECT MIN(id) FROM dbo.Setting)")
            .ToListAsync(ct);
        if (seeded.Count > 0) return seeded[0];

        return (await db.Set<DispatchOrderEntity>().MaxAsync(o => (int?)o.OrderNo, ct) ?? 0) + 1;
    }

    // POST /api/dispatch/orders — create an order + its line items.
    [HttpPost("orders")]
    public async Task<ActionResult> CreateOrder([FromBody] OrderInput input, CancellationToken ct = default)
    {
        // The number and the order it belongs to are written in one transaction: if the
        // insert fails the allocation rolls back with it, rather than burning a number
        // and leaving a gap in a series the old system never had gaps in.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var order = await InsertOrder(_db, input, this.GetUserId(), ct);
        await tx.CommitAsync(ct);

        return Ok(new { id = order.Id, orderNo = order.OrderNo });
    }

    // A new open order and its lines, numbered by AllocateOrderNo. The caller owns the
    // transaction, so the number is only spent if everything else it writes commits too —
    // Add Order here, and approving an order from sales (SalesOrdersController.Approve).
    internal static async Task<DispatchOrderEntity> InsertOrder(
        GbEvergreenContext db, OrderInput input, int createdById, CancellationToken ct)
    {
        var order = new DispatchOrderEntity
        {
            OrderNo = await AllocateOrderNo(db, ct),
            OrderTypeId = 0,
            CustomerId = input.CustomerId,
            HaulierId = input.HaulierId,
            DeliveryDate = input.DeliveryDate,
            PoNumber = input.PoNumber,
            Comments = input.Comments,
            PickingComments = input.PickingComments,
            HasPos = input.HasPos,
            SalesRepId = input.SalesRepId is > 0 ? input.SalesRepId : null,
            VehicleReg = input.VehicleReg,
            TrailerNo = input.TrailerNo,
            TransferOrderNo = 0,
            WeighbridgeRecordId = 0,
            TransferLocationId = 0,
            AddressId = input.AddressId,
            AddressFull = await BuildAddressFull(db, input.AddressId, ct),
            CreatedById = createdById,
            DateCreated = DateTime.Now,
        };
        db.Add(order);
        await db.SaveChangesAsync(ct);

        foreach (var item in input.Items)
        {
            db.Add(new DispatchOrderItemEntity
            {
                OrderId = order.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                Price = item.Price,
                DateCreated = DateTime.Now,
            });
        }
        await db.SaveChangesAsync(ct);
        return order;
    }

    // PUT /api/dispatch/orders/{id} — update an order header + replace its line items.
    [HttpPut("orders/{id:int}")]
    public async Task<ActionResult> UpdateOrder(int id, [FromBody] OrderInput input, CancellationToken ct = default)
    {
        var order = await _db.Set<DispatchOrderEntity>().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order == null || order.DateDeleted != null) return NotFound();

        // Order number: editable only for a pre-hand-over open order (see IsOrderNoEditable).
        // The UI hides the field otherwise, but never trust the client - re-check here and
        // guard against a number already used by another (undeleted) order.
        if (input.OrderNo is { } newNo && newNo != order.OrderNo)
        {
            if (!await IsOrderNoEditable(order, ct))
                return BadRequest(new { message = "This order's number can no longer be changed." });
            if (newNo < 1 || newNo > 99_999_999)
                return BadRequest(new { message = "Enter an order number between 1 and 99,999,999." });
            var clash = await _db.Set<DispatchOrderEntity>().AsNoTracking()
                .AnyAsync(o => o.Id != id && o.DateDeleted == null && o.OrderNo == newNo, ct);
            if (clash)
                return BadRequest(new { message = $"Order number {newNo} is already used by another order." });
            order.OrderNo = newNo;
        }

        order.CustomerId = input.CustomerId;
        order.AddressId = input.AddressId;
        order.AddressFull = await BuildAddressFull(input.AddressId, ct);
        order.HaulierId = input.HaulierId;
        order.DeliveryDate = input.DeliveryDate;
        order.PoNumber = input.PoNumber;
        order.Comments = input.Comments;
        order.PickingComments = input.PickingComments;
        order.HasPos = input.HasPos;
        // The rep of an order approved from sales is fixed; anything else takes the dropdown.
        if (!await IsFromSales(id, ct))
            order.SalesRepId = input.SalesRepId is > 0 ? input.SalesRepId : null;
        order.VehicleReg = input.VehicleReg;
        order.TrailerNo = input.TrailerNo;

        // Reconcile items rather than replacing them wholesale. Picked pallets point at
        // a DispatchOrderItem id, so deleting every row and re-adding it (the old
        // behaviour) left every existing pick pointing at a row that no longer existed.
        // For a product still on the order that damage stayed hidden — the picked totals
        // re-associate by (OrderId, ProductId) — but a line REMOVED by the edit orphaned
        // its picks: the pallets stayed "picked" while belonging to no line, so the load
        // showed more picked pallets than its items accounted for (load 4761: 29 v 24).
        var existing = await _db.Set<DispatchOrderItemEntity>().Where(i => i.OrderId == id).ToListAsync(ct);
        var now = DateTime.Now;
        var kept = new HashSet<int>();
        foreach (var item in input.Items)
        {
            // Same product = same line: update in place so its picks stay attached.
            var line = existing.FirstOrDefault(e => e.ProductId == item.ProductId && !kept.Contains(e.Id));
            if (line != null)
            {
                line.Quantity = item.Quantity;
                line.Price = item.Price;
                kept.Add(line.Id);
                continue;
            }
            _db.Add(new DispatchOrderItemEntity
            {
                OrderId = id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                Price = item.Price,
                DateCreated = now,
            });
        }

        // Lines the edit dropped: unpick their pallets first (they go back on the yard —
        // stock isn't touched until dispatch, so there is nothing to restore) and free
        // the master pallet records, then remove the line itself.
        var dropped = existing.Where(e => !kept.Contains(e.Id)).ToList();
        if (dropped.Count > 0)
        {
            var droppedIds = dropped.Select(d => d.Id).ToList();
            var orphaned = await _db.Set<DispatchOrderPickedEntity>()
                .Where(p => p.DateDeleted == null && droppedIds.Contains(p.DispatchOrderItemId))
                .ToListAsync(ct);
            foreach (var p in orphaned)
            {
                p.DateDeleted = now;
                p.DeletedById = this.GetUserId();
            }
            var codes = orphaned.Select(p => p.PalletId).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            if (codes.Count > 0)
                await _db.Set<CodicoPalletEntity>()
                    .Where(c => c.UniqueCode != null && codes.Contains(c.UniqueCode) && c.PickedOrderId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.PickedOrderId, (int?)null), ct);
            _db.RemoveRange(dropped);
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = order.Id, orderNo = order.OrderNo });
    }

    // DELETE /api/dispatch/orders/{id} — soft delete (DateDeleted).
    [HttpDelete("orders/{id:int}")]
    public async Task<ActionResult> DeleteOrder(int id, CancellationToken ct = default)
    {
        var order = await _db.Set<DispatchOrderEntity>().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order == null) return NotFound();
        order.DateDeleted = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Loads / picking / weigh-out (WeighbridgeRecord) ----

    // GET /api/dispatch/loads/{id} — load detail: the record + its orders + items.
    [HttpGet("loads/{id:int}")]
    public async Task<ActionResult> LoadDetail(int id, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        var orders = await _db.Set<DispatchOrderRow>().AsNoTracking()
            .Where(o => o.WeighbridgeRecordId == id).OrderBy(o => o.SortOrder).ToListAsync(ct);
        var itemRows = await _db.Set<DispatchOrderItemRow>().AsNoTracking()
            .Where(i => i.WeighbridgeRecordId == id).OrderBy(i => i.OrderNo).ToListAsync(ct);
        // Pallets picked onto the load's orders (non-deleted), newest first, with the
        // order number and product description for display.
        var picked = await (
            from p in _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            join o in _db.Set<DispatchOrderEntity>().AsNoTracking() on p.DispatchOrderId equals o.Id
            join pr in _db.Set<Product>().AsNoTracking() on p.ProductId equals pr.Id into prs
            from pr in prs.DefaultIfEmpty()
            where p.DateDeleted == null && o.DateDeleted == null && o.WeighbridgeRecordId == id
            orderby p.DateCreated descending, p.Id descending
            select new
            {
                p.Id,
                OrderId = p.DispatchOrderId,
                o.OrderNo,
                p.ProductId,
                ProductDescription = pr != null ? pr.Description : null,
                Gtin = pr != null ? pr.Gtin : null,
                Size = pr != null ? pr.Size : null,
                Upp = pr != null ? (int?)pr.Upp : null,
                p.PalletId,
                p.Weight,
                p.DateCreated,
                p.IsNoScan,
                p.IsWeightEstimated,
            }
        ).ToListAsync(ct);
        // Photos taken for the load (bytes are streamed via GET /api/picking/photos/{id}).
        var photos = await _db.Set<LoadPhotoEntity>().AsNoTracking()
            .Where(p => p.WeighbridgeRecordId == id)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Description, p.DateCreated })
            .ToListAsync(ct);

        // Picking can only pick a product that IS on the order (PickingController returns
        // "notOnLoad" otherwise), so a pick with no order line means the line was removed
        // by an edit after it was picked — it was never shipped. UpdateOrder now unpicks
        // those, but historical loads still carry them (load 4761: 29 pallets against 24
        // on the lines). Show them rather than let the two tabs silently disagree; they
        // are also a tripwire if anything ever orphans a pick again.
        var onOrder = itemRows.Select(i => (i.OrderId, i.ProductId)).ToHashSet();
        var unordered = picked
            .Where(p => !onOrder.Contains((p.OrderId, p.ProductId)))
            .GroupBy(p => new { p.OrderId, p.OrderNo, p.ProductId, p.ProductDescription, p.Gtin, p.Size, p.Upp })
            .Select((g, n) => new
            {
                Id = -(n + 1), // synthetic — the grid keys on it; no DispatchOrderItem row exists
                g.Key.OrderId,
                g.Key.ProductId,
                ProductDescription = (string?)g.Key.ProductDescription,
                g.Key.Size,
                Upp = g.Key.Upp ?? 0,
                Quantity = (int?)null,
                TotalUnits = (int?)null,
                PickedQuantity = (int?)g.Count(),
                PickedWeight = g.Sum(p => p.Weight),
                Gtin = (string?)g.Key.Gtin,
                UnitDisplay = (string?)null,
                OrderNo = (int?)g.Key.OrderNo,
                WeighbridgeRecordId = (int?)id,
                IsUnordered = true,
                SoldBy = "Pallet",
                Density = (decimal?)null,
                PickedAt = (DateTime?)null,
            })
            .ToList();
        // Each order's POS board, listed after its lines (see PosRowsAsync).
        var posRows = await PosRowsAsync(o => o.WeighbridgeRecordId == id, ct);
        var items = itemRows
            .Select(i => new
            {
                i.Id,
                i.OrderId,
                i.ProductId,
                i.ProductDescription,
                i.Size,
                i.Upp,
                i.Quantity,
                i.TotalUnits,
                i.PickedQuantity,
                i.PickedWeight,
                i.Gtin,
                i.UnitDisplay,
                i.OrderNo,
                i.WeighbridgeRecordId,
                IsUnordered = false,
                i.SoldBy,
                i.Density,
                i.PickedAt,
            })
            .Concat(posRows.Select(i => new
            {
                i.Id,
                i.OrderId,
                i.ProductId,
                i.ProductDescription,
                i.Size,
                i.Upp,
                i.Quantity,
                i.TotalUnits,
                i.PickedQuantity,
                i.PickedWeight,
                i.Gtin,
                i.UnitDisplay,
                i.OrderNo,
                i.WeighbridgeRecordId,
                IsUnordered = false,
                i.SoldBy,
                i.Density,
                i.PickedAt,
            }))
            .Concat(unordered)
            .OrderBy(i => i.OrderNo)
            .ThenBy(i => i.IsUnordered) // an order's unordered picks sit under its real lines
            .ToList();
        // Every email sent for the load (ready for collection, dispatch confirmation), newest
        // first, with who sent it.
        var emails = await (
            from e in _db.Set<WeighbridgeRecordEmail>().AsNoTracking()
            join u in _db.GlobeweighUsers.AsNoTracking() on e.SentById equals (int?)u.Id into uj
            from u in uj.DefaultIfEmpty()
            where e.WeighbridgeRecordId == id
            orderby e.DateSent descending, e.Id descending
            select new
            {
                e.Id,
                e.Kind,
                e.DateSent,
                e.SentTo,
                e.Subject,
                SentBy = u != null ? (u.FirstName + " " + u.Surname).Trim() : null,
            }
        ).ToListAsync(ct);
        return Ok(new { record = rec, orders, items, picked, photos, emails });
    }

    // POST /api/dispatch/loads/{id}/photos/swap — swap the descriptions (slots) of two of
    // a load's photos: e.g. the "Trailer Number" shot was actually the "Side of Trailer"
    // one. The image files stay with their rows; only the labels exchange.
    [HttpPost("loads/{id:int}/photos/swap")]
    public async Task<ActionResult> SwapPhotos(
        int id, [FromBody] SwapPhotosInput input, CancellationToken ct = default)
    {
        var photos = await _db.Set<LoadPhotoEntity>()
            .Where(p => p.WeighbridgeRecordId == id
                && (p.Id == input.PhotoId || p.Id == input.OtherPhotoId))
            .ToListAsync(ct);
        if (input.PhotoId == input.OtherPhotoId || photos.Count != 2)
            return BadRequest("Select two different photos of this load.");

        (photos[0].Description, photos[1].Description) = (photos[1].Description, photos[0].Description);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/dispatch/loads — create a pre-load and link the selected orders.
    [HttpPost("loads")]
    public async Task<ActionResult> CreateLoad([FromBody] LoadInput input, CancellationToken ct = default)
    {
        if (input.HaulierId <= 0) return BadRequest(new { message = "Haulier is required." });
        var orders = input.OrdersWithDeliveryOrder();
        if (DeliveryOrderError(orders) is { } bad) return BadRequest(new { message = bad });

        var rec = new WeighbridgeRecord
        {
            IsOutgoing = true,
            HaulierId = input.HaulierId,
            HaulierName = await HaulierName(input.HaulierId, ct),
            CollectionDate = input.CollectionDate,
            TrailerNo = input.TrailerNo,
            Comments = input.Comments,
            WeightIn = 0,
            WeightOut = 0,
            DateCreated = DateTime.Now,
        };
        _db.Add(rec);
        await _db.SaveChangesAsync(ct);
        await LinkOrders(rec.Id, orders, ct);
        return Ok(new { id = rec.Id });
    }

    // PUT /api/dispatch/loads/{id} — update header + re-link orders.
    [HttpPut("loads/{id:int}")]
    public async Task<ActionResult> UpdateLoad(int id, [FromBody] LoadInput input, CancellationToken ct = default)
    {
        if (input.HaulierId <= 0) return BadRequest(new { message = "Haulier is required." });
        var orders = input.OrdersWithDeliveryOrder();
        if (DeliveryOrderError(orders) is { } bad) return BadRequest(new { message = bad });
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();

        rec.HaulierId = input.HaulierId;
        rec.HaulierName = await HaulierName(input.HaulierId, ct);
        rec.CollectionDate = input.CollectionDate;
        rec.TrailerNo = input.TrailerNo;
        rec.Comments = input.Comments;
        // The in-weight is only editable while the load is pending (weighed in, not out).
        if (input.WeightIn is { } weightIn && rec.DateWeightIn != null && rec.DateWeightOut == null)
            rec.WeightIn = weightIn;

        // Unlink only the orders taken OFF the load, then link the supplied set. Unlinking the
        // lot and relinking used to write "Load 5021 → (cleared)" and "(not set) → Load 5021" to
        // the trail of every order that merely stayed put, on every edit of the load's header.
        // An order that stays keeps its link and its place; EF records nothing for a value that
        // did not change.
        var keep = orders.Select(o => o.OrderId).ToHashSet();
        var dropped = await _db.Set<DispatchOrderEntity>()
            .Where(o => o.WeighbridgeRecordId == id && !keep.Contains(o.Id))
            .ToListAsync(ct);
        foreach (var o in dropped) o.WeighbridgeRecordId = 0;
        await _db.SaveChangesAsync(ct);
        await LinkOrders(id, orders, ct);
        return Ok(new { id });
    }

    // DELETE /api/dispatch/loads/{id} — unlink orders, then hard-delete the record.
    [HttpDelete("loads/{id:int}")]
    public async Task<ActionResult> DeleteLoad(int id, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        // Soft delete, as DeletePending does: a hard delete fails (500) on any load that has had
        // a "ready for collection" email sent, since WeighbridgeRecordEmail has an FK to it.
        // Its orders are unassigned so they go back to the open pool for another load.
        rec.DateDeleted = DateTime.Now;
        var linked = await _db.Set<DispatchOrderEntity>()
            .Where(o => o.WeighbridgeRecordId == id && o.DateDispatched == null)
            .ToListAsync(ct);
        foreach (var o in linked) o.WeighbridgeRecordId = 0;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/dispatch/loads/{id}/complete-picking — mark picking complete.
    [HttpPost("loads/{id:int}/complete-picking")]
    public async Task<ActionResult> CompletePicking(int id, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        rec.DatePickingComplete = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/dispatch/loads/{id}/weigh-in — record the inbound weight (PreLoad → Pending).
    [HttpPost("loads/{id:int}/weigh-in")]
    public async Task<ActionResult> WeighIn(int id, [FromBody] WeightInput input, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        rec.WeightIn = input.Weight;
        rec.DateWeightIn = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(input.TrailerNo)) rec.TrailerNo = input.TrailerNo;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/dispatch/outgoing — create a new outgoing record already weighed in (Pending).
    [HttpPost("outgoing")]
    public async Task<ActionResult> AddOutgoing([FromBody] OutgoingInput input, CancellationToken ct = default)
    {
        if (input.HaulierId <= 0) return BadRequest(new { message = "Haulier is required." });
        var rec = new WeighbridgeRecord
        {
            IsOutgoing = true,
            HaulierId = input.HaulierId,
            HaulierName = await HaulierName(input.HaulierId, ct),
            Comments = input.Comments,
            WeightIn = input.Weight,
            DateWeightIn = DateTime.Now,
            WeightOut = 0,
            DateCreated = DateTime.Now,
        };
        _db.Add(rec);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = rec.Id });
    }

    // POST /api/dispatch/pending/{id}/weigh-out — record outbound weight, dispatch the
    // linked orders and write stock history (Pending → Complete).
    [HttpPost("pending/{id:int}/weigh-out")]
    public async Task<ActionResult> WeighOut(int id, [FromBody] WeightInput input, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();

        rec.WeightOut = input.Weight;
        rec.DateWeightOut = DateTime.Now;
        if (rec.DateWeightIn == null) rec.DateWeightIn = DateTime.Now; // safety: never complete without an in-weight

        // Orders already dispatched (a load Un-completed and weighed out again) keep their
        // dispatch — depleting them a second time would write off pallets twice.
        var orders = await _db.Set<DispatchOrderEntity>()
            .Where(o => o.WeighbridgeRecordId == id && o.DateDispatched == null).ToListAsync(ct);
        var now = DateTime.Now;
        foreach (var order in orders)
        {
            order.DateDispatched = now;
            // Stock history + oldest-first pallet depletion (counts-only model).
            await StockDepletion.DispatchOrderAsync(_db, order.Id, now, ct);
        }

        // Queue the Sage sales orders exactly as the picking path does
        // (PickingController.CompleteLoad): a row in the same SaveChanges, never a call to Sage.
        if (SageExport.AutoExportEnabled(_config))
            await SageExport.EnqueueAsync(_db,
                orders.Where(o => o.DateDeleted == null).Select(o => o.Id),
                SageExportJob.Dispatch, now, ct);

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/dispatch/pending/{id}/linked-orders — the orders that would be freed by
    // deleting this record: still on it and not yet dispatched.
    [HttpGet("pending/{id:int}/linked-orders")]
    public async Task<ActionResult<IEnumerable<object>>> PendingLinkedOrders(int id, CancellationToken ct = default)
    {
        var rows = await _db.Set<DispatchOrderRow>().AsNoTracking()
            .Where(o => o.WeighbridgeRecordId == id && o.DateDeleted == null && o.DateDispatched == null)
            .OrderBy(o => o.SortOrder)
            .Select(o => new { o.Id, o.OrderNo, o.CustomerName })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // DELETE /api/dispatch/pending/{id} — soft delete a weighbridge record and release
    // its orders. Orders already dispatched stay linked (the load is their history);
    // the rest go back to unassigned so they can be put on another load.
    [HttpDelete("pending/{id:int}")]
    public async Task<ActionResult> DeletePending(int id, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        rec.DateDeleted = DateTime.Now;
        var linked = await _db.Set<DispatchOrderEntity>()
            .Where(o => o.WeighbridgeRecordId == id && o.DateDispatched == null)
            .ToListAsync(ct);
        foreach (var o in linked) o.WeighbridgeRecordId = 0;
        await _db.SaveChangesAsync(ct);
        return Ok(new { unassignedOrderNos = linked.Select(o => o.OrderNo).ToList() });
    }

    // POST /api/dispatch/pending/{id}/return-to-loads — "Remove from pending": undo the
    // weigh-in so the record steps back from Pending to the Loads (pre-load) grid. The
    // orders stay linked and any picking done so far is kept; only the in-weight goes.
    [HttpPost("pending/{id:int}/return-to-loads")]
    public async Task<ActionResult> ReturnPendingToLoads(int id, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id && r.DateDeleted == null, ct);
        if (rec == null) return NotFound();
        if (rec.DateWeightOut != null)
            return BadRequest(new { message = "This load has already been weighed out and cannot go back to Loads." });
        if (rec.DateWeightIn == null) return NoContent(); // already a pre-load
        rec.DateWeightIn = null;
        rec.WeightIn = 0;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/dispatch/completed-weighbridge/{id}/uncomplete — "Un-complete": clear the
    // out-weight so the record steps back from Completed to Pending. Only the weigh-out
    // date/time and weight go: the orders stay dispatched, stock stays depleted, photos and
    // queued Sage/print jobs are kept. Weighing it out again does not re-dispatch them
    // (both weigh-out paths skip orders already dispatched).
    [HttpPost("completed-weighbridge/{id:int}/uncomplete")]
    public async Task<ActionResult> UncompleteLoad(int id, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id && r.DateDeleted == null, ct);
        if (rec == null) return NotFound();
        if (rec.DateWeightOut == null) return NoContent(); // already pending
        rec.DateWeightOut = null;
        rec.WeightOut = 0;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // An open order still pointing at a load that has been deleted (here, in the WPF
    // admin, or by hand) would show as assigned forever and could never go on another
    // load. Free them whenever the open-orders grid is read.
    private async Task ReleaseOrphanedOrders(CancellationToken ct)
    {
        var stranded = await (
            from o in _db.Set<DispatchOrderEntity>()
            where o.WeighbridgeRecordId > 0 && o.DateDeleted == null && o.DateDispatched == null
                && !_db.Set<WeighbridgeRecord>().Any(r => r.Id == o.WeighbridgeRecordId && r.DateDeleted == null)
            select o
        ).ToListAsync(ct);
        if (stranded.Count == 0) return;
        foreach (var o in stranded) o.WeighbridgeRecordId = 0;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<string?> HaulierName(int haulierId, CancellationToken ct) =>
        await _db.Set<Haulier>().Where(h => h.Id == haulierId).Select(h => h.Name).FirstOrDefaultAsync(ct);

    // The delivery order (SortOrder) must be a complete 1..N sequence — one number per
    // order on the load, no repeats: 1 is dropped off first, N last.
    private static string? DeliveryOrderError(List<LoadOrderInput> orders)
    {
        if (orders.Select(o => o.OrderId).Distinct().Count() != orders.Count)
            return "An order can only appear once on a load.";
        var wanted = Enumerable.Range(1, orders.Count);
        if (!orders.Select(o => o.DeliveryOrder).OrderBy(n => n).SequenceEqual(wanted))
            return $"Delivery order must be a different number from 1 to {orders.Count} for each order.";
        return null;
    }

    private async Task LinkOrders(int recordId, List<LoadOrderInput> orders, CancellationToken ct)
    {
        if (orders.Count == 0) return;
        var byId = orders.ToDictionary(o => o.OrderId, o => o.DeliveryOrder);
        var ids = byId.Keys.ToList();
        var rows = await _db.Set<DispatchOrderEntity>().Where(o => ids.Contains(o.Id)).ToListAsync(ct);
        foreach (var o in rows)
        {
            o.WeighbridgeRecordId = recordId;
            o.SortOrder = byId[o.Id];
        }
        await _db.SaveChangesAsync(ct);
    }
}

public class LoadInput
{
    public int HaulierId { get; set; }
    public DateTime? CollectionDate { get; set; }
    public string? TrailerNo { get; set; }
    public string? Comments { get; set; }
    // Only sent when editing a pending load — a hand-corrected in-weight. Null leaves
    // the recorded weight alone.
    public int? WeightIn { get; set; }
    // Orders on the load, each with its delivery order (1 = delivered first). Stored in
    // DispatchOrder.SortOrder, which is what picking/touch already sequence by.
    public List<LoadOrderInput> Orders { get; set; } = new();
    // Legacy shape (no delivery order) — kept so an older client still saves a load;
    // the orders are then numbered in the sequence they were sent.
    public List<int> OrderIds { get; set; } = new();

    public List<LoadOrderInput> OrdersWithDeliveryOrder() =>
        Orders.Count > 0
            ? Orders
            : OrderIds.Distinct().Select((id, i) => new LoadOrderInput { OrderId = id, DeliveryOrder = i + 1 }).ToList();
}

public class LoadOrderInput
{
    public int OrderId { get; set; }
    public int DeliveryOrder { get; set; }
}

public class WeightInput
{
    public int Weight { get; set; }
    // Optional: set the trailer number along with the weight (used by the dev Book in helper).
    public string? TrailerNo { get; set; }
}

public class OutgoingInput
{
    public int HaulierId { get; set; }
    public int Weight { get; set; }
    public string? Comments { get; set; }
}

public class SwapPhotosInput
{
    public int PhotoId { get; set; }
    public int OtherPhotoId { get; set; }
}

public class DeliveryNoteDto
{
    // Which design the note is drawn in: "classic" (the original), "ledger" or "greenband" —
    // picked on Admin > Delivery Note (see DispatchController.NoteTemplate).
    public string Template { get; set; } = "classic";
    // The order confirmation rather than the delivery note: titled Order Confirmation, lines as
    // ordered (BuildDeliveryNote's confirmation).
    public bool Confirmation { get; set; }
    public int OrderId { get; set; }
    public int NoteNo { get; set; }
    public DateTime? Date { get; set; }
    public string? PoNumber { get; set; }
    public string? AccountRef { get; set; }
    public string? CustomerName { get; set; }
    public List<string> AddressLines { get; set; } = [];
    public string? AddressFull { get; set; }
    public string? Telephone { get; set; }
    public string? Comments { get; set; }
    public string? HaulierName { get; set; }
    public string? VehicleReg { get; set; }
    public string? TrailerNo { get; set; }
    public List<DeliveryNoteItemDto> Items { get; set; } = [];
}

public class DeliveryNoteItemDto
{
    public string? Description { get; set; }
    public int? Size { get; set; }
    public int Upp { get; set; }
    public int Pallets { get; set; }
    public int TotalBags { get; set; }
    public string SoldBy { get; set; } = ProductSoldBy.Pallet;
    public int CubicMeters { get; set; } // CubicMeter lines only: the m³ that went out
}

public class CommercialInvoiceDto
{
    // UK standard rate. Evergreen's DB holds no tax codes (Sage works VAT out on its side),
    // and every line on these invoices is standard-rated compost/growing media.
    public const decimal VatRate = 0.20m;

    // Same design choice as the delivery note (NoteTemplate): "classic", "ledger" or "greenband".
    public string Template { get; set; } = "classic";

    // The pre-commercial invoice: ordered pallets at average weights, not what was picked.
    public bool Estimate { get; set; }

    public int OrderId { get; set; }
    public int InvoiceNo { get; set; } // the load (WeighbridgeRecord) number; 0 = not on a load
    public int OrderNo { get; set; }
    public DateTime? Date { get; set; }
    public string? AccountRef { get; set; }
    public string? InvoiceName { get; set; }
    public List<string> InvoiceLines { get; set; } = [];
    public string? DeliverName { get; set; }
    public List<string> DeliverLines { get; set; } = [];
    public string? DeliverFull { get; set; }
    // kg, the goods only (never the lorry): gross = this order's calculated (picked) weight,
    // nett = that less each pallet's empty pallet and bags. Null until something is picked.
    public int? TotalGross { get; set; }
    public decimal? TotalNet { get; set; }
    public List<CommercialInvoiceItemDto> Items { get; set; } = [];
    // The same weights per HS tariff code (the Combined Gross / Nett Weight tables).
    public List<CommercialInvoiceWeightDto> WeightsByHsCode { get; set; } = [];
    // The order's products with no pallet weight or no bag weight set (Products), for the
    // preview's warning; not printed.
    public List<CommercialInvoiceMissingWeightDto> MissingWeights { get; set; } = [];
    public decimal Nett { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
}

// A product on the invoice whose packaging weights are not set up.
public class CommercialInvoiceMissingWeightDto
{
    public int ProductId { get; set; }
    public string? Description { get; set; }
    public int? Size { get; set; }
    public bool NoPalletWeight { get; set; }
    public bool NoBagWeight { get; set; }
}

// One HS tariff code's share of the order's weight: its lines' gross (whole kg) and nett.
public class CommercialInvoiceWeightDto
{
    public string HsCode { get; set; } = ""; // "" = lines whose product has no code
    public int Gross { get; set; }
    public decimal Nett { get; set; }
}

public class CommercialInvoiceItemDto
{
    public string? Description { get; set; }
    public int? Size { get; set; }
    public int Upp { get; set; }
    public int Pallets { get; set; }
    public string? HsCode { get; set; }
    public int Units { get; set; } // bags
    // The line's gross weight in whole kg: its picked pallets' calculated weights (estimates
    // included); null = nothing picked on it.
    public int? Weight { get; set; }
    // That less the packaging: per picked pallet, the empty pallet and UPP empty bags (kg, 2dp).
    public decimal? NettWeight { get; set; }
    public decimal? Rate { get; set; } // price per bag; null = unpriced line
    public decimal Nett { get; set; }
}

public class OrderInput
{
    public int CustomerId { get; set; }
    public int AddressId { get; set; } // 0 = customer has no addresses
    public int HaulierId { get; set; }
    public DateTime? DeliveryDate { get; set; } // null = ASAP
    public string? PoNumber { get; set; }
    public string? Comments { get; set; } // delivery-note comments
    public string? PickingComments { get; set; }
    public bool HasPos { get; set; } // a POS board goes with the order (not a product line)
    public int? SalesRepId { get; set; } // null = no rep; ignored for an order approved from sales
    public string? VehicleReg { get; set; }
    public string? TrailerNo { get; set; }
    // Only honoured on update, and only for a pre-hand-over open order (IsOrderNoEditable).
    // null = leave the number as-is; create always allocates its own.
    public int? OrderNo { get; set; }
    public List<OrderItemInput> Items { get; set; } = new();
}

public class OrderItemInput
{
    public int ProductId { get; set; }
    public int? Quantity { get; set; }
    public decimal? Price { get; set; }
}
