using EventRegistration.Api.Data;
using EventRegistration.Api.Models;
using Sinch;
using Sinch.Conversation;
using Sinch.Conversation.Common;
using Sinch.Conversation.Messages.Message;
using Sinch.Conversation.Messages.Send;

namespace EventRegistration.Api.Services;

public class ViberSettings
{
    // When disabled (the default), messages are written to the on-disk outbox instead of being
    // sent — mirroring EmailService so the demo works without a Sinch account.
    public bool Enabled { get; set; } = false;
    // Sinch project id (Sinch dashboard → Settings → Projects).
    public string ProjectId { get; set; } = "";
    // Sinch access key id and secret (Settings → Access keys). Keep the secret in local config only.
    public string KeyId { get; set; } = "";
    public string KeySecret { get; set; } = "";
    // Conversation API app that has the Viber Business Messages channel set up (it holds the sender).
    public string AppId { get; set; } = "";
    // Region the Conversation app was created in: "us", "eu" or "br".
    public string Region { get; set; } = "";
    // Public HTTPS base URL where THIS API is reachable (e.g. "https://nxpert-eventpass.n-pax.com:8000").
    // Used to build the QR image URL that Sinch fetches, so it must be publicly reachable.
    // When empty, a text-only message is sent (no inline QR).
    public string PublicApiBaseUrl { get; set; } = "";
    // When a Viber message fails, send the email instead. Turn off when the provider already
    // falls back on its side, so the registrant does not get the message twice.
    public bool FallbackToEmail { get; set; } = true;
    // Secret of the Sinch Conversation webhook (trigger MESSAGE_DELIVERY) that reports later
    // delivery failures. Local / production config only. Empty or "required" = reports refused.
    public string WebhookSecret { get; set; } = "";
    // Tests only (ignored in Production): write messages to this folder instead of sending them.
    public string TestSendDir { get; set; } = "";
    // Tests only, with TestSendDir: every send fails, to exercise fallback.
    public bool TestFail { get; set; }
}

/// <summary>
/// Delivers entry passes / notifications over Viber Business Messages through the Sinch
/// Conversation API (Sinch .NET SDK). Sinch sends straight to the recipient's phone number. When
/// Sinch isn't configured, messages (and the QR PNG) are written to the on-disk outbox so the demo
/// still works offline; a send failure also falls back to the outbox.
/// A rejected request counts as failed straight away. Sinch reports later delivery failures by
/// webhook (SinchWebhooksController): every accepted message is saved as a <see cref="ViberMessage"/>,
/// and a failure of a tagged one (<see cref="MobileTag"/>) queues the email instead.
/// </summary>
public class SinchViberService : IMobileSender
{
    private readonly ViberSettings _s;
    private readonly ILogger<SinchViberService> _log;
    private readonly IWebHostEnvironment _env;
    private readonly string _outboxDir;
    // One client per process: it caches its OAuth token and HTTP connections.
    private readonly Lazy<ISinchClient> _client;
    private readonly IServiceScopeFactory? _scopes;

    // client: tests only, a stand-in for Sinch; DI leaves it null. scopes: for saving accepted
    // messages (singleton, so a scope per save); null in unit tests.
    public SinchViberService(ViberSettings s, ILogger<SinchViberService> log, IWebHostEnvironment env,
        ISinchClient? client = null, IServiceScopeFactory? scopes = null)
    {
        _s = s;
        _log = log;
        _env = env;
        _scopes = scopes;
        _outboxDir = Path.Combine(env.ContentRootPath, "outbox");
        _client = new Lazy<ISinchClient>(() => client ?? new SinchClient(_s.ProjectId, _s.KeyId, _s.KeySecret,
            o => o.ConversationRegion = new ConversationRegion(_s.Region.Trim().ToLowerInvariant())));
    }

    public bool FallbackToEmail => _s.FallbackToEmail;

    public bool Configured =>
        _s.Enabled && IsSet(_s.ProjectId) && IsSet(_s.KeyId) && IsSet(_s.KeySecret) && IsSet(_s.AppId) && IsSet(_s.Region);

    // "required" is the placeholder appsettings.json ships with; local config overrides it.
    private static bool IsSet(string v) =>
        !string.IsNullOrWhiteSpace(v) && !v.Trim().Equals("required", StringComparison.OrdinalIgnoreCase);

    // Read per send: tests switch the folder on the shared settings at run time.
    private string? TestDir => TestSendFolder.Resolve(_s.TestSendDir, _env);

