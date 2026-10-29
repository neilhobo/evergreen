using System.Net;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace Globeweigh.Api.Services;

/// <summary>
/// The weighbridge door intercoms (Dahua VTO2201F). Holds one long-lived connection to each unit's
/// event stream (<c>/cgi-bin/eventManager.cgi?action=attach</c>, HTTP digest auth, multipart
/// "--myboundary" parts) and keeps the latest call state for the admin app to poll.
///
/// Units come from <c>Intercom:Units</c> (Name, Host, Stream, and optionally their own Username /
/// Password, otherwise <c>Intercom:Username</c> / <c>Intercom:Password</c>; VideoBaseUrl when its camera
/// is served by a different go2rtc; Listen=false for a video-only unit whose event feed this API
/// cannot reach). The older single-unit
/// <c>Intercom:Host</c> + <c>Intercom:Stream</c> still works as one unit called "Weighbridge". The
/// list is read once at start-up — recycle the app pool after editing it.
///
/// A bell press arrives as <c>CallNoAnswered</c> (Start) + <c>Invite</c> (Pulse) carrying a
/// CallID; the call ends with <c>Hangup</c>/<c>HangupPhone</c>/<c>IgnoreInvite</c>. The unit's
/// own clock is not trusted (it boots at 1 Jan 2000), so every time here is the API's.
///
/// Off when no unit is configured, so the prod API does nothing until the units are on site.
/// </summary>
public class IntercomService : BackgroundService
{
    private const int MaxEvents = 50;
    private const int MaxRings = 10;
    // The unit is asked for a heartbeat every 30s; this long with nothing means the link is dead.
    private static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(75);
    // A ring whose hang-up we somehow missed stops showing as ringing after this.
    private static readonly TimeSpan RingLimit = TimeSpan.FromSeconds(90);
    // An answered call nobody hung up (app closed mid-call) is let go after this.
    private static readonly TimeSpan CallLimit = TimeSpan.FromMinutes(15);

    private readonly IConfiguration _config;
    private readonly ILogger<IntercomService> _log;
    private readonly object _lock = new();
    private readonly List<Unit> _units;

    private readonly LinkedList<IntercomEventDto> _events = new();
    private readonly LinkedList<IntercomRing> _rings = new();
    private int _nextRingId = 1;

