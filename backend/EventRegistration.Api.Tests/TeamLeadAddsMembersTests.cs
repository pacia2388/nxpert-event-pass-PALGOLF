using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// A Team Lead adds Participants to their own team by name and email.
[Collection("api")]
public class TeamLeadAddsMembersTests(ApiFixture api)
{
    private static async Task<TeamBody> MyTeam(HttpClient lead) =>
        (await lead.GetFromJsonAsync<TeamBody>("/api/teams/mine"))!;

    [Fact]
    public async Task An_unknown_email_creates_a_participant_who_joins_the_team()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var email = ApiFixture.NewEmail();

        var res = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
            ApiFixture.Member(email, "Mia", "Santos"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var added = await res.Content.ReadFromJsonAsync<AddResultBody>();
        Assert.True(added!.Created);
        Assert.Equal("Mia Santos", added.Member.FullName);

        var mine = await MyTeam(team.LeadClient);
        Assert.Equal(1, mine.MemberCount);
        Assert.Equal(email, Assert.Single(mine.Members!).Email);
    }

    [Fact]
    public async Task A_new_member_signs_in_as_a_participant_with_the_default_temporary_password()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var email = ApiFixture.NewEmail();
        await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(email));

        var login = await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = "char" });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("Participant", (await login.Content.ReadFromJsonAsync<AuthBody>())!.Role);
    }

    [Fact]
    public async Task An_existing_participant_joins_without_a_new_account()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");

        var res = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
            ApiFixture.Member(participant.Email, "Different", "Name"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var added = await res.Content.ReadFromJsonAsync<AddResultBody>();
        Assert.False(added!.Created);
        Assert.Equal(participant.Id, added.Member.UserId);
        // The account is untouched: its own password still signs in.
        await api.SignInAsync(participant.Email, ApiFixture.UserPassword);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("TL")]
    [InlineData("Merchant")]
    public async Task An_email_that_belongs_to_another_role_is_refused(string role)
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var other = await api.CreateUserAsync(admin, role);

        var res = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
            ApiFixture.Member(other.Email));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(0, (await MyTeam(team.LeadClient)).MemberCount);
    }

    [Fact]
    public async Task A_member_of_one_team_cannot_be_added_to_another()
    {
        var admin = await api.AdminAsync();
        var first = await api.CreateTeamAsync(admin, "Regular");
        var second = await api.CreateTeamAsync(admin, "Regular");
        var email = ApiFixture.NewEmail();
        await first.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(email));

        var again = await first.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(email));
        var elsewhere = await second.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(email));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, elsewhere.StatusCode);
        Assert.Equal(1, (await MyTeam(first.LeadClient)).MemberCount);
        Assert.Equal(0, (await MyTeam(second.LeadClient)).MemberCount);
    }

    [Theory]
    [InlineData("Senior", 8)]
    [InlineData("Regular", 10)]
    public async Task A_team_refuses_members_beyond_its_category_cap(string category, int cap)
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, category);
        for (var i = 0; i < cap; i++)
        {
            var ok = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
                ApiFixture.Member(ApiFixture.NewEmail()));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
        var refusedEmail = ApiFixture.NewEmail();

        var res = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
            ApiFixture.Member(refusedEmail));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var mine = await MyTeam(team.LeadClient);
        Assert.Equal(cap, mine.MemberCount);
        Assert.Equal(cap, mine.MaxMembers);
        // No account was left behind for the refused email.
        var login = await api.Anonymous().PostAsJsonAsync("/api/auth/login",
            new { email = refusedEmail, password = "char" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Theory]
    [InlineData("", "Santos", "mia@example.com")]
    [InlineData("Mia", "  ", "mia@example.com")]
    [InlineData("Mia", "Santos", "not-an-email")]
    [InlineData("Mia", "Santos", "")]
    public async Task Missing_names_or_an_invalid_email_are_rejected(string first, string last, string email)
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");

        var res = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
            ApiFixture.Member(email, first, last));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_team_lead_without_a_team_sees_no_team_and_cannot_add_members()
    {
        var admin = await api.AdminAsync();
        var lead = await api.CreateUserAsync(admin, "TL");
        var client = await api.SignInAsync(lead.Email, ApiFixture.UserPassword);

        var mine = await client.GetAsync("/api/teams/mine");
        var add = await client.PostAsJsonAsync("/api/teams/mine/members",
            ApiFixture.Member(ApiFixture.NewEmail()));

        Assert.Equal(HttpStatusCode.NoContent, mine.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
    }

    [Fact]
    public async Task Only_a_team_lead_can_use_the_team_lead_endpoints()
    {
        var admin = await api.AdminAsync();
        var participant = await api.CreateUserAsync(admin, "Participant");
        var client = await api.SignInAsync(participant.Email, ApiFixture.UserPassword);

        var asParticipant = await client.PostAsJsonAsync("/api/teams/mine/members",
            ApiFixture.Member(ApiFixture.NewEmail()));
        var asAdmin = await admin.GetAsync("/api/teams/mine");
        var signedOut = await api.Anonymous().GetAsync("/api/teams/mine");

        Assert.Equal(HttpStatusCode.Forbidden, asParticipant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
    }
}
