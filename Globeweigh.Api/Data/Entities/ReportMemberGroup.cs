using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// A named group of Sage accounts a member-sales report adds up as one line — for the Allied
// Merchant Buying Group report, one row per member ("Drinagh Co-Op" = ZDRIN01…ZDRIN06). Kept
// as data rather than code so the office can add a member or a branch from the Reports page
// without a release. ReportKey is the SageReport the group belongs to.
// Table created (and the Allied Merchant members seeded) by Data/Migrations/AddSageSalesMirror.sql.
public class ReportMemberGroup
{
    public int Id { get; set; }
    public string ReportKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string AccountRefs { get; set; } = "";          // comma-separated, upper-case Sage refs
    public int SortOrder { get; set; }

    public IEnumerable<string> Accounts =>
        AccountRefs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToUpperInvariant());

    public class Config : IEntityTypeConfiguration<ReportMemberGroup>
    {
        public void Configure(EntityTypeBuilder<ReportMemberGroup> b)
        {
            b.ToTable("ReportMemberGroup");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.ReportKey).HasMaxLength(40);
            b.Property(e => e.Name).HasMaxLength(60);
            b.Property(e => e.AccountRefs).HasMaxLength(400);
            b.Ignore(e => e.Accounts);
        }
    }
}
