using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Hubs;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventRegistration.Api.Tests;

// Attachments kept in the .eml, 100-recipient batches with delivered-address tracking, *@domain,
// exclusions and send-immediately.
[Collection("api")]
public class MailDeliveryPhase3Tests(ApiFixture api)
{
    private static string Subject() => $"Phase3 {Guid.NewGuid():N}"[..20];

    private static async Task<int> ComposeScheduledAsync(HttpClient admin, int eventId, object payload,
        params (string Name, byte[] Content, string Type)[] files)
    {
        var res = await admin.PostAsync($"/api/events/{eventId}/invitations/compose",
            ApiFixture.MailForm(payload, files));
        res.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("Scheduled", json.RootElement.GetProperty("status").GetString());
        return json.RootElement.GetProperty("id").GetInt32();
    }

    private async Task MakeDueAsync(int invitationId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.InvitationEmails.SingleAsync(m => m.Id == invitationId);
        item.ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        item.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    private async Task DispatchInvitationAsync(int id)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InvitationEmailDispatcher>().DispatchAsync(id);
    }

    private async Task<InvitationEmail> InvitationRowAsync(int id)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.InvitationEmails.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private static async Task<string[]> SentFiles(string recipient)
    {
        await Task.CompletedTask;
        return ApiFixture.Sent("email", recipient);
    }

    // Batch file names are truncated before the subject, so count the new messages instead.
    private static int SentCount() =>
        Directory.Exists(ApiFixture.SentDir("email"))
            ? Directory.GetFiles(ApiFixture.SentDir("email"), "*.eml").Length
            : 0;

    [Fact]
    public async Task Attachments_survive_edit_and_a_failed_retry()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var address = ApiFixture.NewEmail();
        var subject = Subject();
        var later = DateTimeOffset.UtcNow.AddHours(1);
        var id = await ComposeScheduledAsync(admin, ev.Id,
            new { to = new[] { address }, subject, bodyHtml = "<p>Hello</p>", scheduledAt = later },
            ("agenda.txt", Encoding.UTF8.GetBytes("Agenda A"), "text/plain"));

        var opened = await admin.GetFromJsonAsync<InvitationBody>($"/api/events/{ev.Id}/invitations/emails/{id}");
        Assert.Equal("agenda.txt", Assert.Single(opened!.Attachments!).Name);

        // Edit: keep the first attachment and add one more.
        var update = await admin.PutAsync($"/api/events/{ev.Id}/invitations/emails/{id}", ApiFixture.MailForm(new
        {
            to = new[] { address },
            subject,
            bodyHtml = "<p>Hello again</p>",
            scheduledAt = later,
            version = opened.Version,
            keepAttachments = new[] { "agenda.txt" },
        }, ("notes.txt", Encoding.UTF8.GetBytes("Notes B"), "text/plain")));
        update.EnsureSuccessStatusCode();
        var edited = await admin.GetFromJsonAsync<InvitationBody>($"/api/events/{ev.Id}/invitations/emails/{id}");
        Assert.Equal(new[] { "agenda.txt", "notes.txt" },
            edited!.Attachments!.Select(a => a.Name).OrderBy(n => n).ToArray());

        // A failed SMTP attempt keeps the .eml (with both files); the retry sends the same email.
        await MakeDueAsync(id);
        await using (await api.ChannelsAsync(email: ChannelMode.Fail))
            await DispatchInvitationAsync(id);
        var queued = await InvitationRowAsync(id);
        Assert.Equal(InvitationEmailStatus.Queued, queued.Status);
        Assert.False(string.IsNullOrEmpty(queued.EmailFile));

