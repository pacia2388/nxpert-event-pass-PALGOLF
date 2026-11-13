using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventRegistration.Api.Tests;

/// <summary>
/// Hosts the real API in-process against its own throwaway SQL Server LocalDB database. The
/// database is created by the app's migrations and seeder on first use and dropped when the test
/// run ends, so the dev database is never touched. Email, Viber and SMS never reach a real provider:
/// each writes its messages to test/sent/{email,viber,sms} under the test output folder (cleared at
/// the start of a run, kept afterwards for inspection). <see cref="ChannelsAsync"/> switches a
/// channel to fail or off for one test.
/// </summary>
public class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Development seed account (DbSeeder).
    public const string AdminEmail = "admin@demo.com";
    public const string AdminPassword = "Admin123!";
    // Password given to users the tests create through the admin Users API.
    public const string UserPassword = "Passw0rd!";

    private readonly string _database = $"EventPassTests_{Guid.NewGuid():N}";

    public static readonly string SentRoot = Path.Combine(AppContext.BaseDirectory, "test", "sent");
    public static string SentDir(string channel) => Path.Combine(SentRoot, channel);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // LocalDB by default; set EVENTPASS_TEST_SQLSERVER to run against another SQL Server
        // (e.g. on a build agent). Either way the tests use their own database.
        var server = Environment.GetEnvironmentVariable("EVENTPASS_TEST_SQLSERVER") ?? "(localdb)\\MSSQLLocalDB";
        builder.UseSetting("ConnectionStrings:Default",
            $"Server={server};Database={_database};Integrated Security=True;TrustServerCertificate=True;");
        builder.UseSetting("Email:Enabled", "false");
        builder.UseSetting("Viber:Enabled", "false");
        builder.UseSetting("Sms:Enabled", "false");
        // No real provider can be reached from a test, even if a test turns a channel on.
        foreach (var key in new[] { "Email:Host", "Email:User", "Email:Password", "Viber:BaseUrl", "Viber:ApiKey",
                     "Sms:BaseUrl", "Sms:ApiKey", "Sms:ApiSecret", "FaceEngine:ApiKey" })
            builder.UseSetting(key, "");
        // Start each run with empty folders (files only: an open folder must not fail the run).
        if (Directory.Exists(SentRoot))
            foreach (var file in Directory.GetFiles(SentRoot, "*", SearchOption.AllDirectories)) File.Delete(file);
        builder.UseSetting("Email:TestSendDir", SentDir("email"));
        builder.UseSetting("Viber:TestSendDir", SentDir("viber"));
        builder.UseSetting("Sms:TestSendDir", SentDir("sms"));
        builder.ConfigureTestServices(services =>
        {
            // The background mail schedulers have nothing to do in a test run.
            var schedulers = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType?.Name is "InvitationEmailScheduler" or "MailPlanScheduler" or "EmailRetryScheduler")
                .ToList();
            foreach (var d in schedulers) services.Remove(d);
        });
    }

    ValueTask IAsyncLifetime.InitializeAsync() => ValueTask.CompletedTask;

    // Drop the test database before the host shuts down.
    public override async ValueTask DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    // ---- Helpers: everything goes through the public HTTP API ----

    public HttpClient Anonymous() => CreateClient();

    /// <summary>A client signed in through POST /api/auth/login.</summary>
    public async Task<HttpClient> SignInAsync(string email, string password)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<AuthBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    public Task<HttpClient> AdminAsync() => SignInAsync(AdminEmail, AdminPassword);

    /// <summary>Creates a user through the admin Users API and returns its id and email.</summary>
    public async Task<TestUser> CreateUserAsync(HttpClient admin, string role, string firstName = "Test")
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var res = await admin.PostAsJsonAsync("/api/users", new
        {
            firstName,
            lastName = "User",
            email,
            password = UserPassword,
            role,
            birthdate = "1990-05-01",
            canConfirmRegistrations = false,
        });
        res.EnsureSuccessStatusCode();
        var user = await res.Content.ReadFromJsonAsync<UserBody>();
        return new TestUser(user!.Id, email);
    }

    /// <summary>Creates a Captain user and a team they lead; returns the team and the lead's client.</summary>
    public async Task<TestTeam> CreateTeamAsync(HttpClient admin, string category)
    {
        var lead = await CreateUserAsync(admin, "Captain");
        var res = await admin.PostAsJsonAsync("/api/teams", new
        {
            description = $"Team {Guid.NewGuid():N}",
            category,
            teamLeadUserId = lead.Id,
        });
        res.EnsureSuccessStatusCode();
        var team = await res.Content.ReadFromJsonAsync<TeamBody>();
        return new TestTeam(team!.Id, lead, await SignInAsync(lead.Email, UserPassword));
    }

    // The current team registration link token of a member (only the emailed link carries it).
    public async Task<Guid?> RegistrationTokenAsync(int userId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.TeamMembers.Where(m => m.UserId == userId)
            .Select(m => m.RegistrationToken).SingleAsync();
    }

    public static string NewEmail() => $"member-{Guid.NewGuid():N}@example.com";

    // A unique PH mobile number, stored form "+639XXXXXXXXX".
    public static string NewPhone() => "+639" + Random.Shared.NextInt64(100_000_000, 999_999_999);

    // ---- Channels: each test sets what it needs; disposing restores "every channel sends". ----

    /// <summary>
    /// Sets each channel to Send (writes to test/sent), Fail (every send fails) or Off (not set up:
    /// skipped, as in Production), the email fallback switches and the active mobile provider.
    /// </summary>
    public async Task<IAsyncDisposable> ChannelsAsync(ChannelMode email = ChannelMode.Send,
        ChannelMode viber = ChannelMode.Send, ChannelMode sms = ChannelMode.Send,
        bool viberFallbackToEmail = true, bool smsFallbackToEmail = true, string provider = "Viber")
    {
        var e = Services.GetRequiredService<EmailSettings>();
        var v = Services.GetRequiredService<ViberSettings>();
        var s = Services.GetRequiredService<SmsSettings>();
        (e.TestSendDir, e.TestFail) = Mode(email, "email");
        (v.TestSendDir, v.TestFail) = Mode(viber, "viber");
        (s.TestSendDir, s.TestFail) = Mode(sms, "sms");
        v.FallbackToEmail = viberFallbackToEmail;
        s.FallbackToEmail = smsFallbackToEmail;
        await SetProviderAsync(provider);
        return new Restore(async () =>
        {
            (e.TestSendDir, e.TestFail) = Mode(ChannelMode.Send, "email");
            (v.TestSendDir, v.TestFail) = Mode(ChannelMode.Send, "viber");
            (s.TestSendDir, s.TestFail) = Mode(ChannelMode.Send, "sms");
            v.FallbackToEmail = s.FallbackToEmail = true;
            await SetProviderAsync("Viber");
        });
    }

    private static (string Dir, bool Fail) Mode(ChannelMode m, string channel) =>
        m == ChannelMode.Off ? ("", false) : (SentDir(channel), m == ChannelMode.Fail);

    private async Task SetProviderAsync(string provider)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SettingsService>().SetAsync(SettingKeys.MobileProvider, provider);
    }

    private sealed class Restore(Func<Task> undo) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await undo();
    }

    /// <summary>Messages written to test/sent/{channel} whose file name names this recipient.</summary>
    public static string[] Sent(string channel, string recipient)
    {
        var dir = SentDir(channel);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir).Where(f => !f.EndsWith(".png") && Path.GetFileName(f).Contains(recipient)).OrderBy(f => f).ToArray()
            : [];
    }

    /// <summary>Creates a published event (optionally online/hybrid) and returns its id and slug.</summary>
    public async Task<(int Id, string Slug, string Title)> CreateEventAsync(HttpClient admin, string? format = null,
        bool meal = false)
    {
        var title = $"Event {Guid.NewGuid():N}"[..20];
        var start = DateTime.Today.AddDays(10).AddHours(8);
        var res = await admin.PostAsJsonAsync("/api/events", new
        {
            title, description = "", startsAt = start, endsAt = start.AddHours(8), venue = "Course",
            isPublicLinkEnabled = true, status = "Published", format, notifyOnKitClaim = true,
            meetingUrl = format is null ? null : "https://meet.example.com/golf",
            meals = meal ? new[] { new { id = 0, name = "Lunch", denomination = 150m, quantity = 1, color = (string?)null, day = 1,
                validFrom = (DateTime?)start, validTo = (DateTime?)start.AddHours(8) } } : null,
        });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<EventIdBody>();
        return (body!.Id, body.PublicSlug, title);
    }

    /// <summary>Registers a guest (pending) and returns the registration id.</summary>
    public async Task<int> RegisterGuestAsync(int eventId, string slug, string email, string phone, string channel)
    {
        (await Anonymous().PostAsJsonAsync($"/api/registrations/guest/{slug}", new
        {
            fullName = "Mia Santos", email, phone, notificationChannel = channel,
            senateNo = $"ID-{Guid.NewGuid():N}"[..10],
        })).EnsureSuccessStatusCode();
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Registrations
            .Where(r => r.EventId == eventId && r.GuestEmail == email).Select(r => r.Id).SingleAsync();
    }

    /// <summary>Confirms registrations through the Attendance API (this sends the pass).</summary>
    public async Task<ConfirmBody> ConfirmAsync(HttpClient admin, int eventId, params int[] registrationIds)
    {
        var res = await admin.PostAsJsonAsync($"/api/events/{eventId}/attendance/confirm", new { registrationIds });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ConfirmBody>())!;
    }

    public static object Member(string email, string firstName = "Mia", string lastName = "Santos") =>
        new { firstName, middleInitial = (string?)null, lastName, email };
}

public record TestUser(int Id, string Email);
public record TestTeam(int Id, TestUser Lead, HttpClient LeadClient);

// Response shapes the tests read (camelCase JSON, matched case-insensitively).
public record AuthBody(string Token, int UserId, string Role);
public record UserBody(int Id, string Email, string Role);
public record MemberBody(int UserId, string FullName, string Email, bool NotificationFlag = false, bool Registered = false);
public record TeamBody(int Id, string Description, string Category, int TeamLeadUserId,
    int MemberCount, int MaxMembers, List<MemberBody>? Members);
public record AddResultBody(MemberBody Member, bool Created);
public record ErrorBody(string Message);
public record EventIdBody(int Id, string PublicSlug);
public record ConfirmBody(int Confirmed, int Emailed, int NoEmail, int NotSent);

public enum ChannelMode { Send, Fail, Off }

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture> { }
