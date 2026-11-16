using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// The server checks an event's title and schedule whatever the form sent: a title of 3 to 300
// characters, and an end after the start.
[Collection("api")]
public class EventValidationTests(ApiFixture api)
{
    private record EventBody(int Id, string Title, DateTime EndsAt);

    private static readonly DateTime Start = DateTime.Today.AddDays(10).AddHours(8);

    private static object Event(string title, DateTime endsAt) => new
    {
        title,
        description = "",
        startsAt = Start,
        endsAt,
        venue = "Course",
        isPublicLinkEnabled = true,
        status = "Published",
    };

    [Fact]
    public async Task A_three_letter_title_with_an_end_after_the_start_is_accepted()
    {
        var admin = await api.AdminAsync();

        var res = await admin.PostAsJsonAsync("/api/events", Event("Cup", Start.AddMinutes(1)));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData(0)] // ends when it starts
    [InlineData(-60)] // ends before it starts
    public async Task An_event_that_does_not_end_after_it_starts_is_refused(int endOffsetMinutes)
    {
        var admin = await api.AdminAsync();

        var res = await admin.PostAsJsonAsync("/api/events", Event("Golf Cup", Start.AddMinutes(endOffsetMinutes)));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("Go")]
    [InlineData("   ")]
    [InlineData("  ab  ")] // 2 characters once trimmed
    public async Task A_title_under_three_characters_is_refused(string title)
    {
        var admin = await api.AdminAsync();

        var res = await admin.PostAsJsonAsync("/api/events", Event(title, Start.AddHours(4)));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_title_over_300_characters_is_refused()
    {
        var admin = await api.AdminAsync();

        var res = await admin.PostAsJsonAsync("/api/events", Event(new string('a', 301), Start.AddHours(4)));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Editing_an_event_to_end_before_it_starts_is_refused_and_keeps_the_event()
    {
        var admin = await api.AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/events", Event("Golf Cup", Start.AddHours(4)));
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<EventBody>())!.Id;

        var res = await admin.PutAsJsonAsync($"/api/events/{id}", Event("Golf Cup", Start.AddHours(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var saved = await admin.GetFromJsonAsync<EventBody>($"/api/events/{id}");
        Assert.Equal(Start.AddHours(4), saved!.EndsAt);
    }

    [Fact]
    public async Task Editing_an_event_title_down_to_two_characters_is_refused()
    {
        var admin = await api.AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/events", Event("Golf Cup", Start.AddHours(4)));
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<EventBody>())!.Id;

        var res = await admin.PutAsJsonAsync($"/api/events/{id}", Event("Go", Start.AddHours(4)));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Golf Cup", (await admin.GetFromJsonAsync<EventBody>($"/api/events/{id}"))!.Title);
    }
}
