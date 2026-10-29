using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Globeweigh.Api.Data.Auditing;

/// <summary>One audited entity: what to call it, which record its changes are filed under, and
/// what not to bother recording.</summary>
public sealed class AuditedEntity
{
    /// <summary>What the trail calls it. Stored on every row, so changing it orphans the rows
    /// already written under the old name — pick it once.</summary>
    public required string EntityName { get; init; }

    /// <summary>The record the change is filed under. Usually the entity's own key, but a CHILD
    /// entity files under its PARENT: what happened to an order's line belongs on that order's
    /// trail, not on a trail of its own that nobody would think to look at.</summary>
    public required Func<EntityEntry, int> FileUnder { get; init; }

    /// <summary>Properties not worth a row. Keys, columns that never move, and ids that only ever
    /// travel with the readable name beside them.</summary>
    public IReadOnlySet<string> Ignore { get; init; } = new HashSet<string>();

    /// <summary>Properties recorded but NOT shown: the office asked for them to be kept off the
    /// screen. Still written, unlike <see cref="Ignore"/>, so one of them can be shown again by
    /// taking it out of this set, with no gap in the record for the time it was hidden.</summary>
    public IReadOnlySet<string> Hidden { get; init; } = new HashSet<string>();

    /// <summary>Fields the record is not born with, because whoever creates it cannot know them.
    /// The FIRST value one of these gets is its initial entry, not a change to it, so it is not
    /// audited; every edit after that is.
    ///
    /// <para>Deliberately a named list and not a blanket "empty to something is not a change"
    /// rule. DateDeleted going from empty to a date is a soft delete and DateDispatched going from
    /// empty to a date is an order leaving the yard — both are exactly what a trail exists to
    /// catch, and a blanket rule would swallow them silently.</para></summary>
    public IReadOnlySet<string> EnteredAfterwards { get; init; } = new HashSet<string>();

    /// <summary>How a raw column value READS on the trail, for the columns the general formatting
    /// gets wrong. Given the value, returns what to record — so "Customer 41 → 58" reads as
    /// "Customer  Greenvale → Wilson Country", and a price keeps its pence.</summary>
    public IReadOnlyDictionary<string, Func<GbEvergreenContext, object?, string?>> Reads { get; init; }
        = new Dictionary<string, Func<GbEvergreenContext, object?, string?>>();

    /// <summary>Which line of the parent record a change is about — the product on an order item.
    /// Null for an entity that IS the record.</summary>
    public Func<GbEvergreenContext, EntityEntry, string?>? Scope { get; init; }

    /// <summary>A child row appearing or going away is itself worth a row on the parent's trail
    /// (an order line added or removed by an edit). <see cref="Describe"/> says what to write in
    /// it. Off for an entity that IS the record: creating one is not a change to it.</summary>
    public bool AuditAddedAndRemoved { get; init; }

    /// <summary>The one line an Added / Removed row carries — "10 pallets @ 4.50".</summary>
    public Func<GbEvergreenContext, EntityEntry, string?>? Describe { get; init; }

    /// <summary>Whether the parent record ALREADY has rows like this one, asked of the database
    /// before the save runs. It is what tells an order being created — filled with its first lines
    /// in the same breath — from an order being edited, so the first lines are not each reported
    /// as an addition to a record that did not exist a moment ago.</summary>
    public Func<GbEvergreenContext, int, bool>? AlreadyHasRows { get; init; }

    /// <summary>When, and by whom, the record was created — read from the record itself, not from
    /// a stored row, so every record ever made opens its trail with "Created", not only those made
    /// after auditing began. Null for an entity that is not the record (a line files under its
    /// parent, and the parent says when it was born).</summary>
    public Func<GbEvergreenContext, int, AuditCreation?>? Created { get; init; }

    /// <summary>How a field is named on screen. Anything missing falls back to the property name
    /// split on its capitals, which is right often enough that only the odd ones are listed.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    /// <summary>Fields that are ONE fact with another field when the two change in the same save.
    /// Picking finishing sets when AND by whom, and the office reads that as "picked 14:05 by
    /// Aidan", not as two changes — so the rider's value is appended to its carrier's
    /// ("22/09/2026 14:05 by Aidan McCann") and the rider's own row is dropped. Keyed by the
    /// rider. Applied on read (AuditController), so rows already written fold the same way as new
    /// ones; a rider that changed on its own, with no carrier row in the same save, keeps its
    /// row.</summary>
    public IReadOnlyDictionary<string, AuditRider> Riders { get; init; } = new Dictionary<string, AuditRider>();
}

