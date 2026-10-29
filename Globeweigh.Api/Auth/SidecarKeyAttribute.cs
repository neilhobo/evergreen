using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Globeweigh.Api.Auth;

// Authenticates Globeweigh.SageSidecar: a shared secret in the X-Sidecar-Key header,
// compared against Sage:SidecarKey in configuration. Used on the sidecar half of
// SageController instead of the JWT the UIs carry, because the sidecar is a service with
// no user to sign in as, and a never-expiring user token is the wrong thing to bake into
// a config file on another machine.
//
// Pair it with [AllowAnonymous] on the action: that switches the JWT requirement off, and
// this filter still runs (AllowAnonymous only suppresses the built-in AuthorizeFilter).
//
// An unconfigured or too-short key refuses EVERY call (503), so a deploy that forgot the
// setting fails loudly rather than open. The server's appsettings.json is excluded from
// the deploy (see deploy-to-evergreen.ps1), so the key is set there once by hand.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SidecarKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Sidecar-Key";
    public const int MinKeyLength = 16;

    public void OnAuthorization(AuthorizationFilterContext ctx)
    {
        var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = config["Sage:SidecarKey"];
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < MinKeyLength)
        {
            ctx.Result = new ObjectResult(new
            {
                message = $"Sage:SidecarKey is not configured on the API (needs {MinKeyLength}+ characters).",
            })
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return;
        }

        var given = ctx.HttpContext.Request.Headers[HeaderName].ToString();
        if (!KeysMatch(given, expected))
            ctx.Result = new UnauthorizedResult();
    }

    // Constant-time compare — a key check should not leak how much of the key was right.
    private static bool KeysMatch(string given, string expected)
    {
        if (string.IsNullOrEmpty(given)) return false;
        var a = Encoding.UTF8.GetBytes(given);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
