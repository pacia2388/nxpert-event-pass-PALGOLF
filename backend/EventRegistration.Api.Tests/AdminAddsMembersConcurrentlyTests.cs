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
}
