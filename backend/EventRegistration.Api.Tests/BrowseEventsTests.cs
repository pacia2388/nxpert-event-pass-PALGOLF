using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// Browse Events and the Calendar list self-serve events: published or closed, with the public link on.
// Closed events appear under their own tab; drafts and invite-only events stay hidden.
[Collection("api")]
public class BrowseEventsTests(ApiFixture api)
{
    private record EventBody(int Id, string Status);

    private static async Task<int> CreateAsync(HttpClient admin, string status, bool publicLink)
    {
        var start = DateTime.Today.AddDays(10).AddHours(7);
        var created = await admin.PostAsJsonAsync("/api/events", new
        {
            title = $"Browse Cup {Guid.NewGuid():N}"[..22], description = "", startsAt = start,
            endsAt = start.AddHours(8), venue = "Course", isPublicLinkEnabled = publicLink, status,
        });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<EventBody>())!.Id;
    }

    [Fact]
    public async Task Browse_lists_closed_events_but_not_drafts_or_invite_only_ones()
    {
        var admin = await api.AdminAsync();
        var published = await CreateAsync(admin, "Published", publicLink: true);
        var closed = await CreateAsync(admin, "Published", publicLink: true);
        (await admin.PostAsync($"/api/events/{closed}/close", null)).EnsureSuccessStatusCode();
        var draft = await CreateAsync(admin, "Draft", publicLink: true);
        var closedInviteOnly = await CreateAsync(admin, "Published", publicLink: false);
        (await admin.PostAsync($"/api/events/{closedInviteOnly}/close", null)).EnsureSuccessStatusCode();

        var user = await api.CreateUserAsync(admin, "Participant");
        var client = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        var browse = (await client.GetFromJsonAsync<List<EventBody>>("/api/events/browse"))!;

        Assert.Equal("Published", Assert.Single(browse, e => e.Id == published).Status);
        Assert.Equal("Closed", Assert.Single(browse, e => e.Id == closed).Status);
        Assert.DoesNotContain(browse, e => e.Id == draft);
        Assert.DoesNotContain(browse, e => e.Id == closedInviteOnly);
    }

    private record CalendarEvent(int Id, string Status);
    private record CalendarBody(List<CalendarEvent> Events);

    [Fact]
    public async Task Calendar_shows_a_participant_the_same_closed_events_as_Browse()
    {
        var admin = await api.AdminAsync();
        var closed = await CreateAsync(admin, "Published", publicLink: true);
        (await admin.PostAsync($"/api/events/{closed}/close", null)).EnsureSuccessStatusCode();

        var user = await api.CreateUserAsync(admin, "Participant");
        var client = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        var from = DateTime.Today.ToString("yyyy-MM-dd");
        var to = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");
        var calendar = (await client.GetFromJsonAsync<CalendarBody>($"/api/calendar?from={from}&to={to}"))!;

        Assert.Equal("Closed", Assert.Single(calendar.Events, e => e.Id == closed).Status);
    }
}
