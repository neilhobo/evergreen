using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Annual Leave (TauriAdmin > Annual Leave): the office side — staff and their balances, leave
// requests and their approval, the calendar, the company days and the categories. The sums are
// Services/LeaveCalculator.cs; the tables, Data/Migrations/AddAnnualLeaveTables.sql.
//
// Staged (FeatureFlag AnnualLeave): while it is off only the super-admin gets in; once it is on,
// office admins do too. Refused here, not just hidden from the menu.
[ApiController]
[Authorize]
[Route("api/annual-leave")]
public class AnnualLeaveController : ControllerBase
{
    private const string Feature = "AnnualLeave";
    // eUserRole: GlobeweighAdmin 1, OfficeAdmin 2 — the roles that manage leave once released.
    private static readonly int[] ManagerRoles = { 1, 2 };
    // eUserType.Touch — passcode-only users, never approvers.
    private const int UserTypeTouch = 2;
    // eUserType.NoLogin — Staff who never sign in (drivers, factory): in Annual Leave, never approvers.
    private const int UserTypeNoLogin = 4;

    private readonly GbEvergreenContext _db;
    private readonly EmailSender _email;
    private readonly ILogger<AnnualLeaveController> _log;

    public AnnualLeaveController(GbEvergreenContext db, EmailSender email, ILogger<AnnualLeaveController> log)
    {
        _db = db;
        _email = email;
        _log = log;
    }

    private async Task<bool> CanManageAsync(CancellationToken ct)
    {
        if (await this.IsSuperAdminAsync(_db, ct)) return true;
        var on = await _db.Set<FeatureFlagEntity>().AsNoTracking().AnyAsync(f => f.Name == Feature && f.Enabled, ct);
        if (!on) return false;
        var id = this.GetUserId();
        return await _db.GlobeweighUsers.AsNoTracking().AnyAsync(u => u.Id == id && u.DateDeleted == null && ManagerRoles.Contains(u.RoleId), ct)
            || await _db.Set<UserRoleEntity>().AsNoTracking().AnyAsync(r => r.UserId == id && ManagerRoles.Contains(r.RoleId), ct);
    }

    // GET /api/annual-leave/access/me — whether the menu offers the page.
    [HttpGet("access/me")]
    public async Task<ActionResult<object>> AccessMe(CancellationToken ct = default) =>
        new { canView = await CanManageAsync(ct) };

    // ---- shared loading ---------------------------------------------------------------------

    // Company days by category: categoryId -> the dates it gets off (every year).
    private async Task<Dictionary<int, HashSet<DateOnly>>> HolidaysByCategoryAsync(CancellationToken ct)
    {
        var rows = await (from hc in _db.Set<LeaveCompanyHolidayCategory>()
                          join h in _db.Set<LeaveCompanyHoliday>() on hc.HolidayId equals h.Id
                          select new { hc.CategoryId, h.Date }).AsNoTracking().ToListAsync(ct);
        return rows.GroupBy(r => r.CategoryId).ToDictionary(g => g.Key, g => g.Select(r => r.Date).ToHashSet());
    }

    private static IReadOnlySet<DateOnly> For(Dictionary<int, HashSet<DateOnly>> map, int categoryId) =>
        map.TryGetValue(categoryId, out var s) ? s : new HashSet<DateOnly>();

