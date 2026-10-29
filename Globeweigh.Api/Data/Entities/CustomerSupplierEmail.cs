using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One email address of a customer/supplier (CustomerSupplierEmail table). A record can
// have any number, so the Customers/Suppliers page and Edit Customer > Emails tab work
// off these rows — the same shape as HaulierEmail. Separate from
// CustomerSupplierAddress.Email, which stays what it is: the contact address of one
// delivery address (see Data/Migrations/AddCustomerSupplierEmailTable.sql).
public class CustomerSupplierEmail
{
    public int Id { get; set; }
    public int CustomerSupplierId { get; set; }
    public string Email { get; set; } = "";
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<CustomerSupplierEmail>
    {
        public void Configure(EntityTypeBuilder<CustomerSupplierEmail> b)
        {
            b.ToTable("CustomerSupplierEmail");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Email).HasMaxLength(200);
        }
    }
}
