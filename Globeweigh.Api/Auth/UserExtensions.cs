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

    // Who may use Switch user (sign in as someone else): the super-admin and Colin McCuskey
    // only, by the user's decision 2026-11-05 — not every Office Admin. Hard-coded by username
    // on purpose; TauriAdmin (types.ts canSwitchUser) and the mobile app (models.dart
    // canSwitchUser) carry the same list to hide the menu item.
    public static readonly string[] SwitchUserUsernames =
    {
        SuperAdminUsername,
        "colin@evergreenhorticulture.co.uk",
    };

    public static bool MaySwitchUser(string? username) =>
        SwitchUserUsernames.Any(u => string.Equals(u, username?.Trim(), StringComparison.OrdinalIgnoreCase));

    // Who sees the mobile app's Sales overview (every rep's orders, approve / return from the
    // phone): the same two people, decided 2026-11-05, kept as its own list so either can change
    // alone. The mobile app (models.dart salesOverviewUsernames) carries it to show the button.
    public static readonly string[] SalesOverviewUsernames =
    {
        SuperAdminUsername,
        "colin@evergreenhorticulture.co.uk",
    };

    public static Task<bool> MaySeeSalesOverviewAsync(
        this ControllerBase controller, GbEvergreenContext db, CancellationToken ct) =>
        controller.IsOneOfAsync(db, SalesOverviewUsernames, ct);

    // Who works the gate and barrier (and watches its cameras) from the mobile app: the same
    // two, decided 2026-11-05, its own list like the Sales overview's. models.dart
    // gateBarrierUsernames carries it to show the module.
    public static readonly string[] GateBarrierUsernames =
    {
        SuperAdminUsername,
        "colin@evergreenhorticulture.co.uk",
    };

    public static Task<bool> MayUseGateBarrierAsync(
        this ControllerBase controller, GbEvergreenContext db, CancellationToken ct) =>
        controller.IsOneOfAsync(db, GateBarrierUsernames, ct);

    // Whether the signed-in user's username is on a hard-coded list (looked up by id: the
    // token carries no username).
    private static async Task<bool> IsOneOfAsync(
        this ControllerBase controller, GbEvergreenContext db, string[] usernames, CancellationToken ct)
    {
        var id = controller.GetUserId();
        if (id == 0) return false;
        var username = await db.GlobeweighUsers.AsNoTracking()
            .Where(u => u.Id == id && u.DateDeleted == null)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(ct);
        return usernames.Any(u => string.Equals(u, username?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

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
