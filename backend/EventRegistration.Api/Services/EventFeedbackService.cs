using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// Automatic post-event feedback: freezes the confirmed audience of an enabled campaign once the
/// event has ended, then sends each person a one-time link through <see cref="MessageDispatcher"/>.
/// The dispatcher's own rules apply (mobile once, email retried in the background).
/// </summary>
public class EventFeedbackService(AppDbContext db, MessageDispatcher dispatcher, EmailSender sender,
    IConfiguration config, TimeProvider time, ILogger<EventFeedbackService> log)
{
    public const int MaxPerRun = 50;

    /// <summary>Prepares every due campaign and sends its pending links.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // A send interrupted mid-way (restart) has an uncertain outcome: mark it Failed for an
        // admin Retry instead of sending the link twice.
        var stale = time.GetUtcNow().UtcDateTime.AddMinutes(-10);
        await db.EventFeedbackRecipients
            .Where(r => r.Status == FeedbackRecipientStatus.Sending && r.SentAt < stale)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, FeedbackRecipientStatus.Failed), cancellationToken);
        await PrepareDueAsync(cancellationToken);
        var eventIds = await db.EventFeedbackCampaigns.AsNoTracking()
            .Where(c => c.Enabled && c.PreparedAt != null)
            .Select(c => c.EventId).ToListAsync(cancellationToken);
        foreach (var eventId in eventIds)
        {
            if (cancellationToken.IsCancellationRequested) return;
            await SendPendingAsync(eventId, cancellationToken);
        }
    }

    /// <summary>Freezes the audience of enabled campaigns whose event has ended, once.</summary>
    public async Task PrepareDueAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow().DateTime;
        var campaigns = await db.EventFeedbackCampaigns.Include(c => c.Event)
            .Where(c => c.Enabled && c.PreparedAt == null).ToListAsync(cancellationToken);
        foreach (var campaign in campaigns)
        {
            if (campaign.Event is not { } ev) continue;
            if (ev.Status == EventStatus.Draft) continue;
            if (ev.EndsAt > now) continue;
            await PrepareAsync(campaign, cancellationToken);
        }
    }

    // Confirmed registrations only (captain-registered team members included); soft-deleted rows
    // are already hidden by the global filter and deactivated users are skipped.
    private async Task PrepareAsync(EventFeedbackCampaign campaign, CancellationToken cancellationToken)
    {
        var registrations = await db.Registrations.Include(r => r.User)
            .Where(r => r.EventId == campaign.EventId && r.ConfirmedAt != null)
            .ToListAsync(cancellationToken);
        // One link per person per event, even with several sub-event registrations (each mobile
        // message is billed): by account, else guest email, else guest phone.
        var people = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations.OrderBy(r => r.Id))
        {
            if (reg.User?.DeactivatedAt is not null) continue;
            var name = reg.DisplayName;
            var email = reg.DisplayEmail;
            var phone = reg.DisplayPhone;
            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone)) continue;
            var person = reg.UserId is int uid ? $"u:{uid}"
                : !string.IsNullOrWhiteSpace(email) ? $"e:{email.Trim()}" : $"p:{phone!.Trim()}";
            if (!people.Add(person)) continue;
            var channel = reg.User?.NotificationChannel ?? reg.GuestNotificationChannel ?? NotificationChannels.Email;
            // A phone-only registrant still gets the link by mobile.
            if (string.IsNullOrWhiteSpace(email) && channel == NotificationChannels.Email)
                channel = NotificationChannels.Viber;
            if (string.IsNullOrWhiteSpace(phone) && channel is NotificationChannels.Viber or NotificationChannels.Both)
                channel = NotificationChannels.Email;
            db.EventFeedbackRecipients.Add(new EventFeedbackRecipient
            {
                EventId = campaign.EventId,
                RegistrationId = reg.Id,
                UserId = reg.UserId,
                Name = name,
                Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                Channel = channel,
            });
        }
        campaign.PreparedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sends pending links for one event; returns how many were attempted.</summary>
    public async Task<int> SendPendingAsync(int eventId, CancellationToken cancellationToken = default)
    {
        var ev = await db.Events.FindAsync([eventId], cancellationToken);
        if (ev is null) return 0;
        var recipients = await db.EventFeedbackRecipients
            .Where(r => r.EventId == eventId && r.Status == FeedbackRecipientStatus.Pending)
            .OrderBy(r => r.Id).Take(MaxPerRun).ToListAsync(cancellationToken);
        var frontend = config["App:FrontendBaseUrl"]?.TrimEnd('/') ?? "http://localhost:4200";
        var sent = 0;
        foreach (var recipient in recipients)
        {
            if (cancellationToken.IsCancellationRequested) break;
            // Claim the row first: the scheduler and an admin Retry never send the same link twice.
            var claimedAt = time.GetUtcNow().UtcDateTime;
            var claimed = await db.EventFeedbackRecipients
                .Where(r => r.Id == recipient.Id && r.Status == FeedbackRecipientStatus.Pending)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, FeedbackRecipientStatus.Sending)
                    .SetProperty(r => r.SentAt, claimedAt), cancellationToken);
            if (claimed == 0) continue;
            var url = $"{frontend}/feedback/{recipient.Token}";
            const string subject = "How was the event?";
            var html = EmailTemplates.FeedbackRequest(ev.Title, recipient.Name, url);
            // Mobile preferred: keep the email so a Viber failure reported later still sends it.
            string? emailFile = null;
            if (recipient.Channel == NotificationChannels.Viber && !string.IsNullOrWhiteSpace(recipient.Email))
                emailFile = await sender.SaveAsync(new QueuedEmail([recipient.Email], [], subject, html, null, null));
            var message = new OutgoingMessage(EmailRetryKinds.FeedbackRequest,
                MessageTemplates.FeedbackRequest(ev.Title, recipient.Name, url),
                email: async () =>
                {
                    if (string.IsNullOrWhiteSpace(recipient.Email)) return DeliveryResult.Failed;
                    // A real failure is saved and retried in the background.
                    var result = await sender.SendAsync([recipient.Email], subject, html, includeDefaultCc: false);
                    return result.Accepted ? DeliveryResult.Sent : DeliveryResult.Failed;
                },
                emailFile: emailFile);
            try
            {
                var delivery = await dispatcher.SendAsync(
                    new Recipient(recipient.Phone, recipient.Channel), message);
                recipient.SentAt = time.GetUtcNow().UtcDateTime;
                recipient.Status = delivery.Sent
                    ? FeedbackRecipientStatus.Sent
                    : FeedbackRecipientStatus.Failed;
                if (delivery.Sent) sent++;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Feedback link {Id} could not be sent.", recipient.Id);
                recipient.Status = FeedbackRecipientStatus.Failed;
                recipient.SentAt = time.GetUtcNow().UtcDateTime;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return sent;
    }

    /// <summary>Puts failed links back in the queue and sends them now.</summary>
    public async Task<int> RetryFailedAsync(int eventId, CancellationToken cancellationToken = default)
    {
        await db.EventFeedbackRecipients
            .Where(r => r.EventId == eventId && r.Status == FeedbackRecipientStatus.Failed)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, FeedbackRecipientStatus.Pending)
                .SetProperty(r => r.SentAt, (DateTime?)null), cancellationToken);
        return await SendPendingAsync(eventId, cancellationToken);
    }

    /// <summary>
    /// Sends the link again to everyone who has not answered yet. The audience is frozen first
    /// when the campaign never prepared one (switch turned on late, or no automation ran).
    /// </summary>
    public async Task<int> ResendUnansweredAsync(int eventId, CancellationToken cancellationToken = default)
    {
        var campaign = await db.EventFeedbackCampaigns
            .FirstOrDefaultAsync(c => c.EventId == eventId, cancellationToken);
        if (campaign is null)
        {
            campaign = new EventFeedbackCampaign { EventId = eventId };
            db.EventFeedbackCampaigns.Add(campaign);
        }
        if (campaign.PreparedAt is null) await PrepareAsync(campaign, cancellationToken);
        await db.EventFeedbackRecipients
            .Where(r => r.EventId == eventId && r.SubmittedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, FeedbackRecipientStatus.Pending)
                .SetProperty(r => r.SentAt, (DateTime?)null), cancellationToken);
        return await SendPendingAsync(eventId, cancellationToken);
    }
}

public class EventFeedbackScheduler(IServiceScopeFactory scopes, TimeProvider time,
    ILogger<EventFeedbackScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(120), time);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<EventFeedbackService>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Feedback scheduler failed; retrying on the next poll."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
