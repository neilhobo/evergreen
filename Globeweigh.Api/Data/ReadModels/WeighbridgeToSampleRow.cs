using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for the Touch Sample Weight list (vwWeighbridgeRecordsToBeSampled view, read-only).
// Records weighed in on the filter day; the operator records a sample box weight against each.
public class WeighbridgeToSampleRow
{
    public int Id { get; set; }
    public string? HaulierName { get; set; }
    public string? VehicleReg { get; set; }
    public string? Product { get; set; }
    public string? Supplier { get; set; }
    public string? BogName { get; set; }
    public int WeightIn { get; set; }
    public int? NetWeight { get; set; }
    public decimal? SampleBoxWeight { get; set; }
    public bool? SampleCollected { get; set; }
    public DateTime? DateWeightIn { get; set; }
    public bool IsOutgoing { get; set; }

    public class Config : IEntityTypeConfiguration<WeighbridgeToSampleRow>
    {
        public void Configure(EntityTypeBuilder<WeighbridgeToSampleRow> b)
        {
            b.HasNoKey();
            b.ToView("vwWeighbridgeRecordsToBeSampled");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
