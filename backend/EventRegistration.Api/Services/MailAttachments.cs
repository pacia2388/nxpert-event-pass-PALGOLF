using MimeKit;

namespace EventRegistration.Api.Services;

/// <summary>
/// Attachments of a composed email, coming as multipart parts of the compose/save request and kept
/// inside the saved .eml (no attachment table, no separate upload endpoint).
/// </summary>
public static class MailAttachments
{
    /// <summary>25 MB for the whole email: body, inline images (base64 counted) and attachments.</summary>
    public const long MaxEmailBytes = 25 * 1024 * 1024;

    /// <summary>Reads and validates the uploaded files. A refused file throws an ArgumentException.</summary>
    public static async Task<List<EmailAttachment>> ReadAsync(IEnumerable<IFormFile>? files, long alreadyUsed = 0)
    {
        var attachments = new List<EmailAttachment>();
        var total = alreadyUsed;
        foreach (var file in files ?? Enumerable.Empty<IFormFile>())
        {
            if (file.Length == 0) continue;
            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
                throw new ArgumentException("An attachment needs a file name of up to 150 characters.");
            total += file.Length;
            if (total > MaxEmailBytes)
                throw new ArgumentException("The email and its attachments must stay under 25 MB.");
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            var type = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            attachments.Add(new EmailAttachment(name, memory.ToArray(), type));
        }
        return attachments;
    }

    /// <summary>The stored email's attachments (the internal calendar part is left out).</summary>
    public static List<EmailAttachment> Of(MimeMessage message, IReadOnlySet<string>? keep = null)
    {
        var list = new List<EmailAttachment>();
        foreach (var part in message.Attachments.OfType<MimePart>())
        {
            var name = part.FileName ?? "attachment";
            if (part.ContentType.MimeType.Equals("text/calendar", StringComparison.OrdinalIgnoreCase)) continue;
            if (keep is not null && !keep.Contains(name)) continue;
            using var memory = new MemoryStream();
            part.Content.DecodeTo(memory);
            list.Add(new EmailAttachment(name, memory.ToArray(), part.ContentType.MimeType));
        }
        return list;
    }

    /// <summary>Name + size rows for the edit screen.</summary>
    public static List<MailAttachmentDto> Summaries(MimeMessage message)
    {
        var list = new List<MailAttachmentDto>();
        foreach (var part in message.Attachments.OfType<MimePart>())
        {
            if (part.ContentType.MimeType.Equals("text/calendar", StringComparison.OrdinalIgnoreCase)) continue;
            using var memory = new MemoryStream();
            part.Content.DecodeTo(memory);
            list.Add(new MailAttachmentDto(part.FileName ?? "attachment", memory.Length));
        }
        return list;
    }

    /// <summary>The saved message's own size; the request is refused past <see cref="MaxEmailBytes"/>.</summary>
    public static async Task EnsureUnderLimitAsync(MimeMessage message)
    {
        using var buffer = new MemoryStream();
        await message.WriteToAsync(buffer);
        if (buffer.Length > MaxEmailBytes)
            throw new ArgumentException("The email and its attachments must stay under 25 MB.");
    }
}

public record MailAttachmentDto(string Name, long Size);

/// <summary>
/// Tolerant reader for the small JSON address lists stored on mail rows. A row saved by an older
/// build, or with a blank value, reads as an empty list instead of breaking the send or the page.
/// </summary>
public static class MailJson
{
    public static List<string> List(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }
}
