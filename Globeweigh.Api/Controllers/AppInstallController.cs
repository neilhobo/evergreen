using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Globeweigh.Api.Controllers;

/// <summary>
/// Operator-facing Android install landing page for the Picking app. Lives at the friendly
/// route <c>/app</c> (i.e. http://&lt;server&gt;:5099/app) instead of the raw
/// <c>api/devices/apk?fileName=</c> link, so the first install on an ET40 is "open the page,
/// tap Install". Always serves the LATEST .apk in the packages folder (the same folder the
/// Devices tab uploads to), so the URL never changes between releases. Anonymous — operators
/// aren't logged in.
/// </summary>
[AllowAnonymous]
[Route("app")]
public class AppInstallController : ControllerBase
{
    private static readonly Regex VersionRx = new(@"(\d+\.\d+(?:\.\d+){0,2})", RegexOptions.Compiled);

    private readonly string _packagesPath;

    public AppInstallController(IConfiguration config, IWebHostEnvironment env)
    {
        _packagesPath = config["DeviceManagement:PackagesPath"]
            ?? Path.Combine(env.ContentRootPath, "_packages");
    }

    // The newest APK by version (tie-break on write time); null if none staged.
    private FileInfo? LatestApk()
    {
        if (!Directory.Exists(_packagesPath)) return null;
        return Directory.GetFiles(_packagesPath, "*.apk")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => ParseVersion(f.Name))
            .ThenByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>The install landing page (HTML).</summary>
    [HttpGet]
    public IActionResult Index()
    {
        var apk = LatestApk();
        var basePath = Request.PathBase.HasValue ? Request.PathBase.Value : "";
        var html = apk == null ? NoPackageHtml() : InstallPageHtml(basePath!, ParseVersion(apk.Name), apk.Length);
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8" };
    }

    /// <summary>Stream the latest APK with the Android package content-type so the browser offers to install it.</summary>
    [HttpGet("download")]
    public IActionResult Download()
    {
        var apk = LatestApk();
        if (apk == null) return NotFound("No Android package is available.");
        return PhysicalFile(apk.FullName, "application/vnd.android.package-archive", apk.Name);
    }

    private static Version ParseVersion(string fileName)
    {
        var m = VersionRx.Match(fileName);
        return (m.Success && Version.TryParse(m.Groups[1].Value, out var v)) ? v : new Version(0, 0);
    }

    private static string InstallPageHtml(string basePath, Version version, long sizeBytes)
    {
        var mb = (sizeBytes / 1024.0 / 1024.0).ToString("0.0");
        var dl = $"{basePath}/app/download";
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1, viewport-fit=cover"" />
<title>Install Globeweigh Picking</title>
<style>
  :root {{ --blue:#087EEF; --navy:#152338; }}
  * {{ box-sizing:border-box; }}
  body {{ margin:0; font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif; background:var(--navy);
         color:#fff; min-height:100vh; display:flex; align-items:center; justify-content:center; padding:24px; }}
  .card {{ background:#1e2c44; border-radius:18px; padding:32px 24px; max-width:420px; width:100%; text-align:center;
           box-shadow:0 12px 40px rgba(0,0,0,.4); }}
  h1 {{ font-size:22px; margin:0 0 4px; }}
  .ver {{ color:#9aa4b8; font-size:14px; margin-bottom:24px; }}
  a.btn {{ display:block; background:var(--blue); color:#fff; text-decoration:none; font-size:19px; font-weight:600;
           padding:18px; border-radius:12px; margin-bottom:20px; }}
  a.btn:active {{ filter:brightness(.9); }}
  ol {{ text-align:left; color:#c7cedb; font-size:14px; line-height:1.6; padding-left:20px; margin:0; }}
  .logo {{ width:56px;height:56px;border-radius:14px;background:var(--blue);display:inline-flex;align-items:center;
           justify-content:center;font-size:28px;font-weight:700;margin-bottom:14px; }}
</style>
</head>
<body>
  <div class=""card"">
    <div class=""logo"">G</div>
    <h1>Globeweigh Picking</h1>
    <div class=""ver"">Latest version {version} &middot; {mb} MB</div>
    <a class=""btn"" href=""{dl}"">Install latest version</a>
    <ol>
      <li>Tap <b>Install latest version</b> above.</li>
      <li>If asked, allow this browser to <b>install unknown apps</b>, then tap <b>Install</b>.</li>
      <li>When prompted, tap the downloaded file and confirm <b>Install</b>.</li>
      <li>Open <b>Globeweigh Picking</b> from the home screen.</li>
    </ol>
  </div>
</body>
</html>";
    }

    private static string NoPackageHtml() =>
        "<!DOCTYPE html><html><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>" +
        "<title>Install Globeweigh Picking</title></head><body style=\"font-family:sans-serif;padding:32px;text-align:center\">" +
        "<h2>No Android package is available yet.</h2><p>Please contact your administrator.</p></body></html>";
}
