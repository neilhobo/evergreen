using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Auditing;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

/// <summary>Reading the audit trail. Generic by entity name and id, so a screen for any audited
/// record asks the same way — nothing here knows what a dispatch order is.
///
/// <para>Signed in only. A trail says who did what and when, which is exactly the sort of thing
/// that should not be readable by anything that happens to reach the port. Writing it needs no
/// endpoint at all: it happens in SaveChanges.</para></summary>
[ApiController]
[Authorize]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public AuditController(GbEvergreenContext db) => _db = db;

    /// <summary>Everything recorded against one record, newest first. An empty list is the normal
    /// answer for a record nobody has changed since it was created.</summary>
    [HttpGet("{entityName}/{entityId:int}")]
    public async Task<ActionResult<IEnumerable<AuditEntryDto>>> ForEntity(
        string entityName, int entityId, [FromQuery] int take = 500, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityName))
            return BadRequest(new { message = "An entity name is required." });
        take = Math.Clamp(take, 1, 2000);

        var name = entityName.Trim();
        var rows = await _db.AuditTrail.AsNoTracking()
            .Where(a => a.EntityName == name && a.EntityId == entityId)
            .OrderByDescending(a => a.DateChanged).ThenByDescending(a => a.Id)
            .Take(take)
            .Select(a => new AuditEntryDto
            {
                Id = a.Id,
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                FieldName = a.FieldName,
                FieldScope = a.FieldScope,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                Action = a.Action,
                DateChanged = a.DateChanged,
                UserId = a.UserId,
                UserName = a.UserName,
                Source = a.Source,
            })
            .ToListAsync(ct);

        // Fields recorded but kept off the screen (AuditedEntity.Hidden) go first, whenever their
        // rows were written.
        var hidden = AuditedEntities.HiddenFor(name);
        if (hidden.Count > 0) rows.RemoveAll(r => hidden.Contains(r.FieldName));

        // A field the record is not born with (an order's load) gets its FIRST value as its initial
        // entry, which is not a change and is not shown. The writer applies that rule as it goes
        // (GbEvergreenContext.WriteAuditTrail); it is applied again here so a row written before
        // its field joined the list reads the same way as one that was never written.
        var entered = AuditedEntities.EnteredAfterwardsFor(name);
        if (entered.Count > 0)
        {
            var fields = entered.ToList();
            var earliest = await _db.AuditTrail.AsNoTracking()
                .Where(a => a.EntityName == name && a.EntityId == entityId && fields.Contains(a.FieldName))
                .OrderBy(a => a.DateChanged).ThenBy(a => a.Id)
                .Select(a => new { a.Id, a.FieldName, a.OldValue })
                .ToListAsync(ct);
            var initial = earliest
                .GroupBy(a => a.FieldName)
                .Select(g => g.First())
                .Where(a => string.IsNullOrEmpty(a.OldValue))
                .Select(a => a.Id)
                .ToHashSet();
            if (initial.Count > 0) rows.RemoveAll(r => initial.Contains(r.Id));
        }

        // Two fields that are one fact (AuditedEntity.Riders): picking finishing sets when AND by
        // whom in the same save, and the office reads that as "picked 14:05 by Aidan", not as two
        // changes. The rider's value is appended to its carrier's on both sides of the change and
        // the rider's own row goes. Done on read, so rows already written fold like new ones. A
        // rider with no carrier row in the same save (a correction to that one field) keeps its
        // row. Rows written in one save share their DateChanged to the tick, which is how the
        // pair is found. Folded BEFORE the bounce rule below, so a reopen and a re-complete a moment
        // apart fold the name into both date rows rather than losing it as a bounced pair.
        var riders = AuditedEntities.RidersFor(name);
        if (riders.Count > 0)
        {
            var folded = new HashSet<int>();
            foreach (var r in rows)
            {
                if (r.Action != AuditEntry.ActionUpdated || !riders.TryGetValue(r.FieldName, out var rider)) continue;
                var carrier = rows.FirstOrDefault(c =>
                    c.Action == AuditEntry.ActionUpdated && c.FieldName == rider.Carrier
                    && c.FieldScope == r.FieldScope && c.DateChanged == r.DateChanged);
                if (carrier == null) continue;
                carrier.OldValue = Ride(carrier.OldValue, r.OldValue, rider.Word);
                carrier.NewValue = Ride(carrier.NewValue, r.NewValue, rider.Word);
                // The rider names who did it, and the screen shows that name as the row's who
                // where the change itself recorded nobody (the picking app posts anonymously).
                if (!string.IsNullOrEmpty(r.NewValue)) carrier.Actor = r.NewValue;
                folded.Add(r.Id);
            }
            if (folded.Count > 0) rows.RemoveAll(r => folded.Contains(r.Id));
        }

        // A date set to the moment of the change itself, with nothing there before, is an ACTION
        // being recorded — picking completed, dispatched, deleted — not a value being edited. The
        // screen shows such a row as the action alone with who and when, rather than as
        // "(not set) → 22/09/2026 12:23" under a timestamp that says the same thing. The value
        // is compared as written (dd/MM/yyyy HH:mm, to the minute; a rider's name may follow it).
        // Such a stamp taken back — the date cleared, nothing in its place (picking reopened) —
        // reads the same way: the action, "cleared", who and when.
        foreach (var r in rows)
        {
            if (r.Action != AuditEntry.ActionUpdated) continue;
            if (string.IsNullOrEmpty(r.OldValue))
            {
                if (StampedAt(r.NewValue) is { } stamped && Math.Abs((stamped - r.DateChanged).TotalMinutes) <= 2)
                    r.Stamp = true;
            }
            else if (string.IsNullOrEmpty(r.NewValue) && StampedAt(r.OldValue) != null)
            {
                r.StampCleared = true;
            }
        }

        // A load edit used to unlink every order on it and relink them a moment later, leaving
        // "Load 5021 → (cleared)" and "(not set) → Load 5021" on orders that never moved
        // (DispatchController.UpdateLoad; fixed 2026-09-22). A value that went away and came
        // straight back never changed, so such a pair is dropped on read — trails written before
        // the fix then read like those written after it.
        var bounce = new HashSet<int>();
        foreach (var g in rows.GroupBy(r => (r.FieldName, r.FieldScope)))
        {
            var ordered = g.OrderBy(r => r.DateChanged).ThenBy(r => r.Id).ToList();
            for (var i = 0; i + 1 < ordered.Count; i++)
            {
                var gone = ordered[i];
                var back = ordered[i + 1];
                if (gone.Action != AuditEntry.ActionUpdated || back.Action != AuditEntry.ActionUpdated) continue;
                if (string.IsNullOrEmpty(gone.OldValue) || !string.IsNullOrEmpty(gone.NewValue)) continue;
                if (!string.IsNullOrEmpty(back.OldValue) || back.NewValue != gone.OldValue) continue;
                if (back.DateChanged - gone.DateChanged > TimeSpan.FromSeconds(5)) continue;
                bounce.Add(gone.Id);
                bounce.Add(back.Id);
                i++;
            }
        }
        if (bounce.Count > 0) rows.RemoveAll(r => bounce.Contains(r.Id));

        // The label is worked out here rather than stored: it is how the field is NAMED today, and
        // renaming a heading should not mean rewriting history.
        foreach (var r in rows) r.FieldLabel = AuditedEntities.LabelFor(r.EntityName, r.FieldName);

        // Source used to fall back to the caller's address when the app sent no name for itself
        // (AuditScopeMiddleware; changed 2026-09-22 to name the app by its route instead). An
        // address means nothing to anyone reading the trail, so one already stored reads as no
        // source at all rather than being shown.
        foreach (var r in rows)
            if (r.Source != null && System.Net.IPAddress.TryParse(r.Source, out _)) r.Source = null;

        // The record's own creation closes the trail. It is read off the record, not the trail, so
        // an order made long before auditing began opens with it too — and it is why a trail is
        // never empty for a record that exists. Id 0: there is no stored row behind it.
        var created = AuditedEntities.CreationFor(name, _db, entityId);
        if (created != null)
        {
            rows.Add(new AuditEntryDto
            {
                Id = 0,
                EntityName = name,
                EntityId = entityId,
                FieldName = AuditedEntities.RecordField,
                FieldLabel = AuditedEntities.LabelFor(name, AuditedEntities.RecordField),
                Action = AuditEntry.ActionCreated,
                DateChanged = created.When,
                UserId = created.UserId,
                UserName = created.UserName,
            });
        }
        return Ok(rows);
    }

    // The date-time a trail value starts with — "22/09/2026 12:23", as GbEvergreenContext.Audit
    // writes one, whatever follows it (a rider's name) — or null for any other value. A date
    // alone (a delivery date) is not one: only a date-TIME records an action's moment.
    private static DateTime? StampedAt(string? value) =>
        value is { Length: >= 16 } v
        && DateTime.TryParseExact(v[..16], "dd/MM/yyyy HH:mm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var at)
            ? at
            : null;

    // "22/09/2026 14:05" + "by" + "Aidan McCann". Either half alone stands as it is, and neither
    // is still nothing, so a clear still reads as cleared.
    private static string? Ride(string? carrier, string? rider, string word) =>
        string.IsNullOrEmpty(rider) ? carrier
        : string.IsNullOrEmpty(carrier) ? rider
        : $"{carrier} {word} {rider}";
}

