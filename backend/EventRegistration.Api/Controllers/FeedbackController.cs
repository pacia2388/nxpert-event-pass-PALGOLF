using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Controllers;

[ApiController]
[Route("api/events/{eventId:int}/feedback")]
[Authorize]
public class FeedbackController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EventFeedbackService _automation;

    public FeedbackController(AppDbContext db, EventFeedbackService automation)
    {
        _db = db;
        _automation = automation;
    }

    // ---- Participant: submit or update my rating for an event ----
    [HttpPost]
    public async Task<ActionResult<FeedbackDto>> Submit(int eventId, FeedbackRequest req)
    {
        if (Validate(req.Rating, req.OrganizationRating, req.Comment, req.WentWell, req.Improve) is { } error)
            return BadRequest(new { message = error });

        var uid = this.UserId();
        var isRegistered = await _db.Registrations.AnyAsync(r => r.EventId == eventId && r.UserId == uid);
        if (!isRegistered)
            return StatusCode(403, new { message = "You can only rate events you registered for." });
        // Like the feedback link: answers open once the event has ended (server local time).
        var endsAt = await _db.Events.Where(e => e.Id == eventId).Select(e => e.EndsAt).FirstAsync();
        if (endsAt > DateTime.Now)
            return Conflict(new { message = "Feedback opens after the event ends." });

        var user = await _db.Users.FindAsync(uid);
        // A link answer already stored for this account is the same person's answer: update it.
        var fb = await _db.Feedbacks.FirstOrDefaultAsync(f => f.EventId == eventId && f.UserId == uid);
        if (fb is null)
        {
            fb = new Feedback { EventId = eventId, UserId = uid };
            _db.Feedbacks.Add(fb);
        }
        // The name on an answer is always the account's own, never typed in.
        Apply(fb, req.Rating, req.Comment, req.IsAnonymous, null, user?.FullName,
            req.OrganizationRating, req.WentWell, req.Improve);
        await _db.SaveChangesAsync();

        return Ok(Dto(fb, user?.FullName));
    }

    // ---- Participant: my existing feedback (to prefill the form) ----
    [HttpGet("mine")]
    public async Task<ActionResult<FeedbackDto?>> Mine(int eventId)
    {
        var uid = this.UserId();
        var fb = await _db.Feedbacks.AsNoTracking().FirstOrDefaultAsync(f =>
            f.EventId == eventId && f.UserId == uid);
        if (fb is null) return Ok((FeedbackDto?)null);
        var user = await _db.Users.FindAsync(uid);
        return Ok(Dto(fb, user?.FullName));
    }

    // ---- Admin: the automation switch and the audience's progress ----
    [HttpPut("automation")]
    [Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<FeedbackCampaignDto>> Automate(int eventId, FeedbackAutomationRequest req)
    {
        if (!await _db.Events.AnyAsync(e => e.Id == eventId)) return NotFound();
        var campaign = await _db.EventFeedbackCampaigns.FindAsync(eventId);
        if (campaign is null)
        {
            campaign = new EventFeedbackCampaign { EventId = eventId };
            _db.EventFeedbackCampaigns.Add(campaign);
        }
        campaign.Enabled = req.Enabled;
        // The audience is frozen on its first prepare; turning the switch back on never re-freezes it.
        await _db.SaveChangesAsync();
        return Ok(await CampaignDto(eventId));
    }

    // Sends the feedback link again to confirmed registrants who have not answered yet.
    [HttpPost("automation/send")]
    [Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<IActionResult> SendNow(int eventId)
    {
        var ev = await _db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev is null) return NotFound();
        if (ev.Status == EventStatus.Draft)
            return Conflict(new { message = "Publish the event before sending feedback links." });
        if (ev.EndsAt > DateTime.Now)
            return Conflict(new { message = "Feedback opens after the event ends." });
        var sent = await _automation.ResendUnansweredAsync(eventId);
        return Ok(new { sent });
    }

    // Retries the links that could not be sent.
    [HttpPost("automation/retry")]
    [Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<IActionResult> Retry(int eventId)
    {
        if (!await _db.Events.AnyAsync(e => e.Id == eventId)) return NotFound();
        if (!await _db.EventFeedbackCampaigns.AsNoTracking().AnyAsync(c => c.EventId == eventId && c.Enabled))
            return Conflict(new { message = "Turn on the feedback form for this event before retrying." });
        var sent = await _automation.RetryFailedAsync(eventId);
        return Ok(new { sent });
    }

    // ---- Admin: aggregated feedback for an event ----
    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
    public async Task<ActionResult<EventFeedbackSummary>> Summary(int eventId)
    {
        var rows = await _db.Feedbacks.Include(f => f.User)
            .Where(f => f.EventId == eventId)
            .OrderByDescending(f => f.CreatedAt).ToListAsync();
        var items = rows.Select(f => Dto(f, f.User?.FullName)).ToList();
        var avg = items.Count == 0 ? 0 : Math.Round(items.Average(i => i.Rating), 2);
        return Ok(new EventFeedbackSummary(items.Count, avg, items, await CampaignDto(eventId)));
    }

    private async Task<FeedbackCampaignDto> CampaignDto(int eventId)
    {
        var campaign = await _db.EventFeedbackCampaigns.AsNoTracking().FirstOrDefaultAsync(c => c.EventId == eventId);
        var counts = await _db.EventFeedbackRecipients.AsNoTracking()
            .Where(r => r.EventId == eventId)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Answered = g.Count(r => r.SubmittedAt != null) })
            .ToListAsync();
        int Count(string status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;
        return new FeedbackCampaignDto(
            campaign?.Enabled ?? false,
            campaign?.PreparedAt is not null,
            Count(FeedbackRecipientStatus.Pending),
            Count(FeedbackRecipientStatus.Sent),
            Count(FeedbackRecipientStatus.Failed),
            counts.Sum(c => c.Answered));
    }

    private static FeedbackDto Dto(Feedback f, string? accountName)
    {
        var name = f.IsAnonymous ? "Anonymous" : (f.DisplayName ?? accountName ?? "Attendee");
        return new FeedbackDto(f.Id, f.EventId, name, f.Rating, f.Comment, f.CreatedAt,
            f.IsAnonymous, f.IsAnonymous ? null : f.DisplayName, f.OrganizationRating,
            f.WentWell, f.Improve);
    }

    private static string? Validate(int rating, int? organizationRating, string? comment,
        string? wentWell, string? improve)
    {
        if (rating < 1 || rating > 5) return "Rating must be between 1 and 5.";
        if (organizationRating is < 1 or > 5) return "Organization rating must be between 1 and 5.";
        if ((comment?.Length ?? 0) > 1000) return "Comment must be 1000 characters or fewer.";
        if ((wentWell?.Length ?? 0) > 1000) return "What went well must be 1000 characters or fewer.";
        if ((improve?.Length ?? 0) > 1000) return "What to improve must be 1000 characters or fewer.";
        return null;
    }

    private static void Apply(Feedback fb, int rating, string? comment, bool isAnonymous,
        string? displayName, string? accountName, int? organizationRating, string? wentWell, string? improve)
    {
        fb.Rating = rating;
        fb.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        fb.IsAnonymous = isAnonymous;
        fb.DisplayName = isAnonymous
            ? null
            : (string.IsNullOrWhiteSpace(displayName) ? accountName : displayName.Trim());
        fb.OrganizationRating = organizationRating;
        fb.WentWell = string.IsNullOrWhiteSpace(wentWell) ? null : wentWell.Trim();
        fb.Improve = string.IsNullOrWhiteSpace(improve) ? null : improve.Trim();
    }
}
