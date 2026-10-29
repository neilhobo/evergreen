using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the CostingZonePrice table — used for writes (the zone-price
// grid reads the vwCostingZonePrice view via CostingZonePriceRow read-model).
public class CostingZonePriceEntity
{
    public int Id { get; set; }
    public int CostingZoneId { get; set; }
    public int ProductId { get; set; }
    public string? Gtin { get; set; }
    public decimal Price { get; set; }
    public bool NA { get; set; }

    public class Config : IEntityTypeConfiguration<CostingZonePriceEntity>
    {
        public void Configure(EntityTypeBuilder<CostingZonePriceEntity> b)
        {
            b.ToTable("CostingZonePrice");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