    /// <summary>
    /// Sends the entry-pass message to a phone number over Viber. When a public API base URL is
    /// configured, the QR pass follows as an image message pointing at the public pass-QR endpoint;
    /// otherwise only the text is sent. Falls back to the outbox.
    /// </summary>
    public async Task<DeliveryResult> SendPassAsync(string phone, string title, DateTime startsAt, DateTime endsAt, string venue,
        string attendeeName, string passToken, byte[] qrPng, string? editUrl = null, MobileTag? tag = null)
    {
        var text =
            $"Hi {attendeeName}, your registration for \"{title}\" is confirmed.\n" +
            $"{ScheduleText.Text(startsAt, endsAt)}\n" +
            $"Where: {venue}\n" +
            "Present the QR entry pass at the entrance." +
            (string.IsNullOrWhiteSpace(editUrl) ? "" : $"\n\nNeed to update your details? {editUrl}");

        var to = NormalizePhone(phone);

        if (TestDir is { } dir) return await TestSendFolder.WriteAsync(dir, _s.TestFail, to, title, text, qrPng);
        if (!Configured) return await OffAsync(to, title, text, qrPng);

        try
        {
            // Text first, image second: the details still arrive even if the image is rejected.
            await SendAsync(to, new AppMessage(new TextMessage(text)), tag);
            var mediaUrl = PublicUrl($"api/passes/{passToken}/qr.png");
            if (mediaUrl is not null)
                await SendAsync(to, new AppMessage(new MediaMessage { Url = mediaUrl }));

            _log.LogInformation("Sent Viber pass to {Phone} ('{Title}') via Sinch ({Kind}).",
                to, title, mediaUrl is null ? "text only" : "text + QR image");
            return DeliveryResult.Sent;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send Viber message to {Phone}. Falling back to outbox.", to);
            await WriteToOutboxAsync(to, title, text, qrPng);
            return DeliveryResult.Failed;
        }
    }

    /// <summary>
    /// Sends a plain text notification (no QR/image) to a phone number over Viber.
    /// Falls back to the on-disk outbox when Sinch isn't configured or the send fails.
    /// <paramref name="subject"/> is only used to label the outbox entry.
    /// </summary>
    public async Task<DeliveryResult> SendTextAsync(string phone, string subject, string text, MobileTag? tag = null)
    {
        var to = NormalizePhone(phone);

        if (TestDir is { } dir) return await TestSendFolder.WriteAsync(dir, _s.TestFail, to, subject, text);
        if (!Configured) return await OffAsync(to, subject, text, Array.Empty<byte>());

        try
        {
            await SendAsync(to, new AppMessage(new TextMessage(text)), tag);
            _log.LogInformation("Sent Viber message to {Phone} ('{Subject}') via Sinch.", to, subject);
            return DeliveryResult.Sent;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send Viber message to {Phone}. Falling back to outbox.", to);
            await WriteToOutboxAsync(to, subject, text, Array.Empty<byte>());
            return DeliveryResult.Failed;
        }
    }

    /// <summary>
    /// Sends the registrant's meal stubs over Viber: an intro text plus one full stub image message
    /// per stub (when a public HTTPS base is configured). Falls back to a text summary / the outbox.
    /// </summary>
    public async Task<DeliveryResult> SendMealStubsAsync(string phone, string eventTitle,
        IReadOnlyList<(string Caption, Guid Control)> stubs)
    {
        if (stubs.Count == 0) return DeliveryResult.Sent;
        var to = NormalizePhone(phone);
        var intro = $"Your {stubs.Count} meal stub{(stubs.Count == 1 ? "" : "s")} for \"{eventTitle}\" — " +
                    "present each QR at the meal counter.";
        if (TestDir is { } dir)
            return await TestSendFolder.WriteAsync(dir, _s.TestFail, to, $"meal-stubs_{eventTitle}",
                intro + "\n" + string.Join("\n", stubs.Select(s => s.Caption)));

        // The full composed stub ticket (banner + QR + denomination), not just the QR.
        var urls = stubs.Select(s => PublicUrl($"api/passes/meal-stub/{s.Control}/stub.png")).ToList();
        if (!Configured || urls.Any(u => u is null))
        {
            var summary = intro + "\n" + string.Join("\n", stubs.Select(s => $"• {s.Caption} (No. {s.Control.ToString()[..8].ToUpperInvariant()})"));
            if (!Configured) return await OffAsync(to, $"meal-stubs_{eventTitle}", summary, Array.Empty<byte>());
            await WriteToOutboxAsync(to, $"meal-stubs_{eventTitle}", summary, Array.Empty<byte>());
            _log.LogWarning("Viber meal stubs for {Phone}: no public image address; wrote summary to outbox.", to);
            return DeliveryResult.Failed;
        }

        try
        {
            await SendAsync(to, new AppMessage(new TextMessage(intro)));
            foreach (var url in urls)
                await SendAsync(to, new AppMessage(new MediaMessage { Url = url! }));
            _log.LogInformation("Sent {Count} Viber meal stub image(s) to {Phone}.", stubs.Count, to);
            return DeliveryResult.Sent;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send Viber meal stubs to {Phone}. Falling back to outbox.", to);
            var summary = intro + "\n" + string.Join("\n", stubs.Select(s => $"• {s.Caption}"));
            await WriteToOutboxAsync(to, $"meal-stubs_{eventTitle}", summary, Array.Empty<byte>());
            return DeliveryResult.Failed;
        }
    }

