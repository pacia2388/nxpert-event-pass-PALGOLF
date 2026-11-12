using EventRegistration.Api.Models;
using EventRegistration.Api.Services;

namespace EventRegistration.Api.Tests;

// Every message that tells a registrant when the event is shows the full schedule:
// "Starts: <date and time>" on one line and "Ends: <date and time>" on the next.
public class MessageScheduleTests
{
    private static readonly DateTime Start = new(2026, 11, 12, 9, 0, 0);
    private static readonly DateTime End = new(2026, 11, 14, 17, 30, 0);
    private const string StartsLine = "Starts: Thu, Nov 12 2026, 9:00 AM";
    private const string EndsLine = "Ends: Sat, Nov 14 2026, 5:30 PM";

    [Fact]
    public void Plain_text_puts_the_end_on_the_next_line() =>
        Assert.Equal(StartsLine + "\n" + EndsLine, ScheduleText.Text(Start, End));

    private static void AssertLines(string html)
    {
        Assert.Contains("<strong>Starts:</strong> Thu, Nov 12 2026, 9:00 AM</p>", html);
        Assert.Contains("<strong>Ends:</strong> Sat, Nov 14 2026, 5:30 PM</p>", html);
    }

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
        AssertLines(EmailTemplates.Pass(
            new Event { Title = "Golf Cup", Venue = "Course", StartsAt = Start, EndsAt = End }, "Mia"));
}
