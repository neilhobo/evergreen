using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the CostingZone table — backs both the costing-zone dropdown
// (customer/supplier editor) and the Admin "Costing Zones" CRUD. Replaces the
// former keyless CostingZoneRef read-model (a keyed and a keyless entity cannot
// map the same table).
public class CostingZone
{
    public int Id { get; set; }
    public string? Description { get; set; }
    public string? SecondaryDescription { get; set; }
    // null = Both, true = Euro, false = Sterling (matches the WPF currency radio).
    public bool? IsEuro { get; set; }
    public int GroupId { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<CostingZone>
    {
        public void Configure(EntityTypeBuilder<CostingZone> b)
        {
            b.ToTable("CostingZone");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
