using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One row per label reprinted from the TauriTouch app (the Production screen's "Reprint
// Label" for a missed scan / damaged label, or Manual Stock's). Printing itself never comes
// near the API — the label is rendered to a canvas in the app and spooled to the printer
// attached to that PC — so this is the only record that a pallet's label went out twice.
//
// NOT to be confused with PrintJob, which is a QUEUE of documents for the server to print.
// This is a log of prints that have already happened, written after the fact.
//
// Table created by Data/Migrations/AddLabelReprintTable.sql.
public class LabelReprint
{
    public int Id { get; set; }

    public int PalletId { get; set; }
    // Snapshotted rather than joined, so the log still identifies the pallet if the
    // CodicoPallet row is later deleted (there is deliberately no FK).
    public string? UniqueCode { get; set; }

    public string Source { get; set; } = Production;
    public string? DeviceName { get; set; }
    public string? PrinterName { get; set; }
    public string? UserName { get; set; }   // null on a Factory device with Login disabled

    public DateTime PrintedAtUtc { get; set; }

    // Which screen the reprint came from.
    public const string Production = "Production";
    public const string ManualStock = "ManualStock";

    public class Config : IEntityTypeConfiguration<LabelReprint>
    {
        public void Configure(EntityTypeBuilder<LabelReprint> b)
        {
            b.ToTable("LabelReprint");
            b.HasKey(e => e.Id);
            b.Property(e => e.UniqueCode).HasMaxLength(50);
            b.Property(e => e.Source).HasMaxLength(20);
            b.Property(e => e.DeviceName).HasMaxLength(100);
            b.Property(e => e.PrinterName).HasMaxLength(200);
            b.Property(e => e.UserName).HasMaxLength(100);
        }
    }
}
