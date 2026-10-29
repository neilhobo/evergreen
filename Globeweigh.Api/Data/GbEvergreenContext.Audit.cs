using System.Globalization;
using Globeweigh.Api.Data.Auditing;
using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Globeweigh.Api.Data;

// ---- The audit trail ----
//
// Written HERE, in the save, rather than by whoever made the change. That is the whole point: a
// controller cannot forget to audit, a new endpoint is audited the day it is written, and there is
// no way to change an audited record through this context without it being recorded.
// Which entities are audited is Data/Auditing/AuditedEntities.cs.
public partial class GbEvergreenContext
{
    // Who is making the changes in this request. Optional so the context still works with no
    // request around it — a design-time build, a migration run or a background job has nothing to
    // attribute to, and gets an unattributed trail rather than a crash.
    private readonly AuditScope? _audit;

    public GbEvergreenContext(DbContextOptions<GbEvergreenContext> options, AuditScope audit)
        : base(options)
    {
        _audit = audit;
    }

    public DbSet<AuditEntry> AuditTrail => Set<AuditEntry>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        WriteAuditTrail();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        WriteAuditTrail();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void WriteAuditTrail()
    {
        var touched = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Modified or EntityState.Added or EntityState.Deleted)
            .ToList(); // adding the audit rows below mutates the tracker we are walking
        if (touched.Count == 0) return;

        var now = DateTime.Now;
        // Rows that are only written if this is NOT the first time that field has ever been
        // recorded — resolved together, below, so it costs one query rather than one per field.
        var maybeFirstEntry = new List<AuditEntry>();

        foreach (var entry in touched)
        {
            var audited = AuditedEntities.For(entry.Entity.GetType());
            if (audited == null) continue;

            var entityId = audited.FileUnder(entry);
            if (entityId == 0) continue; // nothing to file it under; better none than a row on id 0

            if (entry.State is EntityState.Added or EntityState.Deleted)
            {
                var row = WholeRowEntry(entry, audited, entityId, now);
                if (row != null) AuditTrail.Add(row);
                continue;
            }

            var scope = audited.Scope?.Invoke(this, entry);
            foreach (var prop in entry.Properties)
            {
                if (!prop.IsModified) continue;
                if (audited.Ignore.Contains(prop.Metadata.Name)) continue;

                // EF marks a property modified when it was ASSIGNED, not when it changed value.
                // Saving a form that re-sends every field would otherwise write a row per field
                // saying nothing happened.
                var reads = audited.Reads.TryGetValue(prop.Metadata.Name, out var custom) ? custom : null;
                var before = reads != null ? reads(this, prop.OriginalValue) : Display(prop.OriginalValue);
                var after = reads != null ? reads(this, prop.CurrentValue) : Display(prop.CurrentValue);
                if (before == after) continue;

                var row = new AuditEntry
                {
                    EntityName = audited.EntityName,
                    EntityId = entityId,
                    FieldName = prop.Metadata.Name,
                    FieldScope = scope,
                    OldValue = before,
                    NewValue = after,
                    Action = AuditEntry.ActionUpdated,
                    DateChanged = now,
                    UserId = _audit?.UserId,
                    UserName = _audit?.UserName,
                    Source = _audit?.Source,
                };

                // A field the record is not born with, arriving from empty, MIGHT be its initial
                // entry — which is not a change and is not recorded. But it might equally be a
                // re-entry after someone cleared it, and that is a change like any other. Only the
                // trail knows which, so the trail is asked.
                if (string.IsNullOrEmpty(before) && audited.EnteredAfterwards.Contains(prop.Metadata.Name))
                    maybeFirstEntry.Add(row);
                else
                    AuditTrail.Add(row);
            }
        }

        foreach (var row in KeepThoseWithHistory(maybeFirstEntry)) AuditTrail.Add(row);
    }

    // A child row that appeared or went away — an order line added or dropped by an edit. Only for
    // entities that asked for it: for a record that IS the record, being created is not a change
    // to it, and auditing inserts would put a row against every field of every new order for no
    // reading anyone would ever do.
    private AuditEntry? WholeRowEntry(EntityEntry entry, AuditedEntity audited, int entityId, DateTime now)
    {
        if (!audited.AuditAddedAndRemoved) return null;
        // A record being created is filled with its first lines in the same breath; that is the
        // record being made, not edited. The parent having rows already is what tells the two
        // apart — at this point the save has not run, so lines this edit is dropping still count.
        if (audited.AlreadyHasRows != null && !audited.AlreadyHasRows(this, entityId)) return null;

        var added = entry.State == EntityState.Added;
        var what = audited.Describe?.Invoke(this, entry);
        return new AuditEntry
        {
            EntityName = audited.EntityName,
            EntityId = entityId,
            FieldName = AuditedEntities.WholeRowField,
            FieldScope = audited.Scope?.Invoke(this, entry),
            OldValue = added ? null : what,
            NewValue = added ? what : null,
            Action = added ? AuditEntry.ActionAdded : AuditEntry.ActionRemoved,
            DateChanged = now,
            UserId = _audit?.UserId,
            UserName = _audit?.UserName,
            Source = _audit?.Source,
        };
    }

    // Of the rows that came from an empty value, the ones for a field this record HAS been audited
    // on before — so a genuine first entry is dropped and a re-entry after a clear is kept. One
    // query for the lot; the list is empty on almost every save.
    private List<AuditEntry> KeepThoseWithHistory(List<AuditEntry> candidates)
    {
        if (candidates.Count == 0) return candidates;

        var names = candidates.Select(c => c.EntityName).Distinct().ToList();
        var ids = candidates.Select(c => c.EntityId).Distinct().ToList();
        var fields = candidates.Select(c => c.FieldName).Distinct().ToList();

        var seen = AuditTrail.AsNoTracking()
            .Where(a => names.Contains(a.EntityName) && ids.Contains(a.EntityId) && fields.Contains(a.FieldName))
            .Select(a => new { a.EntityName, a.EntityId, a.FieldName })
            .Distinct()
            .ToList()
            .Select(a => (a.EntityName, a.EntityId, a.FieldName))
            .ToHashSet();

        return candidates
            .Where(c => seen.Contains((c.EntityName, c.EntityId, c.FieldName)))
            .ToList();
    }

    // Formatted for a person, because an audit trail is read by one. Null stays null so that "set
    // for the first time" and "cleared" both read correctly rather than as the word "null".
    private static string? Display(object? value) => value switch
    {
        null => null,
        bool b => b ? "Yes" : "No",
        DateTime d => d.TimeOfDay == TimeSpan.Zero
            ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.###", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
