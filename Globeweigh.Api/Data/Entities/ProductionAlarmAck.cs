using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One row per missed-scan pallet whose alarm has been silenced (see ProductionController's
// alarm endpoints). A missed pallet with NO row here is an active alarm; suppressing inserts
// rows for everything currently missed, so only a NEW missed pallet re-fires the alarm.
// See Migrations/AddProductionAlarm.sql.
public class ProductionAlarmAck
{
    public int PalletId { get; set; }
    public DateTime AckedAtUtc { get; set; }
    public string? AckedBy { get; set; }

    public class Config : IEntityTypeConfiguration<ProductionAlarmAck>
    {
        public void Configure(EntityTypeBuilder<ProductionAlarmAck> b)
        {
            b.ToTable("ProductionAlarmAck");
            b.HasKey(e => e.PalletId);
            b.Property(e => e.PalletId).ValueGeneratedNever();
            b.Property(e => e.AckedBy).HasMaxLength(100);
        }
    }
}
