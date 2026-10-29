using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the LoadPhoto table — one row per photo taken against a weighbridge
// load by the Picking app's "Complete Load" step (Trailer Number / Side of Trailer /
// Other Side of Trailer). The image bytes live on disk under Picking:PhotosPath; only this
// metadata goes in SQL.
public class LoadPhotoEntity
{
    public int Id { get; set; }
    public int WeighbridgeRecordId { get; set; }
    public string Description { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? ContentType { get; set; }
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<LoadPhotoEntity>
    {
        public void Configure(EntityTypeBuilder<LoadPhotoEntity> b)
        {
            b.ToTable("LoadPhoto");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Description).HasMaxLength(100);
            b.Property(e => e.FileName).HasMaxLength(260);
            b.Property(e => e.ContentType).HasMaxLength(100);
        }
    }
}
