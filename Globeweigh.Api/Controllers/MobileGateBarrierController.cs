using System.Security.Cryptography;
using System.Text;
using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Globeweigh.Api.Controllers;

// Gate & Barrier for the mobile app (Today › Gate & Barrier): the same relay, live weight and
// cameras as TauriAdmin's page, but signed in and for the super-admin and Colin only
// (UserExtensions.GateBarrierUsernames). The web view's api/gate-barrier stays anonymous for the
// site PCs; this is what goes over the tunnel to a phone.
//
// Video: the phone cannot reach go2rtc (a LAN address), so it asks here for a camera's link,
// a short-lived signed path under api/mobile/video that MobileVideoController proxies to go2rtc's
// HLS (H.264 remuxed, no ffmpeg on the box). The signature rides in the PATH so the playlist's
// relative segment URLs carry it too.
[ApiController]
[Authorize]
[Route("api/mobile/gate-barrier")]
public class MobileGateBarrierController : ControllerBase
{
    private readonly GbEvergreenContext _db;
    private readonly RelayService _relay;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly JwtOptions _jwt;

    public MobileGateBarrierController(
        GbEvergreenContext db, RelayService relay, IConfiguration config, IHttpClientFactory httpFactory,
        IOptions<JwtOptions> jwt)
    {
        _db = db;
        _relay = relay;
        _config = config;
        _httpFactory = httpFactory;
        _jwt = jwt.Value;
    }

    private List<CameraDto> Cameras() => _config.GetSection("GateBarrier:Cameras").Get<List<CameraDto>>() ?? new();

    // GET api/mobile/gate-barrier/cameras — name + go2rtc stream of each camera (no IPs).
    [HttpGet("cameras")]
    public async Task<IActionResult> GetCameras(CancellationToken ct)
    {
        if (!await this.MayUseGateBarrierAsync(_db, ct)) return Forbid();
        return Ok(Cameras().Select(c => new { c.Name, c.Stream }));
    }

    // GET api/mobile/gate-barrier/cameras/{stream}/video — the camera's HLS link for the player,
    // good for MobileVideoController.TokenLifetime.
    [HttpGet("cameras/{stream}/video")]
    public async Task<IActionResult> GetVideo(string stream, CancellationToken ct)
    {
        if (!await this.MayUseGateBarrierAsync(_db, ct)) return Forbid();
        if (Cameras().All(c => c.Stream != stream)) return NotFound();
        if (string.IsNullOrWhiteSpace(_config["GateBarrier:VideoBaseUrl"]))
            return StatusCode(503, new { message = "Video is not configured on the server." });
        var token = MobileVideoController.Sign(_jwt.Key, this.GetUserId(), stream);
        return Ok(new { url = $"/api/mobile/video/{token}/stream.m3u8?src={Uri.EscapeDataString(stream)}" });
    }

    [HttpGet("state")]
    public async Task<IActionResult> GetState(CancellationToken ct) =>
        await this.MayUseGateBarrierAsync(_db, ct) ? await GateBarrierRelay.GetState(_config, _httpFactory, _relay, ct) : Forbid();

    [HttpGet("weight")]
    public async Task<IActionResult> GetWeight(CancellationToken ct) =>
        await this.MayUseGateBarrierAsync(_db, ct)
            ? await GateBarrierRelay.ProxyWeight(_config, _httpFactory, "/weighbridge/weight", ct)
            : Forbid();

    [HttpGet("weight2")]
    public async Task<IActionResult> GetWeight2(CancellationToken ct) =>
        await this.MayUseGateBarrierAsync(_db, ct)
            ? await GateBarrierRelay.ProxyWeight(_config, _httpFactory, "/weighbridge2/weight", ct)
            : Forbid();

    [HttpPost("gate")]
    public async Task<IActionResult> SetGate([FromBody] RelayCommandDto cmd, CancellationToken ct) =>
        await this.MayUseGateBarrierAsync(_db, ct)
            ? await GateBarrierRelay.SendCommand(_config, _httpFactory, _relay, "gate", cmd.Open, ct)
            : Forbid();

    [HttpPost("barrier")]
    public async Task<IActionResult> SetBarrier([FromBody] RelayCommandDto cmd, CancellationToken ct) =>
        await this.MayUseGateBarrierAsync(_db, ct)
            ? await GateBarrierRelay.SendCommand(_config, _httpFactory, _relay, "barrier", cmd.Open, ct)
            : Forbid();
}

// The phone's camera video: go2rtc's HLS, proxied. Anonymous at the HTTP level because the
// player (AVPlayer) sends no bearer token; instead the first path segment is a signature over
// (user, stream, expiry) issued by MobileGateBarrierController.GetVideo to a signed-in, listed
// user. Only HLS paths are forwarded, and the playlist only for the stream the token names.
[ApiController]
[AllowAnonymous]
[Route("api/mobile/video/{token}")]
public class MobileVideoController : ControllerBase
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(4);

    private static readonly HashSet<string> AllowedPaths = new(StringComparer.Ordinal)
    {
        "stream.m3u8", "hls/playlist.m3u8", "hls/segment.ts", "hls/segment.m4s", "hls/init.mp4",
    };

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly JwtOptions _jwt;

    public MobileVideoController(IConfiguration config, IHttpClientFactory httpFactory, IOptions<JwtOptions> jwt)
    {
        _config = config;
        _httpFactory = httpFactory;
        _jwt = jwt.Value;
    }

    public static string Sign(string key, int userId, string stream)
    {
        var exp = DateTimeOffset.UtcNow.Add(TokenLifetime).ToUnixTimeSeconds();
        var payload = $"{userId}:{stream}:{exp}";
        return $"{B64(Encoding.UTF8.GetBytes(payload))}.{B64(Mac(key, payload))}";
    }

    // The stream the token was issued for, or null when it is forged or expired.
    private string? Verify(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) return null;
        string payload;
        try { payload = Encoding.UTF8.GetString(Unb64(parts[0])); }
        catch (FormatException) { return null; }
        byte[] sig;
        try { sig = Unb64(parts[1]); }
        catch (FormatException) { return null; }
        if (!CryptographicOperations.FixedTimeEquals(sig, Mac(_jwt.Key, payload))) return null;
        var f = payload.Split(':');
        if (f.Length != 3 || !long.TryParse(f[2], out var exp)) return null;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return null;
        return f[1];
    }

    [HttpGet("{**path}")]
    public async Task<IActionResult> Proxy(string token, string path, CancellationToken ct)
    {
        var stream = Verify(token);
        if (stream == null) return Unauthorized();
        if (!AllowedPaths.Contains(path)) return NotFound();
        if (path == "stream.m3u8" && Request.Query["src"] != stream) return Forbid();

        var baseUrl = _config["GateBarrier:VideoBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return StatusCode(503);

        var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(20);
        HttpResponseMessage resp;
        try
        {
            resp = await http.GetAsync($"{baseUrl.TrimEnd('/')}/api/{path}{Request.QueryString}", ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return StatusCode(502);
        }
        using (resp)
        {
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode);
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            Response.Headers.CacheControl = "no-store";
            return File(bytes, resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream");
        }
    }

    private static byte[] Mac(string key, string payload)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes("mobile-video:" + key));
        return h.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Unb64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
