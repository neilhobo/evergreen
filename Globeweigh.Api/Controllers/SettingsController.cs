using System.Net.Sockets;
using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Globeweigh.Api.Controllers;

// System settings held on the single dbo.Setting row, edited from Admin > Settings and
// (the email account) Super Admin > Email Setup.
// (Bulk density is edited from the Touch weigh popup as well, so it lives on
// ManualStockController with the rest of that module.)
[ApiController]
[Authorize]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public SettingsController(GbEvergreenContext db) => _db = db;

    // GET /api/settings/order-numbering — the hand-over counter, plus the highest number
    // orders have actually reached, which is what the number has to clear to be usable.
    [HttpGet("order-numbering")]
    public async Task<ActionResult<OrderNumberingDto>> OrderNumbering(CancellationToken ct = default)
    {
        var setting = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (setting == null) return NotFound(new { message = "Settings row not found." });

        return Ok(new OrderNumberingDto(
            setting.NextOrderNo,
            setting.NextOrderNoEnabled,
            await HighestOrderNo(ct)));
    }

    // PUT /api/settings/order-numbering — set the number and switch it on or off.
    [HttpPut("order-numbering")]
    public async Task<ActionResult<OrderNumberingDto>> SetOrderNumbering(
        [FromBody] OrderNumberingInput input, CancellationToken ct = default)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(ct);
        if (setting == null) return NotFound(new { message = "Settings row not found." });

        if (input.NextOrderNo is { } n && (n < 1 || n > 99_999_999))
            return BadRequest(new { message = "Enter an order number between 1 and 99,999,999." });

        var highest = await HighestOrderNo(ct);

        if (input.Enabled)
        {
            if (input.NextOrderNo is not { } next)
                return BadRequest(new { message = "Enter the next order number before switching it on." });

            // Anything at or below what has already been issued would re-use a number that
            // is already on an order — and on a delivery note.
            if (next <= highest)
                return BadRequest(new
                {
                    message = $"Next order number must be above {highest:N0}, "
                        + "the highest already used by an order on this system.",
                });
        }

        // Stamp when the switch was flipped on (and clear it when flipped off): dispatch
        // order-number editing keys off it - an order created before this instant is a
        // pre-hand-over ad-hoc number that may still be corrected. Compute before the
        // Enabled field below is overwritten.
        if (input.Enabled && !setting.NextOrderNoEnabled)
            setting.NextOrderNoEnabledDate = DateTime.Now;
        else if (!input.Enabled)
            setting.NextOrderNoEnabledDate = null;

        setting.NextOrderNo = input.NextOrderNo;
        setting.NextOrderNoEnabled = input.Enabled;
        await _db.SaveChangesAsync(ct);

        return Ok(new OrderNumberingDto(setting.NextOrderNo, setting.NextOrderNoEnabled, highest));
    }

    // ---- Email (Super Admin > Email Setup) ------------------------------------------
    // The SMTP account automatic emails go out through (Services/EmailSender.cs). Checked
    // against the super-admin login here as well as in the UI: this is where the password
    // is written, and the relay can be driven from the send-test endpoint.

    // GET /api/settings/email — the account minus its password; hasPassword says one is
    // saved and readable on this server.
    [HttpGet("email")]
    public async Task<ActionResult<EmailSettingsDto>> Email(CancellationToken ct = default)
    {
        if (!await this.IsSuperAdminAsync(_db, ct)) return Forbid();
        var setting = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (setting == null) return NotFound(new { message = "Settings row not found." });
        return Ok(EmailSettingsDto.From(setting));
    }

    // PUT /api/settings/email — save the account. A blank password keeps the saved one.
    [HttpPut("email")]
    public async Task<ActionResult<EmailSettingsDto>> SetEmail(
        [FromBody] EmailSettingsInput input, CancellationToken ct = default)
    {
        if (!await this.IsSuperAdminAsync(_db, ct)) return Forbid();
        var setting = await _db.Settings.FirstOrDefaultAsync(ct);
        if (setting == null) return NotFound(new { message = "Settings row not found." });

        if (ValidateEmailInput(input, out var security) is { } problem)
            return BadRequest(new { message = problem });

        setting.SmtpHost = input.Host!.Trim();
        setting.SmtpPort = input.Port;
        setting.SmtpSecurity = security.ToString();
        setting.SmtpUsername = Trimmed(input.Username);
        // The password never comes back down to the UI, so a blank one here means "keep
        // what is saved"; it only goes when the username does.
        if (!string.IsNullOrEmpty(input.Password))
            setting.SmtpPassword = DpapiSecret.Protect(input.Password);
        else if (setting.SmtpUsername == null)
            setting.SmtpPassword = null;
        setting.EmailFromAddress = input.FromAddress!.Trim();
        setting.EmailFromName = Trimmed(input.FromName);
        await _db.SaveChangesAsync(ct);

        return Ok(EmailSettingsDto.From(setting));
    }

    // POST /api/settings/email/test — connect and sign in with the details as typed
    // (the saved password stands in for a blank one). Nothing is sent.
    [HttpPost("email/test")]
    public async Task<ActionResult> TestEmail([FromBody] EmailSettingsInput input, CancellationToken ct = default)
    {
        if (!await this.IsSuperAdminAsync(_db, ct)) return Forbid();
        var (cfg, problem) = await EmailConfigFor(input, ct);
        if (cfg == null) return BadRequest(new { message = problem });

        try
        {
            await EmailSender.TestAsync(cfg, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return BadRequest(new { message = EmailSender.Describe(e) });
        }
        return Ok(new
        {
            message = cfg.Username == null
                ? $"Connected to {cfg.Host}:{cfg.Port}."
                : $"Connected to {cfg.Host}:{cfg.Port} and signed in as {cfg.Username}.",
        });
    }

    // POST /api/settings/email/send-test — a plain test message to one address, with the
    // details as typed.
    [HttpPost("email/send-test")]
    public async Task<ActionResult> SendTestEmail([FromBody] SendTestEmailInput input, CancellationToken ct = default)
    {
        if (!await this.IsSuperAdminAsync(_db, ct)) return Forbid();
        var to = input.To?.Trim() ?? "";
        if (!MailboxAddress.TryParse(to, out _))
            return BadRequest(new { message = "Enter a valid email address to send to." });
        var (cfg, problem) = await EmailConfigFor(input, ct);
        if (cfg == null) return BadRequest(new { message = problem });

        try
        {
            await EmailSender.SendAsync(cfg, to,
                "Evergreen test email",
                "This is a test email from Evergreen.\r\n\r\n"
                + $"Sent {DateTime.Now:dd/MM/yyyy HH:mm} via {cfg.Host}:{cfg.Port} ({cfg.Security}).",
                ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return BadRequest(new { message = EmailSender.Describe(e) });
        }
        return Ok(new { message = $"Test email sent to {to}." });
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? ValidateEmailInput(EmailSettingsInput input, out EmailSender.SmtpSecurity security)
    {
        security = default;
        if (string.IsNullOrWhiteSpace(input.Host)) return "Enter the SMTP server.";
        if (input.Port is not (>= 1 and <= 65535)) return "Enter a port between 1 and 65535.";
        if (!EmailSender.TryParseSecurity(input.Security, out security)) return "Choose a security setting.";
        if (Trimmed(input.FromAddress) is not { } from || !MailboxAddress.TryParse(from, out _))
            return "Enter a valid From address.";
        return null;
    }

    // The account to try: the details as typed, with the saved password standing in for a
    // blank one.
    private async Task<(EmailSender.SmtpConfig? Config, string? Problem)> EmailConfigFor(
        EmailSettingsInput input, CancellationToken ct)
    {
        if (ValidateEmailInput(input, out var security) is { } problem) return (null, problem);

        var username = Trimmed(input.Username);
        var password = string.IsNullOrEmpty(input.Password) ? null : input.Password;
        if (username != null && password == null)
        {
            var setting = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
            password = DpapiSecret.Unprotect(setting?.SmtpPassword);
            if (password == null)
                return (null, "Enter the password — there isn't one saved on this server.");
        }

        return (new EmailSender.SmtpConfig(input.Host!.Trim(), input.Port!.Value, security, username, password,
            input.FromAddress!.Trim(), Trimmed(input.FromName)), null);
    }

    private async Task<int> HighestOrderNo(CancellationToken ct) =>
        await _db.Set<DispatchOrderEntity>().AsNoTracking().MaxAsync(o => (int?)o.OrderNo, ct) ?? 0;
}

public record OrderNumberingDto(int? NextOrderNo, bool Enabled, int HighestOrderNo);

public class OrderNumberingInput
{
    public int? NextOrderNo { get; set; }
    public bool Enabled { get; set; }
}

// The email account as the UI sees it: never the password, only whether one is saved
// (and decrypts on this server - see DpapiSecret).
public record EmailSettingsDto(
    string? Host, int? Port, string Security, string? Username, bool HasPassword,
    string? FromAddress, string? FromName)
{
    public static EmailSettingsDto From(Setting s) => new(
        s.SmtpHost,
        s.SmtpPort,
        (EmailSender.TryParseSecurity(s.SmtpSecurity, out var sec) ? sec : EmailSender.SmtpSecurity.None).ToString(),
        s.SmtpUsername,
        DpapiSecret.Unprotect(s.SmtpPassword) != null,
        s.EmailFromAddress,
        s.EmailFromName);
}

public class EmailSettingsInput
{
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Security { get; set; }   // None | StartTls | Ssl
    public string? Username { get; set; }
    public string? Password { get; set; }   // blank = keep / use the saved one
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
}

public class SendTestEmailInput : EmailSettingsInput
{
    public string? To { get; set; }
}
