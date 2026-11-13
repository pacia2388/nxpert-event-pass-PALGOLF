namespace EventRegistration.Api.Services;

/// <summary>
/// Tests only: a channel with <c>TestSendDir</c> set (Email / Viber / Sms config) "sends" by writing
/// each message to that folder and counts it as sent; with <c>TestFail</c> it fails every send so
/// fallback can be seen. Ignored in Production, so a stray setting can never divert real mail.
/// </summary>
public static class TestSendFolder
{
    public static string? Resolve(string? dir, IWebHostEnvironment env) =>
        string.IsNullOrWhiteSpace(dir) || env.IsProduction() ? null : dir;

    // One text file per message: "<time>_<recipient>_<subject>.txt" (+ ".png" when there is an image).
    public static async Task<DeliveryResult> WriteAsync(string dir, bool fail, string to, string subject,
        string body, byte[]? image = null)
    {
        if (fail) return DeliveryResult.Failed;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FileName(to, subject));
        await File.WriteAllTextAsync(path + ".txt", $"To: {to}\nSubject: {subject}\n\n{body}\n");
        if (image is { Length: > 0 }) await File.WriteAllBytesAsync(path + ".png", image);
        return DeliveryResult.Sent;
    }

    public static string FileName(string to, string subject)
    {
        var name = $"{DateTime.UtcNow:yyyyMMddHHmmssfffffff}_{to}_{subject}";
        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return name.Length > 150 ? name[..150] : name;
    }
}
