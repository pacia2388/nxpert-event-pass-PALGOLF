using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

public class InvitationEmailDispatcher(AppDbContext db, EmailService email, IConfiguration config,
    MailImageOptions images, [FromKeyedServices("outgoing")] EmailFileStore outgoing,
    NotificationService notify, ILogger<InvitationEmailDispatcher> log)
{
    public const int MaxAttempts = 5;
    // At most 100 recipients per SMTP message; CC addresses count toward the first batch.
    public const int BatchSize = 100;

    /// <param name="requestedBy">The admin whose request started this send gets the progress;
    /// a scheduled send (null) reports to every admin.</param>
    public async Task DispatchAsync(int id, int? requestedBy = null)
    {
        var now = DateTimeOffset.UtcNow;
        // Claim in SQL, shared by requests and every API instance. An edit changes
        // the rowversion; a claimed message can no longer be edited or cancelled.
        var claimed = await db.InvitationEmails.Where(m => m.Id == id &&
            (m.Status == InvitationEmailStatus.Scheduled || m.Status == InvitationEmailStatus.Queued) &&
            m.NextAttemptAt <= now && m.Attempts < MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, InvitationEmailStatus.Sending)
                .SetProperty(m => m.LastAttemptAt, now).SetProperty(m => m.UpdatedAt, now)
                .SetProperty(m => m.Attempts, m => m.Attempts + 1));
        if (claimed == 0) return;
        db.ChangeTracker.Clear();
        var item = await db.InvitationEmails.Include(m => m.Event).SingleOrDefaultAsync(m => m.Id == id);
        if (item is null) return; // The event may have been deleted after the claim.
        try
        {
            var ev = item.Event ?? throw new InvalidOperationException("The event no longer exists.");
            if (ev.StartsAt == default || ev.EndsAt <= ev.StartsAt)
                throw new ArgumentException("Set a valid start and end date/time for this event.");
            var to = MailJson.List(item.ToJson);
            var cc = MailJson.List(item.CcJson);
            var frontend = config["App:FrontendBaseUrl"]?.TrimEnd('/') ?? "http://localhost:4200";
            var registerUrl = $"{frontend}/e/{Uri.EscapeDataString(ev.PublicSlug)}";
            var template = await LoadTemplateAsync(item, ev);

            await db.Events.Where(e => e.Id == ev.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.InviteSequence, e => e.InviteSequence + 2));
            await db.Entry(ev).ReloadAsync();

            // Keep the invitation rows current: queued before the first attempt, Sent per batch.
            var recipients = to.Concat(cc).ToList();
            var invitations = await db.Invitations.Where(i => i.EventId == ev.Id && recipients.Contains(i.Email)).ToListAsync();
            var known = invitations.Select(i => i.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var address in recipients)
                if (known.Add(address))
                {
                    var invite = new Invitation { EventId = ev.Id, Email = address, Status = InvitationStatus.Queued };
                    db.Invitations.Add(invite);
                    invitations.Add(invite);
                }
            await db.SaveChangesAsync();

            var delivered = Delivered(item.DeliveredRecipientsJson);
            var total = recipients.Count;
            var toPending = to.Where(a => !delivered.Contains(a)).ToList();
            var ccPending = cc.Where(a => !delivered.Contains(a)).ToList();
            var problem = false;
            // At most BatchSize addresses per message; CC goes first, in chunks when it is larger.
            foreach (var (batchTo, batchCc) in MailRecipients.Batches(toPending, ccPending, BatchSize))
            {
                var calendar = CalendarInvite.ForInvitation(ev, email.FromName, email.FromAddress,
                    batchTo, batchCc, registerUrl);
                var result = await email.SendStoredAsync(template, batchTo, batchCc, calendar: calendar);
                if (result != DeliveryResult.Sent)
                {
                    problem = true;
                    break;
                }
                foreach (var address in batchTo.Concat(batchCc)) delivered.Add(address);
                item.DeliveredRecipientsJson = JsonSerializer.Serialize(delivered);
                var reached = delivered.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var invite in invitations.Where(i => i.Status != InvitationStatus.Registered && reached.Contains(i.Email)))
                {
                    invite.Status = InvitationStatus.Sent;
                    invite.SentAt = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();
                await notify.MailSendProgressAsync(requestedBy,
                    new MailSendProgressNotice("invitation", item.Id, delivered.Count, total, 0, false));
            }

            if (!problem)
            {
                item.Status = InvitationEmailStatus.Sent;
                item.SentAt = DateTimeOffset.UtcNow;
                item.LastError = null;
                await notify.MailSendProgressAsync(requestedBy,
                    new MailSendProgressNotice("invitation", item.Id, delivered.Count, total, 0, true));
                // Fully delivered: the saved email (with its attachments) is no longer needed.
                outgoing.Delete(item.EmailFile);
                item.EmailFile = null;
            }
            else
            {
                var last = item.Attempts >= MaxAttempts;
                item.Status = last ? InvitationEmailStatus.Failed : InvitationEmailStatus.Queued;
                item.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(5);
                item.LastError = delivered.Count > 0
                    ? $"Sent to {delivered.Count} of {total} — the rest will be retried."
                    : last
                        ? "SMTP delivery was unavailable after five attempts. Open this email to retry."
                        : "SMTP delivery is unavailable. Delivery will be retried in five minutes.";
                await notify.MailSendProgressAsync(requestedBy,
                    new MailSendProgressNotice("invitation", item.Id, delivered.Count, total, total - delivered.Count, true));
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Invitation email {Id} failed.", id);
            item.Status = InvitationEmailStatus.Failed;
            // Do not disclose SMTP credentials, filesystem paths or database details.
            item.LastError = "Delivery could not be completed. Check the event and email settings, then open this email to retry.";
        }
        item.UpdatedAt = DateTimeOffset.UtcNow;
        // If persistence fails after SMTP accepted the message, leave it in Sending.
        // The worker flags it for manual review rather than automatically duplicating it.
        await db.SaveChangesAsync();
    }

    // Rows saved before attachments/outgoing storage existed are built (and stored) on first use.
    private async Task<MimeKit.MimeMessage> LoadTemplateAsync(InvitationEmail item, Event ev)
    {
        var template = await outgoing.LoadAsync(item.EmailFile);
        if (template is not null) return template;
        var message = await new InvitationMessageBuilder(db, config, images).BuildAsync(ev,
            new(MailJson.List(item.ToJson),
                MailJson.List(item.CcJson), item.Subject, item.BodyHtml),
            enforceRecipientEntryLimit: false,
            sendAt: item.ScheduledAt ?? DateTimeOffset.UtcNow);
        template = await email.BuildStoredAsync(message.Subject, message.Html, message.Images);
        item.EmailFile = await outgoing.SaveAsync(template);
        await db.SaveChangesAsync();
        return template;
    }

    private static HashSet<string> Delivered(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (MailJson.List(json))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public class InvitationEmailScheduler(IServiceScopeFactory scopes, ILogger<InvitationEmailScheduler> log) : BackgroundService
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
                // A restart or interrupted SMTP attempt has an uncertain outcome.
                await db.InvitationEmails.Where(m => m.Status == InvitationEmailStatus.Sending &&
                        m.LastAttemptAt < now.AddMinutes(-10))
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, InvitationEmailStatus.Failed)
                        .SetProperty(m => m.UpdatedAt, now)
                        .SetProperty(m => m.LastError, "Delivery was interrupted. Check the recipient's inbox before retrying to avoid a duplicate."), stoppingToken);
                var ids = await db.InvitationEmails.AsNoTracking()
                    .Where(m => (m.Status == InvitationEmailStatus.Scheduled || m.Status == InvitationEmailStatus.Queued) &&
                        m.NextAttemptAt <= now && m.Attempts < InvitationEmailDispatcher.MaxAttempts)
                    .OrderBy(m => m.NextAttemptAt).Select(m => m.Id).Take(20).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    using var deliveryScope = scopes.CreateScope();
                    await deliveryScope.ServiceProvider.GetRequiredService<InvitationEmailDispatcher>().DispatchAsync(id);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Invitation email scheduler failed; retrying on the next poll."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
