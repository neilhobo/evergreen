using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// A tag on a Staff user (Admin > Staff > Edit > Tags). Not a role: a tag switches on one extra
// feature for that person, whatever their role. See Migrations/AddStaffTagsAndPosMade.sql.
public class UserTagEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Tag { get; set; } = "";
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<UserTagEntity>
    {
        public void Configure(EntityTypeBuilder<UserTagEntity> b)
        {
            b.ToTable("GlobeweighUserTag");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Tag).HasMaxLength(50);
            b.Property(e => e.DateCreated).HasColumnType("datetime");
        }
    }
}

// The tags there are. A fixed list: each arrives with the feature it switches on, so there is
// nothing to create in the UI, only who has it. TauriAdmin (api/staff.ts STAFF_TAGS) and the
// mobile app (models.dart StaffTag) carry the same keys.
public static class StaffTags
{
    // Mobile app: POS Boards, the open orders with a POS board, to mark each product's board
    // made up (PosBoardsController).
    public const string PosCreator = "PosCreator";

    public static readonly string[] All = { PosCreator };
}

// A POS board made up for one product of an order (mobile POS Boards > POS made up).
public class DispatchOrderPosMadeEntity
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int MadeById { get; set; }
    public DateTime DateMade { get; set; }

    public class Config : IEntityTypeConfiguration<DispatchOrderPosMadeEntity>
    {
        public void Configure(EntityTypeBuilder<DispatchOrderPosMadeEntity> b)
        {
            b.ToTable("DispatchOrderPosMade");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.DateMade).HasColumnType("datetime");
        }
    }
}

// A POS order finished by hand (mobile POS Boards > Complete): off the list with whatever boards
// are ticked, since not every product gets one. See Migrations/AddDispatchOrderPosComplete.sql.
public class DispatchOrderPosCompleteEntity
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int CompletedById { get; set; }
    public DateTime DateCompleted { get; set; }

    public class Config : IEntityTypeConfiguration<DispatchOrderPosCompleteEntity>
    {
        public void Configure(EntityTypeBuilder<DispatchOrderPosCompleteEntity> b)
        {
            b.ToTable("DispatchOrderPosComplete");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.DateCompleted).HasColumnType("datetime");
        }
    }
}