public class AuditEntryDto
{
    public int Id { get; set; }
    public string EntityName { get; set; } = "";
    public int EntityId { get; set; }
    /// <summary>The property as the model names it — what was actually recorded.</summary>
    public string FieldName { get; set; } = "";
    /// <summary>How that field is named on screen today.</summary>
    public string FieldLabel { get; set; } = "";
    /// <summary>Which line of the record it was about (an order item's product), or null.</summary>
    public string? FieldScope { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Action { get; set; } = "";
    public DateTime DateChanged { get; set; }
    /// <summary>Null when the change came from a screen with no login; Source says where from.</summary>
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Source { get; set; }
    /// <summary>Who the RECORD names as having done it — picking completed BY Aidan McCann — from
    /// a rider folded into this row (AuditedEntity.Riders). Null when the record names nobody.
    /// Shown as the row's who where the change itself was posted with no login.</summary>
    public string? Actor { get; set; }
    /// <summary>The change set a date-time to the moment of the change, with nothing there before:
    /// an action recorded (picking complete, dispatched), not a value edited. Shown without the
    /// from → to line, since the time on the row already says it.</summary>
    public bool Stamp { get; set; }
    /// <summary>A stamp taken back: the date-time cleared with nothing in its place (picking
    /// reopened). Shown as the action and "cleared", with who and when.</summary>
    public bool StampCleared { get; set; }
}
