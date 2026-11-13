using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Admin "Staff (Login)" management. Mirrors the WPF GlobeweighUserListViewModel /
// AddEditGlobeweighUserViewModel: list (excluding the GlobeweighAdmin user id=1),
// add/edit with roles via the Globeweigh_Role junction, soft delete. Plaintext
// password/passcode, matching the existing desktop data.
[ApiController]
[Authorize]
[Route("api/staff")]
public class StaffController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public StaffController(GbEvergreenContext db) => _db = db;

    // eUserType.Touch — Touch-only users have no username/password, just a passcode.
    private const int UserTypeTouch = 2;
    // eUserType.NoLogin — staff kept for Annual Leave who never sign in (drivers, factory): no
    // username, password, passcode or role. Give them a type with a login to let them in.
    private const int UserTypeNoLogin = 4;

    private static bool HasNoOfficeLogin(int userTypeId) => userTypeId is UserTypeTouch or UserTypeNoLogin;

    // eUserRole values a user can be given: Office Admin, Office User, Sales. GlobeweighAdmin (1)
    // is never assignable; Factory User (4) and Weighbridge User (5) are retired.
    private static readonly int[] AssignableRoles = { 2, 3, 6 };

    // A user holds exactly one role (Touch-only users none), so the legacy single RoleId column
    // is kept equal to it — the login folds that column in, and a stale one would add a role.
    private static int SingleRole(StaffInput input) =>
        HasNoOfficeLogin(input.UserTypeId) || input.RoleIds == null || input.RoleIds.Length == 0 ? 0 : input.RoleIds[0];

    // GET /api/staff — staff list with their effective role ids.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StaffRow>>> List(CancellationToken ct = default)
    {
        var users = await _db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.DateDeleted == null && u.Id != 1)
            .OrderBy(u => u.FirstName).ThenBy(u => u.Surname)
            .ToListAsync(ct);

        var ids = users.Select(u => u.Id).ToList();
        var roles = await _db.Set<UserRoleEntity>().AsNoTracking()
            .Where(r => ids.Contains(r.UserId))
            .ToListAsync(ct);

        var tags = (await _db.Set<UserTagEntity>().AsNoTracking()
                .Where(t => ids.Contains(t.UserId))
                .ToListAsync(ct))
            .ToLookup(t => t.UserId, t => t.Tag);

        var rows = users.Select(u => ToRow(u, roles.Where(r => r.UserId == u.Id).Select(r => r.RoleId), tags[u.Id]));
        return Ok(rows);
    }

    // POST /api/staff — create a staff user (+ role rows).
    [HttpPost]
    public async Task<ActionResult<StaffRow>> Create([FromBody] StaffInput input, CancellationToken ct = default)
    {
        var error = Validate(input);
        if (error != null) return BadRequest(new { message = error });

        var user = new GlobeweighUser
        {
            UserTypeId    = input.UserTypeId,
            FirstName     = input.FirstName,
            Surname       = input.Surname,
            Username      = HasNoOfficeLogin(input.UserTypeId) ? null : input.Username,
            Password      = HasNoOfficeLogin(input.UserTypeId) ? null : input.Password,
            Passcode      = input.UserTypeId == UserTypeNoLogin ? null : input.Passcode,
            Disabled      = input.Disabled,
            CostingAccess = input.CostingAccess,
            RoleId        = SingleRole(input),
            DateCreated   = DateTime.Now,
        };
        _db.GlobeweighUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        await SyncRoles(user.Id, input, ct);
        var tags = await SyncTags(user.Id, input, ct);
        await _db.SaveChangesAsync(ct);

        return ToRow(user, HasNoOfficeLogin(input.UserTypeId) ? Array.Empty<int>() : input.RoleIds ?? Array.Empty<int>(), tags);
    }

    // PUT /api/staff/{id} — update a staff user (+ resync role rows).
    [HttpPut("{id:int}")]
    public async Task<ActionResult<StaffRow>> Update(int id, [FromBody] StaffInput input, CancellationToken ct = default)
    {
        var error = Validate(input);
        if (error != null) return BadRequest(new { message = error });

        var user = await _db.GlobeweighUsers.FirstOrDefaultAsync(u => u.Id == id && u.DateDeleted == null, ct);
        if (user == null) return NotFound();

        user.UserTypeId    = input.UserTypeId;
        user.FirstName     = input.FirstName;
        user.Surname       = input.Surname;
        user.Username      = HasNoOfficeLogin(input.UserTypeId) ? null : input.Username;
        user.Password      = HasNoOfficeLogin(input.UserTypeId) ? null : input.Password;
        user.Passcode      = input.UserTypeId == UserTypeNoLogin ? null : input.Passcode;
        user.Disabled      = input.Disabled;
        user.CostingAccess = input.CostingAccess;
        user.RoleId        = SingleRole(input);

        var existing = await _db.Set<UserRoleEntity>().Where(r => r.UserId == id).ToListAsync(ct);
        _db.RemoveRange(existing);
        await SyncRoles(id, input, ct);
        var tags = await SyncTags(id, input, ct);
        await _db.SaveChangesAsync(ct);

        return ToRow(user, HasNoOfficeLogin(input.UserTypeId) ? Array.Empty<int>() : input.RoleIds ?? Array.Empty<int>(), tags);
    }

    // DELETE /api/staff/{id} — soft delete (DateDeleted).
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        var user = await _db.GlobeweighUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return NotFound();
        user.DateDeleted = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task SyncRoles(int userId, StaffInput input, CancellationToken ct)
    {
        // Touch-only users carry no office roles (matches the WPF editor).
        if (HasNoOfficeLogin(input.UserTypeId) || input.RoleIds == null) return;
        foreach (var roleId in input.RoleIds.Distinct())
        {
            _db.Add(new UserRoleEntity { UserId = userId, RoleId = roleId, DateCreated = DateTime.Now });
        }
        await Task.CompletedTask;
    }

    // Brings the user's tags into line with input.Tags and returns them. Tags left out of the
    // request (null: a client from before tags) stay as they are. Unknown keys are dropped. A
    // user with no office login keeps none: the features behind tags need one.
    private async Task<List<string>> SyncTags(int userId, StaffInput input, CancellationToken ct)
    {
        var existing = await _db.Set<UserTagEntity>().Where(t => t.UserId == userId).ToListAsync(ct);
        if (input.Tags == null) return existing.Select(t => t.Tag).ToList();

        var wanted = HasNoOfficeLogin(input.UserTypeId)
            ? new List<string>()
            : StaffTags.All.Where(k => input.Tags.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        _db.RemoveRange(existing.Where(t => !wanted.Contains(t.Tag)));
        foreach (var tag in wanted.Where(k => existing.All(t => t.Tag != k)))
            _db.Add(new UserTagEntity { UserId = userId, Tag = tag, DateCreated = DateTime.Now });
        return wanted;
    }

    // Mirrors AddEditGlobeweighUserViewModel.CanSave.
    private static string? Validate(StaffInput input)
    {
        if (string.IsNullOrWhiteSpace(input.FirstName)) return "First Name is required.";
        if (string.IsNullOrWhiteSpace(input.Surname)) return "Surname is required.";
        if (input.UserTypeId is < 1 or > 4) return "Choose a user type.";

        if (input.UserTypeId == UserTypeNoLogin) return null;

        if (input.UserTypeId != UserTypeTouch)
        {
            if (string.IsNullOrWhiteSpace(input.Username)) return "Username is required.";
            if (string.IsNullOrWhiteSpace(input.Password)) return "Password is required.";
            if (input.RoleIds == null || input.RoleIds.Distinct().Count() != 1) return "Select one role.";
            if (!AssignableRoles.Contains(input.RoleIds[0])) return "That role cannot be assigned.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.Passcode)) return "Passcode is required.";
        }

        if (!string.IsNullOrEmpty(input.Passcode))
        {
            if (input.Passcode.Length != 4 || !input.Passcode.All(char.IsDigit))
                return "Passcode must be 4 digits.";
        }
        return null;
    }

    private static StaffRow ToRow(GlobeweighUser u, IEnumerable<int> roleIds, IEnumerable<string> tags) => new()
    {
        Id            = u.Id,
        UserTypeId    = u.UserTypeId,
        FirstName     = u.FirstName,
        Surname       = u.Surname,
        Username      = u.Username,
        Password      = u.Password,
        Passcode      = u.Passcode,
        Disabled      = u.Disabled,
        CostingAccess = u.CostingAccess,
        RoleIds       = roleIds.Distinct().OrderBy(r => r).ToArray(),
        Tags          = tags.Distinct().OrderBy(t => t).ToArray(),
    };
}

public class StaffRow
{
    public int Id { get; set; }
    public int UserTypeId { get; set; }
    public string? FirstName { get; set; }
    public string? Surname { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Passcode { get; set; }
    public bool Disabled { get; set; }
    public bool CostingAccess { get; set; }
    public int[] RoleIds { get; set; } = Array.Empty<int>();
    // StaffTags keys: extra features switched on for this person (not roles).
    public string[] Tags { get; set; } = Array.Empty<string>();
}

public class StaffInput
{
    public int UserTypeId { get; set; }
    public string? FirstName { get; set; }
    public string? Surname { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Passcode { get; set; }
    public bool Disabled { get; set; }
    public bool CostingAccess { get; set; }
    public int[]? RoleIds { get; set; }
    // null = leave the user's tags as they are.
    public string[]? Tags { get; set; }
}
