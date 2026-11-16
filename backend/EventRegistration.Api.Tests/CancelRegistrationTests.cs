using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// Admins cancel pending registrations only: a confirmed one may already have a pass out.
// Participants cancel their own registrations, but never once they have checked in.
[Collection("api")]
public class CancelRegistrationTests(ApiFixture api)
{
    private record EventBody(int Id);
    private record MineBody(int Id, int EventId);
    private record CancelBody(int Cancelled, int NotPending);

    private async Task<(HttpClient Admin, int EventId)> EventAsync()
    {
        var admin = await api.AdminAsync();
        var start = DateTime.Today.AddDays(10).AddHours(6);
        var created = await admin.PostAsJsonAsync("/api/events", new
        {
            title = $"Cancel Cup {Guid.NewGuid():N}"[..20],
            description = "",
            startsAt = start,
            endsAt = start.AddHours(12),
            venue = "Course",
            isPublicLinkEnabled = true,
            status = "Published",
        });
        created.EnsureSuccessStatusCode();
        return (admin, (await created.Content.ReadFromJsonAsync<EventBody>())!.Id);
    }

    // A participant registered (pending) for the event; returns their client and registration id.
    private async Task<(HttpClient Player, int RegistrationId)> RegisterAsync(HttpClient admin, int eventId)
    {
        var user = await api.CreateUserAsync(admin, "Participant");
        var player = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        (await player.PostAsJsonAsync("/api/registrations", new { eventId })).EnsureSuccessStatusCode();
        var mine = await player.GetFromJsonAsync<List<MineBody>>("/api/registrations/mine");
        return (player, Assert.Single(mine!, r => r.EventId == eventId).Id);
    }

    private static async Task<bool> StillRegisteredAsync(HttpClient player, int eventId) =>
        (await player.GetFromJsonAsync<List<MineBody>>("/api/registrations/mine"))!.Any(r => r.EventId == eventId);

    private static async Task CheckInAsync(HttpClient admin, int eventId, int registrationId) =>
        (await admin.PostAsJsonAsync($"/api/events/{eventId}/attendance/checkin",
            new { registrationIds = new[] { registrationId } })).EnsureSuccessStatusCode();

    [Fact]
    public async Task Bulk_cancel_removes_pending_registrations_and_skips_confirmed_ones()
    {
        var (admin, eventId) = await EventAsync();
        var (pendingPlayer, pending) = await RegisterAsync(admin, eventId);
        var (confirmedPlayer, confirmed) = await RegisterAsync(admin, eventId);
        await api.ConfirmAsync(admin, eventId, confirmed);

        var res = await admin.PostAsJsonAsync($"/api/events/{eventId}/attendance/cancel",
            new { registrationIds = new[] { pending, confirmed } });

        res.EnsureSuccessStatusCode();
        Assert.Equal(new CancelBody(1, 1), await res.Content.ReadFromJsonAsync<CancelBody>());
        Assert.False(await StillRegisteredAsync(pendingPlayer, eventId));
        Assert.True(await StillRegisteredAsync(confirmedPlayer, eventId));
    }

    [Fact]
    public async Task Bulk_cancel_of_only_confirmed_registrations_is_refused()
    {
        var (admin, eventId) = await EventAsync();
        var (player, confirmed) = await RegisterAsync(admin, eventId);
        await api.ConfirmAsync(admin, eventId, confirmed);

        var res = await admin.PostAsJsonAsync($"/api/events/{eventId}/attendance/cancel",
            new { registrationIds = new[] { confirmed } });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(await StillRegisteredAsync(player, eventId));
    }

    [Fact]
    public async Task An_admin_can_cancel_someone_elses_pending_registration()
    {
        var (admin, eventId) = await EventAsync();
        var (player, reg) = await RegisterAsync(admin, eventId);

        var res = await admin.DeleteAsync($"/api/registrations/{reg}");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.False(await StillRegisteredAsync(player, eventId));
    }

    [Fact]
    public async Task An_admin_cannot_cancel_someone_elses_confirmed_registration()
    {
        var (admin, eventId) = await EventAsync();
        var (player, reg) = await RegisterAsync(admin, eventId);
        await api.ConfirmAsync(admin, eventId, reg);

        var res = await admin.DeleteAsync($"/api/registrations/{reg}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(await StillRegisteredAsync(player, eventId));
    }

    [Fact]
    public async Task A_participant_can_cancel_their_own_pending_registration()
    {
        var (admin, eventId) = await EventAsync();
        var (player, reg) = await RegisterAsync(admin, eventId);

        var res = await player.DeleteAsync($"/api/registrations/{reg}");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.False(await StillRegisteredAsync(player, eventId));
    }

    [Fact]
    public async Task A_participant_cannot_cancel_after_checking_in()
    {
        var (admin, eventId) = await EventAsync();
        var (player, reg) = await RegisterAsync(admin, eventId);
        await api.ConfirmAsync(admin, eventId, reg);
        await CheckInAsync(admin, eventId, reg);

        var res = await player.DeleteAsync($"/api/registrations/{reg}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(await StillRegisteredAsync(player, eventId));
    }

    [Fact]
    public async Task A_participant_cannot_cancel_another_participants_registration()
    {
        var (admin, eventId) = await EventAsync();
        var (owner, reg) = await RegisterAsync(admin, eventId);
        var (other, _) = await RegisterAsync(admin, eventId);

        var res = await other.DeleteAsync($"/api/registrations/{reg}");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.True(await StillRegisteredAsync(owner, eventId));
    }
}
