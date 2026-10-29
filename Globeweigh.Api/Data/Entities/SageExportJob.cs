using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One sales order to write into the client's Sage 50 Accounts — the outbox half of the
// Sage integration. The API only ever ENQUEUES; the write itself happens out of process in
// Globeweigh.SageSidecar, a 32-bit service on the machine that has Sage installed. That
// split is forced, not chosen: Sage 50's only write API (SDO) is a 32-bit in-process COM
// server, which this 64-bit IIS worker cannot load, and Sage's data lives on another box
// anyway. The sidecar claims rows over HTTP (SageController's sidecar endpoints), posts
// the order with SDO and reports the Sage order number back.
//
// Payload is the order as it stood when it was dispatched, captured at enqueue time
// (Services/SageExport.BuildPayloadAsync). What reaches Sage is therefore what went out of
// the gate: a later edit to the order, a product rename or a customer-code change cannot
// alter an export already queued, and the sidecar needs nothing from the database but this
// one row.
//
// Table created by Data/Migrations/AddSageExportJobTable.sql.
public class SageExportJob
{
    public int Id { get; set; }

    public string DocType { get; set; } = SalesOrder;     // room for invoices/credits later
    public int DispatchOrderId { get; set; }
    public int OrderNo { get; set; }                       // denormalised for the ops view

    public string Payload { get; set; } = "";              // JSON: SageExport.SalesOrderPayload

    public string Status { get; set; } = Queued;
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    // Why this job exists: Dispatch = queued automatically when the load completed,
    // Manual = an admin asked for it (SageController.ExportOrder).
    public string Reason { get; set; } = Dispatch;

    public int? SageOrderNo { get; set; }                  // Sage's ORDER_NUMBER once posted
    public string? ClaimedBy { get; set; }                 // sidecar machine name

    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public const string SalesOrder = "SalesOrder";

    public const string Queued = "Queued";
    public const string Exporting = "Exporting";
    public const string Done = "Done";
    public const string Failed = "Failed";

    public const string Dispatch = "Dispatch";
    public const string Manual = "Manual";

    // A job is retried this many times before it is parked as Failed. Attempts is
    // incremented at claim time, so a sidecar that dies mid-post still counts.
    public const int MaxAttempts = 3;

    public class Config : IEntityTypeConfiguration<SageExportJob>
    {
        public void Configure(EntityTypeBuilder<SageExportJob> b)
        {
            b.ToTable("SageExportJob");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
