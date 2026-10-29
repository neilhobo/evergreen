using Globeweigh.Api.Data;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// Phase 1 experiment: anonymous so the web view works without a login flow.
// Tighten to [Authorize] once the web UI carries a JWT.
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

    // Where the relay lives. Preferred: GlobeweighService HTTP (:8765), the always-on on-site
    // service that owns the ONE persistent relay connection (same model as live weight). If unset,
    // the API talks to the relay card directly via the in-process RelayService (dev / fallback).
    private string? RelayBaseUrl()
    {
        var url = _config["GateBarrier:RelayServiceUrl"];
        if (string.IsNullOrWhiteSpace(url)) url = _config["GateBarrier:WeighbridgeServiceUrl"];
        return string.IsNullOrWhiteSpace(url) ? null : url.TrimEnd('/');
    }

    private const string Disconnected = "{\"connected\":false,\"gateOpen\":false,\"barrierOpen\":false}";

    /// <summary>Current gate/barrier output states (proxied to GlobeweighService when configured).</summary>
    [HttpGet("state")]
    public async Task<IActionResult> GetState(CancellationToken ct)
    {
        var baseUrl = RelayBaseUrl();
        if (baseUrl == null)
        {
            var local = await _relay.GetStateAsync(ct);
            return Ok(local);
        }
        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(4);
            var resp = await http.GetAsync(baseUrl + "/relay/state", ct);
            if (!resp.IsSuccessStatusCode) return Content(Disconnected, "application/json");
            return Content(await resp.Content.ReadAsStringAsync(ct), "application/json");
        }
        catch
        {
            return Content(Disconnected, "application/json");
        }
    }

    /// <summary>
    /// Live weight, proxied from the GlobeweighService on :8765. Always the production
    /// service (configured via GateBarrier:WeighbridgeServiceUrl, default http://evergreen:8765/,
    /// reachable over Tailscale) — same source as the desktop Gate/Barrier view, regardless of
    /// whether the API itself is the local or prod instance.
    /// </summary>
    [HttpGet("weight")]
    public Task<IActionResult> GetWeight(CancellationToken ct)
        => ProxyWeight("/weighbridge/weight", ct);

    /// <summary>Live weight from the second weighbridge (GlobeweighService /weighbridge2/weight).</summary>
    [HttpGet("weight2")]
    public Task<IActionResult> GetWeight2(CancellationToken ct)
        => ProxyWeight("/weighbridge2/weight", ct);

    private async Task<IActionResult> ProxyWeight(string path, CancellationToken ct)
    {
        var baseUrl = _config["GateBarrier:WeighbridgeServiceUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Content("{\"isConnected\":false}", "application/json");

        var url = baseUrl.TrimEnd('/') + path;
        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(3);
            var json = await http.GetStringAsync(url, ct);
            return Content(json, "application/json");
        }
        catch
        {
            // Service unreachable (Tailscale down, service stopped) — report disconnected, don't 500.
            return Content("{\"isConnected\":false}", "application/json");
        }
    }

    /// <summary>Open/close the gate (relay 1).</summary>
    [HttpPost("gate")]
    public Task<IActionResult> SetGate([FromBody] RelayCommandDto cmd, CancellationToken ct)
        => SendRelayCommand("gate", () => _relay.SetGateAsync(cmd.Open, ct), cmd.Open, ct);

    /// <summary>Open/close the barrier (relay 2).</summary>
    [HttpPost("barrier")]
    public Task<IActionResult> SetBarrier([FromBody] RelayCommandDto cmd, CancellationToken ct)
        => SendRelayCommand("barrier", () => _relay.SetBarrierAsync(cmd.Open, ct), cmd.Open, ct);

    private async Task<IActionResult> SendRelayCommand(
        string which, Func<Task<RelayService.RelayState>> local, bool open, CancellationToken ct)
    {
        var baseUrl = RelayBaseUrl();
        if (baseUrl == null)
        {
            try { return Ok(await local()); }
            catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
        }
        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var url = $"{baseUrl}/relay/{which}?open={(open ? "true" : "false")}";
            var resp = await http.PostAsync(url, null, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return StatusCode(503, new { message = "Relay service unavailable." });
            return Content(json, "application/json");
        }
        catch
        {
            return StatusCode(503, new { message = "Relay service unreachable." });
        }
    }
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
