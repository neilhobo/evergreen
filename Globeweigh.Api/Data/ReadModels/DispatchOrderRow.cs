using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for the Dispatch open-orders grid (vwDispatchOrder view, read-only).
public class DispatchOrderRow
{
    public int Id { get; set; }
    public int OrderNo { get; set; }
    public int CustomerId { get; set; }
    public int HaulierId { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateDispatched { get; set; }
    public DateTime? DateDeleted { get; set; }
    public string? CustomerRef { get; set; }
    public string? CustomerName { get; set; }
    public string? AddressFull { get; set; }
    public string? OrderStatus { get; set; }
    public string? VehicleReg { get; set; }
    public string? CreatedBy { get; set; }
    public string? HaulierName { get; set; }
    public string? PoNumber { get; set; }
    public string? TrailerNo { get; set; }
    public int PickedQuantity { get; set; }
    public decimal PickedWeight { get; set; }
    public int TotalQuantityOrdered { get; set; }
    public int WeighbridgeRecordId { get; set; }
    public int? SortOrder { get; set; }
    public bool? Assigned { get; set; }
    // The latest order confirmation emailed to the customer (DispatchOrderEmail), for the Open
    // Orders grid's Emailed column. Filled by DispatchController.Orders; not in the view.
    [NotMapped] public DateTime? ConfirmationSentAt { get; set; }
    [NotMapped] public string? ConfirmationSentTo { get; set; }

    public class Config : IEntityTypeConfiguration<DispatchOrderRow>
    {
        public void Configure(EntityTypeBuilder<DispatchOrderRow> b)
        {
            b.HasNoKey();
            b.ToView("vwDispatchOrder");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
