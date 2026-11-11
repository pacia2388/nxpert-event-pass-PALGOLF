using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EventRegistration.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventRegistration.Api.Tests;

/// <summary>
/// Hosts the real API in-process against its own throwaway SQL Server LocalDB database. The
/// database is created by the app's migrations and seeder on first use and dropped when the test
/// run ends, so the dev database is never touched. Outbound email, Viber and SMS are switched off.
/// </summary>
public class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Development seed account (DbSeeder).
    public const string AdminEmail = "admin@demo.com";
    public const string AdminPassword = "Admin123!";
    // Password given to users the tests create through the admin Users API.
    public const string UserPassword = "Passw0rd!";

    private readonly string _database = $"EventPassTests_{Guid.NewGuid():N}";

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
        builder.ConfigureTestServices(services =>
        {
            // The background mail schedulers have nothing to do in a test run.
            var schedulers = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType?.Name is "InvitationEmailScheduler" or "MailPlanScheduler")
                .ToList();
            foreach (var d in schedulers) services.Remove(d);
        });
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
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
            canConfirmRegistrations = false,
        });
        res.EnsureSuccessStatusCode();
        var user = await res.Content.ReadFromJsonAsync<UserBody>();
        return new TestUser(user!.Id, email);
    }

    /// <summary>Creates a TL user and a team they lead; returns the team and the lead's client.</summary>
    public async Task<TestTeam> CreateTeamAsync(HttpClient admin, string category)
    {
        var lead = await CreateUserAsync(admin, "TL");
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

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture> { }
