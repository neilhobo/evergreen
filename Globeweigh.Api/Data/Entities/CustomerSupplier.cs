using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the CustomerSuppliers table — used for CRUD writes.
// The admin grid reads the vwCustomerSupplier VIEW via the keyless
// CustomerSupplierRow read-model (a keyed entity and a keyless read-model
// cannot map the same table), so this keyed type handles writes only.
public class CustomerSupplier
{
    public int Id { get; set; }
    public int TypeId { get; set; }
    public bool IsBoth { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public bool Inactive { get; set; }
    public int CostingZoneId { get; set; }
    // The customer's sales rep: a Sales-role user (Migrations/AddCustomerSalesRep.sql). Set only
    // through PUT {id}/sales-rep; Create/Update never touch it.
    public int? SalesRepId { get; set; }
    public DateTime? DateCreated { get; set; }
    public int CreatedById { get; set; }
    public DateTime? DateDeleted { get; set; }
    public int DeletedById { get; set; }

    public class Config : IEntityTypeConfiguration<CustomerSupplier>
    {
        public void Configure(EntityTypeBuilder<CustomerSupplier> b)
        {
            b.ToTable("CustomerSuppliers");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
