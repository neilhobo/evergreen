using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One email sent for an order (not a load) from the Dispatch page: to whom, what, when and by
// whom, so the Open Orders grid can show that an order has been emailed. Kind says which
// message; today there is one, the order confirmation sent to the customer from Open Orders.
// The load's emails are WeighbridgeRecordEmail. See Data/Migrations/AddDispatchOrderEmailTable.sql.
public class DispatchOrderEmail
{
    // The order confirmation, to the customer, with the order confirmation PDF attached.
    public const string KindConfirmation = "Confirmation";

    public int Id { get; set; }
    public int DispatchOrderId { get; set; }
    public string Kind { get; set; } = KindConfirmation;
    // The addresses that were in To, comma-separated.
    public string SentTo { get; set; } = "";
    public string Subject { get; set; } = "";
    public int? SentById { get; set; }
    public DateTime DateSent { get; set; }

    public class Config : IEntityTypeConfiguration<DispatchOrderEmail>
    {
        public void Configure(EntityTypeBuilder<DispatchOrderEmail> b)
        {
            b.ToTable("DispatchOrderEmail");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Kind).HasMaxLength(20);
            b.Property(e => e.SentTo).HasMaxLength(1000);
            b.Property(e => e.Subject).HasMaxLength(300);
            b.Property(e => e.DateSent).HasColumnType("datetime");
        }
    }
}
