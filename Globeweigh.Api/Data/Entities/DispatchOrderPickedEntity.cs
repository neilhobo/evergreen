using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the DispatchOrder_Picked TABLE — one row per pallet scanned/picked
// onto an order. This is the legacy WPF picking record the warehouse picks write to;
// the Picking app (Zebra ET40) now writes the same shape. PalletId holds the pallet's
// UniqueCode (from CodicoPallet). Soft-deleted via DateDeleted.
public class DispatchOrderPickedEntity
{
    public int Id { get; set; }
    public int DispatchOrderId { get; set; }
    public int DispatchOrderItemId { get; set; }
    public string PalletId { get; set; } = "";
    public int ProductId { get; set; }
    public int? Quantity { get; set; }
    public decimal Weight { get; set; }
    public DateTime DateCreated { get; set; }
    public int CreatedById { get; set; }
    public DateTime? DateDeleted { get; set; }
    public int DeletedById { get; set; }
    // True when added via "Add no-scan label" — a pallet whose label wouldn't scan, counted
    // manually so the order can complete. Flagged for reporting. PalletId is empty for these.
    public bool IsNoScan { get; set; }
    // True when Weight is an estimate rather than the pallet's own measured figure: every
    // no-scan label, plus any scanned code with no (or a zero) CodicoPallet row. The API
    // fills it from the product's other pallets (see PickingController.EstimatePalletWeight)
    // so the order's totals stay close to true instead of reading 0 for that pallet. See
    // Migrations/AddDispatchOrderPickedWeightEstimated.sql.
    public bool IsWeightEstimated { get; set; }

    public class Config : IEntityTypeConfiguration<DispatchOrderPickedEntity>
    {
        public void Configure(EntityTypeBuilder<DispatchOrderPickedEntity> b)
        {
            b.ToTable("DispatchOrder_Picked");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.PalletId).HasMaxLength(50);
        }
    }
}
