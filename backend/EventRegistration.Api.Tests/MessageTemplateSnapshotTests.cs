using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace EventRegistration.Api.Tests;

// Locks the wording of every registrant / receiver message on every channel (email, Viber, SMS),
// so the template refactor (plans/completed/20261123_uniform-message-delivery.md) can prove nothing changed.
// Each case sends one message through the real code, reads what the channels wrote to test/sent,
// normalizes run-specific values (addresses, ids, tokens, timestamps) and compares it with
// Snapshots/Messages/<case>.txt. A missing snapshot is written and the test fails once: review
// the file and commit it. To accept an intended change, set UPDATE_SNAPSHOTS=1 and rerun.
[Collection("api")]
public partial class MessageTemplateSnapshotTests(ApiFixture api)
{
    private const string Title = "Snapshot Open";
    private static readonly DateTime Start = new(2030, 3, 15, 8, 0, 0);
    private sealed record Reg(int EventId, string Slug, int Id, string Email, string Phone);

    // ---------------- Pass (confirmation) ----------------

    [Theory]
    [InlineData("pass-inperson", null, true)]
    [InlineData("pass-hybrid", "Hybrid", true)]
    [InlineData("pass-online", "Online", false)]
    public async Task Pass(string name, string? format, bool meal)
    {
        // Email + Viber (registrant channel Both), then SMS as the active provider.
        await CaseAsync($"{name}-email-viber", "Both", "Viber", format, meal, confirmFirst: false,
            (admin, reg) => api.ConfirmAsync(admin, reg.EventId, reg.Id));
        await CaseAsync($"{name}-sms", "Viber", "Sms", format, meal, confirmFirst: false,
            (admin, reg) => api.ConfirmAsync(admin, reg.EventId, reg.Id));
    }

    // ---------------- Meeting update / cancel ----------------

    [Theory]
    [InlineData("meeting-update-online", "Online")]
    [InlineData("meeting-update-inperson", null)]
    public async Task Meeting_update(string name, string? format)
    {
        await CaseAsync($"{name}-email-viber", "Both", "Viber", format, false, confirmFirst: true,
            (_, reg) => PassAsync(reg.Id, (p, ev, r) => p.SendMeetingUpdateAsync(ev, r)));
        await CaseAsync($"{name}-sms", "Viber", "Sms", format, false, confirmFirst: true,
            (_, reg) => PassAsync(reg.Id, (p, ev, r) => p.SendMeetingUpdateAsync(ev, r)));
    }

    [Theory]
    [InlineData("meeting-cancel-registration", MeetingCancelReason.RegistrationCancelled)]
    [InlineData("meeting-cancel-event", MeetingCancelReason.EventCancelled)]
    [InlineData("meeting-cancel-now-in-person", MeetingCancelReason.NowInPerson)]
    public async Task Meeting_cancel(string name, MeetingCancelReason reason)
    {
        await CaseAsync($"{name}-email-viber", "Viber", "Viber", "Online", false, confirmFirst: true,
            (_, reg) => PassAsync(reg.Id, (p, ev, r) => p.SendMeetingCancelAsync(ev, r, reason)));
        await CaseAsync($"{name}-sms", "Viber", "Sms", "Online", false, confirmFirst: true,
            (_, reg) => PassAsync(reg.Id, (p, ev, r) => p.SendMeetingCancelAsync(ev, r, reason)));
    }

    // ---------------- Kit claimed / edit link ----------------

    [Theory]
    [InlineData("kit-claimed-by-other", "Ana Cruz", false)]
    [InlineData("kit-claimed-self", null, true)]
    public async Task Kit_claimed(string name, string? claimedBy, bool selfClaimed)
    {
        Task Claim(HttpClient admin, Reg reg) => admin.PostAsJsonAsync($"/api/events/{reg.EventId}/attendance/claim-kit",
            new { registrationIds = new[] { reg.Id }, claimedBy, selfClaimed }).ContinueWith(t => t.Result.EnsureSuccessStatusCode());
        await CaseAsync($"{name}-email-viber", "Both", "Viber", null, false, confirmFirst: true, Claim);
        await CaseAsync($"{name}-sms", "Viber", "Sms", null, false, confirmFirst: true, Claim);
    }

    [Fact]
    public async Task Edit_link()
    {
        Task Ask(HttpClient _, Reg reg) => api.Anonymous().PostAsJsonAsync($"/api/registrations/public/{reg.Slug}/edit-link",
            new { contact = reg.Email }).ContinueWith(t => t.Result.EnsureSuccessStatusCode());
        await CaseAsync("edit-link-email-viber", "Both", "Viber", null, false, confirmFirst: false, Ask);
        await CaseAsync("edit-link-sms", "Viber", "Sms", null, false, confirmFirst: false, Ask);
    }

