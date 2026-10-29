using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the DispatchOrder TABLE — used for writes (create/update/delete).
// The grids read the vwDispatchOrder VIEW via DispatchOrderRow (keyless); a keyed
// entity and a keyless read-model cannot map the same object, so writes use this.
public class DispatchOrderEntity
{
    public int Id { get; set; }
    public int OrderNo { get; set; }
    public int TransferOrderNo { get; set; }
    public int WeighbridgeRecordId { get; set; }
    public int OrderTypeId { get; set; }
    public int CustomerId { get; set; }
    public int TransferLocationId { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string? Comments { get; set; } // prints on the delivery note
    public string? PickingComments { get; set; } // instructions for the pickers
    public int AddressId { get; set; }
    public string? AddressFull { get; set; }
    public int HaulierId { get; set; }
    public string? VehicleReg { get; set; }
    public string? TrailerNo { get; set; }
    public string? PoNumber { get; set; }
    // A POS (point-of-sale) board goes with the order. Not a product line - no stock, no
    // picking, no price - and an order has at most one, so it is a flag, not an item.
    public bool HasPos { get; set; }
    // The sales rep the order is credited to (Migrations/AddDispatchOrderSalesRep.sql). Fixed
    // for an order approved from My Orders (the rep who sent it); otherwise the office's choice.
    public int? SalesRepId { get; set; }
    // When the picker tapped the POS board picked, and who — see Migrations/AddDispatchOrderPosPicked.sql.
    public DateTime? DatePosPicked { get; set; }
    public int? PosPickedById { get; set; }
    public int? SortOrder { get; set; }
    public int CreatedById { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DatePickingComplete { get; set; }
    // Who finished the picking (the Picking app's signed-in user), and whether it was ended
    // short via "Complete Early" rather than every ordered pallet being picked.
    public int? PickingCompletedById { get; set; }
    public bool PickingCompletedEarly { get; set; }
    public DateTime? DateDispatched { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<DispatchOrderEntity>
    {
        public void Configure(EntityTypeBuilder<DispatchOrderEntity> b)
        {
            b.ToTable("DispatchOrder");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
