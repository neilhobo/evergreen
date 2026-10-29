using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

/// <summary>
/// Mobile-app device registration + the TauriAdmin Devices (deployment) page.
/// The Android Picking app is pull-only: it polls <c>POST /api/devices/register</c> on
/// startup, and if a release is queued for it pulls <c>GET /api/devices/apk</c> and
/// self-installs. The admin endpoints (list / packages / push / cancel / delete) back the
/// dev-only Devices tab. Note the route is <c>api/devices</c> (plural) — distinct from the
/// touch-screen <c>api/device</c> registry in DeviceController.
/// </summary>
[ApiController]
[Authorize]
[Route("api/devices")]
public class DeviceManagementController : ControllerBase
{
    private const string AndroidApp = "Globeweigh.Picking";
    private static readonly Regex VersionRx = new(@"(\d+\.\d+(?:\.\d+){0,2})", RegexOptions.Compiled);

    private readonly GbEvergreenContext _db;
    private readonly string _packagesPath;
    private readonly string _companionsPath;

    public DeviceManagementController(GbEvergreenContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _packagesPath = config["DeviceManagement:PackagesPath"]
            ?? Path.Combine(env.ContentRootPath, "_packages");
        Directory.CreateDirectory(_packagesPath);
        // Companion apps (droidVNC-NG etc.) live apart from _packages so the newest-by-
        // version pickers there never confuse them with Picking releases.
        _companionsPath = config["DeviceManagement:CompanionsPath"]
            ?? Path.Combine(env.ContentRootPath, "_companions");
    }

    /// <summary>
    /// The droidVNC-NG companion APK (remote-view server). The Picking app downloads and
    /// prompt-installs this when it isn't on the device yet. Anonymous — devices have no login.
    /// </summary>
    [HttpGet("companion/vnc")]
    [AllowAnonymous]
    public IActionResult VncCompanionApk()
    {
        var apk = Directory.Exists(_companionsPath)
            ? Directory.GetFiles(_companionsPath, "droidVNC*.apk")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (apk == null) return NotFound("No VNC companion APK is staged on the server.");
        return PhysicalFile(apk.FullName, "application/vnd.android.package-archive", apk.Name);
    }

    // ---- Registration (the device's pull check-in) ----------------------------

    /// <summary>
    /// Register/refresh this device in the Device table on startup. Mirrors the desktop
    /// apps' RegisterDevice: upsert keyed on DeviceName. The caller's IP (as seen by the
    /// server) is recorded. Returns any queued update so the app can offer it. Anonymous:
    /// the mobile app has no login.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<MobileDeviceDto>> Register([FromBody] RegisterDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceName))
            return BadRequest(new { message = "DeviceName is required." });

        var ip = ClientIp();
        var now = DateTime.Now;

        var device = await _db.Set<Device>()
            .FirstOrDefaultAsync(d => d.DeviceName == request.DeviceName);

        if (device is null)
        {
            device = new Device
            {
                DeviceName = request.DeviceName,
                IpAddress = ip,
                ApplicationName = request.ApplicationName,
                CurrentVersion = request.CurrentVersion,
                CurrentPatch = request.CurrentPatch,
                VersionOnServer = string.Empty,
                AutoStartConfigured = false,
                RecycleRequested = false,
                DateCreated = now,
                DateOfLastPing = now,
            };
            _db.Add(device);
        }
        else
        {
            // A new version or Shorebird patch clears the device's old push status in Device
            // Manager (List hides finished push rows older than this).
            if (!string.Equals(device.CurrentVersion, request.CurrentVersion, StringComparison.OrdinalIgnoreCase) ||
                device.CurrentPatch != request.CurrentPatch)
                device.DateOfLastUpdateUtc = DateTime.UtcNow;
            device.IpAddress = ip;
            device.ApplicationName = request.ApplicationName;
            device.CurrentVersion = request.CurrentVersion;
            device.CurrentPatch = request.CurrentPatch;
            device.DateOfLastPing = now;
            device.DateOfLastRestart = now;
        }

        await _db.SaveChangesAsync();

