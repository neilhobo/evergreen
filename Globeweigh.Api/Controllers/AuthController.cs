using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly GbEvergreenContext _db;
    private readonly JwtTokenService _jwt;

    public AuthController(GbEvergreenContext db, JwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    // eUserType.Touch / eUserType.Both — the user types that appear on the Touch login
    // picker and may sign in with a passcode.
    private static readonly int[] TouchUserTypes = { 2, 3 };

    /// <summary>
    /// Desktop Admin login by Username + Password. Mirrors the existing desktop
    /// GlobeweighUserRepository behaviour: case-insensitive username, a blank stored
    /// password matches a blank entry, otherwise an OrdinalIgnoreCase compare. Plaintext.
    /// </summary>
    // Public-internet facing (Cloudflare tunnel, no Access policy) — throttled per client IP.
    [EnableRateLimiting("login")]
    [HttpPost("admin-login")]
    public async Task<ActionResult<AdminUserDto>> AdminLogin([FromBody] AdminLoginRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Username))
            return Unauthorized(new { message = "Invalid details entered" });

        var username = req.Username.Trim();
        var user = await _db.GlobeweighUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.DateDeleted == null
                && u.Username != null
                && u.Username.ToLower() == username.ToLower(), ct);

        if (user == null)
            return Unauthorized(new { message = "Invalid details entered" });

        var password = req.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(user.Password))
        {
            if (!string.IsNullOrWhiteSpace(password))
                return Unauthorized(new { message = "Invalid details entered" });
        }
        else if (!string.Equals(user.Password, password, StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(new { message = "Invalid details entered" });
        }

        return await SessionForAsync(user, null, ct);
    }

    // ---- Switch user (admins only) ----
    // An admin signs in AS another user, no password, to see the app as they do. The
    // admin's id rides in the new token (Jwt.cs ImpersonatedByClaim), which is all
    // "Return to my account" trusts. No switching from inside a switch.

    /// <summary>Everyone the signed-in admin may switch into.</summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet("switch-users")]
    public async Task<ActionResult<IEnumerable<SwitchUserRow>>> SwitchUsers(CancellationToken ct = default)
    {
        var me = await SwitchingAdminAsync(ct);
        if (me == null) return Forbid();

        var users = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.DateDeleted == null && u.Id != me.Value.User.Id)
            .ToListAsync(ct);
        var roles = await RoleIdsByUserAsync(users.Select(u => u.Id).ToList(), ct);

        return users
            .Select(u => new SwitchUserRow
            {
                Id = u.Id,
                FirstName = u.FirstName,
                Surname = u.Surname,
                Username = u.Username,
                RoleIds = EffectiveRoles(u, roles),
                Disabled = u.Disabled
            })
            .Where(r => MaySwitchInto(me.Value.IsGlobeweighAdmin, r.Username, r.RoleIds))
            .OrderBy(r => $"{r.FirstName} {r.Surname}".Trim() is { Length: > 0 } n ? n : r.Username,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Sign in as another user. The returned session replaces the admin's.</summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("switch-user")]
    public async Task<ActionResult<AdminUserDto>> SwitchUser([FromBody] SwitchUserRequest req, CancellationToken ct = default)
    {
        var me = await SwitchingAdminAsync(ct);
        if (me == null) return Forbid();

        var target = await _db.GlobeweighUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == req.TargetUserId && u.DateDeleted == null, ct);
        if (target == null || target.Id == me.Value.User.Id)
            return NotFound(new { message = "User not found." });
        if (target.Disabled)
            return BadRequest(new { message = "That account is disabled." });

        var targetRoles = EffectiveRoles(target, await RoleIdsByUserAsync(new List<int> { target.Id }, ct));
        if (!MaySwitchInto(me.Value.IsGlobeweighAdmin, target.Username, targetRoles))
            return Forbid();

        return await SessionForAsync(target, me.Value.User, ct);
    }

    /// <summary>Back to the admin's own session, read from the signed token.</summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("switch-back")]
    public async Task<ActionResult<AdminUserDto>> SwitchBack(CancellationToken ct = default)
    {
        var adminId = User.GetImpersonatedBy();
        if (adminId == null) return BadRequest(new { message = "Not switched to another user." });

        var admin = await _db.GlobeweighUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == adminId && u.DateDeleted == null && !u.Disabled, ct);
        if (admin == null) return Unauthorized();

        return await SessionForAsync(admin, null, ct);
    }

    // The signed-in user if they may use Switch user: an admin (Globeweigh or Office), on
    // their own session.
    private async Task<(GlobeweighUser User, bool IsGlobeweighAdmin)?> SwitchingAdminAsync(CancellationToken ct)
    {
        if (User.GetImpersonatedBy() != null) return null;
        var id = User.GetUserId();
        var me = await _db.GlobeweighUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.DateDeleted == null && !u.Disabled, ct);
        if (me == null) return null;
        var roles = EffectiveRoles(me, await RoleIdsByUserAsync(new List<int> { me.Id }, ct));
        if (!roles.Contains(RoleGlobeweighAdmin) && !roles.Contains(RoleOfficeAdmin)) return null;
        return (me, roles.Contains(RoleGlobeweighAdmin));
    }

    // No way up through a switch: nobody but the super-admin can become the super-admin,
    // and an Office Admin cannot become a Globeweigh Admin.
    private static bool MaySwitchInto(bool callerIsGlobeweighAdmin, string? targetUsername, int[] targetRoles)
    {
        if (string.Equals(targetUsername?.Trim(), UserExtensions.SuperAdminUsername, StringComparison.OrdinalIgnoreCase))
            return false;
        return callerIsGlobeweighAdmin || !targetRoles.Contains(RoleGlobeweighAdmin);
    }

    // eUserRole.GlobeweighAdmin / eUserRole.OfficeAdmin.
    private const int RoleGlobeweighAdmin = 1;
    private const int RoleOfficeAdmin = 2;

    private async Task<ILookup<int, int>> RoleIdsByUserAsync(List<int> userIds, CancellationToken ct) =>
        (await _db.Set<Globeweigh.Api.Data.Entities.UserRoleEntity>().AsNoTracking()
            .Where(r => userIds.Contains(r.UserId))
            .Select(r => new { r.UserId, r.RoleId })
            .ToListAsync(ct))
        .ToLookup(r => r.UserId, r => r.RoleId);

    // Effective roles come from the Globeweigh_Role junction (the desktop app's source of
    // truth); the single RoleId column can be 0. Fold it in if set.
    private static int[] EffectiveRoles(GlobeweighUser user, ILookup<int, int> roles)
    {
        var ids = roles[user.Id].ToList();
        if (user.RoleId > 0) ids.Add(user.RoleId);
        return ids.Distinct().OrderBy(r => r).ToArray();
    }

    // A fresh token + the session DTO for <paramref name="user"/>; with
    // <paramref name="impersonatedBy"/> set, it is that admin acting as them.
    private async Task<AdminUserDto> SessionForAsync(GlobeweighUser user, GlobeweighUser? impersonatedBy, CancellationToken ct)
    {
        var fullName = $"{user.FirstName} {user.Surname}".Trim();
        (int Id, string Name)? by = impersonatedBy == null
            ? null
            : (impersonatedBy.Id, $"{impersonatedBy.FirstName} {impersonatedBy.Surname}".Trim() is { Length: > 0 } n
                ? n : impersonatedBy.Username?.Trim() ?? "");
        var (token, exp) = _jwt.Create(user.Id, fullName, by);

        return new AdminUserDto
        {
            Id                   = user.Id,
            UserTypeId           = user.UserTypeId,
            FirstName            = user.FirstName,
            Surname              = user.Surname,
            Username             = user.Username,
            RoleId               = user.RoleId,
            RoleIds              = EffectiveRoles(user, await RoleIdsByUserAsync(new List<int> { user.Id }, ct)),
            CostingAccess        = user.CostingAccess,
            Disabled             = user.Disabled,
            ImpersonatedByUserId = by?.Id,
            ImpersonatedByName   = by?.Name,
            Token                = token,
            ExpiresUtc           = exp
        };
    }

    /// <summary>
    /// Self-service password / Touch-passcode change for the signed-in user.
    /// Mirrors the desktop "My Account" screen. Plaintext, matching existing data.
    /// </summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    [EnableRateLimiting("login")]
    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct = default)
    {
        var sub = User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var userId))
            return Unauthorized();
        // A switched-to session is someone else's account — not theirs to re-password.
        if (User.GetImpersonatedBy() != null)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Passwords can't be changed while switched to another user." });

        var user = await _db.GlobeweighUsers.FirstOrDefaultAsync(u => u.Id == userId && u.DateDeleted == null, ct);
        if (user == null) return Unauthorized();

        var newPassword = req.NewPassword ?? string.Empty;
        if (newPassword.Length > 0)
        {
            if (newPassword.Contains(' '))
                return BadRequest(new { message = "Password cannot contain spaces." });
            var current = user.Password ?? string.Empty;
            var supplied = req.OldPassword ?? string.Empty;
            if (!string.Equals(current, supplied, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Old password is incorrect." });
            user.Password = newPassword;
        }

        if (!string.IsNullOrEmpty(req.Passcode))
        {
            if (req.Passcode.Length != 4 || !req.Passcode.All(char.IsDigit))
                return BadRequest(new { message = "Passcode must be 4 digits." });
            user.Passcode = req.Passcode;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { password = user.Password, passcode = user.Passcode });
    }

    /// <summary>
    /// Touch login user picker (TauriTouch and Globeweigh.Picking). Membership is the
    /// user's type — Touch or Both — not a role: the old "Factory User" role that used to
    /// drive this (via vwGlobeweighUserFactory) has been retired. The legacy WPF Touch app
    /// still reads that view, which Migrations/TouchLoginByUserType.sql repoints at the
    /// same rule, so both stay in step.
    /// </summary>
    [HttpGet("touch-users")]
    public async Task<ActionResult<IEnumerable<TouchUserRow>>> TouchUsers(CancellationToken ct = default)
    {
        var rows = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.DateDeleted == null && !u.Disabled && TouchUserTypes.Contains(u.UserTypeId))
            .Select(u => new TouchUserRow
            {
                Id = u.Id,
                FullName = ((u.FirstName ?? "") + " " + (u.Surname ?? "")).Trim()
            })
            .ToListAsync(ct);

        return rows.OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Touch login by userId + numeric passcode. Mirrors GlobeweighUserRepository.LoginTouch:
    /// a blank stored passcode only matches a blank entry, otherwise an exact compare. Plaintext.
    /// </summary>
    [HttpPost("touch-login")]
    public async Task<ActionResult<AdminUserDto>> TouchLogin([FromBody] TouchLoginRequest req, CancellationToken ct = default)
    {
        if (req == null || req.UserId <= 0)
            return Unauthorized(new { message = "Invalid passcode" });

        var user = await _db.GlobeweighUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == req.UserId && u.DateDeleted == null, ct);
        if (user == null)
            return Unauthorized(new { message = "Invalid passcode" });

        // Only Touch / Both users get in here — the same rule as the picker above.
        if (!TouchUserTypes.Contains(user.UserTypeId))
            return Unauthorized(new { message = "Invalid passcode" });

        // Secret login (Ctrl+Enter on the Touch picker) skips the passcode — LAN only.
        // Tunnel traffic carries CF-Connecting-IP; never honour the skip from the internet.
        var viaTunnel = !string.IsNullOrWhiteSpace(Request.Headers["CF-Connecting-IP"].FirstOrDefault());
        if (req.SkipPasscode && viaTunnel)
            return Unauthorized(new { message = "Invalid passcode" });

        var passcode = (req.Passcode ?? string.Empty).Trim();
        var stored = (user.Passcode ?? string.Empty).Trim();
        if (req.SkipPasscode)
        {
            // LAN secret login — no passcode check.
        }
        else if (stored.Length == 0)
        {
            if (passcode.Length != 0) return Unauthorized(new { message = "Invalid passcode" });
        }
        else if (!string.Equals(stored, passcode, StringComparison.Ordinal))
        {
            return Unauthorized(new { message = "Invalid passcode" });
        }

        if (user.Disabled)
            return Unauthorized(new { message = "Account is disabled" });

        return await SessionForAsync(user, null, ct);
    }
}