/// <summary>A field that rides another on the trail (<see cref="AuditedEntity.Riders"/>): the
/// carrier's value, the word, then the rider's — "22/09/2026 14:05" + "by" + "Aidan McCann".</summary>
public sealed record AuditRider(string Carrier, string Word);

/// <summary>The birth of a record, as its trail reports it: when, and who (null when nobody was
/// recorded, as for orders that came across from the old system).</summary>
public sealed record AuditCreation(DateTime When, int? UserId, string? UserName);

/// <summary>The registry: which entities are audited. Adding one is an entry here and nothing
/// else — no schema change, and no code in whatever controller does the saving.
///
/// <para>Auditing everything was the alternative and would have been worse: picking, production
/// and the pallet tables write constantly, and a trail nobody can find the interesting row in is
/// not an audit trail. So it is opt-in, and today what is opted in is a dispatch order and its
/// lines.</para></summary>
public static class AuditedEntities
{
    /// <summary>The name a dispatch order's trail is filed under. Its lines file under it too, so
    /// one read gets everything that was done to the order.</summary>
    public const string DispatchOrder = "DispatchOrder";

    /// <summary>The name a weighbridge record's trail (a delivery, or a dispatch load's header) is
    /// filed under.</summary>
    public const string WeighbridgeRecordName = "WeighbridgeRecord";