        // Is there a queued update for this device? (the per-device push queue)
        var pending = await (
            from pjd in _db.Set<PushJobDevice>()
            join pj in _db.Set<PushJob>() on pjd.PushJobId equals pj.Id
            where pjd.DeviceId == device.Id && pjd.Status == "Pending"
            orderby pjd.Id descending
            select new { pjd, pj }
        ).FirstOrDefaultAsync();

        var pendingUpdate = false;
        string? pendingVersion = null;
        string? pendingPackage = null;

        if (pending is not null)
        {
            // If the device is now reporting the target version, the update completed.
            if (!string.IsNullOrEmpty(device.CurrentVersion) &&
                string.Equals(device.CurrentVersion, pending.pj.Version, StringComparison.OrdinalIgnoreCase))
            {
                pending.pjd.Status = "Done";
                pending.pjd.Detail = $"Updated to {pending.pj.Version}";
                pending.pjd.UpdatedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            else
            {
                pendingUpdate = true;
                pendingVersion = pending.pj.Version;
                pendingPackage = pending.pj.PackageFileName;
            }
        }

        return new MobileDeviceDto(
            device.Id,
            device.DeviceName,
            device.IpAddress,
            device.ApplicationName,
            device.CurrentVersion,
            device.DateCreated,
            device.DateOfLastPing,
            pendingUpdate,
            pendingVersion,
            pendingPackage);
    }

    // ---- Devices grid ----------------------------------------------------------

    /// <summary>
    /// Every device registered on the system — touch screens, factory PCs, tablets —
    /// with its application and version. Backs the client-facing Devices page (the
    /// filtered mobile-only <see cref="List"/> stays for the Device Manager).
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<List<SystemDeviceDto>>> ListAll([FromQuery] bool includeHidden = false)
    {
        return await _db.Set<Device>().AsNoTracking()
            // Admin-app logins register a Device row too — office PCs, not site devices.
            .Where(d => d.ApplicationName != "Globeweigh.Admin")
            .Where(d => includeHidden || !d.Hidden)
            .OrderBy(d => d.ApplicationName).ThenBy(d => d.DeviceName)
            .Select(d => new SystemDeviceDto(
                d.Id, d.DeviceName, d.ApplicationName, d.CurrentVersion,
                d.IpAddress, d.DateOfLastPing, d.Hidden))
            .ToListAsync();
    }

