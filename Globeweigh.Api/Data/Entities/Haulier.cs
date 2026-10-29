using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the Haulier table — used for both the grid list and CRUD
// (a keyed entity and a keyless read-model cannot map the same table, so the
// Hauliers module uses this single keyed type).
public class Haulier
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? ContactName { get; set; }
    public string? Tel { get; set; }
    // Email addresses live in HaulierEmail (one row each); the old Email column is retired.
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AddressLine3 { get; set; }
    public string? AddressLine4 { get; set; }
    public string? AddressLine5 { get; set; }
    public string? VatNumber { get; set; }
    public string? NominalCode { get; set; }
    public string? VatCode { get; set; }
    public bool IntakeHaulier { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }
    public int CreatedById { get; set; }
    public int DeletedById { get; set; }

    public class Config : IEntityTypeConfiguration<Haulier>
    {
        public void Configure(EntityTypeBuilder<Haulier> b)
        {
            b.ToTable("Haulier");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
