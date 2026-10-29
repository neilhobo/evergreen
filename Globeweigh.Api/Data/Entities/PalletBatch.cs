using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for dbo.PalletBatch — a Manual Stock batch: N pallets of one finished
// product created by hand on a Touch screen (see Migrations/AddManualStockTables.sql).
// The batch's pallets land in CodicoPallet with IsManual=1 + PalletBatchId (later phase).
public class PalletBatch
{
    public int Id { get; set; }
    public string? OrderNumber { get; set; }
    public int ProductId { get; set; }
    public int PalletCount { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime DateCreated { get; set; }
    // false = every pallet is created up front at PalletWeight (the product's Average Weight,
    // editable per batch) instead of weighed one by one (Migrations/AddManualStockAverageWeight.sql).
    public bool RequireIndividualWeight { get; set; } = true;
    public decimal? PalletWeight { get; set; }

    public class Config : IEntityTypeConfiguration<PalletBatch>
    {
        public void Configure(EntityTypeBuilder<PalletBatch> b)
        {
            b.ToTable("PalletBatch");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.OrderNumber).HasMaxLength(50);
            b.Property(e => e.CreatedBy).HasMaxLength(100);
            b.Property(e => e.PalletWeight).HasPrecision(10, 2);
        }
    }
}
