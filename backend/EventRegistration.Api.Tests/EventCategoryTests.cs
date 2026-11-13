using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// Admins tag an event as for seniors (65+), for regular players, or leave it untagged.
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
}
