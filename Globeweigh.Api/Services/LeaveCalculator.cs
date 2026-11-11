using Globeweigh.Api.Data.Entities;

namespace Globeweigh.Api.Services;

// Annual Leave sums — every number the Annual Leave pages show comes from here, so the page, the
// approval checks and (later) the employee's own view can never disagree.
//
// The client's rules (2026_Annual_Leave_Entitlement.pdf), and how their spreadsheet applies them:
//   Total     = the category's AnnualDays (28), pro-rated for a starter/leaver by the share of the
//               leave year they are employed for, rounded UP to the next half day.
//   Set       = the company days their category gets that fall on a working day while employed,
//               less any they have been approved to work (LeaveHolidayWorked) — the caller passes
//               THEIR company days, so a worked day moves from Set to Floating, total unchanged.
//   Floating  = Total - Set + manual adjustments. Can be negative: a late starter can earn less
//               than the Christmas shutdown alone (Adam Cushnahan 2026: 7.5 - 11 = -3.5).
//   Remaining = Floating - approved leave. Waiting requests are shown, not deducted.
// These reproduce the 2026 spreadsheet's figures for the three starters exactly.
public static class LeaveCalculator
{
    // Floating days need two weeks' notice (the PDF's "minimum of two weeks' notice").
    public const int NoticeDays = 14;

    public static bool IsWorkingDay(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    // The part of `year` the employee is employed for, or null if none of it.
    public static (DateOnly From, DateOnly To)? EmployedSpan(LeaveEmployee e, int year)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        if (e.StartDate is { } s && s > from) from = s;
        if (e.EndDate is { } f && f < to) to = f;
        return from > to ? null : (from, to);
    }

    public static decimal Total(LeaveEmployee e, decimal annualDays, int year)
    {
        if (EmployedSpan(e, year) is not { } span) return 0;
        var daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
        var employed = span.To.DayNumber - span.From.DayNumber + 1;
        if (employed >= daysInYear) return annualDays;
        var exact = annualDays * employed / daysInYear;
        return Math.Ceiling(exact * 2) / 2;
    }

    // The employee's company days in `year` that fall while they are employed.
    public static int SetDays(LeaveEmployee e, int year, IReadOnlySet<DateOnly> categoryHolidays)
    {
        if (EmployedSpan(e, year) is not { } span) return 0;
        return categoryHolidays.Count(d => d >= span.From && d <= span.To && IsWorkingDay(d));
    }

    // Days a request takes: working days in the range that are not the employee's company days;
    // a single-day request can be a half or quarter day.
    public static decimal RequestDays(DateOnly start, DateOnly end, decimal dayLength, IReadOnlySet<DateOnly> categoryHolidays)
    {
        if (end < start) return 0;
        if (start == end)
            return IsWorkingDay(start) && !categoryHolidays.Contains(start) ? dayLength : 0;
        var n = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
            if (IsWorkingDay(d) && !categoryHolidays.Contains(d)) n++;
        return n;
    }

    // Unpaid: the 2027 policy — "If a pro-rata paid allowance does not cover remaining fixed
    // company holidays, any excess will be treated as unpaid leave ... Additional approved days
    // off after paid leave is exhausted will also be unpaid." So a minus Remaining is unpaid days.
    // Unbooked: floating days not yet approved or asked for (the 31 August check).
    public record Balance(decimal Total, int Set, decimal Adjustments, decimal Floating, decimal Used, decimal Waiting, decimal Remaining)
    {
        public decimal Unpaid => Remaining < 0 ? -Remaining : 0;
        public decimal Unbooked => Math.Max(0, Remaining - Waiting);
    }

    // How many of a request's days would be unpaid: the part of it past the paid allowance.
    public static decimal UnpaidPart(decimal remainingBefore, decimal remainingAfter) =>
        Math.Max(0, -remainingAfter) - Math.Max(0, -remainingBefore);

    public static Balance BalanceFor(
        LeaveEmployee e, decimal annualDays, int year, IReadOnlySet<DateOnly> categoryHolidays,
        IEnumerable<LeaveRequest> requests, IEnumerable<LeaveAdjustment> adjustments)
    {
        var total = Total(e, annualDays, year);
        var set = SetDays(e, year, categoryHolidays);
        var adj = adjustments.Where(a => a.Year == year && a.DateDeleted == null).Sum(a => a.Days);
        var floating = total - set + adj;
        var inYear = requests.Where(r => r.DateDeleted == null && r.StartDate.Year == year).ToList();
        var used = inYear.Where(r => r.Status == LeaveRequest.StatusApproved).Sum(r => r.Days);
        var waiting = inYear.Where(r => r.Status == LeaveRequest.StatusWaiting).Sum(r => r.Days);
        return new Balance(total, set, adj, floating, used, waiting, floating - used);
    }
}
