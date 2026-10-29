using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the WeighbridgeRecord table — the single record behind the
// Loads (pre-load), Pending and Completed grids. State is defined by dates:
//   PreLoad   = DateWeightIn null
//   Pending   = DateWeightIn set, DateWeightOut null
//   Completed = DateWeightOut set
// (The grids themselves read the vwWeighbridge* views; this entity is for writes.)
public class WeighbridgeRecord
{
    public int Id { get; set; }
    public DateTime? CollectionDate { get; set; }
    public bool IsOutgoing { get; set; }
    public int HaulierId { get; set; }
    public string? HaulierName { get; set; }
    public string? Product { get; set; }
    public string? Supplier { get; set; }
    public string? BogName { get; set; }
    public string? VehicleReg { get; set; }
    public string? TrailerNo { get; set; }
    public string? DocketNumber { get; set; }
    public int WeightIn { get; set; }
    public DateTime? DateWeightIn { get; set; }
    public int WeightOut { get; set; }
    public DateTime? DateWeightOut { get; set; }
    public decimal? SampleBoxWeight { get; set; }
    public string? Comments { get; set; }
    public DateTime? DatePickingComplete { get; set; }
    public DateTime? DateDeleted { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateSampleWeightAdded { get; set; }
    // Picker confirmed the trailer number before working the load (Picking app).
    // ById is null until the app gets a login; populated later.
    public DateTime? DateTrailerConfirmed { get; set; }
    public int? TrailerConfirmedById { get; set; }

    public class Config : IEntityTypeConfiguration<WeighbridgeRecord>
    {
        public void Configure(EntityTypeBuilder<WeighbridgeRecord> b)
        {
            b.ToTable("WeighbridgeRecord");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
