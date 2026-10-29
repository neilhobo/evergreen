using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for the Dispatch order-items grid (vwDispatchOrderItem view, read-only).
public class DispatchOrderItemRow
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string? ProductDescription { get; set; }
    public int? Size { get; set; }
    public int Upp { get; set; }
    public int? Quantity { get; set; }
    public int? TotalUnits { get; set; }
    public int? PickedQuantity { get; set; }
    public decimal PickedWeight { get; set; }
    public string? Gtin { get; set; }
    public string? UnitDisplay { get; set; }
    public int? OrderNo { get; set; }
    public int? WeighbridgeRecordId { get; set; }
    public string SoldBy { get; set; } = "Pallet"; // Product.SoldBy — see Migrations/AddDispatchOrderItemSoldBy.sql
    // kg/m³ the picker entered marking a cubic meter line picked (AddDispatchOrderItemDensityToView.sql).
    public decimal? Density { get; set; }
    // Only on the synthetic POS Board row (SoldBy "Pos", see DispatchController.PosRowsAsync):
    // when the board was picked. Not a view column.
    [NotMapped] public DateTime? PickedAt { get; set; }

    public class Config : IEntityTypeConfiguration<DispatchOrderItemRow>
    {
        public void Configure(EntityTypeBuilder<DispatchOrderItemRow> b)
        {
            b.HasNoKey();
            b.ToView("vwDispatchOrderItem");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
