using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// Sign-in never tells a stranger whether an account exists; sign-up checks the profile on the
// server; a password change needs the current password.
[Collection("api")]
public class AuthTests(ApiFixture api)
{
    private static object SignUp(string email, string senateNo = "ID-1001", string? birthdate = "1990-05-01") => new
    {
        firstName = "Mia",
        middleInitial = (string?)null,
        lastName = "Santos",
        senateNo,
        email,
        password = "Passw0rd!",
        phone = (string?)null,
        organization = (string?)null,
        level = "Member",
        photo = (string?)null,
        birthdate,
    };

    private static async Task<string> MessageAsync(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<ErrorBody>())!.Message;

    [Fact]
    public async Task A_wrong_password_and_an_unknown_account_get_the_same_answer()
    {
        var anon = api.Anonymous();

        var wrongPassword = await anon.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFixture.AdminEmail, password = "Wrong123!" });
        var unknown = await anon.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFixture.NewEmail(), password = "Wrong123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await MessageAsync(wrongPassword), await MessageAsync(unknown));
    }

    [Fact]
    public async Task Sign_in_ignores_spaces_and_letter_case_in_the_email()
    {
        var res = await api.Anonymous().PostAsJsonAsync("/api/auth/login",
            new { email = "  ADMIN@Demo.com ", password = ApiFixture.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task A_new_player_can_sign_up_and_then_sign_in()
    {
        var email = ApiFixture.NewEmail();

        (await api.Anonymous().PostAsJsonAsync("/api/auth/register", SignUp(email))).EnsureSuccessStatusCode();

        var signedIn = await api.SignInAsync(email, "Passw0rd!");
        var me = await signedIn.GetFromJsonAsync<UserBody>("/api/auth/me");
        Assert.Equal(email, me!.Email);
        Assert.Equal("Participant", me.Role);
    }

    [Fact]
    public async Task Signing_up_with_an_email_that_has_an_account_is_refused()
    {
        var email = ApiFixture.NewEmail();
        (await api.Anonymous().PostAsJsonAsync("/api/auth/register", SignUp(email))).EnsureSuccessStatusCode();

        var again = await api.Anonymous().PostAsJsonAsync("/api/auth/register", SignUp(email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("   ", "1990-05-01")] // no ID No.
    [InlineData("ID-1001", null)] // no birthdate
    [InlineData("ID-1001", "1899-12-31")] // before 1900
    [InlineData("ID-1001", "2999-01-01")] // in the future
    public async Task Sign_up_without_a_valid_ID_No_or_birthdate_is_refused(string senateNo, string? birthdate)
    {
        var email = ApiFixture.NewEmail();

        var res = await api.Anonymous().PostAsJsonAsync("/api/auth/register", SignUp(email, senateNo, birthdate));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var signIn = await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!" });
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
    }

    private static object Profile(string? currentPassword, string newPassword) => new
    {
        firstName = "Test",
        lastName = "User",
        birthdate = "1990-05-01",
        currentPassword,
        newPassword,
    };

    [Fact]
    public async Task A_player_changes_their_password_with_the_current_one()
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");
        var player = await api.SignInAsync(user.Email, ApiFixture.UserPassword);

        (await player.PutAsJsonAsync("/api/auth/me", Profile(ApiFixture.UserPassword, "N3wPassword")))
            .EnsureSuccessStatusCode();

        await api.SignInAsync(user.Email, "N3wPassword");
    }

    [Theory]
    [InlineData("Wrong123!", "N3wPassword")] // wrong current password
    [InlineData(null, "N3wPassword")] // no current password
    [InlineData(ApiFixture.UserPassword, "short1")] // new password under 8 characters
    public async Task A_password_change_is_refused_without_the_current_password_or_with_a_short_one(
        string? currentPassword, string newPassword)
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");
        var player = await api.SignInAsync(user.Email, ApiFixture.UserPassword);

        var res = await player.PutAsJsonAsync("/api/auth/me", Profile(currentPassword, newPassword));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        await api.SignInAsync(user.Email, ApiFixture.UserPassword); // the old password still works
    }
}
