using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;

namespace Globeweigh.Api.Services;

// Writes Super Admin > Login History (dbo.LoginEvent). Best effort: a failure to log is
// swallowed, never a failed sign-in — not being able to record a login must not lock anyone out.
public static class LoginLog
{
    public static async Task WriteAsync(
        GbEvergreenContext db, HttpContext http, string kind,
        int? userId, string? username, string? detail = null, int? byUserId = null,
        CancellationToken ct = default)
    {
        try
        {
            var req = http.Request;
            var viaTunnel = GateBarrierRelay.ViaTunnel(req);
            var ip = viaTunnel
                ? req.Headers["Cf-Connecting-IP"].ToString()
                : http.Connection.RemoteIpAddress?.MapToIPv4().ToString();
            db.Add(new LoginEvent
            {
                DateLogged = DateTime.Now,
                Kind = kind,
                UserId = userId,
                Username = Cap(username?.Trim(), 100),
                ByUserId = byUserId,
                Detail = Cap(detail, 200),
                Client = ClientOf(req),
                Via = viaTunnel ? "Tunnel" : "Site",
                IpAddress = Cap(ip, 64),
                UserAgent = Cap(req.Headers.UserAgent.ToString(), 300),
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Logged nothing; the sign-in itself carries on.
        }
    }

    // TauriAdmin sends X-Client: TauriAdmin, the mobile app X-Client: Mobile. A mobile build
    // from before the header still says so in its user agent (dart:io).
    private static string ClientOf(HttpRequest req)
    {
        var client = req.Headers["X-Client"].ToString();
        if (!string.IsNullOrWhiteSpace(client)) return Cap(client.Trim(), 30)!;
        var ua = req.Headers.UserAgent.ToString();
        return ua.Contains("dart", StringComparison.OrdinalIgnoreCase) ? "Mobile" : "Unknown";
    }

    private static string? Cap(string? s, int max) =>
        string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s[..max];
}
