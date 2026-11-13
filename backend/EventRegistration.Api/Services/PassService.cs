using System.Text;
using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Services;

/// <summary>
/// How a registrant message was delivered: the channels that delivered ("email", "Viber", "SMS"),
/// the channel that failed before a fallback, whether every delivering channel was off
/// (Development outbox), whether a failed email was queued for retry, and whether any channel was
/// tried at all (false = no contact on file).
/// </summary>
public sealed record PassDelivery(IReadOnlyList<string> Channels, string? FellBackFrom = null,
    bool ChannelsOff = false, bool EmailRetryQueued = false, bool Attempted = false)
{
    public static readonly PassDelivery NoContact = new([]);
    public bool Sent => Channels.Count > 0;

    private const string Retrying = "Email could not be sent; retrying in 5 minutes.";

    /// <summary>The admin-facing result, e.g. "Viber failed; pass sent by email."</summary>
    public string Message
    {
        get
        {
            if (!Sent) return EmailRetryQueued ? Retrying : "Pass could not be sent by any channel.";
            if (ChannelsOff) return "Pass sent (channels are disabled).";
            var sentBy = $"pass sent by {string.Join(" and ", Channels)}.";
            var text = FellBackFrom is null ? char.ToUpperInvariant(sentBy[0]) + sentBy[1..] : $"{FellBackFrom} failed; {sentBy}";
            return EmailRetryQueued ? $"{text} {Retrying}" : text;
        }
    }
}

// Why an online meeting invite is being withdrawn.
public enum MeetingCancelReason { RegistrationCancelled, EventCancelled, NowInPerson }

// What a meeting-cancel email needs after its registration is gone (kept on an EmailRetry row).
public sealed record MeetingCancelSnapshot(CalendarInvite.Details Invite, string DisplayName, MeetingCancelReason Reason);

/// <summary>
/// Issues entry passes and other registrant messages. The pass (a QR of the registration token) is
/// only ever sent once a registration has been confirmed by an admin. Each message goes to the
/// registrant's channel with fallback (<see cref="DeliverAsync"/>). Callers save the DbContext.
/// </summary>
public class PassService
{
    private readonly EmailService _email;
    private readonly MobileMessenger _mobile;
    private readonly NotificationDelivery _delivery;
    private readonly IServiceScopeFactory _scopes;
    private readonly QrService _qr;
    private readonly MealStubService _mealStubs;
    private readonly MealStubImageService _stubImages;
    private readonly IConfiguration _config;

    public PassService(EmailService email, MobileMessenger mobile, NotificationDelivery delivery,
        IServiceScopeFactory scopes, QrService qr, MealStubService mealStubs, MealStubImageService stubImages,
        IConfiguration config)
    {
        _email = email;
        _mobile = mobile;
        _delivery = delivery;
        _scopes = scopes;
        _qr = qr;
        _mealStubs = mealStubs;
        _stubImages = stubImages;
        _config = config;
    }

    private string FrontendBase => _config["App:FrontendBaseUrl"]?.TrimEnd('/') ?? "http://localhost:4200";

    // Public "edit your response" link, including events with only the standard contact fields.
    public string EditUrl(Event ev, Registration reg) =>
        $"{FrontendBase}/r/{reg.Token}";

    private static string ChannelOf(Registration reg) =>
        reg.User?.NotificationChannel ?? reg.GuestNotificationChannel ?? NotificationChannels.Email;

