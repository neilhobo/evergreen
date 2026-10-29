using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// dbo.FeatureFlag (Migrations/AddFeatureFlagTable.sql) — one row per STAGED feature: finished
// and deployed, but not switched on until the client says so. A name with no row is off, so a
// feature can be staged without anything being written here at all.
public class FeatureFlagEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTime? DateChanged { get; set; }
    public int? ChangedById { get; set; }

    public class Config : IEntityTypeConfiguration<FeatureFlagEntity>
    {
        public void Configure(EntityTypeBuilder<FeatureFlagEntity> b)
        {
            b.ToTable("FeatureFlag");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Name).HasMaxLength(100);
            b.Property(e => e.DateChanged).HasColumnType("datetime");
            b.HasIndex(e => e.Name).IsUnique();
        }
    }
}
