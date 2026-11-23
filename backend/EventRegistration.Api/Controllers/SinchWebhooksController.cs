using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using EventRegistration.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventRegistration.Api.Controllers;

/// <summary>
/// Receives Sinch Conversation API delivery reports (webhook trigger MESSAGE_DELIVERY) for Viber
/// messages. Each report updates its <see cref="ViberMessage"/>; a FAILED report on any part of a
/// tagged send (the receiver would have got the email had the send failed straight away) queues that
/// email once for the whole group through <see cref="EmailRetry"/>. Viber is never re-sent.
/// Anonymous, but every call must carry a valid Sinch HMAC signature over the raw body.
/// </summary>
[ApiController]
[Route("api/webhooks/sinch")]
[AllowAnonymous]
public class SinchWebhooksController(AppDbContext db, ViberSettings settings, ILogger<SinchWebhooksController> log)
    : ControllerBase
{
    public const int MaxBodyBytes = 256 * 1024;
    // A failure reported later than this sends no email: by then it is noise.
    public static readonly TimeSpan FallbackCutoff = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> Terminal = ["READ", "FAILED"];

    // POST /api/webhooks/sinch/conversation
    [HttpPost("conversation")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Conversation()
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer);
        var body = buffer.ToArray();

        if (Reject(body) is { } why)
        {
            log.LogWarning("Sinch webhook rejected: {Why}", why);
            return Unauthorized();
        }

        string? messageId, status, reasonCode, reasonDescription, eventTime;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("message_delivery_report", out var report))
            {
                var trigger = root.EnumerateObject().Select(p => p.Name)
                    .FirstOrDefault(n => n.EndsWith("_report") || n.EndsWith("_notification") || n == "message") ?? "unknown";
                log.LogWarning("Sinch webhook: could not read {Trigger} body ({Error})", trigger, "not a message delivery report");
                return Ok();
            }
            messageId = Str(report, "message_id");
            status = Str(report, "status")?.ToUpperInvariant();
            reasonCode = report.TryGetProperty("reason", out var reason) ? Str(reason, "code") : null;
            reasonDescription = reason.ValueKind == JsonValueKind.Object ? Str(reason, "description") : null;
            eventTime = Str(root, "event_time");
        }
        catch (JsonException ex)
        {
            log.LogWarning("Sinch webhook: could not read {Trigger} body ({Error})", "MESSAGE_DELIVERY", ex.Message);
            return Ok();
        }

        log.LogInformation("Sinch webhook received: {Trigger} {MessageId} {Status} {ReasonCode} (event time {EventTime}, body {Bytes} bytes)",
            "MESSAGE_DELIVERY", messageId, status, reasonCode, eventTime, body.Length);
        if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(status)) return Ok();

        try
        {
            await ApplyAsync(messageId, status, Cap(reasonCode, 100), reasonDescription);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Sinch webhook: could not save report for {MessageId}", messageId);
            return StatusCode(StatusCodes.Status500InternalServerError); // Sinch retries 5xx
        }
        return Ok();
    }

    private async Task ApplyAsync(string messageId, string status, string? reasonCode, string? reasonDescription)
    {
        var row = await db.ViberMessages.SingleOrDefaultAsync(m => m.MessageId == messageId);
        if (row is null)
        {
            log.LogInformation("Sinch webhook: message {MessageId} not ours; ignored", messageId);
            return;
        }
        // Reports may repeat or arrive out of order: a final status is never replaced.
        if (row.Status == status || Terminal.Contains(row.Status))
        {
            log.LogDebug("Sinch webhook: duplicate {Status} for {MessageId}; ignored", status, messageId);
            return;
        }

        var old = row.Status;
        row.Status = Cap(status, 30)!;
        row.ReasonCode = reasonCode ?? row.ReasonCode;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        log.LogInformation("Viber message {MessageId}: {OldStatus} → {NewStatus}", messageId, old, status);

        if (status != "FAILED") return;
        var noEmail = row.Kind is null ? "untagged"
            : row.EmailQueuedAt is not null ? "already queued"
            : DateTimeOffset.UtcNow - row.AcceptedAt > FallbackCutoff ? "older than cutoff"
            : null;
        if (noEmail is null)
        {
            // Claim the whole group in SQL: a second failed part, or a duplicate report handled at the
            // same time, finds nothing left to claim and queues no second email.
            var now = DateTimeOffset.UtcNow;
            var claimed = await db.ViberMessages.Where(m => m.GroupId == row.GroupId && m.EmailQueuedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.EmailQueuedAt, now));
            if (claimed == 0) noEmail = "already queued";
        }
        if (noEmail is not null)
        {
            log.LogInformation("Viber message {MessageId} failed ({ReasonCode}); no email because {Why}", messageId, reasonCode, noEmail);
            return;
        }

        EmailRetry? retry;
        if (row.RegistrationId is null)
        {
            // Mail plan, reminder or broadcast: what to email was saved with the group's first message.
            var saved = await db.ViberMessages
                .Where(m => m.GroupId == row.GroupId && (m.EmailSnapshotJson != null || m.EmailFile != null))
                .Select(m => new { m.EmailSnapshotJson, m.EmailFile })
                .FirstOrDefaultAsync();
            if (saved is null)
            {
                log.LogWarning("Viber message {MessageId} failed ({ReasonCode}); no email because none was saved", messageId, reasonCode);
                return;
            }
            // A broadcast saves one email per address: two numbers with the same address that both
            // fail later share it, so it is queued once.
            if (saved.EmailFile is not null && await db.EmailRetries.AnyAsync(r => r.EmailFile == saved.EmailFile))
            {
                log.LogInformation("Viber message {MessageId} failed ({ReasonCode}); no email because {Why}",
                    messageId, reasonCode, "the same email is already queued");
                return;
            }
            retry = new EmailRetry
            {
                Kind = row.Kind!,
                SnapshotJson = saved.EmailSnapshotJson,
                EmailFile = saved.EmailFile,
                NextAttemptAt = DateTimeOffset.UtcNow,
            };
            db.EmailRetries.Add(retry);
            await db.SaveChangesAsync();
        }
        else
        {
            // One pending retry per registration + kind, as for a failed send (MessageDispatcher).
            retry = await db.EmailRetries.FirstOrDefaultAsync(r => r.RegistrationId == row.RegistrationId && r.Kind == row.Kind &&
                (r.Status == EmailRetryStatus.Queued || r.Status == EmailRetryStatus.Sending));
            if (retry is null)
            {
                retry = new EmailRetry { RegistrationId = row.RegistrationId, Kind = row.Kind!, NextAttemptAt = DateTimeOffset.UtcNow };
                db.EmailRetries.Add(retry);
                await db.SaveChangesAsync();
            }
        }
        log.LogWarning("Viber message {MessageId} failed ({ReasonCode}: {ReasonDescription}); email fallback queued (retry {EmailRetryId}, registration {RegistrationId}, {Kind})",
            messageId, reasonCode, reasonDescription, retry.Id, row.RegistrationId, row.Kind);
    }

    // Why the call is refused, or null when its signature is valid. Signature = base64
    // HMAC-SHA256(secret, rawBody + "." + nonce + "." + timestamp), per Sinch's callback docs.
    private string? Reject(byte[] body)
    {
        var secret = settings.WebhookSecret?.Trim();
        if (string.IsNullOrEmpty(secret) || secret.Equals("required", StringComparison.OrdinalIgnoreCase))
            return "secret not configured";
        var h = Request.Headers;
        string signature = h["x-sinch-webhook-signature"].ToString(), nonce = h["x-sinch-webhook-signature-nonce"].ToString(),
            timestamp = h["x-sinch-webhook-signature-timestamp"].ToString();
        if (signature.Length == 0 || nonce.Length == 0 || timestamp.Length == 0) return "missing headers";
        if (!long.TryParse(timestamp, out var seconds)) return "missing headers";
        var skew = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds);
        if (skew > MaxSkew.TotalSeconds) return $"timestamp skew {skew}s";
        return Valid(body, nonce, timestamp, signature, secret) ? null : "bad signature";
    }

    public static string Sign(byte[] body, string nonce, string timestamp, string secret) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            (byte[])[.. body, .. Encoding.UTF8.GetBytes($".{nonce}.{timestamp}")]));

    private static bool Valid(byte[] body, string nonce, string timestamp, string signature, string secret) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(body, nonce, timestamp, secret)),
            Encoding.UTF8.GetBytes(signature));

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Cap(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
