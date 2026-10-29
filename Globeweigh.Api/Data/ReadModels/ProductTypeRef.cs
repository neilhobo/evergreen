using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Lookup read-model for the Product Type dropdown (reads the ProductType table,
// keyless — mapped as a table, not a view).
public class ProductTypeRef
{
    public int Id { get; set; }
    public string? TypeDescription { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<ProductTypeRef>
    {
        public void Configure(EntityTypeBuilder<ProductTypeRef> b)
        {
            b.HasNoKey();
            b.ToTable("ProductType");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
