using System.Text.RegularExpressions;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Auditing;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

/// <summary>
/// Read endpoints for the Globeweigh.Picking app (Zebra ET40). Anonymous — the picking
/// handhelds have no login, exactly like the TauriTouch screens (see DeviceController).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/picking")]
public class PickingController : ControllerBase
{
    private readonly GbEvergreenContext _db;
    private readonly IConfiguration _config;
    private readonly AuditScope _audit;

    public PickingController(GbEvergreenContext db, IConfiguration config, AuditScope audit)
    {
        _db = db;
        _config = config;
        _audit = audit;
    }

    // The app has no login token, but it does know who is picking: the tablet's signed-in user
    // rides every write as UserId. Passing that on is what puts a name on the audit trail for a
    // change made from the app, rather than "not signed in". A 0 or unknown id leaves the scope
    // as the middleware set it.
    private async Task ActAsAsync(int? userId, CancellationToken ct)
    {
        if (userId is not > 0) return;
        var name = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => ((u.FirstName ?? "") + " " + (u.Surname ?? "")).Trim())
            .FirstOrDefaultAsync(ct);
        _audit.ActAs(userId.Value, string.IsNullOrEmpty(name) ? $"#{userId}" : name);
    }

    // Root folder for load photo files (e.g. C:\Globeweigh\LoadPhotos). Per-load
    // files live under {root}\{recordId}\{fileName}.
    private string PhotosRoot =>
        _config["Picking:PhotosPath"]
        ?? throw new InvalidOperationException("Picking:PhotosPath is not configured.");

    // GET /api/picking/pending — the dispatch "Pending" list: weighbridge records that
    // have been weighed in (DateWeightIn set) and are awaiting weigh-out. Same query the
    // TauriAdmin Orders/Dispatch "Pending" panel uses.
    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<PickingPendingDto>>> Pending(CancellationToken ct = default)
    {
        // Picking is only for outgoing loads (dispatches). Incoming records (IsOutgoing=false)
        // never appear on the pending pick list even once weighed in.
        var rows = await _db.Set<WeighbridgePendingRow>().AsNoTracking()
            .Where(r => r.DateWeightIn != null && r.IsOutgoing)
            .OrderByDescending(r => r.PendingWeighDate)
            .ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();

        // Total picks per load (sum of the order lines' pick targets).
        var items = await _db.Set<DispatchOrderItemRow>().AsNoTracking()
            .Where(i => i.WeighbridgeRecordId != null && ids.Contains((int)i.WeighbridgeRecordId))
            .Select(i => new { i.WeighbridgeRecordId, i.Quantity, i.SoldBy })
            .ToListAsync(ct);
        var totals = items
            .GroupBy(i => i.WeighbridgeRecordId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => PickTarget(x.SoldBy, x.Quantity)));

        // Pallets picked so far per load (non-deleted), mapped via the order to its load.
        var pickedLoads = await (
            from p in _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            join o in _db.Set<DispatchOrderEntity>().AsNoTracking() on p.DispatchOrderId equals o.Id
            where p.DateDeleted == null && o.DateDeleted == null && ids.Contains(o.WeighbridgeRecordId)
            select o.WeighbridgeRecordId
        ).ToListAsync(ct);
        var picked = pickedLoads.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        // A POS board is one more pick on its order (tapped, not scanned).
        var pos = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => o.DateDeleted == null && o.HasPos && ids.Contains(o.WeighbridgeRecordId))
            .Select(o => new { o.WeighbridgeRecordId, Picked = o.DatePosPicked != null })
            .ToListAsync(ct);
        foreach (var p in pos)
        {
            totals[p.WeighbridgeRecordId] = totals.GetValueOrDefault(p.WeighbridgeRecordId) + 1;
            if (p.Picked) picked[p.WeighbridgeRecordId] = picked.GetValueOrDefault(p.WeighbridgeRecordId) + 1;
        }

        // Which loads have had their trailer number confirmed by a picker. The pending
        // view predates the column, so read it off the record table directly.
        var confirmed = (await _db.Set<WeighbridgeRecord>().AsNoTracking()
                .Where(r => ids.Contains(r.Id) && r.DateTrailerConfirmed != null)
                .Select(r => r.Id)
                .ToListAsync(ct))
            .ToHashSet();

        return rows.Select(r => new PickingPendingDto(
            r.Id, r.IsOutgoing, r.PendingWeighDate, r.HaulierName,
            r.TrailerNo, r.WeightIn, r.OrderCount,
            picked.GetValueOrDefault(r.Id), totals.GetValueOrDefault(r.Id),
            confirmed.Contains(r.Id))).ToList();
    }

    // POST /api/picking/pending/{id}/confirm-trailer — the picker confirmed the trailer
    // number on the selected load. UserId is optional until the app has a login.
    [HttpPost("pending/{id:int}/confirm-trailer")]
    public async Task<ActionResult> ConfirmTrailer(
        int id, [FromBody] ConfirmTrailerRequest req, CancellationToken ct = default)
    {
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        await ActAsAsync(req.UserId, ct);

        if (rec.DateTrailerConfirmed == null)
        {
            rec.DateTrailerConfirmed = DateTime.Now;
            rec.TrailerConfirmedById = req.UserId;
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    // POST /api/picking/pending/{id}/trailer — the picker answered NO to the trailer number
    // and typed the one actually there. Only corrects the load (audited with the record); the
    // picker still confirms the new number with YES afterwards.
    [HttpPost("pending/{id:int}/trailer")]
    public async Task<ActionResult> ChangeTrailer(
        int id, [FromBody] ChangeTrailerRequest req, CancellationToken ct = default)
    {
        var trailerNo = req.TrailerNo?.Trim();
        if (string.IsNullOrEmpty(trailerNo))
            return BadRequest(new { message = "Enter a trailer number." });
        if (trailerNo.Length > 50)
            return BadRequest(new { message = "Trailer number is too long (50 characters max)." });

        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();
        await ActAsAsync(req.UserId, ct);

        rec.TrailerNo = trailerNo;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/picking/pending/{id}/detail — a pending load's orders (in pick order)
    // plus its totals: distinct products and total pallets across all order items.
    [HttpGet("pending/{id:int}/detail")]
    public async Task<ActionResult<PickingLoadDetailDto>> PendingDetail(
        int id, CancellationToken ct = default)
    {
        // Delivery order (DispatchOrder.SortOrder, set in Admin): 1 is dropped first.
        // PickingComments (the pickers' instructions) live on the table, not the view.
        var byDelivery = await (
            from o in _db.Set<DispatchOrderRow>().AsNoTracking()
            join e in _db.Set<DispatchOrderEntity>().AsNoTracking() on o.Id equals e.Id
            where o.WeighbridgeRecordId == id
            orderby o.SortOrder, o.Id
            select new
            {
                o.Id, o.OrderNo, o.CustomerName, o.PoNumber,
                o.PickedQuantity, o.TotalQuantityOrdered,
                e.PickingComments, e.DatePickingComplete, e.PickingCompletedEarly,
                e.HasPos, PosPicked = e.DatePosPicked != null,
            }
        ).ToListAsync(ct);

        // Loading order is the delivery order reversed — the last drop goes on the trailer
        // first, so the first drop comes off the back. Ranked by position rather than by the
        // stored number, which older loads saved 0-based (or not at all).
        var items = await _db.Set<DispatchOrderItemRow>().AsNoTracking()
            .Where(i => i.WeighbridgeRecordId == id)
            .Select(i => new { i.OrderId, i.ProductId, i.Quantity, i.SoldBy })
            .ToListAsync(ct);
        var totalProducts = items.Select(i => i.ProductId).Distinct().Count();
        var totalPallets = items.Sum(i => PickTarget(i.SoldBy, i.Quantity));
        // Per-order pick targets: the view's TotalQuantityOrdered counts a cubic meter line's
        // m³, but such a line is one pick (marked, never scanned).
        var targets = items.GroupBy(i => i.OrderId)
            .ToDictionary(g => g.Key, g => g.Sum(i => PickTarget(i.SoldBy, i.Quantity)));

        // A POS board is one more pick on its order.
        var orders = byDelivery
            .Select((o, i) => new PickingOrderDto(
                o.Id, o.OrderNo, o.CustomerName, o.PoNumber,
                o.PickedQuantity + (o.PosPicked ? 1 : 0),
                targets.GetValueOrDefault(o.Id) + (o.HasPos ? 1 : 0),
                byDelivery.Count - i, o.PickingComments,
                o.DatePickingComplete, o.PickingCompletedEarly))
            .OrderBy(o => o.LoadingOrder)
            .ToList();

        // Calculated weight: the picks on the load so far, so it climbs as picking
        // progresses. A pick with no measured weight (a no-scan label, or a code with no
        // pallet record) carries an estimate from the product's other pallets instead of 0
        // — the count of those goes back too, so the app can say the total is part-estimated
        // rather than showing it as gospel.
        var orderIds = await OrderIdsForLoad(id, ct);
        var picked = orderIds.Count == 0
            ? new List<PickedWeight>()
            : await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
                .Where(p => p.DateDeleted == null && orderIds.Contains(p.DispatchOrderId))
                .Select(p => new PickedWeight(p.Weight, p.IsNoScan, p.IsWeightEstimated))
                .ToListAsync(ct);

        return new PickingLoadDetailDto(
            totalProducts, totalPallets,
            picked.Sum(p => p.Weight), picked.Count(p => p.IsNoScan),
            picked.Count(p => p.IsWeightEstimated),
            orders,
            byDelivery.Any(o => o.HasPos));
    }

    // GET /api/picking/pending/{id}/picklist — the pallets to pick on a load, grouped by
    // product & order with a pallet count (rather than one row per individual pallet).
    [HttpGet("pending/{id:int}/picklist")]
    public async Task<ActionResult<IEnumerable<PickItemDto>>> PickList(
        int id, CancellationToken ct = default)
    {
        // Keyless view can't translate GroupBy/OrderBy in SQL — group in memory
        // (a load has only a handful of item lines).
        var items = await _db.Set<DispatchOrderItemRow>().AsNoTracking()
            .Where(i => i.WeighbridgeRecordId == id)
            .ToListAsync(ct);

        var lines = items
            .GroupBy(i => new { i.ProductId, i.ProductDescription, i.Gtin, i.OrderId, i.OrderNo, i.Size, i.Upp, i.SoldBy })
            .Select(g => new PickItemDto(
                g.Key.ProductId,
                g.Key.ProductDescription,
                g.Key.Gtin,
                g.Key.OrderId,
                g.Key.OrderNo ?? 0,
                // A cubic meter line is one pick (marked, not scanned); Quantity carries its m³.
                PickTarget(g.Key.SoldBy, g.Sum(x => x.Quantity ?? 0)),
                g.Key.Size,
                g.Key.Upp,
                g.Key.SoldBy,
                g.Sum(x => x.Quantity ?? 0)))
            .OrderBy(r => r.ProductDescription)
            .ThenBy(r => r.OrderNo)
            .ToList();

        // An order's POS board isn't an order line: it is listed as one more line, last on
        // its order, picked with a tap (pick/pos). ProductId 0 marks it.
        var posOrders = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => o.WeighbridgeRecordId == id && o.DateDeleted == null && o.HasPos)
            .Select(o => new { o.Id, o.OrderNo })
            .ToListAsync(ct);
        lines.AddRange(posOrders.Select(o =>
            new PickItemDto(PosProductId, "POS Board", null, o.Id, o.OrderNo, 1, SoldBy: PosSoldBy, Quantity: 1)));
        return lines;
    }

    // The POS board's stand-in "product" on the picklist / picked list.
    private const int PosProductId = 0;
    private const string PosSoldBy = "Pos";

    // GET /api/picking/pending/{id}/picked — pallets already picked for this load (the
    // persisted DispatchOrder_Picked rows). Lets the app restore progress on (re)load and
    // gives the per-product detail page its scanned-pallet list. Newest first.
    [HttpGet("pending/{id:int}/picked")]
    public async Task<ActionResult<IEnumerable<PickedPalletDto>>> Picked(
        int id, CancellationToken ct = default)
    {
        var orderIds = await OrderIdsForLoad(id, ct);
        if (orderIds.Count == 0) return new List<PickedPalletDto>();

        var picked = await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            .Where(p => p.DateDeleted == null && orderIds.Contains(p.DispatchOrderId))
            .OrderByDescending(p => p.DateCreated).ThenByDescending(p => p.Id)
            .Select(p => new PickedPalletDto(
                p.Id, p.DispatchOrderId, p.ProductId, p.PalletId, p.Weight,
                p.DateCreated, p.IsNoScan, p.IsWeightEstimated))
            .ToListAsync(ct);

        // A picked POS board (no pick row; the order's DatePosPicked) against its stand-in line.
        var pos = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => orderIds.Contains(o.Id) && o.HasPos && o.DatePosPicked != null)
            .Select(o => new { o.Id, o.DatePosPicked })
            .ToListAsync(ct);
        picked.AddRange(pos.Select(o =>
            new PickedPalletDto(0, o.Id, PosProductId, "", 0m, o.DatePosPicked!.Value, false)));
        return picked;
    }

    // POST /api/picking/pick — record one scanned pallet against the load. Matches the
    // barcode's product code to an order line by GTIN, then inserts a DispatchOrder_Picked
    // row (PalletId = the pallet UniqueCode). The DB is authoritative for matching and for
    // duplicate detection, so progress is consistent across app restarts and devices.
    //
    // Labels aren't always printed correctly yet, so an unknown UniqueCode (no CodicoPallet
    // match) is still recorded — with PalletKnown=false and an estimated weight in place of
    // the measured one it doesn't have (see EstimatePalletWeight) — never dropped.
    [HttpPost("pick")]
    public async Task<ActionResult<PickResultDto>> Pick(
        [FromBody] PickRequest req, CancellationToken ct = default)
    {
        await ActAsAsync(req.UserId, ct);
        var prodCode = (req.ProdCode ?? "").Trim();
        var uniqueCode = (req.UniqueCode ?? "").Trim();
        if (prodCode.Length == 0 || uniqueCode.Length == 0)
            return PickResultDto.Of("unreadable");

        var orderIds = await OrderIdsForLoad(req.RecordId, ct);
        if (orderIds.Count == 0) return PickResultDto.Of("notOnLoad");

        // The picker works one order at a time: the app sends the order it has selected and
        // the pallet may only land on that one. OrderId 0 keeps the old load-wide behaviour
        // for any client that doesn't send it.
        //
        // A completed order takes nothing more. Completion is always a deliberate act — the
        // picker ends the order from the app, whether it went short ("Complete early") or
        // was picked in full — and it is undone by reopening the order. Up until then an
        // order stays open even at 100%, so a line can still be over-picked (the app asks
        // first). PickingCompletedEarly records which of the two it was, for reporting.
        if (req.OrderId > 0)
        {
            if (!orderIds.Contains(req.OrderId)) return PickResultDto.Of("notOnLoad");
            var selected = await _db.Set<DispatchOrderEntity>().AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct);
            if (selected?.DatePickingComplete != null)
                return PickResultDto.Of("orderComplete") with
                {
                    OrderId = selected.Id, OrderNo = selected.OrderNo, OrderComplete = true,
                };
        }

        // Master pallet records for this UniqueCode. The 3rd party sometimes duplicates a
        // UniqueCode (2+ rows, same code, different id) and prints that many physical labels,
        // so we allow that pallet to be picked once per master record. An unknown code (0
        // rows — e.g. a wrongly-formatted label) is still allowed once and recorded.
        var masters = await _db.Set<CodicoPalletEntity>().AsNoTracking()
            .Where(c => c.UniqueCode == uniqueCode)
            .Select(c => new { c.Weight })
            .ToListAsync(ct);
        var allowed = Math.Max(1, masters.Count);

        // How many times this pallet is already picked (non-deleted) onto this load.
        var alreadyPicked = await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            .CountAsync(p => p.DateDeleted == null
                && p.PalletId == uniqueCode
                && orderIds.Contains(p.DispatchOrderId), ct);
        if (alreadyPicked >= allowed)
            return PickResultDto.Of("duplicate") with { PalletId = uniqueCode };
        var wasDuplicate = alreadyPicked > 0; // an allowed repeat of a duplicated UniqueCode

        // Candidate order lines for this product (GTIN match, leading zeros ignored).
        var want = Norm(prodCode);
        var onLoad = (await _db.Set<DispatchOrderItemRow>().AsNoTracking()
                .Where(i => i.WeighbridgeRecordId == req.RecordId)
                .ToListAsync(ct))
            .Where(i => Norm(i.Gtin) == want)
            .OrderBy(i => i.OrderNo).ThenBy(i => i.Id)
            .ToList();
        if (onLoad.Count == 0)
            return PickResultDto.Of("notOnLoad") with { ProductCode = prodCode };

        // Narrow to the selected order. The product being elsewhere on the load is its own
        // answer ("notOnOrder") — the picker has the right pallet but the wrong order open.
        var lines = req.OrderId > 0
            ? onLoad.Where(i => i.OrderId == req.OrderId).ToList()
            : onLoad;
        if (lines.Count == 0)
        {
            var f = onLoad[0];
            return PickResultDto.Of("notOnOrder") with
            {
                ProductCode = prodCode,
                ProductId = f.ProductId,
                ProductDescription = f.ProductDescription,
                PalletId = uniqueCode,
            };
        }

        // Picked counts per item line so far (non-deleted), to find the first incomplete line.
        var itemIds = lines.Select(l => l.Id).ToList();
        var pickedByItem = (await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
                .Where(p => p.DateDeleted == null && itemIds.Contains(p.DispatchOrderItemId))
                .GroupBy(p => p.DispatchOrderItemId)
                .Select(g => new { ItemId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.ItemId, x => x.Count);

        DispatchOrderItemRow? line = null;
        foreach (var l in lines)
        {
            pickedByItem.TryGetValue(l.Id, out var done);
            if (done < (l.Quantity ?? 0)) { line = l; break; }
        }
        // Every line for this product on the order is already full. The picker is asked
        // whether to over-pick it and, if they confirm, the app repeats the scan with
        // AllowOverPick — the extra pallet then goes onto the first of those lines and the
        // line reads over its ordered count (4 / 3), which is the record of what happened.
        var overPick = false;
        if (line == null)
        {
            var f = lines[0];
            if (!req.AllowOverPick)
            {
                return new PickResultDto("alreadyComplete", f.OrderId, f.OrderNo, f.ProductId,
                    f.ProductDescription, uniqueCode, prodCode, 0m, null,
                    f.Quantity ?? 0, f.Quantity ?? 0, false);
            }
            line = f;
            overPick = true;
        }

        // The pallet's own weight when CodicoPallet has it. Otherwise (unknown code, or a
        // master row holding 0) the best estimate, flagged as such — a 0 here would
        // understate the order's weight on the delivery note and in Sage.
        var weight = masters.Select(m => m.Weight).FirstOrDefault(w => w > 0);
        var weightEstimated = weight <= 0;
        if (weightEstimated) weight = await EstimatePalletWeight(line, ct);

        var now = DateTime.Now;
        var row = new DispatchOrderPickedEntity
        {
            DispatchOrderId = line.OrderId,
            DispatchOrderItemId = line.Id,
            PalletId = uniqueCode,
            ProductId = line.ProductId,
            Quantity = 1,
            Weight = weight,
            DateCreated = now,
            CreatedById = 0,
            DateDeleted = null,
            DeletedById = 0,
            IsNoScan = false,
            IsWeightEstimated = weightEstimated,
        };
        _db.Set<DispatchOrderPickedEntity>().Add(row);
        await _db.SaveChangesAsync(ct);

        // Link the master record(s) for this UniqueCode to the order. For a duplicated code
        // this marks BOTH rows as picked — so reporting can tell exactly what was dispatched.
        if (masters.Count > 0)
        {
            await _db.Set<CodicoPalletEntity>()
                .Where(c => c.UniqueCode == uniqueCode && c.PickedOrderId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PickedOrderId, line.OrderId), ct);
        }

        pickedByItem.TryGetValue(line.Id, out var prev);
        return new PickResultDto("ok", line.OrderId, line.OrderNo, line.ProductId,
            line.ProductDescription, uniqueCode, prodCode, weight, now,
            prev + 1, line.Quantity ?? 0, masters.Count > 0, wasDuplicate, row.Id,
            false, overPick, weightEstimated);
    }

    // POST /api/picking/pick/noscan — manually count a pallet whose label wouldn't scan
    // (not in the expected 2D DataMatrix format). Records a DispatchOrder_Picked row flagged
    // IsNoScan against the chosen order line so the order can still be completed, and so the
    // exception can be reported on later. No UniqueCode and no CodicoPallet link; the weight
    // is the best estimate from the product's other pallets (flagged IsWeightEstimated), so
    // the order's totals stay close to true instead of reading 0 for this pallet.
    [HttpPost("pick/noscan")]
    public async Task<ActionResult<PickResultDto>> NoScan(
        [FromBody] NoScanRequest req, CancellationToken ct = default)
    {
        await ActAsAsync(req.UserId, ct);
        var orderIds = await OrderIdsForLoad(req.RecordId, ct);
        if (orderIds.Count == 0) return PickResultDto.Of("notOnLoad");

        // Same rule as a scan: nothing more goes onto a completed order.
        var order = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct);
        if (order?.DatePickingComplete != null)
            return PickResultDto.Of("orderComplete") with
            {
                OrderId = order.Id, OrderNo = order.OrderNo, OrderComplete = true,
            };

        var line = (await _db.Set<DispatchOrderItemRow>().AsNoTracking()
                .Where(i => i.WeighbridgeRecordId == req.RecordId
                    && i.OrderId == req.OrderId && i.ProductId == req.ProductId)
                .ToListAsync(ct))
            .OrderBy(i => i.Id)
            .FirstOrDefault();
        if (line == null) return PickResultDto.Of("notOnLoad");
        // A cubic meter line has no pallets: it is marked picked (pick/mark), never labelled.
        if (line.SoldBy == ProductSoldBy.CubicMeter) return PickResultDto.Of("cubicMeter");

        // A full line takes another label only once the picker has confirmed the over-pick,
        // exactly as a scan does.
        var done = await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            .CountAsync(p => p.DateDeleted == null && p.DispatchOrderItemId == line.Id, ct);
        var overPick = done >= (line.Quantity ?? 0);
        if (overPick && !req.AllowOverPick)
            return new PickResultDto("alreadyComplete", line.OrderId, line.OrderNo,
                line.ProductId, line.ProductDescription, "", "", 0m, null,
                line.Quantity ?? 0, line.Quantity ?? 0, false);

        var weight = await EstimatePalletWeight(line, ct);
        var now = DateTime.Now;
        var row = new DispatchOrderPickedEntity
        {
            DispatchOrderId = line.OrderId,
            DispatchOrderItemId = line.Id,
            PalletId = "",
            ProductId = line.ProductId,
            Quantity = 1,
            Weight = weight,
            DateCreated = now,
            CreatedById = 0,
            DateDeleted = null,
            DeletedById = 0,
            IsNoScan = true,
            IsWeightEstimated = true,
        };
        _db.Set<DispatchOrderPickedEntity>().Add(row);
        await _db.SaveChangesAsync(ct);

        return new PickResultDto("ok", line.OrderId, line.OrderNo, line.ProductId,
            line.ProductDescription, "", "", weight, now, done + 1, line.Quantity ?? 0,
            false, false, row.Id, false, overPick, true);
    }

    // POST /api/picking/pick/mark — mark a cubic meter line (raw peat, sold by volume) as
    // picked. Nothing is scanned and there are no pallets: the line is one pick, recorded as
    // a single pallet-less 0 kg row so every picked-count rule (line full, order full,
    // complete early, load progress) treats it like any other line. Undone through
    // pick/delete with the returned PickedId.
    // The picker enters the load's density (kg/m³) first; it is saved on the order line
    // (DispatchOrderItem.Density) in the same save as the pick.
    [HttpPost("pick/mark")]
    public async Task<ActionResult<PickResultDto>> MarkPicked(
        [FromBody] MarkPickedRequest req, CancellationToken ct = default)
    {
        if (req.Density is not > 0 || req.Density > 9999999.99m)
            return BadRequest(new { message = "Enter a density greater than zero." });
        await ActAsAsync(req.UserId, ct);
        var order = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == req.OrderId && o.WeighbridgeRecordId == req.RecordId
                && o.DateDeleted == null, ct);
        if (order == null) return PickResultDto.Of("notOnLoad");
        if (order.DatePickingComplete != null)
            return PickResultDto.Of("orderComplete") with
            {
                OrderId = order.Id, OrderNo = order.OrderNo, OrderComplete = true,
            };

        var line = (await _db.Set<DispatchOrderItemRow>().AsNoTracking()
                .Where(i => i.OrderId == req.OrderId && i.ProductId == req.ProductId)
                .ToListAsync(ct))
            .OrderBy(i => i.Id)
            .FirstOrDefault();
        if (line == null) return PickResultDto.Of("notOnLoad");
        if (line.SoldBy != ProductSoldBy.CubicMeter) return PickResultDto.Of("notCubicMeter");

        var existing = await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            .Where(p => p.DateDeleted == null && p.DispatchOrderId == line.OrderId && p.ProductId == line.ProductId)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (existing != null)
            return new PickResultDto("alreadyComplete", line.OrderId, line.OrderNo,
                line.ProductId, line.ProductDescription, "", "", 0m, null, 1, 1, false,
                false, existing.Value);

        var now = DateTime.Now;
        var row = new DispatchOrderPickedEntity
        {
            DispatchOrderId = line.OrderId,
            DispatchOrderItemId = line.Id,
            PalletId = "",
            ProductId = line.ProductId,
            Quantity = 1,
            Weight = 0,
            DateCreated = now,
            CreatedById = 0,
            DateDeleted = null,
            DeletedById = 0,
            IsNoScan = false,
            IsWeightEstimated = false,
        };
        _db.Set<DispatchOrderPickedEntity>().Add(row);
        var item = await _db.Set<DispatchOrderItemEntity>().FirstAsync(i => i.Id == line.Id, ct);
        item.Density = decimal.Round(req.Density!.Value, 2);
        await _db.SaveChangesAsync(ct);

        return new PickResultDto("ok", line.OrderId, line.OrderNo, line.ProductId,
            line.ProductDescription, "", "", 0m, now, 1, 1, false, false, row.Id);
    }

    // POST /api/picking/pick/pos — the picker tapped the order's POS board picked. The board
    // is not an order line (DispatchOrder.HasPos), so it is recorded on the order.
    // POST /api/picking/pick/pos/undo — tapped back off again.
    [HttpPost("pick/pos")]
    public Task<ActionResult<PickResultDto>> PickPos(
        [FromBody] PosPickRequest req, CancellationToken ct = default) => SetPosPicked(req, true, ct);

    [HttpPost("pick/pos/undo")]
    public Task<ActionResult<PickResultDto>> UndoPickPos(
        [FromBody] PosPickRequest req, CancellationToken ct = default) => SetPosPicked(req, false, ct);

    private async Task<ActionResult<PickResultDto>> SetPosPicked(PosPickRequest req, bool picked, CancellationToken ct)
    {
        await ActAsAsync(req.UserId, ct);
        var order = await _db.Set<DispatchOrderEntity>()
            .FirstOrDefaultAsync(o => o.Id == req.OrderId && o.WeighbridgeRecordId == req.RecordId
                && o.DateDeleted == null && o.HasPos, ct);
        if (order == null) return PickResultDto.Of("notOnLoad");
        if (order.DatePickingComplete != null)
            return PickResultDto.Of("orderComplete") with
            {
                OrderId = order.Id, OrderNo = order.OrderNo, OrderComplete = true,
            };

        if (picked && order.DatePosPicked == null)
        {
            order.DatePosPicked = DateTime.Now;
            order.PosPickedById = req.UserId;
        }
        else if (!picked)
        {
            order.DatePosPicked = null;
            order.PosPickedById = null;
        }
        await _db.SaveChangesAsync(ct);

        return new PickResultDto("ok", order.Id, order.OrderNo, PosProductId, "POS Board", "", "",
            0m, order.DatePosPicked, picked ? 1 : 0, 1, false);
    }

    // A line's pick target: its pallet count, or 1 for a cubic meter line (marked picked once,
    // whatever its m³).
    private static int PickTarget(string? soldBy, int? quantity) =>
        soldBy == ProductSoldBy.CubicMeter ? (quantity is > 0 ? 1 : 0) : quantity ?? 0;

    // POST /api/picking/pending/{id}/orders/{orderId}/complete-picking — the picker ends the
    // order. Every order is finished by hand: with all its pallets on ("Complete order") or
    // with some outstanding ("Complete early" — short shipment, stock ran out, ...), which is
    // flagged so the shortfall can be reported on. Either way it frees the picker to start
    // the next order. An already-complete order is left as it is.
    [HttpPost("pending/{id:int}/orders/{orderId:int}/complete-picking")]
    public async Task<ActionResult<PickingOrderCompleteDto>> CompleteOrderPicking(
        int id, int orderId, [FromBody] CompleteOrderRequest req, CancellationToken ct = default)
    {
        var order = await _db.Set<DispatchOrderEntity>()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.WeighbridgeRecordId == id
                && o.DateDeleted == null, ct);
        if (order == null) return NotFound();
        await ActAsAsync(req.UserId, ct);

        if (order.DatePickingComplete == null)
        {
            var target = (await _db.Set<DispatchOrderItemRow>().AsNoTracking()
                    .Where(i => i.OrderId == orderId)
                    .Select(i => new { i.Quantity, i.SoldBy })
                    .ToListAsync(ct))
                .Sum(i => PickTarget(i.SoldBy, i.Quantity));
            var picked = await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
                .CountAsync(p => p.DateDeleted == null && p.DispatchOrderId == orderId, ct);
            // The POS board is one more pick (tapped, not a pick row).
            if (order.HasPos) target++;
            if (order.HasPos && order.DatePosPicked != null) picked++;

            order.DatePickingComplete = DateTime.Now;
            order.PickingCompletedById = req.UserId;
            // "Early" means it went out short; a full (or over-picked) order is the
            // ordinary ending.
            order.PickingCompletedEarly = picked < target;
            await _db.SaveChangesAsync(ct);
        }

        return new PickingOrderCompleteDto(
            order.Id, order.OrderNo, order.DatePickingComplete, order.PickingCompletedEarly);
    }

    // POST /api/picking/pending/{id}/orders/{orderId}/reopen-picking — undo a completion so
    // the order takes pallets again. The picker reaches this from the "Order completed"
    // overlay: an order finished early because stock ran out is reopened when it turns up,
    // and a fully picked one is reopened to correct a pallet or add another.
    [HttpPost("pending/{id:int}/orders/{orderId:int}/reopen-picking")]
    public async Task<ActionResult<PickingOrderCompleteDto>> ReopenOrderPicking(
        int id, int orderId,
        // Optional, and absent from app builds before 1.0.15: a reopen from one of those is
        // recorded with no user, the way every picking write used to be.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReopenOrderRequest? req = null,
        CancellationToken ct = default)
    {
        var order = await _db.Set<DispatchOrderEntity>()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.WeighbridgeRecordId == id
                && o.DateDeleted == null, ct);
        if (order == null) return NotFound();
        await ActAsAsync(req?.UserId, ct);

        order.DatePickingComplete = null;
        order.PickingCompletedById = null;
        order.PickingCompletedEarly = false;
        await _db.SaveChangesAsync(ct);

        return new PickingOrderCompleteDto(
            order.Id, order.OrderNo, order.DatePickingComplete, order.PickingCompletedEarly);
    }

    // POST /api/picking/pick/delete — remove one already-picked pallet from a product line.
    // The picker re-scans the pallet on the product detail page and confirms (or, for a
    // no-scan label — which has no code to scan — taps it in the list); the pick row is
    // soft-deleted (DateDeleted) rather than removed, so the pick history is kept.
    //
    // PickedId targets one exact row and is what the app sends; the UniqueCode fallback
    // (most recent pick of that code on the line) covers a client that doesn't have the id.
    [HttpPost("pick/delete")]
    public async Task<ActionResult<PickResultDto>> DeletePick(
        [FromBody] DeletePickRequest req, CancellationToken ct = default)
    {
        var uniqueCode = (req.UniqueCode ?? "").Trim();
        if (uniqueCode.Length == 0 && req.PickedId <= 0)
            return PickResultDto.Of("unreadable");

        var orderIds = await OrderIdsForLoad(req.RecordId, ct);
        if (!orderIds.Contains(req.OrderId)) return PickResultDto.Of("notFound");

        var pick = req.PickedId > 0
            ? await _db.Set<DispatchOrderPickedEntity>()
                .FirstOrDefaultAsync(p => p.Id == req.PickedId
                    && p.DateDeleted == null
                    && p.DispatchOrderId == req.OrderId
                    && p.ProductId == req.ProductId, ct)
            // Where a duplicated UniqueCode was picked more than once, remove the most recent.
            : await _db.Set<DispatchOrderPickedEntity>()
                .Where(p => p.DateDeleted == null
                    && p.PalletId == uniqueCode
                    && p.DispatchOrderId == req.OrderId
                    && p.ProductId == req.ProductId)
                .OrderByDescending(p => p.DateCreated).ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync(ct);
        if (pick == null)
            return PickResultDto.Of("notFound") with { PalletId = uniqueCode };
        uniqueCode = pick.PalletId; // a no-scan row has none

        var orderId = pick.DispatchOrderId;
        var itemId = pick.DispatchOrderItemId;
        pick.DateDeleted = DateTime.Now;
        pick.DeletedById = 0;
        await _db.SaveChangesAsync(ct);

        // Unlink the master pallet record(s) once the order holds no pick for this code, so
        // the pallet is free to be picked again (here or onto another order). A no-scan row
        // has no code and never linked a master record, so there's nothing to unlink.
        var stillPicked = uniqueCode.Length == 0 || await _db.Set<DispatchOrderPickedEntity>()
            .AsNoTracking()
            .AnyAsync(p => p.DateDeleted == null
                && p.PalletId == uniqueCode && p.DispatchOrderId == orderId, ct);
        if (!stillPicked)
            await _db.Set<CodicoPalletEntity>()
                .Where(c => c.UniqueCode == uniqueCode && c.PickedOrderId == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.PickedOrderId, (int?)null), ct);

        var line = await _db.Set<DispatchOrderItemRow>().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == itemId, ct);
        var remaining = await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            .CountAsync(p => p.DateDeleted == null && p.DispatchOrderItemId == itemId, ct);

        return new PickResultDto("ok", orderId, line?.OrderNo, pick.ProductId,
            line?.ProductDescription, uniqueCode, line?.Gtin, pick.Weight, null,
            remaining, line?.Quantity ?? 0, false);
    }

    // POST /api/picking/pending/{id}/photos — upload one "Complete Load" photo (multipart:
    // file + description). Stored as a file on disk ({PhotosRoot}\{recordId}\{guid}.ext);
    // only metadata goes in SQL. Re-uploading the same description for a load replaces the
    // previous photo, so a retried/repeated complete never accumulates duplicates.
    [HttpPost("pending/{id:int}/photos")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(15_000_000)] // ~15 MB; client compresses well below this
    public async Task<ActionResult<LoadPhotoDto>> UploadLoadPhoto(
        int id, IFormFile file, [FromForm] string? description, CancellationToken ct = default)
    {
        if (id <= 0) return BadRequest("Invalid record id");
        if (file == null || file.Length == 0) return BadRequest("No file");
        var desc = (description ?? "").Trim();
        if (desc.Length == 0) return BadRequest("Missing description");

        var contentType = file.ContentType ?? "application/octet-stream";
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only image files are allowed");

        // Map the content type to an extension; default to .jpg.
        var ext = contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg",
        };

        var loadFolder = Path.Combine(PhotosRoot, id.ToString());
        Directory.CreateDirectory(loadFolder);

        var fileName = Guid.NewGuid().ToString("N") + ext;
        var fullPath = Path.Combine(loadFolder, fileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        try
        {
            // Replace any previous photo with this description for this load.
            var previous = await _db.Set<LoadPhotoEntity>()
                .Where(p => p.WeighbridgeRecordId == id && p.Description == desc)
                .ToListAsync(ct);
            _db.Set<LoadPhotoEntity>().RemoveRange(previous);

            var photo = new LoadPhotoEntity
            {
                WeighbridgeRecordId = id,
                Description = desc,
                FileName = fileName,
                ContentType = contentType,
                DateCreated = DateTime.Now,
            };
            _db.Set<LoadPhotoEntity>().Add(photo);
            await _db.SaveChangesAsync(ct);

            foreach (var old in previous)
            {
                var oldPath = Path.Combine(loadFolder, old.FileName);
                try { System.IO.File.Delete(oldPath); } catch { /* ignore */ }
            }

            return Ok(new LoadPhotoDto(
                photo.Id, photo.WeighbridgeRecordId, photo.Description, photo.DateCreated));
        }
        catch
        {
            // Roll back the orphaned file if the DB insert fails.
            try { System.IO.File.Delete(fullPath); } catch { /* ignore */ }
            throw;
        }
    }

    // GET /api/picking/pending/{id}/photos — the photos already taken for a load, so the
    // app can show which of the required shots exist.
    [HttpGet("pending/{id:int}/photos")]
    public async Task<ActionResult<IEnumerable<LoadPhotoDto>>> LoadPhotos(
        int id, CancellationToken ct = default) =>
        await _db.Set<LoadPhotoEntity>().AsNoTracking()
            .Where(p => p.WeighbridgeRecordId == id)
            .OrderBy(p => p.Id)
            .Select(p => new LoadPhotoDto(p.Id, p.WeighbridgeRecordId, p.Description, p.DateCreated))
            .ToListAsync(ct);

    // GET /api/picking/photos/{photoId} — stream a photo's bytes (for viewing).
    [HttpGet("photos/{photoId:int}")]
    public async Task<IActionResult> LoadPhoto(int photoId, CancellationToken ct = default)
    {
        var photo = await _db.Set<LoadPhotoEntity>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo == null) return NotFound("Photo not found");

        var fullPath = Path.Combine(
            PhotosRoot, photo.WeighbridgeRecordId.ToString(), photo.FileName);
        if (!System.IO.File.Exists(fullPath)) return NotFound("File missing");

        var stream = System.IO.File.OpenRead(fullPath);
        return File(stream, photo.ContentType ?? "application/octet-stream");
    }

    // POST /api/picking/pending/{id}/complete — capture the outbound weight and complete
    // the load. Refused (409) while any order on it is still being picked. Mirrors DispatchController.WeighOut (Pending → Complete): records the
    // out-weight, dispatches the linked orders and writes stock history. Anonymous like
    // the rest of this controller, since the picking handhelds have no login.
    [HttpPost("pending/{id:int}/complete")]
    public async Task<ActionResult> CompleteLoad(
        int id, [FromBody] CompleteLoadRequest req, CancellationToken ct = default)
    {
        await ActAsAsync(req.UserId, ct);
        var rec = await _db.Set<WeighbridgeRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec == null) return NotFound();

        // Every order on the load has to have been finished by its picker first. The app
        // greys out its Complete button until then; this is the backstop, and it matters
        // because completing dispatches the orders and writes stock history.
        var open = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => o.WeighbridgeRecordId == id && o.DateDeleted == null
                && o.DatePickingComplete == null)
            .Select(o => o.OrderNo)
            .ToListAsync(ct);
        if (open.Count > 0)
        {
            var list = string.Join(", ", open.Select(n => $"#{n}"));
            return Conflict(new
            {
                message = open.Count == 1
                    ? $"Order {list} has not been completed yet."
                    : $"These orders have not been completed yet: {list}.",
            });
        }

        rec.WeightOut = req.Weight;
        rec.DateWeightOut = DateTime.Now;
        if (rec.DateWeightIn == null) rec.DateWeightIn = DateTime.Now; // safety: never complete without an in-weight

        // Only orders not yet dispatched: one that already is keeps its dispatch, since
        // depleting it a second time would write off pallets twice. (Admin's Un-complete
        // undispatches the load's orders, so a re-complete normally dispatches them all again.)
        var orders = await _db.Set<DispatchOrderEntity>()
            .Where(o => o.WeighbridgeRecordId == id && o.DateDispatched == null).ToListAsync(ct);
        var now = DateTime.Now;
        foreach (var order in orders)
        {
            order.DateDispatched = now;
            // Stock history + oldest-first pallet depletion (counts-only model).
            await StockDepletion.DispatchOrderAsync(_db, order.Id, now, ct);
        }

        var queued = await QueueDeliveryNotes(id, orders, ct);

        // Sales orders for Sage, when the integration is switched on (Sage:ExportEnabled).
        // Same SaveChanges and the same reasoning as the delivery notes: this queues a row
        // and never talks to Sage, so a Sage problem cannot fail a load completion. The
        // sidecar on the Sage machine does the posting (see Data/Entities/SageExportJob.cs).
        var sageQueued = SageExport.AutoExportEnabled(_config)
            ? await SageExport.EnqueueAsync(_db,
                orders.Where(o => o.DateDeleted == null).Select(o => o.Id),
                SageExportJob.Dispatch, now, ct)
            : new List<int>();

        await _db.SaveChangesAsync(ct);

        // Tell the app what actually happened so it can say so, rather than assert that
        // notes were sent and be wrong (e.g. on a re-completed load, where the filtered
        // unique index means nothing new is queued). Was 204; a 200 with a body is
        // backward-compatible — an older Picking build just ignores it.
        return Ok(new CompleteLoadResultDto(
            OrdersDispatched: orders.Count(o => o.DateDeleted == null),
            DeliveryNotesQueued: queued.Count,
            Copies: queued.Copies,
            SageExportsQueued: sageQueued.Count));
    }

    // Queue the delivery notes that print automatically on load completion — one job per
    // non-deleted order on the load, picked up out of process by GlobeweighService's
    // PrintWorker (see Data/Entities/PrintJob.cs).
    //
    // Enqueued in the SAME SaveChanges as the dispatch above, so a load is never completed
    // without its dockets queued, nor dockets queued for a completion that rolled back.
    // Nothing here talks to a printer: a printer problem must not be able to fail the
    // request that dispatches the orders and writes stock history.
    private async Task<(int Count, int Copies)> QueueDeliveryNotes(
        int recordId, List<DispatchOrderEntity> orders, CancellationToken ct)
    {
        var live = orders.Where(o => o.DateDeleted == null).Select(o => o.Id).ToList();
        if (live.Count == 0) return (0, 0);

        // Belt to the filtered unique index's braces: skip orders already queued, so a
        // retried request (or a load completed twice) doesn't fail on a duplicate key.
        var already = await _db.Set<PrintJob>().AsNoTracking()
            .Where(j => j.Reason == PrintJob.LoadComplete
                && j.DocType == PrintJob.DeliveryNote
                && live.Contains(j.DispatchOrderId))
            .Select(j => j.DispatchOrderId)
            .ToListAsync(ct);

        var settings = await _db.Set<Setting>().AsNoTracking().FirstOrDefaultAsync(ct);
        var copies = settings?.DeliveryNoteCopies ?? 0;
        if (copies <= 0) copies = 2;          // unset/zero would silently print nothing

        var createdUtc = DateTime.UtcNow;
        var toQueue = live.Except(already).ToList();
        foreach (var orderId in toQueue)
        {
            _db.Set<PrintJob>().Add(new PrintJob
            {
                DocType = PrintJob.DeliveryNote,
                DispatchOrderId = orderId,
                WeighbridgeRecordId = recordId,
                Copies = copies,
                PrinterName = settings?.DeliveryNotePrinter,
                Status = PrintJob.Queued,
                Reason = PrintJob.LoadComplete,
                CreatedUtc = createdUtc,
            });
        }
        return (toQueue.Count, copies);
    }

    // GET /api/picking/dev/next-pallet — DEVELOPMENT ONLY. Hands back a real, unpicked
    // pallet for an order line so the app's dev "Auto Pick" button can feed a simulated
    // label through the ordinary scan path (rather than inventing pallet data client-side
    // or short-cutting the pick). 404 on a production server, whatever the caller asks for.
    [HttpGet("dev/next-pallet")]
    public async Task<ActionResult<DevPalletDto>> DevNextPallet(
        [FromServices] IWebHostEnvironment env,
        [FromQuery] int recordId, [FromQuery] int orderId, [FromQuery] int productId,
        CancellationToken ct = default)
    {
        if (!env.IsDevelopment()) return NotFound();

        var line = (await _db.Set<DispatchOrderItemRow>().AsNoTracking()
                .Where(i => i.WeighbridgeRecordId == recordId
                    && i.OrderId == orderId && i.ProductId == productId)
                .ToListAsync(ct))
            .OrderBy(i => i.Id)
            .FirstOrDefault();
        if (line == null) return NotFound("That product isn't on the order.");

        var want = Norm(line.Gtin);
        if (want.Length == 0) return NotFound("The order line has no GTIN to match on.");

        // Codes already on this load can't be scanned again (the pick would be a duplicate).
        var orderIds = await OrderIdsForLoad(recordId, ct);
        var taken = (await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
                .Where(p => p.DateDeleted == null && orderIds.Contains(p.DispatchOrderId))
                .Select(p => p.PalletId)
                .ToListAsync(ct))
            .ToHashSet();

        // In-stock pallets of this product, oldest first — the same order a picker would
        // naturally work through the yard.
        var pallet = (await _db.Set<CodicoPalletEntity>().AsNoTracking()
                .Where(c => c.DateOutOfStock == null && c.PickedOrderId == null
                    && (c.Code13 == line.Gtin || c.Code == line.Gtin))
                .OrderBy(c => c.Id)
                .Take(200)
                .Select(c => new { c.UniqueCode, c.Weight, c.Code })
                .ToListAsync(ct))
            .FirstOrDefault(c => !taken.Contains(c.UniqueCode));
        if (pallet == null) return NotFound("No unpicked pallet of that product in stock.");

        return new DevPalletDto(pallet.Code ?? want, pallet.UniqueCode, pallet.Weight);
    }

    // The non-deleted dispatch orders on a weighbridge load.
    private async Task<List<int>> OrderIdsForLoad(int recordId, CancellationToken ct) =>
        await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(o => o.WeighbridgeRecordId == recordId && o.DateDeleted == null)
            .Select(o => o.Id)
            .ToListAsync(ct);

    // GTIN compare ignoring leading zeros (ProdCd is the 12-digit code; the order line's
    // Gtin is the 13-digit GTIN, e.g. 604565380548 ↔ 0604565380548).
    private static string Norm(string? code) =>
        Regex.Replace((code ?? "").Trim(), "^0+", "");

    // Best-guess weight for a pallet with no measured figure: a no-scan label, or a scanned
    // code CodicoPallet has never heard of (or holds at 0). A product's pallets are all
    // built the same way, so the ones still in stock — the very pallets the picker is
    // pulling from — are the closest guide (the client's rule). Failing that, the pallets
    // already scanned onto the same order line, then the product's last dispatched ones.
    // Median, not mean: the 3rd party's data has the odd bad row, and one zero or ten-fold
    // outlier must not drag the figure. With no weighed pallet anywhere (the bark products),
    // the product's Average Weight as set on the Products list; 0 only when that is blank too
    // — the row is still flagged IsWeightEstimated, so a 0 never passes as measured. Frozen at pick time: the
    // views sum the stored figure, and stock moves on after the pick.
    private async Task<decimal> EstimatePalletWeight(DispatchOrderItemRow line, CancellationToken ct)
    {
        // Same product match as the dev-pallet helper: the line's 13-digit GTIN against the
        // pallet's padded Code13 (or a raw Code that already matches).
        var gtin = (line.Gtin ?? "").Trim();
        if (gtin.Length > 0)
        {
            var inStock = await _db.Set<CodicoPalletEntity>().AsNoTracking()
                .Where(c => c.DateOutOfStock == null && c.PickedOrderId == null && c.Weight > 0
                    && (c.Code13 == gtin || c.Code == gtin))
                .OrderByDescending(c => c.Id)
                .Take(200)
                .Select(c => c.Weight)
                .ToListAsync(ct);
            if (inStock.Count > 0) return Median(inStock);
        }

        var onLine = await _db.Set<DispatchOrderPickedEntity>().AsNoTracking()
            .Where(p => p.DateDeleted == null && p.DispatchOrderItemId == line.Id
                && !p.IsWeightEstimated && p.Weight > 0)
            .Select(p => p.Weight)
            .ToListAsync(ct);
        if (onLine.Count > 0) return Median(onLine);

        if (gtin.Length > 0)
        {
            var dispatched = await _db.Set<CodicoPalletEntity>().AsNoTracking()
                .Where(c => c.PickedOrderId != null && c.Weight > 0
                    && (c.Code13 == gtin || c.Code == gtin))
                .OrderByDescending(c => c.Id)
                .Take(50)
                .Select(c => c.Weight)
                .ToListAsync(ct);
            if (dispatched.Count > 0) return Median(dispatched);
        }

        var average = await _db.Set<Product>().AsNoTracking()
            .Where(p => p.Id == line.ProductId)
            .Select(p => p.AverageWeight)
            .FirstOrDefaultAsync(ct);
        return average is > 0 ? average.Value : 0m;
    }

    private static decimal Median(List<decimal> values)
    {
        values.Sort();
        var n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
    }
}

