namespace EventRegistration.Api.Services;

/// <summary>
/// The SMS and Viber wording of every registrant / receiver message, in one place (the email HTML
/// stays in <see cref="EmailTemplates"/>). Each method returns the parts sent, in order, as one
/// group. Viber is always ONE part per message (text plus its images; see SinchViberService for how
/// it is sent). Locked by MessageTemplateSnapshotTests: a wording change must be intended and reviewed.
/// </summary>
public static class MessageTemplates
{
    public sealed record Mobile(IReadOnlyList<MobilePart> Sms, IReadOnlyList<MobilePart> Viber);

    // Same parts on SMS and Viber.
    private static Mobile Same(params MobilePart[] parts) => new(parts, parts);

    public sealed record MealStubLine(string Caption, Guid Control)
    {
        public string Number => Control.ToString()[..8].ToUpperInvariant();
    }

    /// <summary>What the confirmation message says; shared by its SMS and Viber versions.</summary>
    public sealed record PassDetails(
        string Title, DateTime StartsAt, DateTime EndsAt, string Venue, string Name, Guid Token, byte[] QrPng,
        string? EditUrl, string? MeetingUrl, bool ShowPass, string EventTitle, IReadOnlyList<MealStubLine> Stubs,
        string? SmsPassUrl);

    /// <summary>
    /// The confirmation: the pass, then the join link (hybrid) and the meal stubs. Online-only
    /// events get the confirmation text with the join link instead of the pass.
    /// SMS: separate texts, with a pass page link in place of the QR (when there is a public base).
    /// Viber: one text (pass, join link, stub list) with the QR first, then the stub images.
    /// </summary>
    public static Mobile Pass(PassDetails p) => new(PassSms(p), [PassViber(p)]);

    private static string ConfirmedSubject(PassDetails p) => $"Registration confirmed — {p.Title}";

    private static string Greeting(PassDetails p) =>
        $"Hi {p.Name}, your registration for \"{p.Title}\" is confirmed.\n{ScheduleText.Text(p.StartsAt, p.EndsAt)}";

    private static string JoinText(PassDetails p) => $"You can also join \"{p.Title}\" online: {p.MeetingUrl}";

    // Online-only events: the confirmation text is the "pass".
    private static string OnlineText(PassDetails p)
    {
        var text = Greeting(p);
        if (p.MeetingUrl is not null)
            text += $"\nJoin online: {p.MeetingUrl}";
        if (p.EditUrl is not null)
            text += $"\nEdit your response: {p.EditUrl}";
        return text;
    }

    private static string StubCount(PassDetails p)
    {
        var plural = p.Stubs.Count == 1 ? "" : "s";
        return $"Your {p.Stubs.Count} meal stub{plural} for \"{p.EventTitle}\" — ";
    }

    private static string NumberedStubs(PassDetails p) =>
        string.Join("\n", p.Stubs.Select(s => $"• {s.Caption} (No. {s.Number})"));

    private static List<MobilePart> PassSms(PassDetails p)
    {
        var parts = new List<MobilePart>();
        if (!p.ShowPass)
        {
            parts.Add(new MobilePart(ConfirmedSubject(p), OnlineText(p)));
        }
        else
        {
            var text = $"{Greeting(p)}\nWhere: {p.Venue}\n";
            if (p.SmsPassUrl is null)
                text += "Please present your QR entry pass at the entrance.";
            else
                text += $"View your QR entry pass: {p.SmsPassUrl}";
            if (!string.IsNullOrWhiteSpace(p.EditUrl))
                text += $"\nUpdate details: {p.EditUrl}";
            parts.Add(new MobilePart(p.Title, text));

            if (p.MeetingUrl is not null)
                parts.Add(new MobilePart(ConfirmedSubject(p), JoinText(p)));
        }

        if (p.Stubs.Count > 0)
            parts.Add(new MobilePart($"meal-stubs_{p.EventTitle}",
                StubCount(p) + "present each at the meal counter:\n" + NumberedStubs(p)));
        return parts;
    }

