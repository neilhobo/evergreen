using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// The Reports menu. Sage Financial Reports: saved reports (SageReport), each a member-sales
// report (Services/MemberSalesReport.cs) built from the Sage sales mirror over its own named
// account groups (ReportMemberGroup) — the Allied Merchant Buying Group under "AlliedMerchant"
// first. Reports and their groups are data, created and edited from the page, so a new report,
// member or branch needs no release.
[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public ReportsController(GbEvergreenContext db) => _db = db;

    // GET /api/reports/sage — the saved reports, in order, with how many members and accounts.
    [HttpGet("sage")]
    public async Task<ActionResult<IEnumerable<SageReportDto>>> List(CancellationToken ct = default)
    {
        var reports = await _db.Set<SageReport>().AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync(ct);
        var groups = await _db.Set<ReportMemberGroup>().AsNoTracking().ToListAsync(ct);
        return reports.Select(r =>
        {
            var mine = groups.Where(g => g.ReportKey == r.ReportKey).ToList();
            return new SageReportDto(r.ReportKey, r.Name, mine.Count, mine.Sum(g => g.Accounts.Count()));
        }).ToList();
    }

    // POST /api/reports/sage — a new report: its name and groups together.
    [HttpPost("sage")]
    public async Task<ActionResult<SageReportDto>> Create([FromBody] SageReportInput input, CancellationToken ct = default)
    {
        var (name, groups, error) = Validate(input);
        if (error != null) return BadRequest(new { message = error });
        if (await _db.Set<SageReport>().AnyAsync(r => r.Name == name, ct))
            return BadRequest(new { message = $"There is already a report called '{name}'." });

        var report = new SageReport
        {
            ReportKey = "R" + Guid.NewGuid().ToString("N")[..12],
            Name = name,
            SortOrder = (await _db.Set<SageReport>().MaxAsync(r => (int?)r.SortOrder, ct) ?? 0) + 1,
            CreatedUtc = DateTime.UtcNow,
            CreatedBy = User.FindFirst("name")?.Value,
        };
        _db.Add(report);
        await SaveGroups(report.ReportKey, groups, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(report, groups);
    }

    // PUT /api/reports/sage/{reportKey} — rename and/or replace the groups: existing ids are
    // updated, new rows (id 0) added, rows left out deleted. Order is the order sent.
    [HttpPut("sage/{reportKey}")]
    public async Task<ActionResult<SageReportDto>> Update(string reportKey, [FromBody] SageReportInput input, CancellationToken ct = default)
    {
        var report = await _db.Set<SageReport>().FirstOrDefaultAsync(r => r.ReportKey == reportKey, ct);
        if (report == null) return NotFound(new { message = "That report no longer exists." });
        var (name, groups, error) = Validate(input);
        if (error != null) return BadRequest(new { message = error });
        if (await _db.Set<SageReport>().AnyAsync(r => r.Name == name && r.Id != report.Id, ct))
            return BadRequest(new { message = $"There is already a report called '{name}'." });

        report.Name = name;
        await SaveGroups(reportKey, groups, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(report, groups);
    }

    // DELETE /api/reports/sage/{reportKey} — the report and its groups. The sales mirror is untouched.
    [HttpDelete("sage/{reportKey}")]
    public async Task<IActionResult> Delete(string reportKey, CancellationToken ct = default)
    {
        var report = await _db.Set<SageReport>().FirstOrDefaultAsync(r => r.ReportKey == reportKey, ct);
        if (report == null) return NoContent();
        _db.RemoveRange(await _db.Set<ReportMemberGroup>().Where(g => g.ReportKey == reportKey).ToListAsync(ct));
        _db.Remove(report);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /api/reports/member-sales/{reportKey}?from=2026-01-01&to=2026-10-08 — the whole
    // report for the window (dates inclusive; defaults to this year to date).
    [HttpGet("member-sales/{reportKey}")]
    public async Task<ActionResult<MemberSalesReport.Report>> MemberSales(
        string reportKey, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
    {
        var start = from?.Date ?? new DateTime(DateTime.Today.Year, 1, 1);
        var finish = to?.Date ?? DateTime.Today;
        if (finish < start) return BadRequest(new { message = "'to' is before 'from'." });
        if ((finish - start).TotalDays > 366 * 5) return BadRequest(new { message = "A window of more than five years is not supported." });
        return await MemberSalesReport.BuildAsync(_db, reportKey, start, finish, ct);
    }

    // GET /api/reports/member-sales/{reportKey}/groups — the account groups, in order.
    [HttpGet("member-sales/{reportKey}/groups")]
    public async Task<ActionResult<IEnumerable<MemberGroupDto>>> Groups(string reportKey, CancellationToken ct = default) =>
        (await _db.Set<ReportMemberGroup>().AsNoTracking()
            .Where(g => g.ReportKey == reportKey)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .ToListAsync(ct))
        .Select(g => new MemberGroupDto(g.Id, g.Name, g.AccountRefs, g.SortOrder)).ToList();

    // A report's name and groups, cleaned: names trimmed, refs upper-cased and de-duplicated,
    // an account in one group only.
    private static (string Name, List<(int Id, string Name, List<string> Accounts)> Groups, string? Error) Validate(SageReportInput input)
    {
        var name = (input.Name ?? "").Trim();
        var clean = new List<(int Id, string Name, List<string> Accounts)>();
        if (name.Length == 0) return (name, clean, "The report needs a name.");
        if (name.Length > 80) return (name, clean, "The report name is too long (80 characters at most).");
        var groups = input.Groups ?? [];
        if (groups.Count == 0) return (name, clean, "Add at least one member.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var member = (g.Name ?? "").Trim();
            if (member.Length == 0) return (name, clean, "Every member needs a name.");
            if (member.Length > 60) return (name, clean, $"'{member}' is too long (60 characters at most).");
            var accounts = (g.AccountRefs ?? "")
                .Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => a.ToUpperInvariant())
                .Distinct()
                .ToList();
            if (accounts.Count == 0) return (name, clean, $"'{member}' has no Sage account.");
            foreach (var a in accounts)
            {
                if (a.Length > 8) return (name, clean, $"'{a}' is not a Sage account reference (8 characters at most).");
                if (!seen.Add(a)) return (name, clean, $"{a} is listed under two members.");
            }
            if (string.Join(",", accounts).Length > 400) return (name, clean, $"'{member}' has too many accounts.");
            if (clean.Any(c => c.Name.Equals(member, StringComparison.OrdinalIgnoreCase)))
                return (name, clean, $"'{member}' is listed twice.");
            clean.Add((g.Id, member, accounts));
        }
        return (name, clean, null);
    }

    // Stages the groups for a report (the caller saves).
    private async Task SaveGroups(string reportKey, List<(int Id, string Name, List<string> Accounts)> groups, CancellationToken ct)
    {
        var existing = await _db.Set<ReportMemberGroup>().Where(g => g.ReportKey == reportKey).ToListAsync(ct);
        var keep = new HashSet<int>();
        var order = 0;
        foreach (var c in groups)
        {
            order++;
            var row = c.Id > 0 ? existing.FirstOrDefault(e => e.Id == c.Id) : null;
            if (row == null)
            {
                row = new ReportMemberGroup { ReportKey = reportKey };
                _db.Add(row);
            }
            row.Name = c.Name;
            row.AccountRefs = string.Join(",", c.Accounts);
            row.SortOrder = order;
            if (row.Id > 0) keep.Add(row.Id);
        }
        foreach (var gone in existing.Where(e => !keep.Contains(e.Id)))
            _db.Remove(gone);
    }

    private static SageReportDto ToDto(SageReport r, List<(int Id, string Name, List<string> Accounts)> groups) =>
        new(r.ReportKey, r.Name, groups.Count, groups.Sum(g => g.Accounts.Count));
}

public record SageReportDto(string Key, string Name, int MemberCount, int AccountCount);

public record MemberGroupDto(int Id, string Name, string AccountRefs, int SortOrder);

public class SageReportInput
{
    public string? Name { get; set; }
    public List<MemberGroupInput>? Groups { get; set; }
}

public class MemberGroupInput
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? AccountRefs { get; set; }
}
