using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// Retries a failed email (registrant messages, and any email sent through <see cref="EmailSender"/>) up to
/// <see cref="MaxAttempts"/> times, <see cref="RetryMinutes"/> apart. Email only: Viber/SMS are
/// never re-sent. The email is rebuilt from the registration at each try (current details).
/// </summary>
public class EmailRetryDispatcher(AppDbContext db, PassService pass, EmailSender sender, EmailService email,
    EmailFileStore files, MailPlanDispatcher mailPlans, ILogger<EmailRetryDispatcher> log)
{
    public const int MaxAttempts = 5;
    public const int RetryMinutes = 5;

    public async Task DispatchAsync(int id)
    {
        var now = DateTimeOffset.UtcNow;
        // Claim in SQL so two API instances never send the same retry.
        var claimed = await db.EmailRetries.Where(r => r.Id == id && r.Status == EmailRetryStatus.Queued &&
                r.NextAttemptAt <= now && r.Attempts < MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, EmailRetryStatus.Sending)
                .SetProperty(r => r.LastAttemptAt, now)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1));
        if (claimed == 0) return;
        db.ChangeTracker.Clear();
        var row = await db.EmailRetries.SingleAsync(r => r.Id == id);
        try
        {
            var (result, skip) = await SendAsync(row);
            if (skip is not null)
            {
                row.Status = EmailRetryStatus.Failed;
                row.LastError = skip;
                log.LogInformation("Email retry {Id} dropped: {Reason}", id, skip);
            }
            else if (result.Delivered())
            {
                row.Status = EmailRetryStatus.Sent;
                row.SentAt = DateTimeOffset.UtcNow;
                row.LastError = null;
            }
            else
            {
                var last = row.Attempts >= MaxAttempts;
                row.Status = last ? EmailRetryStatus.Failed : EmailRetryStatus.Queued;
                row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(RetryMinutes);
                row.LastError = last ? "Email could not be sent after five retries." : "Email could not be sent; retrying in 5 minutes.";
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Email retry {Id} failed.", id);
            row.Status = row.Attempts >= MaxAttempts ? EmailRetryStatus.Failed : EmailRetryStatus.Queued;
            row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(RetryMinutes);
            row.LastError = "Email could not be built or sent.";
        }
        await db.SaveChangesAsync();

        // A saved email is no longer needed once the retry is over.
        var finished = row.Status == EmailRetryStatus.Sent || row.Status == EmailRetryStatus.Failed;
        if (finished)
            files.Delete(row.EmailFile);
    }

    // Rebuilds and sends the email. Returns a reason instead when the message no longer applies.
    private async Task<(DeliveryResult Result, string? Skip)> SendAsync(EmailRetry row)
    {
        if (row.Kind == EmailRetryKinds.Message)
        {
            if (row.EmailFile is not null)
            {
                var saved = await files.LoadAsync(row.EmailFile);
                if (saved is null)
                    return (DeliveryResult.Failed, "The saved email file is missing.");
                return (await email.SendMimeAsync(saved), null);
            }

            // Rows queued before emails were saved as files.
            var message = JsonSerializer.Deserialize<QueuedEmail>(row.SnapshotJson ?? "null");
            if (message is null)
                return (DeliveryResult.Failed, "No message to send.");
            return (await sender.SendAsync(message), null);
        }
        if (row.Kind is EmailRetryKinds.MailPlan or EmailRetryKinds.MailReminder)
        {
            var reference = JsonSerializer.Deserialize<MailPlanEmailRef>(row.SnapshotJson ?? "null");
            if (reference is null)
                return (DeliveryResult.Failed, "No mail plan receiver to email.");
            var reminder = row.Kind == EmailRetryKinds.MailReminder;
            return await mailPlans.SendLateEmailAsync(reference.DeliveryId, reminder);
        }
        if (row.Kind == EmailRetryKinds.MeetingCancel)
        {
            var snapshot = JsonSerializer.Deserialize<MeetingCancelSnapshot>(row.SnapshotJson ?? "null");
            return snapshot is null ? (DeliveryResult.Failed, "No calendar entry to cancel.")
                : (await pass.SendMeetingCancelEmailAsync(snapshot), null);
        }

        var reg = await db.Registrations.Include(r => r.User).Include(r => r.Session).Include(r => r.Event)
            .SingleOrDefaultAsync(r => r.Id == row.RegistrationId);
        if (reg?.Event is null) return (DeliveryResult.Failed, "The registration was cancelled or deleted.");
        if (reg.ConfirmedAt is null) return (DeliveryResult.Failed, "The registration is no longer confirmed.");

        var delivery = row.Kind switch
        {
            EmailRetryKinds.Pass => await pass.SendConfirmationAsync(reg.Event, reg, isRetry: true),
            // A self-claim stores the registrant's own name as the claimer.
            EmailRetryKinds.KitClaimed => await pass.SendKitClaimedAsync(reg.Event, reg,
                reg.KitClaimedBy == reg.DisplayName ? null : reg.KitClaimedBy, isRetry: true),
            EmailRetryKinds.EditLink => await pass.SendEditLinkAsync(reg.Event, reg, isRetry: true),
            EmailRetryKinds.MeetingUpdate => await pass.SendMeetingUpdateAsync(reg.Event, reg, isRetry: true),
            _ => PassDelivery.NoContact,
        };
        if (!delivery.Attempted) return (DeliveryResult.Failed, "The registrant has no email address.");
        // Saves PassEmailSentAt (pass) and the calendar sequence bump with the row.
        return (delivery.Sent ? DeliveryResult.Sent : DeliveryResult.Failed, null);
    }
}

public class EmailRetryScheduler(IServiceScopeFactory scopes, ILogger<EmailRetryScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        var lastCleanup = DateTimeOffset.MinValue;
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                // A restart mid-send has an uncertain outcome: give up rather than risk a duplicate.
                await db.EmailRetries.Where(r => r.Status == EmailRetryStatus.Sending && r.LastAttemptAt < now.AddMinutes(-10))
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, EmailRetryStatus.Failed)
                        .SetProperty(r => r.LastError, "Delivery was interrupted."), stoppingToken);
                if (DateTimeOffset.UtcNow - lastCleanup > TimeSpan.FromHours(1))
                {
                    await DeleteOldEmailFilesAsync(scope.ServiceProvider, db, stoppingToken);
                    lastCleanup = DateTimeOffset.UtcNow;
                }
                var ids = await db.EmailRetries.AsNoTracking()
                    .Where(r => r.Status == EmailRetryStatus.Queued && r.NextAttemptAt <= now && r.Attempts < EmailRetryDispatcher.MaxAttempts)
                    .OrderBy(r => r.NextAttemptAt).Select(r => r.Id).Take(20).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    using var retryScope = scopes.CreateScope();
                    await retryScope.ServiceProvider.GetRequiredService<EmailRetryDispatcher>().DispatchAsync(id);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Email retry scheduler failed; retrying on the next poll."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Saved emails nothing can send any more (finished retries, Viber sends past the 24 h report
    // window, broadcasts that never failed) are deleted once old enough.
    private static async Task DeleteOldEmailFilesAsync(IServiceProvider services, AppDbContext db, CancellationToken stoppingToken)
    {
        var pending = await db.EmailRetries.AsNoTracking()
            .Where(r => r.EmailFile != null && (r.Status == EmailRetryStatus.Queued || r.Status == EmailRetryStatus.Sending))
            .Select(r => r.EmailFile!)
            .ToListAsync(stoppingToken);
        services.GetRequiredService<EmailFileStore>().DeleteOld(pending.ToHashSet());
    }
}
