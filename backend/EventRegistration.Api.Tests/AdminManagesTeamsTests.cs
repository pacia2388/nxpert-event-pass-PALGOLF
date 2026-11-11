using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// Admins create teams, assign the Captain and manage members.
[Collection("api")]
public class AdminManagesTeamsTests(ApiFixture api)
{
    private static object Team(int leadId, string category = "Regular", string? description = null) =>
        new { description = description ?? $"Team {Guid.NewGuid():N}", category, teamLeadUserId = leadId };

    private static async Task<TeamBody> Detail(HttpClient admin, int teamId) =>
        (await admin.GetFromJsonAsync<TeamBody>($"/api/teams/{teamId}"))!;

    [Fact]
    public async Task A_new_team_starts_empty_with_its_category_cap()
    {
        var admin = await api.AdminAsync();
        var lead = await api.CreateUserAsync(admin, "Captain");

        var res = await admin.PostAsJsonAsync("/api/teams", Team(lead.Id, "Senior", "Eagles"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var team = await res.Content.ReadFromJsonAsync<TeamBody>();
        Assert.Equal("Eagles", team!.Description);
        Assert.Equal("Senior", team.Category);
        Assert.Equal(lead.Id, team.TeamLeadUserId);
        Assert.Equal(0, team.MemberCount);
        Assert.Equal(8, team.MaxMembers);
    }

    [Theory]
    [InlineData("Participant")]
    [InlineData("Admin")]
    [InlineData("Merchant")]
    public async Task The_team_lead_must_have_the_TL_role(string role)
    {
        var admin = await api.AdminAsync();
        var notALead = await api.CreateUserAsync(admin, role);

        var res = await admin.PostAsJsonAsync("/api/teams", Team(notALead.Id));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_team_lead_leads_only_one_team()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");

        var res = await admin.PostAsJsonAsync("/api/teams", Team(team.Lead.Id));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Theory]
    [InlineData("A", "Regular")]
    [InlineData("Eagles", "Junior")]
    public async Task A_short_name_or_unknown_category_is_rejected(string description, string category)
    {
        var admin = await api.AdminAsync();
        var lead = await api.CreateUserAsync(admin, "Captain");

        var res = await admin.PostAsJsonAsync("/api/teams", Team(lead.Id, category, description));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task An_admin_adds_and_removes_a_participant()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");

        var add = await admin.PostAsJsonAsync($"/api/teams/{team.Id}/members", new { userId = participant.Id });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Equal(participant.Id, Assert.Single((await Detail(admin, team.Id)).Members!).UserId);

        var remove = await admin.DeleteAsync($"/api/teams/{team.Id}/members/{participant.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Empty((await Detail(admin, team.Id)).Members!);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Captain")]
    [InlineData("Merchant")]
    public async Task An_admin_can_only_add_participants(string role)
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var other = await api.CreateUserAsync(admin, role);

        var res = await admin.PostAsJsonAsync($"/api/teams/{team.Id}/members", new { userId = other.Id });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(0, (await Detail(admin, team.Id)).MemberCount);
    }

    [Fact]
    public async Task The_category_cap_also_binds_the_admin()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Senior");
        for (var i = 0; i < 8; i++)
            (await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
                ApiFixture.Member(ApiFixture.NewEmail()))).EnsureSuccessStatusCode();
        var ninth = await api.CreateUserAsync(admin, "Participant");

        var res = await admin.PostAsJsonAsync($"/api/teams/{team.Id}/members", new { userId = ninth.Id });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(8, (await Detail(admin, team.Id)).MemberCount);
    }

    [Fact]
    public async Task A_regular_team_with_nine_members_cannot_become_senior()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        for (var i = 0; i < 9; i++)
            (await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
                ApiFixture.Member(ApiFixture.NewEmail()))).EnsureSuccessStatusCode();

        var res = await admin.PutAsJsonAsync($"/api/teams/{team.Id}", Team(team.Lead.Id, "Senior"));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("Regular", (await Detail(admin, team.Id)).Category);
    }

    [Fact]
    public async Task Deleting_a_team_frees_its_members_and_keeps_their_accounts()
    {
        var admin = await api.AdminAsync();
        var first = await api.CreateTeamAsync(admin, "Regular");
        var second = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");
        (await admin.PostAsJsonAsync($"/api/teams/{first.Id}/members", new { userId = participant.Id }))
            .EnsureSuccessStatusCode();

        var delete = await admin.DeleteAsync($"/api/teams/{first.Id}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/teams/{first.Id}")).StatusCode);
        await api.SignInAsync(participant.Email, ApiFixture.UserPassword);
        var rejoin = await admin.PostAsJsonAsync($"/api/teams/{second.Id}/members", new { userId = participant.Id });
        Assert.Equal(HttpStatusCode.OK, rejoin.StatusCode);
    }

    [Fact]
    public async Task Team_management_is_admin_only()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");
        var participantClient = await api.SignInAsync(participant.Email, ApiFixture.UserPassword);

        var leadLists = await team.LeadClient.GetAsync("/api/teams");
        var leadRemoves = await team.LeadClient.DeleteAsync($"/api/teams/{team.Id}");
        var participantLists = await participantClient.GetAsync("/api/teams");
        var signedOut = await api.Anonymous().GetAsync("/api/teams");

        Assert.Equal(HttpStatusCode.Forbidden, leadLists.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, leadRemoves.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, participantLists.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
    }

    // ---- Users page rules that protect teams ----

    private static object UserEdit(TestUser user, string role) => new
    {
        firstName = "Test", lastName = "User", email = user.Email, role, canConfirmRegistrations = false,
    };

    [Fact]
    public async Task A_team_lead_keeps_the_TL_role_and_account_while_leading_a_team()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");

        var demote = await admin.PutAsJsonAsync($"/api/users/{team.Lead.Id}", UserEdit(team.Lead, "Participant"));
        var delete = await admin.DeleteAsync($"/api/users/{team.Lead.Id}");

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Equal(team.Lead.Id, (await Detail(admin, team.Id)).TeamLeadUserId);
    }

    [Fact]
    public async Task A_team_member_stays_a_participant()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");
        (await admin.PostAsJsonAsync($"/api/teams/{team.Id}/members", new { userId = participant.Id }))
            .EnsureSuccessStatusCode();

        var promote = await admin.PutAsJsonAsync($"/api/users/{participant.Id}", UserEdit(participant, "Merchant"));

        Assert.Equal(HttpStatusCode.Conflict, promote.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_member_account_removes_them_from_the_team()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");
        (await admin.PostAsJsonAsync($"/api/teams/{team.Id}/members", new { userId = participant.Id }))
            .EnsureSuccessStatusCode();

        var delete = await admin.DeleteAsync($"/api/users/{participant.Id}");

        Assert.True(delete.IsSuccessStatusCode);
        Assert.Equal(0, (await Detail(admin, team.Id)).MemberCount);
    }
}
