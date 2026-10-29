using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for costing zone prices (vwCostingZonePrice view, read-only).
public class CostingZonePriceRow
{
    public int Id { get; set; }
    public int CostingZoneId { get; set; }
    public int ProductId { get; set; }
    public decimal Price { get; set; }
    public bool NA { get; set; }
    public string? Description { get; set; }
    public string? SecondaryDescription { get; set; }
    public string? DescriptionDisplay { get; set; }
    public bool? IsEuro { get; set; }
    public string? GroupDescription { get; set; }

    public class Config : IEntityTypeConfiguration<CostingZonePriceRow>
    {
        public void Configure(EntityTypeBuilder<CostingZonePriceRow> b)
        {
            b.HasNoKey();
            b.ToView("vwCostingZonePrice");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
