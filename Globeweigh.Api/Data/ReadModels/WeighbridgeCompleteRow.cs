using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for completed weighbridge records (vwWeighbridgeComplete view, read-only).
public class WeighbridgeCompleteRow
{
    public int Id { get; set; }
    public string? HaulierName { get; set; }
    public string? VehicleReg { get; set; }
    public string? TrailerNo { get; set; }
    public DateTime? DateWeightIn { get; set; }
    public DateTime? DateWeightOut { get; set; }
    public int WeightIn { get; set; }
    public int WeightOut { get; set; }
    public int? NetWeight { get; set; }
    public int? OrderCount { get; set; }
    public decimal TotalWeightPicked { get; set; }
    public string? Product { get; set; }
    public string? Supplier { get; set; }
    public string? BogName { get; set; }
    public bool IsOutgoing { get; set; }
    public string? DocketNumber { get; set; }
    public decimal? SampleBoxWeight { get; set; }
    public decimal? CubicM3 { get; set; }

    public class Config : IEntityTypeConfiguration<WeighbridgeCompleteRow>
    {
        public void Configure(EntityTypeBuilder<WeighbridgeCompleteRow> b)
        {
            b.HasNoKey();
            b.ToView("vwWeighbridgeComplete");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
