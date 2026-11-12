using System.Globalization;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// "My Events" and the pass dialog show the full schedule (start – end), so each of the
// participant's registrations carries the event's end as well as its start.
[Collection("api")]
public class MyRegistrationsScheduleTests(ApiFixture api)
{
    private record EventBody(int Id);
    // The app formats schedules in English whatever the machine's culture.
    private static string Invariant(DateTime value) =>
        value.ToString("ddd, MMM d yyyy, h:mm tt", CultureInfo.InvariantCulture);

    private record MineBody(int EventId, DateTime StartsAt, DateTime EndsAt, Guid Token);

    private async Task<(MineBody Reg, DateTime Start, DateTime End)> RegisterAsync()
    {
        var admin = await api.AdminAsync();
        var start = DateTime.Today.AddDays(10).AddHours(7);
        var end = start.AddDays(2).AddHours(10);
        var created = await admin.PostAsJsonAsync("/api/events", new
        {
            title = $"Golf Cup {Guid.NewGuid():N}"[..20], description = "", startsAt = start,
            endsAt = end, venue = "Course", isPublicLinkEnabled = true, status = "Published",
        });
        created.EnsureSuccessStatusCode();
        var ev = (await created.Content.ReadFromJsonAsync<EventBody>())!;
        var user = await api.CreateUserAsync(admin, "Participant");
        var client = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        (await client.PostAsJsonAsync("/api/registrations", new { eventId = ev.Id })).EnsureSuccessStatusCode();

        var mine = await client.GetFromJsonAsync<List<MineBody>>("/api/registrations/mine");
        return (Assert.Single(mine!, r => r.EventId == ev.Id), start, end);
    }

    [Fact]
    public async Task My_registrations_include_the_event_start_and_end()
    {
        var (reg, start, end) = await RegisterAsync();

        Assert.Equal(start, reg.StartsAt);
        Assert.Equal(end, reg.EndsAt);
    }

    [Fact]
    public async Task Entry_pass_page_shows_the_full_schedule()
    {
        var (reg, start, end) = await RegisterAsync();

        var html = await api.Anonymous().GetStringAsync($"/api/passes/{reg.Token}");

        Assert.Contains($"Starts: {Invariant(start)}</p>", html);
        Assert.Contains($"Ends: {Invariant(end)}</p>", html);
    }
}
