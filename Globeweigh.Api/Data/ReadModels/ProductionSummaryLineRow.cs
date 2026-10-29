using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Result of the GetProductionSummaryByLine stored proc — same as ProductionSummaryRow
// but with a LineNumber, one row per product per production line. Keyless, FromSql only.
public class ProductionSummaryLineRow
{
    public int? LineNumber { get; set; }
    public int? Id { get; set; }
    public string? Gtin { get; set; }
    public string? Description { get; set; }
    public int? Size { get; set; }
    public int? Upp { get; set; }
    // Nullable: the GetProductionSummaryByLine proc can return NULL aggregates for some
    // rows (matching the original EDMX model). Non-nullable here would make EF throw on NULL.
    public int? RecordCount { get; set; }
    public int? BagCount { get; set; }

    public class Config : IEntityTypeConfiguration<ProductionSummaryLineRow>
    {
        public void Configure(EntityTypeBuilder<ProductionSummaryLineRow> b)
        {
            b.HasNoKey();
            b.ToView(null);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
