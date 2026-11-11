using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Super Admin > Login History: who signed in to TauriAdmin or the mobile app, when, from which
// app and from where, the failed attempts, and every Switch user (dbo.LoginEvent, written by
// Services/LoginLog). The super-admin only.
[ApiController]
[Authorize]
[Route("api/login-history")]
public class LoginHistoryController : ControllerBase
{
    private readonly GbEvergreenContext _db;
    public LoginHistoryController(GbEvergreenContext db) => _db = db;

    // GET /api/login-history?days=30 — newest first, at most 2,000 rows.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LoginEventRow>>> List([FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (!await this.IsSuperAdminAsync(_db, ct)) return Forbid();
        days = Math.Clamp(days, 1, 366);
        var since = DateTime.Now.AddDays(-days);
        var rows = await _db.Set<LoginEvent>().AsNoTracking()
            .Where(e => e.DateLogged >= since)
            .OrderByDescending(e => e.DateLogged).ThenByDescending(e => e.Id)
            .Take(2000)
            .ToListAsync(ct);

        var ids = rows.Select(r => r.UserId).Concat(rows.Select(r => r.ByUserId)).OfType<int>().Distinct().ToList();
        var names = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.Surname}".Trim(), ct);
        string? Name(int? id) => id is { } i && names.TryGetValue(i, out var n) && n.Length > 0 ? n : null;

        return rows.Select(r => new LoginEventRow
        {
            Id = r.Id,
            Date = r.DateLogged,
            Kind = r.Kind,
            UserId = r.UserId,
            FullName = Name(r.UserId),
            Username = r.Username,
            ByName = Name(r.ByUserId),
            Detail = r.Detail,
            Client = r.Client,
            Via = r.Via,
            IpAddress = r.IpAddress,
            UserAgent = r.UserAgent,
        }).ToList();
    }
}

public class LoginEventRow
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Kind { get; set; } = "";
    public int? UserId { get; set; }
    public string? FullName { get; set; }   // the account's full name
    public string? Username { get; set; }   // as typed / the account's login
    public string? ByName { get; set; }     // Switch user: the admin
    public string? Detail { get; set; }
    public string? Client { get; set; }
    public string? Via { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
