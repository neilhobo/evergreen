using System.Net;
using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Oil Tanks (TauriAdmin > Oil Tanks): the site's Specgauge oil-tank sensors, read from the
// Specgauge API (client Specgauge, depot Evergreen) and handed to the page as they come.
//
// A proxy rather than the page calling Specgauge itself: the Specgauge key is a per-client
// secret that unlocks every one of that client's devices, so it stays in this server's
// appsettings ("Specgauge" section) and never reaches a desktop.
//
// Who sees it is chosen user by user in Super Admin > Oil Tanks (GlobeweighUser.OilTanksAccess);
// the super-admin always does. The readings are refused to anyone else, not just hidden.
[ApiController]
[Authorize]
[Route("api/oil-tanks")]
public class OilTanksController : ControllerBase
{
    private readonly GbEvergreenContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OilTanksController> _log;

    public OilTanksController(GbEvergreenContext db, IConfiguration config, IHttpClientFactory httpFactory, ILogger<OilTanksController> log)
    {
        _db = db;
        _config = config;
        _httpFactory = httpFactory;
        _log = log;
    }

    // GET /api/oil-tanks?days=14 — Specgauge's GET api/public/depots/{depot}/tanks: each tank's
    // live reading, shape and specs, the last `days` of readings and the last fill.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 14, CancellationToken ct = default)
    {
        if (!await CanViewAsync(ct)) return Forbid();

        var baseUrl = _config["Specgauge:BaseUrl"]?.TrimEnd('/');
        var key = _config["Specgauge:ApiKey"];
        var depot = _config["Specgauge:Depot"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(depot))
            return Problem("Specgauge is not set up on this server (appsettings: Specgauge:BaseUrl, ApiKey, Depot).",
                statusCode: (int)HttpStatusCode.ServiceUnavailable);

        var url = $"{baseUrl}/api/public/depots/{Uri.EscapeDataString(depot)}/tanks?days={Math.Clamp(days, 1, 60)}";
        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(20);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Api-Key", key);

        try
        {
            using var res = await http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("Specgauge tanks call failed: {Status} {Body}", (int)res.StatusCode, body);
                return Problem($"Specgauge answered {(int)res.StatusCode}.", statusCode: (int)HttpStatusCode.BadGateway);
            }
            return Content(body, "application/json");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "Specgauge tanks call failed");
            return Problem("Specgauge could not be reached.", statusCode: (int)HttpStatusCode.BadGateway);
        }
    }

    // eUserType.Touch — Touch-only users never sign in to the admin app, so are not listed.
    private const int UserTypeTouch = 2;

    // Whether the signed-in user may see the page: switched on for them, or the super-admin.
    private async Task<bool> CanViewAsync(CancellationToken ct)
    {
        if (await this.IsSuperAdminAsync(_db, ct)) return true;
        var id = this.GetUserId();
        return await _db.GlobeweighUsers.AsNoTracking()
            .AnyAsync(u => u.Id == id && u.DateDeleted == null && u.OilTanksAccess, ct);
    }

    // GET /api/oil-tanks/access/me — whether the menu should offer the page to this user.
    [HttpGet("access/me")]
    public async Task<ActionResult<OilTanksAccessMe>> AccessMe(CancellationToken ct = default) =>
        new OilTanksAccessMe { CanView = await CanViewAsync(ct) };

    // GET /api/oil-tanks/access — every admin-app user and whether they see the page.
    // Super-admin only: it is the Super Admin > Oil Tanks list.
    [HttpGet("access")]
    public async Task<ActionResult<IEnumerable<OilTanksAccessRow>>> Access(CancellationToken ct = default)
    {
        if (!await this.IsSuperAdminAsync(_db, ct)) return Forbid();
        return Ok(await AccessRowsAsync(ct));
    }

    // PUT /api/oil-tanks/access/{userId} — switch one user on or off; returns the whole list.
    [HttpPut("access/{userId:int}")]
    public async Task<ActionResult<IEnumerable<OilTanksAccessRow>>> SetAccess(
        int userId, [FromBody] OilTanksAccessInput input, CancellationToken ct = default)
    {
        if (!await this.IsSuperAdminAsync(_db, ct)) return Forbid();
        var user = await _db.GlobeweighUsers.FirstOrDefaultAsync(u => u.Id == userId && u.DateDeleted == null, ct);
        if (user == null) return NotFound(new { message = "That user no longer exists." });
        user.OilTanksAccess = input.Enabled;
        await _db.SaveChangesAsync(ct);
        return Ok(await AccessRowsAsync(ct));
    }

    // Live, non-Touch users (not the built-in GlobeweighAdmin, id 1), by name.
    private async Task<List<OilTanksAccessRow>> AccessRowsAsync(CancellationToken ct) =>
        await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.DateDeleted == null && u.Id != 1 && u.UserTypeId != UserTypeTouch)
            .OrderBy(u => u.FirstName).ThenBy(u => u.Surname)
            .Select(u => new OilTanksAccessRow
            {
                Id        = u.Id,
                FirstName = u.FirstName,
                Surname   = u.Surname,
                Username  = u.Username,
                Disabled  = u.Disabled,
                CanView   = u.OilTanksAccess,
            })
            .ToListAsync(ct);
}

public class OilTanksAccessMe
{
    public bool CanView { get; set; }
}

public class OilTanksAccessRow
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? Surname { get; set; }
    public string? Username { get; set; }
    public bool Disabled { get; set; }
    public bool CanView { get; set; }
}

public class OilTanksAccessInput
{
    public bool Enabled { get; set; }
}
