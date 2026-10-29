using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for dbo.StockLocation — a stock-take area (Main Yard, McSloys Yard, …).
// A legacy table reused by the TauriAdmin Stock module (see Migrations/AddStockTakeLocation.sql);
// one stock take covers exactly one location.
public class StockLocation
{
    public int Id { get; set; }
    public string? LocationName { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<StockLocation>
    {
        public void Configure(EntityTypeBuilder<StockLocation> b)
        {
            b.ToTable("StockLocation");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.LocationName).HasMaxLength(100);
        }
    }
}