        await MakeDueAsync(id); // a failed attempt waits five minutes
        await DispatchInvitationAsync(id);
        var sent = await InvitationRowAsync(id);
        Assert.Equal(InvitationEmailStatus.Sent, sent.Status);
        var file = Assert.Single(await SentFiles(address));
        var eml = await File.ReadAllTextAsync(file);
        Assert.Contains("agenda.txt", eml);
        Assert.Contains("notes.txt", eml);
    }

    [Fact]
    public async Task A_viber_only_plan_refuses_attachments()
    {
        var admin = await api.AdminAsync();
        var res = await admin.PostAsync("/api/mail-plans", ApiFixture.MailForm(new
        {
            kind = "Announcement",
            channel = "Viber",
            draft = false,
            groupIds = Array.Empty<string>(),
            emails = new[] { ApiFixture.NewEmail() },
            cc = Array.Empty<string>(),
            sends = new[] { new { id = (int?)null, sendAt = DateTimeOffset.UtcNow.AddHours(1), subject = Subject(), bodyHtml = "<p>Hi</p>" } },
            sendImmediately = false,
        }, ("file.pdf", new byte[] { 1, 2, 3 }, "application/pdf")));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Viber-only", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_ten_megabyte_attachment_is_accepted()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var file = new byte[10 * 1024 * 1024];

        var res = await admin.PostAsync($"/api/events/{ev.Id}/invitations/compose", ApiFixture.MailForm(new
        {
            to = new[] { ApiFixture.NewEmail() },
            subject = Subject(),
            bodyHtml = "<p>Ten MB</p>",
            scheduledAt = DateTimeOffset.UtcNow.AddHours(1),
        }, ("ten.bin", file, "application/octet-stream")));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task An_email_over_twenty_five_megabytes_is_refused_with_a_plain_message()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var big = new byte[26 * 1024 * 1024];

        var res = await admin.PostAsync($"/api/events/{ev.Id}/invitations/compose", ApiFixture.MailForm(new
        {
            to = new[] { ApiFixture.NewEmail() },
            subject = Subject(),
            bodyHtml = "<p>Big</p>",
        }, ("big.bin", big, "application/octet-stream")));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("25 MB", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Two_hundred_thirty_recipients_go_out_in_three_batches_and_a_retry_skips_reached_ones()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var subject = Subject();
        var addresses = Enumerable.Range(0, 230).Select(_ => ApiFixture.NewEmail()).ToArray();
        var id = await ComposeScheduledAsync(admin, ev.Id, new
        {
            to = addresses,
            subject,
            bodyHtml = "<p>Hello all</p>",
            scheduledAt = DateTimeOffset.UtcNow.AddHours(1),
        });
        await MakeDueAsync(id);
        var before = SentCount();
        await DispatchInvitationAsync(id);

        Assert.Equal(3, SentCount() - before);
        Assert.Equal(InvitationEmailStatus.Sent, (await InvitationRowAsync(id)).Status);

        // A later retry must not send the 100 addresses its first batch already reached: 130 left,
        // in two more SMTP messages.
        var firstHundred = addresses.Take(100).ToArray();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.InvitationEmails.SingleAsync(m => m.Id == id);
            item.DeliveredRecipientsJson = JsonSerializer.Serialize(firstHundred);
            item.Status = InvitationEmailStatus.Queued;
            item.Attempts = 0;
            item.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var beforeRetry = SentCount();
        await DispatchInvitationAsync(id);

        Assert.Equal(2, SentCount() - beforeRetry);
    }

    [Fact]
    public async Task A_domain_pattern_expands_only_active_users_of_that_domain()
    {
        var admin = await api.AdminAsync();
        var domain = $"phase3-{Guid.NewGuid():N}.example";
        async Task<(int Id, string Email)> CreateUser(string name)
        {
            var email = $"{name}@{domain}";
            var res = await admin.PostAsJsonAsync("/api/users", new
            {
                firstName = name,
                lastName = "Domain",
                email,
                password = ApiFixture.UserPassword,
                role = "Participant",
                birthdate = "1990-05-01",
                canConfirmRegistrations = false,
            });
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<UserBody>();
            return (body!.Id, email);
        }
        var active = await CreateUser("active");
        var inactive = await CreateUser("inactive");
        (await admin.PostAsync($"/api/users/{inactive.Id}/deactivate", null)).EnsureSuccessStatusCode();

        var expanded = await admin.GetFromJsonAsync<List<string>>(
            $"/api/mail-plans/expand?pattern=%2A%40{domain}");

        Assert.Contains(active.Email, expanded!);
        Assert.DoesNotContain(inactive.Email, expanded!);
    }

    [Fact]
    public async Task Excluded_receivers_are_removed_and_send_immediately_is_due_on_the_next_tick()
    {
        var admin = await api.AdminAsync();
        var keep = ApiFixture.NewEmail();
        var drop = ApiFixture.NewEmail();
        var subject = Subject();
        var res = await admin.PostAsync("/api/mail-plans", ApiFixture.MailForm(new
        {
            kind = "Announcement",
            channel = "Email",
            draft = false,
            groupIds = Array.Empty<string>(),
            emails = new[] { keep, drop },
            excludedEmails = new[] { drop },
            cc = Array.Empty<string>(),
            sends = new[] { new { id = (int?)null, sendAt = DateTimeOffset.UtcNow.AddDays(1), subject, bodyHtml = "<p>Hi</p>" } },
            sendImmediately = true,
        }));
        res.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var planId = json.RootElement.GetProperty("id").GetInt32();
        var sendId = json.RootElement.GetProperty("sends")[0].GetProperty("id").GetInt32();
        Assert.True(json.RootElement.GetProperty("sendImmediately").GetBoolean());

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var send = await db.MailPlanSends.SingleAsync(s => s.Id == sendId);
            Assert.True(send.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1),
                "send-immediately must be due on the next scheduler tick");
            Assert.Equal(0, await db.MailPlanDeliveries.CountAsync(d => d.SendId == sendId)); // not frozen yet
        }

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().DispatchAsync(sendId);

        using (var check = api.Services.CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            var deliveries = await db.MailPlanDeliveries.Where(d => d.SendId == sendId).ToListAsync();
            Assert.Equal(keep, Assert.Single(deliveries).Email);
            Assert.Equal(MailSendStatus.Sent, (await db.MailPlanSends.SingleAsync(s => s.Id == sendId)).Status);
        }
        Assert.Single(ApiFixture.Sent("email", subject));
    }

    [Fact]
    public async Task The_progress_notice_reaches_the_organizer_or_the_admins()
    {
        var hub = new CapturingHub();
        var service = new NotificationService(hub, null!, null!, NullLogger<NotificationService>.Instance);

        await service.MailSendProgressAsync(7,
            new MailSendProgressNotice("invitation", 3, 100, 230, 0, false));
        await service.MailSendProgressAsync(null,
            new MailSendProgressNotice("plan", 4, 100, 230, 0, false));

        Assert.Equal("user-7", hub.Captured[0].Group);
        Assert.Equal("admins", hub.Captured[1].Group);
        Assert.All(hub.Captured, g => Assert.Equal("mailSendProgress", g.Method));
    }


    [Fact]
    public async Task A_plan_with_a_blank_excluded_list_still_opens_and_sends()
    {
        var admin = await api.AdminAsync();
        var address = ApiFixture.NewEmail();
        int planId, sendId;
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adminId = await db.Users.Where(u => u.Email == ApiFixture.AdminEmail).Select(u => u.Id).SingleAsync();
            var plan = new MailPlan
            {
                Channel = MailPlanChannels.Email,
                CreatedByUserId = adminId,
                EmailsJson = JsonSerializer.Serialize(new[] { address }),
                ExcludedEmailsJson = "", // the default written by the first design
            };
            plan.Sends.Add(new MailPlanSend
            {
                Subject = $"Blank {Guid.NewGuid():N}"[..18],
                BodyHtml = "<p>Hello</p>",
                Status = MailSendStatus.Scheduled,
                SendAt = DateTimeOffset.UtcNow,
                NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            });
            db.MailPlans.Add(plan);
            await db.SaveChangesAsync();
            planId = plan.Id;
            sendId = plan.Sends.Single().Id;
        }

        var opened = await admin.GetAsync($"/api/mail-plans/{planId}");
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().DispatchAsync(sendId);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(MailSendStatus.Sent, (await db.MailPlanSends.SingleAsync(s => s.Id == sendId)).Status);
        }
    }

    [Fact]
    public async Task Saving_a_partly_sent_invitation_again_never_resends_to_reached_addresses()
    {
        var admin = await api.AdminAsync();
        var ev = await api.CreateEventAsync(admin);
        var subject = Subject();
        var reached = ApiFixture.NewEmail();
        var rest = ApiFixture.NewEmail();
        var later = DateTimeOffset.UtcNow.AddHours(1);
        var id = await ComposeScheduledAsync(admin, ev.Id,
            new { to = new[] { reached, rest }, subject, bodyHtml = "<p>Hi</p>", scheduledAt = later });
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.InvitationEmails.SingleAsync(m => m.Id == id);
            item.DeliveredRecipientsJson = JsonSerializer.Serialize(new[] { reached });
            item.Status = InvitationEmailStatus.Failed;
            await db.SaveChangesAsync();
        }
        var opened = await admin.GetFromJsonAsync<InvitationBody>($"/api/events/{ev.Id}/invitations/emails/{id}");

        // The retry path: open the failed email and save it unchanged, sending now.
        (await admin.PutAsync($"/api/events/{ev.Id}/invitations/emails/{id}", ApiFixture.MailForm(new
        {
            to = new[] { reached, rest },
            subject,
            bodyHtml = "<p>Hi</p>",
            scheduledAt = (DateTimeOffset?)null,
            version = opened!.Version,
        }))).EnsureSuccessStatusCode();

        Assert.Empty(await SentFiles(reached));
        Assert.Single(await SentFiles(rest));
    }

    [Fact]
    public void No_smtp_message_carries_more_than_a_hundred_addresses()
    {
        var to = new[] { "one@example.com" };
        var cc = Enumerable.Range(0, 300).Select(i => $"cc{i}@example.com").ToArray();

        var batches = MailRecipients.Batches(to, cc, 100);

        Assert.Equal(4, batches.Count);
        Assert.All(batches, b => Assert.True(b.Main.Count + b.Cc.Count <= 100));
        Assert.Equal(301, batches.Sum(b => b.Main.Count + b.Cc.Count));
        // A plan: 150 BCC + 60 CC still stays within 100 per message.
        var plan = MailRecipients.Batches(Enumerable.Range(0, 150).ToArray(), Enumerable.Range(0, 60).ToArray(), 100);
        Assert.All(plan, b => Assert.True(b.Main.Count + b.Cc.Count <= 100));
        Assert.Equal(210, plan.Sum(b => b.Main.Count + b.Cc.Count));
    }

    [Fact]
    public async Task Plan_attachments_are_one_set_that_can_be_kept_removed_and_added()
    {
        var admin = await api.AdminAsync();
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var created = await admin.PostAsync("/api/mail-plans", ApiFixture.MailForm(new
        {
            kind = "Announcement",
            channel = "Email",
            draft = false,
            groupIds = Array.Empty<string>(),
            emails = new[] { ApiFixture.NewEmail() },
            cc = Array.Empty<string>(),
            sends = new[]
            {
                new { id = (int?)null, sendAt = start, subject = Subject(), bodyHtml = "<p>Day 1</p>" },
                new { id = (int?)null, sendAt = start.AddDays(1), subject = Subject(), bodyHtml = "<p>Day 2</p>" },
            },
        }, ("a.txt", Encoding.UTF8.GetBytes("A"), "text/plain"), ("b.txt", Encoding.UTF8.GetBytes("B"), "text/plain")));
        created.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var planId = json.RootElement.GetProperty("id").GetInt32();
        var plan = await admin.GetFromJsonAsync<PlanBody>($"/api/mail-plans/{planId}");

        (await admin.PutAsync($"/api/mail-plans/{planId}", ApiFixture.MailForm(new
        {
            kind = "Announcement",
            channel = "Email",
            draft = false,
            groupIds = Array.Empty<string>(),
            emails = plan!.Emails,
            cc = Array.Empty<string>(),
            sends = plan.Sends.Select(s => new { id = (int?)s.Id, sendAt = s.SendAt, subject = s.Subject, bodyHtml = s.BodyHtml }),
            version = plan.Version,
            keepAttachments = new[] { "a.txt" },
        }, ("c.txt", Encoding.UTF8.GetBytes("C"), "text/plain")))).EnsureSuccessStatusCode();

        var edited = await admin.GetFromJsonAsync<PlanBody>($"/api/mail-plans/{planId}");
        Assert.All(edited!.Sends, s => Assert.Equal(new[] { "a.txt", "c.txt" },
            s.Attachments.Select(a => a.Name).OrderBy(n => n).ToArray()));

        // Switching to Viber-only while files remain is refused.
        var viber = await admin.PutAsync($"/api/mail-plans/{planId}", ApiFixture.MailForm(new
        {
            kind = "Announcement",
            channel = "Viber",
            draft = false,
            groupIds = Array.Empty<string>(),
            emails = edited.Emails,
            cc = Array.Empty<string>(),
            sends = edited.Sends.Select(s => new { id = (int?)s.Id, sendAt = s.SendAt, subject = s.Subject, bodyHtml = s.BodyHtml }),
            version = edited.Version,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, viber.StatusCode);
    }

    [Fact]
    public async Task A_delivered_days_email_file_is_kept_for_a_day_then_cleaned_up()
    {
        var admin = await api.AdminAsync();
        var res = await admin.PostAsync("/api/mail-plans", ApiFixture.MailForm(new
        {
            kind = "Announcement",
            channel = "Email",
            draft = false,
            groupIds = Array.Empty<string>(),
            emails = new[] { ApiFixture.NewEmail() },
            cc = Array.Empty<string>(),
            sends = new[] { new { id = (int?)null, sendAt = DateTimeOffset.UtcNow.AddDays(1), subject = Subject(), bodyHtml = "<p>Hi</p>" } },
            sendImmediately = true,
        }, ("keep.txt", Encoding.UTF8.GetBytes("K"), "text/plain")));
        res.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var sendId = json.RootElement.GetProperty("sends")[0].GetProperty("id").GetInt32();
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MailPlanDispatcher>().DispatchAsync(sendId);

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var outgoing = scope.ServiceProvider.GetRequiredKeyedService<EmailFileStore>("outgoing");
            var send = await db.MailPlanSends.SingleAsync(s => s.Id == sendId);
            Assert.Equal(MailSendStatus.Sent, send.Status);
            var file = Assert.IsType<string>(send.EmailFile); // kept for reminders / late fallback
            Assert.NotNull(await outgoing.LoadAsync(file));

            send.SentAt = DateTimeOffset.UtcNow.AddHours(-25);
            await db.SaveChangesAsync();
            await MailPlanDispatcher.CleanUpSentEmailsAsync(db, outgoing);

            Assert.Null((await db.MailPlanSends.AsNoTracking().SingleAsync(s => s.Id == sendId)).EmailFile);
            Assert.Null(await outgoing.LoadAsync(file));
        }
    }

    private sealed record PlanBody(int Id, string Version, List<string> Emails, List<PlanSendBody> Sends);
    private sealed record PlanSendBody(int Id, DateTimeOffset SendAt, string Subject, string BodyHtml,
        List<MailAttachmentDto> Attachments);

    private sealed record InvitationBody(int Id, string Version, List<MailAttachmentDto>? Attachments);

    // Minimal IHubContext capture: which group and method each push used.
    private sealed class CapturingHub : IHubContext<NotificationHub>
    {
        public List<(string Group, string Method, object? Arg)> Captured { get; } = [];
        public IHubClients Clients { get; }
        public IGroupManager Groups => throw new NotSupportedException();
        public CapturingHub() => Clients = new CapturingClients(Captured);
    }

    private sealed class CapturingClients(List<(string, string, object?)> groups) : IHubClients
    {
        public IClientProxy All => new CapturingProxy("all", groups);
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
        public IClientProxy Client(string connectionId) => new CapturingProxy("client", groups);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;
        public IClientProxy Group(string groupName) => new CapturingProxy(groupName, groups);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new CapturingProxy(groupNames.FirstOrDefault() ?? "", groups);
        public IClientProxy User(string userId) => new CapturingProxy("user", groups);
        public IClientProxy Users(IReadOnlyList<string> userIds) => new CapturingProxy("user", groups);
    }

    private sealed class CapturingProxy(string group, List<(string, string, object?)> groups) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            groups.Add((group, method, args.FirstOrDefault()));
            return Task.CompletedTask;
        }
    }
}
