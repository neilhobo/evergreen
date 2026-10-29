using System.Security.Claims;
using Globeweigh.Api.Auth;

namespace Globeweigh.Api.Data.Auditing;

/// <summary>Who is making the changes in THIS request, and where from. Scoped, so the context
/// saving the changes and the endpoint handling the request are looking at the same answer.
///
/// <para>Filled once per request by <c>UseAuditScope</c> rather than passed down through every
/// method that might save something: the point of auditing in SaveChanges is that a caller cannot
/// forget to do it, and a caller that had to hand the user down could.</para>
///
/// <para>Everything on it is optional. The touch and picking screens post without a token — by
/// design, a warehouse screen may have no login at all. The picking app does know who is on the
/// tablet, though, and sends that user with every write; PickingController passes it on with
/// <see cref="ActAs"/>, so a change made from the app is that person's and not nobody's. A change
/// with no user at all reads that way rather than being given one.</para></summary>
public class AuditScope
{
    public int? UserId { get; private set; }
    public string? UserName { get; private set; }
    public string? Source { get; private set; }

    public void Set(int? userId, string? userName, string? source)
    {
        UserId = userId;
        UserName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
    }

    /// <summary>The user a request said it was acting for, where it carried no token to say so
    /// (the picking app sends the tablet's signed-in user with every write). Source stays as the
    /// middleware set it.</summary>
    public void ActAs(int userId, string? userName)
    {
        UserId = userId;
        UserName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();
    }
}

public static class AuditScopeMiddleware
{
    /// <summary>The header a client uses to name itself — "TauriAdmin", "Picking/GW-TAB-03".
    /// A hint, not a credential: it only ever fills in Source, which is why an unauthenticated
    /// caller is allowed to set it. Without it Source is the app the route belongs to, if that
    /// is known (<see cref="SourceByRoute"/>), and otherwise nothing.</summary>
    public const string ClientHeader = "X-Client";

    /// <summary>Fills the request's <see cref="AuditScope"/> from the bearer token and the client
    /// header. Must run AFTER UseAuthentication, or there is no user on the request yet.</summary>
    public static IApplicationBuilder UseAuditScope(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var scope = ctx.RequestServices.GetService<AuditScope>();
            if (scope != null)
            {
                var user = ctx.User;
                var signedIn = user?.Identity?.IsAuthenticated == true;
                var id = signedIn ? user!.GetUserId() : 0;
                var client = ctx.Request.Headers[ClientHeader].ToString();
                scope.Set(
                    id > 0 ? id : null,
                    signedIn ? NameOf(user!) : null,
                    // The calling app when it names itself; otherwise the app its route belongs
                    // to. Never the caller's address: it used to be, and an address means nothing
                    // to anyone reading the trail (AuditController blanks the ones already stored).
                    string.IsNullOrWhiteSpace(client) ? SourceByRoute(ctx) : Truncate(client, 100));
            }
            await next();
        });

    // The app behind a call that did not name itself, from the route it came in on: the picking
    // and touch apps send no header. Null for anything else — a row with no source is better than
    // one with a made-up source.
    private static string? SourceByRoute(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/api/picking", StringComparison.OrdinalIgnoreCase)) return "Picking";
        if (path.StartsWith("/api/touch", StringComparison.OrdinalIgnoreCase)) return "Touch";
        return null;
    }

    private static string? NameOf(ClaimsPrincipal user) =>
        user.FindFirst("name")?.Value is { Length: > 0 } n ? n : user.Identity?.Name;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
