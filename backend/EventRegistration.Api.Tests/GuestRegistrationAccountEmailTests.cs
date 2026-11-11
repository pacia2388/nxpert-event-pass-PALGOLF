using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// The public (guest) event form refuses an email that already has an account — e.g. a member a
// Team Lead added — so the member registers through their account and is never listed twice.
[Collection("api")]
public class GuestRegistrationAccountEmailTests(ApiFixture api)
{
    private record EventBody(int Id, string PublicSlug);
    private record MineBody(int Id, int EventId);

    private static async Task<EventBody> CreateEventAsync(HttpClient admin)
    {
        var start = DateTime.Today.AddDays(10);
        var res = await admin.PostAsJsonAsync("/api/events", new
        {
            title = $"Golf Day {Guid.NewGuid():N}"[..20], description = "", startsAt = start,
            endsAt = start.AddHours(8), venue = "Course", isPublicLinkEnabled = true, status = "Published",
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<EventBody>())!;
    }

    private static Task<HttpResponseMessage> GuestAsync(HttpClient anon, string slug, string email) =>
        anon.PostAsJsonAsync($"/api/registrations/guest/{slug}", new
        {
            fullName = "Mia Santos", email, phone = "09171234567", senateNo = $"ID-{Guid.NewGuid():N}"[..10],
        });

    [Fact]
    public async Task Team_member_email_is_refused_on_the_guest_form()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var email = ApiFixture.NewEmail();
        var add = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(email));
        add.EnsureSuccessStatusCode();
        var member = (await add.Content.ReadFromJsonAsync<AddResultBody>())!.Member;
        var ev = await CreateEventAsync(admin);

        var res = await GuestAsync(api.Anonymous(), ev.PublicSlug, email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("sign in", (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Message);

        // After completing the account, registering through it creates the only row.
        await team.LeadClient.PostAsJsonAsync("/api/teams/mine/registration-notice", new { userIds = new[] { member.UserId } });
        var token = await api.RegistrationTokenAsync(member.UserId);
        var done = await api.Anonymous().PostAsJsonAsync($"/api/auth/team-registration/{token}", new
        {
            firstName = "Mia", lastName = "Santos", senateNo = "ID-777", level = "Member",
            email, password = "NewPass123", birthdate = "1990-05-01",
        });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var client = await api.SignInAsync(email, "NewPass123");
        Assert.Empty((await client.GetFromJsonAsync<List<MineBody>>("/api/registrations/mine"))!
            .Where(r => r.EventId == ev.Id));
        (await client.PostAsJsonAsync("/api/registrations", new { eventId = ev.Id })).EnsureSuccessStatusCode();
        Assert.Single((await client.GetFromJsonAsync<List<MineBody>>("/api/registrations/mine"))!,
            r => r.EventId == ev.Id);
    }

    [Fact]
    public async Task Unknown_email_can_still_register_as_a_guest()
    {
        var admin = await api.AdminAsync();
        var ev = await CreateEventAsync(admin);

        var res = await GuestAsync(api.Anonymous(), ev.PublicSlug, ApiFixture.NewEmail());

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
