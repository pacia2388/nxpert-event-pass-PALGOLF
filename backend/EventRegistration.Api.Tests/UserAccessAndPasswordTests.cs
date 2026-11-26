using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventRegistration.Api.Tests;

// Deactivation, password setup links, forgot password, invite resolution and the mail limits.
[Collection("api")]
public class UserAccessAndPasswordTests(ApiFixture api)
{
    private static object NewUser(string email, string? password, string role = "Participant") => new
    {
        firstName = "Link",
        lastName = "Check",
        email,
        password,
        role,
        birthdate = "1990-05-01",
        canConfirmRegistrations = false,
    };

    private static readonly Regex SetPasswordLink = new(@"/set-password/([A-Za-z0-9_-]{43})");

    private static async Task<string> TokenFromEmailAsync(string email)
    {
        // Import links are sent in the background: wait for the email to arrive.
        for (var i = 0; i < 50 && !ApiFixture.Sent("email", email).Any(f => f.EndsWith(".eml")); i++)
            await Task.Delay(100);
        var file = ApiFixture.Sent("email", email).Single(f => f.EndsWith(".eml"));
        // Read the decoded HTML body (a raw .eml may wrap long lines).
        var message = await MimeKit.MimeMessage.LoadAsync(file);
        var match = SetPasswordLink.Match(message.HtmlBody ?? message.TextBody ?? "");
        Assert.True(match.Success, "The set-password link was not in the email.");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task A_deactivated_user_is_refused_then_reactivation_restores_access()
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");
        var client = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var deactivate = await admin.PostAsync($"/api/users/{user.Id}/deactivate", null);
        var meWhileDeactivated = await client.GetAsync("/api/auth/me");
        var login = await api.Anonymous().PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = ApiFixture.UserPassword });

        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, meWhileDeactivated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("Invalid username/email or password", await login.Content.ReadAsStringAsync());

        var reactivate = await admin.PostAsync($"/api/users/{user.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_deactivate_their_own_account()
    {
        var admin = await api.AdminAsync();
        var me = await admin.GetFromJsonAsync<ProfileBody>("/api/auth/me");

        var res = await admin.PostAsync($"/api/users/{me!.Id}/deactivate", null);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task An_admin_created_user_without_a_password_sets_it_through_the_link_once()
    {
        var admin = await api.AdminAsync();
        var email = ApiFixture.NewEmail();
        var created = await admin.PostAsJsonAsync("/api/users", NewUser(email, null));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        // No password: neither a guess nor the old shared ones sign in.
        foreach (var guess in new[] { "12345", "char", "Passw0rd!" })
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = guess })).StatusCode);

        var token = await TokenFromEmailAsync(email);
        var info = await api.Anonymous().GetFromJsonAsync<SetPasswordInfoBody>($"/api/auth/set-password/{token}");
        Assert.True(info!.Valid);
        Assert.Equal(email, info.Email);

        var weak = await api.Anonymous().PostAsJsonAsync($"/api/auth/set-password/{token}",
            new { password = "Passw0rd1" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var set = await api.Anonymous().PostAsJsonAsync($"/api/auth/set-password/{token}",
            new { password = "N3wPassword!" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("Participant", (await set.Content.ReadFromJsonAsync<AuthBody>())!.Role);
        await api.SignInAsync(email, "N3wPassword!");

        // Single-use: the link is spent.
        Assert.False((await api.Anonymous().GetFromJsonAsync<SetPasswordInfoBody>($"/api/auth/set-password/{token}"))!.Valid);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await api.Anonymous().PostAsJsonAsync($"/api/auth/set-password/{token}", new { password = "Another1!" })).StatusCode);
    }

    [Fact]
    public async Task An_expired_set_password_link_fails()
    {
        var admin = await api.AdminAsync();
        var email = ApiFixture.NewEmail();
        (await admin.PostAsJsonAsync("/api/users", NewUser(email, null))).EnsureSuccessStatusCode();
        var token = await TokenFromEmailAsync(email);

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            // Only the hash is stored, never the token from the link.
            Assert.Equal(PasswordSetupService.HashToken(token), user.PasswordSetupTokenHash);
            Assert.DoesNotContain(token, user.PasswordSetupTokenHash!);
            user.PasswordSetupTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.False((await api.Anonymous().GetFromJsonAsync<SetPasswordInfoBody>($"/api/auth/set-password/{token}"))!.Valid);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await api.Anonymous().PostAsJsonAsync($"/api/auth/set-password/{token}", new { password = "N3wPassword!" })).StatusCode);
    }

    [Fact]
    public async Task Imported_users_have_no_password_and_get_a_link()
    {
        var admin = await api.AdminAsync();
        var email = ApiFixture.NewEmail();

        var res = await admin.PostAsJsonAsync("/api/users/import", new
        {
            rows = new[] { new { firstName = "Imported", lastName = "User", email, birthdate = "1990-05-01" } },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var result = await res.Content.ReadFromJsonAsync<ImportBody>();
        Assert.Equal(1, result!.Created);
        Assert.Equal(1, result.LinksQueued);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = "12345" })).StatusCode);
        // The link waits as a row marker; the poller (or this call in a test) sends it.
        await api.Services.GetRequiredService<PasswordLinkQueue>().ProcessDueAsync();
        await TokenFromEmailAsync(email);
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_and_is_rate_limited()
    {
        var admin = await api.AdminAsync();
        var known = await api.CreateUserAsync(admin, "Participant");
        var anon = api.Anonymous();

        var knownRes = await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = known.Email });
        var unknownRes = await anon.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = ApiFixture.NewEmail() });

        Assert.Equal(HttpStatusCode.OK, knownRes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownRes.StatusCode);
        Assert.Equal(await knownRes.Content.ReadAsStringAsync(), await unknownRes.Content.ReadAsStringAsync());
        await TokenFromEmailAsync(known.Email);

        // Drain the per-IP window on a host of its own: every test client shares one IP, so
        // draining the shared host would refuse the other forgot-password tests for a minute.
        await using var own = new ApiFixture();
        var ownAnon = own.Anonymous();
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 30 && last != HttpStatusCode.TooManyRequests; i++)
            last = (await ownAnon.PostAsJsonAsync("/api/auth/forgot-password", new { email = ApiFixture.NewEmail() }))
                .StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task Forgot_password_sends_the_link_by_email_whatever_the_channel()
    {
        var admin = await api.AdminAsync();
        var email = ApiFixture.NewEmail();
        var phone = ApiFixture.NewPhone();
        (await admin.PostAsJsonAsync("/api/users", new
        {
            firstName = "Viber",
            lastName = "Only",
            email,
            password = ApiFixture.UserPassword,
            role = "Participant",
            birthdate = "1990-05-01",
            phone,
            notificationChannel = "Viber",
        })).EnsureSuccessStatusCode();

        (await api.Anonymous().PostAsJsonAsync("/api/auth/forgot-password", new { email }))
            .EnsureSuccessStatusCode();

        // The visitor typed an email address: the link goes there, never to the phone.
        await TokenFromEmailAsync(email);
        Assert.Empty(ApiFixture.Sent("viber", phone));
        Assert.Empty(ApiFixture.Sent("sms", phone));
    }

    [Fact]
    public async Task The_admin_password_link_action_sends_the_link_by_email_whatever_the_channel()
    {
        var admin = await api.AdminAsync();
        var email = ApiFixture.NewEmail();
        var phone = ApiFixture.NewPhone();
        var created = await admin.PostAsJsonAsync("/api/users", new
        {
            firstName = "Viber",
            lastName = "Only",
            email,
            password = ApiFixture.UserPassword,
            role = "Participant",
            birthdate = "1990-05-01",
            phone,
            notificationChannel = "Viber",
        });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<ProfileBody>())!.Id;

        (await admin.PostAsync($"/api/users/{id}/password-link", null)).EnsureSuccessStatusCode();

        await TokenFromEmailAsync(email);
        Assert.Empty(ApiFixture.Sent("viber", phone));
        Assert.Empty(ApiFixture.Sent("sms", phone));
    }

    [Fact]
    public async Task The_invite_resolve_never_hands_back_a_sign_in_token()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var user = await api.CreateUserAsync(admin, "Participant");

        Guid token;
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invite = new Invitation { EventId = ev.Id, Email = user.Email, Status = InvitationStatus.Sent };
            db.Invitations.Add(invite);
            await db.SaveChangesAsync();
            token = invite.Token;
        }

        var res = await api.Anonymous().PostAsync($"/api/auth/invite/{token}", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("registered").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("auth", out _));
        Assert.DoesNotContain("token", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mail_limits_come_from_configuration()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var limits = api.Services.GetRequiredService<MailImageOptions>();
        var original = limits.MaxImageBytes;
        try
        {
            limits.MaxImageBytes = 1000; // about one kilobyte, smaller than the image below
            var png = new byte[1100];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
            Encoding.ASCII.GetBytes("IHDR").CopyTo(png, 12);
            BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 10);
            BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 10);
            var image = "data:image/png;base64," + Convert.ToBase64String(png);

            var res = await admin.PostAsync($"/api/events/{ev.Id}/invitations/compose",
                ApiFixture.MailForm(new
                {
                    to = new[] { ApiFixture.NewEmail() },
                    cc = Array.Empty<string>(),
                    subject = "Limits",
                    bodyHtml = $"<div><p>Hi</p><img src=\"{image}\" /></div>",
                }));

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Contains("images", await res.Content.ReadAsStringAsync());
        }
        finally
        {
            limits.MaxImageBytes = original;
        }
    }

    [Fact]
    public async Task A_set_password_link_is_never_copied_to_the_settings_cc_list()
    {
        var admin = await api.AdminAsync();
        var cc = ApiFixture.NewEmail();
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SettingsService>()
                .SetAsync(SettingKeys.EmailCc, cc);
        try
        {
            var email = ApiFixture.NewEmail();
            (await admin.PostAsJsonAsync("/api/users", NewUser(email, null))).EnsureSuccessStatusCode();

            var file = ApiFixture.Sent("email", email).Single(f => f.EndsWith(".eml"));
            var eml = await File.ReadAllTextAsync(file);

            Assert.DoesNotContain(cc, eml);
        }
        finally
        {
            using var scope = api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SettingsService>()
                .SetAsync(SettingKeys.EmailCc, "");
        }
    }

    private sealed record ProfileBody(int Id);
    private sealed record SetPasswordInfoBody(bool Valid, string? Name, string? Email);
    private sealed record ImportBody(int Created, int Skipped, int Failed, int LinksQueued);
}
