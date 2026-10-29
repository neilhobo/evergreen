using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the ReferenceData table — used for both the grid list and CRUD
// (a keyed entity and a keyless read-model cannot map the same table, so the
// Reference Data module uses this single keyed type).
public class ReferenceDataItem
{
    public int Id { get; set; }
    public string? RefType { get; set; }
    public int RefTypeId { get; set; }
    public string? Code { get; set; }
    public string? DisplayName { get; set; }
    public string? AlternativeDisplayName { get; set; }
    public decimal DecimalData { get; set; }
    public bool BoolData { get; set; }
    public int IntData { get; set; }
    public int ForeignKey { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<ReferenceDataItem>
    {
        public void Configure(EntityTypeBuilder<ReferenceDataItem> b)
        {
            b.ToTable("ReferenceData");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
