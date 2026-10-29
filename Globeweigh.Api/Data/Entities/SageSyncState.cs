using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One row per Sage entity the sidecar mirrors ("Customers", "Sales"): when it last ran, whether
// that worked, how much it brought back, and whether someone in Admin has asked for a fresh
// pull (RequestedUtc — the sidecar sees it on its next poll and clears it when it delivers
// or fails). The page's "synced 12 min ago" and its Refresh button both live on this row.
// Table created by Data/Migrations/AddSageCustomerTables.sql.
public class SageSyncState
{
    public string Entity { get; set; } = "";
    public DateTime? LastRunUtc { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public string? LastError { get; set; }
    public int RowCount { get; set; }                      // customers
    public int DetailCount { get; set; }                   // addresses
    public int DurationMs { get; set; }                    // the sidecar's Sage read time
    public string? Machine { get; set; }
    public DateTime? RequestedUtc { get; set; }
    public string? RequestedBy { get; set; }
    // Admin pressed Cancel on a refresh. The sidecar compares it with the value it saw when its
    // pull started (not with the clock) and stops if it has changed. Added by AddSageSyncCancel.sql.
    public DateTime? CancelRequestedUtc { get; set; }
    // Sales only: the first date the mirror covers (the sidecar's SalesHistoryYears window).
    public DateTime? WindowFrom { get; set; }

    public const string Customers = "Customers";
    // The audit trail + invoice lines behind Reports (SageSalesTransaction / SageInvoice).
    public const string Sales = "Sales";

    public class Config : IEntityTypeConfiguration<SageSyncState>
    {
        public void Configure(EntityTypeBuilder<SageSyncState> b)
        {
            b.ToTable("SageSyncState");
            b.HasKey(e => e.Entity);
            b.Property(e => e.Entity).HasMaxLength(40);
        }
    }
}
