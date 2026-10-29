using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for the Products admin grid (vwProduct view, read-only).
public class ProductRow
{
    public int Id { get; set; }
    public string? Description { get; set; }
    public string? ProductType { get; set; }
    public string? SkuCode { get; set; }
    public int? Size { get; set; }
    public int Upp { get; set; }
    public int ProductTypeId { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? Gtin { get; set; }
    public string? CommodityCode { get; set; }
    public bool? HasPos { get; set; }
    public int? PosId { get; set; }
    public string? PosDescription { get; set; }
    public bool IsBulkBag { get; set; }
    public decimal? AverageWeight { get; set; }
    public decimal PalletWeight { get; set; }
    public decimal? BagWeightGrams { get; set; }
    public string SoldBy { get; set; } = "Pallet";
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<ProductRow>
    {
        public void Configure(EntityTypeBuilder<ProductRow> b)
        {
            b.HasNoKey();
            b.ToView("vwProduct");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
