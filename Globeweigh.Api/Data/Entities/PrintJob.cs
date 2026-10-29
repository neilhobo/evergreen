using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One document to print on the server's own printer — today only the delivery notes that
// go out automatically when a picker completes a load (PickingController.CompleteLoad).
//
// The API only ever ENQUEUES. Rendering and printing happen out of process, in
// GlobeweighService's PrintWorker, for two reasons: the API runs under IIS as an app-pool
// identity, which is a poor place to drive a print driver; and printing must never be able
// to fail a load completion, which is what dispatches the orders and writes stock history.
// A dead printer leaves rows in this table, not a load stuck half-completed.
//
// Table created by Data/Migrations/AddPrintJobTable.sql.
public class PrintJob
{
    public int Id { get; set; }

    public string DocType { get; set; } = DeliveryNote;   // room for future documents
    public int DispatchOrderId { get; set; }
    public int WeighbridgeRecordId { get; set; }          // the load it went out on

    public int Copies { get; set; } = 2;
    public string? PrinterName { get; set; }              // null = whatever Setting says

    public string Status { get; set; } = Queued;
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    // Why this job exists. Also the idempotency key: one LoadComplete job per order, so
    // re-completing a load (or a retried request) cannot print the docket twice.
    public string Reason { get; set; } = LoadComplete;

    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public const string DeliveryNote = "DeliveryNote";

    public const string Queued = "Queued";
    public const string Printing = "Printing";
    public const string Done = "Done";
    public const string Failed = "Failed";

    public const string LoadComplete = "LoadComplete";
    public const string Reprint = "Reprint";

    public class Config : IEntityTypeConfiguration<PrintJob>
    {
        public void Configure(EntityTypeBuilder<PrintJob> b)
        {
            b.ToTable("PrintJob");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
