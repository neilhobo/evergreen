using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for dbo.StockTake — one stock take (see Migrations/AddStockTakeTables.sql).
// A legacy table reused by the TauriAdmin Stock module: DateCompleted /
// DateMadeCurrentStock / DateDeleted drive the later "Make Current Stock" flow.
public class StockTake
{
    public int Id { get; set; }
    public int? CreatedById { get; set; }
    // The area this take covers (dbo.StockLocation); null on takes saved before locations
    // existed. For transfers this is the DESTINATION location.
    public int? LocationId { get; set; }
    // Stock transfer (see Migrations/AddStockTransfer.sql): IsTransfer=1 rows move the
    // item counts FromLocationId -> LocationId; they never get DateMadeCurrentStock.
    public bool IsTransfer { get; set; }
    public int? FromLocationId { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateCompleted { get; set; }
    public DateTime? DateMadeCurrentStock { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<StockTake>
    {
        public void Configure(EntityTypeBuilder<StockTake> b)
        {
            b.ToTable("StockTake");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}

// Keyed entity for dbo.StockTakeItem — one counted product within a stock take.
// Counts only, never pallet identity: stock is depleted oldest-first by product.
public class StockTakeItem
{
    public int Id { get; set; }
    public int StockTakeId { get; set; }
    public int ProductId { get; set; }
    public int PalletCount { get; set; }
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<StockTakeItem>
    {
        public void Configure(EntityTypeBuilder<StockTakeItem> b)
        {
            b.ToTable("StockTakeItem");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
