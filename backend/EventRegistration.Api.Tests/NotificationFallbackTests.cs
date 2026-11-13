using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventRegistration.Api.Tests;

// A message is reported failed only after every channel was tried; each channel is tried once.
public class NotificationChainTests
{
    private static readonly NotificationDelivery Chain = new(NullLogger<NotificationDelivery>.Instance);

    private static (ChannelAttempt Attempt, Func<int> Calls) Fake(string channel, DeliveryResult result)
    {
        var calls = 0;
        return (new ChannelAttempt(channel, () => { calls++; return Task.FromResult(result); }), () => calls);
    }

    [Fact]
    public async Task Viber_failure_falls_back_to_email_once()
    {
        var viber = Fake("Viber", DeliveryResult.Failed);
        var email = Fake("email", DeliveryResult.Sent);

        var result = await Chain.DeliverAsync([viber.Attempt, email.Attempt]);

        Assert.Equal("email", result.DeliveredBy);
        Assert.Equal("Viber", result.FirstFailed);
        Assert.Equal((1, 1), (viber.Calls(), email.Calls()));
    }

    [Fact]
    public async Task Email_fallback_off_stops_after_a_real_failure()
    {
        var email = Fake("email", DeliveryResult.Sent);

        var result = await Chain.DeliverAsync([Fake("Viber", DeliveryResult.Failed).Attempt, email.Attempt with { AfterFailure = false }]);

        Assert.False(result.Delivered);
        Assert.Equal(0, email.Calls());
    }

    [Fact]
    public async Task Email_fallback_off_still_sends_email_when_mobile_was_skipped()
    {
        var email = Fake("email", DeliveryResult.Sent);

        var result = await Chain.DeliverAsync([Fake("Viber", DeliveryResult.NotConfigured).Attempt, email.Attempt with { AfterFailure = false }]);

        Assert.Equal("email", result.DeliveredBy);
    }

    [Fact]
    public async Task Stops_at_the_first_channel_that_delivers()
    {
        var viber = Fake("Viber", DeliveryResult.Sent);
        var email = Fake("email", DeliveryResult.Sent);

        var result = await Chain.DeliverAsync([viber.Attempt, email.Attempt]);

        Assert.Equal("Viber", result.DeliveredBy);
        Assert.Equal(0, email.Calls());
    }

    [Fact]
    public async Task All_failing_is_not_delivered()
    {
        var result = await Chain.DeliverAsync([Fake("Viber", DeliveryResult.Failed).Attempt, Fake("email", DeliveryResult.Failed).Attempt]);

        Assert.False(result.Delivered);
        Assert.Equal(2, result.Tried.Count);
    }

    [Fact]
    public async Task A_turned_off_channel_is_skipped_outside_development()
    {
        var result = await Chain.DeliverAsync([Fake("Viber", DeliveryResult.NotConfigured).Attempt, Fake("email", DeliveryResult.Sent).Attempt]);

        Assert.Equal("email", result.DeliveredBy);
        Assert.Null(result.FirstFailed);
    }

    [Fact]
    public async Task A_turned_off_channel_counts_as_sent_in_development()
    {
        var email = Fake("email", DeliveryResult.Sent);

        var result = await Chain.DeliverAsync([Fake("Viber", DeliveryResult.Disabled).Attempt, email.Attempt]);

        Assert.True(result.DeliveredDisabled);
        Assert.Equal(0, email.Calls());
    }

    [Theory]
    [InlineData("Viber", true, false, "Viber failed; pass sent by email.")]
    [InlineData(null, false, false, "Pass sent by email.")]
    [InlineData(null, true, true, "Pass sent (channels are disabled).")]
    public void Admin_message_names_the_channel_used(string? fellBackFrom, bool attempted, bool off, string expected) =>
        Assert.Equal(expected, new PassDelivery(["email"], fellBackFrom, off, false, attempted).Message);

    [Fact]
    public void Admin_message_when_nothing_delivered()
    {
        Assert.Equal("Pass could not be sent by any channel.", new PassDelivery([], Attempted: true).Message);
        Assert.Equal("Email could not be sent; retrying in 5 minutes.", new PassDelivery([], EmailRetryQueued: true, Attempted: true).Message);
    }

    [Theory]
    [InlineData("09171234567", "+639171234567")]
    [InlineData("+63 917 123 4567", "+639171234567")]
    [InlineData("639171234567", "+639171234567")]
    [InlineData("+1 (415) 555-0100", "+14155550100")]
    [InlineData("+44 7911 123456", "+447911123456")]
    [InlineData("+63 817 123 4567", null)]
    [InlineData("+0123456789", null)]
    [InlineData("+123", null)]
    [InlineData("+1234567890123456", null)]
    [InlineData("12345678", null)]
    public void Phones_are_stored_with_their_country_code(string raw, string? expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(raw));

