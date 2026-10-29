using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// dbo.AutomaticEmailText (Migrations/AddAutomaticEmailTextTable.sql) — one row per piece of an
// automatic email whose wording has been changed from the default in Services/AutomaticEmails.cs.
// A piece with no row reads as the default; resetting it deletes the row.
public class AutomaticEmailTextEntity
{
    public int Id { get; set; }
    public string Email { get; set; } = "";   // AutomaticEmails.Ready / .Dispatched
    public string Field { get; set; } = "";   // AutomaticEmails.Subject / .Greeting / ...
    public string Text { get; set; } = "";
    public DateTime DateChanged { get; set; }
    public int? ChangedById { get; set; }

    public class Config : IEntityTypeConfiguration<AutomaticEmailTextEntity>
    {
        public void Configure(EntityTypeBuilder<AutomaticEmailTextEntity> b)
        {
            b.ToTable("AutomaticEmailText");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Email).HasMaxLength(40);
            b.Property(e => e.Field).HasMaxLength(40);
            b.Property(e => e.DateChanged).HasColumnType("datetime");
            b.HasIndex(e => new { e.Email, e.Field }).IsUnique();
        }
    }
}