/// <summary>A pending weighbridge record as shown in the Picking app's Pending Loads list.
/// PickedPallets/TotalPallets drive the per-load picking progress ring.</summary>
public record PickingPendingDto(
    int Id,
    bool IsOutgoing,
    DateTime? PendingWeighDate,
    string? HaulierName,
    string? TrailerNo,
    int WeightIn,
    int? OrderCount,
    int PickedPallets,
    int TotalPallets,
    bool TrailerConfirmed = false);

/// <summary>The picker's trailer-number confirmation. UserId comes later (no login yet).</summary>
public record ConfirmTrailerRequest(int? UserId);

/// <summary>Pick (or un-pick) an order's POS board.</summary>
public record PosPickRequest(int RecordId, int OrderId, int? UserId = null);

/// <summary>Mark a cubic meter line picked, with the density (kg/m³) the picker entered.</summary>
public record MarkPickedRequest(int RecordId, int OrderId, int ProductId, decimal? Density, int? UserId = null);

/// <summary>The trailer number a picker typed after answering NO to the load's one.</summary>
public record ChangeTrailerRequest(int? UserId, string? TrailerNo);

/// <summary>An order on a pending load, as shown in the Picking app's Load Detail.
/// LoadingOrder is the reverse of the delivery order: 1 is loaded onto the trailer
/// first (and delivered last).</summary>
public record PickingOrderDto(
    int Id,
    int OrderNo,
    string? CustomerName,
    string? PoNumber,
    int PickedQuantity,
    int TotalQuantityOrdered,
    int LoadingOrder,
    string? PickingComments,
    // Set once the order's picking is finished — every pallet picked, or ended short by
    // the picker. CompletedEarly distinguishes the two. While it is null the app keeps the
    // picker on this order.
    DateTime? DatePickingComplete = null,
    bool CompletedEarly = false);

