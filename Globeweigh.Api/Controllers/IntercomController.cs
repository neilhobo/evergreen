using Globeweigh.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Globeweigh.Api.Controllers;

/// <summary>The weighbridge door intercoms: call state, the bell-press log and camera stills.</summary>
[Authorize]
[ApiController]
[Route("api/intercom")]
public class IntercomController : ControllerBase
{
    private readonly IntercomService _intercom;

    public IntercomController(IntercomService intercom) => _intercom = intercom;

    /// <summary>Connection, ringing state, recent rings and the event log. Polled by the Intercom page.</summary>
    [HttpGet("status")]
    public ActionResult<IntercomStatusDto> GetStatus() => _intercom.GetStatus();

    /// <summary>The call in progress, if any. Polled by every admin app for the incoming-call overlay.</summary>
    [HttpGet("call")]
    public ActionResult<IntercomCallDto> GetCall() => _intercom.GetCall();

    /// <summary>Takes the call. First to answer wins; anyone after gets 409 naming who has it.</summary>
    [HttpPost("rings/{id:int}/answer")]
    public ActionResult<IntercomRingDto> Answer(int id)
    {
        var name = User.FindFirst("name")?.Value;
        var ring = _intercom.Answer(id, string.IsNullOrWhiteSpace(name) ? "someone" : name);
        if (ring != null) return ring;

        var now = _intercom.GetRing(id);
        var message = now?.AnsweredBy != null ? $"Answered by {now.AnsweredBy}" : "The call has ended";
        return Conflict(new { message });
    }

    [HttpPost("rings/{id:int}/hangup")]
    public IActionResult Hangup(int id)
    {
        _intercom.Hangup(id);
        return NoContent();
    }

    /// <summary>A live still from an intercom camera (?unit=name; the first unit when omitted).</summary>
    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot([FromQuery] string? unit, CancellationToken ct)
    {
        var jpeg = await _intercom.TakeSnapshotAsync(unit, ct);
        return jpeg == null ? NotFound() : File(jpeg, "image/jpeg");
    }

    /// <summary>The still taken the moment the bell was pressed for this ring.</summary>
    [HttpGet("rings/{id:int}/snapshot")]
    public IActionResult GetRingSnapshot(int id)
    {
        var jpeg = _intercom.GetRingSnapshot(id);
        return jpeg == null ? NotFound() : File(jpeg, "image/jpeg");
    }
}
