using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// Usernames come from the name (John Doe → jdoe). Two admins adding people with the same name at
// the same moment both succeed, each with their own username.
[Collection("api")]
public class UsernameCollisionTests(ApiFixture api)
{
    private record UserBody(int Id, string Username);

    [Fact]
    public async Task Users_with_the_same_name_created_at_once_all_get_unique_usernames()
    {
        var admin = await api.AdminAsync();
        var lastName = $"Same{Guid.NewGuid():N}"[..20];

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            admin.PostAsJsonAsync("/api/users", new
            {
                firstName = "Pat",
                lastName,
                email = $"same-{Guid.NewGuid():N}@example.com",
                password = ApiFixture.UserPassword,
                role = "Participant",
                birthdate = "1990-05-01",
                canConfirmRegistrations = false,
            })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var usernames = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<UserBody>())!.Username));
        Assert.Equal(8, usernames.Distinct().Count());
    }

    [Fact]
    public async Task Players_with_the_same_name_signing_up_at_once_all_get_accounts()
    {
        var lastName = $"Same{Guid.NewGuid():N}"[..20];

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            api.Anonymous().PostAsJsonAsync("/api/auth/register", new
            {
                firstName = "Pat",
                lastName,
                senateNo = $"S-{i}",
                level = "Member",
                email = $"signup-{Guid.NewGuid():N}@example.com",
                password = ApiFixture.UserPassword,
                birthdate = "1990-05-01",
            })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var usernames = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<UserBody>())!.Username));
        Assert.Equal(8, usernames.Distinct().Count());
    }
}
