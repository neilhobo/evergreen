using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Annual Leave (TauriAdmin > Annual Leave). The tables and the rules behind them are described in
// Data/Migrations/AddAnnualLeaveTables.sql; the sums are Services/LeaveCalculator.cs.

// Office / Factory & HGV / UK Sales: the full-year entitlement, and (through
// LeaveCompanyHolidayCategory) which company days its staff get.
public class LeaveCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal AnnualDays { get; set; }
    public int SortOrder { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<LeaveCategory>
    {
        public void Configure(EntityTypeBuilder<LeaveCategory> b)
        {
            b.ToTable("LeaveCategory");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Name).HasMaxLength(100);
            b.Property(e => e.AnnualDays).HasColumnType("decimal(6,2)");
            b.Property(e => e.DateCreated).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
            b.Property(e => e.DateDeleted).HasColumnType("datetime");
        }
    }
}

// A company day off. Which categories get it: LeaveCompanyHolidayCategory.
public class LeaveCompanyHoliday
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<LeaveCompanyHoliday>
    {
        public void Configure(EntityTypeBuilder<LeaveCompanyHoliday> b)
        {
            b.ToTable("LeaveCompanyHoliday");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Name).HasMaxLength(100);
            b.Property(e => e.DateCreated).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
        }
    }
}

public class LeaveCompanyHolidayCategory
{
    public int HolidayId { get; set; }
    public int CategoryId { get; set; }

    public class Config : IEntityTypeConfiguration<LeaveCompanyHolidayCategory>
    {
        public void Configure(EntityTypeBuilder<LeaveCompanyHolidayCategory> b)
        {
            b.ToTable("LeaveCompanyHolidayCategory");
            b.HasKey(e => new { e.HolidayId, e.CategoryId });
        }
    }
}

// A stretch where leave is flagged to the approver: peak season, the Sales trade shows.
public class LeaveBlockedPeriod
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    // NULL = every category.
    public int? CategoryId { get; set; }
    // A request of more than this many days inside the period is flagged; NULL = any request.
    public decimal? MaxDays { get; set; }
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<LeaveBlockedPeriod>
    {
        public void Configure(EntityTypeBuilder<LeaveBlockedPeriod> b)
        {
            b.ToTable("LeaveBlockedPeriod");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Name).HasMaxLength(100);
            b.Property(e => e.MaxDays).HasColumnType("decimal(6,2)");
            b.Property(e => e.DateCreated).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
        }
    }
}

// One Admin > Staff user's leave profile (Data/Migrations/LinkLeaveEmployeeToStaff.sql): the
// person, their name and their login are the GlobeweighUser; this is only what Annual Leave
// needs on top. One row per user; DateDeleted = taken out of Annual Leave (put back = restored).
public class LeaveEmployee
{
    public int Id { get; set; }
    public int GlobeweighUserId { get; set; }
    public int CategoryId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public int? ApproverUserId { get; set; }
    public string? Notes { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<LeaveEmployee>
    {
        public void Configure(EntityTypeBuilder<LeaveEmployee> b)
        {
            b.ToTable("LeaveEmployee");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Email).HasMaxLength(200);
            b.Property(e => e.Mobile).HasMaxLength(50);
            b.Property(e => e.Notes).HasMaxLength(1000);
            b.Property(e => e.DateCreated).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
            b.Property(e => e.DateDeleted).HasColumnType("datetime");
        }
    }
}

// An employee works one of their company days and gets a floating day instead
// (Data/Migrations/AddLeaveWorkedDaysAndReminders.sql): it is no longer one of THEIR company days.
public class LeaveHolidayWorked
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int HolidayId { get; set; }
    public string Reason { get; set; } = "";
    public int? CreatedById { get; set; }
    public DateTime DateCreated { get; set; }

    public class Config : IEntityTypeConfiguration<LeaveHolidayWorked>
    {
        public void Configure(EntityTypeBuilder<LeaveHolidayWorked> b)
        {
            b.ToTable("LeaveHolidayWorked");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Reason).HasMaxLength(300);
            b.Property(e => e.DateCreated).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
        }
    }
}

// An "unbooked leave" reminder email (Annual Leave > Outstanding).
public class LeaveReminder
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
    public string SentTo { get; set; } = "";
    public int? SentById { get; set; }
    public DateTime DateSent { get; set; }

    public class Config : IEntityTypeConfiguration<LeaveReminder>
    {
        public void Configure(EntityTypeBuilder<LeaveReminder> b)
        {
            b.ToTable("LeaveReminder");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Days).HasColumnType("decimal(6,2)");
            b.Property(e => e.SentTo).HasMaxLength(200);
            b.Property(e => e.DateSent).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
        }
    }
}

// A manual + or - to one employee's floating days for one year.
public class LeaveAdjustment
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
    public string Reason { get; set; } = "";
    public int? CreatedById { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<LeaveAdjustment>
    {
        public void Configure(EntityTypeBuilder<LeaveAdjustment> b)
        {
            b.ToTable("LeaveAdjustment");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Days).HasColumnType("decimal(6,2)");
            b.Property(e => e.Reason).HasMaxLength(300);
            b.Property(e => e.DateCreated).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
            b.Property(e => e.DateDeleted).HasColumnType("datetime");
        }
    }
}

// A request for floating leave; Days is fixed when it is made.
public class LeaveRequest
{
    public const int StatusWaiting = 0;
    public const int StatusApproved = 1;
    public const int StatusDeclined = 2;
    public const int StatusCancelled = 3;

    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal DayLength { get; set; } = 1;
    public decimal Days { get; set; }
    public int Status { get; set; }
    public string? Note { get; set; }
    public string? DecisionNote { get; set; }
    public int? RequestedById { get; set; }
    public int? DecidedById { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DateDecided { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }

    public class Config : IEntityTypeConfiguration<LeaveRequest>
    {
        public void Configure(EntityTypeBuilder<LeaveRequest> b)
        {
            b.ToTable("LeaveRequest");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.DayLength).HasColumnType("decimal(4,2)");
            b.Property(e => e.Days).HasColumnType("decimal(6,2)");
            b.Property(e => e.Note).HasMaxLength(1000);
            b.Property(e => e.DecisionNote).HasMaxLength(1000);
            b.Property(e => e.DecidedByName).HasMaxLength(100);
            b.Property(e => e.DateDecided).HasColumnType("datetime");
            b.Property(e => e.DateCreated).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
            b.Property(e => e.DateDeleted).HasColumnType("datetime");
        }
    }
}
