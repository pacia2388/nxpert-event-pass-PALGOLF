using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EventRegistration.Api.Controllers;

[ApiController]
[Route("api/events/{eventId:int}/invitations")]
[Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
public class InvitationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly EmailSender _sender;
    private readonly IConfiguration _config;
    private readonly InvitationEmailDispatcher _dispatcher;
    private readonly MailImageOptions _mailImages;
    private readonly EmailFileStore _outgoing;

    public InvitationsController(AppDbContext db, EmailService email, EmailSender sender, IConfiguration config,
        InvitationEmailDispatcher dispatcher, MailImageOptions mailImages,
        [FromKeyedServices("outgoing")] EmailFileStore outgoing)
    {
        _db = db;
        _email = email;
        _sender = sender;
        _config = config;
        _dispatcher = dispatcher;
        _mailImages = mailImages;
        _outgoing = outgoing;
    }

    // The JSON payload of a multipart compose/save request.
    private static T? ParsePayload<T>(string? payload) where T : class
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException) { return null; }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Invitation>>> List(int eventId)
    {
        var items = await _db.Invitations.Where(i => i.EventId == eventId)
            .OrderByDescending(i => i.SentAt).ToListAsync();
        return Ok(items);
    }

    [HttpPost("compose")]
    [MailRequestBodyLimit]
    public async Task<IActionResult> Compose(int eventId, [FromForm] string payload,
        [FromForm] List<IFormFile>? files = null)
    {
        var req = ParsePayload<InvitationComposeRequest>(payload);
        if (req is null) return BadRequest(new { message = "The message could not be read. Please try again." });
        var ev = await _db.Events.FindAsync(eventId);
        if (ev is null) return NotFound(new { message = "This event no longer exists. Reload the event list before sending invitations." });
        InvitationMessage message;
        string? emailFile = null;
        try
        {
            ValidateSchedule(req.ScheduledAt);
            await EnsureNoScheduleConflict(eventId, req.ScheduledAt, exceptId: 0);
            message = await new InvitationMessageBuilder(_db, _config, _mailImages).BuildAsync(ev, req);
            if (ev.StartsAt == default || ev.EndsAt <= ev.StartsAt)
                throw new ArgumentException("Set a valid start and end date/time for this event before sending invitations.");
            var attachments = await MailAttachments.ReadAsync(files);
            var mime = await _email.BuildStoredAsync(message.Subject, message.Html, message.Images, attachments);
            await MailAttachments.EnsureUnderLimitAsync(mime);
            emailFile = await _outgoing.SaveAsync(mime);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }

        var item = new InvitationEmail { EventId = eventId, EmailFile = emailFile };
        ApplyMessage(item, message, req.ScheduledAt);
        _db.InvitationEmails.Add(item);
        await _db.SaveChangesAsync();
        if (req.ScheduledAt is null) await _dispatcher.DispatchAsync(item.Id, this.UserId());
        var saved = await _db.InvitationEmails.AsNoTracking().SingleAsync(m => m.Id == item.Id);
        return Ok(DeliveryResult(saved, message.RegisterUrl));
    }

    [HttpGet("emails")]
    public async Task<IActionResult> EmailHistory(int eventId)
    {
        if (!await _db.Events.AnyAsync(e => e.Id == eventId)) return NotFound();
        // Keep large editor bodies (including inline images) out of the history list.
        var items = await _db.InvitationEmails.AsNoTracking().Where(m => m.EventId == eventId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.EventId,
                m.ToJson,
                m.CcJson,
                m.Subject,
                m.Status,
                m.CreatedAt,
                m.UpdatedAt,
                m.ScheduledAt,
                m.SentAt,
                m.LastAttemptAt,
                m.NextAttemptAt,
                m.Attempts,
                m.LastError,
                m.Version
            }).ToListAsync();
        return Ok(items.Select(m => new
        {
            m.Id,
            m.EventId,
            to = MailJson.List(m.ToJson),
            cc = MailJson.List(m.CcJson),
            m.Subject,
            m.Status,
            m.CreatedAt,
            m.UpdatedAt,
            m.ScheduledAt,
            m.SentAt,
            m.LastAttemptAt,
            m.NextAttemptAt,
            m.Attempts,
            m.LastError,
            m.Version
        }));
    }

    [HttpGet("emails/{id:int}")]
    public async Task<IActionResult> OpenEmail(int eventId, int id)
    {
        var item = await _db.InvitationEmails.AsNoTracking().SingleOrDefaultAsync(m => m.EventId == eventId && m.Id == id);
        if (item is null) return NotFound();
        var saved = await _outgoing.LoadAsync(item.EmailFile);
        return Ok(EmailDetails(item, saved is null ? [] : MailAttachments.Summaries(saved)));
    }

    [HttpPut("emails/{id:int}")]
    [MailRequestBodyLimit]
    public async Task<IActionResult> UpdateEmail(int eventId, int id, [FromForm] string payload,
        [FromForm] List<IFormFile>? files = null)
    {
        var req = ParsePayload<InvitationEmailUpdateRequest>(payload);
        if (req is null) return BadRequest(new { message = "The message could not be read. Please try again." });
        var item = await _db.InvitationEmails.Include(m => m.Event).SingleOrDefaultAsync(m => m.EventId == eventId && m.Id == id);
        if (item is null) return NotFound();
        if (!InvitationEmailStatus.CanEdit(item.Status))
            return Conflict(new { message = "This email is already being sent, sent or cancelled. Open it again to see its current status." });
        if (Convert.ToBase64String(item.Version) != req.Version)
            return Conflict(new { message = "This email has changed. Open it again before editing." });
        string? oldFile = item.EmailFile;
        string? emailFile = null;
        try
        {
            ValidateSchedule(req.ScheduledAt);
            await EnsureNoScheduleConflict(eventId, req.ScheduledAt, exceptId: id);
            var ev = item.Event!;
            if (ev.StartsAt == default || ev.EndsAt <= ev.StartsAt)
                throw new ArgumentException("Set a valid start and end date/time for this event before sending invitations.");
            var savedRecipientCount = MailJson.List(item.ToJson)!.Count +
                MailJson.List(item.CcJson)!.Count;
            // Expanded directory groups may exceed the composer entry limit.
            // Allow their saved recipient list to be edited without re-expanding it.
            var maxEntries = Math.Max(500, savedRecipientCount);
            if ((req.To?.Count ?? 0) + (req.Cc?.Count ?? 0) > maxEntries)
                throw new ArgumentException($"Add at most {maxEntries} recipient entries, or use *@domain for every user of an email domain.");
            var message = await new InvitationMessageBuilder(_db, _config, _mailImages).BuildAsync(ev,
                new(req.To, req.Cc, req.Subject, req.BodyHtml), enforceRecipientEntryLimit: false);
            // Attachments kept on the edit screen (matched by name) plus newly added files.
            var old = await _outgoing.LoadAsync(oldFile);
            var kept = req.KeepAttachments is { } keep && old is not null
                ? MailAttachments.Of(old, keep.ToHashSet(StringComparer.OrdinalIgnoreCase))
                : [];
            kept.AddRange(await MailAttachments.ReadAsync(files, kept.Sum(a => a.Content.LongLength)));
            var mime = await _email.BuildStoredAsync(message.Subject, message.Html, message.Images, kept);
            await MailAttachments.EnsureUnderLimitAsync(mime);
            emailFile = await _outgoing.SaveAsync(mime);

            ApplyMessage(item, message, req.ScheduledAt);
            item.EmailFile = emailFile;
            await _db.SaveChangesAsync();
            if (oldFile != emailFile) _outgoing.Delete(oldFile);
            if (req.ScheduledAt is null) await _dispatcher.DispatchAsync(id, this.UserId());
            var saved = await _db.InvitationEmails.AsNoTracking().SingleAsync(m => m.Id == id);
            return Ok(DeliveryResult(saved, message.RegisterUrl));
        }
        catch (ArgumentException ex)
        {
            if (emailFile is not null) _outgoing.Delete(emailFile);
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            if (emailFile is not null) _outgoing.Delete(emailFile);
            return Conflict(new { message = "This email changed or started sending. Open it again before editing." });
        }
    }

    [HttpPost("emails/{id:int}/cancel")]
    public async Task<IActionResult> CancelEmail(int eventId, int id, InvitationEmailVersionRequest req)
    {
        var item = await _db.InvitationEmails.SingleOrDefaultAsync(m => m.EventId == eventId && m.Id == id);
        if (item is null) return NotFound();
        if (!InvitationEmailStatus.CanEdit(item.Status) || Convert.ToBase64String(item.Version) != req.Version)
            return Conflict(new { message = "This email changed or started sending. Refresh the history before cancelling." });
        item.Status = InvitationEmailStatus.Cancelled;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        var file = item.EmailFile;
        item.EmailFile = null;
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "This email changed or started sending. Refresh the history before cancelling." }); }
        // Only after the cancel is saved: a send that started meanwhile still has its file.
        _outgoing.Delete(file);
        return NoContent();
    }

    private static void ValidateSchedule(DateTimeOffset? scheduledAt)
    {
        if (scheduledAt is not null && scheduledAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Choose a future date and time for the scheduled email.");
    }

    // One scheduled email per event per minute, so multi-day schedules can't stack on the same time.
    private async Task EnsureNoScheduleConflict(int eventId, DateTimeOffset? scheduledAt, int exceptId)
    {
        if (scheduledAt is null) return;
        var utc = scheduledAt.Value.ToUniversalTime();
        var start = utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMinute));
        var end = start.AddMinutes(1);
        if (await _db.InvitationEmails.AnyAsync(m => m.EventId == eventId && m.Id != exceptId &&
                m.Status == InvitationEmailStatus.Scheduled && m.ScheduledAt >= start && m.ScheduledAt < end))
            throw new ArgumentException("Another email for this event is already scheduled at that date and time. Choose a different time.");
    }

    private static void ApplyMessage(InvitationEmail item, InvitationMessage message, DateTimeOffset? scheduledAt)
    {
        // Freeze group membership at save time so the reviewed recipient list is
        // exactly the list delivered later, even if the directory changes.
        item.ToJson = JsonSerializer.Serialize(message.To);
        item.CcJson = JsonSerializer.Serialize(message.Cc);
        item.Subject = message.Subject;
        item.BodyHtml = message.EditableBody;
        item.ScheduledAt = scheduledAt?.ToUniversalTime();
        item.NextAttemptAt = item.ScheduledAt ?? DateTimeOffset.UtcNow;
        item.Status = InvitationEmailStatus.Scheduled;
        item.Attempts = 0;
        item.LastError = null;
        item.LastAttemptAt = null;
        // Saving a partly sent email again (the retry path) never re-sends to anyone it reached:
        // keep the delivered addresses that are still recipients.
        var recipients = message.To.Concat(message.Cc).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stillDelivered = MailJson.List(item.DeliveredRecipientsJson).Where(recipients.Contains).ToList();
        item.DeliveredRecipientsJson = stillDelivered.Count == 0 ? null : JsonSerializer.Serialize(stillDelivered);
        item.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static object EmailDetails(InvitationEmail m, List<MailAttachmentDto> attachments) => new
    {
        m.Id,
        m.EventId,
        to = MailJson.List(m.ToJson),
        cc = MailJson.List(m.CcJson),
        m.Subject,
        m.BodyHtml,
        m.Status,
        attachments,
        m.CreatedAt,
        m.UpdatedAt,
        m.ScheduledAt,
        m.SentAt,
        m.LastAttemptAt,
        m.NextAttemptAt,
        m.Attempts,
        m.LastError,
        m.Version,
    };

    private static object DeliveryResult(InvitationEmail m, string registerUrl)
    {
        var count = MailJson.List(m.ToJson)!.Count + MailJson.List(m.CcJson)!.Count;
        var delivered = string.IsNullOrWhiteSpace(m.DeliveredRecipientsJson) ? 0
            : MailJson.List(m.DeliveredRecipientsJson)!.Count;
        return new
        {
            id = m.Id,
            status = m.Status,
            sent = delivered,
            queued = m.Status == InvitationEmailStatus.Queued ? count - delivered : 0,
            recipientCount = count,
            registerUrl,
            message = m.Status switch
            {
                InvitationEmailStatus.Sent => $"Invitation sent to {count} recipient(s).",
                InvitationEmailStatus.Scheduled when m.ScheduledAt != null => $"Invitation scheduled for {count} recipient(s).",
                InvitationEmailStatus.Queued when delivered > 0 =>
                    $"Sent to {delivered} of {count} — the rest will be retried.",
                InvitationEmailStatus.Queued or InvitationEmailStatus.Failed => m.LastError,
                _ => "Invitation saved and waiting for delivery.",
            }
        };
    }

    [HttpPost]
    public async Task<IActionResult> Send(int eventId, InviteRequest req)
    {
        var ev = await _db.Events.FindAsync(eventId);
        if (ev is null) return NotFound();

        var frontendBase = _config["App:FrontendBaseUrl"]?.TrimEnd('/') ?? "http://localhost:4200";

        var emails = req.Emails
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct()
            .ToList();

        int sent = 0, failed = 0;
        ev.InviteSequence += 2;
        foreach (var email in emails)
        {
            // Reuse the existing invitation token for this email if we've invited them before,
            // so the same magic-link keeps working; otherwise mint a new one.
            var invite = await _db.Invitations.FirstOrDefaultAsync(i => i.EventId == eventId && i.Email == email);
            if (invite is null)
            {
                invite = new Invitation { EventId = eventId, Email = email, Status = InvitationStatus.Sent };
                _db.Invitations.Add(invite);
            }

            // Each invitee gets their own link carrying their invite token (?invite=...),
            // which the landing page uses to auto-login/auto-fill.
            var registerUrl = $"{frontendBase}/e/{ev.PublicSlug}?invite={invite.Token}";
            var html = EmailTemplates.Invitation(ev, registerUrl);
            // A failed email is retried in the background; only a turned-off email counts as failed.
            var result = await _sender.SendAsync([email], $"You're invited: {ev.Title}", html, attachments: new[] {
                CalendarInvite.ForInvitation(ev, _email.FromName, _email.FromAddress, new[] { email }, Array.Empty<string>(), registerUrl) },
                includeDefaultCc: false); // a personal invitation link is never copied to the CC list
            if (result.Accepted) sent++; else failed++;
            await Task.Delay(200); // gentle throttle to stay under Gmail burst limits
        }
        await _db.SaveChangesAsync();

        return Ok(new { sent, failed });
    }
}
