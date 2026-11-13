using System.Net.Http.Json;
using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventRegistration.Api.Tests;

// Every message the app sends, on every channel, with its fallback, failure, off and retry cases.
// Channels write to test/sent/{email,viber,sms} (see ApiFixture), so each test checks which folder
// received the message. Case ids (P01, M01, R01, ...) match documentation/notification-test-matrix.md.
[Collection("api")]
public class NotificationMatrixTests(ApiFixture api)
{
    private const string E = "email", V = "viber", S = "sms";
    private sealed record Reg(int EventId, string Slug, string Title, int Id, string Email, string Phone);

    private async Task<Reg> GuestAsync(HttpClient admin, string channel, string? format = null, bool meal = false)
    {
        var ev = await api.CreateEventAsync(admin, format, meal);
        var email = ApiFixture.NewEmail();
        var phone = ApiFixture.NewPhone();
        var id = await api.RegisterGuestAsync(ev.Id, ev.Slug, email, phone, channel);
        return new Reg(ev.Id, ev.Slug, ev.Title, id, email, phone);
    }

    private static int Count(string channel, string recipient, string subjectPart) =>
        ApiFixture.Sent(channel, recipient).Count(f => Path.GetFileName(f).Contains(subjectPart));

    // Runs a PassService call on a freshly loaded registration and saves what it stamped.
    private async Task<PassDelivery> PassAsync(int regId, Func<PassService, Event, Registration, Task<PassDelivery>> send)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reg = await db.Registrations.Include(r => r.User).Include(r => r.Session).Include(r => r.Event)
            .SingleAsync(r => r.Id == regId);
        var result = await send(scope.ServiceProvider.GetRequiredService<PassService>(), reg.Event!, reg);
        await db.SaveChangesAsync();
        return result;
    }

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = api.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> RetriesAsync(int regId, string kind) => DbAsync(db => db.EmailRetries
        .CountAsync(r => r.RegistrationId == regId && r.Kind == kind && r.Status == EmailRetryStatus.Queued));

    private Task<int> MessageRetriesAsync(string recipient) => DbAsync(db => db.EmailRetries
        .CountAsync(r => r.Kind == EmailRetryKinds.Message && r.SnapshotJson!.Contains(recipient)));

    // Turns one channel off for +63 for the test (the number's country is then "not listed").
    private async Task<IAsyncDisposable> Unlisted(string channel)
    {
        await SetChannel(channel, false);
        return new Undo(() => SetChannel(channel, true));
    }

    private Task<int> SetChannel(string channel, bool on) => DbAsync(db => db.PhoneCountries
        .Where(c => c.CountryCode == "63")
        .ExecuteUpdateAsync(u =>
        {
            if (channel == MobileProviders.Sms) u.SetProperty(c => c.Sms, on);
            else u.SetProperty(c => c.Viber, on);
        }));

    private sealed class Undo(Func<Task> undo) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await undo();
    }

    // Makes every queued retry due now and runs it once.
    private async Task DispatchRetriesAsync(Func<EmailRetry, bool> which)
    {
        var ids = (await DbAsync(db => db.EmailRetries.Where(r => r.Status == EmailRetryStatus.Queued).ToListAsync()))
            .Where(which).Select(r => r.Id).ToList();
        foreach (var id in ids)
        {
            using var scope = api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.EmailRetries.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await scope.ServiceProvider.GetRequiredService<EmailRetryDispatcher>().DispatchAsync(id);
        }
    }

    // ---------------- P: pass delivery chain (confirm) ----------------

    public static TheoryData<string, string, string, ChannelMode, ChannelMode, ChannelMode, bool, string, Expect> PassCases => new()
    {
        // id, provider, registrant channel, email, viber, sms, email fallback on, unlisted channel, expected (see Expect)
        { "P01", "Viber", "Email", ChannelMode.Send, ChannelMode.Send, ChannelMode.Send, true, "", Expect.Email },
        { "P02", "Viber", "Email", ChannelMode.Fail, ChannelMode.Send, ChannelMode.Send, true, "", Expect.Retry | Expect.NotSent },
        { "P03", "Viber", "Email", ChannelMode.Off, ChannelMode.Send, ChannelMode.Send, true, "", Expect.NotSent },
        { "P04", "Viber", "Viber", ChannelMode.Send, ChannelMode.Send, ChannelMode.Send, true, "", Expect.Viber },
        { "P05", "Viber", "Viber", ChannelMode.Send, ChannelMode.Fail, ChannelMode.Send, true, "", Expect.Email },
        { "P06", "Viber", "Viber", ChannelMode.Send, ChannelMode.Fail, ChannelMode.Send, false, "", Expect.NotSent },
        { "P07", "Viber", "Viber", ChannelMode.Send, ChannelMode.Off, ChannelMode.Send, false, "", Expect.Email },
        { "P08", "Sms", "Viber", ChannelMode.Send, ChannelMode.Send, ChannelMode.Send, true, "", Expect.Sms },
        { "P09", "Sms", "Viber", ChannelMode.Send, ChannelMode.Send, ChannelMode.Fail, true, "", Expect.Email },
        { "P10", "Sms", "Viber", ChannelMode.Send, ChannelMode.Send, ChannelMode.Fail, false, "", Expect.NotSent },
        { "P11", "Sms", "Viber", ChannelMode.Send, ChannelMode.Send, ChannelMode.Off, false, "", Expect.Email },
        { "P12", "Viber", "Viber", ChannelMode.Fail, ChannelMode.Fail, ChannelMode.Send, true, "", Expect.Retry | Expect.NotSent },
        { "P13", "Viber", "Both", ChannelMode.Send, ChannelMode.Send, ChannelMode.Send, true, "", Expect.Email | Expect.Viber },
        { "P14", "Viber", "Both", ChannelMode.Send, ChannelMode.Fail, ChannelMode.Send, true, "", Expect.Email },
        { "P15", "Viber", "Both", ChannelMode.Fail, ChannelMode.Send, ChannelMode.Send, true, "", Expect.Viber | Expect.Retry },
        { "P16", "Sms", "Both", ChannelMode.Send, ChannelMode.Send, ChannelMode.Send, true, "", Expect.Email | Expect.Sms },
        { "P17", "Viber", "Viber", ChannelMode.Send, ChannelMode.Send, ChannelMode.Send, false, "Viber", Expect.Email },
        { "P18", "Sms", "Viber", ChannelMode.Send, ChannelMode.Send, ChannelMode.Send, false, "Sms", Expect.Email },
        { "P19", "Viber", "Both", ChannelMode.Send, ChannelMode.Send, ChannelMode.Send, false, "Viber", Expect.Email },
    };

    [Flags]
    public enum Expect { None = 0, Email = 1, Viber = 2, Sms = 4, Retry = 8, NotSent = 16 }

    [Theory]
    [MemberData(nameof(PassCases))]
    public async Task Pass_delivery(string id, string provider, string channel, ChannelMode email, ChannelMode viber,
        ChannelMode sms, bool fallbackToEmail, string unlisted, Expect expect)
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, channel);
        await using var channels = await api.ChannelsAsync(email, viber, sms, fallbackToEmail, fallbackToEmail, provider);
        await using var country = unlisted.Length > 0 ? await Unlisted(unlisted) : new Undo(() => Task.CompletedTask);

        var confirm = await api.ConfirmAsync(admin, reg.EventId, reg.Id);

        var subject = "Registration confirmed";
        Assert.True(Count(E, reg.Email, subject) == (expect.HasFlag(Expect.Email) ? 1 : 0), $"{id}: email");
        Assert.True(Count(V, reg.Phone, reg.Title) == (expect.HasFlag(Expect.Viber) ? 1 : 0), $"{id}: viber");
        Assert.True(Count(S, reg.Phone, reg.Title) == (expect.HasFlag(Expect.Sms) ? 1 : 0), $"{id}: sms");
        Assert.True(await RetriesAsync(reg.Id, EmailRetryKinds.Pass) == (expect.HasFlag(Expect.Retry) ? 1 : 0), $"{id}: retry");
        // A queued email retry is reported as "retrying", not "not sent"; either way nothing was delivered.
        var notSent = expect.HasFlag(Expect.NotSent) && !expect.HasFlag(Expect.Retry);
        Assert.True(confirm.NotSent == (notSent ? 1 : 0), $"{id}: not sent");
        var stamped = await DbAsync(db => db.Registrations.Where(r => r.Id == reg.Id).Select(r => r.PassEmailSentAt).SingleAsync());
        Assert.True((stamped is null) == expect.HasFlag(Expect.NotSent), $"{id}: PassEmailSentAt");
    }

    // ---------------- M: every message type ----------------

    [Fact] // M01
    public async Task Pass_email_carries_the_QR_and_for_hybrid_the_calendar_invite()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Email", format: "Hybrid");

        await api.ConfirmAsync(admin, reg.EventId, reg.Id);

        var eml = await File.ReadAllTextAsync(ApiFixture.Sent(E, reg.Email).Single());
        Assert.Contains("Content-Id", eml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invite.ics", eml);
        Assert.Contains("method=REQUEST", eml); // calendar part header (its body is base64)
    }

    [Fact] // M02
    public async Task Viber_pass_sends_the_QR_picture_and_the_meal_stubs()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Viber", meal: true);

        await api.ConfirmAsync(admin, reg.EventId, reg.Id);

        Assert.Single(ApiFixture.Sent(V, reg.Phone), f => !f.Contains("meal-stubs"));
        Assert.Single(Directory.GetFiles(ApiFixture.SentDir(V), "*.png").Where(f => f.Contains(reg.Phone)));
        Assert.Equal(1, Count(V, reg.Phone, "meal-stubs"));
    }

    [Fact] // M03
    public async Task Resend_pass_goes_by_email_only()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Viber");
        await api.ConfirmAsync(admin, reg.EventId, reg.Id);

        (await admin.PostAsync($"/api/events/{reg.EventId}/attendance/{reg.Id}/resend-pass", null)).EnsureSuccessStatusCode();

        Assert.Equal(1, Count(E, reg.Email, "Registration confirmed"));
        Assert.Equal(1, Count(V, reg.Phone, reg.Title)); // the original Viber pass only
    }

    [Fact] // M04
    public async Task Kit_claimed_reaches_email_and_mobile_for_both()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Both");
        await api.ConfirmAsync(admin, reg.EventId, reg.Id);

        (await admin.PostAsJsonAsync($"/api/events/{reg.EventId}/attendance/claim-kit",
            new { registrationIds = new[] { reg.Id }, claimedBy = "Ana", selfClaimed = false })).EnsureSuccessStatusCode();

        Assert.Equal(1, Count(E, reg.Email, "Event kit claimed"));
        Assert.Equal(1, Count(V, reg.Phone, "Event kit claimed"));
    }

    [Fact] // M05
    public async Task Edit_link_reaches_email_and_mobile_for_both()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Both");

        (await api.Anonymous().PostAsJsonAsync($"/api/registrations/public/{reg.Slug}/edit-link", new { contact = reg.Email }))
            .EnsureSuccessStatusCode();

        Assert.Equal(1, Count(E, reg.Email, "Edit your registration"));
        Assert.Equal(1, Count(V, reg.Phone, "Edit your registration"));
    }

    [Fact] // M06
    public async Task Meeting_update_sends_the_calendar_update_and_a_mobile_text()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Both", format: "Online");
        await api.ConfirmAsync(admin, reg.EventId, reg.Id);

        await PassAsync(reg.Id, (p, ev, r) => p.SendMeetingUpdateAsync(ev, r));

        var eml = await File.ReadAllTextAsync(ApiFixture.Sent(E, reg.Email).Single(f => f.Contains("Updated")));
        Assert.Contains("method=REQUEST", eml); // calendar part header (its body is base64)
        Assert.Equal(1, Count(V, reg.Phone, "Updated"));
    }

    [Fact] // M07
    public async Task Meeting_cancel_withdraws_the_calendar_entry_and_texts_a_mobile_registrant()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Viber", format: "Online");
        await api.ConfirmAsync(admin, reg.EventId, reg.Id);

        await PassAsync(reg.Id, (p, ev, r) => p.SendMeetingCancelAsync(ev, r, MeetingCancelReason.EventCancelled));

        var eml = await File.ReadAllTextAsync(ApiFixture.Sent(E, reg.Email).Single(f => f.Contains("Cancelled")));
        Assert.Contains("method=CANCEL", eml);
        Assert.Equal(1, Count(V, reg.Phone, "Cancelled"));
    }

    [Fact] // M08
    public async Task Meeting_cancel_email_failure_is_queued_with_its_snapshot()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Email", format: "Online");
        await api.ConfirmAsync(admin, reg.EventId, reg.Id);
        await using var _ = await api.ChannelsAsync(email: ChannelMode.Fail);

        await PassAsync(reg.Id, (p, ev, r) => p.SendMeetingCancelAsync(ev, r, MeetingCancelReason.EventCancelled));

        Assert.Equal(1, await DbAsync(db => db.EmailRetries.CountAsync(r =>
            r.RegistrationId == reg.Id && r.Kind == EmailRetryKinds.MeetingCancel && r.SnapshotJson != null)));
    }

    private async Task<(TestTeam Team, int UserId, string Email)> TeamMemberAsync(HttpClient admin)
    {
        var team = await api.CreateTeamAsync(admin, "Regular");
        var email = ApiFixture.NewEmail();
        var res = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/members", ApiFixture.Member(email));
        res.EnsureSuccessStatusCode();
        return (team, (await res.Content.ReadFromJsonAsync<AddResultBody>())!.Member.UserId, email);
    }

    private sealed record NoticeBody(int Sent, int Failed);

    [Theory] // M09 / M10 / M11
    [InlineData("M09", ChannelMode.Send, 1, 0, 0)]
    [InlineData("M10", ChannelMode.Fail, 1, 0, 1)]
    [InlineData("M11", ChannelMode.Off, 0, 1, 0)]
    public async Task Team_registration_notice(string id, ChannelMode email, int sent, int failed, int queued)
    {
        var admin = await api.AdminAsync();
        var (team, userId, address) = await TeamMemberAsync(admin);
        var cc = ApiFixture.NewEmail();
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SettingsService>().SetAsync(SettingKeys.EmailCc, cc);
        await using var _ = await api.ChannelsAsync(email: email);
        try
        {
            var res = await team.LeadClient.PostAsJsonAsync("/api/teams/mine/registration-notice", new { userIds = new[] { userId } });

            var body = (await res.Content.ReadFromJsonAsync<NoticeBody>())!;
            Assert.True((body.Sent, body.Failed) == (sent, failed), $"{id}: counts");
            Assert.True(await MessageRetriesAsync(address) == queued, $"{id}: retry");
            if (email == ChannelMode.Send)
            {
                var eml = await File.ReadAllTextAsync(ApiFixture.Sent(E, address).Single());
                Assert.Contains("Complete your registration", eml);
                Assert.Contains(cc, eml); // the Settings CC copy
            }
        }
        finally
        {
            using var scope = api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SettingsService>().SetAsync(SettingKeys.EmailCc, "");
        }
    }

    private sealed record InviteBody(int Sent, int Failed);

    [Theory] // M12 / M13 / M14
    [InlineData("M12", ChannelMode.Send, 1, 0, 0)]
    [InlineData("M13", ChannelMode.Fail, 1, 0, 1)]
    [InlineData("M14", ChannelMode.Off, 0, 1, 0)]
    public async Task Direct_event_invite(string id, ChannelMode email, int sent, int failed, int queued)
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var address = ApiFixture.NewEmail();
        await using var _ = await api.ChannelsAsync(email: email);

        var res = await admin.PostAsJsonAsync($"/api/events/{ev.Id}/invitations", new { emails = new[] { address } });

        var body = (await res.Content.ReadFromJsonAsync<InviteBody>())!;
        Assert.True((body.Sent, body.Failed) == (sent, failed), $"{id}: counts");
        Assert.True(await MessageRetriesAsync(address) == queued, $"{id}: retry");
        Assert.True(Count(E, address, "invited") == (email == ChannelMode.Send ? 1 : 0), $"{id}: email");
    }

    [Theory] // M15 / M16
    [InlineData("M15", ChannelMode.Send, 1, 0)]
    [InlineData("M16", ChannelMode.Fail, 0, 1)]
    public async Task Calendar_update_to_invitees(string id, ChannelMode email, int sentCount, int queued)
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var address = ApiFixture.NewEmail();
        (await admin.PostAsJsonAsync($"/api/events/{ev.Id}/invitations", new { emails = new[] { address } })).EnsureSuccessStatusCode();
        await using var _ = await api.ChannelsAsync(email: email);

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await scope.ServiceProvider.GetRequiredService<CalendarInvitationNotifier>().NotifyAsync(await db.Events.SingleAsync(e => e.Id == ev.Id));
        }

        Assert.True(Count(E, address, "Updated") == sentCount, $"{id}: email");
        Assert.True(await MessageRetriesAsync(address) == queued, $"{id}: retry");
    }

    [Theory] // M17 / M18
    [InlineData("M17", ChannelMode.Send, InvitationEmailStatus.Sent)]
    [InlineData("M18", ChannelMode.Fail, InvitationEmailStatus.Queued)]
    public async Task Composed_invitation(string id, ChannelMode email, string status)
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var address = ApiFixture.NewEmail();
        await using var _ = await api.ChannelsAsync(email: email);

        (await admin.PostAsJsonAsync($"/api/events/{ev.Id}/invitations/compose",
            new { to = new[] { address }, subject = "Join us", bodyHtml = "<p>Hello</p>" })).EnsureSuccessStatusCode();

        var saved = await DbAsync(db => db.InvitationEmails.Where(m => m.EventId == ev.Id).Select(m => m.Status).SingleAsync());
        Assert.True(saved == status, $"{id}: status {saved}");
        Assert.True(Count(E, address, "Join us") == (email == ChannelMode.Send ? 1 : 0), $"{id}: email");
    }

    // A mail plan with one receiver (a guest with a phone); returns the send id.
    private async Task<int> MailPlanAsync(string planChannel, string address)
    {
        return await DbAsync(async db =>
        {
            var adminId = await db.Users.Where(u => u.Email == ApiFixture.AdminEmail).Select(u => u.Id).SingleAsync();
            var plan = new MailPlan
            {
                Channel = planChannel, CreatedByUserId = adminId,
                EmailsJson = JsonSerializer.Serialize(new[] { address }),
            };
            plan.Sends.Add(new MailPlanSend
            {
                Subject = $"Plan {Guid.NewGuid():N}"[..13], BodyHtml = "<p>Tee times</p>",
                Status = MailSendStatus.Scheduled, SendAt = DateTimeOffset.UtcNow, NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            });
            db.MailPlans.Add(plan);
            await db.SaveChangesAsync();
            return plan.Sends.Single().Id;
        });
    }

    private async Task DispatchPlanAsync(int sendId)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().DispatchAsync(sendId);
    }

    [Theory] // M19 - M23
    [InlineData("M19", "Email", ChannelMode.Send, true, "", 1, 0)]
    [InlineData("M20", "Viber", ChannelMode.Send, true, "", 0, 1)]
    [InlineData("M21", "Viber", ChannelMode.Fail, true, "", 1, 0)]
    [InlineData("M22", "Viber", ChannelMode.Fail, false, "", 0, 0)]
    [InlineData("M23", "Viber", ChannelMode.Send, false, "Viber", 1, 0)]
    public async Task Mail_plan(string id, string planChannel, ChannelMode viber, bool fallbackToEmail, string unlisted,
        int emails, int vibers)
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Viber"); // gives the address a phone
        var sendId = await MailPlanAsync(planChannel, reg.Email);
        await using var _ = await api.ChannelsAsync(viber: viber, viberFallbackToEmail: fallbackToEmail);
        await using var country = unlisted.Length > 0 ? await Unlisted(unlisted) : new Undo(() => Task.CompletedTask);

        await DispatchPlanAsync(sendId);

        var subject = await DbAsync(db => db.MailPlanSends.Where(s => s.Id == sendId).Select(s => s.Subject).SingleAsync());
        Assert.True(Count(E, reg.Email, subject) == emails, $"{id}: email");
        Assert.True(Count(V, reg.Phone, subject) == vibers, $"{id}: viber");
        Assert.True(Count(S, reg.Phone, subject) == 0, $"{id}: sms");
        var status = await DbAsync(db => db.MailPlanSends.Where(s => s.Id == sendId).Select(s => s.Status).SingleAsync());
        Assert.True(status == MailSendStatus.Sent, $"{id}: plan {status}");
    }

    [Theory] // M24 / M25
    [InlineData("M24", ChannelMode.Send, 1, 0)]
    [InlineData("M25", ChannelMode.Fail, 0, 1)]
    public async Task Mail_plan_reminder(string id, ChannelMode email, int sentCount, int queued)
    {
        var admin = await api.AdminAsync();
        var address = ApiFixture.NewEmail();
        var sendId = await MailPlanAsync("Email", address);
        await DispatchPlanAsync(sendId);
        var deliveryId = await DbAsync(async db =>
        {
            var d = await db.MailPlanDeliveries.SingleAsync(x => x.SendId == sendId);
            d.RemindAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            return d.Id;
        });
        await using var _ = await api.ChannelsAsync(email: email);

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().RemindAsync(deliveryId);

        Assert.True(Count(E, address, "Reminder") == sentCount, $"{id}: email");
        Assert.True(await MessageRetriesAsync(address) == queued, $"{id}: retry");
    }

    [Theory] // M26 / M27
    [InlineData("M26", ChannelMode.Send, 1)]
    [InlineData("M27", ChannelMode.Fail, 0)]
    public async Task Account_verification_has_no_retry(string id, ChannelMode email, int sentCount)
    {
        var admin = await api.AdminAsync();
        var user = await api.CreateUserAsync(admin, "Participant");
        await DbAsync(db => db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerified, false)));
        var client = await api.SignInAsync(user.Email, ApiFixture.UserPassword);
        await using var _ = await api.ChannelsAsync(email: email);

        await client.PostAsync("/api/auth/resend-verification", null);

        Assert.True(Count(E, user.Email, "Confirm your email") == sentCount, $"{id}: email");
        Assert.True(await DbAsync(db => db.EmailRetries.CountAsync(r => r.SnapshotJson!.Contains(user.Email))) == 0, $"{id}: no retry");
    }

    // ---------------- R: background email retry ----------------

    [Fact] // R01
    public async Task Failed_pass_email_is_sent_by_the_retry_and_mobile_is_not_resent()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Both");
        await using (await api.ChannelsAsync(email: ChannelMode.Fail))
            await api.ConfirmAsync(admin, reg.EventId, reg.Id);

        await DispatchRetriesAsync(r => r.RegistrationId == reg.Id);

        Assert.Equal(1, Count(E, reg.Email, "Registration confirmed"));
        Assert.Equal(1, Count(V, reg.Phone, reg.Title)); // the first Viber pass only
        Assert.Equal(EmailRetryStatus.Sent, await DbAsync(db => db.EmailRetries.Where(r => r.RegistrationId == reg.Id).Select(r => r.Status).SingleAsync()));
    }

    [Fact] // R02
    public async Task An_email_failing_five_times_is_given_up()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Email");
        await using var _ = await api.ChannelsAsync(email: ChannelMode.Fail);
        await api.ConfirmAsync(admin, reg.EventId, reg.Id);

        for (var i = 0; i < 6; i++) await DispatchRetriesAsync(r => r.RegistrationId == reg.Id);

        var row = await DbAsync(db => db.EmailRetries.SingleAsync(r => r.RegistrationId == reg.Id));
        Assert.Equal((EmailRetryStatus.Failed, EmailRetryDispatcher.MaxAttempts), (row.Status, row.Attempts));
        Assert.Equal(0, Count(E, reg.Email, "Registration confirmed"));
    }

    [Fact] // R03
    public async Task A_queued_whole_email_is_sent_by_the_retry()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var address = ApiFixture.NewEmail();
        await using (await api.ChannelsAsync(email: ChannelMode.Fail))
            (await admin.PostAsJsonAsync($"/api/events/{ev.Id}/invitations", new { emails = new[] { address } })).EnsureSuccessStatusCode();

        await DispatchRetriesAsync(r => r.Kind == EmailRetryKinds.Message && r.SnapshotJson!.Contains(address));

        var eml = await File.ReadAllTextAsync(ApiFixture.Sent(E, address).Single());
        Assert.Contains("invite.ics", eml); // attachments survive the retry
    }

    [Fact] // R04
    public async Task A_meeting_cancel_is_sent_by_the_retry_after_the_registration_is_gone()
    {
        var admin = await api.AdminAsync();
        var reg = await GuestAsync(admin, "Email", format: "Online");
        await api.ConfirmAsync(admin, reg.EventId, reg.Id);
        await using (await api.ChannelsAsync(email: ChannelMode.Fail))
            await PassAsync(reg.Id, (p, ev, r) => p.SendMeetingCancelAsync(ev, r, MeetingCancelReason.RegistrationCancelled));
        await DbAsync(db => db.Registrations.Where(r => r.Id == reg.Id).ExecuteDeleteAsync());

        await DispatchRetriesAsync(r => r.RegistrationId == reg.Id);

        Assert.Equal(1, Count(E, reg.Email, "Registration cancelled"));
    }
}
