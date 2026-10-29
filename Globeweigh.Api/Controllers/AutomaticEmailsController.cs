using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

// The wording of the automatic emails — Admin > Automatic Emails. The emails themselves and
// their pieces are defined in Services/AutomaticEmails.cs; what has been changed is held in
// dbo.AutomaticEmailText, one row per piece, and a piece with no row is the default.
[ApiController]
[Authorize]
[Route("api/automatic-emails")]
public class AutomaticEmailsController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public AutomaticEmailsController(GbEvergreenContext db) => _db = db;

    // GET /api/automatic-emails — every email: its pieces with the current and the default
    // wording, and the placeholders the wording may use.
    [HttpGet]
    public async Task<ActionResult<List<AutomaticEmailDto>>> All(CancellationToken ct = default)
    {
        var result = new List<AutomaticEmailDto>();
        foreach (var def in AutomaticEmails.All)
            result.Add(await DtoFor(def, ct));
        return Ok(result);
    }

    // PUT /api/automatic-emails/{kind} — save the wording as typed: every piece, keyed by field.
    // A piece back at its default loses its row; anything else gets one.
    [HttpPut("{kind}")]
    public async Task<ActionResult<AutomaticEmailDto>> Save(
        string kind, [FromBody] AutomaticEmailInput input, CancellationToken ct = default)
    {
        var def = AutomaticEmails.Find(kind);
        if (def == null) return NotFound(new { message = $"No automatic email '{kind}'." });

        var texts = Normalise(def, input);
        foreach (var f in def.Fields)
            if (AutomaticEmails.Validate(f, texts[f.Key]) is { } problem)
                return BadRequest(new { message = problem });

        var rows = await _db.Set<AutomaticEmailTextEntity>()
            .Where(t => t.Email == def.Kind)
            .ToListAsync(ct);
        foreach (var f in def.Fields)
        {
            var text = texts[f.Key];
            var row = rows.FirstOrDefault(r => string.Equals(r.Field, f.Key, StringComparison.OrdinalIgnoreCase));
            if (text == f.Default)
            {
                if (row != null) _db.Remove(row);
                continue;
            }
            if (row == null)
            {
                row = new AutomaticEmailTextEntity { Email = def.Kind, Field = f.Key };
                _db.Add(row);
            }
            else if (row.Text == text) continue;
            row.Text = text;
            row.DateChanged = DateTime.Now;
            row.ChangedById = this.GetUserId();
        }
        // A row for a piece the code no longer has is nothing to keep.
        foreach (var stray in rows.Where(r => def.FieldFor(r.Field) == null))
            _db.Remove(stray);
        await _db.SaveChangesAsync(ct);

        return Ok(await DtoFor(def, ct));
    }

    // POST /api/automatic-emails/{kind}/preview — the email as it would go out with the wording
    // as typed (nothing is saved), on a made-up load, for the live preview beside the form.
    [HttpPost("{kind}/preview")]
    public ActionResult<AutomaticEmailPreviewDto> Preview(string kind, [FromBody] AutomaticEmailInput input)
    {
        var def = AutomaticEmails.Find(kind);
        if (def == null) return NotFound(new { message = $"No automatic email '{kind}'." });
        var texts = Normalise(def, input);
        // Too long is still previewable; a blank subject is stood in for by the composer.
        var mail = LoadEmailComposer.Compose(def.Kind, LoadEmailComposer.Sample(def.Kind), texts);
        return Ok(new AutomaticEmailPreviewDto(mail.Subject, mail.Html, mail.Text));
    }

    // Every piece the email has, from the input where it was given (CRLF folded to LF, ends
    // trimmed) and the default where it was not.
    private static Dictionary<string, string> Normalise(AutomaticEmails.Definition def, AutomaticEmailInput input)
    {
        var given = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in input.Fields ?? new()) given[k] = v;
        return def.Fields.ToDictionary(
            f => f.Key,
            f => given.TryGetValue(f.Key, out var t) && t != null ? t.Replace("\r\n", "\n").Trim() : f.Default,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<AutomaticEmailDto> DtoFor(AutomaticEmails.Definition def, CancellationToken ct)
    {
        var wording = await AutomaticEmails.WordingAsync(_db, def.Kind, ct);
        return new AutomaticEmailDto(
            def.Kind, def.Title, def.Description,
            def.Placeholders.Select(p => new AutomaticEmailPlaceholderDto(p.Name, p.Meaning)).ToList(),
            def.Fields.Select(f => new AutomaticEmailFieldDto(
                f.Key, f.Label, f.Hint, f.Multiline, f.Default, wording[f.Key])).ToList());
    }
}

public record AutomaticEmailPlaceholderDto(string Name, string Meaning);

public record AutomaticEmailFieldDto(
    string Key, string Label, string Hint, bool Multiline, string Default, string Text);

public record AutomaticEmailDto(
    string Kind, string Title, string Description,
    List<AutomaticEmailPlaceholderDto> Placeholders, List<AutomaticEmailFieldDto> Fields);

public record AutomaticEmailPreviewDto(string Subject, string Html, string Text);

public class AutomaticEmailInput
{
    // Field key → wording as typed. A key left out reverts to the default, so the form sends
    // every piece it shows.
    public Dictionary<string, string?>? Fields { get; set; }
}
