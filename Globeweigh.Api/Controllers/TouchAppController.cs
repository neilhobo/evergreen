using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Globeweigh.Api.Controllers;

// Serves the Globeweigh Touch (TauriTouch) desktop installer + updater manifest:
//  - GET api/touch-app/info        -> version + file metadata
//  - GET api/touch-app/download    -> streams the latest NSIS setup.exe
//  - GET api/touch-app/download/msi -> streams the latest .msi
//  - GET api/touch-app/latest.json -> Tauri updater manifest (the endpoint configured
//                                     in TauriTouch's tauri.conf.json), so the in-app
//                                     shell updater (UpdatePill) works.
// Mirrors AdminAppController but filters the "Globeweigh Touch" artifacts. Installers
// are read from TouchApp:ReleasesPath (the \\evergreen\Globeweigh\Releases share by
// default) — a release is just dropping the built bundle there, no API redeploy needed.
[AllowAnonymous]
[ApiController]
[Route("api/touch-app")]
public class TouchAppController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<TouchAppController> _log;

    // Matches "Globeweigh Touch_0.1.0_x64-setup.exe" / "..._x64_en-US.msi" and
    // pulls out the version. Tolerates 2-4 version parts.
    private static readonly Regex VersionRx =
        new(@"_(\d+(?:\.\d+){1,3})_", RegexOptions.Compiled);

    public TouchAppController(IConfiguration config, ILogger<TouchAppController> log)
    {
        _config = config;
        _log = log;
    }

    private string ReleasesPath =>
        _config["TouchApp:ReleasesPath"] ?? @"\\evergreen\Globeweigh\Releases";

    private sealed record Release(string Path, Version Version, string FileName, long Size, DateTime ModifiedUtc);

    // The newest installer of a given extension (".exe" = NSIS setup, ".msi"), or null.
    private Release? Latest(string extension)
    {
        var dir = ReleasesPath;
        if (!Directory.Exists(dir)) return null;

        Release? best = null;
        foreach (var path in Directory.EnumerateFiles(dir, "*" + extension))
        {
            var name = Path.GetFileName(path);
            // Only the Touch setup artifacts (skip the Admin "Globeweigh WMS" and the
            // old WPF AdminSetup/TouchSetup files).
            if (!name.StartsWith("Globeweigh Touch", StringComparison.OrdinalIgnoreCase)) continue;
            if (extension == ".exe" && !name.EndsWith("setup.exe", StringComparison.OrdinalIgnoreCase)) continue;

            var m = VersionRx.Match(name);
            if (!m.Success || !Version.TryParse(m.Groups[1].Value, out var ver)) continue;

            var info = new FileInfo(path);
            if (best is null || ver > best.Version)
                best = new Release(path, ver, name, info.Length, info.LastWriteTimeUtc);
        }
        return best;
    }

    /// <summary>Latest installer metadata.</summary>
    [HttpGet("info")]
    public ActionResult<TouchAppInfoDto> GetInfo()
    {
        var exe = Latest(".exe");
        var msi = Latest(".msi");
        var primary = exe ?? msi;
        if (primary is null)
            return NotFound(new { message = "No installer is currently published." });

        var baseUrl = $"{Request.Scheme}://{Request.Host}/api/touch-app";
        return new TouchAppInfoDto
        {
            Version        = primary.Version.ToString(),
            ReleasedUtc    = primary.ModifiedUtc,
            SetupFileName  = exe?.FileName,
            SetupSizeBytes = exe?.Size,
            DownloadUrl    = exe is null ? null : $"{baseUrl}/download",
            MsiFileName    = msi?.FileName,
            MsiSizeBytes   = msi?.Size,
            MsiUrl         = msi is null ? null : $"{baseUrl}/download/msi",
        };
    }

    /// <summary>Streams the latest NSIS setup.exe as a download.</summary>
    [HttpGet("download")]
    public IActionResult Download()
    {
        var exe = Latest(".exe");
        if (exe is null) return NotFound("No setup.exe is currently published.");
        return PhysicalFile(exe.Path, "application/octet-stream", exe.FileName, enableRangeProcessing: true);
    }

    /// <summary>Streams the latest .msi as a download.</summary>
    [HttpGet("download/msi")]
    public IActionResult DownloadMsi()
    {
        var msi = Latest(".msi");
        if (msi is null) return NotFound("No .msi is currently published.");
        return PhysicalFile(msi.Path, "application/octet-stream", msi.FileName, enableRangeProcessing: true);
    }

    /// <summary>Tauri updater manifest (configured endpoint in tauri.conf.json).</summary>
    [HttpGet("latest.json")]
    public IActionResult LatestJson()
    {
        var exe = Latest(".exe");
        if (exe is null) return NotFound();

        // The updater verifies the download against the .sig produced alongside the setup.exe.
        var sigPath = exe.Path + ".sig";
        if (!System.IO.File.Exists(sigPath)) return NotFound();
        var signature = System.IO.File.ReadAllText(sigPath).Trim();

        var url = $"{Request.Scheme}://{Request.Host}/api/touch-app/download";
        return Ok(new
        {
            version = exe.Version.ToString(),
            notes = "Globeweigh Touch update.",
            pub_date = exe.ModifiedUtc.ToString("o"),
            platforms = new Dictionary<string, object>
            {
                ["windows-x86_64"] = new { signature, url },
            },
        });
    }
}

public class TouchAppInfoDto
{
    public string Version { get; set; } = "";
    public DateTime ReleasedUtc { get; set; }
    public string? SetupFileName { get; set; }
    public long? SetupSizeBytes { get; set; }
    public string? DownloadUrl { get; set; }
    public string? MsiFileName { get; set; }
    public long? MsiSizeBytes { get; set; }
    public string? MsiUrl { get; set; }
}
