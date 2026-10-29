using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for the Customers/Suppliers admin grid (vwCustomerSupplier view, read-only).
public class CustomerSupplierRow
{
    public int Id { get; set; }
    public int TypeId { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public int CostingZoneId { get; set; }
    public string? CostingZone { get; set; }

    public class Config : IEntityTypeConfiguration<CustomerSupplierRow>
    {
        public void Configure(EntityTypeBuilder<CustomerSupplierRow> b)
        {
            b.HasNoKey();
            b.ToView("vwCustomerSupplier");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
