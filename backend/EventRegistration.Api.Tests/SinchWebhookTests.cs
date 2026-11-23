using System.Net;
using System.Text;
using EventRegistration.Api.Controllers;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventRegistration.Api.Tests;

// Sinch delivery reports for Viber: a later FAILED on a tagged message queues the email once.
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

    private async Task<(string Id, int RegistrationId)> SeedAsync(bool tagged = true, DateTimeOffset? acceptedAt = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var regId = Random.Shared.Next(1_000_000, int.MaxValue);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ViberMessages.Add(new ViberMessage
        {
            MessageId = id, Phone = "+639171234567",
            RegistrationId = tagged ? regId : null, Kind = tagged ? EmailRetryKinds.Pass : null,
            AcceptedAt = acceptedAt ?? DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (id, regId);
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

    [Theory]
    [InlineData("+639171234567", "+63••••••4567")]
    [InlineData("12345", "•••••")]
    public void Phones_in_logs_are_masked(string phone, string expected) =>
        Assert.Equal(expected, PhoneNumber.Mask(phone));
}
