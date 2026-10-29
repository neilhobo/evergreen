using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for the suppliers of a raw-material product (vwProduct_Supplier view, read-only).
// Used by the Touch Delivery weigh-in cascade (Haulier -> Product -> Supplier -> Bog).
public class ProductSupplierRow
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string? Name { get; set; }
    public int SupplierId { get; set; }

    public class Config : IEntityTypeConfiguration<ProductSupplierRow>
    {
        public void Configure(EntityTypeBuilder<ProductSupplierRow> b)
        {
            b.HasNoKey();
            b.ToView("vwProduct_Supplier");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