    /// <summary>Hide (or unhide) a device from the client-facing Devices overview.</summary>
    [HttpPost("{id:int}/hidden")]
    public async Task<IActionResult> SetHidden(int id, [FromBody] SetHiddenRequest req)
    {
        var rows = await _db.Set<Device>().Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Hidden, req.Hidden));
        return rows == 0 ? NotFound() : NoContent();
    }

    /// <summary>List the mobile devices with their latest push status.</summary>
    [HttpGet]
    public async Task<ActionResult<List<DeviceDashboardDto>>> List()
    {
        var devices = await _db.Set<Device>().AsNoTracking()
            .Where(d => d.ApplicationName == AndroidApp)
            .OrderBy(d => d.DeviceName)
            .ToListAsync();

        var ids = devices.Select(d => d.Id).ToList();
        var pushRows = await _db.Set<PushJobDevice>().AsNoTracking()
            .Where(p => p.DeviceId != null && ids.Contains(p.DeviceId!.Value))
            .ToListAsync();
        var latest = pushRows
            .GroupBy(p => p.DeviceId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

        return devices.Select(d =>
        {
            latest.TryGetValue(d.Id, out var p);
            // A finished push the device has since moved on from (new version or patch) is
            // old news: show nothing. Pending always shows.
            if (p is not null && p.Status != "Pending" &&
                d.DateOfLastUpdateUtc is { } updated && p.UpdatedUtc < updated)
                p = null;
            return new DeviceDashboardDto(
                d.Id, d.DeviceName, d.ApplicationName, "Android",
                d.CurrentVersion, d.CurrentPatch, d.IpAddress, d.DateOfLastPing, d.RecycleRequested,
                p?.Status, p?.Detail, p?.PushJobId ?? 0, p?.Id ?? 0,
                d.RemoteHost, d.RemotePort, d.RemoteNotes);
        }).ToList();
    }

    /// <summary>
    /// ICMP-ping each device's last-known IP to report whether it is currently online.
    /// Returns a map of device id → reachable. Pings run in parallel with a short timeout
    /// so an offline device doesn't stall the whole call. The admin grid polls this.
    /// </summary>
    [HttpGet("ping")]
    public async Task<ActionResult<Dictionary<int, bool>>> PingDevices([FromQuery] bool all = false)
    {
        // Default scope is the mobile devices (Device Manager grid); all=true pings every
        // non-admin device for the client-facing Devices overview's live online dots.
        var devices = await _db.Set<Device>().AsNoTracking()
            .Where(d => all ? d.ApplicationName != "Globeweigh.Admin" : d.ApplicationName == AndroidApp)
            .Where(d => d.IpAddress != null && d.IpAddress != "")
            .Select(d => new { d.Id, d.IpAddress })
            .ToListAsync();

        var results = await Task.WhenAll(devices.Select(async d =>
        {
            bool online;
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(d.IpAddress!, 1000);
                online = reply.Status == IPStatus.Success;
            }
            catch
            {
                online = false;
            }
            return (d.Id, online);
        }));

        return results.ToDictionary(r => r.Id, r => r.online);
    }

    /// <summary>
    /// Save a device's remote-control (scrcpy / wireless-ADB) setup — host/port/notes the
    /// TauriAdmin detail panel uses to pair, connect and launch scrcpy. Blank host/notes
    /// store as NULL (host then falls back to the device's registered IP).
    /// </summary>
    [HttpPost("{id:int}/remote")]
    public async Task<IActionResult> SaveRemote(int id, [FromBody] DeviceRemoteRequest req)
    {
        var device = await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == id);
        if (device is null) return NotFound();

        device.RemoteHost = string.IsNullOrWhiteSpace(req.RemoteHost) ? null : req.RemoteHost.Trim();
        device.RemotePort = req.RemotePort;
        device.RemoteNotes = string.IsNullOrWhiteSpace(req.RemoteNotes) ? null : req.RemoteNotes.Trim();
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Manually add a device by name + IP for one that has never run the app (so never
    /// self-registered). Creates a Globeweigh.Picking Device row the grid lists and can
    /// ping / push. If the device later runs the app, Register upserts on DeviceName.
    /// </summary>
    [HttpPost("manual")]
    public async Task<ActionResult<DeviceDashboardDto>> AddManual([FromBody] AddManualDeviceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceName))
            return BadRequest(new { message = "Device name is required." });
        if (string.IsNullOrWhiteSpace(req.IpAddress))
            return BadRequest(new { message = "IP address is required." });

        var name = req.DeviceName.Trim();
        if (await _db.Set<Device>().AnyAsync(d => d.DeviceName == name))
            return Conflict(new { message = $"A device named '{name}' already exists." });

        var device = new Device
        {
            DeviceName = name,
            IpAddress = req.IpAddress.Trim(),
            ApplicationName = AndroidApp,
            CurrentVersion = null,
            VersionOnServer = string.Empty,
            AutoStartConfigured = false,
            RecycleRequested = false,
            DateCreated = DateTime.Now,
            DateOfLastPing = null,
        };
        _db.Add(device);
        await _db.SaveChangesAsync();

        return new DeviceDashboardDto(
            device.Id, device.DeviceName, device.ApplicationName, "Android",
            device.CurrentVersion, device.CurrentPatch, device.IpAddress, device.DateOfLastPing, device.RecycleRequested,
            null, null, 0, 0, device.RemoteHost, device.RemotePort, device.RemoteNotes);
    }

    /// <summary>Delete device records (and their push history).</summary>
    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] int[] deviceIds)
    {
        await _db.Set<PushJobDevice>().Where(p => p.DeviceId != null && deviceIds.Contains(p.DeviceId!.Value))
            .ExecuteDeleteAsync();
        await _db.Set<Device>().Where(d => deviceIds.Contains(d.Id)).ExecuteDeleteAsync();
        return NoContent();
    }

    // ---- Packages (release APKs) ----------------------------------------------

    /// <summary>List the available release packages (APKs) on the server.</summary>
    [HttpGet("packages")]
    public ActionResult<List<PackageDto>> Packages()
    {
        var list = Directory.EnumerateFiles(_packagesPath, "*.apk")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new PackageDto(f.Name, ParseVersion(f.Name), f.Length, f.LastWriteTimeUtc))
            .ToList();
        return list;
    }

    /// <summary>
    /// Upload a new release APK. The raw APK bytes are the request body and the name comes
    /// from the query string — simpler/robuster across the Tauri shell and browser than
    /// multipart form posts.
    /// </summary>
    [HttpPost("packages")]
    [RequestSizeLimit(300_000_000)]
    public async Task<ActionResult<PackageDto>> UploadPackage([FromQuery] string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "A .apk fileName query parameter is required." });

        var name = Path.GetFileName(fileName); // no path traversal
        var dest = Path.Combine(_packagesPath, name);
        await using (var stream = System.IO.File.Create(dest))
        {
            await Request.Body.CopyToAsync(stream);
        }
        var info = new FileInfo(dest);
        return new PackageDto(info.Name, ParseVersion(info.Name), info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>Download an APK (the device pulls this to self-install). Anonymous.</summary>
    [HttpGet("apk")]
    [AllowAnonymous]
    public IActionResult DownloadApk([FromQuery] string fileName)
    {
        var name = Path.GetFileName(fileName);
        var path = Path.Combine(_packagesPath, name);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/vnd.android.package-archive", name);
    }

    // ---- Push (queue a release to devices) ------------------------------------

    /// <summary>Queue a release push to the selected devices.</summary>
    [HttpPost("push")]
    public async Task<ActionResult<object>> Push([FromBody] PushRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PackageFileName) || req.DeviceIds.Length == 0)
            return BadRequest(new { message = "PackageFileName and DeviceIds are required." });

        var version = req.Version ?? ParseVersion(req.PackageFileName);
        var now = DateTime.UtcNow;

        var job = new PushJob
        {
            Version = version,
            PackageFileName = req.PackageFileName,
            Status = "Queued",
            CreatedUtc = now,
        };
        _db.Add(job);
        await _db.SaveChangesAsync();

        // Supersede any earlier still-pending rows for these devices, then queue new ones.
        var stale = await _db.Set<PushJobDevice>()
            .Where(p => p.DeviceId != null && req.DeviceIds.Contains(p.DeviceId!.Value) && p.Status == "Pending")
            .ToListAsync();
        foreach (var s in stale) { s.Status = "Superseded"; s.UpdatedUtc = now; }

        var devices = await _db.Set<Device>().Where(d => req.DeviceIds.Contains(d.Id)).ToListAsync();
        foreach (var d in devices)
        {
            _db.Add(new PushJobDevice
            {
                PushJobId = job.Id,
                DeviceId = d.Id,
                DeviceName = d.DeviceName,
                Status = "Pending",
                Detail = "Waiting for device to check in",
                UpdatedUtc = now,
            });
        }
        await _db.SaveChangesAsync();

        return new { jobId = job.Id };
    }

    /// <summary>Live status of a push job (admin polls this).</summary>
    [HttpGet("push/{jobId:int}")]
    public async Task<ActionResult<PushJobDto>> JobStatus(int jobId)
    {
        var job = await _db.Set<PushJob>().AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
        if (job is null) return NotFound();

        var rows = await _db.Set<PushJobDevice>().AsNoTracking()
            .Where(p => p.PushJobId == jobId)
            .OrderBy(p => p.DeviceName)
            .Select(p => new PushJobDeviceDto(p.Id, p.DeviceId, p.DeviceName, p.Status, p.Detail, p.UpdatedUtc))
            .ToListAsync();

        return new PushJobDto(job.Id, job.Version, job.PackageFileName, job.Status,
            job.CreatedUtc, job.CompletedUtc, rows);
    }

    /// <summary>Cancel a whole push job.</summary>
    [HttpPost("push/{jobId:int}/cancel")]
    public async Task<IActionResult> Cancel(int jobId)
    {
        var now = DateTime.UtcNow;
        await _db.Set<PushJobDevice>().Where(p => p.PushJobId == jobId && p.Status == "Pending")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Cancelled").SetProperty(p => p.UpdatedUtc, now));
        await _db.Set<PushJob>().Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "Cancelled").SetProperty(j => j.CompletedUtc, now));
        return NoContent();
    }

    /// <summary>Cancel one device's pending push.</summary>
    [HttpPost("push-device/{rowId:int}/cancel")]
    public async Task<IActionResult> CancelDevice(int rowId)
    {
        var now = DateTime.UtcNow;
        var rows = await _db.Set<PushJobDevice>().Where(p => p.Id == rowId && p.Status == "Pending")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Cancelled").SetProperty(p => p.UpdatedUtc, now));
        return rows == 0 ? NotFound() : NoContent();
    }

    private static string? ParseVersion(string fileName)
    {
        var m = VersionRx.Match(fileName);
        return m.Success ? m.Value : null;
    }

    /// <summary>The caller's IPv4 address as seen by the server.</summary>
    private string? ClientIp()
    {
        var addr = HttpContext.Connection.RemoteIpAddress;
        if (addr is null) return null;
        if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();
        return addr.ToString();
    }
}

