using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Result of the GetProductionSummary stored proc (production totals per product over a
// date range). Keyless, not mapped to any table/view — queried only via FromSql.
public class ProductionSummaryRow
{
    public int? Id { get; set; }
    public string? Gtin { get; set; }
    public string? Description { get; set; }
    public int? Size { get; set; }
    public int? Upp { get; set; }
    // Nullable: the GetProductionSummary proc can return NULL aggregates for some rows
    // (matching the original EDMX model). Non-nullable here would make EF throw on NULL.
    public int? RecordCount { get; set; }
    public int? BagCount { get; set; }

    public class Config : IEntityTypeConfiguration<ProductionSummaryRow>
    {
        public void Configure(EntityTypeBuilder<ProductionSummaryRow> b)
        {
            b.HasNoKey();
            b.ToView(null);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
