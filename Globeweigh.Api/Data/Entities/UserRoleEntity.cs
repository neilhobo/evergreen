using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the Globeweigh_Role junction (user ↔ role). The desktop app
// resolves a user's effective roles from here, so the single GlobeweighUser.RoleId
// can be 0. Keyed (not a keyless read-model) so the Staff editor can add/remove rows.
public class UserRoleEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<UserRoleEntity>
    {
        public void Configure(EntityTypeBuilder<UserRoleEntity> b)
        {
            b.ToTable("Globeweigh_Role");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