    [Theory]
    [InlineData("+639171234567", true)]
    [InlineData("+14155550100", false)]
    [InlineData(null, false)]
    public void A_number_is_supported_only_when_its_country_code_is_listed(string? phone, bool expected) =>
        Assert.Equal(expected, PhoneNumber.Supported(phone, ["63", "44"]));

    [Fact]
    public void Meeting_cancel_snapshot_survives_a_round_trip()
    {
        var invite = new CalendarInvite.Details("uid@nxpass", 4, "Golf Day", new DateTime(2027, 2, 17, 8, 0, 0),
            new DateTime(2027, 2, 17, 17, 0, 0), "Course", "https://example.com/e/golf", null,
            "PAL GOLF", "events@example.com", "Mia Santos", "mia@example.com");
        var snapshot = new MeetingCancelSnapshot(invite, "Mia Santos", MeetingCancelReason.EventCancelled);

        var back = JsonSerializer.Deserialize<MeetingCancelSnapshot>(JsonSerializer.Serialize(snapshot))!;

        Assert.Equal(snapshot.Invite with { Recipients = null }, back.Invite with { Recipients = null });
        Assert.Equal(MeetingCancelReason.EventCancelled, back.Reason);
    }
}

// Outbox rules: a turned-off channel writes its message to outbox/ only in Development.
public class OutboxRuleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "outbox-test-" + Guid.NewGuid().ToString("N"));
    private string Outbox => Path.Combine(_root, "outbox");
    private int OutboxFiles => Directory.Exists(Outbox) ? Directory.GetFiles(Outbox).Length : 0;

    private sealed class Env(string root, string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData("Development", DeliveryResult.Disabled, true)]
    [InlineData("Production", DeliveryResult.NotConfigured, false)]
    public async Task Viber_off(string env, DeliveryResult expected, bool written)
    {
        var viber = new ViberService(new ViberSettings(), NullLogger<ViberService>.Instance, new Env(_root, env));

        Assert.Equal(expected, await viber.SendTextAsync("+639171234567", "Hi", "Hello"));
        Assert.Equal(written, OutboxFiles > 0);
    }

    [Theory]
    [InlineData("Development", DeliveryResult.Disabled, true)]
    [InlineData("Production", DeliveryResult.NotConfigured, false)]
    public async Task Sms_off(string env, DeliveryResult expected, bool written)
    {
        var sms = new SmsService(new SmsSettings(), NullLogger<SmsService>.Instance, new Env(_root, env));

        Assert.Equal(expected, await sms.SendTextAsync("+639171234567", "Hi", "Hello"));
        Assert.Equal(written, OutboxFiles > 0);
    }

    [Theory]
    [InlineData("Development", DeliveryResult.Disabled, true)]
    [InlineData("Production", DeliveryResult.NotConfigured, false)]
    public async Task Email_off(string env, DeliveryResult expected, bool written)
    {
        var email = new EmailService(new EmailSettings { FromAddress = "events@example.com" },
            NullLogger<EmailService>.Instance, null!, new Env(_root, env));

        Assert.Equal(expected, await email.SendMessageAsync(["mia@example.com"], [], "Hi", "<p>Hello</p>"));
        Assert.Equal(written, OutboxFiles > 0);
    }

    [Fact]
    public async Task Viber_on_but_failing_writes_the_outbox_and_reports_failed()
    {
        // An unreachable gateway: the send fails, the message is kept in outbox/.
        var viber = new ViberService(new ViberSettings { Enabled = true, BaseUrl = "http://127.0.0.1:9", ApiKey = "k", Sender = "s" },
            NullLogger<ViberService>.Instance, new Env(_root, "Production"));

        Assert.Equal(DeliveryResult.Failed, await viber.SendTextAsync("+639171234567", "Hi", "Hello"));
        Assert.True(OutboxFiles > 0);
    }
}

// End to end through the API (environment "Testing", so every turned-off channel is skipped).
[Collection("api")]
public class NotificationFallbackApiTests(ApiFixture api)
{
    private record EventBody(int Id, string PublicSlug);
    private record ConfirmBody(int Confirmed, int Emailed, int NoEmail, int NotSent);

