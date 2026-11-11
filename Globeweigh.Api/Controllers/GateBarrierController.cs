using Globeweigh.Api.Data;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Anonymous so the site's kiosks and scanners work without a login. Open/close coming
// through the Cloudflare tunnel does require one (RefusedOffSite below).
[AllowAnonymous]
[ApiController]
[Route("api/gate-barrier")]
public class GateBarrierController : ControllerBase
{
    private readonly GbEvergreenContext _db;
    private readonly RelayService _relay;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;

    public GateBarrierController(GbEvergreenContext db, RelayService relay, IConfiguration config, IHttpClientFactory httpFactory)
    {
        _db = db;
        _relay = relay;
        _config = config;
        _httpFactory = httpFactory;
    }

    /// <summary>Camera list + relay config for the web view (config-driven, no hardcoding in the UI).</summary>
    [HttpGet("config")]
    public async Task<ActionResult<GateBarrierConfigDto>> GetConfig(CancellationToken ct)
    {
        var s = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        var cameras = _config.GetSection("GateBarrier:Cameras").Get<List<CameraDto>>() ?? new();
        var remoteScreens = _config.GetSection("GateBarrier:RemoteScreens").Get<List<RemoteScreenDto>>() ?? new();

        return new GateBarrierConfigDto
        {
            GateEnabled        = s?.GateEnabled ?? false,
            GateRelayIpAddress = s?.GateRelayIpAddress,
            GateRelayPortNo    = s?.GateRelayPortNo ?? 0,
            VideoBaseUrl       = _config["GateBarrier:VideoBaseUrl"],
            Cameras            = cameras,
            RemoteScreens      = remoteScreens
        };
    }

    // The relay and weight calls live in Services/GateBarrierRelay, shared with the mobile
    // app's signed-in MobileGateBarrierController.

    /// <summary>Current gate/barrier output states (proxied to GlobeweighService when configured).</summary>
    [HttpGet("state")]
    public Task<IActionResult> GetState(CancellationToken ct)
        => GateBarrierRelay.GetState(_config, _httpFactory, _relay, ct);

    /// <summary>
    /// Live weight, proxied from the GlobeweighService on :8765. Always the production
    /// service (configured via GateBarrier:WeighbridgeServiceUrl, default http://evergreen:8765/,
    /// reachable over Tailscale) — same source as the desktop Gate/Barrier view, regardless of
    /// whether the API itself is the local or prod instance.
    /// </summary>
    [HttpGet("weight")]
    public Task<IActionResult> GetWeight(CancellationToken ct)
        => GateBarrierRelay.ProxyWeight(_config, _httpFactory, "/weighbridge/weight", ct);

    /// <summary>Live weight from the second weighbridge (GlobeweighService /weighbridge2/weight).</summary>
    [HttpGet("weight2")]
    public Task<IActionResult> GetWeight2(CancellationToken ct)
        => GateBarrierRelay.ProxyWeight(_config, _httpFactory, "/weighbridge2/weight", ct);

    // Open/close from OFF-SITE (through the Cloudflare tunnel) needs a signed-in user: the
    // remote TauriAdmin already sends its JWT, a stranger on the internet has none. On the site
    // network nothing changes — the touch kiosks raise the barrier without a login. Reading
    // weights and state stays open everywhere (remote users must see the live weights).
    // Decided 2026-11-05 after finding the gate could be opened from the internet.
    private bool RefusedOffSite() =>
        GateBarrierRelay.ViaTunnel(Request) && User.Identity?.IsAuthenticated != true;

    /// <summary>Open/close the gate (relay 1).</summary>
    [HttpPost("gate")]
    public Task<IActionResult> SetGate([FromBody] RelayCommandDto cmd, CancellationToken ct)
        => RefusedOffSite()
            ? Task.FromResult<IActionResult>(Unauthorized(new { message = "Sign in to open or close the gate." }))
            : GateBarrierRelay.SendCommand(_config, _httpFactory, _relay, "gate", cmd.Open, ct);

    /// <summary>Open/close the barrier (relay 2).</summary>
    [HttpPost("barrier")]
    public Task<IActionResult> SetBarrier([FromBody] RelayCommandDto cmd, CancellationToken ct)
        => RefusedOffSite()
            ? Task.FromResult<IActionResult>(Unauthorized(new { message = "Sign in to open or close the barrier." }))
            : GateBarrierRelay.SendCommand(_config, _httpFactory, _relay, "barrier", cmd.Open, ct);
}

public class RelayCommandDto
{
    public bool Open { get; set; }
}

public class CameraDto
{
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Stream { get; set; } = "";   // go2rtc stream name
}

// A remote touchscreen reachable via a websockify proxy that bridges WebSocket→VNC.
// Embedded in the UI with noVNC (no client install). WsUrl points at the proxy, e.g.
// ws://evergreen:6080 → websockify → TOUCH-IN:5900.
public class RemoteScreenDto
{
    public string Name { get; set; } = "";
    public string WsUrl { get; set; } = "";
    // The camera for this screen's lane (go2rtc stream, e.g. the weigh-in intercom), shown first
    // when "Show Camera" splits the remote-screen dialog. Optional: without it the dialog shows
    // the first Gate/Barrier camera, as before.
    public string? Stream { get; set; }
    public string? CameraName { get; set; }
}

public class GateBarrierConfigDto
{
    public bool GateEnabled { get; set; }
    public string? GateRelayIpAddress { get; set; }
    public int GateRelayPortNo { get; set; }
    public string? VideoBaseUrl { get; set; }  // go2rtc base, e.g. http://evergreen:1984
    public List<CameraDto> Cameras { get; set; } = new();
    public List<RemoteScreenDto> RemoteScreens { get; set; } = new();
}