    public IntercomService(IConfiguration config, ILogger<IntercomService> log)
    {
        _config = config;
        _log = log;
        _units = ReadUnits();
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private List<Unit> ReadUnits()
    {
        var user = _config["Intercom:Username"] ?? "admin";
        var pass = _config["Intercom:Password"] ?? "";
        var units = new List<Unit>();
        foreach (var s in _config.GetSection("Intercom:Units").GetChildren())
        {
            var host = Blank(s["Host"]);
            if (host == null) continue;
            units.Add(new Unit
            {
                Name = Blank(s["Name"]) ?? host,
                Host = host,
                Stream = Blank(s["Stream"]) ?? $"intercom{units.Count + 1}",
                User = Blank(s["Username"]) ?? user,
                Password = s["Password"] ?? pass,
                VideoBaseUrl = Blank(s["VideoBaseUrl"]),
                // false = video only: the unit is shown but its event feed is not connected to (a dev PC
                // that can see the unit's camera through evergreen's go2rtc but cannot reach the unit).
                Listen = !string.Equals(s["Listen"], "false", StringComparison.OrdinalIgnoreCase),
            });
        }
        var single = Blank(_config["Intercom:Host"]);
        if (units.Count == 0 && single != null)
            units.Add(new Unit
            {
                Name = "Weighbridge",
                Host = single,
                Stream = Blank(_config["Intercom:Stream"]) ?? "intercom",
                User = user,
                Password = pass,
            });
        return units;
    }

    private string? VideoBaseUrl => Blank(_config["Intercom:VideoBaseUrl"]) ?? Blank(_config["GateBarrier:VideoBaseUrl"]);

    private static HttpClient CreateClient(Unit u, TimeSpan timeout)
    {
        // HttpClientHandler answers the unit's Digest challenge by itself given plain credentials.
        var handler = new HttpClientHandler { Credentials = new NetworkCredential(u.User, u.Password) };
        return new HttpClient(handler) { Timeout = timeout };
    }

    // The unit's call in progress, after letting go of one that has outlived its limit. Caller holds _lock.
    private static IntercomRing? CurrentRing(Unit u)
    {
        var active = u.ActiveRing;
        if (active == null) return null;
        var limit = active.AnsweredUtc == null ? RingLimit : CallLimit;
        var since = active.AnsweredUtc ?? active.StartedUtc;
        if (DateTime.UtcNow - since <= limit) return active;
        active.EndedUtc ??= since + limit;
        u.ActiveRing = null;
        return null;
    }

    /// <summary>
    /// The call every admin app's overlay shows. With both bridge units ringing at once, the one that
    /// rang first leads; the other shows once it has been answered or has stopped.
    /// </summary>
    public IntercomCallDto GetCall()
    {
        lock (_lock)
        {
            var active = _units.Select(CurrentRing).Where(r => r != null).Select(r => r!).ToList();
            var lead = active.Where(r => r.AnsweredUtc == null).OrderBy(r => r.StartedUtc).FirstOrDefault()
                       ?? active.OrderByDescending(r => r.AnsweredUtc).FirstOrDefault();
            return new IntercomCallDto
            {
                Enabled = _units.Count > 0,
                Ring = lead?.ToDto(),
                Stream = lead?.Unit.Stream ?? _units.FirstOrDefault()?.Stream ?? "intercom",
                VideoBaseUrl = lead?.Unit.VideoBaseUrl ?? VideoBaseUrl,
            };
        }
    }

    /// <summary>Takes the call for one user. The first to answer gets it; for anyone after, null.</summary>
    public IntercomRingDto? Answer(int ringId, string answeredBy)
    {
        lock (_lock)
        {
            var ring = _rings.FirstOrDefault(r => r.Id == ringId);
            if (ring == null || CurrentRing(ring.Unit) != ring || ring.AnsweredUtc != null) return null;
            ring.AnsweredBy = answeredBy;
            ring.AnsweredUtc = DateTime.UtcNow;
            _log.LogInformation("Intercom call {RingId} at {Unit} answered by {User}", ringId, ring.Unit.Name, answeredBy);
            // The conversation runs over RTSP talk-back, which the unit does not count as answering:
            // left alone it rings on and, at its ring limit, announces "the call was not answered"
            // mid-conversation. Ending its own call stops both; talk-back carries on regardless.
            var unit = ring.Unit;
            _ = Task.Run(() => EndUnitCallAsync(unit));
            return ring.ToDto();
        }
    }

    /// <summary>
    /// Ends the call the unit placed when the bell was pressed, through its JSON-RPC API
    /// (/RPC2_Login then /RPC2 — the protocol its own web UI uses; the CGI VideoTalkPeer
    /// hangup rejects every form tried). VideoTalkPhone.endCall on a fresh phone instance ends
    /// whatever call is on the unit's one line. Best effort: a failure only means the ring runs on.
    /// </summary>
    private async Task EndUnitCallAsync(Unit u)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var id = 0;
            string? session = null;
            async Task<JsonElement> Rpc(string path, string method, object? prms, object? obj = null)
            {
                var body = new Dictionary<string, object?> { ["method"] = method, ["params"] = prms, ["id"] = ++id };
                if (session != null) body["session"] = session;
                if (obj != null) body["object"] = obj;
                var resp = await http.PostAsync($"http://{u.Host}{path}",
                    new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
                return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
            }
            static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s)));

            // Challenge, then the answer: MD5(user:random:MD5(user:realm:password)), upper-case hex.
            var challenge = await Rpc("/RPC2_Login", "global.login",
                new { userName = u.User, password = "", clientType = "Web3.0", loginType = "Direct" });
            session = challenge.GetProperty("session").ToString();
            var p = challenge.GetProperty("params");
            var realm = p.GetProperty("realm").GetString();
            var random = p.GetProperty("random").GetString();
            var encryption = p.GetProperty("encryption").GetString();
            var login = await Rpc("/RPC2_Login", "global.login", new
            {
                userName = u.User,
                password = Md5($"{u.User}:{random}:{Md5($"{u.User}:{realm}:{u.Password}")}"),
                clientType = "Web3.0",
                loginType = "Direct",
                authorityType = encryption,
            });
            if (!login.TryGetProperty("result", out var ok) || !ok.GetBoolean())
            {
                _log.LogWarning("Intercom {Unit} RPC login failed: {Reply}", u.Name, login.ToString());
                return;
            }
            session = login.GetProperty("session").ToString();

            var phone = (await Rpc("/RPC2", "VideoTalkPhone.factory.instance", null)).GetProperty("result");
            var ended = await Rpc("/RPC2", "VideoTalkPhone.endCall", null, phone);
            await Rpc("/RPC2", "VideoTalkPhone.destroy", null, phone);
            await Rpc("/RPC2", "global.logout", null);
            _log.LogInformation("Intercom {Unit} call ended: {Reply}", u.Name, ended.GetProperty("result").ToString());
        }
        catch (Exception ex)
        {
            _log.LogWarning("Could not end intercom {Unit}'s own call: {Message}", u.Name, ex.Message);
        }
    }

    /// <summary>Ends the call on our side. The unit's own call may already have timed out; talk-back
    /// runs over RTSP, not the call, so the conversation lasts until this.</summary>
    public void Hangup(int ringId)
    {
        lock (_lock)
        {
            var ring = _rings.FirstOrDefault(r => r.Id == ringId);
            if (ring == null || ring.Unit.ActiveRing != ring) return;
            ring.EndedUtc ??= DateTime.UtcNow;
            ring.Unit.ActiveRing = null;
        }
    }

    /// <summary>The ring as it stands now, answered or not — for an app that lost a race to answer.</summary>
    public IntercomRingDto? GetRing(int ringId)
    {
        lock (_lock) return _rings.FirstOrDefault(r => r.Id == ringId)?.ToDto();
    }

    public IntercomStatusDto GetStatus()
    {
        lock (_lock)
        {
            var units = _units.Select(u =>
            {
                var active = CurrentRing(u);
                return new IntercomUnitDto
                {
                    Name = u.Name,
                    Host = u.Host,
                    Stream = u.Stream,
                    VideoBaseUrl = u.VideoBaseUrl ?? VideoBaseUrl,
                    Listening = u.Listen,
                    Connected = u.Connected,
                    LastError = u.LastError,
                    LastHeardUtc = u.LastHeardUtc,
                    Ringing = active != null && active.AnsweredUtc == null,
                    ActiveRing = active?.ToDto(),
                };
            }).ToList();
            var first = units.FirstOrDefault();
            return new IntercomStatusDto
            {
                Enabled = units.Count > 0,
                Units = units,
                // Single-unit fields, kept for older admin builds that read them.
                Host = first?.Host,
                Connected = units.Any(u => u.Listening) && units.Where(u => u.Listening).All(u => u.Connected),
                LastError = units.Select(u => u.LastError).FirstOrDefault(e => e != null),
                LastHeardUtc = first?.LastHeardUtc,
                Stream = first?.Stream ?? "intercom",
                VideoBaseUrl = VideoBaseUrl,
                Ringing = units.Any(u => u.Ringing),
                ActiveRing = units.Select(u => u.ActiveRing).FirstOrDefault(r => r != null),
                Rings = _rings.Select(r => r.ToDto()).ToList(),
                Events = _events.ToList(),
            };
        }
    }

    public byte[]? GetRingSnapshot(int ringId)
    {
        lock (_lock) return _rings.FirstOrDefault(r => r.Id == ringId)?.Snapshot;
    }

    /// <summary>A live still from a unit's camera (the first unit when none is named), or null.</summary>
    public async Task<byte[]?> TakeSnapshotAsync(string? unitName, CancellationToken ct)
    {
        var u = unitName == null ? _units.FirstOrDefault()
            : _units.FirstOrDefault(x => string.Equals(x.Name, unitName, StringComparison.OrdinalIgnoreCase));
        return u == null ? null : await TakeSnapshotAsync(u, ct);
    }

    private async Task<byte[]?> TakeSnapshotAsync(Unit u, CancellationToken ct)
    {
        try
        {
            using var http = CreateClient(u, TimeSpan.FromSeconds(6));
            var resp = await http.GetAsync($"http://{u.Host}/cgi-bin/snapshot.cgi?channel=1", ct);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsByteArrayAsync(ct) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.LogWarning("Intercom {Unit} snapshot failed: {Message}", u.Name, ex.Message);
            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_units.Count == 0) return;
        await Task.WhenAll(_units.Where(u => u.Listen).Select(u => RunUnitAsync(u, stoppingToken)));
    }

    private async Task RunUnitAsync(Unit u, CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(u, stoppingToken);
                delay = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                lock (_lock) { u.Connected = false; u.LastError = ex.Message; }
                _log.LogWarning("Intercom {Unit} event stream at {Host} dropped: {Message}", u.Name, u.Host, ex.Message);
            }
            try { await Task.Delay(delay, stoppingToken); } catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }

    private async Task ListenAsync(Unit u, CancellationToken stoppingToken)
    {
        using var http = CreateClient(u, Timeout.InfiniteTimeSpan);
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        watchdog.CancelAfter(SilenceLimit);

        var url = $"http://{u.Host}/cgi-bin/eventManager.cgi?action=attach&codes=%5BAll%5D&heartbeat=30";
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, watchdog.Token);
        resp.EnsureSuccessStatusCode();

        lock (_lock) { u.Connected = true; u.LastError = null; u.LastHeardUtc = DateTime.UtcNow; }
        _log.LogInformation("Intercom {Unit} event stream at {Host} connected", u.Name, u.Host);

        await using var body = await resp.Content.ReadAsStreamAsync(watchdog.Token);
        using var reader = new StreamReader(body, Encoding.UTF8);

        // Each part is "--myboundary", headers, a blank line, then the body. The body is either
        // "Heartbeat" or "Code=X;action=Y;index=N;data={ ...json over several lines... }".
        var part = new StringBuilder();
        var inBody = false;
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(watchdog.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                throw new TimeoutException($"No heartbeat from the intercom for {SilenceLimit.TotalSeconds:0}s");
            }
            if (line == null) throw new IOException("The intercom closed the event stream");

            watchdog.CancelAfter(SilenceLimit);
            lock (_lock) u.LastHeardUtc = DateTime.UtcNow;

            if (line.StartsWith("--myboundary"))
            {
                if (part.Length > 0) await HandlePartAsync(u, part.ToString().Trim(), stoppingToken);
                part.Clear();
                inBody = false;
            }
            else if (!inBody)
            {
                if (line.Length == 0) inBody = true;
            }
            else
            {
                part.AppendLine(line);
            }
        }
    }

    private async Task HandlePartAsync(Unit u, string text, CancellationToken ct)
    {
        if (!text.StartsWith("Code=")) return; // Heartbeat

        string code = "", action = "", data = "";
        var dataAt = text.IndexOf(";data=", StringComparison.Ordinal);
        var head = dataAt >= 0 ? text[..dataAt] : text;
        if (dataAt >= 0) data = text[(dataAt + 6)..].Trim();
        foreach (var kv in head.Split(';'))
        {
            var eq = kv.IndexOf('=');
            if (eq < 0) continue;
            var key = kv[..eq];
            var value = kv[(eq + 1)..];
            if (key == "Code") code = value;
            else if (key == "action") action = value;
        }

        // Motion fires constantly with someone in view; keep it out of the log.
        if (code == "VideoMotion") return;

        var callId = ReadJsonValue(data, "CallID");
        IntercomRing? newRing = null;
        lock (_lock)
        {
            _events.AddFirst(new IntercomEventDto
            {
                AtUtc = DateTime.UtcNow,
                Unit = u.Name,
                Code = code,
                Action = action,
                CallId = callId,
                UserId = ReadJsonValue(data, "UserID"),
            });
            while (_events.Count > MaxEvents) _events.RemoveLast();

            var current = CurrentRing(u);
            // A press sends CallNoAnswered/Start at once and Invite with the same CallID — but Invite
            // can trail by 10s+ while the unit tries its SIP call, so whichever lands first starts the ring.
            if (code == "Invite" || (code == "CallNoAnswered" && action == "Start"))
            {
                // A repeat with the same CallID is the same ring.
                // A driver pressing again while someone is already talking to them is not a new call.
                if (current == null || (current.AnsweredUtc == null && current.CallId != callId))
                {
                    if (current != null) current.EndedUtc ??= DateTime.UtcNow;
                    newRing = new IntercomRing { Id = _nextRingId++, Unit = u, CallId = callId, StartedUtc = DateTime.UtcNow };
                    u.ActiveRing = newRing;
                    _rings.AddFirst(newRing);
                    while (_rings.Count > MaxRings) _rings.RemoveLast();
                }
            }
            // The unit giving up on its call ends an unanswered ring. An answered one carries on:
            // the conversation runs over RTSP talk-back and lasts until the app hangs up.
            else if (IsCallEnd(code, action) && current != null && current.AnsweredUtc == null
                     && (callId == null || current.CallId == null || callId == current.CallId))
            {
                current.EndedUtc = DateTime.UtcNow;
                u.ActiveRing = null;
            }
        }

        if (newRing != null)
        {
            _log.LogInformation("Intercom {Unit} bell pressed (call {CallId})", u.Name, callId);
            // Who pressed it, as they were at the time: kept with the ring for the log.
            var jpeg = await TakeSnapshotAsync(u, ct);
            lock (_lock) newRing.Snapshot = jpeg;
        }
    }

    private static bool IsCallEnd(string code, string action) =>
        code is "Hangup" or "HungupPhone" or "HangupPhone" or "IgnoreInvite"
        || (code == "CallNoAnswered" && action == "Stop");

    // The data block is small, flat JSON; one value is all we ever need from it.
    private static string? ReadJsonValue(string json, string key)
    {
        var at = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (at < 0) return null;
        var colon = json.IndexOf(':', at);
        if (colon < 0) return null;
        var end = json.IndexOfAny(new[] { ',', '\n', '}' }, colon + 1);
        var raw = (end < 0 ? json[(colon + 1)..] : json[(colon + 1)..end]).Trim().Trim('"');
        return raw.Length == 0 ? null : raw;
    }

    private class Unit
    {
        public string Name { get; init; } = "";
        public string Host { get; init; } = "";
        public string Stream { get; init; } = "";
        public string User { get; init; } = "admin";
        public string Password { get; init; } = "";
        public string? VideoBaseUrl { get; init; }   // this unit's go2rtc, when not the shared one
        public bool Listen { get; init; } = true;
        // Guarded by the service's _lock.
        public bool Connected { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastHeardUtc { get; set; }
        public IntercomRing? ActiveRing { get; set; }
    }

    private class IntercomRing
    {
        public int Id { get; init; }
        public Unit Unit { get; init; } = null!;
        public string? CallId { get; init; }
        public DateTime StartedUtc { get; init; }
        public DateTime? EndedUtc { get; set; }
        public string? AnsweredBy { get; set; }
        public DateTime? AnsweredUtc { get; set; }
        public byte[]? Snapshot { get; set; }

        public IntercomRingDto ToDto() => new()
        {
            Id = Id,
            Unit = Unit.Name,
            Stream = Unit.Stream,
            CallId = CallId,
            StartedUtc = StartedUtc,
            EndedUtc = EndedUtc,
            AnsweredBy = AnsweredBy,
            AnsweredUtc = AnsweredUtc,
            HasSnapshot = Snapshot != null,
        };
    }
}

