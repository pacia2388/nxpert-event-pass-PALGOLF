using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;

namespace EventRegistration.Api.Services;

/// <summary>What a send did. <see cref="Accepted"/>: delivered now, or queued and will be retried.</summary>
public sealed record EmailSendResult(DeliveryResult Result, bool RetryQueued)
{
    public bool Accepted => Result.Delivered() || RetryQueued;
}

/// <summary>
/// A whole email, before it is sent. Kept for a retry as an .eml file (<see cref="EmailFileStore"/>);
/// older retry rows hold it as JSON in EmailRetry.SnapshotJson.
/// IncludeDefaultCc: also copy the CC addresses from Settings.
/// </summary>
public sealed record QueuedEmail(
    IReadOnlyList<string> To, IReadOnlyList<string> Cc, string Subject, string Html,
    IReadOnlyList<EmailAttachment>? Attachments, IReadOnlyDictionary<string, byte[]>? InlineImages,
    bool IncludeDefaultCc = false);

/// <summary>
/// The email entry point for new code.
/// <see cref="SendAsync"/>: one try now; a real failure is saved and retried in the background
/// (<see cref="EmailRetryDispatcher.MaxAttempts"/>×, <see cref="EmailRetryDispatcher.RetryMinutes"/> min apart).
/// A turned-off email is not queued. <see cref="SendNowAsync"/>: one try, no retry.
/// </summary>
public class EmailSender(EmailService email, EmailFileStore files, IServiceScopeFactory scopes, ILogger<EmailSender> log)
{
    public async Task<EmailSendResult> SendAsync(IReadOnlyList<string> to, string subject, string html,
        IReadOnlyList<string>? cc = null, IReadOnlyList<EmailAttachment>? attachments = null,
        IReadOnlyDictionary<string, byte[]>? inlineImages = null, bool includeDefaultCc = false)
    {
        var message = new QueuedEmail(to, cc ?? [], subject, html, attachments, inlineImages, includeDefaultCc);
        var result = await SendAsync(message);
        if (result != DeliveryResult.Failed) return new EmailSendResult(result, false);

        // The email is saved as a file; the retry row keeps only its name.
        var file = await SaveAsync(message);

        // Own scope: the caller's unsaved changes must not be committed with the retry row.
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.EmailRetries.Add(new EmailRetry
        {
            Kind = EmailRetryKinds.Message,
            EmailFile = file,
            NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(EmailRetryDispatcher.RetryMinutes),
        });
        await db.SaveChangesAsync();
        log.LogWarning("Email \"{Subject}\" failed; retrying in the background.", subject);
        return new EmailSendResult(result, true);
    }

    public Task<DeliveryResult> SendNowAsync(IReadOnlyList<string> to, string subject, string html,
        IReadOnlyList<string>? cc = null, IReadOnlyList<EmailAttachment>? attachments = null,
        IReadOnlyDictionary<string, byte[]>? inlineImages = null, bool includeDefaultCc = false) =>
        SendAsync(new QueuedEmail(to, cc ?? [], subject, html, attachments, inlineImages, includeDefaultCc));

    /// <summary>
    /// Saves the whole email as an .eml file for a later send and returns its file name. The Settings
    /// CC copy is resolved now, so the file is exactly what will be sent.
    /// </summary>
    public async Task<string> SaveAsync(QueuedEmail m)
    {
        var mime = await email.BuildMessageAsync(m.To, m.Cc, m.Subject, m.Html,
            inlineImages: m.InlineImages, attachments: m.Attachments, includeDefaultCc: m.IncludeDefaultCc);
        return await files.SaveAsync(mime);
    }

    // Used by the first try and by EmailRetryDispatcher for an older Message row.
    internal Task<DeliveryResult> SendAsync(QueuedEmail m) =>
        email.SendMessageAsync(m.To, m.Cc, m.Subject, m.Html, inlineImages: m.InlineImages, attachments: m.Attachments,
            includeDefaultCc: m.IncludeDefaultCc);
}
