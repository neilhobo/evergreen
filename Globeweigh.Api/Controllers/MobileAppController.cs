using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Globeweigh.Api.Controllers;

/// <summary>
/// Store-version manifest for the Evergreen mobile app (mobile/). The app calls it on launch,
/// resume and sign-in and shows an "Update available" prompt with an Update button (TestFlight
/// on iOS) when its build number is below <c>latest</c>; below <c>minimum</c> it blocks.
///
/// The manifest is <c>mobile-version.json</c> in the API folder, read per request so a release
/// needs no API deploy or recycle. <c>mobile/tools/release-ios.ps1</c> writes it on the server
/// after a successful upload; the API deploy never ships or overwrites it (it's not in the repo,
/// and deploy-to-evergreen.ps1 excludes it). Missing file / platform = nothing to prompt.
///
/// <code>
/// { "ios": { "latest": "1.0.0+4", "previous": "1.0.0+3", "availableFromUtc": "2026-11-05T14:20:00Z",
///            "minimum": "1.0.0+2", "url": "itms-beta://beta.itunes.apple.com/v1/app/6830162362" } }
/// </code>
/// <c>minimum</c> is set by hand on the server when an old build must stop working.
/// Anonymous: the check runs before sign-in too.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/mobile")]
public class MobileAppController : ControllerBase
{
    private readonly string _manifestPath;
    private readonly ILogger<MobileAppController> _log;

    public MobileAppController(IWebHostEnvironment env, ILogger<MobileAppController> log)
    {
        _manifestPath = Path.Combine(env.ContentRootPath, "mobile-version.json");
        _log = log;
    }

    public record MobileVersionDto(string Latest, string Minimum, string Url);

    [HttpGet("version")]
    public ActionResult<MobileVersionDto> Version([FromQuery] string platform = "ios")
    {
        var empty = new MobileVersionDto("", "", "");
        if (!System.IO.File.Exists(_manifestPath)) return empty;
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(_manifestPath));
            if (!doc.RootElement.TryGetProperty(platform.ToLowerInvariant(), out var p) ||
                p.ValueKind != JsonValueKind.Object)
                return empty;
            // TestFlight takes ~10 minutes to process an upload; until availableFromUtc the
            // previous build stays "latest", so nobody is sent to TestFlight for a build it
            // can't install yet.
            var latest = Str(p, "latest");
            if (DateTime.TryParse(Str(p, "availableFromUtc"), null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var from) && DateTime.UtcNow < from)
                latest = Str(p, "previous");
            return new MobileVersionDto(latest, Str(p, "minimum"), Str(p, "url"));
        }
        catch (Exception ex)
        {
            // A hand-edited manifest with a typo must not take the app's start-up down with it.
            _log.LogWarning(ex, "mobile-version.json could not be read");
            return empty;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
