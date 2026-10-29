using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// A Sage account the office will never copy into Evergreen (Sage Link > Comparison, "Ignore"
// on Missing in Evergreen). Keyed by the upper-cased A/C ref; no row = not ignored.
// Table created by Data/Migrations/AddSageIgnoredAccount.sql.
public class SageIgnoredAccount
{
    public string AccountRef { get; set; } = "";
    public int IgnoredById { get; set; }
    public DateTime DateIgnored { get; set; }

    public class Config : IEntityTypeConfiguration<SageIgnoredAccount>
    {
        public void Configure(EntityTypeBuilder<SageIgnoredAccount> b)
        {
            b.ToTable("SageIgnoredAccount");
            b.HasKey(e => e.AccountRef);
            b.Property(e => e.AccountRef).HasMaxLength(20);
            b.Property(e => e.DateIgnored).HasColumnType("datetime");
        }
    }
}
