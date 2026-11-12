using EventRegistration.Api.Models;
using EventRegistration.Api.Services;

namespace EventRegistration.Api.Tests;

// The invitation shows "Starts:" and "Ends:" on separate lines, each with the full date and time.
public class InvitationWhenTests
{
    private static Event NewEvent(DateTime start, DateTime end) =>
        new() { Title = "Golf Cup", Venue = "Course", StartsAt = start, EndsAt = end };

    [Fact]
    public void Same_day_event_shows_the_full_start_and_end()
    {
        var html = EmailTemplates.Invitation(
            NewEvent(new DateTime(2026, 11, 12, 9, 0, 0), new DateTime(2026, 11, 12, 17, 0, 0)), "https://x/e/golf");

        Assert.Contains("<strong>Starts:</strong> Thu, Nov 12 2026, 9:00 AM</p>", html);
        Assert.Contains("<strong>Ends:</strong> Thu, Nov 12 2026, 5:00 PM</p>", html);
    }

    [Fact]
    public void Multi_day_event_shows_the_full_start_and_end()
    {
        var html = EmailTemplates.Invitation(
            NewEvent(new DateTime(2026, 11, 12, 9, 0, 0), new DateTime(2026, 11, 14, 17, 30, 0)), "https://x/e/golf");

        Assert.Contains("<strong>Starts:</strong> Thu, Nov 12 2026, 9:00 AM</p>", html);
        Assert.Contains("<strong>Ends:</strong> Sat, Nov 14 2026, 5:30 PM</p>", html);
    }
}
