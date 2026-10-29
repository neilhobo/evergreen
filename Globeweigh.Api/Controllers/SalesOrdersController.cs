using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Auditing;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// My Orders — the Sales role's one tab. A salesperson builds orders under their own login as
// drafts (dbo.SalesOrder, not DispatchOrder, so the office never sees a draft) and sends each
// to the office for approval when ready. Every call is scoped to the signed-in user: nobody
// sees or touches another salesperson's orders here.
//
// The office answers each sent order from Orders / Dispatch: approve (it becomes a real
// DispatchOrder) or return it with a note. Once approved, its progress (on a load,
// dispatched) is read from that order.
[ApiController]
[Authorize]
[Route("api/sales-orders")]
public class SalesOrdersController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public SalesOrdersController(GbEvergreenContext db) => _db = db;

    // GET /api/sales-orders — the signed-in salesperson's orders, newest first, each with its
    // lines (the page works out pallets and value from them) and where it has got to. Also the
    // orders the OFFICE entered with this rep as the order's Sales Rep (FromOffice, negative id =
    // minus the DispatchOrder id): read-only here, tracked through loading to dispatch.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SalesOrderRow>>> List(CancellationToken ct = default)
    {
        var me = this.GetUserId();
        var orders = await _db.Set<SalesOrderEntity>().AsNoTracking()
            .Where(o => o.SalesUserId == me && o.DateDeleted == null)
            .OrderByDescending(o => o.Id)
            .ToListAsync(ct);
        var rows = await ToRows(orders, ct);
        rows.AddRange(await OfficeRows(me, null, ct));
        return Ok(rows.OrderByDescending(r => r.DateCreated).ToList());
    }

    // GET /api/sales-orders/office — the office's view (Orders / Dispatch, "Orders (from sales)"):
    // every salesperson's orders that have been sent and are awaiting approval, longest waiting
    // first, each with the rep's name. Drafts stay private to their salesperson and are never here.
    [HttpGet("office")]
    public async Task<ActionResult<IEnumerable<SalesOrderRow>>> Office(CancellationToken ct = default)
    {
        var orders = await _db.Set<SalesOrderEntity>().AsNoTracking()
            .Where(o => o.DateDeleted == null && o.Status == SalesOrderEntity.StatusPending)
            .OrderBy(o => o.DateSent).ThenBy(o => o.Id)
            .ToListAsync(ct);
        var rows = await ToRows(orders, ct);

        var repIds = orders.Select(o => o.SalesUserId).Distinct().ToList();
        var reps = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => repIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.Surname}".Trim(), ct);
        for (var i = 0; i < rows.Count; i++)
            rows[i].SalesRepName = reps.GetValueOrDefault(orders[i].SalesUserId);
        return Ok(rows);
    }

    // GET /api/sales-orders/{id} — one order with its history, for the detail side. A negative
    // id is an office-entered order (see List); its history is just who entered it.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SalesOrderDetail>> Get(int id, CancellationToken ct = default)
    {
        if (id < 0)
        {
            var office = (await OfficeRows(this.GetUserId(), -id, ct)).FirstOrDefault();
            if (office == null) return NotFound();
            var d = await _db.Set<DispatchOrderEntity>().AsNoTracking().FirstAsync(o => o.Id == -id, ct);
            var by = await _db.GlobeweighUsers.AsNoTracking().Where(u => u.Id == d.CreatedById)
                .Select(u => ((u.FirstName ?? "") + " " + (u.Surname ?? "")).Trim()).FirstOrDefaultAsync(ct);
            return Ok(new SalesOrderDetail
            {
                Order = office,
                Events =
                [
                    new SalesOrderEventRow
                    {
                        Date = d.DateCreated ?? DateTime.MinValue,
                        Who = string.IsNullOrWhiteSpace(by) ? "Office" : by,
                        Text = $"Entered order {d.OrderNo} with you as the sales rep",
                    },
                ],
            });
        }

        var order = await Mine(id, ct, tracking: false);
        if (order == null) return NotFound();
        var row = (await ToRows(new List<SalesOrderEntity> { order }, ct))[0];

        var events = await _db.Set<SalesOrderEventEntity>().AsNoTracking()
            .Where(e => e.SalesOrderId == id)
            .OrderBy(e => e.DateCreated).ThenBy(e => e.Id)
            .ToListAsync(ct);
        var userIds = events.Select(e => e.UserId).Distinct().ToList();
        var names = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.Surname}".Trim(), ct);
        var me = this.GetUserId();

        return Ok(new SalesOrderDetail
        {
            Order = row,
            Events = events.Select(e => new SalesOrderEventRow
            {
                Date = e.DateCreated,
                Who = e.UserId == me ? "You" : names.GetValueOrDefault(e.UserId) ?? "Office",
                Text = e.Text,
                Changes = string.IsNullOrEmpty(e.Changes)
                    ? null
                    : System.Text.Json.JsonSerializer.Deserialize<List<string>>(e.Changes),
            }).ToList(),
        });
    }

    // POST /api/sales-orders — start a draft.
    [HttpPost]
    public async Task<ActionResult<SalesOrderRow>> Create([FromBody] SalesOrderInput input, CancellationToken ct = default)
    {
        if (input.CustomerId <= 0) return BadRequest(new { message = "Choose a customer." });
        if (await NotMyCustomer(input.CustomerId, ct)) return BadRequest(new { message = NotMyCustomerMessage });

        var order = new SalesOrderEntity
        {
            SalesUserId = this.GetUserId(),
            Status = SalesOrderEntity.StatusDraft,
            DateCreated = DateTime.Now,
        };
        Apply(order, input);
        _db.Add(order);
        await _db.SaveChangesAsync(ct);

        AddItems(order.Id, input.Items);
        // A new order lists no lines in its history, except one priced from another zone than
        // the customer's: that is the rep's call, and it is kept on record from the start.
        var zoneChanges = await DescribeChanges(new SalesOrderEntity { CustomerId = input.CustomerId },
            [], input, ct, zonesOnly: true);
        AddEvent(order.Id, input.CopiedFromId switch
        {
            > 0 and var from => $"Started as a copy of {Ref(from)}",
            < 0 and var office => $"Started as a copy of office order {await OrderNoOf(-office, ct)}",
            _ => "Started the order",
        }, zoneChanges);
        await _db.SaveChangesAsync(ct);
        return Ok((await ToRows(new List<SalesOrderEntity> { order }, ct))[0]);
    }

    // PUT /api/sales-orders/{id} — edit a draft (or a returned order). Once it is with the
    // office it is theirs until they approve it or send it back (or it is taken back).
    [HttpPut("{id:int}")]
    public async Task<ActionResult<SalesOrderRow>> Update(int id, [FromBody] SalesOrderInput input, CancellationToken ct = default)
    {
        var order = await Mine(id, ct);
        if (order == null) return NotFound();
        if (!IsEditable(order)) return BadRequest(new { message = "Only a draft can be changed. Take it back from the office first." });
        if (input.CustomerId <= 0) return BadRequest(new { message = "Choose a customer." });
        if (await NotMyCustomer(input.CustomerId, ct)) return BadRequest(new { message = NotMyCustomerMessage });

        // What this save changes, worked out before anything is overwritten. A save that changes
        // nothing writes no history.
        var oldItems = await _db.Set<SalesOrderItemEntity>().Where(i => i.SalesOrderId == id).ToListAsync(ct);
        var changes = await DescribeChanges(order, oldItems, input, ct);

        Apply(order, input);
        _db.RemoveRange(oldItems);
        AddItems(id, input.Items);
        if (changes.Count > 0) AddEvent(id, "Changed the order", changes);
        await _db.SaveChangesAsync(ct);
        return Ok((await ToRows(new List<SalesOrderEntity> { order }, ct))[0]);
    }

    // POST /api/sales-orders/{id}/send — hand a draft (or a corrected returned order) to the office.
    [HttpPost("{id:int}/send")]
    public async Task<ActionResult> Send(int id, CancellationToken ct = default)
    {
        var order = await Mine(id, ct);
        if (order == null) return NotFound();
        if (!IsEditable(order)) return BadRequest(new { message = "This order has already been sent." });

        var items = await _db.Set<SalesOrderItemEntity>().AsNoTracking()
            .Where(i => i.SalesOrderId == id).ToListAsync(ct);
        if (items.Count == 0 && !order.HasPos)
            return BadRequest(new { message = "Add at least one item before sending the order." });
        if (items.Any(i => (i.Quantity ?? 0) <= 0))
            return BadRequest(new { message = "Every item needs a quantity before the order is sent." });
        var hasAddresses = await _db.Set<CustomerSupplierAddress>().AsNoTracking()
            .AnyAsync(a => a.CustomerSupplierId == order.CustomerId, ct);
        if (hasAddresses && order.AddressId <= 0)
            return BadRequest(new { message = "Choose a delivery address before sending the order." });

        if (await NotMyCustomer(order.CustomerId, ct)) return BadRequest(new { message = NotMyCustomerMessage });

        var resend = order.Status == SalesOrderEntity.StatusReturned;
        order.Status = SalesOrderEntity.StatusPending;
        order.DateSent = DateTime.Now;
        AddEvent(id, resend ? "Resent to the office" : "Sent to the office for approval");
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/sales-orders/{id}/recall — take an order back from the office before they
    // have approved it, to change it.
    [HttpPost("{id:int}/recall")]
    public async Task<ActionResult> Recall(int id, CancellationToken ct = default)
    {
        var order = await Mine(id, ct);
        if (order == null) return NotFound();
        if (order.Status != SalesOrderEntity.StatusPending)
            return BadRequest(new { message = "Only an order waiting for approval can be taken back." });

        order.Status = SalesOrderEntity.StatusDraft;
        order.DateSent = null;
        AddEvent(id, "Took the order back to draft");
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/sales-orders/{id} — throw away a draft. Nothing the office has (or has
    // approved) can be deleted from here.
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        var order = await Mine(id, ct);
        if (order == null) return NotFound();
        if (!IsEditable(order)) return BadRequest(new { message = "Only a draft can be deleted." });
        order.DateDeleted = DateTime.Now;
        AddEvent(id, "Deleted the draft");
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/sales-orders/{id}/approve — the office makes a sent order a real open order.
    // The body is the order as the office saved it in the order dialog: what the rep sent, plus
    // the haulier, vehicle and trailer, and any change the office made. The sales order is
    // brought into line with it (the rep sees what was approved) and the changes are listed in
    // its history under "Approved as order N".
    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult> Approve(int id, [FromBody] OrderInput input, CancellationToken ct = default)
    {
        if (await IsSalesUser(ct)) return Forbid();
        if (input.CustomerId <= 0) return BadRequest(new { message = "Choose a customer." });

        // One transaction: the order number is only spent if the sales order is marked too.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var order = await _db.Set<SalesOrderEntity>().FirstOrDefaultAsync(o => o.Id == id && o.DateDeleted == null, ct);
        if (order == null) return NotFound();
        if (order.Status != SalesOrderEntity.StatusPending)
            return BadRequest(new { message = $"{Ref(id)} is no longer waiting for approval — the rep may have taken it back, or it has already been answered." });

        input.OrderNo = null; // a new order always takes the next number
        input.SalesRepId = order.SalesUserId; // the rep who sent it, fixed from here on
        input.Items = input.Items.Where(i => i.ProductId > 0).ToList();
        var dispatch = await DispatchController.InsertOrder(_db, input, this.GetUserId(), ct);

        var asSent = new SalesOrderInput
        {
            CustomerId = input.CustomerId,
            AddressId = input.AddressId,
            DeliveryDate = input.DeliveryDate,
            PoNumber = input.PoNumber,
            Comments = input.Comments,
            PickingComments = input.PickingComments,
            HasPos = input.HasPos,
            Items = input.Items,
        };
        var oldItems = await _db.Set<SalesOrderItemEntity>().Where(i => i.SalesOrderId == id).ToListAsync(ct);
        await AuditRepZoneChoices(order, oldItems, dispatch, input.Items, ct);
        var changes = await DescribeChanges(order, oldItems, asSent, ct);
        if (changes.Count > 0)
        {
            Apply(order, asSent);
            _db.RemoveRange(oldItems);
            AddItems(id, asSent.Items);
        }

        order.Status = SalesOrderEntity.StatusApproved;
        order.DispatchOrderId = dispatch.Id;
        order.DateApproved = DateTime.Now;
        order.OfficeNote = null;
        AddEvent(id, $"Approved as order {dispatch.OrderNo}", changes);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new { id = dispatch.Id, orderNo = dispatch.OrderNo });
    }

    // POST /api/sales-orders/{id}/return — the office sends a sent order back to its rep with
    // a note saying what to change. The rep can edit it and send it again.
    [HttpPost("{id:int}/return")]
    public async Task<ActionResult> Return(int id, [FromBody] ReturnInput input, CancellationToken ct = default)
    {
        if (await IsSalesUser(ct)) return Forbid();
        var note = Clean(input.Note);
        if (note == null) return BadRequest(new { message = "Say what needs changing, so the rep knows what to fix." });

        var order = await _db.Set<SalesOrderEntity>().FirstOrDefaultAsync(o => o.Id == id && o.DateDeleted == null, ct);
        if (order == null) return NotFound();
        if (order.Status != SalesOrderEntity.StatusPending)
            return BadRequest(new { message = $"{Ref(id)} is no longer waiting for approval — the rep may have taken it back, or it has already been answered." });

        order.Status = SalesOrderEntity.StatusReturned;
        order.OfficeNote = note;
        AddEvent(id, $"Returned by the office: {note}");
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/sales-orders/office/{id} — the office throws away a sent order (a duplicate,
    // a test, one phoned in another way). It goes from the rep's My Orders too; its history
    // keeps who deleted it.
    [HttpDelete("office/{id:int}")]
    public async Task<ActionResult> OfficeDelete(int id, CancellationToken ct = default)
    {
        if (await IsSalesUser(ct)) return Forbid();
        var order = await _db.Set<SalesOrderEntity>().FirstOrDefaultAsync(o => o.Id == id && o.DateDeleted == null, ct);
        if (order == null) return NotFound();
        if (order.Status != SalesOrderEntity.StatusPending)
            return BadRequest(new { message = $"{Ref(id)} is no longer waiting for approval — the rep may have taken it back, or it has already been answered." });

        order.DateDeleted = DateTime.Now;
        AddEvent(id, "Deleted by the office");
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- helpers ----

    // A Sales user orders only for the customers assigned to them (Customers grid, Sales Rep).
    // Anyone else using My Orders (the testing phase) may pick any customer.
    private const int RoleSales = 6;
    private const string NotMyCustomerMessage = "That customer is not assigned to you. Ask the office to make you its sales rep.";

    private async Task<bool> NotMyCustomer(int customerId, CancellationToken ct)
    {
        if (!await IsSalesUser(ct)) return false;
        var me = this.GetUserId();
        return !await _db.Set<CustomerSupplier>().AsNoTracking().AnyAsync(c => c.Id == customerId && c.SalesRepId == me, ct);
    }

    // A Sales-role user: answering orders (approve / return) is the office's, never a rep's.
    private async Task<bool> IsSalesUser(CancellationToken ct)
    {
        var me = this.GetUserId();
        return await _db.Set<UserRoleEntity>().AsNoTracking().AnyAsync(r => r.UserId == me && r.RoleId == RoleSales, ct)
            || await _db.GlobeweighUsers.AsNoTracking().AnyAsync(u => u.Id == me && u.RoleId == RoleSales, ct);
    }

    public static string Ref(int id) => $"S-{id:D4}";

    private static bool IsEditable(SalesOrderEntity o) =>
        o.Status == SalesOrderEntity.StatusDraft || o.Status == SalesOrderEntity.StatusReturned;

    private async Task<SalesOrderEntity?> Mine(int id, CancellationToken ct, bool tracking = true)
    {
        var me = this.GetUserId();
        var q = _db.Set<SalesOrderEntity>().Where(o => o.Id == id && o.SalesUserId == me && o.DateDeleted == null);
        return await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private static void Apply(SalesOrderEntity order, SalesOrderInput input)
    {
        order.CustomerId = input.CustomerId;
        order.AddressId = input.AddressId;
        order.DeliveryDate = input.DeliveryDate;
        order.PoNumber = Clean(input.PoNumber);
        order.Comments = Clean(input.Comments);
        order.PickingComments = Clean(input.PickingComments);
        order.HasPos = input.HasPos;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void AddItems(int orderId, List<OrderItemInput> items)
    {
        var n = 0;
        foreach (var i in items.Where(i => i.ProductId > 0))
            _db.Add(new SalesOrderItemEntity
            {
                SalesOrderId = orderId,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                Price = i.Price,
                CostingZoneId = i.CostingZoneId,
                SortOrder = n++,
            });
    }

    private void AddEvent(int orderId, string text, List<string>? changes = null) =>
        _db.Add(new SalesOrderEventEntity
        {
            SalesOrderId = orderId,
            UserId = this.GetUserId(),
            Text = text,
            Changes = changes is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(changes) : null,
            DateCreated = DateTime.Now,
        });

    // An edit, in sentences the salesperson (and later the office) can read: the header fields
    // old → new, then the lines — added, removed, and quantity or price changed. Lines are
    // matched by product, as the order holds each product once.
    // zonesOnly: just the lines priced from another zone than the customer's (a new order's
    // history mentions those and nothing else).
    private async Task<List<string>> DescribeChanges(
        SalesOrderEntity order, List<SalesOrderItemEntity> oldItems, SalesOrderInput input, CancellationToken ct,
        bool zonesOnly = false)
    {
        var changes = new List<string>();
        var newItems = input.Items.Where(i => i.ProductId > 0).ToList();

        // A line's zone, by name: its own (the rep pressed Change) or the customer's.
        var customerZoneId = await _db.Set<CustomerSupplier>().AsNoTracking()
            .Where(c => c.Id == input.CustomerId).Select(c => c.CostingZoneId).FirstOrDefaultAsync(ct);
        var zoneIds = oldItems.Select(i => i.CostingZoneId).Concat(newItems.Select(i => i.CostingZoneId))
            .OfType<int>().Append(customerZoneId).Distinct().ToList();
        var zones = await _db.Set<CostingZone>().AsNoTracking()
            .Where(z => zoneIds.Contains(z.Id))
            .ToDictionaryAsync(z => z.Id, AuditedEntities.ZoneLabel, ct);
        var customerZone = zones.GetValueOrDefault(customerZoneId) ?? "none";
        string ZoneName(int? id) => id is { } z ? zones.GetValueOrDefault(z) ?? $"Zone #{z}" : $"{customerZone} (customer's zone)";
        string ZoneNote(int? id) => id == null ? "" : $" — {ZoneName(id)} price, not the customer's zone ({customerZone})";

        if (zonesOnly)
        {
            var names = await ProductNames(newItems.Where(i => i.CostingZoneId != null).Select(i => i.ProductId), ct);
            foreach (var n in newItems.Where(i => i.CostingZoneId != null))
                changes.Add($"Added {names.GetValueOrDefault(n.ProductId)}: {Qty(n.Quantity)} at {Money(n.Price)}{ZoneNote(n.CostingZoneId)}");
            return changes;
        }

        if (order.CustomerId != input.CustomerId)
        {
            var names = await _db.Set<CustomerSupplier>().AsNoTracking()
                .Where(c => c.Id == order.CustomerId || c.Id == input.CustomerId)
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            changes.Add($"Customer: {names.GetValueOrDefault(order.CustomerId) ?? "—"} → {names.GetValueOrDefault(input.CustomerId) ?? "—"}");
        }
        if (order.AddressId != input.AddressId)
        {
            var names = await _db.Set<CustomerSupplierAddress>().AsNoTracking()
                .Where(a => a.Id == order.AddressId || a.Id == input.AddressId)
                .ToDictionaryAsync(a => a.Id, a => a.AddressName ?? a.Address1, ct);
            changes.Add($"Address: {names.GetValueOrDefault(order.AddressId) ?? "none"} → {names.GetValueOrDefault(input.AddressId) ?? "none"}");
        }
        if (order.DeliveryDate?.Date != input.DeliveryDate?.Date)
            changes.Add($"Delivery: {Day(order.DeliveryDate)} → {Day(input.DeliveryDate)}");
        Text("PO", order.PoNumber, input.PoNumber);
        Text("Delivery note comments", order.Comments, input.Comments);
        Text("Picking comments", order.PickingComments, input.PickingComments);
        if (order.HasPos != input.HasPos)
            changes.Add(input.HasPos ? "Added a POS board" : "Removed the POS board");

        var products = await ProductNames(oldItems.Select(i => i.ProductId).Concat(newItems.Select(i => i.ProductId)), ct);
        string Name(int productId) => products.GetValueOrDefault(productId) ?? $"#{productId}";

        foreach (var n in newItems)
        {
            var o = oldItems.FirstOrDefault(x => x.ProductId == n.ProductId);
            if (o == null)
            {
                changes.Add($"Added {Name(n.ProductId)}: {Qty(n.Quantity)} at {Money(n.Price)}{ZoneNote(n.CostingZoneId)}");
                continue;
            }
            if (o.Quantity != n.Quantity)
                changes.Add($"{Name(n.ProductId)}: {Qty(o.Quantity)} → {Qty(n.Quantity)}");
            if (o.CostingZoneId != n.CostingZoneId)
                changes.Add($"{Name(n.ProductId)} costing zone: {ZoneName(o.CostingZoneId)} → {ZoneName(n.CostingZoneId)}");
            if (o.Price != n.Price)
                changes.Add($"{Name(n.ProductId)} price: {Money(o.Price)} → {Money(n.Price)}");
        }
        foreach (var o in oldItems.Where(o => newItems.All(n => n.ProductId != o.ProductId)))
            changes.Add($"Removed {Name(o.ProductId)} ({Qty(o.Quantity)})");

        return changes;

        // Free text: quoted, and a long one shortened, so the list stays readable.
        void Text(string label, string? was, string? now)
        {
            was = Clean(was);
            now = Clean(now);
            if (was == now) return;
            changes.Add($"{label}: {Quote(was)} → {Quote(now)}");
        }
        static string Quote(string? s) =>
            s == null ? "none" : "\"" + (s.Length > 60 ? s[..57].Replace("\n", " ") + "…" : s.Replace("\n", " ")) + "\"";
        static string Day(DateTime? d) => d == null ? "ASAP" : d.Value.ToString("ddd d MMM");
        static string Qty(int? q) => q == null ? "no quantity" : q == 1 ? "1 pallet" : $"{q} pallets";
        static string Money(decimal? p) => p == null ? "no price" : p.Value.ToString("0.00");
    }

    // Products as the history names them: description and size ("Gro-Sure Multi 50L").
    private async Task<Dictionary<int, string>> ProductNames(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await _db.Set<Product>().AsNoTracking()
            .Where(p => list.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Size is { } s ? $"{(p.Description ?? $"#{p.Id}").Trim()} {s}L" : p.Description?.Trim() ?? $"#{p.Id}", ct);
    }

    // The order's own audit trail starts when the office approves it, so a line the rep priced
    // from another zone would otherwise reach it as nothing at all (a new order's lines are not
    // audited). Each such line gets a Costing Zone row in the rep's name, from the customer's
    // zone to the one they picked, beside the office's Created row.
    private async Task AuditRepZoneChoices(
        SalesOrderEntity order, List<SalesOrderItemEntity> repItems, DispatchOrderEntity dispatch,
        List<OrderItemInput> approved, CancellationToken ct)
    {
        var lines = approved.Where(i => i.CostingZoneId != null).ToList();
        if (lines.Count == 0) return;

        var customerZoneId = await _db.Set<CustomerSupplier>().AsNoTracking()
            .Where(c => c.Id == dispatch.CustomerId).Select(c => c.CostingZoneId).FirstOrDefaultAsync(ct);
        var customerZone = AuditedEntities.ZoneName(_db, customerZoneId) ?? AuditedEntities.CustomersZone;
        var products = await _db.Set<Product>().AsNoTracking()
            .Where(p => lines.Select(l => l.ProductId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Description, ct);
        var users = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.Id == order.SalesUserId || u.Id == this.GetUserId())
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.Surname}".Trim(), ct);
        var now = DateTime.Now;

        foreach (var line in lines)
        {
            // The rep's choice as sent; the office cannot change a line's zone, but should one
            // arrive changed it is the approver's doing, not the rep's.
            var byRep = repItems.Any(r => r.ProductId == line.ProductId && r.CostingZoneId == line.CostingZoneId);
            var who = byRep ? order.SalesUserId : this.GetUserId();
            _db.AuditTrail.Add(new AuditEntry
            {
                EntityName = AuditedEntities.DispatchOrder,
                EntityId = dispatch.Id,
                FieldName = nameof(DispatchOrderItemEntity.CostingZoneId),
                FieldScope = products.GetValueOrDefault(line.ProductId) ?? $"#{line.ProductId}",
                OldValue = customerZone,
                NewValue = AuditedEntities.ZoneName(_db, line.CostingZoneId),
                Action = AuditEntry.ActionUpdated,
                DateChanged = now,
                UserId = who,
                UserName = users.GetValueOrDefault(who),
                Source = byRep ? "My Orders" : null,
            });
        }
    }

    // Rows for the list: the order's own fields, its lines, the customer and address as text,
    // and — once approved — the dispatch order's number, load and dispatch date.
    private async Task<List<SalesOrderRow>> ToRows(List<SalesOrderEntity> orders, CancellationToken ct)
    {
        var ids = orders.Select(o => o.Id).ToList();
        var items = await _db.Set<SalesOrderItemEntity>().AsNoTracking()
            .Where(i => ids.Contains(i.SalesOrderId))
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .ToListAsync(ct);

        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var customers = await _db.Set<CustomerSupplier>().AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var addressIds = orders.Select(o => o.AddressId).Where(a => a > 0).Distinct().ToList();
        var addresses = await _db.Set<CustomerSupplierAddress>().AsNoTracking()
            .Where(a => addressIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        var dispatchIds = orders.Where(o => o.DispatchOrderId != null).Select(o => o.DispatchOrderId!.Value).ToList();
        var dispatch = await _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(d => dispatchIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);
        var loadIds = dispatch.Values.Where(d => d.WeighbridgeRecordId > 0).Select(d => d.WeighbridgeRecordId).Distinct().ToList();
        var loads = await _db.Set<WeighbridgeRecord>().AsNoTracking()
            .Where(w => loadIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, ct);

        return orders.Select(o =>
        {
            addresses.TryGetValue(o.AddressId, out var a);
            DispatchOrderEntity? d = null;
            if (o.DispatchOrderId is { } did) dispatch.TryGetValue(did, out d);
            WeighbridgeRecord? load = null;
            if (d is { WeighbridgeRecordId: > 0 }) loads.TryGetValue(d.WeighbridgeRecordId, out load);

            return new SalesOrderRow
            {
                Id = o.Id,
                Ref = Ref(o.Id),
                Status = StatusKey(o, d),
                CustomerId = o.CustomerId,
                CustomerName = customers.GetValueOrDefault(o.CustomerId),
                AddressId = o.AddressId,
                AddressText = a == null ? null : string.Join("\n", new[] { a.AddressName, a.Address1, a.Address2, a.Address3, a.Address4, a.Address5 }
                    .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim())),
                DeliveryDate = o.DeliveryDate,
                PoNumber = o.PoNumber,
                Comments = o.Comments,
                PickingComments = o.PickingComments,
                HasPos = o.HasPos,
                OfficeNote = o.OfficeNote,
                DateCreated = o.DateCreated,
                DateSent = o.DateSent,
                DateApproved = o.DateApproved,
                OrderNo = d?.OrderNo,
                LoadId = load?.Id,
                LoadDate = load?.CollectionDate,
                LoadHaulier = load?.HaulierName,
                DateDispatched = d?.DateDispatched,
                Items = items.Where(i => i.SalesOrderId == o.Id)
                    .Select(i => new OrderItemInput { ProductId = i.ProductId, Quantity = i.Quantity, Price = i.Price, CostingZoneId = i.CostingZoneId })
                    .ToList(),
            };
        }).ToList();
    }

    // Office-entered orders crediting this rep (DispatchOrder.SalesRepId), not ones that came
    // from a SalesOrder (those are already in the list as the rep's own). Deleted ones are left
    // out: an office mistake removed is not the rep's business. onlyId: one order, for Get.
    private async Task<List<SalesOrderRow>> OfficeRows(int rep, int? onlyId, CancellationToken ct)
    {
        var fromSales = _db.Set<SalesOrderEntity>().Where(s => s.DispatchOrderId != null).Select(s => s.DispatchOrderId!.Value);
        var q = _db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(d => d.SalesRepId == rep && d.DateDeleted == null && !fromSales.Contains(d.Id));
        if (onlyId is { } one) q = q.Where(d => d.Id == one);
        var orders = await q.ToListAsync(ct);
        if (orders.Count == 0) return [];

        var ids = orders.Select(d => d.Id).ToList();
        var items = await _db.Set<DispatchOrderItemEntity>().AsNoTracking()
            .Where(i => ids.Contains(i.OrderId)).OrderBy(i => i.Id).ToListAsync(ct);
        var customerIds = orders.Select(d => d.CustomerId).Distinct().ToList();
        var customers = await _db.Set<CustomerSupplier>().AsNoTracking()
            .Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var addressIds = orders.Select(d => d.AddressId).Where(a => a > 0).Distinct().ToList();
        var addresses = await _db.Set<CustomerSupplierAddress>().AsNoTracking()
            .Where(a => addressIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var loadIds = orders.Where(d => d.WeighbridgeRecordId > 0).Select(d => d.WeighbridgeRecordId).Distinct().ToList();
        var loads = await _db.Set<WeighbridgeRecord>().AsNoTracking()
            .Where(w => loadIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
        var approved = new SalesOrderEntity { Status = SalesOrderEntity.StatusApproved };

        return orders.Select(d =>
        {
            addresses.TryGetValue(d.AddressId, out var a);
            loads.TryGetValue(d.WeighbridgeRecordId, out var load);
            return new SalesOrderRow
            {
                Id = -d.Id,
                Ref = "Office order",
                FromOffice = true,
                Status = StatusKey(approved, d),
                CustomerId = d.CustomerId,
                CustomerName = customers.GetValueOrDefault(d.CustomerId),
                AddressId = d.AddressId,
                AddressText = a == null ? null : string.Join("\n", new[] { a.AddressName, a.Address1, a.Address2, a.Address3, a.Address4, a.Address5 }
                    .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim())),
                DeliveryDate = d.DeliveryDate,
                PoNumber = d.PoNumber,
                Comments = d.Comments,
                PickingComments = d.PickingComments,
                HasPos = d.HasPos,
                DateCreated = d.DateCreated ?? DateTime.MinValue,
                DateApproved = d.DateCreated,
                OrderNo = d.OrderNo,
                LoadId = load?.Id,
                LoadDate = load?.CollectionDate,
                LoadHaulier = load?.HaulierName,
                DateDispatched = d.DateDispatched,
                Items = items.Where(i => i.OrderId == d.Id)
                    .Select(i => new OrderItemInput { ProductId = i.ProductId, Quantity = i.Quantity, Price = i.Price, CostingZoneId = i.CostingZoneId })
                    .ToList(),
            };
        }).ToList();
    }

    private async Task<string> OrderNoOf(int dispatchOrderId, CancellationToken ct) =>
        (await _db.Set<DispatchOrderEntity>().AsNoTracking().Where(d => d.Id == dispatchOrderId)
            .Select(d => (int?)d.OrderNo).FirstOrDefaultAsync(ct))?.ToString() ?? $"#{dispatchOrderId}";

    // Where the order has got to, as the page names it.
    private static string StatusKey(SalesOrderEntity o, DispatchOrderEntity? d) => o.Status switch
    {
        SalesOrderEntity.StatusPending => "pending",
        SalesOrderEntity.StatusReturned => "returned",
        SalesOrderEntity.StatusApproved when d == null => "approved",
        SalesOrderEntity.StatusApproved when d.DateDeleted != null => "cancelled",
        SalesOrderEntity.StatusApproved when d.DateDispatched != null => "dispatched",
        SalesOrderEntity.StatusApproved when d.WeighbridgeRecordId > 0 => "planned",
        SalesOrderEntity.StatusApproved => "approved",
        _ => "draft",
    };
}

public class SalesOrderInput
{
    public int CustomerId { get; set; }
    public int AddressId { get; set; } // 0 = the customer has no addresses
    public DateTime? DeliveryDate { get; set; } // null = ASAP
    public string? PoNumber { get; set; }
    public string? Comments { get; set; }
    public string? PickingComments { get; set; }
    public bool HasPos { get; set; }
    public int? CopiedFromId { get; set; } // "Copy as new order": noted in the new draft's history
    public List<OrderItemInput> Items { get; set; } = new();
}

public class ReturnInput
{
    public string? Note { get; set; }
}

public class SalesOrderRow
{
    public int Id { get; set; }
    public string Ref { get; set; } = "";
    // draft | pending | returned | approved | planned | dispatched | cancelled
    public string Status { get; set; } = "draft";
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int AddressId { get; set; }
    public string? AddressText { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string? PoNumber { get; set; }
    public string? Comments { get; set; }
    public string? PickingComments { get; set; }
    public bool HasPos { get; set; }
    public string? OfficeNote { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateSent { get; set; }
    public DateTime? DateApproved { get; set; }
    public int? OrderNo { get; set; }
    public int? LoadId { get; set; } // the load (WeighbridgeRecord) the order is on, for View Load Detail
    public DateTime? LoadDate { get; set; }
    public string? LoadHaulier { get; set; }
    public DateTime? DateDispatched { get; set; }
    public string? SalesRepName { get; set; } // the office list only (GET office)
    // Entered by the office with this rep as its Sales Rep, not sent from My Orders: read-only,
    // Id is minus the DispatchOrder id.
    public bool FromOffice { get; set; }
    public List<OrderItemInput> Items { get; set; } = new();
}

public class SalesOrderEventRow
{
    public DateTime Date { get; set; }
    public string Who { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string>? Changes { get; set; } // an edit's changes; null for every other event
}

public class SalesOrderDetail
{
    public SalesOrderRow Order { get; set; } = new();
    public List<SalesOrderEventRow> Events { get; set; } = new();
}
