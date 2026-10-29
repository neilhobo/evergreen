using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// dbo.AuditTrail (Migrations/AddAuditTrail.sql) — one row per field that changed on a record
// after it was created. Written by GbEvergreenContext.SaveChanges for any entity listed in
// Data/Auditing/AuditedEntities.cs; nothing writes one by hand.
public class AuditEntry
{
    public int Id { get; set; }
    public string EntityName { get; set; } = "";
    public int EntityId { get; set; }
    public string FieldName { get; set; } = "";
    /// <summary>Which line of the record this is about (an order item's product), for a change to
    /// a child row filed under its parent. Null for a change to the record itself.</summary>
    public string? FieldScope { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Action { get; set; } = ActionUpdated;
    public DateTime DateChanged { get; set; }
    // Null when the change came from a screen with no login — the touch and picking apps post
    // anonymously. Source then says where it came from.
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Source { get; set; }

    public const string ActionUpdated = "Updated";
    public const string ActionAdded = "Added";
    public const string ActionRemoved = "Removed";
    /// <summary>The record itself coming into being. Never stored: AuditController reads it off
    /// the record (AuditedEntity.Created) and reports it with Id 0.</summary>
    public const string ActionCreated = "Created";

    public class Config : IEntityTypeConfiguration<AuditEntry>
    {
        public void Configure(EntityTypeBuilder<AuditEntry> b)
        {
            b.ToTable("AuditTrail");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.EntityName).HasMaxLength(100);
            b.Property(e => e.FieldName).HasMaxLength(200);
            b.Property(e => e.FieldScope).HasMaxLength(200);
            b.Property(e => e.Action).HasMaxLength(20);
            b.Property(e => e.UserName).HasMaxLength(200);
            b.Property(e => e.Source).HasMaxLength(100);
            b.Property(e => e.DateChanged).HasColumnType("datetime");
            b.HasIndex(e => new { e.EntityName, e.EntityId, e.DateChanged });
        }
    }
}
