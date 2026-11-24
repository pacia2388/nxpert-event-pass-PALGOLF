using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EventRegistration.Api.Tests;

// The look a deployment wears comes from the theme file the "theme" setting names (theme.palgolf.json or
// theme.nxpass.json). The web app ships the same files and reads its own copy.
[Collection("api")]
public class ThemeTests(ApiFixture api) : IAsyncLifetime
{
    ValueTask IAsyncLifetime.InitializeAsync() => ValueTask.CompletedTask;

    // Every test leaves the deployment on the default theme.
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (File.Exists(ApiFixture.ThemeFile)) File.Delete(ApiFixture.ThemeFile);
        return ValueTask.CompletedTask;
    }

    private void WriteTheme(string json) => File.WriteAllText(ApiFixture.ThemeFile, json);

    private async Task<JsonElement> ThemeAsync()
    {
        var res = await api.Anonymous().GetAsync("/api/theme");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Str(JsonElement e, params string[] path)
    {
        foreach (var p in path) e = e.GetProperty(p);
        return e.ValueKind == JsonValueKind.Null ? null! : e.GetString()!;
    }

    [Fact]
    public async Task Without_a_theme_file_anyone_gets_the_PALGOLF_theme()
    {
        var theme = await ThemeAsync();

        Assert.Equal("PALGOLF", Str(theme, "name"));
        Assert.Equal("default", Str(theme, "source"));
        Assert.Equal("#0f5fb8", Str(theme, "colors", "primary"));
        Assert.Equal("themes/palgolf/login-poster.jpg", Str(theme, "logos", "loginImage"));
        Assert.Equal("poster", Str(theme, "layout", "loginImage"));
    }

    [Fact]
    public async Task A_theme_file_naming_NXPass_brings_back_the_teal_look_and_its_logos()
    {
        WriteTheme("""{ "base": "nxpass" }""");

        var theme = await ThemeAsync();

        Assert.Equal("NXPass", Str(theme, "name"));
        Assert.Equal("file", Str(theme, "source"));
        Assert.Equal("#0d9488", Str(theme, "colors", "primary"));
        Assert.Equal("NXPass", Str(theme, "appName"));
        Assert.Equal("themes/nxpass/favicon.ico", Str(theme, "logos", "favicon"));
        Assert.Equal("themes/nxpass/logo.png", Str(theme, "logos", "loginImage"));
        Assert.Equal("gradient", Str(theme, "layout", "buttons"));
        Assert.Equal("glass", Str(theme, "layout", "footer"));
        Assert.Equal("logo", Str(theme, "layout", "loginImage"));
    }

    [Fact]
    public async Task Values_in_the_file_override_its_base_one_at_a_time()
    {
        WriteTheme("""
            {
              "base": "NXPass",
              "appName": "Demo Cup",
              "colors": { "primary": "#AA3300" },
              "logos": { "header": "brand/demo-logo.png" },
              "login": { "lines": ["Feb 1, 2027"] }
            }
            """);

        var theme = await ThemeAsync();

        Assert.Equal("#aa3300", Str(theme, "colors", "primary"));
        Assert.Equal("#0f766e", Str(theme, "colors", "primaryDeep"));
        Assert.Equal("Demo Cup", Str(theme, "appName"));
        Assert.Equal("brand/demo-logo.png", Str(theme, "logos", "header"));
        Assert.Equal("Feb 1, 2027", theme.GetProperty("login").GetProperty("lines")[0].GetString());
        Assert.True(theme.GetProperty("customised").GetBoolean());
    }

    [Fact]
    public async Task Invalid_values_are_dropped_and_the_base_value_kept()
    {
        WriteTheme("""
            {
              "base": "NXPass",
              "colors": { "primary": "red;}body{display:none", "tint": "rgb(1,2,3)", "accent": "#0d948880" },
              "gradients": { "brand": "linear-gradient(url(https://evil.example/x.png))" },
              "fonts": { "display": "Roboto; color: red" },
              "layout": { "buttons": "sparkly", "loginImage": "banner" },
              "logos": { "favicon": "javascript:alert(1)", "tabIcon": "//evil.example/x.png", "loginImage": "../secret.png" },
              "appName": "   "
            }
            """);

        var theme = await ThemeAsync();

        Assert.Equal("#0d9488", Str(theme, "colors", "primary"));
        // Only #rgb / #rrggbb: the meal-stub renderer and email clients read nothing else the same way.
        Assert.Equal("#e6f6f3", Str(theme, "colors", "tint"));
        Assert.Equal("#3dbb8a", Str(theme, "colors", "accent"));
        Assert.Equal("linear-gradient(135deg, #0d9488 0%, #22c55e 100%)", Str(theme, "gradients", "brand"));
        Assert.Equal("'Roboto', sans-serif", Str(theme, "fonts", "display"));
        Assert.Equal("gradient", Str(theme, "layout", "buttons"));
        Assert.Equal("logo", Str(theme, "layout", "loginImage"));
        Assert.Equal("themes/nxpass/favicon.ico", Str(theme, "logos", "favicon"));
        Assert.Equal("themes/nxpass/tab-192.png", Str(theme, "logos", "tabIcon"));
        Assert.Equal("themes/nxpass/logo.png", Str(theme, "logos", "loginImage"));
        Assert.Equal("NXPass", Str(theme, "appName"));
    }

    [Fact]
    public async Task A_broken_theme_file_falls_back_to_PALGOLF()
    {
        WriteTheme("""{ "base": "NXPass", """);

        var theme = await ThemeAsync();

        Assert.Equal("PALGOLF", Str(theme, "name"));
        Assert.Equal("#0f5fb8", Str(theme, "colors", "primary"));
    }

    [Fact]
    public async Task Editing_the_theme_file_applies_on_the_next_request()
    {
        WriteTheme("""{ "base": "NXPass" }""");
        Assert.Equal("NXPass", Str(await ThemeAsync(), "name"));

        WriteTheme("""{ "base": "PALGOLF", "appName": "Changed" }""");
        var theme = await ThemeAsync();

        Assert.Equal("PALGOLF", Str(theme, "name"));
        Assert.Equal("Changed", Str(theme, "appName"));
    }

    [Theory]
    [InlineData("theme.palgolf.json", "PALGOLF")]
    [InlineData("theme.nxpass.json", "NXPass")]
    public void The_shipped_theme_files_are_the_built_in_looks_and_the_web_app_has_the_same_copy(string file, string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(root.FullName, "frontend"))) root = root.Parent!;
        var apiCopy = File.ReadAllText(Path.Combine(root.FullName, "backend", "EventRegistration.Api", file));
        var webCopy = File.ReadAllText(Path.Combine(root.FullName, "frontend", "web", "public", file));

        var resolved = EventRegistration.Api.Services.Theming.ThemeResolver.Resolve(apiCopy);

        Assert.Empty(resolved.Warnings);
        Assert.False(resolved.Customised);
        Assert.Equal(
            JsonSerializer.Serialize(EventRegistration.Api.Services.Theming.BuiltInThemes.Find(name), EventRegistration.Api.Services.Theming.ThemeResolver.Json),
            JsonSerializer.Serialize(resolved.Theme, EventRegistration.Api.Services.Theming.ThemeResolver.Json));
        Assert.Equal(apiCopy.ReplaceLineEndings(), webCopy.ReplaceLineEndings());
    }

    [Fact]
    public async Task Emails_wear_the_active_theme_its_name_and_its_header_logo()
    {
        WriteTheme("""{ "base": "NXPass", "logos": { "header": "brand/n-logo.png" } }""");
        await ThemeAsync();

        var html = EventRegistration.Api.Services.EmailTemplates.TeamRegistrationNotice("Mia Santos", "Eagles", "Leo Cruz",
            "https://example.com/register", 7);

        Assert.Contains("#0d9488", html);
        Assert.DoesNotContain("#0f5fb8", html);
        Assert.Contains("NXPass · this is an automated message", html);
        Assert.Matches("<img [^>]*src=\"https?://[^\"]+/brand/n-logo.png\"", html);
    }

    [Fact]
    public async Task Emails_come_from_the_theme_name_unless_a_sender_name_is_configured()
    {
        WriteTheme("""{ "base": "NXPass" }""");
        await ThemeAsync();

        Assert.Equal("NXPass", new EventRegistration.Api.Services.EmailSettings().SenderName);
        Assert.Equal("Ops Desk", new EventRegistration.Api.Services.EmailSettings { FromName = "Ops Desk" }.SenderName);
    }

    [Fact]
    public async Task The_pass_page_wears_the_active_theme()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var email = ApiFixture.NewEmail();
        var reg = await api.RegisterGuestAsync(ev.Id, ev.Slug, email, ApiFixture.NewPhone(), "Email");
        await api.ConfirmAsync(admin, ev.Id, reg);
        var token = await PassTokenAsync(reg);
        WriteTheme("""{ "base": "NXPass" }""");

        var page = await api.Anonymous().GetStringAsync($"/api/passes/{token}");

        Assert.Contains("#0d9488", page);
        Assert.DoesNotContain("#0f5fb8", page);
    }

    private async Task<Guid> PassTokenAsync(int registrationId)
    {
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(api.Services);
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<EventRegistration.Api.Data.AppDbContext>(scope.ServiceProvider);
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            db.Registrations.Where(r => r.Id == registrationId).Select(r => r.Token));
    }
}
