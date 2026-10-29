using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the CostingZonePriceHistory table — price-change audit trail.
// NOTE: the ConstingZonePriceId property name is intentionally misspelled to
// match the real column name in the database.
public class CostingZonePriceHistoryEntity
{
    public int Id { get; set; }
    public int ConstingZonePriceId { get; set; }
    public int ProductId { get; set; }
    public int ZoneId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<CostingZonePriceHistoryEntity>
    {
        public void Configure(EntityTypeBuilder<CostingZonePriceHistoryEntity> b)
        {
            b.ToTable("CostingZonePriceHistory");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