    private static readonly Dictionary<Type, AuditedEntity> Registry = new()
    {
        [typeof(DispatchOrderEntity)] = new AuditedEntity
        {
            EntityName = DispatchOrder,
            FileUnder = e => (int)(e.Property(nameof(DispatchOrderEntity.Id)).CurrentValue ?? 0),
            Ignore = new HashSet<string>
            {
                nameof(DispatchOrderEntity.Id),
                nameof(DispatchOrderEntity.DateCreated),
                nameof(DispatchOrderEntity.CreatedById),
                // Written once at creation and never used again by Evergreen (the transfer-order
                // machinery came across from the old system and is always 0 here).
                nameof(DispatchOrderEntity.OrderTypeId),
                nameof(DispatchOrderEntity.TransferOrderNo),
                nameof(DispatchOrderEntity.TransferLocationId),
                // The address id always moves with AddressFull beside it, and AddressFull is the
                // half a person can read.
                nameof(DispatchOrderEntity.AddressId),
            },
            // Picking, as the office asked on 2026-09-22: the yard's completing, reopening and
            // ending-early are the picking app's business, and the order's trail is read for what
            // the OFFICE did to the order and when it left. Recorded still, in case that changes.
            Hidden = new HashSet<string>
            {
                nameof(DispatchOrderEntity.DatePickingComplete),
                nameof(DispatchOrderEntity.PickingCompletedById),
                nameof(DispatchOrderEntity.PickingCompletedEarly),
                nameof(DispatchOrderEntity.DatePosPicked),
                nameof(DispatchOrderEntity.PosPickedById),
            },
            // An order is not born on a load: it is put on one later, and given its place in the
            // drop sequence with it. That first placing is the order joining the load — which the
            // load already shows — not a change to the order. Being moved to another load, or
            // resequenced, is a change and is recorded.
            EnteredAfterwards = new HashSet<string>
            {
                nameof(DispatchOrderEntity.WeighbridgeRecordId),
                nameof(DispatchOrderEntity.SortOrder),
            },
            Created = OrderCreation,
            Reads = new Dictionary<string, Func<GbEvergreenContext, object?, string?>>
            {
                [nameof(DispatchOrderEntity.CustomerId)] = CustomerName,
                [nameof(DispatchOrderEntity.HaulierId)] = HaulierName,
                [nameof(DispatchOrderEntity.PickingCompletedById)] = UserName,
                [nameof(DispatchOrderEntity.SalesRepId)] = UserName,
                // 0 is "not on a load" rather than load zero.
                [nameof(DispatchOrderEntity.WeighbridgeRecordId)] = (_, v) =>
                    Key(v) is { } id and > 0 ? $"Load {id}" : null,
            },
            Riders = new Dictionary<string, AuditRider>
            {
                // Who finished picking rides when it finished: one row, "22/09/2026 14:05 by
                // Aidan McCann", rather than a row for the time and another for the name.
                [nameof(DispatchOrderEntity.PickingCompletedById)] =
                    new AuditRider(nameof(DispatchOrderEntity.DatePickingComplete), "by"),
            },
            Labels = new Dictionary<string, string>
            {
                [nameof(DispatchOrderEntity.OrderNo)] = "Order No",
                [nameof(DispatchOrderEntity.CustomerId)] = "Customer",
                [nameof(DispatchOrderEntity.HaulierId)] = "Haulier",
                [nameof(DispatchOrderEntity.AddressFull)] = "Address",
                [nameof(DispatchOrderEntity.PoNumber)] = "PO Number",
                [nameof(DispatchOrderEntity.VehicleReg)] = "Vehicle Reg",
                [nameof(DispatchOrderEntity.TrailerNo)] = "Trailer No",
                [nameof(DispatchOrderEntity.Comments)] = "Delivery Note Comments",
                [nameof(DispatchOrderEntity.PickingComments)] = "Picking Comments",
                [nameof(DispatchOrderEntity.HasPos)] = "POS Board",
                [nameof(DispatchOrderEntity.SalesRepId)] = "Sales Rep",
                [nameof(DispatchOrderEntity.DeliveryDate)] = "Delivery Date",
                [nameof(DispatchOrderEntity.WeighbridgeRecordId)] = "Load",
                [nameof(DispatchOrderEntity.SortOrder)] = "Delivery Order",
                [nameof(DispatchOrderEntity.DatePickingComplete)] = "Picking Complete",
                [nameof(DispatchOrderEntity.PickingCompletedById)] = "Picking Completed By",
                [nameof(DispatchOrderEntity.PickingCompletedEarly)] = "Picking Ended Early",
                [nameof(DispatchOrderEntity.DateDispatched)] = "Dispatched",
                [nameof(DispatchOrderEntity.DateDeleted)] = "Deleted",
                [RecordField] = "Order",
            },
        },

        // An order's LINES, filed under the order. A line's quantity changing, and a line being
        // added or dropped by an edit, are both "somebody changed this order" — the office asks
        // that question of the order, not of a line it can no longer see.
        [typeof(DispatchOrderItemEntity)] = new AuditedEntity
        {
            EntityName = DispatchOrder,
            FileUnder = e => (int)(e.Property(nameof(DispatchOrderItemEntity.OrderId)).CurrentValue ?? 0),
            Ignore = new HashSet<string>
            {
                nameof(DispatchOrderItemEntity.Id),
                nameof(DispatchOrderItemEntity.OrderId),
                nameof(DispatchOrderItemEntity.DateCreated),
                // The product IS the line: an edit reconciles by product, so a line never changes
                // which product it is for. It is the scope of every row instead.
                nameof(DispatchOrderItemEntity.ProductId),
            },
            Scope = (db, e) => ProductName(db, e.Property(nameof(DispatchOrderItemEntity.ProductId)).CurrentValue),
            Reads = new Dictionary<string, Func<GbEvergreenContext, object?, string?>>
            {
                // Money keeps its pence: the general decimal format drops a trailing zero, and
                // "3.43 -> 3.6" is a price nobody ever typed.
                [nameof(DispatchOrderItemEntity.Price)] = (_, v) => Money(v),
            },
            AuditAddedAndRemoved = true,
            AlreadyHasRows = (db, orderId) =>
                db.Set<DispatchOrderItemEntity>().AsNoTracking().Any(i => i.OrderId == orderId),
            Describe = (_, e) =>
            {
                var qty = e.Property(nameof(DispatchOrderItemEntity.Quantity)).CurrentValue as int?;
                var price = e.Property(nameof(DispatchOrderItemEntity.Price)).CurrentValue as decimal?;
                var pallets = qty == null ? "No quantity" : $"{qty} pallet{(qty == 1 ? "" : "s")}";
                return price == null ? pallets : $"{pallets} @ {price:0.00}";
            },
            Labels = new Dictionary<string, string>
            {
                [nameof(DispatchOrderItemEntity.Quantity)] = "Quantity (pallets)",
                ["Line"] = "Order Line",
            },
        },

        // A weighbridge record — the lorry, its weights and its docket. Opted in 2026-09-25 for
        // the Deliveries/Incoming "View" panel, which shows an incoming delivery's trail. The same
        // table holds dispatch loads, so their header edits are recorded under the same name too;
        // the picking app's own stamps are ignored so the trail stays the office's.
        [typeof(WeighbridgeRecord)] = new AuditedEntity
        {
            EntityName = WeighbridgeRecordName,
            FileUnder = e => (int)(e.Property(nameof(WeighbridgeRecord.Id)).CurrentValue ?? 0),
            Ignore = new HashSet<string>
            {
                nameof(WeighbridgeRecord.Id),
                nameof(WeighbridgeRecord.DateCreated),
                nameof(WeighbridgeRecord.IsOutgoing),
                // Stored copy of the haulier's name: it always moves with HaulierId, which is
                // recorded as the name already.
                nameof(WeighbridgeRecord.HaulierName),
                // The picking app's stamps on a dispatch load, not something the office edits.
                nameof(WeighbridgeRecord.DatePickingComplete),
                nameof(WeighbridgeRecord.DateTrailerConfirmed),
                nameof(WeighbridgeRecord.TrailerConfirmedById),
                nameof(WeighbridgeRecord.DateSampleWeightAdded),
            },
            Created = WeighbridgeCreation,
            Reads = new Dictionary<string, Func<GbEvergreenContext, object?, string?>>
            {
                [nameof(WeighbridgeRecord.HaulierId)] = HaulierName,
                // 0 kg is "not weighed yet", not a reading.
                [nameof(WeighbridgeRecord.WeightIn)] = (_, v) => Kg(v),
                [nameof(WeighbridgeRecord.WeightOut)] = (_, v) => Kg(v),
                [nameof(WeighbridgeRecord.SampleBoxWeight)] = (_, v) =>
                    v is decimal m ? $"{m.ToString("#,##0.##", System.Globalization.CultureInfo.InvariantCulture)} kg" : null,
            },
            Labels = new Dictionary<string, string>
            {
                [nameof(WeighbridgeRecord.HaulierId)] = "Haulier",
                [nameof(WeighbridgeRecord.VehicleReg)] = "Vehicle Reg",
                [nameof(WeighbridgeRecord.TrailerNo)] = "Trailer No",
                [nameof(WeighbridgeRecord.DocketNumber)] = "Docket No",
                [nameof(WeighbridgeRecord.BogName)] = "Bog Name",
                [nameof(WeighbridgeRecord.WeightIn)] = "Weight In",
                [nameof(WeighbridgeRecord.DateWeightIn)] = "Weighed In",
                [nameof(WeighbridgeRecord.WeightOut)] = "Weight Out",
                [nameof(WeighbridgeRecord.DateWeightOut)] = "Weighed Out",
                [nameof(WeighbridgeRecord.SampleBoxWeight)] = "Sample Weight",
                [nameof(WeighbridgeRecord.CollectionDate)] = "Collection Date",
                [nameof(WeighbridgeRecord.DateDeleted)] = "Deleted",
                [RecordField] = "Record",
            },
        },
    };

