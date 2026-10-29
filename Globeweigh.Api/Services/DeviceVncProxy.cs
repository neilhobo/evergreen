using System.Net.Sockets;
using System.Net.WebSockets;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

/// <summary>
/// WebSocket→TCP bridge so the admin UI's embedded noVNC can view a device's VNC
/// server (droidVNC-NG on the Android tablets). The browser connects to
/// ws(s)://…/api/devices/{id}/vnc?access_token=JWT and this pumps raw RFB bytes to the
/// device's RemoteHost:RemotePort (defaults: the device's last-registered IP, port 5900).
/// Same idea as the GlobeweighService /vnc bridge used for the weighbridge screens, but
/// the target comes from the Device row instead of a config allow-list — the API server
/// sits on the client LAN, so it can reach devices the admin's browser can't.
/// </summary>
public static class DeviceVncProxy
{
    public static async Task Handle(HttpContext context, int id)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket request expected.");
            return;
        }

        var db = context.RequestServices.GetRequiredService<GbEvergreenContext>();
        var device = await db.Set<Device>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        var host = device?.RemoteHost;
        if (string.IsNullOrWhiteSpace(host)) host = device?.IpAddress;
        if (device == null || string.IsNullOrWhiteSpace(host))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Device not found or has no host/IP.");
            return;
        }
        var port = device.RemotePort is > 0 ? device.RemotePort.Value : 5900;

        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(8), context.RequestAborted);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync($"Could not reach {host}:{port} — is the VNC server running on the device?");
            return;
        }
        tcp.NoDelay = true;

        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        var stream = tcp.GetStream();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        // Either side closing/erroring tears the whole bridge down.
        var wsToTcp = PumpWsToTcp(ws, stream, cts.Token);
        var tcpToWs = PumpTcpToWs(stream, ws, cts.Token);
        await Task.WhenAny(wsToTcp, tcpToWs);
        cts.Cancel();

        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bridge closed", CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch { /* peer already gone */ }
    }

    private static async Task PumpWsToTcp(WebSocket ws, NetworkStream tcp, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var msg = await ws.ReceiveAsync(buffer, ct);
                if (msg.MessageType == WebSocketMessageType.Close) return;
                if (msg.Count > 0) await tcp.WriteAsync(buffer.AsMemory(0, msg.Count), ct);
            }
        }
        catch { /* disconnect — WhenAny in Handle tears down the other pump */ }
    }

    private static async Task PumpTcpToWs(NetworkStream tcp, WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var read = await tcp.ReadAsync(buffer, ct);
                if (read <= 0) return; // device closed the connection
                await ws.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, true, ct);
            }
        }
        catch { /* disconnect — WhenAny in Handle tears down the other pump */ }
    }
}
