using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One email sent for a load from the Dispatch page's Send Email dialogs: to whom, what, when
// and by whom, so the grids can show that a load has been emailed. Kind says which of the two
// messages it was — the "ready for collection" one from the Loads grid, or the dispatch
// confirmation from the Completed Loads grid — and each grid's Emailed column reads its own.
// See Data/Migrations/AddWeighbridgeRecordEmailTable.sql and AddWeighbridgeRecordEmailKind.sql.
public class WeighbridgeRecordEmail
{
    // The load is ready for the haulier to collect (sent while it is on the Loads grid).
    public const string KindReady = "Ready";
    // The load has weighed out and left (sent from the Completed Loads grid).
    public const string KindDispatched = "Dispatched";
    // The dispatch confirmation's copy to one customer on the load (same dialog, sent after
    // the haulier's). Not what the Completed Loads grid's Emailed column reads.
    public const string KindDispatchedCustomer = "DispatchedCustomer";
    // The pre-commercial invoices, to the haulier from Load detail before the load is picked.
    // Not what either grid's Emailed column reads.
    public const string KindPreCommercial = "PreCommercial";

    public int Id { get; set; }
    public int WeighbridgeRecordId { get; set; }
    public string Kind { get; set; } = KindReady;
    // The addresses that were in To, comma-separated.
    public string SentTo { get; set; } = "";
    public string Subject { get; set; } = "";
    public int? SentById { get; set; }
    public DateTime DateSent { get; set; }

    public class Config : IEntityTypeConfiguration<WeighbridgeRecordEmail>
    {
        public void Configure(EntityTypeBuilder<WeighbridgeRecordEmail> b)
        {
            b.ToTable("WeighbridgeRecordEmail");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Kind).HasMaxLength(20);
            b.Property(e => e.SentTo).HasMaxLength(1000);
            b.Property(e => e.Subject).HasMaxLength(300);
            b.Property(e => e.DateSent).HasColumnType("datetime");
        }
    }
}
