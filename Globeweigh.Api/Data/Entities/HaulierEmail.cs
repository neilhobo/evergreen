using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One email address of a haulier (HaulierEmail table). A haulier can have any number, so
// the Hauliers page and Edit Haulier > Emails tab work off these rows rather than the
// retired single Haulier.Email column (see Data/Migrations/AddHaulierEmailTable.sql).
public class HaulierEmail
{
    public int Id { get; set; }
    public int HaulierId { get; set; }
    public string Email { get; set; } = "";
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<HaulierEmail>
    {
        public void Configure(EntityTypeBuilder<HaulierEmail> b)
        {
            b.ToTable("HaulierEmail");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Email).HasMaxLength(200);
        }
    }
}
