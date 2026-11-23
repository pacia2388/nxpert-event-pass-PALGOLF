using System.Net;
using System.Net.Http.Json;
using EventRegistration.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventRegistration.Api.Tests;

// Admins tag an event with a category from categories.json (Senior / Regular in this repo), or
// leave it untagged.
[Collection("api")]
public class EventCategoryTests(ApiFixture api)
{
    private static object NewEvent(string? category) => new
    {
        title = $"Cup {Guid.NewGuid():N}",
        description = "",
        startsAt = DateTime.Today.AddDays(30).AddHours(8),
        endsAt = DateTime.Today.AddDays(30).AddHours(17),
        venue = "Villamor Golf Club",
        isPublicLinkEnabled = true,
        status = "Published",
        capacity = (int?)null,
        category,
    };

    private record EventBody(int Id, string PublicSlug, string? Category);

    private static async Task<EventBody> Create(HttpClient admin, string? category)
    {
        var res = await admin.PostAsJsonAsync("/api/events", NewEvent(category));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<EventBody>())!;
    }

    [Theory]
    [InlineData("Senior")]
    [InlineData("Regular")]
    [InlineData(null)]
    public async Task An_event_keeps_the_category_it_was_created_with(string? category)
    {
        var admin = await api.AdminAsync();

        var created = await Create(admin, category);

        var read = await admin.GetFromJsonAsync<EventBody>($"/api/events/{created.Id}");
        Assert.Equal(category, read!.Category);
    }

    [Fact]
    public async Task An_admin_changes_or_clears_the_category()
    {
        var admin = await api.AdminAsync();
        var created = await Create(admin, "Senior");

        await admin.PutAsJsonAsync($"/api/events/{created.Id}", NewEvent("Regular"));
        Assert.Equal("Regular", (await admin.GetFromJsonAsync<EventBody>($"/api/events/{created.Id}"))!.Category);

        await admin.PutAsJsonAsync($"/api/events/{created.Id}", NewEvent(""));
        Assert.Null((await admin.GetFromJsonAsync<EventBody>($"/api/events/{created.Id}"))!.Category);
    }

    [Theory]
    [InlineData(" Senior ", "Senior")]
    [InlineData("regular", "Regular")]
    public async Task A_category_is_trimmed_and_matched_ignoring_case(string sent, string saved)
    {
        var admin = await api.AdminAsync();

        var created = await Create(admin, sent);

        Assert.Equal(saved, created.Category);
    }

    [Theory]
    [InlineData("Junior")]
    [InlineData("Seniors")]
    public async Task An_unknown_category_is_rejected(string category)
    {
        var admin = await api.AdminAsync();

        var res = await admin.PostAsJsonAsync("/api/events", NewEvent(category));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task The_public_event_page_and_the_admin_list_show_the_category()
    {
        var admin = await api.AdminAsync();
        var created = await Create(admin, "Senior");

        var publicEvent = await api.Anonymous().GetFromJsonAsync<EventBody>($"/api/events/public/{created.PublicSlug}");
        var listed = (await admin.GetFromJsonAsync<List<EventBody>>("/api/events"))!.Single(e => e.Id == created.Id);

        Assert.Equal("Senior", publicEvent!.Category);
        Assert.Equal("Senior", listed.Category);
    }

    private record CategoryBody(string Name, string Icon, int TeamMaxMembers);

    [Fact]
    public async Task Anyone_can_read_the_category_list_from_categories_json()
    {
        var list = await api.Anonymous().GetFromJsonAsync<List<CategoryBody>>("/api/settings/categories");

        Assert.Equal(
            [new("Senior", "elderly", 8), new("Regular", "person", 10)],
            list);
    }

    [Fact]
    public async Task An_unknown_category_is_refused_with_the_listed_names()
    {
        var admin = await api.AdminAsync();

        var res = await admin.PostAsJsonAsync("/api/events", NewEvent("Junior"));

        Assert.Contains("Senior, Regular, or none", await res.Content.ReadAsStringAsync());
    }

    // Writes a category straight to the database, as if it was taken off categories.json later.
    private async Task Store(Action<AppDbContext> change)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_edit_keeps_a_category_that_was_taken_off_the_list()
    {
        var admin = await api.AdminAsync();
        var created = await Create(admin, "Senior");
        await Store(db => db.Events.Single(e => e.Id == created.Id).Category = "Junior");

        var kept = await admin.PutAsJsonAsync($"/api/events/{created.Id}", NewEvent("junior"));
        var other = await admin.PutAsJsonAsync($"/api/events/{created.Id}", NewEvent("Juniors"));

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal("Junior", (await admin.GetFromJsonAsync<EventBody>($"/api/events/{created.Id}"))!.Category);
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
    }

    [Fact]
    public async Task A_team_keeps_a_category_taken_off_the_list_and_uses_the_default_cap()
    {
        var admin = await api.AdminAsync();
        var team = await api.CreateTeamAsync(admin, "Senior");
        await Store(db => db.Teams.Single(t => t.Id == team.Id).Category = "Junior");

        var res = await admin.PutAsJsonAsync($"/api/teams/{team.Id}",
            new { description = $"Team {Guid.NewGuid():N}", category = "Junior", teamLeadUserId = team.Lead.Id });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var saved = (await res.Content.ReadFromJsonAsync<TeamBody>())!;
        Assert.Equal("Junior", saved.Category);
        Assert.Equal(10, saved.MaxMembers);
    }
}
