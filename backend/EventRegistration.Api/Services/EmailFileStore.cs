using MimeKit;

namespace EventRegistration.Api.Services;

/// <summary>
/// Emails kept for a later send are saved as .eml files here; the database stores only the file
/// name. Two instances:
/// <list type="bullet">
/// <item><c>mail-queue/</c> (<c>App:MailQueueDir</c>): the background email retry and late Viber
/// failure copies. Old files are removed after 26 h.</item>
/// <item><c>mail-outgoing/</c> (<c>App:MailOutgoingDir</c>): the whole composed invitation / mail
/// plan email with its attachments, kept until it is delivered. Never cleaned by age.</item>
/// </list>
/// Both hold personal data: never serve them, and keep them across deploys (no <c>robocopy /MIR</c>).
/// </summary>
public class EmailFileStore
{
    // After this no retry can need a file: Viber failure reports older than 24 h are ignored, and a
    // retry gives up after 5 tries 5 minutes apart.
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(26);

    private readonly string _dir;

    public EmailFileStore(IConfiguration config, IWebHostEnvironment env)
        : this(Resolve(config, env, "App:MailQueueDir", "mail-queue")) { }

    private EmailFileStore(string dir) => _dir = dir;

    /// <summary>The outgoing-mail store (composed emails with their attachments).</summary>
    public static EmailFileStore Outgoing(IConfiguration config, IWebHostEnvironment env) =>
        new(Resolve(config, env, "App:MailOutgoingDir", "mail-outgoing"));

    private static string Resolve(IConfiguration config, IWebHostEnvironment env, string key, string fallback)
    {
        var configured = config[key];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, fallback)
            : Path.GetFullPath(configured, env.ContentRootPath);
    }

    /// <summary>Writes the whole email (headers, HTML, inline images, attachments); returns its file name.</summary>
    public async Task<string> SaveAsync(MimeMessage message)
    {
        Directory.CreateDirectory(_dir);
        var name = $"{Guid.NewGuid():N}.eml";
        await message.WriteToAsync(Path.Combine(_dir, name));
        return name;
    }

    /// <summary>The saved email, or null when the name is not valid or the file is gone.</summary>
    public async Task<MimeMessage?> LoadAsync(string? name)
    {
        var path = PathOf(name);
        if (path is null || !File.Exists(path))
            return null;
        return await MimeMessage.LoadAsync(path);
    }

    /// <summary>The saved email's size in bytes, or 0 when it is missing.</summary>
    public long SizeOf(string? name)
    {
        var path = PathOf(name);
        return path is not null && File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    public void Delete(string? name)
    {
        var path = PathOf(name);
        if (path is not null && File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// The full path of a stored file. Only a bare "<c>name.eml</c>" is accepted, so a value read
    /// from the database can never point outside the folder.
    /// </summary>
    public string? PathOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (Path.GetFileName(name) != name || !name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
            return null;
        return Path.Combine(_dir, name);
    }

    /// <summary>Deletes files older than <see cref="KeepFor"/> that no pending retry uses (queue store only).</summary>
    public void DeleteOld(IReadOnlySet<string> inUse)
    {
        if (!Directory.Exists(_dir))
            return;
        var cutoff = DateTime.UtcNow - KeepFor;
        foreach (var path in Directory.GetFiles(_dir, "*.eml"))
        {
            if (inUse.Contains(Path.GetFileName(path)))
                continue;
            if (File.GetLastWriteTimeUtc(path) < cutoff)
                File.Delete(path);
        }
    }
}