    // ---------------- Organizer broadcast ----------------

    [Theory]
    [InlineData("broadcast-viber", ChannelMode.Send)]
    [InlineData("broadcast-email-fallback", ChannelMode.Fail)]
    public async Task Broadcast(string name, ChannelMode viber)
    {
        var admin = await api.AdminAsync();
        var reg = await RegisterAsync(admin, "Both", null, false);
        await using var _ = await api.ChannelsAsync(viber: viber);
        await SnapshotAsync(name, reg, async () =>
            (await admin.PostAsJsonAsync($"/api/events/{reg.EventId}/viber-broadcast",
                new { message = "Tee times moved to 9:00 AM.\nSee you there!", fallbackToEmail = true })).EnsureSuccessStatusCode());
    }

    // ---------------- Mail plans ----------------

    [Theory]
    [InlineData("mail-plan-email", "Email", "Viber")]
    [InlineData("mail-plan-viber", "Viber", "Viber")]
    [InlineData("mail-plan-sms", "Viber", "Sms")]
    public async Task Mail_plan(string name, string planChannel, string provider)
    {
        var admin = await api.AdminAsync();
        var reg = await RegisterAsync(admin, "Viber", null, false); // gives the address a phone
        var sendId = await MailPlanAsync(planChannel, reg.Email);
        await using var _ = await api.ChannelsAsync(provider: provider);
        await SnapshotAsync(name, reg, async () =>
        {
            using var scope = api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().DispatchAsync(sendId);
        });
    }

    [Fact]
    public async Task Mail_plan_reminder()
    {
        var admin = await api.AdminAsync();
        var reg = await RegisterAsync(admin, "Email", null, false);
        var sendId = await MailPlanAsync("Email", reg.Email);
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().DispatchAsync(sendId);
        var deliveryId = await DbAsync(async db =>
        {
            var d = await db.MailPlanDeliveries.SingleAsync(x => x.SendId == sendId);
            d.RemindAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            return d.Id;
        });
        await SnapshotAsync("mail-plan-reminder", reg, async () =>
        {
            using var scope = api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().RemindAsync(deliveryId);
        });
    }

    // ---------------- Setup helpers ----------------

    private async Task CaseAsync(string name, string channel, string provider, string? format, bool meal, bool confirmFirst,
        Func<HttpClient, Reg, Task> act)
    {
        var admin = await api.AdminAsync();
        var reg = await RegisterAsync(admin, channel, format, meal);
        await using var _ = await api.ChannelsAsync(provider: provider);
        if (confirmFirst) await api.ConfirmAsync(admin, reg.EventId, reg.Id);
        await SnapshotAsync(name, reg, () => act(admin, reg));
    }

    // A published event with fixed title and dates (so the text does not change between runs),
    // and one pending guest registration on it.
    private async Task<Reg> RegisterAsync(HttpClient admin, string channel, string? format, bool meal)
    {
        var res = await admin.PostAsJsonAsync("/api/events", new
        {
            title = Title,
            description = "",
            startsAt = Start,
            endsAt = Start.AddHours(8),
            venue = "Course",
            isPublicLinkEnabled = true,
            status = "Published",
            format,
            notifyOnKitClaim = true,
            meetingUrl = format is null ? null : "https://meet.example.com/golf",
            meals = meal ? new[] { new { id = 0, name = "Lunch", denomination = 150m, quantity = 1, color = (string?)null, day = 1,
                validFrom = (DateTime?)Start, validTo = (DateTime?)Start.AddHours(8) } } : null,
        });
        res.EnsureSuccessStatusCode();
        var ev = (await res.Content.ReadFromJsonAsync<EventIdBody>())!;
        var email = ApiFixture.NewEmail();
        var phone = ApiFixture.NewPhone();
        var id = await api.RegisterGuestAsync(ev.Id, ev.PublicSlug, email, phone, channel);
        return new Reg(ev.Id, ev.PublicSlug, id, email, phone);
    }

