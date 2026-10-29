using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for a supplier's bogs/fields (Supplier_Bog table). Read by the Touch
// Delivery cascade and read/written by the admin Raw Materials screen, mirroring the
// WPF AddUpdateSupplier_Bog / RemoveSupplier_Bog repository methods (hard delete).
public class SupplierBog
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string? Name { get; set; }

    public class Config : IEntityTypeConfiguration<SupplierBog>
    {
        public void Configure(EntityTypeBuilder<SupplierBog> b)
        {
            b.ToTable("Supplier_Bog");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