// One row on the Touch login picker (GET /api/auth/touch-users).
public class TouchUserRow
{
    public int Id { get; set; }
    public string? FullName { get; set; }
}

// One row on the Switch user picker (GET /api/auth/switch-users).
public class SwitchUserRow
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? Surname { get; set; }
    public string? Username { get; set; }
    public int[] RoleIds { get; set; } = Array.Empty<int>();
    public bool Disabled { get; set; }
}

public class SwitchUserRequest
{
    public int TargetUserId { get; set; }
}

public class TouchLoginRequest
{
    public int UserId { get; set; }
    public string? Passcode { get; set; }
    public bool SkipPasscode { get; set; }
}

public class ChangePasswordRequest
{
    public string? OldPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? Passcode { get; set; }
}

public class AdminLoginRequest
{
    public string Username { get; set; } = "";
    public string? Password { get; set; }
}

public class AdminUserDto
{
    public int Id { get; set; }
    public int UserTypeId { get; set; }
    public string? FirstName { get; set; }
    public string? Surname { get; set; }
    public string? Username { get; set; }
    public int RoleId { get; set; }
    public int[] RoleIds { get; set; } = Array.Empty<int>();
    public bool CostingAccess { get; set; }
    public bool Disabled { get; set; }
    // Set while an admin is signed in AS this user (Switch user).
    public int? ImpersonatedByUserId { get; set; }
    public string? ImpersonatedByName { get; set; }
    public string Token { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
}