/// <summary>A pending load's detail: its orders plus totals (distinct products, pallets).</summary>
public record PickingLoadDetailDto(
    int TotalProducts,
    int TotalPallets,
    // Summed weight of the picks on the load so far (Kg), estimates included.
    decimal TotalWeight,
    // How many of those picks were no-scan labels (an exception, flagged for reporting).
    int NoScanPallets,
    // How many carry an estimated rather than measured weight (every no-scan label, plus
    // any scanned code with no pallet record) — any at all means TotalWeight is a best
    // estimate rather than a measured figure.
    int EstimatedPallets,
    List<PickingOrderDto> Orders,
    // Some order on the load has a POS board: completing the load needs a POS Boards photo.
    bool HasPos = false);

/// <summary>One pick's contribution to a load's calculated weight.</summary>
public record PickedWeight(decimal Weight, bool IsNoScan, bool IsWeightEstimated);

/// <summary>A pallet-pick line: a product on an order, with the pallet count to pick.
/// Size/Upp mirror the order-item line (bag size + units per pallet).</summary>
public record PickItemDto(
    int ProductId,
    string? ProductDescription,
    string? Gtin,
    int OrderId,
    int OrderNo,
    int PalletCount,
    int? Size = null,
    int Upp = 0,
    // Product.SoldBy. A CubicMeter line is marked picked (PalletCount 1) and never scanned;
    // Quantity is then its m³ for display.
    string SoldBy = ProductSoldBy.Pallet,
    int Quantity = 0);

