using System.Net;
using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services.Theming;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace EventRegistration.Api.Services;

/// <summary>
/// Delivers mail-plan days and the reminders receivers set on them. The first attempt freezes the
/// receiver list (one MailPlanDelivery each), which also puts the mail in their My Reminders inbox.
/// Email goes out in BCC batches of at most 100; per-receiver progress makes retries skip anyone
/// already reached.
/// </summary>
public class MailPlanDispatcher(AppDbContext db, EmailService email, EmailSender sender, MessageDispatcher dispatcher,
    MailPlanAudience audience, MailImageOptions mailImages, [FromKeyedServices("outgoing")] EmailFileStore outgoing,
    NotificationService notify, ILogger<MailPlanDispatcher> log)
{
    public const int MaxAttempts = 5;
    // At most 100 recipients per SMTP message; CC addresses count toward the first batch.
    public const int BatchSize = 100;

    private sealed class DeliveryException(string message) : Exception(message);

    /// <param name="requestedBy">The admin whose request started this send gets the progress;
    /// a scheduled send (null) reports to every admin.</param>
    public async Task DispatchAsync(int sendId, int? requestedBy = null)
    {
        var now = DateTimeOffset.UtcNow;
        // Claimed in SQL like invitation emails: an edit changes the rowversion, and a claimed
        // day can no longer be edited or removed.
        var claimed = await db.MailPlanSends.Where(s => s.Id == sendId && !s.Plan!.Draft &&
                (s.Status == MailSendStatus.Scheduled || s.Status == MailSendStatus.Queued) &&
                s.NextAttemptAt <= now && s.Attempts < MaxAttempts)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, MailSendStatus.Sending)
                .SetProperty(s => s.LastAttemptAt, now).SetProperty(s => s.UpdatedAt, now)
                .SetProperty(s => s.Attempts, s => s.Attempts + 1));
        if (claimed == 0) return;
        db.ChangeTracker.Clear();
        var send = await db.MailPlanSends.Include(s => s.Plan).ThenInclude(p => p!.Event).SingleOrDefaultAsync(s => s.Id == sendId);
        if (send is null) return; // The plan was deleted after the claim.
        var plan = send.Plan!;
        var progressUser = requestedBy;
        try
        {
            if (send.DispatchedAt is null)
            {
                var excluded = MailJson.List(plan.ExcludedEmailsJson);
                var recipients = await audience.ResolveAsync(
                    MailJson.List(plan.GroupIdsJson),
                    MailJson.List(plan.EmailsJson),
                    excluded);
                if (recipients.Count == 0)
                    throw new DeliveryException("This plan has no receivers. Edit the plan's receivers, then retry.");
                // A receiver who is also on CC gets one copy, as a receiver.
                var reached = recipients.Select(r => r.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var cc = (await audience.ResolveAsync([], MailJson.List(plan.CcJson), excluded))
                    .Where(c => !reached.Contains(c.Email)).ToList();
                foreach (var r in recipients)
                    db.MailPlanDeliveries.Add(new MailPlanDelivery { SendId = send.Id, Email = r.Email, UserId = r.UserId, Phone = r.Phone });
                foreach (var c in cc)
                    db.MailPlanDeliveries.Add(new MailPlanDelivery { SendId = send.Id, Email = c.Email, UserId = c.UserId, IsCc = true });
                send.RecipientCount = recipients.Count + cc.Count;
                send.DispatchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            var deliveries = await db.MailPlanDeliveries.Where(d => d.SendId == send.Id).OrderBy(d => d.Id).ToListAsync();
            var (subject, html, images) = Email(plan, send, reminder: false);
            var template = await LoadTemplateAsync(send, subject, html, images);

            // Viber-only plans email the receivers who have no phone number, and (on a retry) those
            // whose one mobile try failed. CC addresses are shown on every batch but get their
            // single copy with the first batch that goes out.
            var queued = false;
            var ccRows = deliveries.Where(d => d.IsCc).ToList();
            var ccList = ccRows.Select(d => d.Email).ToList();
            var delivered = Delivered(send.DeliveredRecipientsJson);
            var byEmail = deliveries.Where(d => !d.IsCc && d.EmailSentAt is null &&
                (plan.Channel != MailPlanChannels.Viber || d.Phone is null ||
                    (d.MobileAttemptedAt is not null && d.ViberSentAt is null))).ToList();
            // At most BatchSize addresses per message (BCC + delivered CC); CC goes first, in its
            // own chunks when it is larger. Later batches show the CC list without delivering it.
            var total = deliveries.Count;
            var ccToSend = ccRows.Where(d => d.EmailSentAt is null).ToList();
            foreach (var (batch, ccPending) in MailRecipients.Batches(byEmail, ccToSend, BatchSize))
            {
                var batchEmails = batch.Select(d => d.Email).ToList();
                var result = ccPending.Count > 0
                    ? await email.SendStoredAsync(template, [], ccPending.Select(d => d.Email).ToList(), batchEmails)
                    : await email.SendStoredAsync(template, [], ccList, batchEmails, deliverCc: false);
                if (result != DeliveryResult.Sent)
                {
                    queued = true;
                    await notify.MailSendProgressAsync(progressUser,
                        new MailSendProgressNotice("plan", send.Id, delivered.Count, total, total - delivered.Count, true));
                    break;
                }
                foreach (var d in batch.Concat(ccPending))
                {
                    d.EmailSentAt = DateTimeOffset.UtcNow;
                    delivered.Add(d.Email);
                }
                send.DeliveredRecipientsJson = JsonSerializer.Serialize(delivered);
                await db.SaveChangesAsync();
                await notify.MailSendProgressAsync(progressUser,
                    new MailSendProgressNotice("plan", send.Id, delivered.Count, total, 0, false));
            }

            // Viber/SMS: one try per receiver (shared send → fall back flow), never repeated by a
            // plan retry. Viber plan: mobile preferred, so a rejected send or an unlisted country gets
            // the email (rejected: only when the provider's FallbackToEmail is on), and a later Viber
            // failure queues it too. Both plan: everyone is already in the BCC batches, no fallback.
            // Never when already emailed; a failed fallback email is retried with the plan.
            var mobileFailed = 0;
            if (plan.Channel != MailPlanChannels.Email)
            {
                var mobile = MessageTemplates.MailPlan(send.Subject, send.BodyHtml);
                var channel = NotificationChannels.Viber;
                if (plan.Channel == MailPlanChannels.Both)
                    channel = NotificationChannels.Both;

                var mobileReceivers = deliveries.Where(d =>
                    !d.IsCc && d.Phone is not null && d.MobileAttemptedAt is null && d.ViberSentAt is null);
                foreach (var d in mobileReceivers)
                {
                    d.MobileAttemptedAt = DateTimeOffset.UtcNow;

                    // Fallback email: Viber plan only, and not for a receiver the batches already reached.
                    Func<Task<DeliveryResult>>? sendEmail = null;
                    string? emailRef = null;
                    var canEmail = channel == NotificationChannels.Viber && d.EmailSentAt is null &&
                                   !string.IsNullOrWhiteSpace(d.Email);
                    if (canEmail)
                    {
                        sendEmail = async () =>
                        {
                            var emailResult = await email.SendStoredAsync(template, [d.Email], []);
                            if (emailResult == DeliveryResult.Sent)
                            {
                                d.EmailSentAt = DateTimeOffset.UtcNow;
                                delivered.Add(d.Email);
                                send.DeliveredRecipientsJson = JsonSerializer.Serialize(delivered);
                            }
                            else
                                queued = true;
                            return emailResult;
                        };
                        emailRef = JsonSerializer.Serialize(new MailPlanEmailRef(d.Id));
                    }

                    var message = new OutgoingMessage(EmailRetryKinds.MailPlan, mobile, sendEmail, emailRef);
                    var result = await dispatcher.SendAsync(new Recipient(d.Phone, channel), message);

                    var sentByMobile = result.Channels.Any(c => c != "email");
                    if (sentByMobile)
                    {
                        d.ViberSentAt = DateTimeOffset.UtcNow;
                        continue;
                    }
                    mobileFailed++;
                    var outcome = result.Sent ? "emailed instead" : "not emailed";
                    log.LogWarning("Mail plan send {Id}: mobile message to a receiver was not sent ({Result}).", send.Id, outcome);
                }
                await db.SaveChangesAsync();
            }

            if (!queued)
            {
                send.Status = MailSendStatus.Sent;
                send.SentAt = DateTimeOffset.UtcNow;
                send.LastError = mobileFailed == 0 ? null
                    : $"{mobileFailed} mobile message{(mobileFailed == 1 ? "" : "s")} could not be sent; those receivers got the email instead where they have an address and email fallback is on.";
                await notify.MailSendProgressAsync(progressUser,
                    new MailSendProgressNotice("plan", send.Id, delivered.Count, total, 0, true));
                // The saved email stays for reminders and a late Viber fallback (they carry its
                // attachments); the scheduler removes it once neither can need it.
            }
            else
            {
                var last = send.Attempts >= MaxAttempts;
                send.Status = last ? MailSendStatus.Failed : MailSendStatus.Queued;
                send.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(5);
                var problem = delivered.Count > 0
                    ? $"Sent to {delivered.Count} of {total} — the rest will be retried"
                    : "Email delivery is unavailable";
                send.LastError = last
                    ? $"{problem}. Gave up after five attempts. Retry to try again; receivers already reached are skipped."
                    : $"{problem}; retrying in five minutes. Receivers already reached are skipped.";
            }
        }
        catch (DeliveryException ex)
        {
            send.Status = MailSendStatus.Failed;
            send.LastError = ex.Message;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Mail plan send {Id} failed.", sendId);
            send.Status = MailSendStatus.Failed;
            // Do not disclose SMTP credentials, filesystem paths or database details.
            send.LastError = "Delivery could not be completed. Check the plan and email settings, then retry.";
        }
        send.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Sends a receiver the reminder they set, by their preferred channel with email fallback. Once.</summary>
    public async Task RemindAsync(int deliveryId)
    {
        var now = DateTimeOffset.UtcNow;
        var claimed = await db.MailPlanDeliveries.Where(d => d.Id == deliveryId && d.ReminderSentAt == null &&
                d.DoneAt == null && d.RemindAt <= now)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.ReminderSentAt, now));
        if (claimed == 0) return;
        var delivery = await db.MailPlanDeliveries.AsNoTracking().Include(d => d.Send).ThenInclude(s => s!.Plan)
            .SingleOrDefaultAsync(d => d.Id == deliveryId);
        if (delivery?.Send?.Plan is not { } plan) return;
        var send = delivery.Send;
        try
        {
            var user = await db.Users.AsNoTracking().Where(u => u.Email == delivery.Email)
                .Select(u => new { u.NotificationChannel, u.Phone }).FirstOrDefaultAsync();
            var channel = user?.NotificationChannel ?? NotificationChannels.Email;
            var phone = string.IsNullOrWhiteSpace(user?.Phone) ? delivery.Phone : user.Phone;
            var (subject, html, images) = Email(plan, send, reminder: true);
            // Only read: a reminder never stores a new file (the day's .eml may already be cleaned up).
            var template = await LoadTemplateAsync(send, subject, html, images, store: false);
            // Preference → fall back, like every message. The email is retried in the background on
            // failure (ReminderSentAt is already stamped).
            var attachments = MailAttachments.Of(template);
            async Task<DeliveryResult> SendEmail()
            {
                // One try now; a real failure is queued with the whole email (and its attachments)
                // and retried in the background.
                var sent = await sender.SendAsync([delivery.Email], subject, html,
                    attachments: attachments, inlineImages: images);
                return sent.Accepted ? DeliveryResult.Sent : DeliveryResult.Failed;
            }

            var mobile = MessageTemplates.MailReminder(subject, send.BodyHtml);
            var emailRef = JsonSerializer.Serialize(new MailPlanEmailRef(delivery.Id));
            var message = new OutgoingMessage(EmailRetryKinds.MailReminder, mobile, SendEmail, emailRef);
            await dispatcher.SendAsync(new Recipient(phone, channel), message);
        }
        catch (Exception ex) { log.LogError(ex, "Mail reminder {Id} could not be sent.", deliveryId); }
    }

    /// <summary>
    /// The email a late Viber failure owes one receiver (day mail or reminder), from the stored .eml
    /// (or rebuilt from the plan when it is gone). A day mail the receiver already got is not sent again.
    /// </summary>
    public async Task<(DeliveryResult Result, string? Skip)> SendLateEmailAsync(int deliveryId, bool reminder)
    {
        var d = await db.MailPlanDeliveries.Include(x => x.Send).ThenInclude(s => s!.Plan).ThenInclude(p => p!.Event)
            .SingleOrDefaultAsync(x => x.Id == deliveryId);
        if (d?.Send?.Plan is not { } plan)
            return (DeliveryResult.Failed, "The mail plan was deleted.");
        if (!reminder && d.EmailSentAt is not null)
            return (DeliveryResult.Failed, "The receiver was already emailed.");

        var (subject, html, images) = Email(plan, d.Send, reminder);
        var template = await outgoing.LoadAsync(d.Send.EmailFile);
        // A reminder has its own subject/intro, so only the day's attachments come from the .eml.
        var result = template is null
            ? await email.SendMessageAsync([d.Email], [], subject, html, inlineImages: images)
            : reminder
                ? await email.SendMessageAsync([d.Email], [], subject, html, inlineImages: images,
                    attachments: MailAttachments.Of(template))
                : await email.SendStoredAsync(template, [d.Email], []);
        if (!reminder && result == DeliveryResult.Sent)
            d.EmailSentAt = DateTimeOffset.UtcNow;
        return (result, null);
    }

    // The stored whole email for a day (saved at compose time); older rows are built and stored now.
    private async Task<MimeMessage> LoadTemplateAsync(MailPlanSend send, string subject, string html,
        Dictionary<string, byte[]> images, bool store = true)
    {
        var stored = await outgoing.LoadAsync(send.EmailFile);
        if (stored is not null) return stored;
        var built = await email.BuildStoredAsync(subject, html, images);
        if (!store) return built;
        send.EmailFile = await outgoing.SaveAsync(built);
        await db.SaveChangesAsync();
        return built;
    }

    /// <summary>How long a delivered day keeps its .eml for a late Viber failure (reported within 24 h).</summary>
    public static readonly TimeSpan KeepSentEmailFor = TimeSpan.FromHours(24);

    /// <summary>
    /// Removes the .eml of delivered days once no late Viber fallback (24 h) and no pending
    /// reminder can need it. A reminder set later is sent without the attachments.
    /// </summary>
    public static async Task CleanUpSentEmailsAsync(AppDbContext db, EmailFileStore outgoing, CancellationToken ct = default)
    {
        var before = DateTimeOffset.UtcNow - KeepSentEmailFor;
        var done = await db.MailPlanSends.AsNoTracking()
            .Where(s => s.Status == MailSendStatus.Sent && s.EmailFile != null && s.SentAt < before &&
                !db.MailPlanDeliveries.Any(d => d.SendId == s.Id && d.RemindAt != null &&
                    d.ReminderSentAt == null && d.DoneAt == null))
            .Select(s => new { s.Id, s.EmailFile }).Take(100).ToListAsync(ct);
        foreach (var s in done)
        {
            var cleared = await db.MailPlanSends.Where(x => x.Id == s.Id && x.EmailFile == s.EmailFile)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.EmailFile, (string?)null), ct);
            if (cleared > 0) outgoing.Delete(s.EmailFile);
        }
    }

    private static HashSet<string> Delivered(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (MailJson.List(json))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // A day mail or its reminder: subject, HTML and inline images.
    private (string Subject, string Html, Dictionary<string, byte[]> Images) Email(MailPlan plan, MailPlanSend send, bool reminder)
    {
        var (body, images) = InvitationMessageBuilder.RenderBody(send.BodyHtml, mailImages);
        if (!reminder)
            return (send.Subject, Template(plan, body, email.FromName), images);

        var subject = "Reminder: " + send.Subject;
        if (subject.Length > 200) subject = subject[..200];
        var intro = $"<p style=\"margin:0 0 16px;padding:10px 14px;border-radius:8px;background:{ThemeStore.Current.Theme.Colors.Tint};color:{ThemeStore.Current.Theme.Colors.PrimaryDeep}\">" +
            "You asked to be reminded about this mail.</p>";
        return (subject, Template(plan, intro + body, email.FromName), images);
    }

    // Message, then the related event (when linked to one) and a short footer. The body is
    // already sanitized and encoded by RenderBody.
    public static string Template(MailPlan plan, string body, string fromName)
    {
        var ev = plan.Event;
        var eventBlock = ev is null ? "" :
            "<div style=\"margin-top:24px;padding:12px 16px;border:1px solid #e0e0e0;border-radius:8px\">" +
            $"<div style=\"font-weight:700\">{WebUtility.HtmlEncode(ev.Title)}</div>" +
            $"<div style=\"color:#5f6368\">{WebUtility.HtmlEncode(ScheduleText.Starts(ev.StartsAt))}</div>" +
            $"<div style=\"color:#5f6368\">{WebUtility.HtmlEncode(ScheduleText.Ends(ev.EndsAt))}</div>" +
            (string.IsNullOrWhiteSpace(ev.Venue) ? "" : $"<div style=\"color:#5f6368\">{WebUtility.HtmlEncode(ev.Venue)}</div>") +
            "</div>";
        return "<!doctype html><html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#202124\">" +
            "<div style=\"max-width:800px;margin:0 auto;font-size:15px;line-height:1.6\">" + body + eventBlock +
            $"<p style=\"margin-top:28px;font-size:12px;color:#5f6368\">Sent by {WebUtility.HtmlEncode(fromName)}. " +
            "Open My Reminders in the app to be reminded about this mail.</p></div></body></html>";
    }
}

