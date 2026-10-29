using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the DispatchOrderItem TABLE — used for writes. The grid reads the
// vwDispatchOrderItem VIEW via DispatchOrderItemRow (keyless).
public class DispatchOrderItemEntity
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int? Quantity { get; set; }
    public decimal? Price { get; set; }
    public DateTime? DateCreated { get; set; }
    // kg/m³, entered when a cubic meter line is marked picked — see Migrations/AddDispatchOrderItemDensity.sql.
    public decimal? Density { get; set; }

    public class Config : IEntityTypeConfiguration<DispatchOrderItemEntity>
    {
        public void Configure(EntityTypeBuilder<DispatchOrderItemEntity> b)
        {
            b.ToTable("DispatchOrderItem");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Density).HasColumnType("decimal(9,2)");
        }
    }
}
