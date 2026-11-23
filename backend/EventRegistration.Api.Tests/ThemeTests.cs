using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EventRegistration.Api.Tests;

// The look a deployment wears comes from theme.json beside the API (never published). The API
// resolves it and hands it to the web app at GET /api/theme.
[Collection("api")]
public class ThemeTests(ApiFixture api) : IAsyncLifetime
{
    ValueTask IAsyncLifetime.InitializeAsync() => ValueTask.CompletedTask;

    // Every test leaves the deployment on the default theme.
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (File.Exists(api.ThemeFile)) File.Delete(api.ThemeFile);
        return ValueTask.CompletedTask;
    }

    private void WriteTheme(string json) => File.WriteAllText(api.ThemeFile, json);

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
        Assert.Equal("brand/pal-interclub-2027-poster.jpg", Str(theme, "logos", "loginImage"));
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
        Assert.Equal("nxpax-logo.png", Str(theme, "logos", "loginImage"));
        Assert.Equal("gradient", Str(theme, "layout", "buttons"));
        Assert.Equal("glass", Str(theme, "layout", "footer"));
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
              "colors": { "primary": "red;}body{display:none" },
              "gradients": { "brand": "linear-gradient(url(https://evil.example/x.png))" },
              "fonts": { "display": "Roboto; color: red" },
              "layout": { "buttons": "sparkly" },
              "logos": { "favicon": "javascript:alert(1)", "tabIcon": "//evil.example/x.png", "loginImage": "../secret.png" },
              "appName": "   "
            }
            """);

        var theme = await ThemeAsync();

        Assert.Equal("#0d9488", Str(theme, "colors", "primary"));
        Assert.Equal("linear-gradient(135deg, #0d9488 0%, #22c55e 100%)", Str(theme, "gradients", "brand"));
        Assert.Equal("'Roboto', sans-serif", Str(theme, "fonts", "display"));
        Assert.Equal("gradient", Str(theme, "layout", "buttons"));
        Assert.Equal("themes/nxpass/favicon.ico", Str(theme, "logos", "favicon"));
        Assert.Equal("themes/nxpass/icon-192.png", Str(theme, "logos", "tabIcon"));
        Assert.Equal("nxpax-logo.png", Str(theme, "logos", "loginImage"));
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

    [Fact]
    public async Task Only_admins_can_download_the_theme_template()
    {
        var admin = await api.AdminAsync();
        var participant = await api.CreateUserAsync(admin, "Participant");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.Anonymous().GetAsync("/api/settings/theme-template")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await (await api.SignInAsync(participant.Email, ApiFixture.UserPassword)).GetAsync("/api/settings/theme-template")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/settings/theme-template")).StatusCode);
    }

    [Fact]
    public async Task The_template_is_a_complete_PALGOLF_theme_file_that_reads_back_unchanged()
    {
        var admin = await api.AdminAsync();
        var res = await admin.GetAsync("/api/settings/theme-template");

        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
        Assert.Equal("theme-template.json", res.Content.Headers.ContentDisposition?.FileNameStar
            ?? res.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var template = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(template);
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("PALGOLF", doc.RootElement.GetProperty("base").GetString());
        Assert.True(doc.RootElement.TryGetProperty("$help", out _));
        foreach (var section in new[] { "colors", "gradients", "fonts", "layout", "logos", "login" })
            Assert.True(doc.RootElement.TryGetProperty(section, out _), section);

        var before = await (await api.Anonymous().GetAsync("/api/theme")).Content.ReadFromJsonAsync<JsonElement>();
        WriteTheme(template);
        var after = await ThemeAsync();

        Assert.Equal("file", Str(after, "source"));
        foreach (var section in new[] { "colors", "gradients", "fonts", "layout", "logos", "login" })
            Assert.Equal(before.GetProperty(section).GetRawText(), after.GetProperty(section).GetRawText());
    }
}
