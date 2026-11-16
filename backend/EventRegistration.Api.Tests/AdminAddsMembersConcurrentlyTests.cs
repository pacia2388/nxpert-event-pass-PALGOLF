using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// Admins adding members at the same time (two browser tabs, or e2e tests in parallel) never get a
// server error: each add succeeds, or is refused with a clean 409 and a message.
[Collection("api")]
public class AdminAddsMembersConcurrentlyTests(ApiFixture api)
{
    private const int Parallel = 8;

    [Fact]
    public async Task Parallel_adds_to_different_teams_all_succeed()
    {
        var admin = await api.AdminAsync();
        var teams = new List<TestTeam>();
        var users = new List<TestUser>();
        for (var i = 0; i < Parallel; i++)
        {
            teams.Add(await api.CreateTeamAsync(admin, "Regular"));
            users.Add(await api.CreateUserAsync(admin, "Participant"));
        }

        var results = await Task.WhenAll(teams.Select((t, i) =>
            admin.PostAsJsonAsync($"/api/teams/{t.Id}/members", new { userId = users[i].Id })));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task Parallel_adds_never_overfill_a_team()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Senior"); // cap 8
        for (var i = 0; i < 6; i++)
            (await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members",
                ApiFixture.Member(ApiFixture.NewEmail()))).EnsureSuccessStatusCode();
        var users = new List<TestUser>();
        for (var i = 0; i < Parallel; i++) users.Add(await api.CreateUserAsync(admin, "Participant"));

        var results = await Task.WhenAll(users.Select(u =>
            admin.PostAsJsonAsync($"/api/teams/{team.Id}/members", new { userId = u.Id })));

        await AssertOkOrCleanConflict(results);
        Assert.Equal(2, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(8, (await admin.GetFromJsonAsync<TeamBody>($"/api/teams/{team.Id}"))!.MemberCount);
    }

    [Fact]
    public async Task Parallel_adds_of_one_user_put_them_in_one_team()
    {
        var admin = await api.AdminAsync();
        var participant = await api.CreateUserAsync(admin, "Participant");
        var teams = new List<TestTeam>();
        for (var i = 0; i < Parallel; i++) teams.Add(await api.CreateTeamAsync(admin, "Regular"));

        var results = await Task.WhenAll(teams.Select(t =>
            admin.PostAsJsonAsync($"/api/teams/{t.Id}/members", new { userId = participant.Id })));

        await AssertOkOrCleanConflict(results);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
    }

    private static async Task AssertOkOrCleanConflict(HttpResponseMessage[] results)
    {
        foreach (var r in results)
        {
            Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            if (r.StatusCode == HttpStatusCode.Conflict)
                Assert.False(string.IsNullOrWhiteSpace((await r.Content.ReadFromJsonAsync<ErrorBody>())!.Message));
        }
    }
}