public class IntercomStatusDto
{
    public bool Enabled { get; set; }
    public List<IntercomUnitDto> Units { get; set; } = new();
    public string? Host { get; set; }
    public bool Connected { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastHeardUtc { get; set; }
    public string Stream { get; set; } = "intercom";   // go2rtc stream name
    public string? VideoBaseUrl { get; set; }           // go2rtc base, e.g. http://evergreen:1984
    public bool Ringing { get; set; }
    public IntercomRingDto? ActiveRing { get; set; }
    public List<IntercomRingDto> Rings { get; set; } = new();
    public List<IntercomEventDto> Events { get; set; } = new();
}

public class IntercomUnitDto
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string Stream { get; set; } = "";
    public string? VideoBaseUrl { get; set; }           // go2rtc that serves this unit's camera
    public bool Listening { get; set; }                 // false = video only, no event feed (no rings)
    public bool Connected { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastHeardUtc { get; set; }
    public bool Ringing { get; set; }
    public IntercomRingDto? ActiveRing { get; set; }
}

public class IntercomRingDto
{
    public int Id { get; set; }
    public string Unit { get; set; } = "";              // which intercom rang, e.g. "Bridge 1 – In"
    public string Stream { get; set; } = "";            // its go2rtc stream
    public string? CallId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public string? AnsweredBy { get; set; }
    public DateTime? AnsweredUtc { get; set; }
    public bool HasSnapshot { get; set; }
}

public class IntercomCallDto
{
    public bool Enabled { get; set; }
    public IntercomRingDto? Ring { get; set; }      // null = no call; AnsweredBy set = someone has it
    public string Stream { get; set; } = "intercom";
    public string? VideoBaseUrl { get; set; }
}

public class IntercomEventDto
{
    public DateTime AtUtc { get; set; }
    public string Unit { get; set; } = "";
    public string Code { get; set; } = "";
    public string Action { get; set; } = "";
    public string? CallId { get; set; }
    public string? UserId { get; set; }
}