    /// <summary>The FieldName an Added / Removed row is written under — there is no one property
    /// that changed, the whole line came or went.</summary>
    public const string WholeRowField = "Line";

    /// <summary>The FieldName the record's own creation is reported under. Never stored: the
    /// creation row is read off the record when the trail is read.</summary>
    public const string RecordField = "Record";

    public static AuditedEntity? For(Type clrType) =>
        Registry.TryGetValue(clrType, out var audited) ? audited : null;

    /// <summary>Every field filed under this name whose first value is an initial entry rather
    /// than a change (<see cref="AuditedEntity.EnteredAfterwards"/>), across all the entities that
    /// share the name.</summary>
    public static IReadOnlySet<string> EnteredAfterwardsFor(string entityName) =>
        Registry.Values.Where(a => a.EntityName == entityName)
            .SelectMany(a => a.EnteredAfterwards)
            .ToHashSet();

    /// <summary>Every field filed under this name that is recorded but kept off the screen
    /// (<see cref="AuditedEntity.Hidden"/>), across the entities that share the name.</summary>
    public static IReadOnlySet<string> HiddenFor(string entityName) =>
        Registry.Values.Where(a => a.EntityName == entityName)
            .SelectMany(a => a.Hidden)
            .ToHashSet();

    /// <summary>Every rider filed under this name (<see cref="AuditedEntity.Riders"/>), across the
    /// entities that share the name.</summary>
    public static IReadOnlyDictionary<string, AuditRider> RidersFor(string entityName)
    {
        var riders = new Dictionary<string, AuditRider>();
        foreach (var audited in Registry.Values)
        {
            if (audited.EntityName != entityName) continue;
            foreach (var (field, rider) in audited.Riders) riders[field] = rider;
        }
        return riders;
    }

