using System.Text.Json;
using System.Text.RegularExpressions;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Controllers;

// Mail planner (organizers): plans with target days, each with its own mail, sent to receiver lists.
[ApiController]
[Route("api/mail-plans")]
[Authorize(Roles = Roles.Admin + "," + Roles.SuperAdmin)]
public class MailPlansController(AppDbContext db, MailPlanAudience audience, MailPlanDispatcher dispatcher,
    MailImageOptions mailImages, EmailService email,
    [FromKeyedServices("outgoing")] EmailFileStore outgoing) : ControllerBase
{
    private const int MaxDays = 60;
    private const int MaxEmails = 200;
    private const int MaxCc = 50;
    private const int MaxGroups = 50;
    private const int MaxBodyText = 4000;
    private const int MaxMobileText = 1000;
    private static readonly TimeSpan PhOffset = TimeSpan.FromHours(8);

    // The JSON payload of a multipart create/update request.
    private static MailPlanRequest? ParsePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            return JsonSerializer.Deserialize<MailPlanRequest>(payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException) { return null; }
    }

    // Plans with their days; bodies stay out of the list (they can carry several MB of images).
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var plans = await db.MailPlans.AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new MailPlan
            {
                Id = p.Id,
                Kind = p.Kind,
                EventId = p.EventId,
                EventName = p.EventName,
                Channel = p.Channel,
                Draft = p.Draft,
                GroupIdsJson = p.GroupIdsJson,
                EmailsJson = p.EmailsJson,
                CcJson = p.CcJson,
                CreatedByUserId = p.CreatedByUserId,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                Version = p.Version,
                Sends = p.Sends.OrderBy(s => s.SendAt).Select(s => new MailPlanSend
                {
                    Id = s.Id,
                    PlanId = s.PlanId,
                    SendAt = s.SendAt,
                    Subject = s.Subject,
                    PreviewText = s.PreviewText,
                    ImageCount = s.ImageCount,
                    Status = s.Status,
                    SentAt = s.SentAt,
                    RecipientCount = s.RecipientCount,
                    LastError = s.LastError,
                    Attempts = s.Attempts,
                    NextAttemptAt = s.NextAttemptAt,
                }).ToList(),
            }).ToListAsync();
        return Ok(plans.Select(p => PlanDto(p, withBodies: false)));
    }

    // One plan with every day's full message, for editing.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var plan = await db.MailPlans.AsNoTracking().Include(p => p.Sends).SingleOrDefaultAsync(p => p.Id == id);
        if (plan is null) return NotFound();
        var attachments = new Dictionary<int, List<MailAttachmentDto>>();
        foreach (var send in plan.Sends)
        {
            var message = await outgoing.LoadAsync(send.EmailFile);
            if (message is not null) attachments[send.Id] = MailAttachments.Summaries(message);
        }
        return Ok(PlanDto(plan, withBodies: true, attachments));
    }

    // Expands a *@domain entry to the active users of that domain, for the receiver field's chips.
    [HttpGet("expand")]
    public async Task<IActionResult> Expand([FromQuery] string? pattern)
    {
        if (!MailRecipients.IsDomainPattern(pattern))
            return BadRequest(new { message = "Enter an address like *@company.com." });
        return Ok(await MailRecipients.DomainEmailsAsync(db, pattern!));
    }

    [HttpGet("sends/{sendId:int}/body")]
    public async Task<IActionResult> Body(int sendId)
    {
        var body = await db.MailPlanSends.AsNoTracking().Where(s => s.Id == sendId).Select(s => s.BodyHtml).SingleOrDefaultAsync();
        return body is null ? NotFound() : Ok(new { bodyHtml = body });
    }

    // Event names and receiver lists for the plan form.
    [HttpGet("options")]
    public async Task<IActionResult> Options()
    {
        var since = DateTime.Now.AddDays(-30);
        var events = await db.Events.AsNoTracking().Where(e => e.EndsAt >= since).OrderBy(e => e.StartsAt)
            .Select(e => e.Title).Take(100).ToListAsync();
        var saved = await db.MailPlanEventNames.AsNoTracking().Select(n => n.Name).ToListAsync();
        var used = await db.MailPlans.AsNoTracking().Where(p => p.EventName != null).Select(p => p.EventName!).Distinct().ToListAsync();
        var known = events.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Ok(new
        {
            events = events.Distinct(StringComparer.OrdinalIgnoreCase),
            savedEvents = saved.Concat(used).Where(known.Add).Order(StringComparer.OrdinalIgnoreCase),
            groups = await audience.GroupsAsync(),
            savedCc = await db.MailPlanSavedCcs.AsNoTracking().OrderBy(c => c.Email).Select(c => c.Email).ToListAsync(),
        });
    }

    // Saved CC addresses: offered in the CC dropdown; typing a new address still works.
    [HttpPost("saved-cc")]
    public async Task<IActionResult> SaveCc(MailSavedCcRequest req)
    {
        var email = InvitationMessageBuilder.Address(req.Email);
        if (email is null) return BadRequest(new { message = "Enter a valid email address." });
        if (!await db.MailPlanSavedCcs.AnyAsync(c => c.Email == email))
        {
            if (await db.MailPlanSavedCcs.CountAsync() >= 500)
                return BadRequest(new { message = "The saved CC list is full (500). Remove addresses you no longer use." });
            db.MailPlanSavedCcs.Add(new MailPlanSavedCc { Email = email });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { /* Saved at the same time by someone else. */ }
        }
        return Ok(new { email });
    }

    [HttpDelete("saved-cc")]
    public async Task<IActionResult> RemoveSavedCc([FromQuery] string? email)
    {
        var address = InvitationMessageBuilder.Address(email);
        if (address is not null) await db.MailPlanSavedCcs.Where(c => c.Email == address).ExecuteDeleteAsync();
        return NoContent();
    }

    [HttpPost("event-names")]
    public async Task<IActionResult> SaveEventName(MailEventNameRequest req)
    {
        var name = CleanEventName(req.Name);
        if (name is null) return BadRequest(new { message = "Enter an event name of up to 200 characters." });
        if (!await db.MailPlanEventNames.AnyAsync(n => n.Name == name))
        {
            db.MailPlanEventNames.Add(new MailPlanEventName { Name = name });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { /* Saved at the same time by someone else. */ }
        }
        return Ok(new { name });
    }

    [HttpPost]
    [MailRequestBodyLimit]
    public async Task<IActionResult> Create([FromForm] string payload, [FromForm] List<IFormFile>? files = null)
    {
        var req = ParsePayload(payload);
        if (req is null) return BadRequest(new { message = "The plan could not be read. Please try again." });
        var plan = new MailPlan { CreatedByUserId = this.UserId() };
        string? error;
        var stored = new List<string>();
        try
        {
            var attachments = await MailAttachments.ReadAsync(files);
            error = await Apply(plan, req, attachments, stored);
        }
        catch (ArgumentException ex) { foreach (var f in stored) outgoing.Delete(f); return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { foreach (var f in stored) outgoing.Delete(f); return Conflict(new { message = ex.Message }); }
        if (error is not null) { foreach (var f in stored) outgoing.Delete(f); return BadRequest(new { message = error }); }
        db.MailPlans.Add(plan);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { foreach (var f in stored) outgoing.Delete(f); throw; }
        return Ok(PlanDto(plan, withBodies: false));
    }

    [HttpPut("{id:int}")]
    [MailRequestBodyLimit]
    public async Task<IActionResult> Update(int id, [FromForm] string payload, [FromForm] List<IFormFile>? files = null)
    {
        var req = ParsePayload(payload);
        if (req is null) return BadRequest(new { message = "The plan could not be read. Please try again." });
        var plan = await db.MailPlans.Include(p => p.Sends).SingleOrDefaultAsync(p => p.Id == id);
        if (plan is null) return NotFound(new { message = "This plan no longer exists." });
        if (Convert.ToBase64String(plan.Version) != req.Version)
            return Conflict(new { message = "This plan was changed by someone else. Reopen it before editing." });
        string? error;
        var stored = new List<string>();
        var replaced = new List<string>();
        try
        {
            var attachments = await MailAttachments.ReadAsync(files);
            error = await Apply(plan, req, attachments, stored, replaced);
        }
        catch (ArgumentException ex) { foreach (var f in stored) outgoing.Delete(f); return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { foreach (var f in stored) outgoing.Delete(f); return Conflict(new { message = ex.Message }); }
        if (error is not null) { foreach (var f in stored) outgoing.Delete(f); return BadRequest(new { message = error }); }
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            foreach (var f in stored) outgoing.Delete(f);
            return Conflict(new { message = "A day of this plan started sending or was changed. Reopen the plan before editing." });
        }
        foreach (var f in replaced) outgoing.Delete(f);
        return Ok(PlanDto(plan, withBodies: false));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var plan = await db.MailPlans.Include(p => p.Sends).SingleOrDefaultAsync(p => p.Id == id);
        if (plan is null) return NoContent();
        if (plan.Sends.Any(s => s.Status == MailSendStatus.Sending))
            return Conflict(new { message = "A day of this plan is being sent right now. Try again in a minute." });
        var files = plan.Sends.Select(s => s.EmailFile).Where(f => f != null).Cast<string>().ToList();
        db.MailPlans.Remove(plan);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "This plan started sending. Try again in a minute." }); }
        foreach (var file in files) outgoing.Delete(file);
        return NoContent();
    }

    // Removes a day that hasn't gone out; removing a plan's only day removes the plan.
    [HttpDelete("sends/{sendId:int}")]
    public async Task<IActionResult> DeleteSend(int sendId)
    {
        var send = await db.MailPlanSends.Include(s => s.Plan).ThenInclude(p => p!.Sends).SingleOrDefaultAsync(s => s.Id == sendId);
        if (send is null) return NoContent();
        if (send.Status is MailSendStatus.Sending or MailSendStatus.Sent)
            return Conflict(new { message = "This day has already gone out and stays in the plan's history." });
        var plan = send.Plan!;
        var planDeleted = plan.Sends.Count == 1;
        var files = planDeleted
            ? plan.Sends.Select(s => s.EmailFile).Where(f => f != null).Cast<string>().ToList()
            : new List<string> { send.EmailFile! }.Where(f => f != null).ToList();
        if (planDeleted) db.MailPlans.Remove(plan);
        else
        {
            db.MailPlanSends.Remove(send);
            plan.UpdatedAt = DateTimeOffset.UtcNow;
        }
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "This day started sending. Refresh the planner." }); }
        foreach (var file in files) outgoing.Delete(file);
        return Ok(new { planDeleted });
    }

    [HttpPost("sends/{sendId:int}/send-now")]
    public async Task<IActionResult> SendNow(int sendId)
    {
        var send = await db.MailPlanSends.Include(s => s.Plan).SingleOrDefaultAsync(s => s.Id == sendId);
        if (send is null) return NotFound(new { message = "This day no longer exists. Refresh the planner." });
        if (send.Plan!.Draft) return BadRequest(new { message = "Finish this draft plan before sending it." });
        if (send.Status != MailSendStatus.Scheduled) return Conflict(new { message = "This day is already being sent or was sent." });
        send.SendAt = send.NextAttemptAt = DateTimeOffset.UtcNow;
        send.UpdatedAt = DateTimeOffset.UtcNow;
        return await SaveAndDispatch(send);
    }

    // Tries a failed or queued day again now; receivers it already reached are skipped.
    [HttpPost("sends/{sendId:int}/retry")]
    public async Task<IActionResult> Retry(int sendId)
    {
        var send = await db.MailPlanSends.Include(s => s.Plan).SingleOrDefaultAsync(s => s.Id == sendId);
        if (send is null) return NotFound(new { message = "This day no longer exists. Refresh the planner." });
        if (send.Status is not (MailSendStatus.Failed or MailSendStatus.Queued))
            return Conflict(new { message = "Only failed or queued days can be retried." });
        if (send.Plan!.Draft) return BadRequest(new { message = "Finish this draft plan before sending it." });
        send.Status = MailSendStatus.Queued;
        send.Attempts = 0;
        send.NextAttemptAt = send.UpdatedAt = DateTimeOffset.UtcNow;
        send.LastError = null;
        return await SaveAndDispatch(send);
    }

    private async Task<IActionResult> SaveAndDispatch(MailPlanSend send)
    {
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "This day started sending. Refresh the planner." }); }
        await dispatcher.DispatchAsync(send.Id, this.UserId());
        var saved = await db.MailPlanSends.AsNoTracking().SingleAsync(s => s.Id == send.Id);
        return Ok(SendDto(saved, withBody: false));
    }

    // Validates the request and applies it to the plan. Days that have gone out (anything not
    // Scheduled) are kept as they are; Scheduled days are updated, added or removed to match.
    // Files saved before a failure are listed in stored, so the caller can delete them.
    private async Task<string?> Apply(MailPlan plan, MailPlanRequest req,
        List<EmailAttachment> attachments, List<string> stored, List<string>? replaced = null)
    {
        if (!MailPlanKinds.IsValid(req.Kind)) return "Choose a mail type.";
        if (!MailPlanChannels.IsValid(req.Channel)) return "Choose Email, Viber or Both.";
        string? eventName = null;
        if (!string.IsNullOrWhiteSpace(req.EventName) && (eventName = CleanEventName(req.EventName)) is null)
            return "Enter an event name of up to 200 characters.";

        // Typed addresses: *@domain is expanded to the active users of that domain before saving;
        // the pattern itself never reaches the mail server.
        foreach (var raw in req.Emails ?? [])
            if (!MailRecipients.IsDomainPattern(raw) && InvitationMessageBuilder.Address(raw) is null)
                return $"Check this address: {raw}";
        if ((req.Emails?.Count ?? 0) > MaxEmails)
            return $"Add at most {MaxEmails} email addresses per plan.";
        var emails = (await audience.ExpandAsync(req.Emails)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var cc = new List<string>();
        foreach (var raw in req.Cc ?? [])
        {
            if (InvitationMessageBuilder.Address(raw) is not { } address) return $"Check this CC address: {raw}";
            cc.Add(address);
        }
        cc = cc.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cc.Count > MaxCc) return $"Add at most {MaxCc} CC addresses per plan.";

        var excluded = new List<string>();
        foreach (var raw in req.ExcludedEmails ?? [])
        {
            if (MailRecipients.IsDomainPattern(raw)) excluded.AddRange(await MailRecipients.DomainEmailsAsync(db, raw));
            else if (InvitationMessageBuilder.Address(raw) is { } address) excluded.Add(address);
            else return $"Check this excluded address: {raw}";
        }
        excluded = excluded.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var groupIds = (req.GroupIds ?? []).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();
        if (groupIds.Count > MaxGroups) return $"Pick at most {MaxGroups} saved lists.";
        var previous = MailJson.List(plan.GroupIdsJson);
        var known = (await audience.GroupsAsync()).Select(g => g.Id).Concat(previous).ToHashSet();
        if (groupIds.Any(g => !known.Contains(g))) return "A saved list no longer exists. Reload the planner and pick the lists again.";

        var locked = plan.Sends.Where(s => s.Status != MailSendStatus.Scheduled).ToList();
        var editable = plan.Sends.Where(s => s.Status == MailSendStatus.Scheduled).ToDictionary(s => s.Id);
        var inputs = req.Sends ?? [];
        if (inputs.Count > MaxDays) return $"A plan can have at most {MaxDays} days.";
        if (locked.Count + inputs.Count == 0) return "Pick at least one target day.";
        // Send immediately is a single action: one day goes out on the next scheduler tick.
        var immediate = req.SendImmediately;
        if (immediate && inputs.Count != 1)
            return "Send immediately works with one day at a time. Remove the other days first.";
        var days = locked.Select(s => s.SendAt.ToOffset(PhOffset).Date).ToHashSet();
        var prepared = new List<(MailPlanSendInput Input, DateTimeOffset SendAt, string Subject, string Body, string Text, int Images)>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var label = $"Day {locked.Count + i + 1}";
            if (input.Id is { } existingId && !editable.ContainsKey(existingId))
                throw new InvalidOperationException($"{label} has already gone out or was removed. Reopen the plan before editing.");
            var subject = (input.Subject ?? "").Trim();
            if (subject.Length is 0 or > 200 || subject.Contains('\r') || subject.Contains('\n'))
                return $"{label}: enter a subject of up to 200 characters.";
            string body;
            int images;
            try
            {
                var rendered = InvitationMessageBuilder.RenderBody(input.BodyHtml, mailImages);
                body = InvitationMessageBuilder.EditableBody(rendered.Html, rendered.Images);
                images = rendered.Images.Count;
            }
            catch (ArgumentException ex) { return $"{label}: {ex.Message}"; }
            var text = InvitationMessageBuilder.PlainText(body);
            if (text.Length == 0 && images == 0) return $"{label}: write the message.";
            if (text.Length > MaxBodyText) return $"{label}: keep the message under {MaxBodyText} characters.";
            if (req.Channel != MailPlanChannels.Email && (text.Length == 0 || text.Length > MaxMobileText))
                return $"{label}: a mobile message must be 1 to {MaxMobileText} characters.";
            var sendAt = immediate ? now : input.SendAt;
            if (!req.Draft && !immediate && sendAt <= now) return $"{label}: pick a send time that has not passed.";
            if (!immediate && !days.Add(sendAt.ToOffset(PhOffset).Date))
                return $"{label}: another day of this plan is already on that date.";
            prepared.Add((input, sendAt, subject, body, text, images));
        }
        if (!req.Draft && (await audience.ResolveAsync(groupIds, emails, excluded)).Count == 0)
            return "Add at least one receiver email.";
        // One attachment set for the whole plan: the kept ones already on it plus the new uploads
        // (a new file replaces a kept one with the same name).
        var planAttachments = new List<EmailAttachment>();
        var existingFile = editable.Values.Select(s => s.EmailFile).FirstOrDefault(f => f != null);
        if (existingFile is not null && await outgoing.LoadAsync(existingFile) is { } existing)
        {
            var keep = req.KeepAttachments?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newNames = attachments.Select(a => a.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            planAttachments.AddRange(MailAttachments.Of(existing, keep).Where(a => !newNames.Contains(a.FileName)));
        }
        planAttachments.AddRange(attachments);
        if (req.Channel == MailPlanChannels.Viber && planAttachments.Count > 0)
            return "Attachments are sent by email only, so a Viber-only plan cannot carry them. Remove the attachments first.";

        plan.Kind = req.Kind!;
        plan.Channel = req.Channel!;
        plan.Draft = req.Draft;
        plan.EventName = eventName;
        var today = DateTime.Now;
        plan.EventId = eventName is null ? null : await db.Events.Where(e => e.Title == eventName)
            .OrderByDescending(e => e.EndsAt >= today).ThenBy(e => e.StartsAt).Select(e => (int?)e.Id).FirstOrDefaultAsync();
        plan.Event = plan.EventId is int eventId ? await db.Events.FindAsync(eventId) : null;
        plan.GroupIdsJson = JsonSerializer.Serialize(groupIds);
        plan.EmailsJson = JsonSerializer.Serialize(emails);
        plan.CcJson = JsonSerializer.Serialize(cc);
        plan.ExcludedEmailsJson = JsonSerializer.Serialize(excluded);
        plan.SendImmediately = immediate;
        plan.SendAt = immediate ? now : null;
        plan.UpdatedAt = now;

        var kept = prepared.Where(p => p.Input.Id is not null).Select(p => p.Input.Id!.Value).ToHashSet();
        foreach (var removed in editable.Values.Where(s => !kept.Contains(s.Id)))
        {
            if (removed.EmailFile is not null) replaced?.Add(removed.EmailFile);
            db.MailPlanSends.Remove(removed);
        }

        // Every day's whole email (with its inline images and attachments) is saved as an .eml now,
        // so a later send never rebuilds it and attachments cannot change after review.
        foreach (var (input, sendAt, subject, body, text, images) in prepared)
        {
            var send = input.Id is { } existingId ? editable[existingId] : new MailPlanSend();
            send.SendAt = sendAt.ToUniversalTime();
            send.NextAttemptAt = send.SendAt;
            send.Subject = subject;
            send.BodyHtml = body;
            var preview = Regex.Replace(text, @"\s+", " ");
            send.PreviewText = preview.Length > 300 ? preview[..300] : preview;
            send.ImageCount = images;
            send.Attempts = 0;
            send.LastError = null;
            send.UpdatedAt = now;

            var oldFile = send.EmailFile;
            var (renderedHtml, inline) = InvitationMessageBuilder.RenderBody(body, mailImages);
            var html = MailPlanDispatcher.Template(plan, renderedHtml, email.FromName);
            var mime = await email.BuildStoredAsync(subject, html, inline, planAttachments);
            await MailAttachments.EnsureUnderLimitAsync(mime);
            var file = await outgoing.SaveAsync(mime);
            stored.Add(file);
            if (oldFile is not null) replaced?.Add(oldFile);
            send.EmailFile = file;
            send.DeliveredRecipientsJson = null;
            if (input.Id is null) plan.Sends.Add(send);
        }
        return null;
    }

    private static string? CleanEventName(string? value)
    {
        var name = Regex.Replace(value ?? "", @"\s+", " ").Trim();
        return name.Length is 0 or > 200 || name.Any(char.IsControl) ? null : name;
    }

    private object PlanDto(MailPlan p, bool withBodies,
        Dictionary<int, List<MailAttachmentDto>>? attachments = null) => new
        {
            p.Id,
            p.Kind,
            p.EventId,
            p.EventName,
            p.Channel,
            p.Draft,
            groupIds = MailJson.List(p.GroupIdsJson),
            emails = MailJson.List(p.EmailsJson),
            cc = MailJson.List(p.CcJson),
            excludedEmails = MailJson.List(p.ExcludedEmailsJson),
            p.SendImmediately,
            p.SendAt,
            p.CreatedAt,
            p.UpdatedAt,
            p.Version,
            sends = p.Sends.OrderBy(s => s.SendAt).Select(s => SendDto(s, withBodies, attachments)),
        };

    private static object SendDto(MailPlanSend s, bool withBody,
        Dictionary<int, List<MailAttachmentDto>>? attachments = null) => new
        {
            s.Id,
            s.SendAt,
            s.Subject,
            s.PreviewText,
            s.ImageCount,
            bodyHtml = withBody ? s.BodyHtml : null,
            attachments = attachments?.GetValueOrDefault(s.Id) ?? [],
            s.Status,
            s.SentAt,
            s.RecipientCount,
            s.LastError,
            s.Attempts,
            s.NextAttemptAt,
        };
}
