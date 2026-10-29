using System.Net.Sockets;
using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Globeweigh.Api.Services;

// Outbound email through the SMTP account set up in Super Admin > Email Setup (the
// dbo.Setting Smtp* / EmailFrom* columns). Anything that emails automatically goes through
// the instance SendAsync, which reads the saved account; the settings page drives the
// static Test/Send with the details as typed, before they are saved.
public class EmailSender
{
    // How the connection is secured. Stored on Setting.SmtpSecurity by name.
    public enum SmtpSecurity { None, StartTls, Ssl }

    public record SmtpConfig(
        string Host,
        int Port,
        SmtpSecurity Security,
        string? Username,     // null = the relay takes mail without signing in
        string? Password,
        string FromAddress,
        string? FromName);

    private readonly GbEvergreenContext _db;

    public EmailSender(GbEvergreenContext db) => _db = db;

    public static bool TryParseSecurity(string? name, out SmtpSecurity security) =>
        Enum.TryParse(name, ignoreCase: true, out security);

    // The saved account, or null while Email Setup is incomplete.
    public static SmtpConfig? ConfigFrom(Setting s)
    {
        if (string.IsNullOrWhiteSpace(s.SmtpHost) || s.SmtpPort is not { } port
            || string.IsNullOrWhiteSpace(s.EmailFromAddress))
            return null;
        TryParseSecurity(s.SmtpSecurity, out var security);
        return new SmtpConfig(s.SmtpHost.Trim(), port, security, s.SmtpUsername,
            DpapiSecret.Unprotect(s.SmtpPassword), s.EmailFromAddress.Trim(), s.EmailFromName);
    }

    // The saved account, ready to use. Throws InvalidOperationException while it is not set up,
    // or when its password was saved on another machine (a prod database brought onto a dev
    // PC): DPAPI reads that as null, the relay would refuse the login, so say why first.
    private async Task<SmtpConfig> SavedConfigAsync(CancellationToken ct)
    {
        var setting = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        var cfg = setting == null ? null : ConfigFrom(setting);
        if (setting == null || cfg == null)
            throw new InvalidOperationException("Email is not set up (Super Admin > Email Setup).");
        if (!string.IsNullOrEmpty(setting.SmtpUsername) && cfg.Password == null && !string.IsNullOrEmpty(setting.SmtpPassword))
            throw new InvalidOperationException(
                "The saved email password was entered on another machine and cannot be read here. Enter it again in Super Admin > Email Setup.");
        return cfg;
    }

    // What went wrong with the relay, in words a page can show.
    public static string Describe(Exception e) => e switch
    {
        AuthenticationException => "The server rejected the username and password.",
        SslHandshakeException => "Secure connection failed — the port may not match the security setting. " + e.Message,
        TimeoutException => "Timed out — check the server and port.",
        SocketException => "Could not reach the server — check the server and port. " + e.Message,
        SmtpCommandException => "The server refused: " + e.Message,
        _ => e.Message,
    };

    // Send with the saved account.
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var cfg = await SavedConfigAsync(ct);
        await SendAsync(cfg, to, subject, body, ct);
    }

    // Connect and sign in, then hang up — proves the host, port, security and password
    // without sending anything.
    public static async Task TestAsync(SmtpConfig cfg, CancellationToken ct)
    {
        using var client = await ConnectAsync(cfg, ct);
        await client.DisconnectAsync(true, ct);
    }

    public static async Task SendAsync(SmtpConfig cfg, string to, string subject, string body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(cfg.FromName ?? "", cfg.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = await ConnectAsync(cfg, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    // A file sent with a message (a delivery note as PDF, say).
    public record Attachment(string FileName, byte[] Content, string MediaType);

    // Send with the saved account to several addresses in the one email's To, as HTML with a
    // plain-text alternative, and any attachments.
    public async Task SendHtmlAsync(
        IReadOnlyList<string> to, string subject, string html, string text,
        IReadOnlyList<Attachment>? attachments = null, CancellationToken ct = default)
    {
        var cfg = await SavedConfigAsync(ct);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(cfg.FromName ?? "", cfg.FromAddress));
        foreach (var address in to) message.To.Add(MailboxAddress.Parse(address));
        message.Subject = subject;
        var body = new BodyBuilder { HtmlBody = html, TextBody = text };
        foreach (var a in attachments ?? Array.Empty<Attachment>())
            body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.MediaType));
        message.Body = body.ToMessageBody();

        using var client = await ConnectAsync(cfg, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    private static async Task<SmtpClient> ConnectAsync(SmtpConfig cfg, CancellationToken ct)
    {
        // A wrong host/port must fail in seconds, not hang the settings page for minutes.
        var client = new SmtpClient { Timeout = 20_000 };
        try
        {
            var options = cfg.Security switch
            {
                SmtpSecurity.Ssl => SecureSocketOptions.SslOnConnect,
                SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
                _ => SecureSocketOptions.None,
            };
            await client.ConnectAsync(cfg.Host, cfg.Port, options, ct);
            if (!string.IsNullOrEmpty(cfg.Username))
                await client.AuthenticateAsync(cfg.Username, cfg.Password ?? "", ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
