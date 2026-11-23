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
/// only ever sent once a registration has been confirmed by an admin. Each message is built from
/// <see cref="MessageTemplates"/> and sent by <see cref="MessageDispatcher"/> (send → fall back).
/// Callers save the DbContext.
/// </summary>
public class PassService
{
    private readonly EmailService _email;
    private readonly MessageDispatcher _dispatcher;
    private readonly SmsSettings _sms;
    private readonly QrService _qr;
    private readonly MealStubService _mealStubs;
    private readonly MealStubImageService _stubImages;
    private readonly IConfiguration _config;

    public PassService(EmailService email, MessageDispatcher dispatcher, SmsSettings sms, QrService qr,
        MealStubService mealStubs, MealStubImageService stubImages, IConfiguration config)
    {
        _email = email;
        _dispatcher = dispatcher;
        _sms = sms;
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

    private static Recipient To(Registration reg, string channel) => new(reg.DisplayPhone, channel, reg.Id);

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
        var email = reg.DisplayEmail;

        // One message: the pass (or, for online-only events, the confirmation text), the join link
        // and the meal stubs travel together, so a failed part sends the email.
        var stubLines = new List<MessageTemplates.MealStubLine>();
        foreach (var s in stubs)
        {
            var caption = $"{s.Name} ₱{s.Denomination:0.00} · {ValidityFor(s)}";
            stubLines.Add(new MessageTemplates.MealStubLine(caption, s.ControlNumber));
        }
        var mobile = MessageTemplates.Pass(new MessageTemplates.PassDetails(
            Title: title, StartsAt: startsAt, EndsAt: endsAt, Venue: venue, Name: name, Token: reg.Token, QrPng: qrPng,
            EditUrl: editUrl, MeetingUrl: meetingUrl, ShowPass: showPass, EventTitle: ev.Title, Stubs: stubLines,
            SmsPassUrl: _sms.PassUrl(reg.Token)));

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
                        s.Color ?? EventRegistration.Api.Services.Theming.ThemeStore.Current.Theme.Colors.Primary, ev.Title, ev.BannerImage, ValidityFor(s), s.Denomination, s.ControlNumber);
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

        Func<Task<DeliveryResult>>? sendEmail = null;
        if (!string.IsNullOrWhiteSpace(email))
            sendEmail = Email;

        var message = new OutgoingMessage(EmailRetryKinds.Pass, mobile, sendEmail);
        var delivery = await _dispatcher.SendAsync(To(reg, channel), message, isRetry);
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

        // No invite = no email address, so no email.
        Func<Task<DeliveryResult>>? sendEmail = null;
        if (invite is not null)
        {
            string html;
            if (EventFormats.HasOnline(ev.Format))
                html = EmailTemplates.MeetingUpdated(title, startsAt, endsAt, reg.DisplayName, url);
            else
                html = "<p>The event details have changed. The attached calendar invitation updates your existing event.</p>" +
                       EmailTemplates.Invitation(ev, System.Net.WebUtility.HtmlEncode(url));
            var ics = new EmailAttachment(CalendarInvite.FileName, CalendarInvite.Request(invite), CalendarInvite.ContentType(false));
            sendEmail = () => _email.SendAsync(invite.AttendeeEmail, subject, html, attachments: new[] { ics });
        }

        var mobile = MessageTemplates.MeetingUpdate(title, startsAt, endsAt, reg.DisplayName, url);
        var message = new OutgoingMessage(EmailRetryKinds.MeetingUpdate, mobile, sendEmail);
        return await _dispatcher.SendAsync(To(reg, ChannelOf(reg)), message, isRetry);
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
        // The email (CANCEL invite) is kept as a snapshot: the registration may be gone by the retry.
        Func<Task<DeliveryResult>>? sendEmail = null;
        string? snapshotJson = null;
        if (invite is not null)
        {
            var snapshot = new MeetingCancelSnapshot(invite, reg.DisplayName, reason);
            sendEmail = () => SendMeetingCancelEmailAsync(snapshot);
            snapshotJson = JsonSerializer.Serialize(snapshot);
        }

        // Documented exception (plan decision 5): the calendar entry is always withdrawn by email
        // (when there is one); a mobile-channel registrant also gets a text, so it is sent as "Both".
        var channel = NotificationChannels.Email;
        if (NotificationChannels.UsesViber(ChannelOf(reg)))
            channel = NotificationChannels.Both;

        var mobile = MessageTemplates.MeetingCancel(title, reg.DisplayName, reason);
        var message = new OutgoingMessage(EmailRetryKinds.MeetingCancel, mobile, sendEmail, snapshotJson);
        return await _dispatcher.SendAsync(To(reg, channel), message);
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
        var subject = $"Event kit claimed — {ev.Title}";
        var email = reg.DisplayEmail;

        Func<Task<DeliveryResult>>? sendEmail = null;
        if (!string.IsNullOrWhiteSpace(email))
            sendEmail = () => _email.SendAsync(email, subject, EmailTemplates.KitClaimed(ev.Title, name, claimedBy));

        var mobile = MessageTemplates.KitClaimed(ev.Title, name, claimedBy);
        var message = new OutgoingMessage(EmailRetryKinds.KitClaimed, mobile, sendEmail);
        return await _dispatcher.SendAsync(To(reg, ChannelOf(reg)), message, isRetry);
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

        Func<Task<DeliveryResult>>? sendEmail = null;
        if (!string.IsNullOrWhiteSpace(email))
            sendEmail = () => _email.SendAsync(email, subject, EmailTemplates.EditLink(ev.Title, name, url));

        var mobile = MessageTemplates.EditLink(ev.Title, name, url);
        var message = new OutgoingMessage(EmailRetryKinds.EditLink, mobile, sendEmail);
        return await _dispatcher.SendAsync(To(reg, ChannelOf(reg)), message, isRetry);
    }
}