    // One message to one phone on the Viber Business Messages channel. Throws SinchApiException
    // when Sinch rejects the request. An accepted message is saved for its delivery reports.
    private async Task SendAsync(string to, AppMessage message, MobileTag? tag = null)
    {
        var resp = await _client.Value.Conversation.Messages.Send(new SendMessageRequest
        {
            AppId = _s.AppId,
            Message = message,
            MessageMetadata = tag is null ? null : $"reg:{tag.RegistrationId};kind:{tag.Kind}",
            Recipient = new Identified
            {
                IdentifiedBy = new IdentifiedBy
                {
                    ChannelIdentities = [new ChannelIdentity { Channel = ConversationChannel.ViberBm, Identity = to }],
                },
            },
        });
        _log.LogInformation("Sinch accepted Viber message {MessageId} for {PhoneMasked} (registration {RegistrationId}, {Kind}, tagged {Tagged})",
            resp.MessageId, PhoneNumber.Mask(to), tag?.RegistrationId, tag?.Kind, tag is not null);
        await RecordAsync(resp.MessageId, to, tag);
    }

    // The message is already accepted: a save failure is logged, never turned into a send failure.
    private async Task RecordAsync(string? messageId, string to, MobileTag? tag)
    {
        if (_scopes is null || string.IsNullOrEmpty(messageId)) return;
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ViberMessages.Add(new ViberMessage
            {
                MessageId = messageId, Phone = to, RegistrationId = tag?.RegistrationId, Kind = tag?.Kind,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not save Viber message {MessageId}; its delivery reports will be ignored.", messageId);
        }
    }

    // Absolute HTTPS URL Sinch fetches for an image message, or null when no publicly reachable
    // HTTPS base is configured (e.g. localhost in dev) — the caller then sends text only.
    private string? PublicUrl(string path)
    {
        var baseUrl = _s.PublicApiBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps) return null;
        var host = uri.Host;
        if (host is "localhost" or "127.0.0.1" or "0.0.0.0" || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return null;
        return $"{baseUrl.TrimEnd('/')}/{path}";
    }

    // Viber is off: Development writes the message to outbox/ and counts it as sent; anywhere else
    // nothing is written and the caller moves on to the next channel.
    private async Task<DeliveryResult> OffAsync(string to, string title, string text, byte[] qr)
    {
        if (!_env.IsDevelopment())
        {
            _log.LogWarning("Viber is off; message for {Phone} ('{Title}') was not sent.", to, title);
            return DeliveryResult.NotConfigured;
        }
        await WriteToOutboxAsync(to, title, text, qr);
        _log.LogWarning("Viber/Sinch not configured. Wrote message for {Phone} ('{Title}') to outbox.", to, title);
        return DeliveryResult.Disabled;
    }

    // Keep a leading '+' and digits; strip spaces, dashes, and parentheses.
    public static string NormalizePhone(string phone)
    {
        var trimmed = phone.Trim();
        var plus = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return plus ? "+" + digits : digits;
    }

    private async Task WriteToOutboxAsync(string phone, string title, string text, byte[] qr)
    {
        Directory.CreateDirectory(_outboxDir);
        var safe = string.Join("_", $"viber_{title}".Split(Path.GetInvalidFileNameChars()));
        var baseName = $"{DateTime.UtcNow.Ticks}_{safe}";
        var qrNote = "";
        if (qr.Length > 0)
        {
            await File.WriteAllBytesAsync(Path.Combine(_outboxDir, $"{baseName}.qr.png"), qr);
            qrNote = $"\n\n[QR pass: {baseName}.qr.png]";
        }
        var body = $"To (Viber): {phone}\nRe: {title}\n\n{text}{qrNote}\n";
        await File.WriteAllTextAsync(Path.Combine(_outboxDir, $"{baseName}.txt"), body);
    }
}