// ---- DTOs ----------------------------------------------------------------------

/// <summary>Body the device sends on startup to register itself. CurrentPatch is the Shorebird
/// patch the app is running (null: bare release, or an app too old to say).</summary>
public record RegisterDeviceRequest(
    string DeviceName, string? ApplicationName, string? CurrentVersion, int? CurrentPatch = null);

/// <summary>
/// A registered device, returned after registration. PendingUpdate true → the app shows
/// "Update available" and pulls PendingPackageFileName.
/// </summary>
public record MobileDeviceDto(
    int Id,
    string? DeviceName,
    string? IpAddress,
    string? ApplicationName,
    string? CurrentVersion,
    DateTime DateCreated,
    DateTime? DateOfLastPing,
    bool PendingUpdate = false,
    string? PendingVersion = null,
    string? PendingPackageFileName = null);

/// <summary>A row for the client-facing Devices page — any device on the system.</summary>
public record SystemDeviceDto(
    int Id,
    string? DeviceName,
    string? ApplicationName,
    string? CurrentVersion,
    string? IpAddress,
    DateTime? LastSeen,
    bool Hidden);

/// <summary>Body for hiding/unhiding a device on the Devices overview.</summary>
public record SetHiddenRequest(bool Hidden);

/// <summary>A device row for the admin Devices grid, with its latest push status.</summary>
public record DeviceDashboardDto(
    int Id,
    string? DeviceName,
    string? ApplicationName,
    string? Platform,
    string? CurrentVersion,
    int? CurrentPatch,
    string? IpAddress,
    DateTime? LastSeen,
    bool RecycleRequested,
    string? LatestPushStatus,
    string? LatestPushDetail,
    int LatestPushJobId,
    int LatestPushRowId,
    string? RemoteHost,
    int? RemotePort,
    string? RemoteNotes);

/// <summary>Body for saving a device's remote-control (scrcpy) setup.</summary>
public record DeviceRemoteRequest(string? RemoteHost, int? RemotePort, string? RemoteNotes);

/// <summary>Body for manually adding a device by name + IP.</summary>
public record AddManualDeviceRequest(string DeviceName, string IpAddress);

/// <summary>An uploaded release package (APK) available to push.</summary>
public record PackageDto(string FileName, string? Version, long SizeBytes, DateTime UploadedUtc);

/// <summary>Body for pushing a release to a set of devices.</summary>
public record PushRequest(string PackageFileName, string? Version, int[] DeviceIds);

/// <summary>A push job with its per-device rows, for the admin live grid.</summary>
public record PushJobDto(
    int Id,
    string? Version,
    string? PackageFileName,
    string Status,
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    List<PushJobDeviceDto> Devices);

public record PushJobDeviceDto(
    int RowId,
    int? DeviceId,
    string? DeviceName,
    string Status,
    string? Detail,
    DateTime UpdatedUtc);
