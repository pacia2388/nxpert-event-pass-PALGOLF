using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// The full flow: admin creates a team for a TL, the TL adds members, emails the unregistered ones
// a registration link, and each member completes their account through that link.
[Collection("api")]
public class TeamRegistrationNoticeTests(ApiFixture api)
{
    private record NoticeBody(int Sent, int Failed, List<MemberBody> Members);
    private record PrefillBody(string FirstName, string? MiddleInitial, string LastName, string Email);

    private static async Task<MemberBody> AddAsync(TestTeam team, string email, string first = "Mia")
    {
        var res = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
            ApiFixture.Member(email, first, "Santos"));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<AddResultBody>())!.Member;
    }

    private static Task<HttpResponseMessage> SendAsync(TestTeam team, params int[] userIds) =>
        team.LeadClient.PostAsJsonAsync("/api/teams/mine/registration-notice", new { userIds });

    private static object Completion(string password = "NewPass123") => new
    {
        firstName = "Mia", middleInitial = "R", lastName = "Santos", senateNo = "ID-001",
        level = "Member", email = "ignored@example.com", password, birthdate = "1990-05-01",
    };

    [Fact]
    public async Task New_accounts_start_unregistered_and_existing_participants_are_registered()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");

        var created = await AddAsync(team, ApiFixture.NewEmail());
        var existing = await AddAsync(team, participant.Email);

        Assert.False(created.Registered);
        Assert.False(created.NotificationFlag);
        Assert.True(existing.Registered);
    }

    [Fact]
    public async Task Full_flow_sends_only_to_selected_and_member_completes_registration()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var a = await AddAsync(team, ApiFixture.NewEmail(), "Ana");
        var b = await AddAsync(team, ApiFixture.NewEmail(), "Ben");
        var c = await AddAsync(team, ApiFixture.NewEmail(), "Cy");

        // A token minted at creation is not usable before a notice is sent.
        var unsent = await api.RegistrationTokenAsync(a.UserId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await api.Anonymous().GetAsync($"/api/auth/team-registration/{unsent}")).StatusCode);

        var res = await SendAsync(team, a.UserId, b.UserId);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var notice = (await res.Content.ReadFromJsonAsync<NoticeBody>())!;
        Assert.Equal(2, notice.Sent);
        Assert.True(notice.Members.Single(m => m.UserId == a.UserId).NotificationFlag);
        Assert.True(notice.Members.Single(m => m.UserId == b.UserId).NotificationFlag);
        Assert.False(notice.Members.Single(m => m.UserId == c.UserId).NotificationFlag);

        var token = await api.RegistrationTokenAsync(a.UserId);
        Assert.NotEqual(unsent, token);
        var anon = api.Anonymous();
        var prefill = await anon.GetFromJsonAsync<PrefillBody>($"/api/auth/team-registration/{token}");
        Assert.Equal(a.Email, prefill!.Email);
        Assert.Equal("Ana", prefill.FirstName);

        var done = await anon.PostAsJsonAsync($"/api/auth/team-registration/{token}", Completion());
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);

        // New password signs in, the temporary one no longer does, the link is spent.
        await api.SignInAsync(a.Email, "NewPass123");
        var old = await anon.PostAsJsonAsync("/api/auth/login", new { email = a.Email, password = "char" });
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        var again = await anon.PostAsJsonAsync($"/api/auth/team-registration/{token}", Completion());
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        var mine = (await team.LeadClient.GetFromJsonAsync<TeamBody>("/api/teams/mine"))!;
        Assert.True(mine.Members!.Single(m => m.UserId == a.UserId).Registered);
    }

    [Fact]
    public async Task Registered_members_are_exempt()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");
        var existing = await AddAsync(team, participant.Email);

        var res = await SendAsync(team, existing.UserId);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_lead_cannot_email_members_of_another_team()
    {
        var admin = await api.AdminAsync();
        var mine = await api.CreateTeamAsync(admin, "Regular");
        var other = await api.CreateTeamAsync(admin, "Regular");
        var own = await AddAsync(mine, ApiFixture.NewEmail());
        var stranger = await AddAsync(other, ApiFixture.NewEmail());

        var res = await SendAsync(mine, own.UserId, stranger.UserId);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        // Nobody was emailed, not even the lead's own member.
        var team = (await mine.LeadClient.GetFromJsonAsync<TeamBody>("/api/teams/mine"))!;
        Assert.False(team.Members!.Single().NotificationFlag);
    }

    [Fact]
    public async Task Empty_selection_is_refused_and_non_leads_are_forbidden()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(team)).StatusCode);
        var asAdmin = await admin.PostAsJsonAsync("/api/teams/mine/registration-notice", new { userIds = new[] { 1 } });
        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode);
    }

    [Fact]
    public async Task Resending_invalidates_the_previous_link()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var m = await AddAsync(team, ApiFixture.NewEmail());

        await SendAsync(team, m.UserId);
        var first = await api.RegistrationTokenAsync(m.UserId);
        await SendAsync(team, m.UserId);

        var res = await api.Anonymous().GetAsync($"/api/auth/team-registration/{first}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_weak_password_is_refused()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var m = await AddAsync(team, ApiFixture.NewEmail());
        await SendAsync(team, m.UserId);
        var token = await api.RegistrationTokenAsync(m.UserId);

        var res = await api.Anonymous().PostAsJsonAsync($"/api/auth/team-registration/{token}", Completion("short"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
