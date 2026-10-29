using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for the Dispatch "Loads" grid (vwWeighbridgePreLoad view, read-only).
public class WeighbridgePreLoadRow
{
    public int Id { get; set; }
    public string? IdRef { get; set; }
    public DateTime? CollectionDate { get; set; }
    public int HaulierId { get; set; }
    public string? HaulierName { get; set; }
    public string? VehicleReg { get; set; }
    public string? TrailerNo { get; set; }
    public int? OrderCount { get; set; }

    public class Config : IEntityTypeConfiguration<WeighbridgePreLoadRow>
    {
        public void Configure(EntityTypeBuilder<WeighbridgePreLoadRow> b)
        {
            b.HasNoKey();
            b.ToView("vwWeighbridgePreLoad");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
