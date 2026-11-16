using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EventRegistration.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventRegistration.Api.Tests;

// "ñ" can arrive as one character (U+00F1) or as "n" plus a combining tilde (U+0303), depending on
// the keyboard or a paste. They look the same, so a password works whichever way it is typed.
[Collection("api")]
public class PasswordNormalizationTests(ApiFixture api)
{
    private const string Composed = "Contraseña1!";
    private const string Decomposed = "Contraseña1!";

    private static object SignUp(string email, string password) => new
    {
        firstName = "Mia",
        lastName = "Santos",
        senateNo = "ID-1001",
        email,
        password,
        level = "Member",
        birthdate = "1990-05-01",
    };

    private Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password });

    [Theory]
    [InlineData(Decomposed, Composed)]
    [InlineData(Composed, Decomposed)]
    public async Task A_password_with_n_tilde_signs_in_whichever_way_the_tilde_is_typed(string signUpWith, string signInWith)
    {
        var email = ApiFixture.NewEmail();
        (await api.Anonymous().PostAsJsonAsync("/api/auth/register", SignUp(email, signUpWith))).EnsureSuccessStatusCode();

        var res = await SignInAsync(email, signInWith);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task An_account_saved_before_normalization_still_signs_in_with_the_same_typing()
    {
        var email = ApiFixture.NewEmail();
        (await api.Anonymous().PostAsJsonAsync("/api/auth/register", SignUp(email, "Passw0rd!"))).EnsureSuccessStatusCode();
        // The hash as it was made before: from the text exactly as typed, tilde as its own character.
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Decomposed, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var stored = $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
                .Where(u => u.Email == email).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, stored));

        var res = await SignInAsync(email, Decomposed);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task A_combining_tilde_does_not_count_as_the_symbol()
    {
        // Reads as "Passw0rdñ": a letter, not a symbol.
        var res = await api.Anonymous().PostAsJsonAsync("/api/auth/register",
            SignUp(ApiFixture.NewEmail(), "Passw0rdñ"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
