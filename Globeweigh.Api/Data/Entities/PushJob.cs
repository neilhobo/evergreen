using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// A release-push job — one row per "push this APK to these devices" action, plus its
// per-device queue rows. Backs the TauriAdmin Devices page and the Android pull-update
// flow: the mobile app polls /api/devices/register and self-installs any Pending row.
// Tables created by Data/Migrations/AddPushJobTables.sql.
public class PushJob
{
    public int Id { get; set; }
    public string? Version { get; set; }
    public string? PackageFileName { get; set; }
    public string Status { get; set; } = "Queued"; // Queued | Running | Completed | Cancelled
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public class Config : IEntityTypeConfiguration<PushJob>
    {
        public void Configure(EntityTypeBuilder<PushJob> b)
        {
            b.ToTable("PushJob");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}

// Per-device row of a push job — the pending-update queue the mobile app polls.
public class PushJobDevice
{
    public int Id { get; set; }
    public int PushJobId { get; set; }
    public int? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string Status { get; set; } = "Pending"; // Pending | Done | Cancelled | Superseded
    public string? Detail { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public class Config : IEntityTypeConfiguration<PushJobDevice>
    {
        public void Configure(EntityTypeBuilder<PushJobDevice> b)
        {
            b.ToTable("PushJobDevice");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
