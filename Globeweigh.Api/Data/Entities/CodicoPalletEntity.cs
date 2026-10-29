using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the CodicoPallet TABLE — the master list of produced pallets pushed
// in by a 3rd party. Picking looks a pallet up by UniqueCode (the barcode's UniqueCode
// field) to get its real Weight. The Production grids read this table via ProductionController.
public class CodicoPalletEntity
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public decimal Weight { get; set; }
    public string UniqueCode { get; set; } = "";
    public bool IsScanned { get; set; }
    public DateTime? DateScanned { get; set; }
    // How this pallet came to be marked scanned (see Migrations/AddCodicoPalletScanSource.sql).
    // NULL means the conveyor scanner as well: GlobeweighService's BarcodeWorker never writes
    // the column, and every row predating it is NULL. Use IsScannerRead/ScannerSources when
    // asking "did the SCANNER read this", never IsScanned on its own.
    public string? ScanSource { get; set; }
    public DateTime? DateCreated { get; set; }
    public string? Code13 { get; set; }
    // The dispatch order this pallet was picked onto (set by the Picking app). Null until
    // picked. Lets us report exactly which pallets were dispatched even when a UniqueCode
    // is duplicated by the 3rd party (every matching master row gets linked).
    public int? PickedOrderId { get; set; }
    // Manual Stock (see Migrations/AddManualStockTables.sql): pallets weighed by hand on a
    // Touch screen rather than pushed by the 3rd party, linked to their dbo.PalletBatch.
    public bool IsManual { get; set; }
    public int? PalletBatchId { get; set; }
    // When the pallet's label was last printed from Manual Stock; NULL = never printed
    // (see Migrations/AddManualStockAverageWeight.sql).
    public DateTime? DateLabelPrinted { get; set; }
    // Out-of-stock state (see Migrations/AddCodicoPalletOutOfStock.sql): NULL = in stock.
    // Set by "Make Current Stock" on a stock take (method "StockTake"); cleared again if a
    // later stock take's counts cover the pallet — a state, not a log.
    public DateTime? DateOutOfStock { get; set; }
    public string? OutOfStockMethod { get; set; }
    public int? OutOfStockStockTakeId { get; set; }
    // Stock location (dbo.StockLocation). New pallets default to Main Yard via a DB
    // DEFAULT constraint (see Migrations/AddCodicoPalletLocation.sql) so the 3rd party's
    // direct SQL INSERT and Manual Stock's EF insert share one source of truth.
    public int? LocationId { get; set; }

    // ScanSource values. Only the scanner ones count as "the conveyor scanner read this
    // pallet"; the operator ones record that a human cleared the row, which is never evidence
    // about the scanner. NULL is a scanner read too — see the property above.
    public const string ScannedByScanner = "Scanner";
    public const string ScannedByReprint = "Reprint";
    public const string ScannedByOperator = "Manual";

    public class Config : IEntityTypeConfiguration<CodicoPalletEntity>
    {
        public void Configure(EntityTypeBuilder<CodicoPalletEntity> b)
        {
            b.ToTable("CodicoPallet");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.UniqueCode).HasMaxLength(50);
            b.Property(e => e.Code).HasMaxLength(50);
            b.Property(e => e.OutOfStockMethod).HasMaxLength(50);
            b.Property(e => e.ScanSource).HasMaxLength(20);
            // The DB supplies the Main Yard default — EF must omit the column when the
            // property is left null so the DEFAULT constraint applies.
            b.Property(e => e.LocationId).HasDefaultValueSql();
            // Code13 is a PERSISTED computed column (right('0000000000000'+[Code],13)) — the
            // DB derives the 13-digit code from Code. Mark it store-computed so EF reads it
            // back but never tries to INSERT/UPDATE it (SqlException 271 otherwise).
            b.Property(e => e.Code13)
                .HasMaxLength(13)
                .HasComputedColumnSql("right('0000000000000'+[Code],(13))", stored: true);
        }
    }
}
