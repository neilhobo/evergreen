using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the Product table — used for CRUD writes. The admin grid
// reads the vwProduct view via the keyless ProductRow read-model (a keyed
// entity and a keyless read-model map different DB objects, so no conflict).
public class Product
{
    public int Id { get; set; }
    public string? Gtin { get; set; }
    public string? Description { get; set; }
    public string? SkuCode { get; set; }
    public int? Size { get; set; }
    public int Upp { get; set; }
    public string? CommodityCode { get; set; }
    public int ProductTypeId { get; set; }
    public int CategoryId { get; set; }
    public int? PosId { get; set; }
    public bool IsBulkBag { get; set; } // see Migrations/AddProductIsBulkBagColumn.sql
    public decimal? AverageWeight { get; set; } // kg per pallet; see Migrations/AddProductAverageWeight.sql
    public decimal PalletWeight { get; set; } = 25; // kg; read-only in the UI for now — see Migrations/AddProductPalletAndBagWeight.sql
    public decimal? BagWeightGrams { get; set; } // g per empty bag; same migration
    public string SoldBy { get; set; } = ProductSoldBy.Pallet; // see Migrations/AddProductSoldBy.sql
    public int CreatedById { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }
    public int DeletedById { get; set; }
    public string? Gtin13 { get; set; }

    public class Config : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> b)
        {
            b.ToTable("Product");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            // Computed in the DB: RIGHT('0000000000000' + Gtin, 13). EF must never
            // write it — including it in an INSERT/UPDATE fails with SQL error 271.
            b.Property(e => e.Gtin13).HasComputedColumnSql();
        }
    }
}

// Product.SoldBy values (CK_Product_SoldBy). Every product was Pallet when the column arrived.
public static class ProductSoldBy
{
    public const string Pallet = "Pallet";
    public const string CubicMeter = "CubicMeter";

    public static bool IsValid(string? v) => v is Pallet or CubicMeter;
}
