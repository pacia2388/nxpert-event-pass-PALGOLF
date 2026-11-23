using System.Net;
using System.Text;
using EventRegistration.Api.Controllers;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventRegistration.Api.Tests;

// Sinch delivery reports for Viber: a later FAILED on any part of a tagged send queues the email
// once for the whole group.
[Collection("api")]
public class SinchWebhookTests(ApiFixture api)
{
    private const string Secret = "test-webhook-secret";
    private const string Url = "/api/webhooks/sinch/conversation";

    private async Task<HttpResponseMessage> PostAsync(string messageId, string status, string? secret = Secret,
        DateTimeOffset? at = null, string? signature = null)
    {
        api.Services.GetRequiredService<ViberSettings>().WebhookSecret = Secret;
        var body = Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                app_id = "app",
                event_time = "2026-11-22T00:00:00Z",
                message_delivery_report = new
                {
                    message_id = messageId,
                    status,
                    reason = new { code = "RECIPIENT_NOT_REACHABLE", description = "no Viber" },
                },
            }));
        var nonce = Guid.NewGuid().ToString("N");
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var req = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new ByteArrayContent(body) };
        req.Content.Headers.ContentType = new("application/json");
        if (secret is not null)
        {
            req.Headers.Add("x-sinch-webhook-signature", signature ?? SinchWebhooksController.Sign(body, nonce, timestamp, secret));
            req.Headers.Add("x-sinch-webhook-signature-nonce", nonce);
            req.Headers.Add("x-sinch-webhook-signature-timestamp", timestamp);
            req.Headers.Add("x-sinch-webhook-signature-algorithm", "HmacSHA256");
        }
        return await api.Anonymous().SendAsync(req);
    }

    private async Task<(string Id, int RegistrationId)> SeedAsync(bool tagged = true, DateTimeOffset? acceptedAt = null,
        Guid? group = null, int? regId = null)
    {
        var id = Guid.NewGuid().ToString("N");
        regId ??= Random.Shared.Next(1_000_000, int.MaxValue);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ViberMessages.Add(new ViberMessage
        {
            MessageId = id, Phone = "+639171234567",
            RegistrationId = tagged ? regId : null, Kind = tagged ? EmailRetryKinds.Pass : null,
            AcceptedAt = acceptedAt ?? DateTimeOffset.UtcNow, GroupId = group ?? Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return (id, regId.Value);
    }

    private async Task<(ViberMessage? Row, int Retries)> StateAsync(string id, int regId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.ViberMessages.AsNoTracking().SingleOrDefaultAsync(m => m.MessageId == id),
            await db.EmailRetries.CountAsync(r => r.RegistrationId == regId && r.Kind == EmailRetryKinds.Pass));
    }

    [Fact]
    public async Task Missing_or_bad_signature_is_refused_and_changes_nothing()
    {
        var (id, reg) = await SeedAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(id, "FAILED", secret: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(id, "FAILED", secret: "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(id, "FAILED", at: DateTimeOffset.UtcNow.AddMinutes(-10))).StatusCode);

        var (row, retries) = await StateAsync(id, reg);
        Assert.Equal(ViberMessage.Accepted, row!.Status);
        Assert.Equal(0, retries);
    }

    [Fact]
    public async Task No_secret_configured_refuses_every_report()
    {
        var settings = api.Services.GetRequiredService<ViberSettings>();
        settings.WebhookSecret = "required";
        try
        {
            var res = await api.Anonymous().PostAsync(Url, new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
        finally { settings.WebhookSecret = Secret; }
    }

    [Fact]
    public async Task Failed_tagged_message_queues_one_email_even_when_reported_twice()
    {
        var (id, reg) = await SeedAsync();

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(id, "FAILED")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(id, "FAILED")).StatusCode);

        var (row, retries) = await StateAsync(id, reg);
        Assert.Equal("FAILED", row!.Status);
        Assert.Equal("RECIPIENT_NOT_REACHABLE", row.ReasonCode);
        Assert.NotNull(row.EmailQueuedAt);
        Assert.Equal(1, retries);
    }

    [Fact]
    public async Task Delivered_and_read_update_the_status_without_email()
    {
        var (id, reg) = await SeedAsync();

        await PostAsync(id, "DELIVERED");
        Assert.Equal("DELIVERED", (await StateAsync(id, reg)).Row!.Status);
        await PostAsync(id, "READ");

        var (row, retries) = await StateAsync(id, reg);
        Assert.Equal("READ", row!.Status);
        Assert.Null(row.EmailQueuedAt);
        Assert.Equal(0, retries);
    }

    [Fact]
    public async Task Failed_untagged_message_sends_no_email()
    {
        var (id, reg) = await SeedAsync(tagged: false);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(id, "FAILED")).StatusCode);

        var (row, retries) = await StateAsync(id, reg);
        Assert.Equal("FAILED", row!.Status);
        Assert.Null(row.EmailQueuedAt);
        Assert.Equal(0, retries);
    }

    [Fact]
    public async Task Unknown_message_is_acknowledged_and_ignored()
    {
        var id = Guid.NewGuid().ToString("N");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(id, "FAILED")).StatusCode);

        Assert.Null((await StateAsync(id, 0)).Row);
    }

    [Fact]
    public async Task Failure_reported_after_the_cutoff_sends_no_email()
    {
        var (id, reg) = await SeedAsync(acceptedAt: DateTimeOffset.UtcNow - SinchWebhooksController.FallbackCutoff - TimeSpan.FromMinutes(1));

        await PostAsync(id, "FAILED");

        var (row, retries) = await StateAsync(id, reg);
        Assert.Equal("FAILED", row!.Status);
        Assert.Null(row.EmailQueuedAt);
        Assert.Equal(0, retries);
    }

    [Fact]
    public async Task Two_failed_parts_of_one_group_queue_one_email()
    {
        // The pass text and its QR image: both fail, one email.
        var group = Guid.NewGuid();
        var (text, reg) = await SeedAsync(group: group);
        var (image, _) = await SeedAsync(group: group, regId: reg);

        await PostAsync(text, "FAILED");
        await PostAsync(image, "FAILED");

        Assert.Equal(1, (await StateAsync(image, reg)).Retries);
        Assert.NotNull((await StateAsync(image, reg)).Row!.EmailQueuedAt);
    }

    [Fact]
    public async Task Failed_broadcast_message_queues_its_saved_email_file()
    {
        // No registration: the .eml file saved with the group's first part is queued by name.
        var group = Guid.NewGuid();
        var file = $"{Guid.NewGuid():N}.eml";
        var id = Guid.NewGuid().ToString("N");
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ViberMessages.Add(new ViberMessage
            {
                MessageId = id, Phone = "+639171234567", Kind = EmailRetryKinds.Message, GroupId = group,
                EmailFile = file,
            });
            await db.SaveChangesAsync();
        }

        await PostAsync(id, "FAILED");
        await PostAsync(id, "FAILED");

        using var check = api.Services.CreateScope();
        var rows = await check.ServiceProvider.GetRequiredService<AppDbContext>().EmailRetries
            .Where(r => r.EmailFile == file).ToListAsync();
        Assert.Single(rows);
        Assert.Null(rows[0].RegistrationId);
        Assert.Null(rows[0].SnapshotJson);
    }

    [Fact]
    public async Task Two_numbers_sharing_a_saved_broadcast_email_queue_it_once()
    {
        // Two numbers with the same address: two Viber groups, one saved email.
        var file = $"{Guid.NewGuid():N}.eml";
        var ids = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var id in ids)
                db.ViberMessages.Add(new ViberMessage
                {
                    MessageId = id, Phone = "+639171234567", Kind = EmailRetryKinds.Message, GroupId = Guid.NewGuid(),
                    EmailFile = file,
                });
            await db.SaveChangesAsync();
        }

        foreach (var id in ids)
            await PostAsync(id, "FAILED");

        using var check = api.Services.CreateScope();
        var retries = await check.ServiceProvider.GetRequiredService<AppDbContext>().EmailRetries
            .CountAsync(r => r.EmailFile == file);
        Assert.Equal(1, retries);
    }

    [Theory]
    [InlineData("+639171234567", "+63••••••4567")]
    [InlineData("12345", "•••••")]
    public void Phones_in_logs_are_masked(string phone, string expected) =>
        Assert.Equal(expected, PhoneNumber.Mask(phone));
}
