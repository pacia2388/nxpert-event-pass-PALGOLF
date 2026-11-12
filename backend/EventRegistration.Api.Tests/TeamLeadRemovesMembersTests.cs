using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// A Captain removes members from their own team. The member's account is kept.
[Collection("api")]
public class TeamLeadRemovesMembersTests(ApiFixture api)
{
    private static async Task<TeamBody> MyTeam(HttpClient lead) =>
        (await lead.GetFromJsonAsync<TeamBody>("/api/teams/mine"))!;

    [Fact]
    public async Task A_removed_member_leaves_the_team_and_keeps_their_account()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");
        await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(participant.Email));

        var res = await team.LeadClient.DeleteAsync($"/api/teams/mine/members/{participant.Id}");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var mine = await MyTeam(team.LeadClient);
        Assert.Equal(0, mine.MemberCount);
        Assert.Empty(mine.Members!);
        await api.SignInAsync(participant.Email, ApiFixture.UserPassword);
    }

    [Fact]
    public async Task A_captain_cannot_remove_a_member_of_another_team()
    {
        var admin = await api.AdminAsync();
        var mine = await api.CreateTeamAsync(admin, "Regular");
        var other = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");
        await other.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(participant.Email));

        var res = await mine.LeadClient.DeleteAsync($"/api/teams/mine/members/{participant.Id}");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(1, (await MyTeam(other.LeadClient)).MemberCount);
    }

    [Fact]
    public async Task Removing_someone_who_is_not_in_the_team_is_not_found()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var outsider = await api.CreateUserAsync(admin, "Participant");

        var res = await team.LeadClient.DeleteAsync($"/api/teams/mine/members/{outsider.Id}");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_participant_member_cannot_remove_anyone()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");
        await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(participant.Email));
        var client = await api.SignInAsync(participant.Email, ApiFixture.UserPassword);

        var res = await client.DeleteAsync($"/api/teams/mine/members/{participant.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(1, (await MyTeam(team.LeadClient)).MemberCount);
    }

    [Fact]
    public async Task A_removed_member_can_be_added_back()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");
        await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(participant.Email));
        await team.LeadClient.DeleteAsync($"/api/teams/mine/members/{participant.Id}");

        var again = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(participant.Email));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False((await again.Content.ReadFromJsonAsync<AddResultBody>())!.Created);
        Assert.Equal(1, (await MyTeam(team.LeadClient)).MemberCount);
    }
}
