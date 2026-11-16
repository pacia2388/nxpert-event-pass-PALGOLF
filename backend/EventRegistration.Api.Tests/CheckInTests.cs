using System.Net;
using System.Net.Http.Json;

namespace EventRegistration.Api.Tests;

// The admin scanner checks a pass in only when it is confirmed and the event is running today.
// Anything else is reported back (unconfirmed, not started, ended, invalid) and nothing is recorded.
[Collection("api")]
public class CheckInTests(ApiFixture api)
{
    private record EventBody(int Id);
    private record MineBody(int Id, int EventId, Guid Token, DateTime? CheckedInAt);
    private record ScanBody(string Status, string? FullName, string? Message);

    // An event starting `startDay` days from today; a participant registered for it (pending).
    private async Task<(HttpClient Admin, int EventId, MineBody Reg, HttpClient Player)> RegisteredAsync(int startDay = 0)
    {
        var admin = await api.AdminAsync();
        var start = DateTime.Today.AddDays(startDay).AddHours(6);
        var created = await admin.PostAsJsonAsync("/api/events", new
        {
            title = $"Scan Cup {Guid.NewGuid():N}"[..20],
            description = "",
            startsAt = start,
            endsAt = start.AddHours(12),
            venue = "Course",
            isPublicLinkEnabled = true,
            status = "Published",
        });
        created.EnsureSuccessStatusCode();
        var eventId = (await created.Content.ReadFromJsonAsync<EventBody>())!.Id;

        var user = await api.CreateUserAsync(admin, "Participant");
        var player = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        (await player.PostAsJsonAsync("/api/registrations", new { eventId })).EnsureSuccessStatusCode();
        return (admin, eventId, await MineAsync(player, eventId), player);
    }

    private static async Task<MineBody> MineAsync(HttpClient player, int eventId) =>
        Assert.Single((await player.GetFromJsonAsync<List<MineBody>>("/api/registrations/mine"))!,
            r => r.EventId == eventId);

    private static async Task<ScanBody> ScanAsync(HttpClient admin, object request)
    {
        var res = await admin.PostAsJsonAsync("/api/checkin", request);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ScanBody>())!;
    }

    [Fact]
    public async Task A_confirmed_pass_checks_in_on_the_event_day()
    {
        var (admin, eventId, reg, player) = await RegisteredAsync();
        await api.ConfirmAsync(admin, eventId, reg.Id);

        var scan = await ScanAsync(admin, new { token = reg.Token.ToString() });

        Assert.Equal("valid", scan.Status);
        Assert.NotNull((await MineAsync(player, eventId)).CheckedInAt);
    }

    [Fact]
    public async Task Scanning_the_same_pass_again_reports_it_as_already_checked_in()
    {
        var (admin, eventId, reg, _) = await RegisteredAsync();
        await api.ConfirmAsync(admin, eventId, reg.Id);
        await ScanAsync(admin, new { token = reg.Token.ToString() });

        var again = await ScanAsync(admin, new { token = reg.Token.ToString() });

        Assert.Equal("already", again.Status);
    }

    [Fact]
    public async Task A_pending_pass_is_refused_as_not_confirmed()
    {
        var (admin, eventId, reg, player) = await RegisteredAsync();

        var scan = await ScanAsync(admin, new { token = reg.Token.ToString() });

        Assert.Equal("unconfirmed", scan.Status);
        Assert.Null((await MineAsync(player, eventId)).CheckedInAt);
    }

    [Fact]
    public async Task A_QR_code_with_the_REG_prefix_checks_in()
    {
        var (admin, eventId, reg, _) = await RegisteredAsync();
        await api.ConfirmAsync(admin, eventId, reg.Id);

        var scan = await ScanAsync(admin, new { token = $"REG:{reg.Token}" });

        Assert.Equal("valid", scan.Status);
    }

    [Theory]
    [InlineData("not-a-pass")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")] // a well-formed token no pass carries
    public async Task An_unreadable_or_unknown_code_is_reported_as_invalid(string token)
    {
        var admin = await api.AdminAsync();

        var scan = await ScanAsync(admin, new { token });

        Assert.Equal("invalid", scan.Status);
    }

    [Fact]
    public async Task Check_in_is_not_open_before_the_event_starts()
    {
        var (admin, eventId, reg, player) = await RegisteredAsync(startDay: 3);
        await api.ConfirmAsync(admin, eventId, reg.Id);

        var scan = await ScanAsync(admin, new { token = reg.Token.ToString() });

        Assert.Equal("notstarted", scan.Status);
        Assert.Null((await MineAsync(player, eventId)).CheckedInAt);
    }

    [Fact]
    public async Task Check_in_is_closed_after_the_event_ends()
    {
        var (admin, eventId, reg, player) = await RegisteredAsync(startDay: -3);
        await api.ConfirmAsync(admin, eventId, reg.Id);

        var scan = await ScanAsync(admin, new { token = reg.Token.ToString() });

        Assert.Equal("ended", scan.Status);
        Assert.Null((await MineAsync(player, eventId)).CheckedInAt);
    }

    [Fact]
    public async Task A_pass_for_another_event_is_refused_at_this_events_scanner()
    {
        var (admin, eventId, reg, player) = await RegisteredAsync();
        await api.ConfirmAsync(admin, eventId, reg.Id);
        var other = await RegisteredAsync();

        var scan = await ScanAsync(admin, new { token = reg.Token.ToString(), eventId = other.EventId });

        Assert.Equal("invalid", scan.Status);
        Assert.Null((await MineAsync(player, eventId)).CheckedInAt);
    }

    [Fact]
    public async Task Verify_mode_reports_a_confirmed_pass_without_checking_it_in()
    {
        var (admin, eventId, reg, player) = await RegisteredAsync();
        await api.ConfirmAsync(admin, eventId, reg.Id);

        var scan = await ScanAsync(admin, new { token = reg.Token.ToString(), verify = true });

        Assert.Equal("verified", scan.Status);
        Assert.Null((await MineAsync(player, eventId)).CheckedInAt);
    }

    [Fact]
    public async Task Only_admins_can_use_the_check_in_scanner()
    {
        var (admin, eventId, reg, player) = await RegisteredAsync();
        await api.ConfirmAsync(admin, eventId, reg.Id);

        var res = await player.PostAsJsonAsync("/api/checkin", new { token = reg.Token.ToString() });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