    /// <summary>
    /// Sends the "registration confirmed" pass to the registrant over their preferred channel
    /// (an account's <see cref="User.NotificationChannel"/>; guests always use email), stamping
    /// <see cref="Registration.PassEmailSentAt"/> when any channel delivered. Does not call
    /// SaveChanges. Requires <c>reg.User</c> and <c>reg.Session</c> to be loaded.
    /// Set <c>emailOnly</c> for an explicit email resend instead of the preferred channel;
    /// <c>isRetry</c> for a background email retry (email only, never queued again).
    /// </summary>
    public async Task<PassDelivery> SendConfirmationAsync(Event ev, Registration reg, bool emailOnly = false, bool isRetry = false)
    {
        // For a sub-event registration, the pass shows the session's title/time/venue.
        var title = reg.Session is not null ? $"{ev.Title} — {reg.Session.Title}" : ev.Title;
        var startsAt = reg.Session?.StartsAt ?? ev.StartsAt;
        var endsAt = reg.Session?.EndsAt ?? ev.EndsAt;
        var venue = reg.Session?.Venue ?? ev.Venue;
        var name = reg.DisplayName;
        var qrPng = _qr.GeneratePng($"REG:{reg.Token}");

        // Online / hybrid: meeting link + calendar invite. Online-only events have no door, so no
        // QR pass or meal stubs — the join link is the "pass".
        var showPass = EventFormats.HasVenue(ev.Format);
        var meetingUrl = EventFormats.HasOnline(ev.Format) ? CalendarInvite.MeetingUrlFor(ev, reg.Session) : null;
        var invite = CalendarInvite.For(ev, reg, _email.FromName, _email.FromAddress, $"{FrontendBase}/e/{ev.PublicSlug}");

        // Generate (once) and deliver the registrant's individual meal stubs alongside the pass.
        var stubs = showPass ? await _mealStubs.EnsureStubsAsync(reg) : new List<MealStub>();

        // A stub's validity label, from its explicit date range (e.g. "VALID ON OCTOBER 09, 2026"
        // or "VALID SEP 11 – 13, 2026").
        static string ValidityFor(MealStub s) => MealStubImageService.ValidityLabel(s.ValidFrom, s.ValidTo);

        var editUrl = EditUrl(ev, reg);
        var channel = emailOnly ? NotificationChannels.Email : ChannelOf(reg);
        var phone = reg.DisplayPhone;
        var email = reg.DisplayEmail;

        // Mobile: the pass (or, for online-only events, the confirmation text) decides delivery;
        // the join link and meal stubs follow on the same provider, best effort.
        async Task<DeliveryResult> Mobile(IMobileSender m)
        {
            var result = showPass
                ? await m.SendPassAsync(phone!, title, startsAt, endsAt, venue, name, reg.Token.ToString(), qrPng, editUrl)
                : await m.SendTextAsync(phone!, $"Registration confirmed — {title}",
                    $"Hi {name}, your registration for \"{title}\" is confirmed.\n" +
                    $"{ScheduleText.Text(startsAt, endsAt)}" +
                    (meetingUrl is null ? "" : $"\nJoin online: {meetingUrl}") +
                    (editUrl is null ? "" : $"\nEdit your response: {editUrl}"));
            if (!result.Delivered()) return result;
            if (showPass && meetingUrl is not null)
                await m.SendTextAsync(phone!, $"Registration confirmed — {title}", $"You can also join \"{title}\" online: {meetingUrl}");
            if (stubs.Count > 0)
                await m.SendMealStubsAsync(phone!, ev.Title, stubs
                    .Select(s => (Caption: $"{s.Name} ₱{s.Denomination:0.00} · {ValidityFor(s)}", Control: s.ControlNumber))
                    .ToList());
            return result;
        }

        async Task<DeliveryResult> Email()
        {
            // Pass QR + one fully-composed image per meal stub (banner, QR, MEAL STUB, validity,
            // vertical denomination — all baked into a single PNG so it renders identically anywhere).
            var images = showPass ? new Dictionary<string, byte[]> { ["qrcode"] = qrPng } : new();
            var html = new StringBuilder(
                EmailTemplates.Confirmation(title, startsAt, endsAt, venue, name, editUrl, meetingUrl, showPass));
            if (stubs.Count > 0)
            {
                html.Append(
                    "<div style=\"max-width:560px;margin:16px auto 0;\">" +
                    "<h3 style=\"text-align:center;color:#333;margin:8px 0\">Your meal stubs</h3>" +
                    "<p style=\"text-align:center;color:#888;font-size:13px;margin:0 0 8px\">" +
                    "Present each stub at the meal counter. Each stub can be redeemed once.</p></div>");
                foreach (var s in stubs)
                {
                    var cid = "stub-" + s.ControlNumber.ToString("N");
                    images[cid] = _stubImages.Render(
                        s.Color ?? "#0f5fb8", ev.Title, ev.BannerImage, ValidityFor(s), s.Denomination, s.ControlNumber);
                    html.Append(
                        $"<div style=\"text-align:center;margin:6px 0\">" +
                        $"<img src=\"cid:{cid}\" alt=\"Meal stub\" width=\"400\" style=\"max-width:100%;display:block;margin:0 auto\" /></div>");
                }
            }
            // Each confirmation re-sends the event's calendar entry: rank it above earlier revisions.
            if (invite is not null)
            {
                ev.InviteSequence += 2;
                invite = invite with { Sequence = ev.InviteSequence };
            }
            var attachments = invite is null
                ? null
                : new[] { new EmailAttachment(CalendarInvite.FileName, CalendarInvite.Request(invite), CalendarInvite.ContentType(false)) };
            var subject = showPass
                ? $"Registration confirmed — your pass for {title}"
                : $"Registration confirmed — online meeting for {title}";
            return await _email.SendAsync(email!, subject, html.ToString(), inlineImages: images, attachments: attachments);
        }

        var delivery = await DeliverAsync(reg, channel, Mobile,
            string.IsNullOrWhiteSpace(email) ? null : Email, EmailRetryKinds.Pass, isRetry);
        if (delivery.Sent) reg.PassEmailSentAt = DateTime.Now;
        return delivery;
    }

