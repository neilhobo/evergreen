using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/stock")]
public class StockController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public StockController(GbEvergreenContext db) => _db = db;

    // GET /api/stock/summary — current stock as a per-product summary of the CodicoPallet
    // master list (never a pallet-level list). In stock = DateOutOfStock IS NULL; pallets
    // are marked in/out by "Make Current Stock" on a stock take.
    // With ?locationId=N the summary is that location's STANDING COUNTS instead — a
    // satellite yard's counted stock, or for Main Yard the pallets left over once the
    // satellites are set aside (StandingCountsAsync). Pallets are never attributed to a
    // location, so a per-location view is counts only (no weights available there).
    [HttpGet("summary")]
    public async Task<ActionResult<IEnumerable<StockSummaryDto>>> Summary(
        [FromQuery] int? locationId = null, CancellationToken ct = default)
    {
        if (locationId != null)
        {
            var standing = await StandingCountsAsync(ct);
            var counts = standing.GetValueOrDefault(locationId.Value);
            if (counts == null || counts.Count == 0) return new List<StockSummaryDto>();

            var ids = counts.Keys.ToList();
            var prods = await _db.Set<Product>().AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToListAsync(ct);
            var locRows = prods
                .Select(pr => new StockSummaryDto
                {
                    ProductId = pr.Id,
                    Code13 = pr.Gtin13,
                    Gtin = pr.Gtin,
                    Description = pr.Description,
                    Size = pr.Size,
                    Upp = pr.Upp,
                    CategoryId = pr.CategoryId,
                    PalletCount = counts.GetValueOrDefault(pr.Id),
                })
                .Where(r => r.PalletCount > 0)
                .ToList();
            await FillCategoryNames(locRows, ct);
            return SortByCategory(locRows);
        }
        // Aggregate per Code13 in SQL, then attach product info in memory — the same
        // Code13 -> Product.Gtin13 match the Production grids use.
        var agg = await _db.Set<CodicoPalletEntity>().AsNoTracking()
            .Where(p => p.DateOutOfStock == null)
            .GroupBy(p => p.Code13)
            .Select(g => new
            {
                Code13 = g.Key,
                PalletCount = g.Count(),
                TotalWeight = g.Sum(p => p.Weight),
            })
            .ToListAsync(ct);

        var codes = agg.Where(a => a.Code13 != null).Select(a => a.Code13!).ToList();
        var products = await _db.Set<Product>().AsNoTracking()
            .Where(p => p.Gtin13 != null && codes.Contains(p.Gtin13))
            .ToListAsync(ct);
        var byGtin13 = products
            .GroupBy(p => p.Gtin13!)
            .ToDictionary(g => g.Key, g => g.First());

        // Cubic meter products carry no stock, so they never appear in it.
        var cubicMeter = products.Where(p => p.SoldBy == ProductSoldBy.CubicMeter).Select(p => p.Gtin13!).ToHashSet();
        var rows = agg
            .Where(a => a.Code13 == null || !cubicMeter.Contains(a.Code13))
            .Select(a =>
            {
                var pr = a.Code13 != null && byGtin13.TryGetValue(a.Code13, out var p) ? p : null;
                return new StockSummaryDto
                {
                    ProductId = pr?.Id,
                    Code13 = a.Code13,
                    Gtin = pr?.Gtin,
                    Description = pr?.Description,
                    Size = pr?.Size,
                    Upp = pr?.Upp,
                    CategoryId = pr?.CategoryId,
                    PalletCount = a.PalletCount,
                    TotalWeight = a.TotalWeight,
                };
            })
            .ToList();
        await FillCategoryNames(rows, ct);
        return SortByCategory(rows);
    }

    // Standing counts per location (locationId -> productId -> pallets).
    //
    // SATELLITE YARDS (everything but Main Yard) are counted: the latest made-current
    // stock take's counts, plus transfers in/out and manual corrections dated AFTER that
    // take — a newer made-current take supersedes them (the stock was re-counted).
    // Counts-only: transfers never touch the pallet in/out-of-stock state.
    //
    // MAIN YARD is derived: in-stock pallets MINUS the satellite counts. Production and
    // dispatch churn the pallet master daily and never touch a stock take, so a counted
    // Main Yard drifts below the real total the moment the next pallet is made (it was
    // 1,173 pallets adrift after three weeks). Deriving it makes the location list add up
    // to the "All" pallet total by construction — every movement lands in exactly one
    // place: satellites move on takes/transfers/manual, everything else is Main Yard.
    // Consequence: Main Yard's own take counts only matter through the pallet state that
    // "Make Current Stock" writes, and any drift in the pallet master shows up there —
    // which is the honest place for it, as that is where production and dispatch happen.
    private async Task<Dictionary<int, Dictionary<int, int>>> StandingCountsAsync(CancellationToken ct)
    {
        var takes = await _db.Set<StockTake>().AsNoTracking()
            .Where(t => t.DateDeleted == null && !t.IsTransfer
                && t.DateMadeCurrentStock != null && t.LocationId != null)
            .ToListAsync(ct);
        var latest = takes
            .GroupBy(t => t.LocationId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.DateMadeCurrentStock).ThenByDescending(t => t.Id).First());

        var transfers = await _db.Set<StockTake>().AsNoTracking()
            .Where(t => t.DateDeleted == null && t.IsTransfer
                && t.LocationId != null && t.FromLocationId != null)
            .ToListAsync(ct);

        var ids = latest.Values.Select(t => t.Id).Concat(transfers.Select(t => t.Id)).ToList();
        var itemsByTake = (await _db.Set<StockTakeItem>().AsNoTracking()
                .Where(i => ids.Contains(i.StockTakeId))
                .ToListAsync(ct))
            .GroupBy(i => i.StockTakeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<int, Dictionary<int, int>>();
        void Add(int loc, int product, int qty)
        {
            if (!result.TryGetValue(loc, out var d)) result[loc] = d = new Dictionary<int, int>();
            d[product] = d.GetValueOrDefault(product) + qty;
        }

        foreach (var (loc, take) in latest)
            foreach (var i in itemsByTake.GetValueOrDefault(take.Id) ?? [])
                Add(loc, i.ProductId, i.PalletCount);

        foreach (var tr in transfers.OrderBy(t => t.DateCreated).ThenBy(t => t.Id))
        {
            var from = tr.FromLocationId!.Value;
            var to = tr.LocationId!.Value;
            foreach (var i in itemsByTake.GetValueOrDefault(tr.Id) ?? [])
            {
                if (!latest.TryGetValue(from, out var fb) || tr.DateCreated > fb.DateMadeCurrentStock)
                    Add(from, i.ProductId, -i.PalletCount);
                if (!latest.TryGetValue(to, out var tb) || tr.DateCreated > tb.DateMadeCurrentStock)
                    Add(to, i.ProductId, i.PalletCount);
            }
        }

        // Manual stock corrections: "ManualAdd" history rows (EntityId = location id)
        // add to their location until a newer take there re-counts the stock.
        var manualAdds = await _db.Set<StockHistory>().AsNoTracking()
            .Where(h => h.TransactionType == "ManualAdd")
            .ToListAsync(ct);
        foreach (var m in manualAdds)
            if (!latest.TryGetValue(m.EntityId, out var mbase) || m.DateCreated > mbase.DateMadeCurrentStock)
                Add(m.EntityId, m.ProductId, m.Quantity);

        // A movement can't drive a location negative in the views even if data drifts.
        foreach (var d in result.Values)
            foreach (var k in d.Keys.ToList())
                if (d[k] < 0) d[k] = 0;

        // Main Yard is DERIVED, not counted: it is the pallet master minus whatever the
        // satellite yards hold. See the note on the method — this is what keeps the
        // location list a true breakdown of the in-stock pallet total.
        var mainYard = await _db.Set<StockLocation>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationName == "Main Yard" && l.DateDeleted == null, ct);
        if (mainYard != null)
        {
            result.Remove(mainYard.Id);

            // Pallets carry a code, not a product id: fold both sides onto Gtin13.
            var products = await _db.Set<Product>().AsNoTracking()
                .Where(p => p.Gtin13 != null)
                .Select(p => new { p.Id, Code13 = p.Gtin13! })
                .ToListAsync(ct);
            var codeByProduct = products.ToDictionary(p => p.Id, p => p.Code13);
            // One product per code — the same first-match rule the summary grids use.
            var productByCode = products
                .GroupBy(p => p.Code13)
                .ToDictionary(g => g.Key, g => g.First().Id);

            var satellites = new Dictionary<string, int>();
            foreach (var d in result.Values)
                foreach (var (productId, qty) in d)
                    if (codeByProduct.TryGetValue(productId, out var code))
                        satellites[code] = satellites.GetValueOrDefault(code) + qty;

            var inStock = await _db.Set<CodicoPalletEntity>().AsNoTracking()
                .Where(p => p.DateOutOfStock == null && p.Code13 != null)
                .GroupBy(p => p.Code13!)
                .Select(g => new { Code13 = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var yard = new Dictionary<int, int>();
            foreach (var s in inStock)
            {
                // A pallet code with no product can't be shown per product — it still
                // counts in the "All" total, so the yard totals fall short by that much.
                if (!productByCode.TryGetValue(s.Code13, out var productId)) continue;
                var qty = s.Count - satellites.GetValueOrDefault(s.Code13);
                if (qty > 0) yard[productId] = qty;
            }
            result[mainYard.Id] = yard;
        }
        return result;
    }

    // Resolve CategoryId -> ReferenceData.DisplayName (the product categories) in memory.
    private async Task FillCategoryNames(List<StockSummaryDto> rows, CancellationToken ct)
    {
        var catIds = rows.Where(r => r.CategoryId != null).Select(r => r.CategoryId!.Value).Distinct().ToList();
        if (catIds.Count == 0) return;
        var names = await _db.Set<ReferenceDataItem>().AsNoTracking()
            .Where(r => catIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.DisplayName, ct);
        foreach (var r in rows)
            if (r.CategoryId != null)
                r.Category = names.GetValueOrDefault(r.CategoryId.Value);
    }

    // Category order follows the category ids (matching the printed price list); products
    // with no category — including unmatched pallet codes — sink to the bottom.
    private static List<StockSummaryDto> SortByCategory(IEnumerable<StockSummaryDto> rows) =>
        rows.OrderBy(r => r.CategoryId ?? int.MaxValue)
            .ThenBy(r => r.Description ?? "￿")
            .ThenBy(r => r.Code13)
            .ToList();

    // ---- Stock Takes ----
    // A stock take is a list of (product, pallet count) entries — counts only, never
    // pallet identity. Saving one changes nothing else yet; "Make Current Stock" is a
    // later phase (the legacy DateMadeCurrentStock column is reserved for it).

    // GET /api/stock/locations — the stock-take areas (one take covers one location).
    [HttpGet("locations")]
    public async Task<ActionResult<IEnumerable<StockLocationDto>>> Locations(CancellationToken ct = default)
    {
        return await _db.Set<StockLocation>().AsNoTracking()
            .Where(l => l.DateDeleted == null)
            .OrderBy(l => l.LocationName)
            .Select(l => new StockLocationDto { Id = l.Id, Name = l.LocationName })
            .ToListAsync(ct);
    }

    // GET /api/stock/location-counts — sidebar counts for the Current Stock view:
    // All = real in-stock pallets, and the locations are a breakdown of it — the satellite
    // yards hold their counted stock, Main Yard holds the rest (StandingCountsAsync). The
    // locations sum to All except for pallets whose code matches no product.
    [HttpGet("location-counts")]
    public async Task<ActionResult<StockLocationCountsDto>> LocationCounts(CancellationToken ct = default)
    {
        var locs = await _db.Set<StockLocation>().AsNoTracking()
            .Where(l => l.DateDeleted == null)
            .ToListAsync(ct);
        // Main Yard (the main site) leads, straight under "All"; the rest alphabetical.
        locs = locs
            .OrderBy(l => l.LocationName == "Main Yard" ? 0 : 1)
            .ThenBy(l => l.LocationName)
            .ToList();

        var standing = await StandingCountsAsync(ct);
        return new StockLocationCountsDto
        {
            AllPallets = await _db.Set<CodicoPalletEntity>().CountAsync(p => p.DateOutOfStock == null, ct),
            Locations = locs.Select(l => new StockLocationCountDto
            {
                Id = l.Id,
                Name = l.LocationName,
                PalletCount = standing.GetValueOrDefault(l.Id)?.Values.Sum() ?? 0,
            }).ToList(),
        };
    }

    // GET /api/stock/history — live stock movement feed, newest first: additions from
    // production (pallets per product per day) and applied stock takes, transfers
    // between locations, and depletions from dispatched orders. Direction: +1 add,
    // -1 deplete, 0 transfer (net-zero move). Optional whole-day from/to date range.
    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<StockMovementDto>>> History(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var f = from?.Date;
        var t2 = to?.Date.AddDays(1);
        var list = new List<StockMovementDto>();

        // Depletions — dispatch audit rows, with the customer-facing order number.
        var dispatches = await (
            from h in _db.Set<StockHistory>().AsNoTracking()
            where h.TransactionType == "Dispatch"
                && (f == null || h.DateCreated >= f) && (t2 == null || h.DateCreated < t2)
            join o in _db.Set<DispatchOrderEntity>().AsNoTracking() on h.EntityId equals o.Id into oj
            from o in oj.DefaultIfEmpty()
            join pr in _db.Set<Product>().AsNoTracking() on h.ProductId equals pr.Id into pj
            from pr in pj.DefaultIfEmpty()
            orderby h.DateCreated descending, h.Id descending
            select new
            {
                h.Id,
                h.ProductId,
                h.DateCreated,
                Description = pr != null ? pr.Description : null,
                Size = pr != null ? pr.Size : null,
                OrderNo = o != null ? (int?)o.OrderNo : null,
                h.Quantity,
            }).Take(150).ToListAsync(ct);
        list.AddRange(dispatches.Select(d => new StockMovementDto
        {
            Id = $"d{d.Id}",
            Date = d.DateCreated,
            Type = "Order",
            ProductId = d.ProductId,
            Description = d.Description,
            Size = d.Size,
            Reference = d.OrderNo != null ? $"Order {d.OrderNo}" : "",
            Qty = d.Quantity,
            Direction = -1,
        }));

        // Additions — production pallets, one row per product per day (stock-take "ST"
        // and manual-add "MA" placeholder pallets excluded; those surface as their own
        // Stock Take / Manual rows instead).
        var prodAgg = await _db.Set<CodicoPalletEntity>().AsNoTracking()
            .Where(p => p.DateCreated != null
                && !(p.IsManual && (p.UniqueCode.StartsWith("ST") || p.UniqueCode.StartsWith("MA")))
                && (f == null || p.DateCreated >= f) && (t2 == null || p.DateCreated < t2))
            .GroupBy(p => new { p.Code13, Day = p.DateCreated!.Value.Date })
            .Select(g => new
            {
                g.Key.Code13,
                Qty = g.Count(),
                Last = g.Max(p => p.DateCreated),
            })
            .OrderByDescending(g => g.Last)
            .Take(150)
            .ToListAsync(ct);
        var prodCodes = prodAgg.Select(a => a.Code13).Where(c => c != null).Distinct().ToList();
        var prodByCode = (await _db.Set<Product>().AsNoTracking()
                .Where(p => p.Gtin13 != null && prodCodes.Contains(p.Gtin13))
                .ToListAsync(ct))
            .GroupBy(p => p.Gtin13!)
            .ToDictionary(g => g.Key, g => g.First());
        list.AddRange(prodAgg.Select(a =>
        {
            var pr = a.Code13 != null ? prodByCode.GetValueOrDefault(a.Code13) : null;
            return new StockMovementDto
            {
                Id = $"p{a.Code13}-{a.Last:yyyyMMdd}",
                Date = a.Last,
                Type = "Production",
                ProductId = pr?.Id,
                Description = pr?.Description ?? a.Code13,
                Size = pr?.Size,
                Reference = "",
                Qty = a.Qty,
                Direction = 1,
            };
        }));

        // Manual stock corrections — one row each, with the location added at.
        var manuals = await (
            from h in _db.Set<StockHistory>().AsNoTracking()
            where h.TransactionType == "ManualAdd"
                && (f == null || h.DateCreated >= f) && (t2 == null || h.DateCreated < t2)
            join pr in _db.Set<Product>().AsNoTracking() on h.ProductId equals pr.Id into pj
            from pr in pj.DefaultIfEmpty()
            join l in _db.Set<StockLocation>().AsNoTracking() on h.EntityId equals l.Id into lj
            from l in lj.DefaultIfEmpty()
            orderby h.DateCreated descending, h.Id descending
            select new
            {
                h.Id,
                h.ProductId,
                h.DateCreated,
                Description = pr != null ? pr.Description : null,
                Size = pr != null ? pr.Size : null,
                Location = l != null ? l.LocationName : null,
                h.Quantity,
            }).Take(100).ToListAsync(ct);
        list.AddRange(manuals.Select(m => new StockMovementDto
        {
            Id = $"m{m.Id}",
            Date = m.DateCreated,
            Type = "Manual",
            ProductId = m.ProductId,
            Description = m.Description,
            Size = m.Size,
            Reference = m.Location ?? "",
            // Quantity is signed: negative = downward correction.
            Qty = Math.Abs(m.Quantity),
            Direction = m.Quantity >= 0 ? 1 : -1,
        }));

        // Transfers — one row per moved product, with the from -> to locations.
        var transfers = await (
            from t in _db.Set<StockTake>().AsNoTracking()
            where t.IsTransfer && t.DateDeleted == null
                && (f == null || t.DateCreated >= f) && (t2 == null || t.DateCreated < t2)
            join i in _db.Set<StockTakeItem>().AsNoTracking() on t.Id equals i.StockTakeId
            join pr in _db.Set<Product>().AsNoTracking() on i.ProductId equals pr.Id into pj
            from pr in pj.DefaultIfEmpty()
            join fl in _db.Set<StockLocation>().AsNoTracking() on t.FromLocationId equals (int?)fl.Id into flj
            from fl in flj.DefaultIfEmpty()
            join tl in _db.Set<StockLocation>().AsNoTracking() on t.LocationId equals (int?)tl.Id into tlj
            from tl in tlj.DefaultIfEmpty()
            orderby t.DateCreated descending, i.Id descending
            select new
            {
                i.Id,
                i.ProductId,
                t.DateCreated,
                Description = pr != null ? pr.Description : null,
                Size = pr != null ? pr.Size : null,
                From = fl != null ? fl.LocationName : "?",
                To = tl != null ? tl.LocationName : "?",
                i.PalletCount,
            }).Take(100).ToListAsync(ct);
        list.AddRange(transfers.Select(t => new StockMovementDto
        {
            Id = $"t{t.Id}",
            Date = t.DateCreated,
            Type = "Transfer",
            ProductId = t.ProductId,
            Description = t.Description,
            Size = t.Size,
            Reference = $"{t.From} → {t.To}",
            Qty = t.PalletCount,
            Direction = 0,
        }));

        // Stock takes made current — one row per take (its counts became the stock).
        var applied = await (
            from t in _db.Set<StockTake>().AsNoTracking()
            where !t.IsTransfer && t.DateDeleted == null && t.DateMadeCurrentStock != null
                && (f == null || t.DateMadeCurrentStock >= f) && (t2 == null || t.DateMadeCurrentStock < t2)
            join l in _db.Set<StockLocation>().AsNoTracking() on t.LocationId equals (int?)l.Id into lj
            from l in lj.DefaultIfEmpty()
            orderby t.DateMadeCurrentStock descending
            select new
            {
                t.Id,
                Date = t.DateMadeCurrentStock,
                Location = l != null ? l.LocationName : null,
                Qty = _db.Set<StockTakeItem>()
                    .Where(i => i.StockTakeId == t.Id)
                    .Sum(i => (int?)i.PalletCount) ?? 0,
            }).Take(50).ToListAsync(ct);
        list.AddRange(applied.Select(t => new StockMovementDto
        {
            Id = $"s{t.Id}",
            Date = t.Date,
            Type = "Stock Take",
            Description = $"Made current stock — Stock Take #{t.Id}",
            Size = null,
            Reference = t.Location ?? "",
            Qty = t.Qty,
            Direction = 1,
        }));

        return list
            .OrderByDescending(m => m.Date)
            .ThenByDescending(m => m.Id, StringComparer.Ordinal)
            .Take(200)
            .ToList();
    }

    // GET /api/stock/takes — all non-deleted stock takes, newest first.
    [HttpGet("takes")]
    public async Task<ActionResult<IEnumerable<StockTakeDto>>> Takes(CancellationToken ct = default)
    {
        return await (
            from t in _db.Set<StockTake>().AsNoTracking()
            where t.DateDeleted == null
            join u in _db.Set<GlobeweighUser>().AsNoTracking() on t.CreatedById equals (int?)u.Id into uj
            from u in uj.DefaultIfEmpty()
            join l in _db.Set<StockLocation>().AsNoTracking() on t.LocationId equals (int?)l.Id into lj
            from l in lj.DefaultIfEmpty()
            join fl in _db.Set<StockLocation>().AsNoTracking() on t.FromLocationId equals (int?)fl.Id into flj
            from fl in flj.DefaultIfEmpty()
            orderby t.DateCreated descending, t.Id descending
            select new StockTakeDto
            {
                Id = t.Id,
                DateCreated = t.DateCreated,
                DateCompleted = t.DateCompleted,
                DateMadeCurrentStock = t.DateMadeCurrentStock,
                CreatedBy = u != null ? (u.FirstName + " " + u.Surname) : null,
                LocationId = t.LocationId,
                LocationName = l != null ? l.LocationName : null,
                IsTransfer = t.IsTransfer,
                FromLocationName = fl != null ? fl.LocationName : null,
                ProductCount = _db.Set<StockTakeItem>().Count(i => i.StockTakeId == t.Id),
                PalletCount = _db.Set<StockTakeItem>()
                    .Where(i => i.StockTakeId == t.Id)
                    .Sum(i => (int?)i.PalletCount) ?? 0,
            }).ToListAsync(ct);
    }

    // POST /api/stock/manual-add — manual stock correction (the stock take count was
    // wrong): a SIGNED pallet quantity for a product at a location. Positive creates
    // zero-weight manual placeholder pallets (the All view is pallet-based); negative
    // marks the OLDEST in-stock pallets of the product out (method "Manual", same FIFO
    // as dispatch). Both write a StockHistory "ManualAdd" audit row (EntityId =
    // location id, signed Quantity) that the standing-counts engine applies to the
    // location — superseded, like transfers, by the next made-current take there.
    [HttpPost("manual-add")]
    public async Task<ActionResult> ManualAdd([FromBody] ManualAddInput input, CancellationToken ct = default)
    {
        if (input.PalletCount == 0) return BadRequest("Enter a pallet quantity.");
        var product = await _db.Set<Product>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == input.ProductId, ct);
        if (product == null) return BadRequest("Product not found.");
        if (product.SoldBy == ProductSoldBy.CubicMeter)
            return BadRequest("This product is sold by the cubic meter and carries no stock.");
        if (product.Gtin13 == null)
            return BadRequest("Product has no Evergreen code (GTIN) — its pallets can't be tracked.");
        var loc = await _db.Set<StockLocation>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == input.LocationId && l.DateDeleted == null, ct);
        if (loc == null) return BadRequest("Location not found.");

        if (input.PalletCount < 0)
        {
            var removing = -input.PalletCount;
            var inStock = await _db.Set<CodicoPalletEntity>()
                .CountAsync(p => p.Code13 == product.Gtin13 && p.DateOutOfStock == null, ct);
            if (removing > inStock)
                return BadRequest($"Only {inStock} pallets of this product are in stock.");
            var standing = await StandingCountsAsync(ct);
            var atLocation = standing.GetValueOrDefault(loc.Id)?.GetValueOrDefault(product.Id) ?? 0;
            if (removing > atLocation)
                return BadRequest($"{loc.LocationName} only holds {atLocation} pallets of this product.");
        }

        var now = DateTime.Now;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (input.PalletCount > 0)
        {
            var stamp = now.ToString("yyMMddHHmmss");
            for (var n = 1; n <= input.PalletCount; n++)
                _db.Add(new CodicoPalletEntity
                {
                    UniqueCode = $"MA{stamp}-{product.Id}-{n:D3}",
                    Code = product.Gtin,
                    Weight = 0,
                    IsManual = true,
                    // Born scanned: these pallets never pass the conveyor scanner, and an
                    // unscanned pallet dated today counts as a missed scan (red row + siren).
                    IsScanned = true,
                    DateScanned = now,
                    DateCreated = now,
                    LocationId = loc.Id,
                });
        }
        else
        {
            // Remove oldest-first (FIFO) among the product's in-stock pallets.
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE cp SET DateOutOfStock = {now}, OutOfStockMethod = 'Manual'
FROM dbo.CodicoPallet cp
JOIN (SELECT id, ROW_NUMBER() OVER (ORDER BY DateCreated ASC, id ASC) AS rn
      FROM dbo.CodicoPallet
      WHERE Code13 = {product.Gtin13} AND DateOutOfStock IS NULL) r ON r.id = cp.id
WHERE r.rn <= {-input.PalletCount}", ct);
        }
        _db.Add(new StockHistory
        {
            ProductId = product.Id,
            Quantity = input.PalletCount,
            EntityId = loc.Id,
            TransactionType = "ManualAdd",
            DateCreated = now,
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    // POST /api/stock/transfers — move counted stock from one location to another
    // (counts-only; the pallet in/out state is untouched). Stored as a StockTake row
    // with IsTransfer=1: FromLocationId -> LocationId, items = quantities moved.
    [HttpPost("transfers")]
    public async Task<ActionResult<StockTakeDto>> CreateTransfer(
        [FromBody] StockTransferInput input, CancellationToken ct = default)
    {
        if (input.FromLocationId == input.ToLocationId)
            return BadRequest("Choose two different locations.");
        if (input.Items.Count == 0)
            return BadRequest("Add at least one product to transfer.");
        if (input.Items.Any(i => i.PalletCount <= 0))
            return BadRequest("Quantities must be positive.");
        if (input.Items.GroupBy(i => i.ProductId).Any(g => g.Count() > 1))
            return BadRequest("The same product appears more than once.");
        var locCount = await _db.Set<StockLocation>()
            .CountAsync(l => (l.Id == input.FromLocationId || l.Id == input.ToLocationId)
                && l.DateDeleted == null, ct);
        if (locCount != 2) return BadRequest("Location not found.");

        // No overdraw: a transfer can't move more than the source location holds.
        var standing = await StandingCountsAsync(ct);
        var source = standing.GetValueOrDefault(input.FromLocationId) ?? new Dictionary<int, int>();
        foreach (var i in input.Items)
            if (i.PalletCount > source.GetValueOrDefault(i.ProductId))
                return BadRequest("Transfer exceeds the stock at the source location.");

        var now = DateTime.Now;
        var tr = new StockTake
        {
            IsTransfer = true,
            FromLocationId = input.FromLocationId,
            LocationId = input.ToLocationId,
            CreatedById = this.GetUserId(),
            DateCreated = now,
            DateCompleted = now,
        };
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Add(tr);
        await _db.SaveChangesAsync(ct);
        foreach (var i in input.Items)
            _db.Add(new StockTakeItem
            {
                StockTakeId = tr.Id,
                ProductId = i.ProductId,
                PalletCount = i.PalletCount,
                DateCreated = now,
            });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new StockTakeDto { Id = tr.Id, DateCreated = tr.DateCreated, IsTransfer = true };
    }

    // POST /api/stock/takes — create a stock take with its items in one shot (the UI
    // edits a local draft, dispatch-order style; Cancel there saves nothing).
    [HttpPost("takes")]
    public async Task<ActionResult<StockTakeDto>> CreateTake(
        [FromBody] StockTakeInput input, CancellationToken ct = default)
    {
        var err = await ValidateAsync(input, ct);
        if (err != null) return BadRequest(err);

        var take = new StockTake
        {
            CreatedById = this.GetUserId(),
            LocationId = input.LocationId,
            DateCreated = DateTime.Now,
        };
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Add(take);
        await _db.SaveChangesAsync(ct);
        foreach (var i in input.Items)
            _db.Add(new StockTakeItem
            {
                StockTakeId = take.Id,
                ProductId = i.ProductId,
                PalletCount = i.PalletCount,
                DateCreated = DateTime.Now,
            });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new StockTakeDto { Id = take.Id, DateCreated = take.DateCreated };
    }

    // PUT /api/stock/takes/{id} — replace a stock take's items with the edited draft.
    [HttpPut("takes/{id:int}")]
    public async Task<ActionResult<StockTakeDto>> UpdateTake(
        int id, [FromBody] StockTakeInput input, CancellationToken ct = default)
    {
        var err = await ValidateAsync(input, ct);
        if (err != null) return BadRequest(err);

        var take = await _db.Set<StockTake>()
            .FirstOrDefaultAsync(t => t.Id == id && t.DateDeleted == null, ct);
        if (take == null) return NotFound();
        if (take.IsTransfer)
            return BadRequest("Transfers cannot be edited — delete the transfer and redo it.");
        if (take.DateMadeCurrentStock != null)
            return BadRequest("This stock take has been made current stock and can no longer be edited.");
        take.LocationId = input.LocationId;

        // Wholesale replace in a single SaveChanges so the swap is atomic.
        var existing = await _db.Set<StockTakeItem>().Where(i => i.StockTakeId == id).ToListAsync(ct);
        _db.RemoveRange(existing);
        foreach (var i in input.Items)
            _db.Add(new StockTakeItem
            {
                StockTakeId = id,
                ProductId = i.ProductId,
                PalletCount = i.PalletCount,
                DateCreated = DateTime.Now,
            });
        await _db.SaveChangesAsync(ct);

        return new StockTakeDto { Id = take.Id, DateCreated = take.DateCreated };
    }

    private async Task<string?> ValidateAsync(StockTakeInput input, CancellationToken ct)
    {
        if (input.Items.Any(i => i.PalletCount < 0)) return "Pallet count cannot be negative.";
        if (input.Items.GroupBy(i => i.ProductId).Any(g => g.Count() > 1))
            return "The same product appears more than once.";
        if (!await _db.Set<StockLocation>()
                .AnyAsync(l => l.Id == input.LocationId && l.DateDeleted == null, ct))
            return "Location is required.";
        return null;
    }

    // GET /api/stock/takes/{id} — one stock take with its items (product info attached).
    [HttpGet("takes/{id:int}")]
    public async Task<ActionResult<StockTakeDetailDto>> Take(int id, CancellationToken ct = default)
    {
        var head = await (
            from t in _db.Set<StockTake>().AsNoTracking()
            where t.Id == id && t.DateDeleted == null
            join u in _db.Set<GlobeweighUser>().AsNoTracking() on t.CreatedById equals (int?)u.Id into uj
            from u in uj.DefaultIfEmpty()
            join l in _db.Set<StockLocation>().AsNoTracking() on t.LocationId equals (int?)l.Id into lj
            from l in lj.DefaultIfEmpty()
            join fl in _db.Set<StockLocation>().AsNoTracking() on t.FromLocationId equals (int?)fl.Id into flj
            from fl in flj.DefaultIfEmpty()
            select new StockTakeDetailDto
            {
                Id = t.Id,
                DateCreated = t.DateCreated,
                DateCompleted = t.DateCompleted,
                DateMadeCurrentStock = t.DateMadeCurrentStock,
                CreatedBy = u != null ? (u.FirstName + " " + u.Surname) : null,
                LocationId = t.LocationId,
                LocationName = l != null ? l.LocationName : null,
                IsTransfer = t.IsTransfer,
                FromLocationName = fl != null ? fl.LocationName : null,
            }).FirstOrDefaultAsync(ct);
        if (head == null) return NotFound();

        head.Items = await TakeItems(id).ToListAsync(ct);
        return head;
    }

    // POST /api/stock/takes/{id}/make-current — apply an OPEN stock take to the pallet
    // master list. Effective stock = this take's counts PLUS the latest made-current take
    // of every other location, summed per product. For each product the newest N pallets
    // stay in stock (pallets previously marked out come back if the combined counts now
    // cover them); every other pallet is marked out with method "StockTake". Stock is
    // counts-only — pallet identity within a location is never tracked.
    [HttpPost("takes/{id:int}/make-current")]
    public async Task<ActionResult<MakeCurrentResultDto>> MakeCurrent(int id, CancellationToken ct = default)
    {
        var take = await _db.Set<StockTake>()
            .FirstOrDefaultAsync(t => t.Id == id && t.DateDeleted == null, ct);
        if (take == null) return NotFound();
        if (take.IsTransfer)
            return BadRequest("Transfers cannot be made current stock.");
        if (take.DateMadeCurrentStock != null)
            return BadRequest("This stock take has already been made current stock.");
        if (take.LocationId == null)
            return BadRequest("Set a location on this stock take first.");

        // Effective counts = this take's items + every OTHER location's standing counts
        // (their latest made-current take adjusted by later transfers).
        var standing = await StandingCountsAsync(ct);
        standing.Remove(take.LocationId.Value);
        var perProduct = new Dictionary<int, int>();
        foreach (var d in standing.Values)
            foreach (var kv in d)
                perProduct[kv.Key] = perProduct.GetValueOrDefault(kv.Key) + kv.Value;
        var thisItems = await _db.Set<StockTakeItem>().AsNoTracking()
            .Where(i => i.StockTakeId == take.Id)
            .ToListAsync(ct);
        foreach (var i in thisItems)
            perProduct[i.ProductId] = perProduct.GetValueOrDefault(i.ProductId) + i.PalletCount;

        // Keyed by Gtin13 (how pallets are matched); products with no Gtin13 can't be
        // matched to pallets and are skipped.
        var prodIds = perProduct.Keys.ToList();
        var totals = (await _db.Set<Product>().AsNoTracking()
                // Cubic meter products carry no stock: never given placeholder pallets, and
                // left out of the totals so any pallet of theirs is marked out below.
                .Where(p => prodIds.Contains(p.Id) && p.Gtin13 != null && p.SoldBy != ProductSoldBy.CubicMeter)
                .Select(p => new { p.Id, p.Gtin13, p.Gtin })
                .ToListAsync(ct))
            .GroupBy(p => p.Gtin13!)
            .Select(g => new
            {
                Gtin13 = g.Key,
                Count = g.Sum(p => perProduct[p.Id]),
                Gtin = g.Max(p => p.Gtin),
            })
            .ToList();

        var now = DateTime.Now;
        int broughtBack = 0, markedOut = 0, created = 0;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // The counts are the truth: when a counted quantity exceeds the pallets recorded
        // on the system (e.g. bark never pushed by the 3rd party), create placeholder
        // pallet records (manual, zero weight) so the stock reconciles. Created "now",
        // they are the newest pallets of the product and are kept by the newest-N pass.
        foreach (var t in totals)
        {
            var existing = await _db.Set<CodicoPalletEntity>().CountAsync(p => p.Code13 == t.Gtin13, ct);
            for (var n = 1; n <= t.Count - existing; n++)
            {
                _db.Add(new CodicoPalletEntity
                {
                    UniqueCode = $"ST{take.Id}-{t.Gtin13}-{n:D3}",
                    Code = t.Gtin,
                    Weight = 0,
                    IsManual = true,
                    // Born scanned — see the manual-add above: never conveyor-scanned, so
                    // they must not register as missed scans.
                    IsScanned = true,
                    DateScanned = now,
                    DateCreated = now,
                    LocationId = take.LocationId,
                });
                created++;
            }
        }
        if (created > 0) await _db.SaveChangesAsync(ct);

        foreach (var t in totals)
        {
            // Newest N pallets of the product stay in stock — clear any out-of-stock mark.
            broughtBack += await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE cp SET DateOutOfStock = NULL, OutOfStockMethod = NULL, OutOfStockStockTakeId = NULL
FROM dbo.CodicoPallet cp
JOIN (SELECT id, ROW_NUMBER() OVER (ORDER BY DateCreated DESC, id DESC) AS rn
      FROM dbo.CodicoPallet WHERE Code13 = {t.Gtin13}) r ON r.id = cp.id
WHERE r.rn <= {t.Count} AND cp.DateOutOfStock IS NOT NULL", ct);

            // Everything older than the newest N goes out (already-out pallets keep their
            // original method/date).
            markedOut += await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE cp SET DateOutOfStock = {now}, OutOfStockMethod = 'StockTake', OutOfStockStockTakeId = {take.Id}
FROM dbo.CodicoPallet cp
JOIN (SELECT id, ROW_NUMBER() OVER (ORDER BY DateCreated DESC, id DESC) AS rn
      FROM dbo.CodicoPallet WHERE Code13 = {t.Gtin13}) r ON r.id = cp.id
WHERE r.rn > {t.Count} AND cp.DateOutOfStock IS NULL", ct);
        }

        // Products not counted in any effective take have no stock anywhere — mark all
        // their in-stock pallets out.
        var gtins = totals.Select(t => t.Gtin13).ToList();
        markedOut += await _db.Set<CodicoPalletEntity>()
            .Where(p => p.DateOutOfStock == null && (p.Code13 == null || !gtins.Contains(p.Code13)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DateOutOfStock, now)
                .SetProperty(p => p.OutOfStockMethod, "StockTake")
                .SetProperty(p => p.OutOfStockStockTakeId, take.Id), ct);

        take.DateMadeCurrentStock = now;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var inStock = await _db.Set<CodicoPalletEntity>().CountAsync(p => p.DateOutOfStock == null, ct);
        return new MakeCurrentResultDto
        {
            BroughtBack = broughtBack,
            MarkedOut = markedOut,
            Created = created,
            InStock = inStock,
        };
    }

    // DELETE /api/stock/takes/{id} — soft-delete a stock take (items stay for the FK cascade
    // if it's ever hard-deleted; the list and detail endpoints filter on DateDeleted).
    [HttpDelete("takes/{id:int}")]
    public async Task<IActionResult> DeleteTake(int id, CancellationToken ct = default)
    {
        var take = await _db.Set<StockTake>()
            .FirstOrDefaultAsync(t => t.Id == id && t.DateDeleted == null, ct);
        if (take == null) return NotFound();
        if (take.DateMadeCurrentStock != null)
            return BadRequest("This stock take has been made current stock and can no longer be deleted.");
        take.DateDeleted = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Items of one stock take with product info, oldest first (the order they were added).
    private IQueryable<StockTakeItemDto> TakeItems(int takeId) =>
        from i in _db.Set<StockTakeItem>().AsNoTracking()
        where i.StockTakeId == takeId
        join pr in _db.Set<Product>().AsNoTracking() on i.ProductId equals pr.Id into pj
        from pr in pj.DefaultIfEmpty()
        orderby i.Id
        select new StockTakeItemDto
        {
            Id = i.Id,
            ProductId = i.ProductId,
            Gtin = pr != null ? pr.Gtin : null,
            Description = pr != null ? pr.Description : null,
            Size = pr != null ? pr.Size : null,
            Upp = pr != null ? (int?)pr.Upp : null,
            PalletCount = i.PalletCount,
        };
}

public class StockSummaryDto
{
    public int? ProductId { get; set; }
    public string? Code13 { get; set; }
    public string? Gtin { get; set; }
    public string? Description { get; set; }
    public int? Size { get; set; }
    public int? Upp { get; set; }
    public int? CategoryId { get; set; }
    public string? Category { get; set; }
    public int PalletCount { get; set; }
    public decimal TotalWeight { get; set; }
}

public class StockLocationDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public class StockMovementDto
{
    public string Id { get; set; } = "";
    public DateTime? Date { get; set; }
    public string Type { get; set; } = "";
    public int? ProductId { get; set; } // null for whole-take rows
    public string? Description { get; set; }
    public int? Size { get; set; }
    public string Reference { get; set; } = "";
    public int Qty { get; set; }
    public int Direction { get; set; } // +1 add, -1 deplete, 0 transfer
}

public class MakeCurrentResultDto
{
    public int BroughtBack { get; set; }
    public int MarkedOut { get; set; }
    public int Created { get; set; }
    public int InStock { get; set; }
}

public class StockLocationCountDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int PalletCount { get; set; }
}

public class StockLocationCountsDto
{
    public int AllPallets { get; set; }
    public List<StockLocationCountDto> Locations { get; set; } = [];
}

public class StockTakeDto
{
    public int Id { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateCompleted { get; set; }
    public DateTime? DateMadeCurrentStock { get; set; }
    public string? CreatedBy { get; set; }
    public int? LocationId { get; set; }
    public string? LocationName { get; set; }
    public bool IsTransfer { get; set; }
    public string? FromLocationName { get; set; }
    public int ProductCount { get; set; }
    public int PalletCount { get; set; }
}

public class StockTakeDetailDto : StockTakeDto
{
    public List<StockTakeItemDto> Items { get; set; } = [];
}

public class StockTakeItemDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string? Gtin { get; set; }
    public string? Description { get; set; }
    public int? Size { get; set; }
    public int? Upp { get; set; }
    public int PalletCount { get; set; }
}

public class StockTakeItemInput
{
    public int ProductId { get; set; }
    public int PalletCount { get; set; }
}

public class StockTakeInput
{
    public int LocationId { get; set; }
    public List<StockTakeItemInput> Items { get; set; } = [];
}

public class StockTransferInput
{
    public int FromLocationId { get; set; }
    public int ToLocationId { get; set; }
    public List<StockTakeItemInput> Items { get; set; } = [];
}

public class ManualAddInput
{
    public int ProductId { get; set; }
    public int LocationId { get; set; }
    public int PalletCount { get; set; }
}
