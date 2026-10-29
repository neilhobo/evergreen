using System.Security.Claims;
using Globeweigh.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Auth;

public static class UserExtensions
{
    // The one Globeweigh-internal super-admin login. The admin UI hides its pages from
    // everyone else (types.ts SUPER_ADMIN_USERNAME - the same string); the endpoints behind
    // them ask here so a token alone is not enough. A username, not a role: client staff
    // hold the GlobeweighAdmin role too.
    public const string SuperAdminUsername = "neil.hobson@globeweigh.com";

    // The signed-in user's GlobeweighUser.Id, from the JWT "sub" claim (0 if absent). The same
    // answer off the principal itself — for middleware, which has a request but no controller —
    // is ClaimsExtensions.GetUserId in Auth/Jwt.cs.
    public static int GetUserId(this ControllerBase controller) => controller.User.GetUserId();

    // Whether the signed-in user is the super-admin (looked up by id: the token carries
    // no username).
    public static async Task<bool> IsSuperAdminAsync(
        this ControllerBase controller, GbEvergreenContext db, CancellationToken ct)
    {
        var id = controller.GetUserId();
        if (id == 0) return false;
        var username = await db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.Id == id && u.DateDeleted == null)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(ct);
        return string.Equals(username?.Trim(), SuperAdminUsername, StringComparison.OrdinalIgnoreCase);
    }
}
