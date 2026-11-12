using EventRegistration.Api.Models;
using EventRegistration.Api.Services;

namespace EventRegistration.Api.Tests;

// Every message that tells a registrant when the event is shows the full schedule:
// "Starts: <date and time>" on one line and "Ends: <date and time>" on the next.
public class MessageScheduleTests
{
    private static readonly DateTime Start = new(2026, 11, 12, 9, 0, 0);
    private static readonly DateTime End = new(2026, 11, 14, 17, 30, 0);

    private static Event NewEvent(DateTime start, DateTime end, string title = "Golf Cup", string venue = "Course") =>
        new() { Title = title, Venue = venue, StartsAt = start, EndsAt = end };

    private static void AssertLines(string html, string starts = "Thu, Nov 12 2026, 9:00 AM",
        string ends = "Sat, Nov 14 2026, 5:30 PM")
    {
        Assert.Contains($"<strong>Starts:</strong> {starts}</p>", html);
        Assert.Contains($"<strong>Ends:</strong> {ends}</p>", html);
    }

    [Fact]
    public void Plain_text_puts_the_end_on_the_next_line() =>
        Assert.Equal("Starts: Thu, Nov 12 2026, 9:00 AM\nEnds: Sat, Nov 14 2026, 5:30 PM", ScheduleText.Text(Start, End));

    [Fact]
    public void Invitation_shows_the_full_start_and_end() =>
        AssertLines(EmailTemplates.Invitation(NewEvent(Start, End), "https://x/e/golf"));

    [Fact]
    public void Same_day_invitation_still_shows_the_full_end_date() =>
        AssertLines(EmailTemplates.Invitation(NewEvent(Start, Start.AddHours(8)), "https://x/e/golf"),
            ends: "Thu, Nov 12 2026, 5:00 PM");

    [Fact]
    public void Confirmation_email_shows_the_full_schedule() =>
        AssertLines(EmailTemplates.Confirmation("Golf Cup", Start, End, "Course", "Mia"));

    [Fact]
    public void Meeting_updated_email_shows_the_full_schedule() =>
        AssertLines(EmailTemplates.MeetingUpdated("Golf Cup", Start, End, "Mia", "https://meet/x"));

    [Fact]
    public void Meeting_cancelled_email_shows_the_full_schedule() =>
        AssertLines(
            EmailTemplates.MeetingCancelled("Golf Cup", Start, End, "Mia", MeetingCancelReason.EventCancelled));

    [Fact]
    public void Pass_email_shows_the_full_schedule() =>
        AssertLines(EmailTemplates.Pass(NewEvent(Start, End), "Mia"));
}

// Names, titles and venues typed by people are shown as text in emails, never as markup.
public class EmailEncodingTests
{
    private const string Markup = "<b>x</b>";
    private const string Encoded = "&lt;b&gt;x&lt;/b&gt;";
    private static readonly DateTime Start = new(2026, 11, 12, 9, 0, 0);

    private static void AssertEncoded(string html)
    {
        Assert.DoesNotContain(Markup, html);
        Assert.Contains(Encoded, html);
    }

    [Fact]
    public void Confirmation_encodes_title_venue_and_name()
    {
        var html = EmailTemplates.Confirmation("T" + Markup, Start, Start.AddHours(1), "V" + Markup, "N" + Markup);
        AssertEncoded(html);
        Assert.Contains("T" + Encoded, html);
        Assert.Contains("V" + Encoded, html);
        Assert.Contains("N" + Encoded, html);
    }

    [Fact]
    public void Meeting_updated_encodes_title_and_name()
    {
        var html = EmailTemplates.MeetingUpdated("T" + Markup, Start, Start.AddHours(1), "N" + Markup, "https://meet/x");
        AssertEncoded(html);
        Assert.Contains("T" + Encoded, html);
        Assert.Contains("N" + Encoded, html);
    }

    [Theory]
    [InlineData(MeetingCancelReason.EventCancelled)]
    [InlineData(MeetingCancelReason.NowInPerson)]
    [InlineData(MeetingCancelReason.RegistrationCancelled)]
    public void Meeting_cancelled_encodes_title_and_name(MeetingCancelReason reason)
    {
        var html = EmailTemplates.MeetingCancelled("T" + Markup, Start, Start.AddHours(1), "N" + Markup, reason);
        AssertEncoded(html);
        Assert.Contains("T" + Encoded, html);
        Assert.Contains("N" + Encoded, html);
    }

    [Fact]
    public void Pass_encodes_title_venue_and_name()
    {
        var html = EmailTemplates.Pass(
            new Event { Title = "T" + Markup, Venue = "V" + Markup, StartsAt = Start, EndsAt = Start.AddHours(1) },
            "N" + Markup);
        AssertEncoded(html);
        Assert.Contains("T" + Encoded, html);
        Assert.Contains("V" + Encoded, html);
        Assert.Contains("N" + Encoded, html);
    }
}
