using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// Who a message goes to: phone, channel preference (<see cref="NotificationChannels"/>) and, for a
/// registrant, the registration (email retry rebuild and the Viber tag).
/// </summary>
public sealed record Recipient(string? Phone, string Channel, int? RegistrationId = null);

/// <summary>
/// The one send → fall back flow for every registrant / receiver message. Each channel is tried once:
/// <list type="bullet">
/// <item>Mobile preferred: the active provider only, then email when its FallbackToEmail is on
/// (a later Viber failure report sends the email too: the send is tagged).</item>
/// <item>Both: mobile and email, neither falls back (the email has its own retry).</item>
/// <item>Email preferred: email only.</item>
/// </list>
/// No phone, or a country code not listed for the provider (Phone country table): mobile is skipped
/// and email is sent. A failed registrant email is queued for the background retry, never sent by
/// mobile; messages without a registration retry their email themselves (EmailSender, mail plan).
/// </summary>
public class MessageDispatcher(MobileMessenger mobile, NotificationDelivery delivery, IServiceScopeFactory scopes)
{
    /// <param name="isRetry">Background email retry: email only, never queued again.</param>
    /// <param name="route">Forces the provider (the Viber-only broadcast); default the active one.</param>
    /// <param name="fallbackToEmail">Overrides the provider's FallbackToEmail, also for an unlisted
    /// country (the broadcast dialog's tick box).</param>
    public async Task<PassDelivery> SendAsync(Recipient to, OutgoingMessage message, bool isRetry = false,
        MobileRoute? route = null, bool? fallbackToEmail = null)
    {
        var channel = to.Channel;
        if (isRetry)
            channel = NotificationChannels.Email;

        // The mobile provider, only when this channel uses mobile and there is a phone.
        var usesMobile = NotificationChannels.UsesViber(channel) && !string.IsNullOrWhiteSpace(to.Phone);
        if (!usesMobile)
            route = null;
        else if (route is null)
            route = await mobile.RouteAsync();

        var fallback = fallbackToEmail ?? route?.Sender.FallbackToEmail ?? true;
        var phoneSupported = route is not null && route.Supports(to.Phone);

        ChannelAttempt? emailAttempt = null;
        if (message.Email is not null)
            emailAttempt = new ChannelAttempt("email", message.Email);

        var tag = TagFor(to, message, channel, route, fallback, emailAttempt is not null);

        var mobileChain = new List<ChannelAttempt>();
        if (phoneSupported)
            mobileChain.Add(new ChannelAttempt(route!.Name, () => route.Sender.SendAsync(to.Phone!, message, tag)));

        var chains = new List<ChainResult>();
        if (channel == NotificationChannels.Both)
        {
            // Both: each channel on its own, neither falls back.
            if (mobileChain.Count > 0)
                chains.Add(await delivery.DeliverAsync(mobileChain));
            if (emailAttempt is not null)
                chains.Add(await delivery.DeliverAsync([emailAttempt]));
        }
        else
        {
            // Mobile preferred: mobile, then email as the fallback. Email preferred: email only.
            var mobilePreferred = channel == NotificationChannels.Viber;
            var chain = new List<ChannelAttempt>();
            if (mobilePreferred)
                chain.AddRange(mobileChain);

            // An unlisted country gets the email, unless the caller turned the fallback off.
            var emailAllowed = !mobilePreferred || phoneSupported || fallbackToEmail != false;
            if (emailAttempt is not null && emailAllowed)
                chain.Add(emailAttempt with { AfterFailure = fallback });

            if (chain.Count > 0)
                chains.Add(await delivery.DeliverAsync(chain));
        }

        var queued = false;
        var emailFailed = chains.Any(c => c.Tried.Any(t => t.Channel == "email" && t.Result == DeliveryResult.Failed));
        if (emailFailed && !isRetry && to.RegistrationId is int registrationId)
            queued = await QueueEmailRetryAsync(registrationId, message.Kind, message.EmailSnapshotJson);

        var delivered = chains.Where(c => c.Delivered).ToList();
        var deliveredBy = delivered.Select(c => c.DeliveredBy!).ToList();
        var fellBackFrom = delivered.Select(c => c.FirstFailed).FirstOrDefault(f => f is not null);
        var allOff = delivered.Count > 0 && delivered.All(c => c.DeliveredDisabled);
        return new PassDelivery(deliveredBy, fellBackFrom, allOff, queued, Attempted: chains.Count > 0);
    }

    // A send is tagged when a later Viber failure must still send the email: mobile preferred,
    // fallback on, there is an email, and it can be rebuilt (registration, snapshot) or was saved (file).
    private static MobileTag? TagFor(Recipient to, OutgoingMessage message, string channel, MobileRoute? route,
        bool fallback, bool hasEmail)
    {
        if (channel != NotificationChannels.Viber || route is null || !fallback || !hasEmail)
            return null;
        if (to.RegistrationId is not null)
            return new MobileTag(to.RegistrationId, message.Kind);
        if (message.EmailSnapshotJson is not null || message.EmailFile is not null)
            return new MobileTag(null, message.Kind, message.EmailSnapshotJson, message.EmailFile);
        return null;
    }

    // One pending retry per registration + kind (a meeting cancel always gets its own snapshot row).
    // Saved in its own scope so the caller's unsaved changes are not committed with it.
    private async Task<bool> QueueEmailRetryAsync(int registrationId, string kind, string? snapshotJson)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (snapshotJson is null && await db.EmailRetries.AnyAsync(r => r.RegistrationId == registrationId && r.Kind == kind &&
                (r.Status == EmailRetryStatus.Queued || r.Status == EmailRetryStatus.Sending)))
            return true;
        db.EmailRetries.Add(new EmailRetry
        {
            RegistrationId = registrationId,
            Kind = kind,
            SnapshotJson = snapshotJson,
            NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(EmailRetryDispatcher.RetryMinutes),
        });
        await db.SaveChangesAsync();
        return true;
    }
}
