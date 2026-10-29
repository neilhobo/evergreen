using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for pending weighbridge records (vwWeighbridgePending view, read-only).
public class WeighbridgePendingRow
{
    public int Id { get; set; }
    public int HaulierId { get; set; }
    public string? HaulierName { get; set; }
    public string? VehicleReg { get; set; }
    public string? TrailerNo { get; set; }
    public int PendingWeight { get; set; }
    public DateTime? PendingWeighDate { get; set; }
    public int? OrderCount { get; set; }
    public bool IsOutgoing { get; set; }
    public string? Product { get; set; }
    public string? Supplier { get; set; }
    public string? BogName { get; set; }
    public int WeightIn { get; set; }
    public DateTime? DateWeightIn { get; set; }
    public string? DocketNumber { get; set; }

    public class Config : IEntityTypeConfiguration<WeighbridgePendingRow>
    {
        public void Configure(EntityTypeBuilder<WeighbridgePendingRow> b)
        {
            b.HasNoKey();
            b.ToView("vwWeighbridgePending");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
