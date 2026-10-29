using Globeweigh.Api.Auth;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Controllers;

/// <summary>Staged features: built and deployed, switched on separately.
///
/// <para>Reading is open to any signed-in user, because every screen has to know which way a
/// feature is set before it can draw itself. CHANGING one is super-admin only — it is the
/// Globeweigh-internal Staging page, and a client should not be able to switch on work that has
/// not been handed over. The exception is OfficeFlags, the delivery-note design.</para></summary>
[ApiController]
[Authorize]
[Route("api/features")]
public class FeaturesController : ControllerBase
{
    private readonly GbEvergreenContext _db;

    public FeaturesController(GbEvergreenContext db) => _db = db;

    // Appearance choices any signed-in user may change (see Set). Everything else is staged
    // work and stays super-admin only.
    private static readonly HashSet<string> OfficeFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Style.DeliveryNoteLedger",
        "Style.DeliveryNoteGreenBand",
    };

    /// <summary>Every flag that has ever been set, as a name→on/off map. A name that is not in it
    /// has never been switched and is OFF — which is why the client treats a missing key as off
    /// rather than asking for a list of known features.</summary>
    [HttpGet]
    public async Task<ActionResult<Dictionary<string, bool>>> All(CancellationToken ct = default)
    {
        var rows = await _db.Set<FeatureFlagEntity>().AsNoTracking()
            .Select(f => new { f.Name, f.Enabled })
            .ToListAsync(ct);
        return Ok(rows.ToDictionary(r => r.Name, r => r.Enabled, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Switch one on or off, creating its row the first time. Super-admin only.</summary>
    [HttpPut("{name}")]
    public async Task<ActionResult<Dictionary<string, bool>>> Set(
        string name, [FromBody] FeatureFlagInput input, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        // The delivery-note design is picked on Admin > Delivery Note, which the office admins
        // use (like Automatic Emails beside it), so those two flags are not super-admin only.
        if (!OfficeFlags.Contains(name) && !await this.IsSuperAdminAsync(_db, ct)) return Forbid();
        if (name.Length == 0 || name.Length > 100)
            return BadRequest(new { message = "A feature name is required." });

        var flag = await _db.Set<FeatureFlagEntity>().FirstOrDefaultAsync(f => f.Name == name, ct);
        if (flag == null)
        {
            flag = new FeatureFlagEntity { Name = name };
            _db.Add(flag);
        }
        flag.Enabled = input.Enabled;
        flag.DateChanged = DateTime.Now;
        flag.ChangedById = this.GetUserId();
        await _db.SaveChangesAsync(ct);

        return await All(ct);
    }
}

public class FeatureFlagInput
{
    public bool Enabled { get; set; }
}