/// <summary>The captured out-weight (Kg) that completes a load.</summary>
/// <summary>UserId: the tablet's signed-in user, for the audit trail of every order the load
/// dispatches. Optional — app builds before 1.0.15 do not send it.</summary>
public record CompleteLoadRequest(int Weight, int? UserId = null);

/// <summary>What completing a load actually did, so the Picking app can tell the picker
/// rather than assume. DeliveryNotesQueued is 0 on a re-completed load (the notes were
/// already queued the first time), which is why the app must not hard-code "notes sent".
/// SageExportsQueued is 0 whenever Sage:ExportEnabled is off, and on a re-completed load.</summary>
public record CompleteLoadResultDto(
    int OrdersDispatched,
    int DeliveryNotesQueued,
    int Copies,
    int SageExportsQueued = 0);

/// <summary>A "Complete Load" photo's metadata (bytes are streamed via GET photos/{id}).</summary>
public record LoadPhotoDto(
    int Id,
    int WeighbridgeRecordId,
    string Description,
    DateTime DateCreated);

/// <summary>A scanned pallet to record: which load, plus the barcode's product + unique code.
/// OrderId is the order the picker has selected — the pallet may only land on that one
/// (0 falls back to the old behaviour of taking the first matching line on the load).</summary>
public record PickRequest(
    int RecordId, string? ProdCode, string? UniqueCode, int OrderId = 0, int? UserId = null,
    // Set when the picker has confirmed the "already fully picked" prompt and wants the
    // pallet recorded anyway.
    bool AllowOverPick = false);

