using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Controllers;

/// <summary>
/// The one-time post-event feedback link. No sign-in: the token identifies the frozen recipient
/// and keeps one answer per person. Anonymous answers never show who wrote them.
/// </summary>
[ApiController]
[Route("api/feedback")]
[AllowAnonymous]
[EnableRateLimiting("feedback")]
public class PublicFeedbackController(AppDbContext db) : ControllerBase
{
    [HttpGet("{token:guid}")]
    public async Task<ActionResult<PublicFeedbackDto>> Open(Guid token)
    {
        var r = await Recipient(token);
        if (r is null) return NotFound(new { message = "This feedback link is invalid." });
        var ev = r.Event!;
        return Ok(new PublicFeedbackDto(ev.Id, ev.Title, ev.StartsAt, ev.EndsAt, r.SubmittedAt is not null));
    }

    [HttpPost("{token:guid}")]
    public async Task<IActionResult> Submit(Guid token, PublicFeedbackRequest req)
    {
        if (req.Rating < 1 || req.Rating > 5)
            return BadRequest(new { message = "Rating must be between 1 and 5." });
        if (req.OrganizationRating is < 1 or > 5)
            return BadRequest(new { message = "Organization rating must be between 1 and 5." });
        if ((req.Comment?.Length ?? 0) > 1000 || (req.WentWell?.Length ?? 0) > 1000 ||
            (req.Improve?.Length ?? 0) > 1000)
            return BadRequest(new { message = "Each answer must be 1000 characters or fewer." });

        var r = await Recipient(token);
        if (r is null) return NotFound(new { message = "This feedback link is invalid." });
        var ev = r.Event!;
        // Feedback opens only after the event has ended (server local time, Philippine time).
        if (ev.EndsAt > DateTime.Now)
            return Conflict(new { message = "Feedback opens after the event ends." });

        // One answer per person/event: the same account may already have rated the event in My
        // Events (or answered through another sub-event's link), so find that answer and update it.
        var fb = r.UserId is int userId
            ? await db.Feedbacks.FirstOrDefaultAsync(f =>
                f.EventId == ev.Id && (f.FeedbackRecipientId == r.Id || f.UserId == userId))
            : await db.Feedbacks.FirstOrDefaultAsync(f => f.FeedbackRecipientId == r.Id);
        if (fb is null)
        {
            fb = new Feedback { EventId = ev.Id };
            db.Feedbacks.Add(fb);
        }
        fb.FeedbackRecipientId = r.Id;
        fb.UserId = r.UserId;
        fb.Rating = req.Rating;
        fb.Comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
        fb.IsAnonymous = req.IsAnonymous;
        fb.DisplayName = req.IsAnonymous ? null : r.Name;
        fb.OrganizationRating = req.OrganizationRating;
        fb.WentWell = string.IsNullOrWhiteSpace(req.WentWell) ? null : req.WentWell.Trim();
        fb.Improve = string.IsNullOrWhiteSpace(req.Improve) ? null : req.Improve.Trim();
        r.SubmittedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Thank you for your feedback." });
    }

    private Task<EventFeedbackRecipient?> Recipient(Guid token) =>
        db.EventFeedbackRecipients.Include(r => r.Event).FirstOrDefaultAsync(r => r.Token == token);
}