// What a late Viber failure keeps for a mail-plan email: the delivery, not a copy of the email.
public sealed record MailPlanEmailRef(int DeliveryId);

public class MailPlanScheduler(IServiceScopeFactory scopes, ILogger<MailPlanScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                // A restart or interrupted attempt has an uncertain outcome; retrying skips receivers already reached.
                await db.MailPlanSends.Where(s => s.Status == MailSendStatus.Sending && s.LastAttemptAt < now.AddMinutes(-10))
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, MailSendStatus.Failed).SetProperty(s => s.UpdatedAt, now)
                        .SetProperty(s => s.LastError, "Delivery was interrupted. Retry to reach the remaining receivers."), stoppingToken);
                var sendIds = await db.MailPlanSends.AsNoTracking()
                    .Where(s => !s.Plan!.Draft && (s.Status == MailSendStatus.Scheduled || s.Status == MailSendStatus.Queued) &&
                        s.NextAttemptAt <= now && s.Attempts < MailPlanDispatcher.MaxAttempts)
                    .OrderBy(s => s.NextAttemptAt).Select(s => s.Id).Take(10).ToListAsync(stoppingToken);
                var reminderIds = await db.MailPlanDeliveries.AsNoTracking()
                    .Where(d => d.RemindAt != null && d.ReminderSentAt == null && d.DoneAt == null && d.RemindAt <= now)
                    .OrderBy(d => d.RemindAt).Select(d => d.Id).Take(50).ToListAsync(stoppingToken);
                foreach (var id in sendIds)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    using var deliveryScope = scopes.CreateScope();
                    await deliveryScope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().DispatchAsync(id);
                }
                foreach (var id in reminderIds)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    using var reminderScope = scopes.CreateScope();
                    await reminderScope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().RemindAsync(id);
                }
                await MailPlanDispatcher.CleanUpSentEmailsAsync(db,
                    scope.ServiceProvider.GetRequiredKeyedService<EmailFileStore>("outgoing"), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Mail plan scheduler failed; retrying on the next poll."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