    /// <summary>
    /// Event details changed (time / title / venue / link): sends the registrant an updated calendar
    /// invite (same UID, higher SEQUENCE) plus the new link by mobile when that's their channel.
    /// Only for confirmed registrations (they're the ones who received an invite). Requires
    /// <c>reg.User</c> and <c>reg.Session</c> to be loaded.
    /// </summary>
    public async Task<PassDelivery> SendMeetingUpdateAsync(Event ev, Registration reg, bool isRetry = false)
    {
        var invite = CalendarInvite.For(ev, reg, _email.FromName, _email.FromAddress, $"{FrontendBase}/e/{ev.PublicSlug}");
        var url = EventFormats.HasOnline(ev.Format) ? CalendarInvite.MeetingUrlFor(ev, reg.Session) : null;
        url ??= $"{FrontendBase}/e/{ev.PublicSlug}";
        var title = reg.Session is not null ? $"{ev.Title} — {reg.Session.Title}" : ev.Title;
        var startsAt = reg.Session?.StartsAt ?? ev.StartsAt;
        var endsAt = reg.Session?.EndsAt ?? ev.EndsAt;
        var subject = $"Updated: {title}";

        return await DeliverAsync(reg, ChannelOf(reg),
            m => m.SendTextAsync(reg.DisplayPhone!, subject,
                $"Hi {reg.DisplayName}, the event details for \"{title}\" have changed.\n" +
                $"{ScheduleText.Text(startsAt, endsAt)}\nJoin: {url}"),
            invite is null ? null : () => _email.SendAsync(invite.AttendeeEmail, subject,
                EventFormats.HasOnline(ev.Format) ? EmailTemplates.MeetingUpdated(title, startsAt, endsAt, reg.DisplayName, url) :
                    "<p>The event details have changed. The attached calendar invitation updates your existing event.</p>" +
                    EmailTemplates.Invitation(ev, System.Net.WebUtility.HtmlEncode(url)),
                attachments: new[] { new EmailAttachment(CalendarInvite.FileName, CalendarInvite.Request(invite), CalendarInvite.ContentType(false)) }),
            EmailRetryKinds.MeetingUpdate, isRetry);
    }

