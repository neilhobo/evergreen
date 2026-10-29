using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Stock movement audit row. Weigh-out writes one per dispatched order item
// (TransactionType "Dispatch"), mirroring CompleteWeighBridgeRecord.
public class StockHistory
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public int EntityId { get; set; }
    public string? TransactionType { get; set; }
    public DateTime? DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<StockHistory>
    {
        public void Configure(EntityTypeBuilder<StockHistory> b)
        {
            b.ToTable("StockHistory");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
