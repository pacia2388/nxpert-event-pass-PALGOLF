using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// A password an admin sets on the user screen meets the same rule as sign-up: 8 to 128 characters
// with a letter, a digit and a symbol, and not the user's email.
[Collection("api")]
public class AdminUserPasswordTests(ApiFixture api)
{
    private static object User(string email, string? password) => new
    {
        firstName = "Rule",
        lastName = "Check",
        email,
        password,
        role = "Participant",
        birthdate = "1990-05-01",
        canConfirmRegistrations = false,
    };

    [Theory]
    [InlineData("Passw0rd1")]   // no symbol
    [InlineData("Password!")]   // no digit
    [InlineData("Pass w0rd1")]  // a space isn't a symbol
    public async Task An_admin_cannot_create_a_user_with_a_weak_password(string password)
    {
        var admin = await api.AdminAsync();

        var res = await admin.PostAsJsonAsync("/api/users", User(ApiFixture.NewEmail(), password));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("symbol", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_admin_cannot_set_the_users_email_as_their_password()
    {
        var admin = await api.AdminAsync();
        // Has a letter, a digit and symbols (@ and .), so only the email rule refuses it.
        var email = $"rule1-{Guid.NewGuid():N}@example.com";

        var res = await admin.PostAsJsonAsync("/api/users", User(email, email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("not your email", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_admin_cannot_change_a_password_to_a_weak_one_but_can_leave_it_blank()
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");

        var weak = await admin.PutAsJsonAsync($"/api/users/{user.Id}", User(user.Email, "Passw0rd1"));
        var blank = await admin.PutAsJsonAsync($"/api/users/{user.Id}", User(user.Email, null));
        var strong = await admin.PutAsJsonAsync($"/api/users/{user.Id}", User(user.Email, "N3wPassword!"));

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        Assert.Equal(HttpStatusCode.OK, strong.StatusCode);
        var signIn = await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "N3wPassword!" });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }
}