    /// <summary>
    /// Withdraws the calendar event (CANCEL invite) by email, and tells a mobile-channel registrant
    /// by text — sent when a confirmed registration is cancelled or the whole event is deleted.
    /// No-op for registrations that never received anything. Requires <c>reg.Session</c> loaded.
    /// </summary>
    public async Task<PassDelivery> SendMeetingCancelAsync(Event ev, Registration reg, MeetingCancelReason reason)
    {
        if (reg.PassEmailSentAt is null) return PassDelivery.NoContact; // never received a pass or invite
        var invite = CalendarInvite.For(ev, reg, _email.FromName, _email.FromAddress, $"{FrontendBase}/e/{ev.PublicSlug}");
        var title = reg.Session is not null ? $"{ev.Title} — {reg.Session.Title}" : ev.Title;
        var text = reason switch
        {
            MeetingCancelReason.EventCancelled => $"Hi {reg.DisplayName}, \"{title}\" has been cancelled.",
            MeetingCancelReason.NowInPerson => $"Hi {reg.DisplayName}, the online meeting for \"{title}\" has been removed.",
            _ => $"Hi {reg.DisplayName}, your registration for \"{title}\" has been cancelled.",
        };
        var snapshot = invite is null ? null : new MeetingCancelSnapshot(invite, reg.DisplayName, reason);
        // The calendar entry is always withdrawn by email (when there is one); a mobile-channel
        // registrant also gets a text, so it is sent as "Both".
        var channel = NotificationChannels.UsesViber(ChannelOf(reg)) ? NotificationChannels.Both : NotificationChannels.Email;
        return await DeliverAsync(reg, channel,
            m => m.SendTextAsync(reg.DisplayPhone!, $"Cancelled: {title}", text),
            snapshot is null ? null : () => SendMeetingCancelEmailAsync(snapshot),
            EmailRetryKinds.MeetingCancel, isRetry: false,
            snapshot is null ? null : JsonSerializer.Serialize(snapshot));
    }

    /// <summary>The meeting-cancel email (CANCEL invite) from its snapshot; also used by the retry.</summary>
    public Task<DeliveryResult> SendMeetingCancelEmailAsync(MeetingCancelSnapshot s)
    {
        var subject = s.Reason switch
        {
            MeetingCancelReason.EventCancelled => $"Cancelled: {s.Invite.Title}",
            MeetingCancelReason.NowInPerson => $"Online meeting removed — {s.Invite.Title}",
            _ => $"Registration cancelled — {s.Invite.Title}",
        };
        return _email.SendAsync(s.Invite.AttendeeEmail, subject,
            EmailTemplates.MeetingCancelled(s.Invite.Title, s.Invite.StartsAt, s.Invite.EndsAt, s.DisplayName, s.Reason),
            attachments: new[] { new EmailAttachment(CalendarInvite.FileName, CalendarInvite.Cancel(s.Invite), CalendarInvite.ContentType(true)) });
    }

    /// <summary>
    /// Notifies the registrant that their event kit has been claimed, over their preferred channel
    /// (account's <see cref="User.NotificationChannel"/>; guests use their chosen channel, email by
    /// default). When <paramref name="claimedBy"/> is null the kit was self-claimed and a generic
    /// message is sent; otherwise it names the claimer. Requires <c>reg.User</c> to be loaded.
    /// </summary>
    public async Task<PassDelivery> SendKitClaimedAsync(Event ev, Registration reg, string? claimedBy, bool isRetry = false)
    {
        var name = reg.DisplayName;
        var text = string.IsNullOrWhiteSpace(claimedBy)
            ? $"Hi {name}, this confirms that your kit for \"{ev.Title}\" has already been claimed."
            : $"Hi {name}, your kit for \"{ev.Title}\" has been claimed by {claimedBy}.";
        var subject = $"Event kit claimed — {ev.Title}";
        var email = reg.DisplayEmail;

        return await DeliverAsync(reg, ChannelOf(reg),
            m => m.SendTextAsync(reg.DisplayPhone!, subject, text),
            string.IsNullOrWhiteSpace(email) ? null : () => _email.SendAsync(email, subject, EmailTemplates.KitClaimed(ev.Title, name, claimedBy)),
            EmailRetryKinds.KitClaimed, isRetry);
    }

