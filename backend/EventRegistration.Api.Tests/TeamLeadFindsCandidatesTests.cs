using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// While typing an email on the Team page, a Captain sees existing Participants they can add.
[Collection("api")]
public class TeamLeadFindsCandidatesTests(ApiFixture api)
{
    private record CandidateBody(int UserId, string FirstName, string? MiddleInitial, string LastName, string Email);

    private static async Task<List<CandidateBody>> Candidates(HttpClient lead, string email) =>
        (await lead.GetFromJsonAsync<List<CandidateBody>>(
            $"/api/teams/mine/candidates?email={Uri.EscapeDataString(email)}"))!;

    [Fact]
    public async Task A_participant_whose_email_starts_with_the_text_is_suggested_with_their_names()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var participant = await api.CreateUserAsync(admin, "Participant");

        var found = await Candidates(team.LeadClient, participant.Email[..^4].ToUpperInvariant());

        var c = Assert.Single(found, x => x.UserId == participant.Id);
        Assert.Equal(participant.Email, c.Email);
        Assert.False(string.IsNullOrWhiteSpace(c.FirstName));
        Assert.False(string.IsNullOrWhiteSpace(c.LastName));
    }

    [Fact]
    public async Task People_already_in_a_team_and_other_roles_are_not_suggested()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        var other = await api.CreateTeamAsync(admin, "Regular");
        var mine = await api.CreateUserAsync(admin, "Participant");
        var elsewhere = await api.CreateUserAsync(admin, "Participant");
        var merchant = await api.CreateUserAsync(admin, "Merchant");
        await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(mine.Email));
        await other.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(elsewhere.Email));

        Assert.Empty(await Candidates(team.LeadClient, mine.Email));
        Assert.Empty(await Candidates(team.LeadClient, elsewhere.Email));
        Assert.Empty(await Candidates(team.LeadClient, merchant.Email));
    }

    [Fact]
    public async Task Fewer_than_three_characters_suggest_nobody()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        await api.CreateUserAsync(admin, "Participant");

        Assert.Empty(await Candidates(team.LeadClient, "pa"));
    }

    [Fact]
    public async Task Suggestions_are_capped_at_eight()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Regular");
        for (var i = 0; i < 9; i++) await api.CreateUserAsync(admin, "Participant");

        Assert.Equal(8, (await Candidates(team.LeadClient, "participant-")).Count);
    }

    [Fact]
    public async Task Only_a_captain_can_look_up_candidates()
    {
        var admin = await api.AdminAsync();
        var participant = await api.CreateUserAsync(admin, "Participant");
        var client = await api.SignInAsync(participant.Email, ApiFixture.UserPassword);

        var asParticipant = await client.GetAsync("/api/teams/mine/candidates?email=participant-");
        var signedOut = await api.Anonymous().GetAsync("/api/teams/mine/candidates?email=participant-");

        Assert.Equal(HttpStatusCode.Forbidden, asParticipant.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
    }
}
