using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// One sign-in attempt, Switch user or Return to my account, for Super Admin > Login History.
// Written by Services/LoginLog from AuthController. See Data/Migrations/AddLoginEventTable.sql.
public class LoginEvent
{
    public const string KindSignIn = "SignIn";
    public const string KindFailed = "Failed";
    public const string KindSwitchUser = "SwitchUser";
    public const string KindSwitchBack = "SwitchBack";

    public int Id { get; set; }
    public DateTime DateLogged { get; set; }
    public string Kind { get; set; } = KindSignIn;
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public int? ByUserId { get; set; }
    public string? Detail { get; set; }
    public string? Client { get; set; }
    public string? Via { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public class Config : IEntityTypeConfiguration<LoginEvent>
    {
        public void Configure(EntityTypeBuilder<LoginEvent> b)
        {
            b.ToTable("LoginEvent");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.DateLogged).HasColumnType("datetime");
            b.Property(e => e.Kind).HasMaxLength(20);
            b.Property(e => e.Username).HasMaxLength(100);
            b.Property(e => e.Detail).HasMaxLength(200);
            b.Property(e => e.Client).HasMaxLength(30);
            b.Property(e => e.Via).HasMaxLength(10);
            b.Property(e => e.IpAddress).HasMaxLength(64);
            b.Property(e => e.UserAgent).HasMaxLength(300);
        }
    }
}