    /// <summary>When and by whom the record filed under this name was created, if the entity
    /// that IS the record says how to find out. Null when none does, or the record is gone.</summary>
    public static AuditCreation? CreationFor(string entityName, GbEvergreenContext db, int entityId)
    {
        foreach (var audited in Registry.Values)
        {
            if (audited.EntityName != entityName || audited.Created == null) continue;
            var created = audited.Created(db, entityId);
            if (created != null) return created;
        }
        return null;
    }

    // An order's birth: its own DateCreated and CreatedById. A CreatedById of 0 is an order that
    // came across from the old system with nobody recorded, and reads as no one rather than "#0".
    private static AuditCreation? OrderCreation(GbEvergreenContext db, int id)
    {
        var o = db.Set<DispatchOrderEntity>().AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.DateCreated, x.CreatedById })
            .FirstOrDefault();
        if (o?.DateCreated is not { } when) return null;
        var by = o.CreatedById > 0 ? o.CreatedById : (int?)null;
        return new AuditCreation(when, by, by == null ? null : UserName(db, by));
    }

    // A weighbridge record's birth: its DateCreated. The table has no CreatedById, so nobody is
    // named — the row reads "Created" and the time.
    private static AuditCreation? WeighbridgeCreation(GbEvergreenContext db, int id)
    {
        var when = db.Set<WeighbridgeRecord>().AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => (DateTime?)x.DateCreated)
            .FirstOrDefault();
        return when is { } w && w > DateTime.MinValue ? new AuditCreation(w, null, null) : null;
    }

    private static string? Kg(object? value) =>
        Key(value) is { } kg and > 0
            ? $"{kg.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture)} kg"
            : null;

    /// <summary>The on-screen name for a field. Falls back to splitting the property name on its
    /// capitals, so an entity with no labels at all still reads as English.</summary>
    public static string LabelFor(string entityName, string fieldName)
    {
        // Several entities can share a name (an order and its lines both file under
        // "DispatchOrder"), so every one of them gets a say before the fallback.
        foreach (var audited in Registry.Values)
        {
            if (audited.EntityName != entityName) continue;
            if (audited.Labels.TryGetValue(fieldName, out var label)) return label;
        }
        return Spaced(fieldName);
    }

    // ---- id -> name, for the columns a person cannot read ----
    //
    // One small query each, and only when that column actually changed. Read with AsNoTracking so
    // looking a name up inside a save never drags another entity into the change tracker.

    private static int? Key(object? value) => value switch
    {
        int i => i,
        long l => (int)l,
        _ => null,
    };

    private static string? Money(object? value) =>
        value is decimal m ? m.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : null;

    private static string? CustomerName(GbEvergreenContext db, object? id) =>
        Key(id) is { } key and > 0
            ? db.Set<CustomerSupplier>().AsNoTracking()
                  .Where(c => c.Id == key).Select(c => c.Name).FirstOrDefault() ?? $"#{key}"
            : null;

    private static string? HaulierName(GbEvergreenContext db, object? id) =>
        Key(id) is { } key and > 0
            ? db.Set<Haulier>().AsNoTracking()
                  .Where(h => h.Id == key).Select(h => h.Name).FirstOrDefault() ?? $"#{key}"
            : null;

    private static string? ProductName(GbEvergreenContext db, object? id) =>
        Key(id) is { } key and > 0
            ? db.Set<Product>().AsNoTracking()
                  .Where(p => p.Id == key).Select(p => p.Description).FirstOrDefault() ?? $"#{key}"
            : null;

    private static string? UserName(GbEvergreenContext db, object? id) =>
        Key(id) is { } key and > 0
            ? db.GlobeweighUsers.AsNoTracking()
                  .Where(u => u.Id == key)
                  .Select(u => ((u.FirstName ?? "") + " " + (u.Surname ?? "")).Trim())
                  .FirstOrDefault() is { Length: > 0 } name
                ? name
                : $"#{key}"
            : null;

    // "DeliveryDate" -> "Delivery Date". Runs of capitals ("No", "PO") are left alone rather than
    // split letter by letter.
    private static string Spaced(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
