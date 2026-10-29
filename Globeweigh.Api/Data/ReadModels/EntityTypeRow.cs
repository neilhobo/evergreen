using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.ReadModels;

// Read model for the EntityType table (read-only).
public class EntityTypeRow
{
    public int Id { get; set; }
    public string? EntityName { get; set; }
    public string? EntityTypeName { get; set; }
    public int EntityTypeId { get; set; }
    public bool IsReferenceDataType { get; set; }
    public string? DisplayName { get; set; }
    public int SortOrder { get; set; }
    public bool AdminAccessOnly { get; set; }
    public bool SuperAdminAccessOnly { get; set; }

    public class Config : IEntityTypeConfiguration<EntityTypeRow>
    {
        public void Configure(EntityTypeBuilder<EntityTypeRow> b)
        {
            b.HasNoKey();
            b.ToTable("EntityType");
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
