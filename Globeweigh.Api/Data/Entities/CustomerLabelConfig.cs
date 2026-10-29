using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Per-customer pallet-label configuration — currently just an optional logo that
// overrides the default Evergreen logo on the master label template. One row per
// customer, keyed on CustomerSupplierId; absent row = all defaults.
// Table created by Data/Migrations/AddCustomerLabelConfig.sql.
public class CustomerLabelConfig
{
    public int CustomerSupplierId { get; set; }
    public byte[]? LogoImage { get; set; }
    public string? LogoContentType { get; set; }
    public string? LogoFileName { get; set; }
    public DateTime DateLastModified { get; set; }

    public class Config : IEntityTypeConfiguration<CustomerLabelConfig>
    {
        public void Configure(EntityTypeBuilder<CustomerLabelConfig> b)
        {
            b.ToTable("CustomerLabelConfig");
            b.HasKey(e => e.CustomerSupplierId);
        }
    }
}