    private static MobilePart PassViber(PassDetails p)
    {
        // Text blocks are joined by a blank line. The text-only version (no public HTTPS base)
        // differs only in the stub list, which then carries the stub numbers.
        var blocks = new List<string>();
        var textOnlyBlocks = new List<string>();
        var images = new List<MobileImage>();
        var subject = ConfirmedSubject(p);

        if (!p.ShowPass)
        {
            blocks.Add(OnlineText(p));
        }
        else
        {
            subject = p.Title;
            var pass = $"{Greeting(p)}\nWhere: {p.Venue}\nPresent the QR entry pass at the entrance.";
            if (!string.IsNullOrWhiteSpace(p.EditUrl))
                pass += $"\n\nNeed to update your details? {p.EditUrl}";
            blocks.Add(pass);
            images.Add(new MobileImage($"api/passes/{p.Token}/qr.png", p.QrPng));

            if (p.MeetingUrl is not null)
                blocks.Add(JoinText(p));
        }
        textOnlyBlocks.AddRange(blocks);

        if (p.Stubs.Count > 0)
        {
            var intro = StubCount(p) + "present each QR at the meal counter.";
            var captions = string.Join("\n", p.Stubs.Select(s => s.Caption));
            blocks.Add(intro + "\n" + captions);
            textOnlyBlocks.Add(intro + "\n" + NumberedStubs(p));
            // The full composed stub ticket (banner + QR + denomination), not just the QR.
            foreach (var stub in p.Stubs)
                images.Add(new MobileImage($"api/passes/meal-stub/{stub.Control}/stub.png"));
        }

        var text = string.Join("\n\n", blocks);
        var textOnly = string.Join("\n\n", textOnlyBlocks);
        return new MobilePart(subject, text)
        {
            Images = images.Count > 0 ? images : null,
            TextWithoutImages = textOnly != text ? textOnly : null,
        };
    }

    public static Mobile MeetingUpdate(string title, DateTime startsAt, DateTime endsAt, string name, string url)
    {
        var text = $"Hi {name}, the event details for \"{title}\" have changed.\n" +
                   $"{ScheduleText.Text(startsAt, endsAt)}\nJoin: {url}";
        return Same(new MobilePart($"Updated: {title}", text));
    }

    public static Mobile MeetingCancel(string title, string name, MeetingCancelReason reason)
    {
        var text = reason switch
        {
            MeetingCancelReason.EventCancelled => $"Hi {name}, \"{title}\" has been cancelled.",
            MeetingCancelReason.NowInPerson => $"Hi {name}, the online meeting for \"{title}\" has been removed.",
            _ => $"Hi {name}, your registration for \"{title}\" has been cancelled.",
        };
        return Same(new MobilePart($"Cancelled: {title}", text));
    }

    public static Mobile KitClaimed(string eventTitle, string name, string? claimedBy)
    {
        string text;
        if (string.IsNullOrWhiteSpace(claimedBy))
            text = $"Hi {name}, this confirms that your kit for \"{eventTitle}\" has already been claimed.";
        else
            text = $"Hi {name}, your kit for \"{eventTitle}\" has been claimed by {claimedBy}.";
        return Same(new MobilePart($"Event kit claimed — {eventTitle}", text));
    }

    public static Mobile EditLink(string eventTitle, string name, string url)
    {
        var text = $"Hi {name}, review or update your response for \"{eventTitle}\" here: {url}";
        return Same(new MobilePart($"Edit your registration — {eventTitle}", text));
    }

    // Mail plan day: the body as plain text, link addresses kept.
    public static Mobile MailPlan(string subject, string bodyXhtml)
    {
        var text = InvitationMessageBuilder.PlainText(bodyXhtml);
        return Same(new MobilePart(subject, text));
    }

    public static Mobile MailReminder(string subject, string bodyXhtml)
    {
        var text = "You asked to be reminded about this mail.\n\n" + InvitationMessageBuilder.PlainText(bodyXhtml);
        return Same(new MobilePart(subject, text));
    }

    // Organizer's Viber broadcast, as typed.
    public static Mobile Broadcast(string eventTitle, string message) =>
        Same(new MobilePart($"broadcast_{eventTitle}", message));
}
