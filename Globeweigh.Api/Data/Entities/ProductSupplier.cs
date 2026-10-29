using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the Product_Supplier junction table (raw-material product <-> supplier).
// The admin Raw Materials screen reads the vwProduct_Supplier view via ProductSupplierRow;
// writes (link/unlink a supplier) go through this entity, mirroring the WPF
// AddProduct_Supplier / RemoveProduct_Supplier repository methods (hard delete).
public class ProductSupplier
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int SupplierId { get; set; }

    public class Config : IEntityTypeConfiguration<ProductSupplier>
    {
        public void Configure(EntityTypeBuilder<ProductSupplier> b)
        {
            b.ToTable("Product_Supplier");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
