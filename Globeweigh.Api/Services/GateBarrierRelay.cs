using Microsoft.AspNetCore.Mvc;

namespace Globeweigh.Api.Services;

// The relay and live-weight calls behind Gate/Barrier, shared by the anonymous web view
// (GateBarrierController, api/gate-barrier) and the signed-in mobile one
// (MobileGateBarrierController, api/mobile/gate-barrier), so both always behave the same.
public static class GateBarrierRelay
{
    private const string Disconnected = "{\"connected\":false,\"gateOpen\":false,\"barrierOpen\":false}";

    // Where the relay lives. Preferred: GlobeweighService HTTP (:8765), the always-on on-site
    // service that owns the ONE persistent relay connection (same model as live weight). If unset,
    // the API talks to the relay card directly via the in-process RelayService (dev / fallback).
    private static string? RelayBaseUrl(IConfiguration config)
    {
        var url = config["GateBarrier:RelayServiceUrl"];
        if (string.IsNullOrWhiteSpace(url)) url = config["GateBarrier:WeighbridgeServiceUrl"];
        return string.IsNullOrWhiteSpace(url) ? null : url.TrimEnd('/');
    }

    /// <summary>Current gate/barrier output states (proxied to GlobeweighService when configured).</summary>
    public static async Task<IActionResult> GetState(
        IConfiguration config, IHttpClientFactory httpFactory, RelayService relay, CancellationToken ct)
    {
        var baseUrl = RelayBaseUrl(config);
        if (baseUrl == null) return new OkObjectResult(await relay.GetStateAsync(ct));
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(4);
            var resp = await http.GetAsync(baseUrl + "/relay/state", ct);
            if (!resp.IsSuccessStatusCode) return Json(Disconnected);
            return Json(await resp.Content.ReadAsStringAsync(ct));
        }
        catch
        {
            return Json(Disconnected);
        }
    }

    /// <summary>
    /// Live weight, proxied from the GlobeweighService on :8765 (GateBarrier:WeighbridgeServiceUrl)
    /// — same source as the desktop Gate/Barrier view. path: /weighbridge/weight or /weighbridge2/weight.
    /// </summary>
    public static async Task<IActionResult> ProxyWeight(
        IConfiguration config, IHttpClientFactory httpFactory, string path, CancellationToken ct)
    {
        var baseUrl = config["GateBarrier:WeighbridgeServiceUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return Json("{\"isConnected\":false}");
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(3);
            return Json(await http.GetStringAsync(baseUrl.TrimEnd('/') + path, ct));
        }
        catch
        {
            // Service unreachable (Tailscale down, service stopped) — report disconnected, don't 500.
            return Json("{\"isConnected\":false}");
        }
    }

    /// <summary>Open/close the gate (relay 1) or the barrier (relay 2).</summary>
    public static async Task<IActionResult> SendCommand(
        IConfiguration config, IHttpClientFactory httpFactory, RelayService relay,
        string which, bool open, CancellationToken ct)
    {
        var baseUrl = RelayBaseUrl(config);
        if (baseUrl == null)
        {
            try
            {
                return new OkObjectResult(which == "gate"
                    ? await relay.SetGateAsync(open, ct)
                    : await relay.SetBarrierAsync(open, ct));
            }
            catch (InvalidOperationException ex)
            {
                return new ObjectResult(new { message = ex.Message }) { StatusCode = 503 };
            }
        }
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var url = $"{baseUrl}/relay/{which}?open={(open ? "true" : "false")}";
            var resp = await http.PostAsync(url, null, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new ObjectResult(new { message = "Relay service unavailable." }) { StatusCode = 503 };
            return Json(json);
        }
        catch
        {
            return new ObjectResult(new { message = "Relay service unreachable." }) { StatusCode = 503 };
        }
    }

    // Whether a request came in through the Cloudflare tunnel (evergreen.specdigital.com):
    // cloudflared stamps every request it forwards with these, and a caller on the internet
    // cannot strip them. Site-network callers (site PCs, touch kiosks, scanners) talk to
    // evergreen:5099 directly and never carry them.
    public static bool ViaTunnel(HttpRequest request) =>
        request.Headers.ContainsKey("Cf-Connecting-IP") || request.Headers.ContainsKey("Cf-Ray");

    private static ContentResult Json(string json) => new() { Content = json, ContentType = "application/json" };
}