    [Fact]
    public async Task With_every_channel_off_a_confirmed_pass_is_reported_not_sent()
    {
        await using var off = await api.ChannelsAsync(ChannelMode.Off, ChannelMode.Off, ChannelMode.Off);
        var admin = await api.AdminAsync();
        var start = DateTime.Today.AddDays(10);
        var created = await admin.PostAsJsonAsync("/api/events", new
        {
            title = $"Golf Day {Guid.NewGuid():N}"[..20], description = "", startsAt = start,
            endsAt = start.AddHours(8), venue = "Course", isPublicLinkEnabled = true, status = "Published",
        });
        created.EnsureSuccessStatusCode();
        var ev = (await created.Content.ReadFromJsonAsync<EventBody>())!;
        (await api.Anonymous().PostAsJsonAsync($"/api/registrations/guest/{ev.PublicSlug}", new
        {
            fullName = "Mia Santos", email = ApiFixture.NewEmail(), phone = "09171234567",
            senateNo = $"ID-{Guid.NewGuid():N}"[..10],
        })).EnsureSuccessStatusCode();
        int regId;
        using (var scope = api.Services.CreateScope())
            regId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Registrations
                .Where(r => r.EventId == ev.Id).Select(r => r.Id).SingleAsync();

        var res = await admin.PostAsJsonAsync($"/api/events/{ev.Id}/attendance/confirm", new { registrationIds = new[] { regId } });

        res.EnsureSuccessStatusCode();
        var body = (await res.Content.ReadFromJsonAsync<ConfirmBody>())!;
        Assert.Equal((1, 0, 1), (body.Confirmed, body.Emailed, body.NotSent));
        using var check = api.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Registrations.SingleAsync(r => r.Id == regId)).PassEmailSentAt);
        // A turned-off email is skipped, not failed, so nothing is queued for retry.
        Assert.False(await db.EmailRetries.AnyAsync(r => r.RegistrationId == regId));

        var resend = await admin.PostAsync($"/api/events/{ev.Id}/attendance/{regId}/resend-pass", null);
        Assert.Equal(HttpStatusCode.BadRequest, resend.StatusCode);
        Assert.Equal("Pass could not be sent by any channel.", (await resend.Content.ReadFromJsonAsync<ErrorBody>())!.Message);
    }

    [Fact]
    public async Task Admin_can_save_a_phone_only_when_its_country_code_is_listed()
    {
        var admin = await api.AdminAsync();
        object User(string email) => new
        {
            firstName = "Ana", lastName = "Cruz", email, password = "Passw0rd!", role = "Participant",
            phone = "+1 415 555 0100", notificationChannel = "Email", birthdate = "1990-01-01",
        };

        var rejected = await admin.PostAsJsonAsync("/api/users", User(ApiFixture.NewEmail()));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var added = await admin.PostAsJsonAsync("/api/phone-countries", new { countryCode = "1" });
        added.EnsureSuccessStatusCode();
        var row = await added.Content.ReadFromJsonAsync<JsonElement>();
        try
        {
            var saved = await admin.PostAsJsonAsync("/api/users", User(ApiFixture.NewEmail()));
            saved.EnsureSuccessStatusCode();
            Assert.Equal("+14155550100", (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("phone").GetString());
        }
        finally
        {
            (await admin.DeleteAsync($"/api/phone-countries/{row.GetProperty("id").GetInt32()}")).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Phone_country_list_is_public_and_seeded()
    {
        var rows = await api.Anonymous().GetFromJsonAsync<JsonElement>("/api/phone-countries");

        Assert.Contains(rows.EnumerateArray(), r => r.GetProperty("countryCode").GetString() == "63");
    }

    [Fact]
    public async Task Mobile_channel_label_is_public()
    {
        var body = await api.Anonymous().GetFromJsonAsync<JsonElement>("/api/settings/mobile-channel");

        Assert.Contains(body.GetProperty("mobileProvider").GetString(), new[] { "Viber", "Sms" });
    }

    // EmailService whose sends always fail, to exercise EmailSender's queue.
    private sealed class FailingEmail(IServiceProvider sp) : EmailService(sp.GetRequiredService<EmailSettings>(),
        NullLogger<EmailService>.Instance, sp.GetRequiredService<SettingsService>(), sp.GetRequiredService<IWebHostEnvironment>())
    {
        public int Calls;
        public override Task<DeliveryResult> SendMessageAsync(IReadOnlyCollection<string> to, IReadOnlyCollection<string> cc,
            string subject, string htmlBody, byte[]? inlineQrPng = null, string cid = "qrcode",
            IReadOnlyDictionary<string, byte[]>? inlineImages = null, IReadOnlyList<EmailAttachment>? attachments = null,
            bool includeDefaultCc = false)
        {
            Calls++;
            return Task.FromResult(DeliveryResult.Failed);
        }
    }

    [Fact]
    public async Task A_failed_email_is_queued_whole_for_retry()
    {
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var failing = new FailingEmail(sp);
        var sender = new EmailSender(failing, sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<EmailSender>.Instance);
        var subject = $"Queued {Guid.NewGuid():N}";
        var ics = new EmailAttachment("invite.ics", [1, 2, 3], "text/calendar");

        var result = await sender.SendAsync(["mia@example.com"], subject, "<p>Hi</p>", attachments: [ics], includeDefaultCc: true);

        Assert.True(result.RetryQueued);
        Assert.True(result.Accepted);
        var db = sp.GetRequiredService<AppDbContext>();
        var row = await db.EmailRetries.SingleAsync(r => r.Kind == EmailRetryKinds.Message && r.SnapshotJson!.Contains(subject));
        var message = JsonSerializer.Deserialize<QueuedEmail>(row.SnapshotJson!)!;
        Assert.Equal(["mia@example.com"], message.To);
        Assert.Equal(ics.Content, message.Attachments![0].Content);
        Assert.True(message.IncludeDefaultCc); // the Settings CC copy survives the retry

        Assert.Equal(DeliveryResult.Failed, await sender.SendNowAsync(["mia@example.com"], subject, "<p>Hi</p>"));
        Assert.Equal(1, await db.EmailRetries.CountAsync(r => r.SnapshotJson!.Contains(subject)));
    }

    [Fact]
    public async Task A_turned_off_email_is_not_queued()
    {
        await using var off = await api.ChannelsAsync(ChannelMode.Off, ChannelMode.Off, ChannelMode.Off);
        using var scope = api.Services.CreateScope();
        var subject = $"Off {Guid.NewGuid():N}";

        var result = await scope.ServiceProvider.GetRequiredService<EmailSender>().SendAsync(["mia@example.com"], subject, "<p>Hi</p>");

        Assert.False(result.Accepted);
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailRetries.AnyAsync(r => r.SnapshotJson!.Contains(subject)));
    }

    [Fact]
    public async Task A_queued_message_is_retried_from_its_snapshot()
    {
        await using var off = await api.ChannelsAsync(ChannelMode.Off, ChannelMode.Off, ChannelMode.Off);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = new EmailRetry
        {
            Kind = EmailRetryKinds.Message, NextAttemptAt = DateTimeOffset.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(new QueuedEmail(["mia@example.com"], [], "Hi", "<p>Hi</p>", null, null)),
        };
        db.EmailRetries.Add(row);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<EmailRetryDispatcher>().DispatchAsync(row.Id);

        // Email is off in tests, so the send is not delivered and the row waits for the next try.
        db.ChangeTracker.Clear();
        var after = await db.EmailRetries.SingleAsync(r => r.Id == row.Id);
        Assert.Equal((EmailRetryStatus.Queued, 1), (after.Status, after.Attempts));
    }

    [Fact]
    public async Task Email_retry_for_a_deleted_registration_is_dropped()
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = new EmailRetry { RegistrationId = int.MaxValue, Kind = EmailRetryKinds.Pass, NextAttemptAt = DateTimeOffset.UtcNow };
        db.EmailRetries.Add(row);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<EmailRetryDispatcher>().DispatchAsync(row.Id);

        db.ChangeTracker.Clear();
        var after = await db.EmailRetries.SingleAsync(r => r.Id == row.Id);
        Assert.Equal((EmailRetryStatus.Failed, 1), (after.Status, after.Attempts));
    }

    [Fact]
    public async Task Meeting_cancel_retry_is_sent_from_its_snapshot_and_requeued_while_email_is_off()
    {
        await using var off = await api.ChannelsAsync(ChannelMode.Off, ChannelMode.Off, ChannelMode.Off);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invite = new CalendarInvite.Details("uid@nxpass", 2, "Golf Day", DateTime.Today.AddDays(3),
            DateTime.Today.AddDays(3).AddHours(8), null, "https://example.com/e/golf", null,
            "PAL GOLF", "events@example.com", "Mia Santos", "mia@example.com");
        var row = new EmailRetry
        {
            RegistrationId = int.MaxValue - 1, Kind = EmailRetryKinds.MeetingCancel, NextAttemptAt = DateTimeOffset.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(new MeetingCancelSnapshot(invite, "Mia Santos", MeetingCancelReason.EventCancelled)),
        };
        db.EmailRetries.Add(row);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<EmailRetryDispatcher>().DispatchAsync(row.Id);

        db.ChangeTracker.Clear();
        var after = await db.EmailRetries.SingleAsync(r => r.Id == row.Id);
        // Not dropped for the missing registration: the snapshot was used and will be tried again.
        Assert.Equal((EmailRetryStatus.Queued, 1), (after.Status, after.Attempts));
    }
}
