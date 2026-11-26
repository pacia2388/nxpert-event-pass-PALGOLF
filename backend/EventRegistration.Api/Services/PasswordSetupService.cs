using System.Security.Cryptography;
using System.Text;
using EventRegistration.Api.Data;
using Microsoft.IdentityModel.Tokens;
using EventRegistration.Api.Models;

namespace EventRegistration.Api.Services;

/// <summary>
/// Issues the single-use "set your password" link for accounts created without one (admin create,
/// CSV import, captain-added members, forgot password, admin resend). The message goes through
/// <see cref="MessageDispatcher"/> like every other registrant/receiver message.
/// </summary>
public class PasswordSetupService(AppDbContext db, MessageDispatcher dispatcher, EmailSender sender, IConfiguration config)
{
    public const int DefaultLinkHours = 72;

    /// <summary>The stored form of a link token (hex SHA-256); null when it is not a token we issue.</summary>
    public static string? HashToken(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)))
            : null;

    public static int LinkHours(IConfiguration config) =>
        Math.Clamp(config.GetValue("App:PasswordSetupLinkHours", DefaultLinkHours), 1, 24 * 30);

    /// <summary>Mints a new token (replacing any earlier one), saves it and sends the link.</summary>
    /// <param name="emailOnly">Email only, whatever the account's channel (forgot password: the
    /// visitor typed an email address, so the link goes there).</param>
    public async Task<PassDelivery> IssueAsync(User user, bool emailOnly = false)
    {
        var hours = LinkHours(config);
        // 32 random bytes in the link; only their hash is stored, so a database copy holds no usable link.
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        user.PasswordSetupTokenHash = HashToken(token);
        user.PasswordSetupTokenExpiresAt = DateTime.UtcNow.AddHours(hours);
        await db.SaveChangesAsync();

        var baseUrl = config["App:FrontendBaseUrl"]?.TrimEnd('/') ?? "http://localhost:4200";
        var url = $"{baseUrl}/set-password/{token}";
        const string subject = "Set your password";
        var html = EmailTemplates.PasswordSetup(user.FullName, url, hours);
        var message = new OutgoingMessage(EmailRetryKinds.PasswordSetup,
            MessageTemplates.PasswordSetup(user.FullName, url, hours),
            // A personal sign-in link is never copied to the Settings CC list.
            email: async () => (await sender.SendAsync([user.Email], subject, html, includeDefaultCc: false)).Result);
        var channel = emailOnly ? NotificationChannels.Email : user.NotificationChannel;
        return await dispatcher.SendAsync(new Recipient(user.Phone, channel), message);
    }
}