/// <summary>A manual "no-scan" pick: which load, and the order line to count against.
/// AllowOverPick is the picker's confirmation to add it to a line that is already full.</summary>
public record NoScanRequest(
    int RecordId, int OrderId, int ProductId, int? UserId = null, bool AllowOverPick = false);

/// <summary>A real pallet for the dev-only "Auto Pick" button to simulate scanning.</summary>
public record DevPalletDto(string ProdCode, string UniqueCode, decimal Weight);

/// <summary>Who is completing an order's picking (the app's signed-in user).</summary>
public record CompleteOrderRequest(int? UserId);

/// <summary>Who reopened an order's picking — the tablet's signed-in user, for the audit trail.</summary>
public record ReopenOrderRequest(int? UserId);

/// <summary>An order's picking-complete state after completing it.</summary>
public record PickingOrderCompleteDto(
    int Id, int OrderNo, DateTime? DatePickingComplete, bool CompletedEarly);

/// <summary>A pallet to un-pick: which load and order line, plus the pick to remove —
/// by its row id (PickedId, exact) or, failing that, by the pallet's unique code.</summary>
public record DeletePickRequest(
    int RecordId, int OrderId, int ProductId, string? UniqueCode, int PickedId = 0);

/// <summary>A pallet already picked onto a load (a persisted DispatchOrder_Picked row).
/// Id is that row's key — how the app removes one specific pick (a no-scan label has no
/// code to identify it by).</summary>
public record PickedPalletDto(
    int Id,
    int OrderId,
    int ProductId,
    string PalletId,
    decimal Weight,
    DateTime PickedAt,
    bool IsNoScan,
    // Weight is an estimate from the product's other pallets, not this pallet's own
    // measured figure (see IsWeightEstimated on the entity).
    bool IsWeightEstimated = false);

/// <summary>
/// Outcome of a pick scan. Status is one of: ok, duplicate, notOnLoad, notOnOrder,
/// orderComplete, alreadyComplete, unreadable. On ok the pallet was recorded;
/// PickedForLine/TargetForLine give that order line's new progress, PalletKnown says whether
/// the pallet was found in CodicoPallet, and OrderComplete says whether that pallet finished
/// the order (so the app can release the picker onto the next one).
/// </summary>
public record PickResultDto(
    string Status,
    int? OrderId,
    int? OrderNo,
    int? ProductId,
    string? ProductDescription,
    string? PalletId,
    string? ProductCode,
    decimal Weight,
    DateTime? PickedAt,
    int PickedForLine,
    int TargetForLine,
    bool PalletKnown,
    bool WasDuplicate = false,
    int PickedId = 0,
    bool OrderComplete = false,
    // The pallet was taken onto a line that was already full, at the picker's confirmation.
    bool WasOverPick = false,
    // Weight is an estimate from the product's other pallets, not this pallet's own
    // measured figure (a no-scan label, or a code with no pallet record).
    bool WeightEstimated = false)
{
    public static PickResultDto Of(string status) =>
        new(status, null, null, null, null, null, null, 0m, null, 0, 0, false);
}