    /// <summary>
    /// Sends the registrant their "edit your response" link over their preferred channel — used by
    /// the public-page recovery flow ("lost my link"). Requires <c>reg.Event</c> and <c>reg.User</c>
    /// to be loaded.
    /// </summary>
    public async Task<PassDelivery> SendEditLinkAsync(Event ev, Registration reg, bool isRetry = false)
    {
        var url = EditUrl(ev, reg);
        var name = reg.DisplayName;
        var subject = $"Edit your registration — {ev.Title}";
        var email = reg.DisplayEmail;

        return await DeliverAsync(reg, ChannelOf(reg),
            m => m.SendTextAsync(reg.DisplayPhone!, subject,
                $"Hi {name}, review or update your response for \"{ev.Title}\" here: {url}"),
            string.IsNullOrWhiteSpace(email) ? null : () => _email.SendAsync(email, subject, EmailTemplates.EditLink(ev.Title, name, url)),
            EmailRetryKinds.EditLink, isRetry);
    }

    /// <summary>
    /// Sends one registrant message by channel, each channel tried once:
    /// mobile preferred = the active mobile provider only, then email when its FallbackToEmail is on;
    /// email preferred = email only; Both = mobile and email, each sent, neither falls back.
    /// A mobile-preferred registrant with no phone, or whose country code is not listed for the
    /// active channel (Phone country table), gets email whatever FallbackToEmail says. A failed email never falls back to
    /// mobile; it is queued for a background retry instead (unless this is that retry, which is
    /// email only). <paramref name="email"/> is null when there is no address.
    /// </summary>
    private async Task<PassDelivery> DeliverAsync(Registration reg, string channel,
        Func<IMobileSender, Task<DeliveryResult>> mobile, Func<Task<DeliveryResult>>? email,
        string retryKind, bool isRetry, string? snapshotJson = null)
    {
        if (isRetry) channel = NotificationChannels.Email;
        var route = NotificationChannels.UsesViber(channel) && !string.IsNullOrWhiteSpace(reg.DisplayPhone)
            ? await _mobile.RouteAsync() : null;
        var mobileChain = route is not null && route.Supports(reg.DisplayPhone)
            ? new List<ChannelAttempt> { new(route.Name, () => mobile(route.Sender)) }
            : new List<ChannelAttempt>();
        var emailAttempt = email is null ? null : new ChannelAttempt("email", email);

        var chains = new List<ChainResult>();
        if (channel == NotificationChannels.Both)
        {
            if (mobileChain.Count > 0) chains.Add(await _delivery.DeliverAsync(mobileChain));
            if (emailAttempt is not null) chains.Add(await _delivery.DeliverAsync([emailAttempt]));
        }
        else
        {
            // Mobile preferred: email is the last fallback. Email preferred: the only channel.
            var chain = channel == NotificationChannels.Viber ? mobileChain : new List<ChannelAttempt>();
            if (emailAttempt is not null) chain.Add(emailAttempt with { AfterFailure = route?.Sender.FallbackToEmail ?? true });
            if (chain.Count > 0) chains.Add(await _delivery.DeliverAsync(chain));
        }

        var emailFailed = chains.Any(c => c.Tried.Any(t => t.Channel == "email" && t.Result == DeliveryResult.Failed));
        var queued = emailFailed && !isRetry && await QueueEmailRetryAsync(reg.Id, retryKind, snapshotJson);
        var delivered = chains.Where(c => c.Delivered).ToList();
        return new PassDelivery(
            delivered.Select(c => c.DeliveredBy!).ToList(),
            delivered.Select(c => c.FirstFailed).FirstOrDefault(f => f is not null),
            delivered.Count > 0 && delivered.All(c => c.DeliveredDisabled),
            queued,
            chains.Count > 0);
    }

    // One pending retry per registration + kind (a meeting cancel always gets its own snapshot row).
    // Saved in its own scope so the caller's unsaved changes are not committed with it.
    private async Task<bool> QueueEmailRetryAsync(int registrationId, string kind, string? snapshotJson)
    {
        using var scope = _scopes.CreateScope();
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
