using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// A saved Sage financial report (Reports > Sage Financial Reports): a name the office gave it
// and, through ReportKey, the account groups it adds up (ReportMemberGroup). Every one is a
// member-sales report (Services/MemberSalesReport.cs). The Allied Merchant Buying Group is the
// first, under its original key "AlliedMerchant"; new ones get a generated key.
// Table created by Data/Migrations/AddSageReport.sql.
public class SageReport
{
    public int Id { get; set; }
    public string ReportKey { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string? CreatedBy { get; set; }

    public class Config : IEntityTypeConfiguration<SageReport>
    {
        public void Configure(EntityTypeBuilder<SageReport> b)
        {
            b.ToTable("SageReport");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.ReportKey).HasMaxLength(40);
            b.Property(e => e.Name).HasMaxLength(80);
            b.Property(e => e.CreatedBy).HasMaxLength(100);
        }
    }
}
