using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// A price for one product agreed with one customer (customer-specific pricing), set from the
// Product detail panel. One row per customer per product (unique index); removing a price
// deletes the row. See Data/Migrations/AddCustomerProductPrice.sql.
public class CustomerProductPrice
{
    public int Id { get; set; }
    public int CustomerSupplierId { get; set; }
    public int ProductId { get; set; }
    public decimal Price { get; set; }
    public DateTime DateCreated { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? DateModified { get; set; }
    public string? ModifiedBy { get; set; }

    public class Config : IEntityTypeConfiguration<CustomerProductPrice>
    {
        public void Configure(EntityTypeBuilder<CustomerProductPrice> b)
        {
            b.ToTable("CustomerProductPrice");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Price).HasColumnType("decimal(18,2)");
            b.Property(e => e.DateCreated).HasColumnType("datetime");
            b.Property(e => e.DateModified).HasColumnType("datetime");
            b.Property(e => e.CreatedBy).HasMaxLength(100);
            b.Property(e => e.ModifiedBy).HasMaxLength(100);
        }
    }
}

// Every add / change / removal of a customer price — the CostingZonePriceHistory twin.
// OldPrice null = added, NewPrice null = removed.
public class CustomerProductPriceHistory
{
    public int Id { get; set; }
    public int CustomerSupplierId { get; set; }
    public int ProductId { get; set; }
    public decimal? OldPrice { get; set; }
    public decimal? NewPrice { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<CustomerProductPriceHistory>
    {
        public void Configure(EntityTypeBuilder<CustomerProductPriceHistory> b)
        {
            b.ToTable("CustomerProductPriceHistory");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.OldPrice).HasColumnType("decimal(18,2)");
            b.Property(e => e.NewPrice).HasColumnType("decimal(18,2)");
            b.Property(e => e.DateCreated).HasColumnType("datetime");
            b.Property(e => e.ChangedBy).HasMaxLength(100);
        }
    }
}
