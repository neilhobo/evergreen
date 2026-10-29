using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// dbo.SalesOrder (Migrations/AddSalesOrderTables.sql) — an order a salesperson builds under
// their own login (My Orders) and sends to the office. Not a DispatchOrder until the office
// approves it, so no office screen sees a draft.
public class SalesOrderEntity
{
    public const int StatusDraft = 0;
    public const int StatusPending = 1; // sent to the office, awaiting approval
    public const int StatusReturned = 2; // sent back by the office with a note
    public const int StatusApproved = 3; // DispatchOrderId is the order it became

    public int Id { get; set; }
    public int SalesUserId { get; set; }
    public int Status { get; set; }
    public int CustomerId { get; set; }
    public int AddressId { get; set; }
    public DateTime? DeliveryDate { get; set; } // null = ASAP
    public string? PoNumber { get; set; }
    public string? Comments { get; set; } // prints on the delivery note
    public string? PickingComments { get; set; }
    public bool HasPos { get; set; }
    public string? OfficeNote { get; set; } // why the office returned it
    public int? DispatchOrderId { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateSent { get; set; }
    public DateTime? DateApproved { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<SalesOrderEntity>
    {
        public void Configure(EntityTypeBuilder<SalesOrderEntity> b)
        {
            b.ToTable("SalesOrder");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.DeliveryDate).HasColumnType("datetime");
            b.Property(e => e.DateCreated).HasColumnType("datetime");
            b.Property(e => e.DateSent).HasColumnType("datetime");
            b.Property(e => e.DateApproved).HasColumnType("datetime");
            b.Property(e => e.DateDeleted).HasColumnType("datetime");
        }
    }
}

public class SalesOrderItemEntity
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public int ProductId { get; set; }
    public int? Quantity { get; set; }
    public decimal? Price { get; set; }
    public int SortOrder { get; set; }

    public class Config : IEntityTypeConfiguration<SalesOrderItemEntity>
    {
        public void Configure(EntityTypeBuilder<SalesOrderItemEntity> b)
        {
            b.ToTable("SalesOrderItem");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Price).HasColumnType("decimal(18,4)");
        }
    }
}

public class SalesOrderEventEntity
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public int UserId { get; set; }
    public string Text { get; set; } = "";
    // An edit's changes, a JSON array of sentences (Migrations/AddSalesOrderEventChanges.sql).
    public string? Changes { get; set; }
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<SalesOrderEventEntity>
    {
        public void Configure(EntityTypeBuilder<SalesOrderEventEntity> b)
        {
            b.ToTable("SalesOrderEvent");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.DateCreated).HasColumnType("datetime");
        }
    }
}
