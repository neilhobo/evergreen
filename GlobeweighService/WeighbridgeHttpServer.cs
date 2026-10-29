using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GlobeweighService
{
    public class WeighbridgeHttpServer
    {
        private readonly int _port;

        // Allow-list of remote screens reachable via the WebSocket→VNC bridge (/vnc).
        // Configured in App.config: VncTargets = "TOUCH-IN=192.168.250.177:5900;TOUCH-OUT=192.168.250.178:5900".
        private sealed class VncTarget { public string Host; public int Port; }
        private static readonly Dictionary<string, VncTarget> VncTargets = LoadVncTargets();

        public WeighbridgeHttpServer(int port)
        {
            _port = port;
        }

        private static Dictionary<string, VncTarget> LoadVncTargets()
        {
            var map = new Dictionary<string, VncTarget>(StringComparer.OrdinalIgnoreCase);
            var raw = ConfigurationManager.AppSettings["VncTargets"];
            if (string.IsNullOrWhiteSpace(raw)) return map;

            foreach (var entry in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                var name = entry.Substring(0, eq).Trim();
                var hostPort = entry.Substring(eq + 1).Trim();
                var colon = hostPort.LastIndexOf(':');
                string host;
                int port = 5900;
                if (colon > 0)
                {
                    host = hostPort.Substring(0, colon);
                    int.TryParse(hostPort.Substring(colon + 1), out port);
                }
                else
                {
                    host = hostPort;
                }
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(host))
                    map[name] = new VncTarget { Host = host, Port = port };
            }
            return map;
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            Log($"HTTP server starting on port {_port}");

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{_port}/");

            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                Log($"HTTP listener failed to start ({ex.ErrorCode}): {ex.Message}. " +
                    "Likely missing URL ACL. Run as admin once: " +
                    $"netsh http add urlacl url=http://+:{_port}/ user=Everyone");
                return;
            }
            catch (Exception ex)
            {
                Log("HTTP listener failed to start: " + ex);
                return;
            }

            Log("HTTP server listening");

            using (stoppingToken.Register(() =>
            {
                try { listener.Stop(); } catch { }
                try { listener.Close(); } catch { }
            }))
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await listener.GetContextAsync();
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (HttpListenerException) { break; }
                    catch (Exception ex)
                    {
                        Log("GetContext error: " + ex.Message);
                        continue;
                    }

                    var _ = Task.Run(async () =>
                    {
                        if (ctx.Request.IsWebSocketRequest)
                            await HandleWebSocketAsync(ctx);
                        else
                            HandleRequest(ctx);
                    });
                }
            }

            Log("HTTP server stopped");
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";

                var path = ctx.Request.Url.AbsolutePath ?? "/";

                if (path.Equals("/weighbridge/weight", StringComparison.OrdinalIgnoreCase))
                {
                    WriteWeightResponse(ctx, WeighbridgeState.Bridge1);
                    return;
                }

                if (path.Equals("/weighbridge2/weight", StringComparison.OrdinalIgnoreCase))
                {
                    WriteWeightResponse(ctx, WeighbridgeState.Bridge2);
                    return;
                }

                if (path.Equals("/scanner/recent", StringComparison.OrdinalIgnoreCase))
                {
                    WriteScannerResponse(ctx);
                    return;
                }

                if (path.Equals("/relay/state", StringComparison.OrdinalIgnoreCase))
                {
                    WriteRelayState(ctx);
                    return;
                }

                if (path.Equals("/relay/gate", StringComparison.OrdinalIgnoreCase))
                {
                    HandleRelayCommand(ctx, RelayWorker.GateChannel);
                    return;
                }

                if (path.Equals("/relay/barrier", StringComparison.OrdinalIgnoreCase))
                {
                    HandleRelayCommand(ctx, RelayWorker.BarrierChannel);
                    return;
                }

                if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(ctx, 200, "{\"status\":\"ok\"}");
                    return;
                }

                if (path == "/" ||
                    path.Equals("/live-weight.html", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/live", StringComparison.OrdinalIgnoreCase))
                {
                    WriteStaticFile(ctx, "live-weight.html");
                    return;
                }

                if (path.Equals("/live-scanner.html", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/scanner", StringComparison.OrdinalIgnoreCase))
                {
                    WriteStaticFile(ctx, "live-scanner.html");
                    return;
                }

                WriteJson(ctx, 404, "{\"error\":\"not_found\"}");
            }
            catch (Exception ex)
            {
                Log("Request error: " + ex.Message);
                try { WriteJson(ctx, 500, "{\"error\":\"server_error\"}"); } catch { }
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }

        // WebSocket→VNC bridge: /vnc?target=NAME. Accepts the WebSocket, opens a TCP
        // connection to the allow-listed VNC server, and pumps bytes both ways. Lets the
        // browser-based noVNC client reach the touchscreens with no client install — and
        // reuses this service's existing :8765 URL ACL + firewall rule.
        private static async Task HandleWebSocketAsync(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url.AbsolutePath ?? "/";
            if (!path.Equals("/vnc", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 404;
                try { ctx.Response.Close(); } catch { }
                return;
            }

            var targetName = ctx.Request.QueryString["target"];
            VncTarget target;
            if (string.IsNullOrEmpty(targetName) || !VncTargets.TryGetValue(targetName, out target))
            {
                Log($"VNC bridge rejected: unknown target '{targetName}'");
                ctx.Response.StatusCode = 400;
                try { ctx.Response.Close(); } catch { }
                return;
            }

            HttpListenerWebSocketContext wsCtx;
            try
            {
                wsCtx = await ctx.AcceptWebSocketAsync(null);
            }
            catch (Exception ex)
            {
                Log("VNC bridge WS accept failed: " + ex.Message);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
                return;
            }

            var ws = wsCtx.WebSocket;
            var tcp = new TcpClient();
            try
            {
                await tcp.ConnectAsync(target.Host, target.Port);
            }
            catch (Exception ex)
            {
                Log($"VNC connect to {targetName} ({target.Host}:{target.Port}) failed: {ex.Message}");
                try { await ws.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "VNC server unreachable", CancellationToken.None); } catch { }
                ws.Dispose();
                tcp.Close();
                return;
            }

            Log($"VNC bridge open: {targetName} -> {target.Host}:{target.Port}");
            using (ws)
            using (tcp)
            {
                var stream = tcp.GetStream();
                try
                {
                    await Task.WhenAny(PumpWsToTcp(ws, stream), PumpTcpToWs(stream, ws));
                }
                catch (Exception ex)
                {
                    Log($"VNC bridge error ({targetName}): " + ex.Message);
                }
            }
            Log($"VNC bridge closed: {targetName}");
        }

        // Browser → VNC server. VNC is a raw byte stream, so frame boundaries don't matter.
        private static async Task PumpWsToTcp(WebSocket ws, NetworkStream tcp)
        {
            var buf = new byte[16384];
            while (ws.State == WebSocketState.Open)
            {
                var seg = new ArraySegment<byte>(buf);
                var result = await ws.ReceiveAsync(seg, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.Count > 0)
                    await tcp.WriteAsync(buf, 0, result.Count);
            }
        }

        // VNC server → browser (always binary frames; noVNC uses arraybuffer).
        private static async Task PumpTcpToWs(NetworkStream tcp, WebSocket ws)
        {
            var buf = new byte[16384];
            while (true)
            {
                int n;
                try { n = await tcp.ReadAsync(buf, 0, buf.Length); }
                catch { break; }
                if (n <= 0) break;
                if (ws.State != WebSocketState.Open) break;
                await ws.SendAsync(new ArraySegment<byte>(buf, 0, n), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
        }

        private static void WriteStaticFile(HttpListenerContext ctx, string fileName)
        {
            var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (!File.Exists(htmlPath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                var msg = Encoding.UTF8.GetBytes(fileName + " not found next to the service binary.");
                ctx.Response.ContentLength64 = msg.Length;
                ctx.Response.OutputStream.Write(msg, 0, msg.Length);
                return;
            }

            var bytes = File.ReadAllBytes(htmlPath);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteScannerResponse(HttpListenerContext ctx)
        {
            var events = ScannerState.Snapshot();
            var isConnected = ScannerState.IsConnected ? "true" : "false";
            var lastEvent = ScannerState.LastEventUtc;
            var lastEventStr = lastEvent == default(DateTime) ? "" : lastEvent.ToString("o");

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"isConnected\":").Append(isConnected).Append(",");
            sb.Append("\"lastEventUtc\":\"").Append(lastEventStr).Append("\",");
            sb.Append("\"events\":[");
            for (int i = 0; i < events.Length; i++)
            {
                if (i > 0) sb.Append(",");
                var e = events[i];
                sb.Append("{");
                sb.Append("\"timestampUtc\":\"").Append(e.TimestampUtc.ToString("o")).Append("\",");
                sb.Append("\"raw\":\"").Append(JsonEscape(e.Raw)).Append("\",");
                sb.Append("\"prodCd\":\"").Append(JsonEscape(e.ProdCd)).Append("\",");
                sb.Append("\"uniqueCode\":\"").Append(JsonEscape(e.UniqueCode)).Append("\",");
                sb.Append("\"weight\":\"").Append(JsonEscape(e.Weight)).Append("\",");
                sb.Append("\"result\":\"").Append(e.Result).Append("\",");
                sb.Append("\"scannerError\":").Append(e.ScannerReportedError ? "true" : "false").Append(",");
                sb.Append("\"message\":\"").Append(JsonEscape(e.Message)).Append("\"");
                sb.Append("}");
            }
            sb.Append("]}");
            WriteJson(ctx, 200, sb.ToString());
        }

        private static void WriteRelayState(HttpListenerContext ctx)
        {
            var json = "{" +
                       "\"connected\":" + (RelayState.IsConnected ? "true" : "false") + "," +
                       "\"gateOpen\":" + (RelayState.GateOpen ? "true" : "false") + "," +
                       "\"barrierOpen\":" + (RelayState.BarrierOpen ? "true" : "false") +
                       "}";
            WriteJson(ctx, 200, json);
        }

        // POST (or GET) /relay/gate|barrier?open=true|false — opens/closes the relay via the
        // worker's single connection, then returns the refreshed state.
        private static void HandleRelayCommand(HttpListenerContext ctx, int channel)
        {
            var openRaw = ctx.Request.QueryString["open"];
            bool open = string.Equals(openRaw, "true", StringComparison.OrdinalIgnoreCase) || openRaw == "1";

            var worker = RelayWorker.Instance;
            if (worker == null || !worker.SendCommand(channel, open))
            {
                WriteJson(ctx, 503, "{\"connected\":false,\"gateOpen\":false,\"barrierOpen\":false}");
                return;
            }
            WriteRelayState(ctx);
        }

        private static void WriteWeightResponse(HttpListenerContext ctx, WeighbridgeState state)
        {
            var weight = state.Weight ?? string.Empty;
            var lastUpdate = state.LastUpdateUtc;
            var ageMs = lastUpdate == default(DateTime)
                ? -1
                : (long)(DateTime.UtcNow - lastUpdate).TotalMilliseconds;
            var isConnected = state.IsConnected ? "true" : "false";

            // Outage window, so callers can say "no data for 4m" rather than showing a bare dash.
            var dataLostSince = state.DataLostSinceUtc;
            var dataLostMs = dataLostSince.HasValue
                ? (long)(DateTime.UtcNow - dataLostSince.Value).TotalMilliseconds
                : -1;

            var json = "{" +
                       "\"weight\":\"" + JsonEscape(weight) + "\"," +
                       "\"timestampUtc\":\"" + (lastUpdate == default(DateTime) ? "" : lastUpdate.ToString("o")) + "\"," +
                       "\"ageMs\":" + ageMs + "," +
                       "\"isConnected\":" + isConnected + "," +
                       "\"dataLostSinceUtc\":\"" + (dataLostSince.HasValue ? dataLostSince.Value.ToString("o") : "") + "\"," +
                       "\"dataLostMs\":" + dataLostMs +
                       "}";
            WriteJson(ctx, 200, json);
        }

        private static void WriteJson(HttpListenerContext ctx, int status, string json)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void Log(string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [HTTP] {message}";
                Console.WriteLine(line);

                var logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Logs",
                    "Weighbridge.log");

                var directory = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