    private async Task PassAsync(int regId, Func<PassService, Event, Registration, Task<PassDelivery>> send)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reg = await db.Registrations.Include(r => r.User).Include(r => r.Session).Include(r => r.Event)
            .SingleAsync(r => r.Id == regId);
        await send(scope.ServiceProvider.GetRequiredService<PassService>(), reg.Event!, reg);
        await db.SaveChangesAsync();
    }

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> MailPlanAsync(string planChannel, string address) => DbAsync(async db =>
    {
        var adminId = await db.Users.Where(u => u.Email == ApiFixture.AdminEmail).Select(u => u.Id).SingleAsync();
        var plan = new MailPlan
        {
            Channel = planChannel,
            CreatedByUserId = adminId,
            EmailsJson = JsonSerializer.Serialize(new[] { address }),
        };
        plan.Sends.Add(new MailPlanSend
        {
            Subject = "Tee times for Saturday",
            // The editor sends XHTML with one root element.
            BodyHtml = "<div><p>Hello golfers,</p><p>Tee times are <b>posted</b>. See the <a href=\"https://example.com/tees\">list</a>.</p></div>",
            Status = MailSendStatus.Scheduled,
            SendAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        db.MailPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan.Sends.Single().Id;
    });

    // ---------------- Capture, normalize, compare ----------------

    private static readonly string[] Channels = ["email", "viber", "sms"];

    // Runs the action and snapshots only the messages it wrote for this registrant.
    private static async Task SnapshotAsync(string name, Reg reg, Func<Task> act)
    {
        var before = Files(reg).ToHashSet();
        await act();
        var written = Files(reg).Where(f => !before.Contains(f)).ToList();

        var text = new StringBuilder();
        foreach (var channel in Channels)
            foreach (var file in written.Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == channel).OrderBy(f => f, StringComparer.Ordinal))
                text.Append($"===== {channel} =====\n").Append(channel == "email" ? Email(file) : Mobile(file)).Append('\n');
        // Every case sends something; an empty capture means the setup broke, not a template.
        Assert.True(text.Length > 0, $"{name}: nothing was sent");

        Compare(name, Normalize(text.ToString(), reg));
    }

    // Message files (not the .png images) whose name carries this registrant's email or phone.
    private static IEnumerable<string> Files(Reg reg) => Channels.SelectMany(c =>
        ApiFixture.Sent(c, reg.Email).Concat(ApiFixture.Sent(c, reg.Phone)).Concat(ApiFixture.Sent(c, reg.Phone.TrimStart('+'))).Distinct());

    private static string Mobile(string file)
    {
        var body = File.ReadAllText(file);
        var png = Path.ChangeExtension(file, null) + ".png";
        return body + (File.Exists(png) ? "[image attached]\n" : "");
    }

    // The parts of an email a reader sees: subject, recipients, HTML, inline images, attachments.
    private static string Email(string file)
    {
        var msg = MimeMessage.Load(file);
        var s = new StringBuilder();
        s.Append($"To: {msg.To}\nCc: {msg.Cc}\nBcc: {msg.Bcc}\nSubject: {msg.Subject}\n");
        foreach (var part in msg.BodyParts.OfType<MimePart>())
        {
            // The body parts themselves are printed below.
            if (part is TextPart body && part.ContentId is null && !part.IsAttachment && body.ContentType.MimeType != "text/calendar")
                continue;
            var label = part.ContentId is not null ? $"inline {part.ContentId}" : $"attachment {part.FileName}";
            s.Append($"[{label}: {part.ContentType.MimeType}");
            if (part.ContentType.Parameters.TryGetValue("method", out string? method)) s.Append($"; method={method}");
            s.Append("]\n");
            if (part is TextPart text && part.ContentType.MimeType == "text/calendar") s.Append(text.Text.Trim()).Append('\n');
        }
        s.Append("--- html ---\n").Append(msg.HtmlBody?.Trim() ?? "(none)").Append('\n');
        if (msg.TextBody is { } plain && !string.IsNullOrWhiteSpace(plain)) s.Append("--- text ---\n").Append(plain.Trim()).Append('\n');
        return s.ToString();
    }

    // Replaces what differs between runs; trailing spaces never matter.
    private static string Normalize(string text, Reg reg)
    {
        text = text.Replace("\r\n", "\n")
            .Replace(reg.Email, "<email>")
            .Replace(reg.Phone, "<phone>")
            .Replace(reg.Phone.TrimStart('+'), "<phone>")
            .Replace(reg.Slug, "<slug>");
        text = Stamp().Replace(text, "DTSTAMP:<now>");
        text = StubNo().Replace(text, "No. <stub>");
        text = GuidDashed().Replace(text, "<guid>");
        text = Guid32().Replace(text, "<guid>");
        return string.Join("\n", text.Split('\n').Select(l => l.TrimEnd()));
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidDashed();
    [GeneratedRegex("(?<![0-9a-fA-F])[0-9a-fA-F]{32}(?![0-9a-fA-F])")]
    private static partial Regex Guid32();
    [GeneratedRegex(@"No\. [0-9A-F]{8}")]
    private static partial Regex StubNo();
    [GeneratedRegex("DTSTAMP:[0-9TZ]+")]
    private static partial Regex Stamp();

    private static void Compare(string name, string actual, [CallerFilePath] string source = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(source)!, "Snapshots", "Messages", name + ".txt");
        var update = Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";
        if (!File.Exists(path) || update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            if (!update) Assert.Fail($"Snapshot {name} created at {path}; review it and rerun.");
            return;
        }
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }
}
