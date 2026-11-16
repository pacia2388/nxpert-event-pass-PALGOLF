using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// A signed-in player registers for an open event once. Closed, full and invite-only events refuse
// new registrations; registering twice keeps the one registration.
[Collection("api")]
public class EventRegistrationRulesTests(ApiFixture api)
{
    private record EventBody(int Id);
    private record MineBody(int Id, int EventId);
    private record RegisterBody(int Created, int AlreadyRegistered);

    private static async Task<int> EventAsync(HttpClient admin, int? capacity = null, bool publicLink = true)
    {
        var start = DateTime.Today.AddDays(10).AddHours(6);
        var created = await admin.PostAsJsonAsync("/api/events", new
        {
            title = $"Rules Cup {Guid.NewGuid():N}"[..20],
            description = "",
            startsAt = start,
            endsAt = start.AddHours(12),
            venue = "Course",
            isPublicLinkEnabled = publicLink,
            status = "Published",
            capacity,
        });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<EventBody>())!.Id;
    }

    private async Task<HttpClient> PlayerAsync(HttpClient admin)
    {
        var user = await api.CreateUserAsync(admin, "Participant");
        return await api.SignInAsync(user.Email, ApiFixture.UserPassword);
    }

    private static async Task<int> RegistrationsAsync(HttpClient player, int eventId) =>
        (await player.GetFromJsonAsync<List<MineBody>>("/api/registrations/mine"))!.Count(r => r.EventId == eventId);

    [Fact]
    public async Task Registering_twice_keeps_one_registration()
    {
        var admin = await api.AdminAsync();
        var eventId = await EventAsync(admin);
        var player = await PlayerAsync(admin);
        (await player.PostAsJsonAsync("/api/registrations", new { eventId })).EnsureSuccessStatusCode();

        var again = await player.PostAsJsonAsync("/api/registrations", new { eventId });

        again.EnsureSuccessStatusCode();
        Assert.Equal(new RegisterBody(0, 1), await again.Content.ReadFromJsonAsync<RegisterBody>());
        Assert.Equal(1, await RegistrationsAsync(player, eventId));
    }

    [Fact]
    public async Task A_closed_event_refuses_registration_until_it_is_reopened()
    {
        var admin = await api.AdminAsync();
        var eventId = await EventAsync(admin);
        (await admin.PostAsync($"/api/events/{eventId}/close", null)).EnsureSuccessStatusCode();
        var player = await PlayerAsync(admin);

        var whileClosed = await player.PostAsJsonAsync("/api/registrations", new { eventId });
        Assert.Equal(HttpStatusCode.Forbidden, whileClosed.StatusCode);
        Assert.Equal(0, await RegistrationsAsync(player, eventId));

        (await admin.PostAsync($"/api/events/{eventId}/reopen", null)).EnsureSuccessStatusCode();
        (await player.PostAsJsonAsync("/api/registrations", new { eventId })).EnsureSuccessStatusCode();
        Assert.Equal(1, await RegistrationsAsync(player, eventId));
    }

    [Fact]
    public async Task A_full_event_refuses_the_next_player()
    {
        var admin = await api.AdminAsync();
        var eventId = await EventAsync(admin, capacity: 1);
        (await (await PlayerAsync(admin)).PostAsJsonAsync("/api/registrations", new { eventId })).EnsureSuccessStatusCode();
        var late = await PlayerAsync(admin);

        var res = await late.PostAsJsonAsync("/api/registrations", new { eventId });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(0, await RegistrationsAsync(late, eventId));
    }

    [Fact]
    public async Task An_invite_only_event_refuses_a_player_who_was_not_invited()
    {
        var admin = await api.AdminAsync();
        var eventId = await EventAsync(admin, publicLink: false);
        var player = await PlayerAsync(admin);

        var res = await player.PostAsJsonAsync("/api/registrations", new { eventId });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(0, await RegistrationsAsync(player, eventId));
    }

    [Fact]
    public async Task Only_admins_can_create_events()
    {
        var admin = await api.AdminAsync();
        var player = await PlayerAsync(admin);
        var start = DateTime.Today.AddDays(10);

        var res = await player.PostAsJsonAsync("/api/events", new
        {
            title = "Player Cup",
            description = "",
            startsAt = start,
            endsAt = start.AddHours(4),
            venue = "Course",
            isPublicLinkEnabled = true,
            status = "Published",
        });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
