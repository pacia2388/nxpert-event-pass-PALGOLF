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
/// and a failure of a tagged one (<see cref="MobileTag"/>) queues the email once for its group.
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
    /// Sends a message's Viber part(s) as one group. Hard rule (cost: Sinch bills each message):
    /// text only = one text message; text + one image = ONE card message (image with the text);
    /// text + several images = one card with the text and the first image (the QR), then one image
    /// message per other image. With no public HTTPS base the text-only version goes as one text.
    /// Every message carries the same tag and group, so a later failure on any of them queues the
    /// email once. A rejected message stops the group and fails the whole send. Falls back to the outbox.
    /// </summary>
    public async Task<DeliveryResult> SendAsync(string phone, OutgoingMessage message, MobileTag? tag = null)
    {
        var to = NormalizePhone(phone);

        if (TestDir is { } dir)
            return await WriteToTestFolderAsync(dir, to, message);

        if (!Configured)
            return await WriteOffAsync(to, message);

        var group = Guid.NewGuid();
        // What to email after a later failure is saved once per group, on its first message.
        var firstOfGroup = true;
        foreach (var part in message.Viber)
        {
            var imageUrls = PublicImageUrls(part);
            var text = part.Text;
            if (imageUrls.Count == 0 && part.TextWithoutImages is not null)
                text = part.TextWithoutImages;

            try
            {
                if (imageUrls.Count == 0)
                {
                    // Rule 1: text only.
                    await SendAsync(to, new AppMessage(new TextMessage(text)), tag, group, firstOfGroup);
                }
                else
                {
                    // Rules 2 and 3: the text with the first image (the QR) as one card, then each
                    // other image as its own message.
                    var card = new CardMessage
                    {
                        Description = text,
                        MediaMessage = new MediaProperties { Url = imageUrls[0] },
                    };
                    await SendAsync(to, new AppMessage(card), tag, group, firstOfGroup);
                    foreach (var url in imageUrls.Skip(1))
                        await SendAsync(to, new AppMessage(new MediaMessage { Url = url }), tag, group, false);
                }
                firstOfGroup = false;

                _log.LogInformation("Sent Viber message to {Phone} ('{Subject}') via Sinch ({Images} image(s)).",
                    to, part.Subject, imageUrls.Count);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to send Viber message to {Phone}. Falling back to outbox.", to);
                await WriteToOutboxAsync(to, part.Subject, text, Png(part));
                return DeliveryResult.Failed;
            }
        }
        return DeliveryResult.Sent;
    }

    // Absolute URLs of a part's images, or none when any of them has no public HTTPS address
    // (then the text-only version is sent).
    private List<string> PublicImageUrls(MobilePart part)
    {
        var urls = new List<string>();
        if (part.Images is null)
            return urls;
        foreach (var image in part.Images)
        {
            var url = PublicUrl(image.Path);
            if (url is null)
                return [];
            urls.Add(url);
        }
        return urls;
    }

    // Tests only: every part is written to the test folder (with the QR image), as Viber would send it.
    private async Task<DeliveryResult> WriteToTestFolderAsync(string dir, string to, OutgoingMessage message)
    {
        var result = DeliveryResult.NotConfigured;
        foreach (var part in message.Viber)
        {
            result = await TestSendFolder.WriteAsync(dir, _s.TestFail, to, part.Subject, part.Text, Png(part));
            if (!result.Delivered())
                return result;
        }
        return result;
    }

    // Viber is off: each part goes to the outbox (Development) or is not sent at all.
    private async Task<DeliveryResult> WriteOffAsync(string to, OutgoingMessage message)
    {
        var result = DeliveryResult.NotConfigured;
        foreach (var part in message.Viber)
        {
            var text = part.TextWithoutImages ?? part.Text;
            result = await OffAsync(to, part.Subject, text, Png(part));
            if (result == DeliveryResult.NotConfigured)
                return result;
        }
        return result;
    }

    // The image kept for the outbox / test folder (the QR pass), if any.
    private static byte[] Png(MobilePart part)
    {
        var first = part.Images?.FirstOrDefault();
        return first?.Png ?? [];
    }

    // One message to one phone on the Viber Business Messages channel. Throws SinchApiException
    // when Sinch rejects the request. An accepted message is saved for its delivery reports.
    private async Task SendAsync(string to, AppMessage message, MobileTag? tag, Guid group, bool firstOfGroup)
    {
        var messageId = await PostAsync(new SendMessageRequest
        {
            AppId = _s.AppId,
            Message = message,
            MessageMetadata = Metadata(tag, group),
            Recipient = new Identified
            {
                IdentifiedBy = new IdentifiedBy
                {
                    ChannelIdentities = [new ChannelIdentity { Channel = ConversationChannel.ViberBm, Identity = to }],
                },
            },
        });
        _log.LogInformation("Sinch accepted Viber message {MessageId} for {PhoneMasked} (registration {RegistrationId}, {Kind}, tagged {Tagged})",
            messageId, PhoneNumber.Mask(to), tag?.RegistrationId, tag?.Kind, tag is not null);
        await RecordAsync(messageId, to, tag, group, firstOfGroup);
    }

    // The one call to Sinch; returns the accepted message's id. Overridden by tests to stand in for Sinch.
    protected virtual async Task<string?> PostAsync(SendMessageRequest request)
    {
        var response = await _client.Value.Conversation.Messages.Send(request);
        return response.MessageId;
    }

    // Tagged messages carry their registration, kind and group, for reading Sinch's dashboard.
    private static string? Metadata(MobileTag? tag, Guid group)
    {
        if (tag is null)
            return null;
        return $"reg:{tag.RegistrationId};kind:{tag.Kind};group:{group:N}";
    }

    // The message is already accepted: a save failure is logged, never turned into a send failure.
    private async Task RecordAsync(string? messageId, string to, MobileTag? tag, Guid group, bool firstOfGroup)
    {
        if (_scopes is null || string.IsNullOrEmpty(messageId)) return;
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ViberMessages.Add(new ViberMessage
            {
                MessageId = messageId, Phone = to, RegistrationId = tag?.RegistrationId, Kind = tag?.Kind,
                GroupId = group,
                EmailSnapshotJson = firstOfGroup ? tag?.EmailSnapshotJson : null,
                EmailFile = firstOfGroup ? tag?.EmailFile : null,
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