    // The company days each employee has been approved to work (LeaveHolidayWorked): employeeId -> dates.
    private async Task<Dictionary<int, HashSet<DateOnly>>> WorkedByEmployeeAsync(CancellationToken ct)
    {
        var rows = await (from w in _db.Set<LeaveHolidayWorked>()
                          join h in _db.Set<LeaveCompanyHoliday>() on w.HolidayId equals h.Id
                          select new { w.EmployeeId, h.Date }).AsNoTracking().ToListAsync(ct);
        return rows.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.Select(r => r.Date).ToHashSet());
    }

    // THEIR company days: their category's, less any they are working. Everything per-person
    // (set days, the days a request takes) is worked out from this, so a worked company day moves
    // from Set to Floating with the total unchanged — the 2027 policy's "substitute a floating day".
    private static IReadOnlySet<DateOnly> Mine(
        Dictionary<int, HashSet<DateOnly>> hols, Dictionary<int, HashSet<DateOnly>> worked, LeaveEmployee e)
    {
        var cat = For(hols, e.CategoryId);
        return worked.TryGetValue(e.Id, out var w) && w.Count > 0 ? cat.Where(d => !w.Contains(d)).ToHashSet() : cat;
    }

    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.GlobeweighUsers.AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.Surname}".Trim(), ct);
    }

    private async Task<string> MyNameAsync(CancellationToken ct)
    {
        var id = this.GetUserId();
        var u = await _db.GlobeweighUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return u == null ? "" : $"{u.FirstName} {u.Surname}".Trim();
    }

    // ---- staff + balances -------------------------------------------------------------------

    // The people in Annual Leave: Admin > Staff users with a live leave profile. The name and the
    // login are the Staff user's; the rest is the profile's.
    private sealed class Person
    {
        public LeaveEmployee E { get; init; } = null!;
        public string Forename { get; init; } = "";
        public string Surname { get; init; } = "";
        public string? Login { get; init; }
        public string Name => $"{Forename} {Surname}".Trim();
        // Where approval emails go: the profile's address, else their login when that is one
        // (office logins are email addresses).
        public string? EmailTo => !string.IsNullOrWhiteSpace(E.Email) ? E.Email
            : Login != null && Login.Contains('@') ? Login.Trim() : null;
    }

    private IQueryable<Person> People() =>
        from e in _db.Set<LeaveEmployee>().AsNoTracking()
        join u in _db.GlobeweighUsers.AsNoTracking() on e.GlobeweighUserId equals u.Id
        where e.DateDeleted == null && u.DateDeleted == null
        select new Person { E = e, Forename = u.FirstName ?? "", Surname = u.Surname ?? "", Login = u.Username };

    private async Task<Person?> PersonAsync(int employeeId, CancellationToken ct) =>
        await People().FirstOrDefaultAsync(p => p.E.Id == employeeId, ct);

    public record EmployeeRow(
        int Id, int UserId, string Forename, string Surname, int CategoryId, string CategoryName,
        DateOnly? StartDate, DateOnly? EndDate, string? Email, string? EmailTo, string? Mobile,
        int? ApproverUserId, string? ApproverName, string? Notes,
        decimal Total, int Set, decimal Adjustments, decimal Floating, decimal Used, decimal Waiting, decimal Remaining,
        decimal Unpaid, decimal Unbooked, int Worked);

    private async Task<List<EmployeeRow>> EmployeeRowsAsync(int year, CancellationToken ct)
    {
        var cats = await _db.Set<LeaveCategory>().AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var hols = await HolidaysByCategoryAsync(ct);
        var worked = await WorkedByEmployeeAsync(ct);
        var people = (await People().ToListAsync(ct))
            .OrderBy(p => p.Surname.Trim(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Forename.Trim(), StringComparer.OrdinalIgnoreCase).ToList();
        var reqs = (await _db.Set<LeaveRequest>().AsNoTracking()
                .Where(r => r.DateDeleted == null && r.StartDate.Year == year).ToListAsync(ct))
            .ToLookup(r => r.EmployeeId);
        var adjs = (await _db.Set<LeaveAdjustment>().AsNoTracking()
                .Where(a => a.DateDeleted == null && a.Year == year).ToListAsync(ct))
            .ToLookup(a => a.EmployeeId);
        var approvers = await UserNamesAsync(people.Where(p => p.E.ApproverUserId != null).Select(p => p.E.ApproverUserId!.Value), ct);

        // Leavers drop off once they are gone for the whole year asked about.
        return people.Where(p => LeaveCalculator.EmployedSpan(p.E, year) != null).Select(p =>
        {
            var e = p.E;
            var cat = cats.GetValueOrDefault(e.CategoryId);
            var b = LeaveCalculator.BalanceFor(e, cat?.AnnualDays ?? 0, year, Mine(hols, worked, e), reqs[e.Id], adjs[e.Id]);
            var workedN = worked.GetValueOrDefault(e.Id)?.Count(d => d.Year == year) ?? 0;
            return new EmployeeRow(e.Id, e.GlobeweighUserId, p.Forename.Trim(), p.Surname.Trim(), e.CategoryId, cat?.Name ?? "",
                e.StartDate, e.EndDate, e.Email, p.EmailTo, e.Mobile, e.ApproverUserId,
                e.ApproverUserId is { } a ? approvers.GetValueOrDefault(a) : null, e.Notes,
                b.Total, b.Set, b.Adjustments, b.Floating, b.Used, b.Waiting, b.Remaining,
                b.Unpaid, b.Unbooked, workedN);
        }).ToList();
    }

    // GET /api/annual-leave/employees?year=2026 — everyone with their balance for the year.
    [HttpGet("employees")]
    public async Task<ActionResult<List<EmployeeRow>>> Employees([FromQuery] int? year, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        return await EmployeeRowsAsync(year ?? DateTime.Today.Year, ct);
    }

    // ---- a Staff user's leave profile (Admin > Staff's Annual Leave section) ----------------
    //
    // People are added, named and removed in Admin > Staff; Annual Leave only holds their leave
    // profile. Both the Staff dialog and Annual Leave > Staff edit it through these two.

    public record StaffLeaveRow(int UserId, int EmployeeId, string CategoryName);

    // GET /api/annual-leave/staff — who is in Annual Leave, by Staff user (Admin > Staff's column).
    [HttpGet("staff")]
    public async Task<ActionResult<List<StaffLeaveRow>>> StaffLeave(CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var cats = await _db.Set<LeaveCategory>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return (await People().ToListAsync(ct))
            .Select(p => new StaffLeaveRow(p.E.GlobeweighUserId, p.E.Id, cats.GetValueOrDefault(p.E.CategoryId) ?? "")).ToList();
    }

    public class LeaveProfile
    {
        // False = not in Annual Leave (never was, or taken out).
        public bool Enrolled { get; set; }
        public int? EmployeeId { get; set; }
        public int? CategoryId { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string? Email { get; set; }
        public string? Mobile { get; set; }
        public int? ApproverUserId { get; set; }
        public string? Notes { get; set; }
        // Read only: their login, when it is an email address — where emails go if Email is blank.
        public string? LoginEmail { get; set; }
    }

    private static string? LoginEmail(GlobeweighUser u) =>
        u.Username != null && u.Username.Contains('@') ? u.Username.Trim() : null;

    // GET /api/annual-leave/staff/{userId} — one Staff user's leave profile.
    [HttpGet("staff/{userId:int}")]
    public async Task<ActionResult<LeaveProfile>> GetProfile(int userId, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var u = await _db.GlobeweighUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.DateDeleted == null, ct);
        if (u == null) return NotFound(new { message = "That user no longer exists." });
        var e = await _db.Set<LeaveEmployee>().AsNoTracking().FirstOrDefaultAsync(x => x.GlobeweighUserId == userId, ct);
        if (e == null) return new LeaveProfile { LoginEmail = LoginEmail(u) };
        return new LeaveProfile
        {
            Enrolled = e.DateDeleted == null, EmployeeId = e.Id, CategoryId = e.CategoryId,
            StartDate = e.StartDate, EndDate = e.EndDate, Email = e.Email, Mobile = e.Mobile,
            ApproverUserId = e.ApproverUserId, Notes = e.Notes, LoginEmail = LoginEmail(u),
        };
    }

    // PUT /api/annual-leave/staff/{userId} — set it. Enrolled = false takes them out of Annual
    // Leave (their history is kept, and comes back if they are put back in).
    [HttpPut("staff/{userId:int}")]
    public async Task<IActionResult> SaveProfile(int userId, [FromBody] LeaveProfile input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        if (!await _db.GlobeweighUsers.AnyAsync(x => x.Id == userId && x.DateDeleted == null, ct))
            return NotFound(new { message = "That user no longer exists." });
        var e = await _db.Set<LeaveEmployee>().FirstOrDefaultAsync(x => x.GlobeweighUserId == userId, ct);

        if (!input.Enrolled)
        {
            if (e is { DateDeleted: null })
            {
                e.DateDeleted = DateTime.Now;
                await _db.SaveChangesAsync(ct);
            }
            return NoContent();
        }

        if (input.CategoryId is not { } catId || !await _db.Set<LeaveCategory>().AnyAsync(c => c.Id == catId && c.DateDeleted == null, ct))
            return BadRequest(new { message = "Choose an Annual Leave category." });
        if (input.StartDate is { } s && input.EndDate is { } f && f < s)
            return BadRequest(new { message = "The leaving date is before the start date." });

        if (e == null)
        {
            e = new LeaveEmployee { GlobeweighUserId = userId, DateCreated = DateTime.Now };
            _db.Add(e);
        }
        e.DateDeleted = null;
        e.CategoryId = catId;
        e.StartDate = input.StartDate;
        e.EndDate = input.EndDate;
        e.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        e.Mobile = string.IsNullOrWhiteSpace(input.Mobile) ? null : input.Mobile.Trim();
        e.ApproverUserId = input.ApproverUserId;
        e.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = e.Id });
    }

    public record AdjustmentRow(int Id, decimal Days, string Reason, string? CreatedByName, DateTime DateCreated);

    // GET /api/annual-leave/employees/{id}/adjustments?year=
    [HttpGet("employees/{id:int}/adjustments")]
    public async Task<ActionResult<List<AdjustmentRow>>> Adjustments(int id, [FromQuery] int year, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var rows = await _db.Set<LeaveAdjustment>().AsNoTracking()
            .Where(a => a.EmployeeId == id && a.Year == year && a.DateDeleted == null)
            .OrderBy(a => a.DateCreated).ToListAsync(ct);
        var names = await UserNamesAsync(rows.Where(a => a.CreatedById != null).Select(a => a.CreatedById!.Value), ct);
        return rows.Select(a => new AdjustmentRow(a.Id, a.Days, a.Reason,
            a.CreatedById is { } c ? names.GetValueOrDefault(c) : null, a.DateCreated)).ToList();
    }

    public class AdjustmentInput
    {
        public int Year { get; set; }
        public decimal Days { get; set; }
        public string Reason { get; set; } = "";
    }

    [HttpPost("employees/{id:int}/adjustments")]
    public async Task<IActionResult> AddAdjustment(int id, [FromBody] AdjustmentInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        if (input.Days == 0 || input.Days % 0.25m != 0) return BadRequest(new { message = "Enter the days to add or take away, in quarter days." });
        if (string.IsNullOrWhiteSpace(input.Reason)) return BadRequest(new { message = "Give a reason." });
        if (!await _db.Set<LeaveEmployee>().AnyAsync(e => e.Id == id && e.DateDeleted == null, ct))
            return NotFound(new { message = "That employee no longer exists." });
        _db.Add(new LeaveAdjustment
        {
            EmployeeId = id, Year = input.Year, Days = input.Days, Reason = input.Reason.Trim(),
            CreatedById = this.GetUserId(), DateCreated = DateTime.Now,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("adjustments/{id:int}")]
    public async Task<IActionResult> DeleteAdjustment(int id, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var a = await _db.Set<LeaveAdjustment>().FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (a == null) return NotFound();
        a.DateDeleted = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/annual-leave/approvers — office logins a request can be signed off by.
    [HttpGet("approvers")]
    public async Task<ActionResult<object>> Approvers(CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        return Ok(await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.DateDeleted == null && !u.Disabled && u.Id != 1
                        && u.UserTypeId != UserTypeTouch && u.UserTypeId != UserTypeNoLogin)
            .OrderBy(u => u.FirstName).ThenBy(u => u.Surname)
            .Select(u => new { id = u.Id, name = (u.FirstName + " " + u.Surname).Trim() })
            .ToListAsync(ct));
    }

    // ---- requests ---------------------------------------------------------------------------

    // Key: which policy it is (dates, overlap, employment, notice, allowance, peak, clash), so the
    // Requests grid can give each its own column; Short: the cell text for that column.
    public record Check(string Level, string Text, string? Key = null, string? Short = null);

    public record RequestRow(
        int Id, int EmployeeId, string EmployeeName, string CategoryName,
        DateOnly StartDate, DateOnly EndDate, decimal DayLength, decimal Days, int Status,
        string? Note, string? DecisionNote, string? DecidedByName, DateTime? DateDecided, DateTime DateCreated,
        string? RequestedByName, decimal RemainingBefore, decimal RemainingAfter, List<Check> Checks);

    // Everything the approver should know about this leave before saying yes: hard errors (it
    // cannot be saved) and warnings (the client's policy says think twice), plus the passes, so a
    // clean request reads as checked rather than unchecked.
    private async Task<(decimal Days, decimal RemainingBefore, decimal RemainingAfter, List<Check> Checks)> AssessAsync(
        LeaveEmployee emp, DateOnly start, DateOnly end, decimal dayLength, int? excludeRequestId, CancellationToken ct)
    {
        var checks = new List<Check>();
        var hols = await HolidaysByCategoryAsync(ct);
        var mine = Mine(hols, await WorkedByEmployeeAsync(ct), emp);
        var days = LeaveCalculator.RequestDays(start, end, dayLength, mine);
        var year = start.Year;

        if (end < start) checks.Add(new("error", "The last day is before the first day.", "dates", "Bad dates"));
        if (start.Year != end.Year) checks.Add(new("error", "Leave can't run into the next year — book it as two requests.", "dates", "Spans 2 years"));
        if (days == 0 && end >= start) checks.Add(new("error", "There are no working days in those dates (weekends and company days don't count).", "dates", "No working days"));

        var cat = await _db.Set<LeaveCategory>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == emp.CategoryId, ct);
        var reqs = await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.EmployeeId == emp.Id && r.DateDeleted == null && r.StartDate.Year == year).ToListAsync(ct);
        var adjs = await _db.Set<LeaveAdjustment>().AsNoTracking()
            .Where(a => a.EmployeeId == emp.Id && a.Year == year && a.DateDeleted == null).ToListAsync(ct);
        var others = reqs.Where(r => r.Id != excludeRequestId).ToList();
        var bal = LeaveCalculator.BalanceFor(emp, cat?.AnnualDays ?? 0, year, mine, others, adjs);
        var before = bal.Remaining;
        var after = before - days;

        var overlap = others.FirstOrDefault(r =>
            (r.Status is LeaveRequest.StatusWaiting or LeaveRequest.StatusApproved) && r.StartDate <= end && r.EndDate >= start);
        if (overlap != null)
            checks.Add(new("error", $"Already has leave {Range(overlap.StartDate, overlap.EndDate)} ({(overlap.Status == LeaveRequest.StatusApproved ? "approved" : "waiting")}).", "overlap", "Overlaps"));

        if (emp.StartDate is { } s && start < s) checks.Add(new("warn", $"Starts before their start date ({s:d MMM yyyy}).", "employment", "Before start"));
        if (emp.EndDate is { } f && end > f) checks.Add(new("warn", $"Runs past their leaving date ({f:d MMM yyyy}).", "employment", "After leaving"));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var notice = start.DayNumber - today.DayNumber;
        if (notice < 0) checks.Add(new("info", "These dates are in the past.", "notice", "Past"));
        else if (notice < LeaveCalculator.NoticeDays) checks.Add(new("warn", $"Only {notice} day{(notice == 1 ? "" : "s")}' notice — floating days need 2 weeks.", "notice", $"{notice}d"));
        else checks.Add(new("ok", $"2 weeks' notice: {notice} days' notice given.", "notice", $"{notice}d"));

        // Past the paid allowance is unpaid (2027 policy), and there is no carry-over to borrow from.
        var unpaid = LeaveCalculator.UnpaidPart(before, after);
        if (unpaid > 0)
            checks.Add(new("warn", unpaid == days
                ? $"Unpaid: they have no paid leave left, so all {Fmt(days)} day{(days == 1 ? "" : "s")} would be unpaid ({Fmt(-after)} unpaid in {year} after this)."
                : $"Unpaid: {Fmt(unpaid)} of these {Fmt(days)} days would be unpaid — past their paid allowance.", "allowance", $"{Fmt(-after)} unpaid"));
        else if (after < 0)
            checks.Add(new("warn", $"They are already {Fmt(-after)} day{(-after == 1 ? "" : "s")} past their paid allowance — those days are unpaid.", "allowance", $"{Fmt(-after)} unpaid"));
        else checks.Add(new("ok", $"Within allowance: {Fmt(after)} floating day{(after == 1 ? "" : "s")} left after.", "allowance", $"{Fmt(after)} left"));

        var periods = await _db.Set<LeaveBlockedPeriod>().AsNoTracking()
            .Where(p => p.StartDate <= end && p.EndDate >= start && (p.CategoryId == null || p.CategoryId == emp.CategoryId))
            .ToListAsync(ct);
        foreach (var p in periods)
        {
            if (p.MaxDays is { } max && days <= max)
                checks.Add(new("ok", $"{p.Name}: {Fmt(days)} day{(days == 1 ? "" : "s")} is within the {Fmt(max)}-day limit.", "peak", $"{Fmt(days)} / {Fmt(max)}"));
            else
                checks.Add(new("warn", p.MaxDays is { } m
                    ? $"{p.Name} ({Range(p.StartDate, p.EndDate)}): more than {Fmt(m)} days off is discouraged."
                    : $"{p.Name} ({Range(p.StartDate, p.EndDate)}): leave isn't normally approved.",
                    "peak", p.MaxDays is { } m2 ? $"{Fmt(days)} / {Fmt(m2)}" : "Not normally"));
        }

        // Who else in the same category is off on any of those working days.
        var clash = await (from r in _db.Set<LeaveRequest>().AsNoTracking()
                           join p in People() on r.EmployeeId equals p.E.Id
                           where r.DateDeleted == null && p.E.Id != emp.Id
                                 && p.E.CategoryId == emp.CategoryId
                                 && (r.Status == LeaveRequest.StatusApproved || r.Status == LeaveRequest.StatusWaiting)
                                 && r.StartDate <= end && r.EndDate >= start
                           select new { p.Forename, p.Surname, r.Status }).ToListAsync(ct);
        if (clash.Count > 0)
        {
            var names = string.Join(", ", clash.Select(c => $"{$"{c.Forename} {c.Surname}".Trim()}{(c.Status == LeaveRequest.StatusWaiting ? " (waiting)" : "")}").Distinct());
            // Short: "G. McCann", or "G. McCann +2" when more.
            var shortNames = clash.Select(c => $"{(c.Forename.Trim().Length > 0 ? c.Forename.Trim()[0] + ". " : "")}{c.Surname.Trim()}").Distinct().ToList();
            checks.Add(new("warn", $"Also off then ({cat?.Name}): {names}.", "clash",
                shortNames.Count == 1 ? shortNames[0] : $"{shortNames[0]} +{shortNames.Count - 1}"));
        }
        else checks.Add(new("ok", $"Nobody else in {cat?.Name} is off then.", "clash", "None"));

        return (days, before, after, checks);
    }

    private static string Fmt(decimal d) => d.ToString("0.##");
    private static string Range(DateOnly a, DateOnly b) =>
        a == b ? a.ToString("ddd d MMM") : $"{a:ddd d MMM} – {b:ddd d MMM}";

    // GET /api/annual-leave/requests?year=2026 — every request in the year, newest first, with the
    // checks worked out for the waiting ones.
    [HttpGet("requests")]
    public async Task<ActionResult<List<RequestRow>>> Requests([FromQuery] int? year, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var y = year ?? DateTime.Today.Year;
        var rows = await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.DateDeleted == null && r.StartDate.Year == y)
            .OrderByDescending(r => r.DateCreated).ThenByDescending(r => r.Id).ToListAsync(ct);
        var people = (await People().ToListAsync(ct)).ToDictionary(p => p.E.Id);
        var cats = await _db.Set<LeaveCategory>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var names = await UserNamesAsync(rows.Where(r => r.RequestedById != null).Select(r => r.RequestedById!.Value), ct);
        var list = new List<RequestRow>();
        foreach (var r in rows)
        {
            if (!people.TryGetValue(r.EmployeeId, out var p)) continue;
            var e = p.E;
            decimal before = 0, after = 0;
            var checks = new List<Check>();
            if (r.Status == LeaveRequest.StatusWaiting)
                (_, before, after, checks) = await AssessAsync(e, r.StartDate, r.EndDate, r.DayLength, r.Id, ct);
            list.Add(new RequestRow(r.Id, e.Id, p.Name, cats.GetValueOrDefault(e.CategoryId) ?? "",
                r.StartDate, r.EndDate, r.DayLength, r.Days, r.Status, r.Note, r.DecisionNote, r.DecidedByName, r.DateDecided,
                r.DateCreated, r.RequestedById is { } q ? names.GetValueOrDefault(q) : null, before, after, checks));
        }
        return list;
    }

    // GET /api/annual-leave/requests/{id} — one request (the calendar's popup), with what it does
    // to their balance and the policy checks for its dates (worked out without itself, so editing
    // it never clashes with itself).
    [HttpGet("requests/{id:int}")]
    public async Task<ActionResult<RequestRow>> GetRequest(int id, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var r = await _db.Set<LeaveRequest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (r == null) return NotFound(new { message = "That request no longer exists." });
        var p = await PersonAsync(r.EmployeeId, ct);
        if (p == null) return NotFound(new { message = "They are no longer in Annual Leave (Admin > Staff)." });
        var cat = await _db.Set<LeaveCategory>().AsNoTracking().Where(c => c.Id == p.E.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var requestedBy = r.RequestedById is { } q ? (await UserNamesAsync(new[] { q }, ct)).GetValueOrDefault(q) : null;
        decimal before = 0, after = 0;
        var checks = new List<Check>();
        if (r.Status is LeaveRequest.StatusWaiting or LeaveRequest.StatusApproved)
            (_, before, after, checks) = await AssessAsync(p.E, r.StartDate, r.EndDate, r.DayLength, r.Id, ct);
        return new RequestRow(r.Id, p.E.Id, p.Name, cat ?? "", r.StartDate, r.EndDate, r.DayLength, r.Days, r.Status,
            r.Note, r.DecisionNote, r.DecidedByName, r.DateDecided, r.DateCreated, requestedBy, before, after, checks);
    }

    // PUT /api/annual-leave/requests/{id} — change a waiting or approved request's dates, length
    // or note. Its days are worked out again and the same checks apply (errors refuse the change);
    // it keeps its status, and the change is noted on it with who made it.
    [HttpPut("requests/{id:int}")]
    public async Task<IActionResult> UpdateRequest(int id, [FromBody] RequestInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var r = await _db.Set<LeaveRequest>().FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (r == null) return NotFound(new { message = "That request no longer exists." });
        if (r.Status is not (LeaveRequest.StatusWaiting or LeaveRequest.StatusApproved))
            return BadRequest(new { message = "Only waiting or approved leave can be changed." });
        var p = await PersonAsync(r.EmployeeId, ct);
        if (p == null) return BadRequest(new { message = "They are no longer in Annual Leave (Admin > Staff)." });
        var length = NormaliseLength(input);
        var (days, _, _, checks) = await AssessAsync(p.E, input.StartDate, input.EndDate, length, r.Id, ct);
        if (checks.FirstOrDefault(c => c.Level == "error") is { } bad) return BadRequest(new { message = bad.Text });

        var moved = r.StartDate != input.StartDate || r.EndDate != input.EndDate || r.DayLength != length;
        var was = moved ? $"{Range(r.StartDate, r.EndDate)}{(r.DayLength < 1 ? $" ({Fmt(r.DayLength)} day)" : "")}" : null;
        r.StartDate = input.StartDate;
        r.EndDate = input.EndDate;
        r.DayLength = length;
        r.Days = days;
        r.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        if (moved)
        {
            // Kept on the request, so an approved holiday that was moved says so.
            var stamp = $"Changed from {was} by {await MyNameAsync(ct)} on {DateTime.Now:d MMM yyyy}.";
            r.DecisionNote = string.IsNullOrWhiteSpace(r.DecisionNote) ? stamp : $"{r.DecisionNote} {stamp}";
            if (r.DecisionNote.Length > 1000) r.DecisionNote = r.DecisionNote[^1000..];
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/annual-leave/requests/{id} — removes a request entered by mistake, whatever its
    // status (it leaves the balance and the calendar). Leave that was real but is no longer being
    // taken should be CANCELLED instead, which keeps the record.
    [HttpDelete("requests/{id:int}")]
    public async Task<IActionResult> DeleteRequest(int id, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var r = await _db.Set<LeaveRequest>().FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (r == null) return NotFound(new { message = "That request no longer exists." });
        r.DateDeleted = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public class RequestInput
    {
        // Preview only: the request being edited, left out of its own checks.
        public int? RequestId { get; set; }
        public int EmployeeId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public decimal DayLength { get; set; } = 1;
        public string? Note { get; set; }
        // Enter it already approved (a signed paper form) rather than waiting.
        public bool Approve { get; set; }
    }

    private static decimal NormaliseLength(RequestInput i) =>
        i.StartDate == i.EndDate && i.DayLength is 0.5m or 0.25m ? i.DayLength : 1;

    // POST /api/annual-leave/requests/preview — what saving this would mean: days, balance, checks.
    [HttpPost("requests/preview")]
    public async Task<ActionResult<object>> Preview([FromBody] RequestInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var emp = (await PersonAsync(input.EmployeeId, ct))?.E;
        if (emp == null) return NotFound(new { message = "Choose an employee." });
        var (days, before, after, checks) = await AssessAsync(emp, input.StartDate, input.EndDate, NormaliseLength(input), input.RequestId, ct);
        return Ok(new { days, remainingBefore = before, remainingAfter = after, checks });
    }

    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest([FromBody] RequestInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var person = await PersonAsync(input.EmployeeId, ct);
        if (person == null) return NotFound(new { message = "Choose an employee." });
        var emp = person.E;
        var length = NormaliseLength(input);
        var (days, _, after, checks) = await AssessAsync(emp, input.StartDate, input.EndDate, length, null, ct);
        if (checks.FirstOrDefault(c => c.Level == "error") is { } bad) return BadRequest(new { message = bad.Text });

        var r = new LeaveRequest
        {
            EmployeeId = emp.Id, StartDate = input.StartDate, EndDate = input.EndDate, DayLength = length, Days = days,
            Status = LeaveRequest.StatusWaiting, Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
            RequestedById = this.GetUserId(), DateCreated = DateTime.Now,
        };
        if (input.Approve)
        {
            r.Status = LeaveRequest.StatusApproved;
            r.DecidedById = this.GetUserId();
            r.DecidedByName = await MyNameAsync(ct);
            r.DateDecided = DateTime.Now;
        }
        _db.Add(r);
        await _db.SaveChangesAsync(ct);
        var emailed = input.Approve && await NotifyAsync(person, r, after, ct);
        return Ok(new { id = r.Id, emailed });
    }

    public class DecideInput
    {
        public bool Approve { get; set; }
        public string? Note { get; set; }
        public bool Email { get; set; } = true;
    }

    // POST /api/annual-leave/requests/{id}/decide — approve or decline a waiting request, and
    // email the employee (when they have an address and email is set up).
    [HttpPost("requests/{id:int}/decide")]
    public async Task<IActionResult> Decide(int id, [FromBody] DecideInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var r = await _db.Set<LeaveRequest>().FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (r == null) return NotFound(new { message = "That request no longer exists." });
        if (r.Status != LeaveRequest.StatusWaiting) return BadRequest(new { message = "That request has already been decided." });
        var person = await PersonAsync(r.EmployeeId, ct);
        if (person == null) return BadRequest(new { message = "They are no longer in Annual Leave (Admin > Staff)." });
        var emp = person.E;
        if (input.Approve)
        {
            var (_, _, _, checks) = await AssessAsync(emp, r.StartDate, r.EndDate, r.DayLength, r.Id, ct);
            if (checks.FirstOrDefault(c => c.Level == "error") is { } bad) return BadRequest(new { message = bad.Text });
        }
        r.Status = input.Approve ? LeaveRequest.StatusApproved : LeaveRequest.StatusDeclined;
        r.DecisionNote = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        r.DecidedById = this.GetUserId();
        r.DecidedByName = await MyNameAsync(ct);
        r.DateDecided = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        var emailed = false;
        if (input.Email)
        {
            var row = (await EmployeeRowsAsync(r.StartDate.Year, ct)).FirstOrDefault(x => x.Id == emp.Id);
            emailed = await NotifyAsync(person, r, row?.Remaining ?? 0, ct);
        }
        return Ok(new { emailed });
    }

    // POST /api/annual-leave/requests/{id}/cancel — takes approved or waiting leave back off.
    [HttpPost("requests/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var r = await _db.Set<LeaveRequest>().FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (r == null) return NotFound(new { message = "That request no longer exists." });
        r.Status = LeaveRequest.StatusCancelled;
        r.DecidedById = this.GetUserId();
        r.DecidedByName = await MyNameAsync(ct);
        r.DateDecided = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // The employee's email: the decision and their balance after it. False (and logged) when
    // there is no address or email is not set up — the decision stands either way.
    private async Task<bool> NotifyAsync(Person emp, LeaveRequest r, decimal remaining, CancellationToken ct)
    {
        if (emp.EmailTo is not { } to) return false;
        var approved = r.Status == LeaveRequest.StatusApproved;
        var what = r.StartDate == r.EndDate
            ? $"{r.StartDate:dddd d MMMM yyyy}{(r.DayLength < 1 ? $" ({(r.DayLength == 0.5m ? "half day" : "quarter day")})" : "")}"
            : $"{r.StartDate:dddd d MMMM} to {r.EndDate:dddd d MMMM yyyy}";
        var body =
            $"Hello {emp.Forename.Trim()},\n\n" +
            $"Your annual leave request for {what} ({Fmt(r.Days)} day{(r.Days == 1 ? "" : "s")}) has been {(approved ? "APPROVED" : "DECLINED")}" +
            $"{(string.IsNullOrWhiteSpace(r.DecidedByName) ? "" : $" by {r.DecidedByName}")}.\n\n" +
            (string.IsNullOrWhiteSpace(r.DecisionNote) ? "" : $"Note: {r.DecisionNote}\n\n") +
            (remaining >= 0
                ? $"You have {Fmt(remaining)} floating day{(remaining == 1 ? "" : "s")} left for {r.StartDate.Year}.\n"
                : $"Your paid leave for {r.StartDate.Year} is used up: {Fmt(-remaining)} day{(remaining == -1 ? "" : "s")} will be unpaid.\n");
        try
        {
            await _email.SendAsync(to, $"Annual leave {(approved ? "approved" : "declined")}: {what}", body, ct);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Annual leave email to {Email} failed", to);
            return false;
        }
    }

    // ---- calendar ---------------------------------------------------------------------------

    // GET /api/annual-leave/calendar?year=2026 — the company days (with their categories) and every
    // approved or waiting request in the year; the page lays them out by month or whole year.
    [HttpGet("calendar")]
    public async Task<ActionResult<object>> Calendar([FromQuery] int? year, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var y = year ?? DateTime.Today.Year;
        var holidays = await HolidayRowsAsync(y, ct);
        var leave = await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.DateDeleted == null && r.StartDate.Year == y
                        && (r.Status == LeaveRequest.StatusApproved || r.Status == LeaveRequest.StatusWaiting))
            .Select(r => new { r.Id, r.EmployeeId, r.StartDate, r.EndDate, r.DayLength, r.Days, r.Status })
            .ToListAsync(ct);
        // Company days people are working instead (shown as a working day on their row).
        var yearStart = new DateOnly(y, 1, 1);
        var yearEnd = new DateOnly(y, 12, 31);
        var worked = await (from w in _db.Set<LeaveHolidayWorked>()
                            join h in _db.Set<LeaveCompanyHoliday>() on w.HolidayId equals h.Id
                            where h.Date >= yearStart && h.Date <= yearEnd
                            select new { w.EmployeeId, h.Date }).AsNoTracking().ToListAsync(ct);
        return Ok(new { holidays, leave, worked });
    }

    // ---- company days worked (2027 policy: work a fixed holiday, get a floating day) ---------

    public record WorkedRow(int Id, int HolidayId, DateOnly Date, string HolidayName, string Reason, string? CreatedByName, DateTime DateCreated);

    // GET /api/annual-leave/employees/{id}/worked?year= — the company days they are working.
    [HttpGet("employees/{id:int}/worked")]
    public async Task<ActionResult<List<WorkedRow>>> Worked(int id, [FromQuery] int year, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var rows = await (from w in _db.Set<LeaveHolidayWorked>()
                          join h in _db.Set<LeaveCompanyHoliday>() on w.HolidayId equals h.Id
                          where w.EmployeeId == id && h.Date.Year == year
                          orderby h.Date
                          select new { w.Id, w.HolidayId, h.Date, h.Name, w.Reason, w.CreatedById, w.DateCreated })
            .AsNoTracking().ToListAsync(ct);
        var names = await UserNamesAsync(rows.Where(r => r.CreatedById != null).Select(r => r.CreatedById!.Value), ct);
        return rows.Select(r => new WorkedRow(r.Id, r.HolidayId, r.Date, r.Name, r.Reason,
            r.CreatedById is { } c ? names.GetValueOrDefault(c) : null, r.DateCreated)).ToList();
    }

    public class WorkedInput
    {
        public int HolidayId { get; set; }
        public string Reason { get; set; } = "";
    }

    // POST /api/annual-leave/employees/{id}/worked — they work this company day; it becomes a
    // floating day for them instead. Refused when it is not one of their company days, when it
    // falls outside their employment, or when they already have leave booked on it.
    [HttpPost("employees/{id:int}/worked")]
    public async Task<IActionResult> AddWorked(int id, [FromBody] WorkedInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Reason)) return BadRequest(new { message = "Give a reason." });
        var emp = (await PersonAsync(id, ct))?.E;
        if (emp == null) return NotFound(new { message = "That employee no longer exists." });
        var h = await _db.Set<LeaveCompanyHoliday>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.HolidayId, ct);
        if (h == null) return NotFound(new { message = "That company day no longer exists." });
        if (!await _db.Set<LeaveCompanyHolidayCategory>().AnyAsync(l => l.HolidayId == h.Id && l.CategoryId == emp.CategoryId, ct))
            return BadRequest(new { message = $"{h.Date:d MMM yyyy} isn't one of their company days." });
        if (LeaveCalculator.EmployedSpan(emp, h.Date.Year) is not { } span || h.Date < span.From || h.Date > span.To)
            return BadRequest(new { message = $"They aren't employed on {h.Date:d MMM yyyy}." });
        if (await _db.Set<LeaveHolidayWorked>().AnyAsync(w => w.EmployeeId == id && w.HolidayId == h.Id, ct))
            return BadRequest(new { message = $"They are already working {h.Date:d MMM yyyy}." });
        _db.Add(new LeaveHolidayWorked
        {
            EmployeeId = id, HolidayId = h.Id, Reason = input.Reason.Trim(),
            CreatedById = this.GetUserId(), DateCreated = DateTime.Now,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/annual-leave/worked/{id} — back to having the company day off.
    [HttpDelete("worked/{id:int}")]
    public async Task<IActionResult> DeleteWorked(int id, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var w = await _db.Set<LeaveHolidayWorked>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w == null) return NotFound();
        _db.Remove(w);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- outstanding leave (2027 policy: unallocated on 31 August, confirm by 15 September) --

    public record OutstandingRow(
        int EmployeeId, string Name, string CategoryName, decimal Floating, decimal Used, decimal Waiting,
        decimal Unbooked, string? EmailTo, DateTime? LastReminded, decimal? LastRemindedDays);

    // GET /api/annual-leave/outstanding?year= — everyone with floating days neither approved nor
    // asked for, most unbooked first, with the last reminder they were sent.
    [HttpGet("outstanding")]
    public async Task<ActionResult<List<OutstandingRow>>> Outstanding([FromQuery] int? year, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var y = year ?? DateTime.Today.Year;
        var rows = (await EmployeeRowsAsync(y, ct)).Where(r => r.Unbooked > 0).ToList();
        var ids = rows.Select(r => r.Id).ToList();
        var last = (await _db.Set<LeaveReminder>().AsNoTracking()
                .Where(r => r.Year == y && ids.Contains(r.EmployeeId)).ToListAsync(ct))
            .GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.DateSent).First());
        return rows.OrderByDescending(r => r.Unbooked).ThenBy(r => r.Surname).Select(r =>
        {
            var l = last.GetValueOrDefault(r.Id);
            return new OutstandingRow(r.Id, $"{r.Forename} {r.Surname}".Trim(), r.CategoryName, r.Floating, r.Used, r.Waiting,
                r.Unbooked, r.EmailTo, l?.DateSent, l?.Days);
        }).ToList();
    }

    public class ReminderInput
    {
        public int Year { get; set; }
        public List<int> EmployeeIds { get; set; } = new();
    }

    // POST /api/annual-leave/reminders — email each chosen employee their unbooked floating days
    // and the policy's 15 September deadline. Those with no email address are skipped and named.
    [HttpPost("reminders")]
    public async Task<ActionResult<object>> SendReminders([FromBody] ReminderInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var rows = (await EmployeeRowsAsync(input.Year, ct)).Where(r => input.EmployeeIds.Contains(r.Id)).ToList();
        var deadline = new DateOnly(input.Year, 9, 15);
        int sent = 0;
        var skipped = new List<string>();
        var failed = new List<string>();
        foreach (var r in rows)
        {
            var name = $"{r.Forename} {r.Surname}".Trim();
            if (r.Unbooked <= 0) continue;
            if (r.EmailTo is not { } to) { skipped.Add(name); continue; }
            var body =
                $"Hello {r.Forename},\n\n" +
                $"You have {Fmt(r.Unbooked)} floating day{(r.Unbooked == 1 ? "" : "s")} of {input.Year} annual leave not yet booked.\n\n" +
                $"Under the annual leave policy, please confirm by {deadline:dddd d MMMM yyyy} when you intend to take " +
                $"{(r.Unbooked == 1 ? "it" : "them")}. Otherwise the company may allocate outstanding leave with one month's notice.\n\n" +
                "Leave is not normally carried forward into the next year.\n";
            try
            {
                await _email.SendAsync(to, $"Annual leave: {Fmt(r.Unbooked)} day{(r.Unbooked == 1 ? "" : "s")} of {input.Year} still to book", body, ct);
                _db.Add(new LeaveReminder
                {
                    EmployeeId = r.Id, Year = input.Year, Days = r.Unbooked, SentTo = to,
                    SentById = this.GetUserId(), DateSent = DateTime.Now,
                });
                sent++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Annual leave reminder to {Email} failed", to);
                failed.Add(name);
            }
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { sent, skipped, failed });
    }

    // ---- company days -----------------------------------------------------------------------

    public record HolidayRow(int Id, DateOnly Date, string Name, List<int> CategoryIds);

    private async Task<List<HolidayRow>> HolidayRowsAsync(int year, CancellationToken ct)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var hols = await _db.Set<LeaveCompanyHoliday>().AsNoTracking()
            .Where(h => h.Date >= from && h.Date <= to).OrderBy(h => h.Date).ToListAsync(ct);
        var ids = hols.Select(h => h.Id).ToList();
        var links = (await _db.Set<LeaveCompanyHolidayCategory>().AsNoTracking()
            .Where(l => ids.Contains(l.HolidayId)).ToListAsync(ct)).ToLookup(l => l.HolidayId, l => l.CategoryId);
        return hols.Select(h => new HolidayRow(h.Id, h.Date, h.Name, links[h.Id].OrderBy(x => x).ToList())).ToList();
    }

    [HttpGet("holidays")]
    public async Task<ActionResult<List<HolidayRow>>> Holidays([FromQuery] int year, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        return await HolidayRowsAsync(year, ct);
    }

    public class HolidayInput
    {
        public DateOnly Date { get; set; }
        public string Name { get; set; } = "";
        public List<int> CategoryIds { get; set; } = new();
    }

    [HttpPost("holidays")]
    public async Task<IActionResult> CreateHoliday([FromBody] HolidayInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { message = "Give the day a name." });
        if (await _db.Set<LeaveCompanyHoliday>().AnyAsync(h => h.Date == input.Date, ct))
            return BadRequest(new { message = $"{input.Date:d MMM yyyy} is already a company day." });
        var h = new LeaveCompanyHoliday { Date = input.Date, Name = input.Name.Trim(), DateCreated = DateTime.Now };
        _db.Add(h);
        await _db.SaveChangesAsync(ct);
        await SetHolidayCategoriesAsync(h.Id, input.CategoryIds, ct);
        return Ok(new { id = h.Id });
    }

    [HttpPut("holidays/{id:int}")]
    public async Task<IActionResult> UpdateHoliday(int id, [FromBody] HolidayInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var h = await _db.Set<LeaveCompanyHoliday>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h == null) return NotFound(new { message = "That company day no longer exists." });
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { message = "Give the day a name." });
        if (await _db.Set<LeaveCompanyHoliday>().AnyAsync(x => x.Date == input.Date && x.Id != id, ct))
            return BadRequest(new { message = $"{input.Date:d MMM yyyy} is already a company day." });
        h.Date = input.Date;
        h.Name = input.Name.Trim();
        await _db.SaveChangesAsync(ct);
        await SetHolidayCategoriesAsync(h.Id, input.CategoryIds, ct);
        return NoContent();
    }

    private async Task SetHolidayCategoriesAsync(int holidayId, List<int> categoryIds, CancellationToken ct)
    {
        var existing = await _db.Set<LeaveCompanyHolidayCategory>().Where(l => l.HolidayId == holidayId).ToListAsync(ct);
        _db.RemoveRange(existing);
        foreach (var c in categoryIds.Distinct())
            _db.Add(new LeaveCompanyHolidayCategory { HolidayId = holidayId, CategoryId = c });
        await _db.SaveChangesAsync(ct);
    }

    [HttpDelete("holidays/{id:int}")]
    public async Task<IActionResult> DeleteHoliday(int id, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var h = await _db.Set<LeaveCompanyHoliday>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h == null) return NotFound();
        _db.Remove(h);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/annual-leave/holidays/copy {fromYear, toYear} — starts a year from the last one:
    // every company day carried to the same day and month, names and categories kept. Moving
    // holidays (Easter, bank-holiday Mondays) land on the wrong date, so the page flags weekend
    // dates and asks for the moving ones to be checked.
    public class CopyInput
    {
        public int FromYear { get; set; }
        public int ToYear { get; set; }
    }

    [HttpPost("holidays/copy")]
    public async Task<IActionResult> CopyHolidays([FromBody] CopyInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var target = await HolidayRowsAsync(input.ToYear, ct);
        if (target.Count > 0) return BadRequest(new { message = $"{input.ToYear} already has company days." });
        var source = await HolidayRowsAsync(input.FromYear, ct);
        if (source.Count == 0) return BadRequest(new { message = $"{input.FromYear} has no company days to copy." });
        foreach (var s in source)
        {
            if (s.Date.Month == 2 && s.Date.Day == 29) continue;
            var h = new LeaveCompanyHoliday { Date = new DateOnly(input.ToYear, s.Date.Month, s.Date.Day), Name = s.Name, DateCreated = DateTime.Now };
            _db.Add(h);
            await _db.SaveChangesAsync(ct);
            foreach (var c in s.CategoryIds) _db.Add(new LeaveCompanyHolidayCategory { HolidayId = h.Id, CategoryId = c });
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- blocked periods --------------------------------------------------------------------

    public record BlockedRow(int Id, string Name, DateOnly StartDate, DateOnly EndDate, int? CategoryId, decimal? MaxDays);

    [HttpGet("blocked")]
    public async Task<ActionResult<List<BlockedRow>>> Blocked([FromQuery] int year, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        return await _db.Set<LeaveBlockedPeriod>().AsNoTracking()
            .Where(p => p.StartDate <= to && p.EndDate >= from).OrderBy(p => p.StartDate)
            .Select(p => new BlockedRow(p.Id, p.Name, p.StartDate, p.EndDate, p.CategoryId, p.MaxDays))
            .ToListAsync(ct);
    }

    public class BlockedInput
    {
        public string Name { get; set; } = "";
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int? CategoryId { get; set; }
        public decimal? MaxDays { get; set; }
    }

    private static string? Validate(BlockedInput i) =>
        string.IsNullOrWhiteSpace(i.Name) ? "Give the period a name."
        : i.EndDate < i.StartDate ? "The end is before the start."
        : i.MaxDays is < 0 ? "The day limit can't be negative."
        : null;

    [HttpPost("blocked")]
    public async Task<IActionResult> CreateBlocked([FromBody] BlockedInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        if (Validate(input) is { } err) return BadRequest(new { message = err });
        var p = new LeaveBlockedPeriod { DateCreated = DateTime.Now };
        Apply(p, input);
        _db.Add(p);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = p.Id });
    }

    [HttpPut("blocked/{id:int}")]
    public async Task<IActionResult> UpdateBlocked(int id, [FromBody] BlockedInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var p = await _db.Set<LeaveBlockedPeriod>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound(new { message = "That period no longer exists." });
        if (Validate(input) is { } err) return BadRequest(new { message = err });
        Apply(p, input);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(LeaveBlockedPeriod p, BlockedInput i)
    {
        p.Name = i.Name.Trim();
        p.StartDate = i.StartDate;
        p.EndDate = i.EndDate;
        p.CategoryId = i.CategoryId;
        p.MaxDays = i.MaxDays;
    }

    [HttpDelete("blocked/{id:int}")]
    public async Task<IActionResult> DeleteBlocked(int id, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var p = await _db.Set<LeaveBlockedPeriod>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();
        _db.Remove(p);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- categories -------------------------------------------------------------------------

    public record CategoryRow(int Id, string Name, decimal AnnualDays, int SortOrder, int Employees, int SetDays);

    // GET /api/annual-leave/categories?year= — with how many staff are in each, and the set days
    // the year gives a full-year employee (so Floating = AnnualDays - SetDays reads off the row).
    [HttpGet("categories")]
    public async Task<ActionResult<List<CategoryRow>>> Categories([FromQuery] int? year, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var y = year ?? DateTime.Today.Year;
        var hols = await HolidaysByCategoryAsync(ct);
        var counts = (await People().Select(p => p.E.CategoryId).ToListAsync(ct))
            .GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        var cats = await _db.Set<LeaveCategory>().AsNoTracking().Where(c => c.DateDeleted == null)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        return cats.Select(c => new CategoryRow(c.Id, c.Name, c.AnnualDays, c.SortOrder, counts.GetValueOrDefault(c.Id),
            For(hols, c.Id).Count(d => d.Year == y && LeaveCalculator.IsWorkingDay(d)))).ToList();
    }

    public class CategoryInput
    {
        public string Name { get; set; } = "";
        public decimal AnnualDays { get; set; }
        public int SortOrder { get; set; }
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { message = "Give the category a name." });
        if (input.AnnualDays is <= 0 or > 366) return BadRequest(new { message = "Enter the days a year." });
        var c = new LeaveCategory { Name = input.Name.Trim(), AnnualDays = input.AnnualDays, SortOrder = input.SortOrder, DateCreated = DateTime.Now };
        _db.Add(c);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = c.Id });
    }

    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryInput input, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var c = await _db.Set<LeaveCategory>().FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (c == null) return NotFound(new { message = "That category no longer exists." });
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { message = "Give the category a name." });
        if (input.AnnualDays is <= 0 or > 366) return BadRequest(new { message = "Enter the days a year." });
        c.Name = input.Name.Trim();
        c.AnnualDays = input.AnnualDays;
        c.SortOrder = input.SortOrder;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct)) return Forbid();
        var c = await _db.Set<LeaveCategory>().FirstOrDefaultAsync(x => x.Id == id && x.DateDeleted == null, ct);
        if (c == null) return NotFound();
        if (await _db.Set<LeaveEmployee>().AnyAsync(e => e.CategoryId == id && e.DateDeleted == null, ct))
            return BadRequest(new { message = "Staff are still in this category — move them first." });
        c.DateDeleted = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
